using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stable identity for a persistent world object (enemy, chest, future loot container).
/// The save system stores world state as a <c>guid -&gt; state</c> dictionary; this
/// component provides the guid half of that pair.
///
/// Guid assignment:
/// - Scene objects get a guid in the editor the moment the component is added or the
///   scene is opened; duplicating an object in the scene regenerates the copy's guid.
/// - Prefab assets deliberately keep an empty guid — every instance must own its own.
///   Runtime spawns get a random guid in Awake, or a caller-chosen one via
///   <see cref="SetGuid"/> right after Instantiate (deterministic guids for a future
///   procedural generator).
///
/// The static registry tracks instances independently of their active state (dead
/// enemies are deactivated, not destroyed): registration happens in Awake, removal in
/// OnDestroy. Objects inactive from scene load are not registered until first enabled.
/// </summary>
[DisallowMultipleComponent]
public class SaveableEntity : MonoBehaviour
{
    [Tooltip("Stable identifier used by the save system. Auto-generated; never edit or reuse.")]
    [SerializeField] private string guid;

    private static readonly Dictionary<string, SaveableEntity> Registry =
        new Dictionary<string, SaveableEntity>();

    /// <summary>Stable identity of this instance (empty on prefab assets).</summary>
    public string Guid => guid;

    /// <summary>All live entities, including inactive (e.g. deactivated-on-death) ones.</summary>
    public static IReadOnlyCollection<SaveableEntity> All => Registry.Values;

    /// <summary>Returns the live entity with the given guid, or null when none exists.</summary>
    public static SaveableEntity Find(string guid)
    {
        return !string.IsNullOrEmpty(guid) && Registry.TryGetValue(guid, out var entity)
            ? entity
            : null;
    }

    /// <summary>
    /// Overrides the guid of a runtime-spawned instance. Call right after Instantiate;
    /// re-registers the entity under the new guid.
    /// </summary>
    public void SetGuid(string newGuid)
    {
        if (string.IsNullOrEmpty(newGuid) || newGuid == guid) return;
        Unregister();
        guid = newGuid;
        Register();
    }

    private void Awake()
    {
        // Runtime spawn from a prefab (empty guid) that was not given an explicit id.
        if (string.IsNullOrEmpty(guid))
            guid = System.Guid.NewGuid().ToString("N");
        Register();
    }

    private void OnDestroy()
    {
        Unregister();
    }

    private void Register()
    {
        if (Registry.TryGetValue(guid, out var existing) && existing != this)
        {
            Debug.LogError(
                $"[SaveableEntity] Duplicate guid '{guid}' on '{name}' and '{existing.name}' — " +
                "regenerating. Saved state for this object will not restore correctly; " +
                "fix the duplicate in the scene.", this);
            guid = System.Guid.NewGuid().ToString("N");
        }
        Registry[guid] = this;
    }

    private void Unregister()
    {
        if (!string.IsNullOrEmpty(guid) &&
            Registry.TryGetValue(guid, out var entity) && entity == this)
        {
            Registry.Remove(guid);
        }
    }

#if UNITY_EDITOR
    // Editor-time guid bookkeeping: hands out guids to scene objects and repairs
    // duplicates created by copy-pasting objects within (or across) open scenes.
    private static readonly Dictionary<string, SaveableEntity> EditorGuids =
        new Dictionary<string, SaveableEntity>();

    private void OnValidate()
    {
        if (Application.isPlaying) return;

        // Prefab assets keep an empty guid; only instances in a scene own one.
        if (!gameObject.scene.IsValid())
        {
            if (!string.IsNullOrEmpty(guid))
            {
                guid = string.Empty;
                UnityEditor.EditorUtility.SetDirty(this);
            }
            return;
        }

        if (string.IsNullOrEmpty(guid) || IsGuidTakenByOther())
        {
            guid = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
        EditorGuids[guid] = this;
    }

    private bool IsGuidTakenByOther()
    {
        return EditorGuids.TryGetValue(guid, out var other) &&
               other != null && other != this && other.guid == guid;
    }
#endif
}
