using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FamBook.Services;
using ProjectM;
using ProjectM.Hybrid;
using ProjectM.Network;
using ProjectM.Physics;
using ProjectM.Scripting;
using ProjectM.UI;
using Stunlock.Core;
using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace FamBook;

internal static class Core
{
    static World? _client;
    static SystemService? _systemService;
    static Entity _localCharacter = Entity.Null;
    static Entity _localUser = Entity.Null;

    public static Entity LocalCharacter =>
        _localCharacter.Exists()
        ? _localCharacter
        : (ConsoleShared.TryGetLocalCharacterInCurrentWorld(out _localCharacter, _client!)
            ? _localCharacter
            : Entity.Null);

    public static Entity LocalUser =>
        _localUser.Exists()
        ? _localUser
        : (ConsoleShared.TryGetLocalUserInCurrentWorld(out _localUser, _client!)
            ? _localUser
            : Entity.Null);

    public static EntityManager EntityManager => _client!.EntityManager;
    public static SystemService SystemService => _systemService ??= new(_client!);
    public static ClientGameManager ClientGameManager => SystemService.ClientScriptMapper._ClientGameManager;
    public static CanvasService? CanvasService { get; set; }
    public static ServerTime ServerTime => ClientGameManager.ServerTime;
    public static ManualLogSource Log => Plugin.LogInstance;

    static MonoBehaviour? _monoBehaviour;

    public static bool HasInitialized => _initialized;
    static bool _initialized;

    public static void Initialize(GameDataManager gameDataManager)
    {
        if (_initialized) return;

        _client = gameDataManager.World;
        _initialized = true;

        Log.LogInfo($"[FamBook] Core initialized.");
    }

    public static void Reset()
    {
        _client = null;
        _systemService = null;
        CanvasService = null;
        _initialized = false;
        _localCharacter = Entity.Null;
        _localUser = Entity.Null;

        Log.LogInfo($"[FamBook] Core reset.");
    }

    public static void SetCanvas(UICanvasBase canvas)
    {
        CanvasService = new CanvasService(canvas);
        Log.LogInfo($"[FamBook] Canvas service initialized.");
    }

    public static Coroutine StartCoroutine(IEnumerator routine)
    {
        if (_monoBehaviour == null)
        {
            var go = new GameObject(MyPluginInfo.PLUGIN_NAME);
            _monoBehaviour = go.AddComponent<IgnorePhysicsDebugSystem>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        return _monoBehaviour.StartCoroutine(routine.WrapToIl2Cpp());
    }

    public static void StopCoroutine(Coroutine routine)
    {
        if (_monoBehaviour == null) return;
        _monoBehaviour.StopCoroutine(routine);
    }
}
