using System.Text.RegularExpressions;
using UnityEngine;

namespace ScarletTeleportGUI.Services;

internal static class TeleportDataService
{
    enum TeleportSection
    {
        None,
        Public,
        Private,
        Player,
        Waypoints
    }

    public static readonly List<string> PublicTeleports = new();
    public static readonly List<string> PrivateTeleports = new();
    public static readonly List<string> PlayerTeleports = new();
    public static readonly List<string> Waypoints = new();

    public static bool AwaitingTeleportList { get; private set; }
    public static bool IsDirty { get; set; }

    public static IReadOnlyList<string> VisiblePublicTeleports =>
        AwaitingTeleportList ? _pendingPublicTeleports : PublicTeleports;

    public static IReadOnlyList<string> VisiblePrivateTeleports =>
        AwaitingTeleportList ? _pendingPrivateTeleports : PrivateTeleports;

    public static IReadOnlyList<string> VisiblePlayerTeleports =>
        AwaitingTeleportList ? _pendingPlayerTeleports : PlayerTeleports;

    public static IReadOnlyList<string> VisibleWaypoints =>
        AwaitingTeleportList ? _pendingWaypoints : Waypoints;

    static readonly List<string> _pendingPublicTeleports = new();
    static readonly List<string> _pendingPrivateTeleports = new();
    static readonly List<string> _pendingPlayerTeleports = new();
    static readonly List<string> _pendingWaypoints = new();

    static float _responseDeadline;
    static bool _capturing;
    static TeleportSection _currentSection;

    const float BASE_WINDOW = 3.0f;
    const float EXTEND_EACH = 1.5f;

    static readonly Regex _leadingListDecoratorsRx = new(
        @"^\s*(?:[-*•]+|ΓÇó+|\d+[.)]|[A-Za-z][.)])\s*",
        RegexOptions.Compiled);

    public static void BeginAwaitingTeleportList()
    {
        _pendingPublicTeleports.Clear();
        _pendingPrivateTeleports.Clear();
        _pendingPlayerTeleports.Clear();
        _pendingWaypoints.Clear();
        _currentSection = TeleportSection.None;
        _capturing = false;
        _responseDeadline = Time.realtimeSinceStartup + BASE_WINDOW;
        AwaitingTeleportList = true;
        IsDirty = true;
        Core.Log.LogInfo("[ScarletTeleportGUI] Awaiting .stp ltp response...");
    }

    public static void FinalizeIfExpired()
    {
        if (!AwaitingTeleportList) return;
        if (Time.realtimeSinceStartup < _responseDeadline) return;

        PublicTeleports.Clear();
        PublicTeleports.AddRange(_pendingPublicTeleports);

        PrivateTeleports.Clear();
        PrivateTeleports.AddRange(_pendingPrivateTeleports);

        PlayerTeleports.Clear();
        PlayerTeleports.AddRange(_pendingPlayerTeleports);

        Waypoints.Clear();
        Waypoints.AddRange(_pendingWaypoints);

        _pendingPublicTeleports.Clear();
        _pendingPrivateTeleports.Clear();
        _pendingPlayerTeleports.Clear();
        _pendingWaypoints.Clear();
        _currentSection = TeleportSection.None;
        _capturing = false;
        AwaitingTeleportList = false;
        IsDirty = true;

        Core.Log.LogInfo($"[ScarletTeleportGUI] Finalized teleports: private={PrivateTeleports.Count}, public={PublicTeleports.Count}, player={PlayerTeleports.Count}, waypoints={Waypoints.Count}");
    }

    public static bool TryParseTeleportLine(string raw)
    {
        if (!AwaitingTeleportList) return false;
        if (Time.realtimeSinceStartup > _responseDeadline) return false;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string stripped = StripTags(raw);
        if (string.IsNullOrWhiteSpace(stripped)) return false;

        string normalized = NormalizeWhitespace(stripped);
        normalized = NormalizeBulletArtifacts(normalized);

        if (!_capturing && !LooksLikeTeleportListStart(normalized))
            return false;

        bool consumed = false;

        consumed |= TryConsumeSectionHeader(normalized);
        consumed |= TryConsumeSectionLine(normalized);

        if (consumed)
        {
            _responseDeadline = Time.realtimeSinceStartup + EXTEND_EACH;
            return true;
        }

        return _capturing && LooksLikeTeleportContinuation(normalized);
    }

    public static void CancelAwaiting()
    {
        AwaitingTeleportList = false;
        _capturing = false;
        _currentSection = TeleportSection.None;
        _pendingPublicTeleports.Clear();
        _pendingPrivateTeleports.Clear();
        _pendingPlayerTeleports.Clear();
        _pendingWaypoints.Clear();
        Core.Log.LogInfo("[ScarletTeleportGUI] Teleport interception cancelled.");
    }

    public static void Reset()
    {
        PublicTeleports.Clear();
        PrivateTeleports.Clear();
        PlayerTeleports.Clear();
        Waypoints.Clear();
        _pendingPublicTeleports.Clear();
        _pendingPrivateTeleports.Clear();
        _pendingPlayerTeleports.Clear();
        _pendingWaypoints.Clear();
        _currentSection = TeleportSection.None;
        _capturing = false;
        AwaitingTeleportList = false;
        IsDirty = false;
    }

