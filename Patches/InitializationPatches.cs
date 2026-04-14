using HarmonyLib;
using ProjectM;
using ProjectM.UI;

namespace FamBook.Patches;

/// <summary>
/// Patches the game's initialization sequence so FamBook can set up its
/// core services and canvas once the game world is ready.
/// </summary>
[HarmonyPatch]
internal static class InitializationPatches
{
    static bool _setCanvas;

    // ── GameDataManager patch – fires when the game world is fully loaded ─────

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
            Core.Log.LogError($"[FamBook] GameDataManager init failed: {ex}");
        }
    }

    // ── UICanvasSystem patch – fires every frame; used once to set up canvas ──
    // Eclipse uses UICanvasSystem.UpdateHideIfDisabled – the postfix receives
    // the UICanvasBase that was just processed, so we grab it on first call.

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
            Core.Log.LogError($"[FamBook] Canvas initialization failed: {ex}");
        }
    }

    // ── ClientBootstrapSystem patch – fires on disconnect / scene exit ────────

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
            Core.Log.LogError($"[FamBook] Reset failed: {ex}");
        }
    }
}
