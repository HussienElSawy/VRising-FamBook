using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FamBook;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
internal class Plugin : BasePlugin
{
    Harmony _harmony = null!;

    internal static Plugin Instance { get; private set; } = null!;
    public static ManualLogSource LogInstance => Instance.Log;

    // --- Config Entries ---
    static ConfigEntry<bool> _familiarsPanel = null!;
    static ConfigEntry<bool> _showActiveIndicator = null!;
    static ConfigEntry<bool> _eclipsed = null!;

    // --- Public Accessors ---
    public static bool FamiliarsPanel => _familiarsPanel.Value;
    public static bool ShowActiveIndicator => _showActiveIndicator.Value;
    public static bool Eclipsed => _eclipsed.Value;

    public override void Load()
    {
        Instance = this;

        // FamBook is a client-only mod; skip patching on dedicated server builds
        if (!Application.productName.Equals("VRising", System.StringComparison.OrdinalIgnoreCase))
        {
            Core.Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] is a client mod – skipping on server ({Application.productName}).");
            return;
        }

        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        InitConfig();

        Core.Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] loaded on client!");
    }

    static void InitConfig()
    {
        _familiarsPanel = InitConfigEntry(
            "UIOptions", "FamiliarsPanel", true,
            "Enable/Disable the familiars panel that lists all unlocked familiars.");

        _showActiveIndicator = InitConfigEntry(
            "UIOptions", "ShowActiveIndicator", true,
            "Highlight the currently active familiar in the panel.");

        _eclipsed = InitConfigEntry(
            "UIOptions", "Eclipsed", true,
            "Set to false for slower update intervals (0.1s -> 1s) if performance is negatively impacted.");
    }

    static ConfigEntry<T> InitConfigEntry<T>(string section, string key, T defaultValue, string description)
    {
        var entry = Instance.Config.Bind(section, key, defaultValue, description);

        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.cfg"), true);
        if (configFile.TryGetEntry(section, key, out ConfigEntry<T> existingEntry))
        {
            entry.Value = existingEntry.Value;
        }

        return entry;
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        return true;
    }
}
