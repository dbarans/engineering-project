using System;
using UnityEngine;

/// <summary>
/// A single stack entry: an item plus how many of it occupy one slot.
/// Backs the slots of <see cref="ItemContainer"/>.
/// </summary>
[Serializable]
public class ItemStack
{
    public ItemData item;
    public int count = 1;

    /// <summary>
    /// Remaining durability for a durable item (see <see cref="ItemData.maxDurability"/>).
    /// <c>-1</c> means "undamaged": the stack reads as full without having to be seeded when
    /// the item is created, moved, or restored. Read through <see cref="CurrentDurability"/>,
    /// which resolves the sentinel to the item's max.
    /// </summary>
    public int durability = -1;

    /// <summary>True when this stack holds no item (or a non-positive count).</summary>
    public bool IsEmpty => item == null || count <= 0;

    /// <summary>True when the held item wears out with use (its <see cref="ItemData.maxDurability"/> is positive).</summary>
    public bool HasDurability => item != null && item.maxDurability > 0;

    /// <summary>The held item's durability ceiling (0 when empty or not a durable item).</summary>
    public int MaxDurability => item != null ? Mathf.Max(0, item.maxDurability) : 0;

    /// <summary>
    /// Remaining durability in the range 0..<see cref="MaxDurability"/>, resolving the
    /// "undamaged" sentinel (<c>-1</c>) to a full bar. 0 for non-durable or empty stacks.
    /// </summary>
    public int CurrentDurability =>
        HasDurability ? (durability < 0 ? MaxDurability : Mathf.Clamp(durability, 0, MaxDurability)) : 0;

    /// <summary>The stack ceiling for the held item (at least 1), or 1 when empty.</summary>
    public int MaxStack => item != null ? Mathf.Max(1, item.maxStack) : 1;

    /// <summary>How many more units fit in this stack before hitting <see cref="MaxStack"/>.</summary>
    public int SpaceLeft => IsEmpty ? 0 : Mathf.Max(0, MaxStack - count);

    /// <summary>True when <paramref name="other"/> could merge into this stack (same item, room left).</summary>
    public bool CanStackWith(ItemData other) => !IsEmpty && IsSameItem(item, other) && SpaceLeft > 0;

    /// <summary>Empties the stack in place.</summary>
    public void Clear()
    {
        item = null;
        count = 0;
        durability = -1;
    }

    /// <summary>
    /// Item equality used across the inventory systems: by reference, then by stable
    /// <see cref="ItemData.Id"/>. The display name is only a fallback for assets that
    /// have no id yet — names are presentation data and may repeat or change.
    /// </summary>
    public static bool IsSameItem(ItemData a, ItemData b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (!string.IsNullOrEmpty(a.Id) && !string.IsNullOrEmpty(b.Id))
            return string.Equals(a.Id, b.Id, StringComparison.Ordinal);
        return string.Equals(a.itemName, b.itemName, StringComparison.Ordinal);
    }
}
