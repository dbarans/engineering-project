using UnityEngine;

/// <summary>
/// Shared helpers for the one-click scene setup tools
/// (<see cref="SlotInventorySetup"/>, <see cref="ItemDropSetup"/>,
/// <see cref="DungeonSceneSetup"/>, …).
/// </summary>
public static class EditorSetupUtility
{
    /// <summary>
    /// Returns the component on <paramref name="target"/>, adding it when absent.
    ///
    /// Exists so setup tools stop writing <c>GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()</c>.
    /// That form looks equivalent but is not: <c>??</c> tests reference equality, while a
    /// Unity object whose native half is missing or not yet live is a non-null C#
    /// reference that only Unity's overloaded <c>==</c> reports as null. In that case the
    /// right-hand side never runs, the component is never added, and the next field
    /// assignment throws MissingComponentException — with the tool having reported no
    /// error at all.
    /// </summary>
    public static T EnsureComponent<T>(GameObject target) where T : Component
    {
        if (target == null) return null;

        T existing = target.GetComponent<T>();
        return existing != null ? existing : target.AddComponent<T>();
    }
}
