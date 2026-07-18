using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ScarletTeleportGUI;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
internal class Plugin : BasePlugin
{
    Harmony _harmony = null!;

    internal static Plugin Instance { get; private set; } = null!;
    public static ManualLogSource LogInstance => Instance.Log;

    static ConfigEntry<bool> _teleportsPanel = null!;
    static ConfigEntry<bool> _eclipsed = null!;

    public static bool TeleportsPanel => _teleportsPanel.Value;
    public static bool Eclipsed => _eclipsed.Value;

    public override void Load()
    {
        Instance = this;

        if (!Application.productName.Equals("VRising", System.StringComparison.OrdinalIgnoreCase))
        {
            Core.Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] is a client mod - skipping on server ({Application.productName}).");
            return;
        }

        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        InitConfig();

        Core.Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] loaded on client.");
    }

    static void InitConfig()
    {
        _teleportsPanel = InitConfigEntry(
            "UIOptions", "TeleportsPanel", true,
            "Enable or disable the Scarlet Teleport GUI panel.");

        _eclipsed = InitConfigEntry(
            "UIOptions", "Eclipsed", true,
            "Set to false for slower update intervals if you want lower polling overhead.");
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