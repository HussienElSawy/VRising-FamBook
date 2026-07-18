using ProjectM.Network;
using Unity.Entities;

namespace ScarletTeleportGUI;

internal static class VExtensions
{
    static EntityManager EntityManager => Core.EntityManager;

    public static bool Has<T>(this Entity entity)
        => EntityManager.HasComponent<T>(entity);

    public static bool Exists(this Entity entity)
        => entity != Entity.Null && EntityManager.Exists(entity);

    public static T Read<T>(this Entity entity) where T : struct
        => EntityManager.TryGetComponentData<T>(entity, out T data) ? data : default;

    public static NetworkId GetNetworkId(this Entity entity)
    {
        if (EntityManager.TryGetComponentData<NetworkId>(entity, out var networkId))
            return networkId;
        return NetworkId.Empty;
    }
}