using UnityEngine;

/// <summary>
/// The ink cost of saving. Each save consumes one "ink" item from the player's
/// inventory (hotbar or backpack), so the number of times the player can save equals
/// the amount of ink they carry. This component owns the ink item reference and
/// answers "how many saves are left" and "spend one" for <see cref="SaveLoadUI"/>.
///
/// The ink item is non-stackable by design (<see cref="ItemData.maxStack"/> = 1): one
/// ink occupies one slot and buys exactly one save. When no item is wired the cost is
/// disabled (saving stays free) so a half-configured scene is never soft-locked out of
/// saving — the editor setup (<c>Tools ▸ Save System ▸ Build Save Station &amp; UI</c>)
/// wires the ink item, which is what enforces the cost in a properly built scene.
///
/// The inventory is the source of truth, so ink picked up, crafted, dropped or spent
/// elsewhere is reflected automatically — nothing here tracks a separate counter.
/// </summary>
public class SaveCost : MonoBehaviour
{
    [Tooltip("Item each save consumes (the ink). Leave empty to make saving free.")]
    [SerializeField] private ItemData saveItem;

    private SlotInventory _inventory;

    /// <summary>The item a save consumes, or null when saving is free.</summary>
    public ItemData SaveItem => saveItem;

    /// <summary>Whether an ink cost is configured at all.</summary>
    public bool HasCost => saveItem != null;

    private SlotInventory Inventory
    {
        get
        {
            if (_inventory == null)
                _inventory = FindFirstObjectByType<SlotInventory>();
            return _inventory;
        }
    }

    /// <summary>
    /// How many times the player can save right now: the total ink across the hotbar and
    /// backpack. Returns <see cref="int.MaxValue"/> when no cost is configured.
    /// </summary>
    public int AvailableSaves
    {
        get
        {
            if (saveItem == null) return int.MaxValue;
            var inv = Inventory;
            if (inv == null) return 0;
            return inv.Hotbar.Count(saveItem) + inv.Backpack.Count(saveItem);
        }
    }

    /// <summary>True when a save is affordable right now.</summary>
    public bool CanSave => saveItem == null || AvailableSaves > 0;

    /// <summary>
    /// Spends one ink, draining the hotbar first then the backpack. Returns false only
    /// when a cost is configured but no ink was available; a no-op returning true when
    /// saving is free.
    /// </summary>
    public bool TryConsume()
    {
        if (saveItem == null) return true;
        var inv = Inventory;
        if (inv == null) return false;
        if (inv.Hotbar.TryRemove(saveItem, 1) > 0) return true;
        return inv.Backpack.TryRemove(saveItem, 1) > 0;
    }

    /// <summary>
    /// Puts one ink back, used to undo a <see cref="TryConsume"/> when the save write
    /// failed — a failed save must never cost the player their ink. There is always room
    /// because the slot was freed a moment ago.
    /// </summary>
    public void Refund()
    {
        if (saveItem == null) return;
        var inv = Inventory;
        if (inv == null) return;
        int leftover = inv.Hotbar.TryAddItem(saveItem, 1);
        if (leftover > 0) inv.Backpack.TryAddItem(saveItem, leftover);
    }
}
