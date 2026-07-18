using HarmonyLib;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace ScarletTeleportGUI.Patches;

[HarmonyPatch]
internal static class ClientChatSystemPatch
{
    static EntityManager EntityManager => Core.EntityManager;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
    static void OnUpdate_Prefix(ClientChatSystem __instance)
    {
        if (!Core.HasInitialized) return;
        if (!Services.CanvasService.IsOpen) return;
        if (!Services.TeleportDataService.AwaitingTeleportList) return;

        NativeArray<Entity> entities =
            __instance._ReceiveChatMessagesQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (Entity entity in entities)
            {
                if (!entity.Has<ChatMessageServerEvent>()) continue;

                var chatEvent = entity.Read<ChatMessageServerEvent>();
                if (!chatEvent.MessageType.Equals(ServerChatMessageType.System)) continue;

                string message = chatEvent.MessageText.Value;
                if (string.IsNullOrWhiteSpace(message)) continue;

                Core.Log.LogInfo($"[ScarletTeleportGUI][intercept] {message}");

                try
                {
                    if (Services.TeleportDataService.TryParseTeleportLine(message))
                        EntityManager.DestroyEntity(entity);
                }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[ScarletTeleportGUI] Error parsing message: {ex.Message}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}