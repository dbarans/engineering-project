using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry resolving stable string ids back to spawnable prefabs — the prefab-side
/// counterpart of <see cref="ItemDatabase"/>.
///
/// Two systems need it. The procedural generator spawns enemies, doors and props that
/// exist in no authored scene, so a save can only reference them by id. And
/// <see cref="SaveManager"/> currently skips any saved entity whose guid has no live
/// object, logging that "respawning from a prefab registry is not implemented yet" —
/// this asset is the missing half of that path.
///
/// Ids are written into save files, so they are permanent: rename the prefab freely,
/// never the id. Lives in <c>Assets/Resources</c> so it loads without scene wiring.
/// </summary>
[CreateAssetMenu(fileName = "PrefabRegistry", menuName = "Generation/Prefab Registry")]
public class PrefabRegistry : ScriptableObject
{
    /// <summary>Path under Resources the runtime instance is loaded from.</summary>
    public const string ResourcesPath = "PrefabRegistry";

    /// <summary>One id-to-prefab binding.</summary>
    [Serializable]
    public class Entry
    {
        [Tooltip("Stable id stored in save files, e.g. \"enemy.skullguy\". Never change it once saves exist.")]
        public string id;

        public GameObject prefab;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    private Dictionary<string, GameObject> _byId;
    private static PrefabRegistry _instance;

    /// <summary>The project-wide registry from Resources, or null when the asset is missing.</summary>
    public static PrefabRegistry Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<PrefabRegistry>(ResourcesPath);
            return _instance;
        }
    }

    /// <summary>All registered bindings.</summary>
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>Returns the prefab bound to the given id, or null when unknown.</summary>
    public GameObject Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) BuildLookup();
        return _byId.TryGetValue(id, out var prefab) ? prefab : null;
    }

    /// <summary>
    /// Instantiates the prefab bound to <paramref name="id"/> and, when the instance
    /// carries a <see cref="SaveableEntity"/>, stamps it with <paramref name="guid"/>.
    /// The generator passes a guid derived from the dungeon seed so a regenerated
    /// dungeon produces the same identities as the one that was saved.
    /// Returns null (and logs) when the id is unknown.
    /// </summary>
    public GameObject Spawn(string id, Vector2 position, Transform parent = null, string guid = null)
    {
        GameObject prefab = Resolve(id);
        if (prefab == null)
        {
            Debug.LogError($"[PrefabRegistry] Unknown prefab id '{id}' — nothing spawned.", this);
            return null;
        }

        GameObject instance = InstantiateFor(prefab, position, parent);

        var entity = instance.GetComponent<SaveableEntity>();
        if (entity != null)
        {
            // Recorded even without a guid: it is what lets the save system recreate the
            // object later, rather than only overlay state onto one that already exists.
            entity.SetPrefabId(id);
            if (!string.IsNullOrEmpty(guid)) entity.SetGuid(guid);
        }
        else if (!string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning(
                $"[PrefabRegistry] '{id}' was given guid '{guid}' but its prefab has no " +
                "SaveableEntity — the object will not survive a save/load.", instance);
        }

        return instance;
    }

    /// <summary>
    /// Creates the instance, keeping it linked to its prefab when this runs in the editor.
    ///
    /// Plain <see cref="Object.Instantiate"/> in edit mode produces a detached copy: it
    /// looks identical, but it is no longer an instance of anything, so later edits to the
    /// prefab never reach it. That does not matter at runtime, where the objects live for
    /// one session — it matters a great deal for the Dungeon scene, which is authored by
    /// generating a dungeon and saving the result, and would otherwise accumulate hundreds
    /// of orphaned doors and props that quietly stop tracking their prefabs.
    /// </summary>
    private static GameObject InstantiateFor(GameObject prefab, Vector2 position, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var linked = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            linked.transform.SetPositionAndRotation(position, Quaternion.identity);
            return linked;
        }
#endif
        return Instantiate(prefab, position, Quaternion.identity, parent);
    }

    private void OnEnable()
    {
        _byId = null; // entries may have been edited in the inspector; relearn lazily
    }

    private void BuildLookup()
    {
        _byId = new Dictionary<string, GameObject>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id) || entry.prefab == null) continue;
            if (!_byId.TryAdd(entry.id, entry.prefab))
            {
                Debug.LogError(
                    $"[PrefabRegistry] Duplicate id '{entry.id}' on '{entry.prefab.name}' and " +
                    $"'{_byId[entry.id].name}'. Saves referencing it will resolve to the first one.",
                    this);
            }
        }
    }
}
