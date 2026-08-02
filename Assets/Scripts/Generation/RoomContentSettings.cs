using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What <see cref="DungeonPopulator"/> puts into a generated dungeon. Split from
/// <see cref="DungeonGenerationSettings"/> because the two are tuned for different
/// reasons: layout settings decide whether a dungeon is well shaped, these decide
/// whether it is worth playing.
///
/// Everything references prefabs and items by the stable ids in
/// <see cref="PrefabRegistry"/> and <see cref="ItemDatabase"/>, never by direct asset
/// reference, so the same ids work for spawning and for saving.
/// </summary>
[CreateAssetMenu(fileName = "RoomContentSettings", menuName = "Generation/Room Content Settings")]
public class RoomContentSettings : ScriptableObject
{
    /// <summary>A prefab that may be spawned, with its relative likelihood.</summary>
    [Serializable]
    public class PrefabChoice
    {
        [Tooltip("Id from the PrefabRegistry.")]
        public string prefabId;

        [Min(0f)] public float weight = 1f;

        [Tooltip("Never appears in rooms closer than this many hops from the start room.")]
        [Min(0)] public int minDepth;
    }

    /// <summary>An item that may be dropped as loot, with its relative likelihood.</summary>
    [Serializable]
    public class ItemChoice
    {
        [Tooltip("Stable ItemData id from the ItemDatabase.")]
        public string itemId;

        [Min(0f)] public float weight = 1f;
        [Min(1)] public int minCount = 1;
        [Min(1)] public int maxCount = 1;
    }

    [Header("Doors")]
    [Tooltip("Spawned on every doorway cell. Leave empty to generate open archways.")]
    public string doorPrefabId = "world.door";

    [Header("Enemies")]
    public List<PrefabChoice> enemies = new List<PrefabChoice>();

    [Tooltip("Enemies in a room one hop from the start. Fractional values act as a chance.")]
    [Min(0f)] public float enemiesAtFirstDepth = 0.6f;

    [Tooltip("Additional enemies per further hop from the start room.")]
    [Min(0f)] public float enemiesPerDepth = 0.5f;

    [Min(0)] public int maxEnemiesPerRoom = 4;

    [Header("Loot")]
    public List<ItemChoice> loot = new List<ItemChoice>();
    [Min(0)] public int minLootPerRoom;
    [Min(0)] public int maxLootPerRoom = 2;

    [Tooltip("Item guaranteed a minimum number of drops per dungeon — normally lamp fuel. " +
             "Without a floor on this, a bad roll can make a run unwinnable in the dark.")]
    public string guaranteedItemId;
    [Min(0)] public int guaranteedItemDrops = 3;

    [Header("Treasure room")]
    public List<ItemChoice> treasureLoot = new List<ItemChoice>();
    [Min(0)] public int treasureLootCount = 3;

    [Header("Props")]
    [Tooltip("Cover and scenery. Per the project convention these never block vision or " +
             "pathfinding, so they are safe to scatter anywhere walkable.")]
    public List<PrefabChoice> props = new List<PrefabChoice>();
    [Min(0f)] public float propsPerHundredFloorCells = 6f;

    [Header("Camp room")]
    public string saveStationPrefabId = "world.savestation";
    public string lightPrefabId = "world.lamp";

    /// <summary>
    /// Picks a prefab by weight, ignoring entries gated above the room's depth.
    /// Returns null when nothing is eligible.
    /// </summary>
    public PrefabChoice PickPrefab(List<PrefabChoice> pool, int depth, DeterministicRandom random)
    {
        if (pool == null || pool.Count == 0) return null;

        float total = 0f;
        foreach (var choice in pool)
        {
            if (IsEligible(choice, depth)) total += choice.weight;
        }
        if (total <= 0f) return null;

        float roll = random.NextFloat() * total;
        foreach (var choice in pool)
        {
            if (!IsEligible(choice, depth)) continue;
            roll -= choice.weight;
            if (roll <= 0f) return choice;
        }

        return null;
    }

    /// <summary>Picks an item by weight; returns null for an empty or zero-weight pool.</summary>
    public ItemChoice PickItem(List<ItemChoice> pool, DeterministicRandom random)
    {
        if (pool == null || pool.Count == 0) return null;

        float total = 0f;
        foreach (var choice in pool)
        {
            if (!string.IsNullOrEmpty(choice.itemId)) total += choice.weight;
        }
        if (total <= 0f) return null;

        float roll = random.NextFloat() * total;
        foreach (var choice in pool)
        {
            if (string.IsNullOrEmpty(choice.itemId)) continue;
            roll -= choice.weight;
            if (roll <= 0f) return choice;
        }

        return null;
    }

    /// <summary>
    /// How many enemies a room of the given depth gets. The fractional remainder is
    /// rolled rather than truncated, so shallow rooms are sometimes empty and sometimes
    /// not instead of being uniformly bare.
    /// </summary>
    public int EnemyCountFor(int depth, DeterministicRandom random)
    {
        if (depth <= 0) return 0; // the start room is always safe

        float expected = enemiesAtFirstDepth + (depth - 1) * enemiesPerDepth;
        int whole = Mathf.FloorToInt(expected);
        if (random.Chance(expected - whole)) whole++;

        return Mathf.Clamp(whole, 0, maxEnemiesPerRoom);
    }

    private static bool IsEligible(PrefabChoice choice, int depth)
    {
        return choice != null &&
               !string.IsNullOrEmpty(choice.prefabId) &&
               choice.weight > 0f &&
               depth >= choice.minDepth;
    }

    private void OnValidate()
    {
        if (maxLootPerRoom < minLootPerRoom) maxLootPerRoom = minLootPerRoom;
        foreach (var choice in loot) ClampCounts(choice);
        foreach (var choice in treasureLoot) ClampCounts(choice);
    }

    private static void ClampCounts(ItemChoice choice)
    {
        if (choice != null && choice.maxCount < choice.minCount) choice.maxCount = choice.minCount;
    }
}
