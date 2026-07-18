using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.Sequencer;
using System.Reflection;

namespace ScarletTeleportGUI.Patches;

[HarmonyPatch]
internal static class GameplayInputSuppressionPatch
{
    [HarmonyTargetMethods]
    static IEnumerable<MethodBase> TargetMethods()
    {
        return GetExistingTargets();
    }

    [HarmonyPrefix]
    static bool Prefix(MethodBase __originalMethod)
    {
        bool suppressGameplay = Services.CanvasService.SuppressGameplayInput;
        bool blockMapInput = Services.CanvasService.BlockMapInput;

        if (!suppressGameplay && !blockMapInput)
            return true;

        if (blockMapInput)
        {
            string? typeName = __originalMethod.DeclaringType?.FullName;
            if (!string.IsNullOrWhiteSpace(typeName)
                && typeName.IndexOf("HandleOpenMap", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        return !suppressGameplay;
    }

    static IEnumerable<MethodBase> GetExistingTargets()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var candidates = new (Type? type, string[] methods)[]
        {
            // Suppress keyboard/game hotkeys only while the player textbox is focused.
            (typeof(GameplayInputSystem), new[] { "OnUpdate" }),
            (typeof(InputActionSystem), new[] { "OnUpdate", "UpdateInputDisabled", "TryUpdateCurrentGamepadType", "UpdateControlScheme" }),
            (typeof(SendUserInputSystem), new[] { "OnUpdate" }),
            (typeof(Pull_InputSystem), new[] { "OnUpdate" }),
            (typeof(AbilityInputSystem), new[] { "OnUpdate" }),
            (typeof(UpdateEntityInput_Server), new[] { "OnUpdate" }),
            (typeof(HandleOpenVBloodMenuSystem), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.Sequencer.HandleOpenMapSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.Sequencer.HandleOpenMapMenuSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.HandleOpenMapSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.HandleOpenMapMenuSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.Sequencer.HandleOpenMenuSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.HandleOpenMenuSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.Sequencer.HandleOpenInventorySystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.HandleOpenInventorySystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.Sequencer.HandleOpenSocialMenuSystem, ProjectM"), new[] { "OnUpdate" }),
            (ResolveType("ProjectM.HandleOpenSocialMenuSystem, ProjectM"), new[] { "OnUpdate" }),
        };

        var yielded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (type, methods) in candidates)
        {
            if (type == null) continue;

            foreach (string methodName in methods)
            {
                MethodInfo? method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    Plugin.LogInstance.LogInfo($"[ScarletTeleportGUI] Input suppression patch skipped: {type.FullName}.{methodName}");
                    continue;
                }

                string key = $"{method.DeclaringType?.FullName}.{method.Name}";
                if (!yielded.Add(key)) continue;

                Plugin.LogInstance.LogInfo($"[ScarletTeleportGUI] Input suppression patch armed: {key}");
                yield return method;
            }
        }
    }

    static Type? ResolveType(string assemblyQualifiedName)
        => Type.GetType(assemblyQualifiedName, throwOnError: false);
}