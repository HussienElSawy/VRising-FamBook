using HarmonyLib;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace FamBook.Patches;

/// <summary>
/// Intercepts incoming System messages from the server BEFORE the ClientChatSystem
/// processes them (HarmonyPrefix — same pattern as Eclipse).
///
/// ── Key design note ──────────────────────────────────────────────────────────
///
///   [HarmonyPrefix] runs BEFORE ClientChatSystem.OnUpdate consumes the entities
///   in _ReceiveChatMessagesQuery.  Using Postfix means the system has already
///   processed/cleared the entities by the time our code runs — leaving nothing.
///
///   When AwaitingResponse is true (set by CommandSender after ".fam l" is sent),
///   every incoming System message is logged and offered to DataService for
///   parsing.  Matched messages are destroyed so they never appear in chat.
///
///   DataService.FinalizeIfExpired() (called from CanvasService.UpdateLoop)
///   commits collected entries and signals IsDirty=true once the window closes.
/// </summary>
[HarmonyPatch]
internal static class ClientChatSystemPatch
{
    static EntityManager EntityManager => Core.EntityManager;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
    static void OnUpdate_Prefix(ClientChatSystem __instance)
    {
        if (!Core.HasInitialized) return;
        if (!Services.DataService.AwaitingResponse && !Services.DataService.AwaitingBindAttempt && !Services.DataService.AwaitingListBoxes && !Services.DataService.AwaitingSearch) return;

        NativeArray<Entity> entities =
            __instance._ReceiveChatMessagesQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (Entity entity in entities)
            {
                if (!entity.Has<ChatMessageServerEvent>()) continue;

                var chatEvent = entity.Read<ChatMessageServerEvent>();

                // Bloodcraft sends familiar list as System messages
                if (!chatEvent.MessageType.Equals(ServerChatMessageType.System)) continue;

                string message = chatEvent.MessageText.Value;
                if (string.IsNullOrEmpty(message)) continue;

                // Log every System message while awaiting so we can verify format in BepInEx log
                Core.Log.LogInfo($"[FamBook][intercept] {message}");

                try
                {
                    if (Services.DataService.TryParseBloodcraftLine(message))
                        EntityManager.DestroyEntity(entity);
                }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[FamBook] Error parsing message: {ex.Message}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}

