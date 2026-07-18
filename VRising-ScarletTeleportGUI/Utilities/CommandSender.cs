using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace ScarletTeleportGUI.Utilities;

internal static class CommandSender
{
    static EntityManager EntityManager => Core.EntityManager;
    static Entity LocalCharacter => Core.LocalCharacter;
    static Entity LocalUser => Core.LocalUser;

    static FromCharacter? _fromCharacter;

    static FromCharacter FromCharacter =>
        _fromCharacter ??= new FromCharacter
        {
            Character = LocalCharacter,
            User = LocalUser
        };

    static readonly ComponentType[] _componentTypes =
    [
        ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
        ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
        ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
        ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>())
    ];

    static readonly NetworkEventType _networkEventType = new()
    {
        IsAdminEvent = false,
        EventId = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };

    public static void Send(string command)
    {
        if (!Core.HasInitialized) return;
        if (LocalCharacter == Entity.Null || LocalUser == Entity.Null) return;

        ChatMessageEvent chatMessage = new()
        {
            MessageText = new FixedString512Bytes(command),
            MessageType = ChatMessageType.Local,
            ReceiverEntity = LocalUser.GetNetworkId()
        };

        Entity networkEvent = EntityManager.CreateEntity(_componentTypes);
        EntityManager.SetComponentData(networkEvent, FromCharacter);
        EntityManager.SetComponentData(networkEvent, _networkEventType);
        EntityManager.SetComponentData(networkEvent, chatMessage);

        Core.Log.LogInfo($"[ScarletTeleportGUI] Sent command: {command}");
    }

    public static void Reset() => _fromCharacter = null;
}