    static bool TryConsumeSectionHeader(string normalized)
    {
        if (normalized.IndexOf("Available Teleports", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _capturing = true;
            _currentSection = TeleportSection.None;
            IsDirty = true;
            return true;
        }

        if (normalized.Equals("Global", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Public", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Public Teleports", StringComparison.OrdinalIgnoreCase))
        {
            _capturing = true;
            _currentSection = TeleportSection.Public;
            IsDirty = true;
            return true;
        }

        if (normalized.Equals("Personal", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Private", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Private Teleports", StringComparison.OrdinalIgnoreCase))
        {
            _capturing = true;
            _currentSection = TeleportSection.Private;
            IsDirty = true;
            return true;
        }

        if (normalized.Equals("Player", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Player Teleports", StringComparison.OrdinalIgnoreCase))
        {
            _capturing = true;
            _currentSection = TeleportSection.Player;
            IsDirty = true;
            return true;
        }

        if (normalized.Equals("Waypoints", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Waypoint", StringComparison.OrdinalIgnoreCase))
        {
            _capturing = true;
            _currentSection = TeleportSection.Waypoints;
            IsDirty = true;
            return true;
        }

        if (normalized.IndexOf("no public teleport", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _capturing = true;
            _currentSection = TeleportSection.Public;
            IsDirty = true;
            return true;
        }

        if (normalized.IndexOf("no private teleport", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _capturing = true;
            _currentSection = TeleportSection.Private;
            IsDirty = true;
            return true;
        }

        return false;
    }

    static bool TryConsumeSectionLine(string normalized)
    {
        if (_currentSection == TeleportSection.None) return false;

        var target = _currentSection == TeleportSection.Public
            ? _pendingPublicTeleports
            : _currentSection == TeleportSection.Private
                ? _pendingPrivateTeleports
                : _currentSection == TeleportSection.Player
                    ? _pendingPlayerTeleports
                    : _pendingWaypoints;

        string? singleName = ExtractSingleTeleportName(normalized);
        if (!string.IsNullOrWhiteSpace(singleName))
        {
            AddIfMissing(target, singleName);
            IsDirty = true;
            Core.Log.LogInfo($"[ScarletTeleportGUI] Parsed teleport ({_currentSection}): {singleName}");
            return true;
        }

        var splitNames = SplitNames(normalized);
        if (splitNames.Count > 0)
        {
            AddMany(target, splitNames);
            IsDirty = true;
            Core.Log.LogInfo($"[ScarletTeleportGUI] Parsed {splitNames.Count} teleport entries for {_currentSection}.");
            return true;
        }

        return false;
    }

    static bool LooksLikeTeleportListStart(string normalized)
    {
        return normalized.IndexOf("Available Teleports", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.Equals("Personal", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Global", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Player", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Waypoints", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Private Teleports", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Public Teleports", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Player Teleports", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeTeleportContinuation(string normalized)
    {
        if (_currentSection == TeleportSection.None) return false;

        string trimmed = _leadingListDecoratorsRx.Replace(normalized, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return false;

        return IsTeleportName(trimmed);
    }

    static List<string> SplitNames(string input)
    {
        string cleaned = _leadingListDecoratorsRx.Replace(input, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return new();

        cleaned = cleaned.Trim('[', ']', '(', ')');
        if (cleaned.IndexOf("no public teleport", StringComparison.OrdinalIgnoreCase) >= 0) return new();
        if (cleaned.IndexOf("no private teleport", StringComparison.OrdinalIgnoreCase) >= 0) return new();

        var results = new List<string>();
        foreach (string piece in Regex.Split(cleaned, @"\s*(?:,|\||;)\s*"))
        {
            string candidate = CleanName(piece);
            if (IsTeleportName(candidate))
                results.Add(candidate);
        }

        if (results.Count == 0)
        {
            string single = CleanName(cleaned);
            if (IsTeleportName(single))
                results.Add(single);
        }

        return results;
    }

    static string? ExtractSingleTeleportName(string normalized)
    {
        string cleaned = _leadingListDecoratorsRx.Replace(normalized, string.Empty).Trim();
        cleaned = CleanName(cleaned);
        return IsTeleportName(cleaned) ? cleaned : null;
    }

    static string CleanName(string input)
    {
        string cleaned = NormalizeWhitespace(_leadingListDecoratorsRx.Replace(input, string.Empty));
        cleaned = cleaned.Trim(':', '-', '•', '*', '"', '\'', ' ');
        cleaned = cleaned.Replace("ΓÇó", string.Empty).Trim();
        return cleaned;
    }

    static bool IsTeleportName(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (candidate.IndexOf("public teleport", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (candidate.IndexOf("private teleport", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (candidate.IndexOf("player teleport", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (candidate.IndexOf("available teleports", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (candidate.IndexOf("waypoint", StringComparison.OrdinalIgnoreCase) >= 0 && candidate.Equals("Waypoints", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("global", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("personal", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("player", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("public", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("private", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("waypoints", StringComparison.OrdinalIgnoreCase)) return false;
        if (candidate.Equals("waypoint", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    static void AddMany(List<string> target, List<string> values)
    {
        foreach (string value in values)
        {
            AddIfMissing(target, value);
        }
    }

    static void AddIfMissing(List<string> target, string value)
    {
        if (!target.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
            target.Add(value);
    }

    static string StripTags(string value)
        => Regex.Replace(value, "<.*?>", string.Empty).Trim();

    static string NormalizeWhitespace(string value)
        => Regex.Replace(value, @"\s+", " ").Trim();

    static string NormalizeBulletArtifacts(string value)
        => value.Replace("ΓÇó", "•").Trim();
}