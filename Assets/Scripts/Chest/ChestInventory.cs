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

    /// <summary>Total slots this chest has, without forcing <see cref="Container"/> to exist.</summary>
    public int SlotCount => columns * rows;

    /// <summary>
    /// Replaces what this chest starts stocked with. Called by the dungeon populator.
    ///
    /// Writes <see cref="startingItems"/> rather than <see cref="Container"/>, because the
    /// container is runtime-only state and would not survive the Dungeon scene being baked
    /// (generate in edit mode, then save the scene). If the container has already been
    /// created — a second Generate over a live scene — it is re-applied too, so the chest
    /// does not silently keep the previous run's contents.
    /// </summary>
    public void SetStartingItems(IEnumerable<(ItemData item, int count)> stacks)
    {
        startingItems.Clear();
        foreach (var (item, count) in stacks)
        {
            if (item == null || count <= 0) continue;
            startingItems.Add(new StartingStack { item = item, count = count });
        }

        if (_container != null)
        {
            for (int i = 0; i < _container.SlotCount; i++) _container.Clear(i);
            ApplyStartingItems();
        }

#if UNITY_EDITOR
        // Without this, a chest stocked while baking the Dungeon scene looks stocked in
        // memory but is never written into the scene file, and comes back empty after a
        // domain reload.
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
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
