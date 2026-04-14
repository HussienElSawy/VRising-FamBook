using Il2CppInterop.Runtime;
using ProjectM.Network;
using ProjectM.Scripting;
using Unity.Entities;

namespace FamBook;

/// <summary>
/// Extension methods for Unity ECS Entity — provides Has, Read, Write,
/// Exists, GetNetworkId helpers identical in purpose to Eclipse's VExtensions.
/// </summary>
internal static class VExtensions
{
    static EntityManager EntityManager => Core.EntityManager;

    // ── Component checks ──────────────────────────────────────────────────────

    public static bool Has<T>(this Entity entity)
        => EntityManager.HasComponent<T>(entity);

    public static bool Exists(this Entity entity)
        => entity != Entity.Null && EntityManager.Exists(entity);

    // ── Component data read / write ───────────────────────────────────────────

    public static T Read<T>(this Entity entity) where T : struct
        => EntityManager.TryGetComponentData<T>(entity, out T data) ? data : default;

    public static void Write<T>(this Entity entity, T componentData) where T : struct
    {
        if (!entity.Has<T>()) return;
        EntityManager.SetComponentData(entity, componentData);
    }

    // ── NetworkId helper ──────────────────────────────────────────────────────

    public static NetworkId GetNetworkId(this Entity entity)
    {
        if (EntityManager.TryGetComponentData<NetworkId>(entity, out var networkId))
            return networkId;
        return NetworkId.Empty;
    }

    // ── Coroutine helpers (matches Eclipse pattern) ───────────────────────────

    public static UnityEngine.Coroutine Run(this System.Collections.IEnumerator routine)
        => Core.StartCoroutine(routine);

    public static void Stop(this UnityEngine.Coroutine routine)
    {
        if (routine != null) Core.StopCoroutine(routine);
    }
}
