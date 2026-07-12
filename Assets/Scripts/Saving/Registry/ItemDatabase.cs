using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry resolving stable <see cref="ItemData.Id"/> values back to their assets.
/// The save system stores item ids only, never asset references. Lives in
/// <c>Assets/Resources</c> so it loads without scene wiring; kept up to date by the
/// editor scanner (<b>Tools ▸ Save System ▸ Rebuild Item Database</b>, plus an
/// automatic rebuild whenever item assets are imported or deleted).
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Items/Item Database")]
public class ItemDatabase : ScriptableObject
{
    /// <summary>Path under Resources the runtime instance is loaded from.</summary>
    public const string ResourcesPath = "ItemDatabase";

    [SerializeField] private List<ItemData> items = new List<ItemData>();

    private Dictionary<string, ItemData> _byId;
    private static ItemDatabase _instance;

    /// <summary>The project-wide database from Resources, or null when the asset is missing.</summary>
    public static ItemDatabase Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<ItemDatabase>(ResourcesPath);
            return _instance;
        }
    }

    /// <summary>All registered items.</summary>
    public IReadOnlyList<ItemData> Items => items;

    /// <summary>Returns the item with the given stable id, or null when unknown.</summary>
    public ItemData Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) BuildLookup();
        return _byId.TryGetValue(id, out var item) ? item : null;
    }

    private void OnEnable()
    {
        _byId = null; // list may have been rebuilt in the editor; relearn lazily
    }

    private void BuildLookup()
    {
        _byId = new Dictionary<string, ItemData>(items.Count);
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) continue;
            if (!_byId.TryAdd(item.Id, item))
            {
                Debug.LogError(
                    $"[ItemDatabase] Duplicate item id '{item.Id}' on '{item.name}' and " +
                    $"'{_byId[item.Id].name}'. Run Tools ▸ Save System ▸ Rebuild Item Database.",
                    this);
            }
        }
    }
}
