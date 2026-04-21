using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FamBook.Services;

/// <summary>
/// Stores familiar box data and parses System messages sent by Bloodcraft
/// in response to ".fam cb [Name]" + ".fam l" commands.
///
/// ── Actual Bloodcraft message format (.fam l) ────────────────────────────────
///
///   First message (box header):
///     <color=white>BoxName</color>:
///
///   Per-familiar messages (one per familiar in the box):
///     No shiny:  <color=yellow>N</color>| <color=green>Name</color> [<color=white>level</color>]
///     With shiny + prestige:
///                <color=yellow>N</color>| <color=green>Name</color><color=#FF69B4>*</color> [<color=white>level</color>][<color=#90EE90>prestige</color>]
///
///   Messages arrive as ServerChatMessageType.System.
///   We intercept and destroy them so they don't appear in chat.
/// </summary>
internal static class DataService
{
    // ── Box state ─────────────────────────────────────────────────────────────

    public static int    CurrentBoxIndex { get; set; }  = 0;
    public static string CurrentBoxName  { get; private set; } = string.Empty;

    public static readonly Dictionary<int, BoxData> Boxes = [];

    public static bool IsDirty { get; set; } = false;

    // ── Awaiting-response state machine ───────────────────────────────────────

    public static bool AwaitingResponse { get; private set; } = false;

    static float _responseDeadline;
    static readonly List<FamiliarEntry> _pendingEntries = [];
    static string _pendingBoxName = string.Empty;

    // ── Bind attempt state ────────────────────────────────────────────────────

    public static bool AwaitingBindAttempt { get; private set; } = false;
    public static int PendingBindFamiliarNumber { get; private set; } = 0;

    const float BASE_WINDOW = 3.0f;  // seconds to wait for first message
    const float EXTEND_EACH = 1.5f;  // extend deadline per matched message

    // ── Regex patterns ────────────────────────────────────────────────────────

    // Box header:  <color=white>BoxName</color>:
    static readonly Regex _boxHeaderRx = new(
        @"^<color=white>(.+)</color>:$",
        RegexOptions.Compiled);

