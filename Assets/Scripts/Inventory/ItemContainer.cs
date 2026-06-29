using System;
using UnityEngine;

/// <summary>
/// A fixed-size grid of <see cref="ItemStack"/> slots. Slots always exist; an
/// empty slot is a non-null stack with no item. Plain C# (not a MonoBehaviour)
/// so it can be owned by <see cref="SlotInventory"/> and shared between views.
/// </summary>
public class ItemContainer
{
    private readonly ItemStack[] _slots;

    /// <summary>Number of slots in this container (always &gt;= 0).</summary>
    public int SlotCount => _slots.Length;

    /// <summary>Raised with a slot index whenever that slot's contents change.</summary>
    public event Action<int> SlotChanged;

    public ItemContainer(int size)
    {
        size = Mathf.Max(0, size);
        _slots = new ItemStack[size];
        for (int i = 0; i < size; i++)
            _slots[i] = new ItemStack { item = null, count = 0 };
    }

    private bool InRange(int index) => index >= 0 && index < _slots.Length;

    /// <summary>Returns the live stack at <paramref name="index"/>, or <c>null</c> if out of range.</summary>
    public ItemStack Get(int index) => InRange(index) ? _slots[index] : null;

    /// <summary>Overwrites a slot with the given item/count (count &lt;= 0 or null item clears it).</summary>
    public void Set(int index, ItemData item, int count)
    {
        if (!InRange(index)) return;
        bool hasItem = item != null && count > 0;
        _slots[index].item = hasItem ? item : null;
        _slots[index].count = hasItem ? count : 0;
        SlotChanged?.Invoke(index);
    }

    /// <summary>Empties the slot at <paramref name="index"/>.</summary>
    public void Clear(int index) => Set(index, null, 0);

    /// <summary>Swaps the contents of two slots, raising a change for each.</summary>
    public void Swap(int a, int b)
    {
        if (!InRange(a) || !InRange(b) || a == b) return;
        ItemData itemA = _slots[a].item;
        int countA = _slots[a].count;
        _slots[a].item = _slots[b].item;
        _slots[a].count = _slots[b].count;
        _slots[b].item = itemA;
        _slots[b].count = countA;
        SlotChanged?.Invoke(a);
        SlotChanged?.Invoke(b);
    }

    /// <summary>
    /// Adds <paramref name="count"/> of <paramref name="item"/>, first topping up
    /// existing matching stacks (respecting <see cref="ItemData.maxStack"/>) then
    /// filling empty slots. Returns the amount that did not fit.
    /// </summary>
    public int TryAddItem(ItemData item, int count = 1)
    {
        if (item == null || count <= 0) return 0;
        int max = Mathf.Max(1, item.maxStack);

        // Top up existing stacks of the same item.
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            var slot = _slots[i];
            if (slot.IsEmpty || !ItemStack.IsSameItem(slot.item, item)) continue;
            int moved = Mathf.Min(slot.SpaceLeft, count);
            if (moved <= 0) continue;
            Set(i, item, slot.count + moved);
            count -= moved;
        }

        // Fill empty slots with fresh stacks.
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            int moved = Mathf.Min(max, count);
            Set(i, item, moved);
            count -= moved;
        }

        return count;
    }
}
