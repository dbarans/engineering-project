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

    /// <summary>
    /// Overwrites a slot with the given item/count (count &lt;= 0 or null item clears it).
    /// Durability is carried over when the slot already held the same item (so draining or
    /// topping up a stack keeps its wear) and otherwise reset to full for the incoming item.
    /// Use the four-argument overload to place an item with an explicit durability (moves, saves).
    /// </summary>
    public void Set(int index, ItemData item, int count)
    {
        if (!InRange(index)) return;
        var slot = _slots[index];
        bool sameItem = !slot.IsEmpty && ItemStack.IsSameItem(slot.item, item);
        Set(index, item, count, sameItem ? slot.durability : -1);
    }

    /// <summary>
    /// Overwrites a slot with the given item/count and an explicit remaining
    /// <paramref name="durability"/> (<c>-1</c> = full). Used when the durability must travel
    /// with the item — cursor moves and save restore — rather than being inferred.
    /// </summary>
    public void Set(int index, ItemData item, int count, int durability)
    {
        if (!InRange(index)) return;
        bool hasItem = item != null && count > 0;
        _slots[index].item = hasItem ? item : null;
        _slots[index].count = hasItem ? count : 0;
        _slots[index].durability = hasItem ? durability : -1;
        SlotChanged?.Invoke(index);
    }

    /// <summary>Empties the slot at <paramref name="index"/>.</summary>
    public void Clear(int index) => Set(index, null, 0);

    /// <summary>Remaining durability of the slot's item (0 for empty or non-durable slots).</summary>
    public int GetDurability(int index)
    {
        var s = Get(index);
        return s != null ? s.CurrentDurability : 0;
    }

    /// <summary>
    /// Sets the slot's remaining durability, clamped to the item's range, and raises
    /// <see cref="SlotChanged"/> when it actually changed. No-op for empty or non-durable slots.
    /// </summary>
    public void SetDurability(int index, int value)
    {
        var s = Get(index);
        if (s == null || s.IsEmpty || !s.HasDurability) return;
        int clamped = Mathf.Clamp(value, 0, s.MaxDurability);
        if (s.durability == clamped) return;
        s.durability = clamped;
        SlotChanged?.Invoke(index);
    }

    /// <summary>
    /// Spends <paramref name="amount"/> durability from the slot's item (never below 0),
    /// raising <see cref="SlotChanged"/> when it changed. Returns the remaining durability.
    /// </summary>
    public int ReduceDurability(int index, int amount)
    {
        var s = Get(index);
        if (s == null || s.IsEmpty || !s.HasDurability || amount <= 0) return GetDurability(index);
        int next = Mathf.Clamp(s.CurrentDurability - amount, 0, s.MaxDurability);
        if (next != s.durability)
        {
            s.durability = next;
            SlotChanged?.Invoke(index);
        }
        return next;
    }

    /// <summary>Total number of <paramref name="item"/> units held across all slots.</summary>
    public int Count(ItemData item)
    {
        if (item == null) return 0;
        int total = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            if (!s.IsEmpty && ItemStack.IsSameItem(s.item, item)) total += s.count;
        }
        return total;
    }

    /// <summary>
    /// Removes up to <paramref name="count"/> units of <paramref name="item"/>, draining
    /// matching stacks until satisfied. Returns the amount actually removed.
    /// </summary>
    public int TryRemove(ItemData item, int count = 1)
    {
        if (item == null || count <= 0) return 0;
        int removed = 0;
        for (int i = 0; i < _slots.Length && removed < count; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || !ItemStack.IsSameItem(s.item, item)) continue;
            int take = Mathf.Min(s.count, count - removed);
            Set(i, s.item, s.count - take); // 0 -> clears; raises SlotChanged
            removed += take;
        }
        return removed;
    }

    /// <summary>Swaps the contents of two slots, raising a change for each.</summary>
    public void Swap(int a, int b)
    {
        if (!InRange(a) || !InRange(b) || a == b) return;
        ItemData itemA = _slots[a].item;
        int countA = _slots[a].count;
        int durabilityA = _slots[a].durability;
        _slots[a].item = _slots[b].item;
        _slots[a].count = _slots[b].count;
        _slots[a].durability = _slots[b].durability;
        _slots[b].item = itemA;
        _slots[b].count = countA;
        _slots[b].durability = durabilityA;
        SlotChanged?.Invoke(a);
        SlotChanged?.Invoke(b);
    }

    /// <summary>
    /// Whether <paramref name="count"/> units of <paramref name="item"/> would fit —
    /// topping up matching stacks (respecting <see cref="ItemData.maxStack"/>) then
    /// using empty slots. Does not mutate anything.
    /// </summary>
    public bool CanAdd(ItemData item, int count = 1)
    {
        if (item == null) return false;
        if (count <= 0) return true;

        int max = Mathf.Max(1, item.maxStack);
        int room = 0;
        for (int i = 0; i < _slots.Length && room < count; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty) room += max;
            else if (ItemStack.IsSameItem(s.item, item)) room += s.SpaceLeft;
        }
        return room >= count;
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

    /// <summary>
    /// Adds <paramref name="count"/> of <paramref name="item"/> carrying an explicit
    /// remaining <paramref name="durability"/> (<c>-1</c> = full/undamaged). Used when
    /// picking a worn item up off the ground, where the plain overload would silently
    /// repair it.
    ///
    /// A worn item only ever fills empty slots, never merges into an existing stack:
    /// one slot holds one durability value, so merging two differently worn copies would
    /// have to pick one of them and quietly discard the other.
    /// </summary>
    public int TryAddItem(ItemData item, int count, int durability)
    {
        if (item == null || count <= 0) return 0;

        // Nothing to carry — a non-durable item, or one that is undamaged anyway — so the
        // ordinary stacking rules apply and are strictly better (they merge).
        if (durability < 0 || item.maxDurability <= 0) return TryAddItem(item, count);

        int max = Mathf.Max(1, item.maxStack);
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            int moved = Mathf.Min(max, count);
            Set(i, item, moved, durability);
            count -= moved;
        }
        return count;
    }
}
