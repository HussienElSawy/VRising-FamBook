using UnityEngine;

namespace FamBook.Utilities;

/// <summary>
/// Utility helpers for finding and traversing Unity GameObjects in the HUD.
/// </summary>
internal static class GameObjects
{
    /// <summary>
    /// Recursively searches <paramref name="parent"/> and its descendants for
    /// a child with the given <paramref name="name"/>.
    /// Returns the matching Transform, or <c>null</c> if not found.
    /// </summary>
    public static Transform? FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var result = FindChildRecursive(parent.GetChild(i), name);
            if (result != null) return result;
        }

        return null;
    }

    /// <summary>
    /// Finds a child by name, logging a warning when not found.
    /// </summary>
    public static GameObject? FindTargetObject(Transform parent, string name)
    {
        var result = FindChildRecursive(parent, name);
        if (result == null)
            Core.Log.LogWarning($"[FamBook] Could not find child '{name}' under '{parent.name}'");
        return result?.gameObject;
    }

    /// <summary>
    /// Deactivates all children of <paramref name="parent"/> except those
    /// whose name matches <paramref name="keepName"/>.
    /// </summary>
    public static void DeactivateChildrenExcept(Transform parent, string keepName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (!child.name.Equals(keepName, StringComparison.Ordinal))
                child.gameObject.SetActive(false);
        }
    }
}
