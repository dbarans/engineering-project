using UnityEngine;

/// <summary>
/// Capture/restore of an <see cref="ItemContainer"/> as save data, shared by every
/// saveable that owns one — the player's hotbar and backpack
/// (<see cref="PlayerSaveable"/>) and each chest (<see cref="ChestSaveable"/>).
///
/// Two rules hold for all of them, which is why this lives in one place:
/// items travel as <see cref="ItemData.Id"/> strings resolved through
/// <see cref="ItemDatabase"/> and never as asset references, and remaining durability
/// travels with the item so a worn weapon does not come back repaired.
/// </summary>
public static class ContainerSaveUtility
{
    /// <summary>
    /// Snapshots every slot of <paramref name="container"/>, empty ones included — the
    /// array index is the slot index, so gaps have to be recorded rather than skipped.
    /// Returns null for a null container.
    /// </summary>
    public static ContainerSaveData Capture(ItemContainer container)
    {
        if (container == null) return null;

        var slots = new SlotSaveData[container.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            var stack = container.Get(i);
            bool occupied = stack != null && !stack.IsEmpty;
            slots[i] = new SlotSaveData
            {
                itemId = occupied ? stack.item.Id : null,
                count = occupied ? stack.count : 0,
                durability = occupied && stack.HasDurability ? stack.CurrentDurability : -1
            };
        }
        return new ContainerSaveData { slots = slots };
    }

    /// <summary>
    /// The save is the source of truth for the whole container: every slot is
    /// overwritten, so content seeded after scene load — a debug fill, or the loot the
    /// dungeon generator puts in a freshly generated chest — cannot leak into a restored
    /// game. Does nothing when there is no saved data, which is what leaves a container
    /// the save never knew about at its authored contents.
    /// </summary>
    /// <param name="context">Logged as the offending object when an item id is unknown.</param>
    public static void Restore(ItemContainer container, ContainerSaveData saved, Object context = null)
    {
        if (container == null || saved?.slots == null) return;

        var database = ItemDatabase.Instance;
        for (int i = 0; i < container.SlotCount; i++)
        {
            var slot = i < saved.slots.Length ? saved.slots[i] : null;

            ItemData item = null;
            if (slot != null && !string.IsNullOrEmpty(slot.itemId))
            {
                item = database != null ? database.Resolve(slot.itemId) : null;
                if (item == null)
                    Debug.LogWarning(
                        $"[ContainerSaveUtility] Item id '{slot.itemId}' from the save is not " +
                        "in the ItemDatabase (asset removed?) — slot left empty.", context);
            }

            container.Set(i, item, item != null ? slot.count : 0, item != null ? slot.durability : -1);
        }
    }
}
