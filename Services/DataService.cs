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

    public static readonly System.Collections.Generic.Dictionary<int, BoxData> Boxes = new();

    public static bool IsDirty { get; set; } = false;

    // ── Awaiting-response state machine ───────────────────────────────────────

    public static bool AwaitingResponse { get; private set; } = false;

    static float _responseDeadline;
    static readonly System.Collections.Generic.List<FamiliarEntry> _pendingEntries = new();
    static string _pendingBoxName = string.Empty;

    // ── List boxes state ──────────────────────────────────────────────────────
    public static bool AwaitingListBoxes { get; private set; } = false;
    static readonly System.Collections.Generic.List<string> _pendingBoxNames = new();
    public static readonly System.Collections.Generic.List<string> LastListedBoxNames = new();

    // ── Bind attempt state ────────────────────────────────────────────────────

    public static bool AwaitingBindAttempt { get; private set; } = false;

    // ── Search (.fam s) state ─────────────────────────────────────────────────
    public static bool AwaitingSearch { get; private set; } = false;
    static string _pendingSearchName = string.Empty;
    public static readonly Dictionary<string, string> VbloodResults = new();

    static string NormalizeVbloodKey(string s)
    {
        if (s == null) return string.Empty;
        // collapse whitespace, trim, case-insensitive keying
        var k = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        return k.ToLowerInvariant();
    }

    public static bool TryGetVbloodResult(string vbName, out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(vbName)) return false;
        return VbloodResults.TryGetValue(NormalizeVbloodKey(vbName), out value);
    }
    public static readonly System.Collections.Generic.List<System.Collections.Generic.List<string>> VbloodPages = new();
    public static int PendingBindFamiliarNumber { get; private set; } = 0;

    const float BASE_WINDOW = 3.0f;  // seconds to wait for first message
    const float EXTEND_EACH = 1.5f;  // extend deadline per matched message

    // ── Regex patterns ────────────────────────────────────────────────────────

    // Box header:  <color=white>BoxName</color>:
    static readonly Regex _boxHeaderRx = new(
        @"^<color=white>(.+)</color>:$",
        RegexOptions.Compiled);

    // Box selected confirmation: Box Selected - <color=white>Name</color>
    // Capture any name (numeric or not). Numeric names will update CurrentBoxIndex.
    static readonly Regex _boxSelectedRx = new(
        @"^Box Selected - <color=white>(.+)</color>$",
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

    /// <summary>Begin awaiting the server's response to a .fam s search</summary>
    public static void BeginAwaitingSearch(string name)
    {
        _pendingSearchName = name;
        AwaitingSearch = true;
        _responseDeadline = Time.realtimeSinceStartup + BASE_WINDOW;
        Core.Log.LogInfo($"[FamBook] Awaiting .fam s response for '{name}'...");
    }

    /// <summary>Load VBlood pages from vblods.json (multiple fallbacks).</summary>
    public static void LoadVbloodPages()
    {
        VbloodPages.Clear();
        VbloodResults.Clear();

        string assemblyDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? System.AppDomain.CurrentDomain.BaseDirectory;
        string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        string cwd = System.IO.Directory.GetCurrentDirectory();
        string[] candidates = new string[] {
            System.IO.Path.Combine(assemblyDir, "vbloods.json"),
            System.IO.Path.Combine(assemblyDir, "Services", "vbloods.json"),
            System.IO.Path.Combine(baseDir, "vbloods.json"),
            System.IO.Path.Combine(baseDir, "Services", "vbloods.json"),
            System.IO.Path.Combine(cwd, "vbloods.json"),
            System.IO.Path.Combine(cwd, "Services", "vbloods.json"),
        };

        // Debug: log candidate locations so users can diagnose why the file wasn't found.
        Core.Log.LogInfo($"[FamBook] Searching vbloods.json candidates: assemblyDir={assemblyDir}, baseDir={baseDir}, cwd={cwd}");

        string? found = null;
        foreach (var c in candidates)
        {
            if (System.IO.File.Exists(c)) { found = c; break; }
        }

        if (found == null)
        {
            // Try embedded resource inside the DLL (Services/vbloods.json)
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            string resourceName = "FamBook.Services.vbloods.json";
            using var rsrc = asm.GetManifestResourceStream(resourceName);
            if (rsrc != null)
            {
                try
                {
                    using var reader = new System.IO.StreamReader(rsrc);
                    var txt = reader.ReadToEnd();
                    using var doc = System.Text.Json.JsonDocument.Parse(txt);
                    var root = doc.RootElement;
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var page = new System.Collections.Generic.List<string>();
                            foreach (var el in prop.Value.EnumerateArray())
                                page.Add(el.GetString() ?? string.Empty);
                            VbloodPages.Add(page);
                        }
                    }
                    Core.Log.LogInfo($"[FamBook] Loaded {VbloodPages.Count} VBlood pages from embedded resource ({resourceName}).");
                    return;
                }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[FamBook] Failed to load embedded vbloods.json: {ex.Message}");
                    return;
                }
            }

            Core.Log.LogWarning("[FamBook] vbloods.json not found; VBlood pages empty.");
            return;
        }

        try
        {
            var txt = System.IO.File.ReadAllText(found);
            using var doc = System.Text.Json.JsonDocument.Parse(txt);
            var root = doc.RootElement;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var page = new System.Collections.Generic.List<string>();
                    foreach (var el in prop.Value.EnumerateArray())
                        page.Add(el.GetString() ?? string.Empty);
                    VbloodPages.Add(page);
                }
            }

            Core.Log.LogInfo($"[FamBook] Loaded {VbloodPages.Count} VBlood pages from {found}.");
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[FamBook] Failed to load vbloods.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Try to parse one System message from Bloodcraft's ".fam l" output OR a bind error.
    /// Returns true when the message was consumed and should be silenced.
    /// </summary>
    public static bool TryParseBloodcraftLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // 1) Bind attempt handling (highest priority)
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

        // 2) List boxes handling
        if (AwaitingListBoxes)
        {
            if (Time.realtimeSinceStartup > _responseDeadline) return false;

            // Strip any rich tags
            string stripped = StripTags(raw);
            if (string.IsNullOrWhiteSpace(stripped)) return false;

            // Ignore header line
            if (stripped.StartsWith("Familiar Boxes", System.StringComparison.OrdinalIgnoreCase))
            {
                // extend deadline in case names follow
                _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
                return true;
            }

            // Ignore confirmation lines like "Box Selected - boxN"
            if (_boxSelectedRx.IsMatch(raw)) return true;

            // Ignore familiar lines (they contain '|') and box header lines ending with ':'
            if (stripped.Contains("|") || stripped.EndsWith(":"))
            {
                _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
                return true;
            }

            // If the line contains comma-separated tokens, split and add each cleaned token
            if (stripped.Contains(","))
            {
                var parts = stripped.Split(',');
                foreach (var p in parts)
                {
                    var tok = CleanBoxToken(p);
                    if (!string.IsNullOrWhiteSpace(tok) && !_pendingBoxNames.Contains(tok))
                    {
                        _pendingBoxNames.Add(tok);
                        Core.Log.LogInfo($"[FamBook] Parsed box token: {tok}");
                    }
                }

                _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
                return true;
            }

            // Single token line (likely a box name)
            var token = CleanBoxToken(stripped);
            if (!string.IsNullOrWhiteSpace(token) && !_pendingBoxNames.Contains(token))
            {
                _pendingBoxNames.Add(token);
                Core.Log.LogInfo($"[FamBook] Parsed box token: {token}");
            }

            _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
            return true;
        }

        static string CleanBoxToken(string s)
        {
            var t = s.Trim();
            // remove trailing colon or surrounding brackets
            if (t.EndsWith(":")) t = t.Substring(0, t.Length - 1).Trim();
            // strip any remaining tags or stray punctuation
            t = System.Text.RegularExpressions.Regex.Replace(t, "[\n\r\t\\\"']", string.Empty).Trim();
            return t;
        }

        // Handle search-only responses even when not awaiting a full .fam l response.
        // This allows .fam s queries to be handled without setting AwaitingResponse.
        if (AwaitingSearch)
        {
            // Example server reply: "Matching familiar(s) found in: <color=white>box12</color>"
            // Or: "No matching familiar found" / "Couldn't find matching familiar"
            string s = StripTags(raw);
            if (s.IndexOf("Matching familiar", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // extract box name after colon
                var parts = s.Split(':');
                if (parts.Length >= 2)
                {
                    string boxed = parts[1].Trim();
                    // boxed may be like "box12" or a name
                    var pending = _pendingSearchName;
                    VbloodResults[NormalizeVbloodKey(pending)] = boxed;
                    Core.Log.LogInfo($"[FamBook] Search result: {pending} -> {boxed}");
                }
                else
                {
                    var pending = _pendingSearchName;
                    VbloodResults[NormalizeVbloodKey(pending)] = "unknown";
                }

                _pendingSearchName = string.Empty;
                AwaitingSearch = false;
                IsDirty = true;
                return true;
            }

            if (s.IndexOf("couldn't find", System.StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("no matching", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var pending = _pendingSearchName;
                VbloodResults[NormalizeVbloodKey(pending)] = "not owned";
                Core.Log.LogInfo($"[FamBook] Search result: {pending} -> not owned");
                _pendingSearchName = string.Empty;
                AwaitingSearch = false;
                IsDirty = true;
                return true;
            }
        }

        // 3) Normal .fam l response handling
        if (!AwaitingResponse) return false;
        if (Time.realtimeSinceStartup > _responseDeadline) return false;

        // Silently consume Bloodcraft's "Box Selected" confirmation (.fam cb response)
        var selMatch = _boxSelectedRx.Match(raw);
        if (selMatch.Success)
        {
            string selName = selMatch.Groups[1].Value.Trim();
            // If the captured name is numeric like "box12" or just a number, try to set index.
            // Support forms like "box12" -> 12, or "12" -> 12.
            int parsed = 0;
            var m = System.Text.RegularExpressions.Regex.Match(selName, @"box(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success)
                int.TryParse(selName, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed);
            else
                parsed = ParseInt(m.Groups[1].Value);

            if (parsed > 0)
            {
                CurrentBoxIndex = parsed - 1;
                Core.Log.LogInfo($"[FamBook] Box selected: {CurrentBoxIndex + 1}");
            }
            else
            {
                // Non-numeric box name selected; record pending box name so subsequent fam lines attach correctly.
                _pendingBoxName = selName;
                Core.Log.LogInfo($"[FamBook] Box selected: {selName}");
            }

            // If a search was active, consume this as part of search results (some servers may echo selection)
            if (AwaitingSearch && _pendingSearchName.Length > 0)
            {
                var pending = _pendingSearchName;
                // record that the search resolved to this box name (use normalized key)
                VbloodResults[NormalizeVbloodKey(pending)] = selName;
                _pendingSearchName = string.Empty;
                AwaitingSearch = false;
                IsDirty = true;
                Core.Log.LogInfo($"[FamBook] Search result: {pending} -> {selName}");
            }

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

    static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty).Trim();

    /// <summary>Begin awaiting the server's response to ".fam listboxes".</summary>
    public static void BeginAwaitingBoxList()
    {
        _pendingBoxNames.Clear();
        _responseDeadline = Time.realtimeSinceStartup + BASE_WINDOW;
        AwaitingListBoxes = true;
        Core.Log.LogInfo("[FamBook] Awaiting .fam listboxes response...");
    }

    /// <summary>Finalize the captured listboxes once the deadline expires.</summary>
    public static void FinalizeListIfExpired()
    {
        if (!AwaitingListBoxes) return;
        if (Time.realtimeSinceStartup < _responseDeadline) return;

        LastListedBoxNames.Clear();
        LastListedBoxNames.AddRange(_pendingBoxNames);

        _pendingBoxNames.Clear();
        AwaitingListBoxes = false;
        IsDirty = true;

        Core.Log.LogInfo($"[FamBook] Finalised listboxes: {LastListedBoxNames.Count} boxes.");
    }

    /// <summary>Called from UpdateLoop; finalises collection once the deadline has passed.</summary>
    public static void FinalizeIfExpired()
    {
        if (!AwaitingResponse) return;
        if (Time.realtimeSinceStartup < _responseDeadline) return;

        int idx = CurrentBoxIndex;
        Boxes[idx]  = new BoxData(idx, _pendingBoxName, new System.Collections.Generic.List<FamiliarEntry>(_pendingEntries));
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

        // Clear listboxes state
        AwaitingListBoxes = false;
        _pendingBoxNames.Clear();
        LastListedBoxNames.Clear();
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
        (int val, string sym)[] map = new (int val, string sym)[] {
            (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),
            (100,"C"),(90,"XC"),(50,"L"),(40,"XL"),
            (10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")
        };
        var sb = new System.Text.StringBuilder();
        foreach (var (val, sym) in map)
            while (num >= val) { sb.Append(sym); num -= val; }
        return sb.ToString();
    }
}
