using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace FamBook.Utilities;

/// <summary>
/// Sends chat commands to the server by creating a <see cref="ChatMessageEvent"/>
/// network entity — identical pattern to Eclipse's Quips.cs.
///
/// Usage:
///   CommandSender.Send(".fam cb box1");
///   CommandSender.Send(".fam l");
/// </summary>
internal static class CommandSender
{
    static EntityManager EntityManager => Core.EntityManager;
    static Entity LocalCharacter => Core.LocalCharacter;
    static Entity LocalUser     => Core.LocalUser;

    static FromCharacter? _fromCharacter;

    static FromCharacter FromCharacter =>
        _fromCharacter ??= new FromCharacter
        {
            Character = LocalCharacter,
            User      = LocalUser
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
        EventId      = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };

    /// <summary>Sends <paramref name="command"/> as a Local chat message to the server.</summary>
    public static void Send(string command)
    {
        if (!Core.HasInitialized) return;
        if (LocalCharacter == Unity.Entities.Entity.Null || LocalUser == Unity.Entities.Entity.Null) return;

        ChatMessageEvent chatMessage = new()
        {
            MessageText    = new FixedString512Bytes(command),
            MessageType    = ChatMessageType.Local,
            ReceiverEntity = LocalUser.GetNetworkId()
        };

        Entity networkEvent = EntityManager.CreateEntity(_componentTypes);
        EntityManager.SetComponentData(networkEvent, FromCharacter);
        EntityManager.SetComponentData(networkEvent, _networkEventType);
        EntityManager.SetComponentData(networkEvent, chatMessage);

        Core.Log.LogInfo($"[FamBook] Sent command: {command}");
    }

    /// <summary>
    /// Sends two commands in sequence: switch to the given box, then list its familiars.
    /// The server (Bloodcraft) will respond with familiar data for that box.
    /// </summary>
    public static void RequestBoxData(int boxIndex)
    {
        // boxIndex is 0-based internally; server uses 1-based box names
        Send($".fam cb box{boxIndex + 1}");
        Send(".fam l");
        // Begin intercepting the incoming System messages so they don't show in chat
        Services.DataService.BeginAwaitingResponse();
    }

    /// <summary>Clears the cached FromCharacter so it is rebuilt on next use (call on reset).</summary>
    public static void Reset() => _fromCharacter = null;
}
