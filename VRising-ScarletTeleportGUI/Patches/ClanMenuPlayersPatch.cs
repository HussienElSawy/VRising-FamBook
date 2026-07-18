using HarmonyLib;
using ProjectM.UI;
using ScarletTeleportGUI.Services;
namespace ScarletTeleportGUI.Patches;

[HarmonyPatch]
internal static class ClanMenuPlayersPatch
{
    // Social menu callback: capture the online roster even when the local player is not in a clan.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ClanMenu), nameof(ClanMenu._StunShared_UI_IInitializeableUI_InitializeUI_b__137_3))]
    static void ClanMenuSocialEntry_Postfix(ClanMenu_MemberEntry entry, ClanMenu_MemberEntry.Data data)
    {
        if (!Core.HasInitialized) return;

        try
        {
            Core.Log.LogInfo($"[ScarletTeleportGUI] Social menu entry: name='{data.Name}' online={data.IsOnline}");
            CanvasService.RegisterSocialMenuMember(data.Name);
        }
        catch
        {
            // Keep patch non-fatal on signature/runtime drift.
        }
    }
}
