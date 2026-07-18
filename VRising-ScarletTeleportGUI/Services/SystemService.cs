using Il2CppInterop.Runtime;
using ProjectM.Hybrid;
using ProjectM.Scripting;
using StunShared.UI;
using Unity.Entities;

namespace ScarletTeleportGUI.Services;

internal class SystemService(World world)
{
    readonly World _world = world ?? throw new ArgumentNullException(nameof(world));

    ClientScriptMapper? _clientScriptMapper;
    public ClientScriptMapper ClientScriptMapper => _clientScriptMapper ??= GetSystem<ClientScriptMapper>();

    T GetSystem<T>() where T : ComponentSystemBase
    {
        return _world.GetExistingSystemManaged<T>()
            ?? throw new InvalidOperationException($"Failed to get {Il2CppType.Of<T>().FullName} from the world.");
    }

    public bool TryUpdateSystem(string assemblyQualifiedName)
    {
        Type? systemType = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (systemType == null)
            return false;

        return TryUpdateSystem(systemType);
    }

    public bool TryUpdateSystem(Type systemType)
    {
        if (!typeof(ComponentSystemBase).IsAssignableFrom(systemType))
            return false;

        var method = typeof(World).GetMethod(
            nameof(World.GetExistingSystemManaged),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        if (method == null || !method.IsGenericMethodDefinition)
            return false;

        try
        {
            object? system = method.MakeGenericMethod(systemType).Invoke(_world, null);
            if (system is not ComponentSystemBase componentSystem)
                return false;

            componentSystem.Update();
            return true;
        }
        catch
        {
            return false;
        }
    }
}