using HarmonyLib;
using ProjectM;
using ProjectM.UI;

namespace ScarletTeleportGUI.Patches;

[HarmonyPatch]
internal static class InitializationPatches
{
    static bool _setCanvas;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.OnUpdate))]
    static void GameDataManager_OnUpdate_Postfix(GameDataManager __instance)
    {
        if (Core.HasInitialized) return;
        if (!__instance.GameDataInitialized || !__instance.World.IsCreated) return;

        try
        {
            Core.Initialize(__instance);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[ScarletTeleportGUI] GameDataManager init failed: {ex}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UICanvasSystem), nameof(UICanvasSystem.UpdateHideIfDisabled))]
    static void UICanvasSystem_UpdateHideIfDisabled_Postfix(UICanvasBase canvas)
    {
        if (_setCanvas || !Core.HasInitialized) return;
        if (Core.CanvasService != null) return;

        try
        {
            _setCanvas = true;
            Core.SetCanvas(canvas);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[ScarletTeleportGUI] Canvas initialization failed: {ex}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ClientBootstrapSystem), nameof(ClientBootstrapSystem.OnDestroy))]
    static void ClientBootstrapSystem_OnDestroy_Prefix()
    {
        try
        {
            _setCanvas = false;
            Services.CanvasService.ResetState();
            Core.Reset();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[ScarletTeleportGUI] Reset failed: {ex}");
        }
    }
}