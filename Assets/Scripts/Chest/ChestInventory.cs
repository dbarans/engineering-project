using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source of truth for a single chest's storage. Mirrors SlotInventory's pattern:
/// owns one ItemContainer, created lazily so it's ready regardless of script
/// execution order.
///
/// Each instance owns its container outright, which is what makes chests independent of
/// one another — the shared <see cref="ChestUI"/> panel only ever binds to the container
/// of whichever chest is open. Persistence is the job of the
/// <see cref="ChestSaveable"/> next to this component.
/// </summary>
public class ChestInventory : MonoBehaviour
{
    /// <summary>One stack a chest starts stocked with.</summary>
    [Serializable]
    public class StartingStack
    {
        public ItemData item;
        [Min(1)] public int count = 1;
    }

    [Header("Chest (columns x rows)")]
    [SerializeField, Min(0)] private int columns = 4;
    [SerializeField, Min(0)] private int rows = 4;

    /// <summary>
    /// What this chest holds before anyone opens it, applied when the container is first
    /// created.
    ///
    /// Serialized rather than poured into the container on spawn, and deliberately so:
    /// the container is runtime-only state, so contents written straight into it vanish
    /// the moment a chest is baked into a scene. Anything that wants to stock a chest
    /// programmatically — a loot table, a spawner — has to write this list, not
    /// <see cref="Container"/>.
    /// </summary>
    [Header("Starting contents")]
    [Tooltip("What this chest holds before anyone opens it. A save always overrides it.")]
    [SerializeField] private List<StartingStack> startingItems = new List<StartingStack>();

    private ItemContainer _container;

    /// <summary>This chest's storage (default 4 x 4 = 16 slots).</summary>
    public ItemContainer Container
    {
        get
        {
            if (_container == null)
            {
                _container = new ItemContainer(columns * rows);
                ApplyStartingItems();
            }
            return _container;
        }
    }

    private void ApplyStartingItems()
    {
        foreach (var stack in startingItems)
        {
            if (stack == null || stack.item == null || stack.count <= 0) continue;
            _container.TryAddItem(stack.item, stack.count);
        }
    }
}