    // Box selected confirmation: Box Selected - <color=white>boxN</color>
    static readonly Regex _boxSelectedRx = new(
        @"^Box Selected - <color=white>box(\d+)</color>$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Familiar line (no shiny):   <color=yellow>N</color>| <color=green>Name</color> [<color=white>level</color>][optional prestige]
    // Familiar line (with shiny): <color=yellow>N</color>| <color=green>Name</color><color=#HEX>*</color> [<color=white>level</color>][optional prestige]
    static readonly Regex _famLineRx = new(
        @"^<color=yellow>\d+</color>\| <color=green>(.+?)</color>(?:(<color=[^>]+>)\*</color>)? \[<color=white>(\d+)</color>\](?:\[<color=#90EE90>(\d+)</color>\])?$",
        RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Called by CommandSender after ".fam l" is sent to the server.</summary>
    public static void BeginAwaitingResponse()
    {
        _pendingEntries.Clear();
        _pendingBoxName   = string.Empty;
        _responseDeadline = Time.realtimeSinceStartup + BASE_WINDOW;
        AwaitingResponse  = true;
        Core.Log.LogInfo("[FamBook] Awaiting .fam l response...");
    }

    /// <summary>Called when attempting to bind a familiar; waits for error response.</summary>
    public static void BeginAwaitingBindAttempt(int familiarNumber)
    {
        PendingBindFamiliarNumber = familiarNumber;
        AwaitingBindAttempt = true;
        Core.Log.LogInfo($"[FamBook] Awaiting bind attempt response for familiar #{familiarNumber}...");
    }

    /// <summary>Handles bind retry: sends .fam ub then .fam b order again.</summary>
    public static void RetryBindWithUnbind()
    {
        if (PendingBindFamiliarNumber <= 0) return;
        
        AwaitingBindAttempt = false;
        int famNum = PendingBindFamiliarNumber;
        
        Utilities.CommandSender.Send(".fam ub");
        Utilities.CommandSender.Send($".fam b {famNum}");
        
        Core.Log.LogInfo($"[FamBook] Bind conflict detected; retrying familiar #{famNum} after unbind.");
    }

    /// <summary>Clears bind attempt state.</summary>
    public static void ClearBindAttempt()
    {
        AwaitingBindAttempt = false;
        PendingBindFamiliarNumber = 0;
    }

    /// <summary>
    /// Try to parse one System message from Bloodcraft's ".fam l" output OR a bind error.
    /// Returns true when the message was consumed and should be silenced.
    /// </summary>
    public static bool TryParseBloodcraftLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Check for bind error if awaiting bind attempt
        if (AwaitingBindAttempt)
        {
            if (raw.Contains("you already have an active familiar") || raw.Contains("Unbind that one first"))
            {
                Core.Log.LogInfo($"[FamBook] Detected bind conflict: {raw}");
                RetryBindWithUnbind();
                return true;  // Consume this error message
            }
            // If we get any other system message while awaiting bind, assume success and clear state
            ClearBindAttempt();
            return false;
        }

        if (!AwaitingResponse) return false;
        if (Time.realtimeSinceStartup > _responseDeadline) return false;

        // Silently consume Bloodcraft's "Box Selected" confirmation (.fam cb response)
        var selMatch = _boxSelectedRx.Match(raw);
        if (selMatch.Success)
        {
            int parsed = ParseInt(selMatch.Groups[1].Value);
            if (parsed > 0) CurrentBoxIndex = parsed - 1;
            Core.Log.LogInfo($"[FamBook] Box selected: {CurrentBoxIndex + 1}");
            return true;
        }

        // 1. Familiar entry line?
        var famMatch = _famLineRx.Match(raw);
        if (famMatch.Success)
        {
            string name       = famMatch.Groups[1].Value;
            string shinyColor = famMatch.Groups[2].Success ? famMatch.Groups[2].Value : string.Empty;
            int    level      = ParseInt(famMatch.Groups[3].Value);
            int    prestige   = famMatch.Groups[4].Success ? ParseInt(famMatch.Groups[4].Value) : 0;

            _pendingEntries.Add(new FamiliarEntry(name, level, prestige, shinyColor));
            _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
            Core.Log.LogInfo($"[FamBook] Parsed: {name} Lv{level} P{prestige} shiny={shinyColor.Length > 0}");
            return true;
        }

        // 2. Box header line?
        var hdrMatch = _boxHeaderRx.Match(raw);
        if (hdrMatch.Success)
        {
            _pendingBoxName   = hdrMatch.Groups[1].Value;
            _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
            Core.Log.LogInfo($"[FamBook] Box header: {_pendingBoxName}");
            return true;
        }

        return false;
    }

    /// <summary>Called from UpdateLoop; finalises collection once the deadline has passed.</summary>
    public static void FinalizeIfExpired()
    {
        if (!AwaitingResponse) return;
        if (Time.realtimeSinceStartup < _responseDeadline) return;

        int idx = CurrentBoxIndex;
        Boxes[idx]  = new BoxData(idx, _pendingBoxName, [.._pendingEntries]);
        CurrentBoxName = _pendingBoxName;

        _pendingEntries.Clear();
        _pendingBoxName  = string.Empty;
        AwaitingResponse = false;
        IsDirty          = true;

        Core.Log.LogInfo($"[FamBook] Finalised box {idx + 1} ({CurrentBoxName}): {Boxes[idx].Familiars.Count} familiars.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    public static void Reset()
    {
        Boxes.Clear();
        CurrentBoxName    = string.Empty;
        CurrentBoxIndex   = 0;
        IsDirty           = false;
        AwaitingResponse  = false;
        AwaitingBindAttempt = false;
        PendingBindFamiliarNumber = 0;
        _pendingEntries.Clear();
        _pendingBoxName   = string.Empty;
    }
}

// ── Data Models ───────────────────────────────────────────────────────────────

/// <summary>Data for a single familiar box (page).</summary>
internal sealed class BoxData(int boxIndex, string boxName, List<FamiliarEntry> familiars)
{
    public int                 BoxIndex  { get; } = boxIndex;
    public string              BoxName   { get; } = boxName;
    public List<FamiliarEntry> Familiars { get; } = familiars;

    /// <summary>Display title: server's box name if known, otherwise "Box N".</summary>
    public string Title => string.IsNullOrEmpty(BoxName) ? $"Box {BoxIndex + 1}" : BoxName;
}

/// <summary>One familiar parsed from ".fam l" output.</summary>
internal sealed class FamiliarEntry(string name, int level, int prestige, string shinyColorTag)
{
    public string Name          { get; } = name;
    public int    Level         { get; } = level;
    public int    Prestige      { get; } = prestige;

    /// <summary>
    /// Non-empty when the familiar has a shiny buff.
    /// Contains the opening color tag, e.g. &lt;color=#FF69B4&gt;.
    /// </summary>
    public string ShinyColorTag { get; } = shinyColorTag;
    public bool   IsShiny       => ShinyColorTag.Length > 0;

    /// <summary>Roman-numeral prestige label; empty when prestige is 0.</summary>
    public string PrestigeLabel => Prestige > 0 ? ToRoman(Prestige) : string.Empty;

    static string ToRoman(int num)
    {
        (int val, string sym)[] map =
        [
            (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),
            (100,"C"),(90,"XC"),(50,"L"),(40,"XL"),
            (10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")
        ];
        var sb = new System.Text.StringBuilder();
        foreach (var (val, sym) in map)
            while (num >= val) { sb.Append(sym); num -= val; }
        return sb.ToString();
    }
}
