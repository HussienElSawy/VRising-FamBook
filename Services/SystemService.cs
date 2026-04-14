using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Hybrid;
using ProjectM.Scripting;
using ProjectM.UI;
using StunShared.UI;
using Unity.Entities;

namespace FamBook.Services;

internal class SystemService(World world)
{
    readonly World _world = world ?? throw new ArgumentNullException(nameof(world));

    ClientScriptMapper? _clientScriptMapper;
    public ClientScriptMapper ClientScriptMapper => _clientScriptMapper ??= GetSystem<ClientScriptMapper>();

    PrefabCollectionSystem? _prefabCollectionSystem;
    public PrefabCollectionSystem PrefabCollectionSystem => _prefabCollectionSystem ??= GetSystem<PrefabCollectionSystem>();

    GameDataSystem? _gameDataSystem;
    public GameDataSystem GameDataSystem => _gameDataSystem ??= GetSystem<GameDataSystem>();

    ManagedDataSystem? _managedDataSystem;
    public ManagedDataSystem ManagedDataSystem => _managedDataSystem ??= GetSystem<ManagedDataSystem>();

    UIDataSystem? _uiDataSystem;
    public UIDataSystem UIDataSystem => _uiDataSystem ??= GetSystem<UIDataSystem>();

    HybridModelSystem? _hybridModelSystem;
    public HybridModelSystem HybridModelSystem => _hybridModelSystem ??= GetSystem<HybridModelSystem>();

    T GetSystem<T>() where T : ComponentSystemBase
    {
        return _world.GetExistingSystemManaged<T>()
            ?? throw new InvalidOperationException($"Failed to get {Il2CppType.Of<T>().FullName} from the world.");
    }
}
