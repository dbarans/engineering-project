using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
/// <summary>
/// A single stack entry for the player's inventory.
/// </summary>
public class ItemStack
{
    public ItemData item;
    public int count = 1;
}

/// <summary>
/// Player inventory implementation backed by a list of item stacks.
/// </summary>
public class PlayerInventory : MonoBehaviour, IInventory
{
    [SerializeField] private List<ItemStack> items = new List<ItemStack>();

    private List<ItemData> _flatCache = new List<ItemData>();

    private static bool IsSameItem(ItemData a, ItemData b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        return string.Equals(a.itemName, b.itemName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns a flattened view of all items in the inventory (repeated by stack count).
    /// </summary>
    public IReadOnlyList<ItemData> Items
    {
        get
        {
            _flatCache.Clear();
            foreach (var s in items)
            {
                if (s?.item == null) continue;
                for (int i = 0; i < s.count; i++)
                    _flatCache.Add(s.item);
            }
            return _flatCache;
        }
    }

    /// <summary>
    /// Counts how many instances of the given item are present in the inventory.
    /// </summary>
    public int Count(ItemData item)
    {
        if (item == null) return 0;
        foreach (var s in items)
            if (IsSameItem(s?.item, item)) return s.count;
        return 0;
    }

    /// <summary>
    /// Returns <c>true</c> if the inventory contains at least one instance of the given item.
    /// </summary>
    public bool HasItem(ItemData item)
    {
        return Count(item) > 0;
    }

    /// <summary>
    /// Removes one instance of the given item (decrements stack count, removes empty stacks).
    /// </summary>
    public bool RemoveItem(ItemData item)
    {
        if (item == null) return false;
        for (int i = 0; i < items.Count; i++)
        {
            if (!IsSameItem(items[i]?.item, item)) continue;
            items[i].count--;
            if (items[i].count <= 0)
                items.RemoveAt(i);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Adds one instance of the given item (increments existing stack or creates a new one).
    /// </summary>
    public void AddItem(ItemData item)
    {
        if (item == null) return;
        foreach (var s in items)
        {
            if (IsSameItem(s?.item, item))
            {
                s.count++;
                return;
            }
        }
        items.Add(new ItemStack { item = item, count = 1 });
    }
}
