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

        [Tooltip("Never appears in rooms closer than this many hops from the hub.")]
        [Min(0)] public int minDepth;

        [Tooltip("Props only. Always placed alone, never as the anchor or a member of a " +
                 "cluster — for things that read wrong repeated, like a statue.")]
        public bool solitary;
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

    [Tooltip("Enemies in a room one hop from the hub. Fractional values act as a chance.")]
    [Min(0f)] public float enemiesAtFirstDepth = 0.6f;

    [Tooltip("Additional enemies per further hop from the hub.")]
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

    [Header("Treasure rooms")]
    [Tooltip("Item id of the key a treasure room's door takes — the craftable one, not " +
             "the golden exit key. Every treasure room takes the same kind of key and " +
             "consumes one when opened. Leave empty to leave them unlocked, which makes " +
             "them ordinary small rooms with good loot in them.")]
    public string treasureKeyItemId;

    [Tooltip("Loose loot on a treasure room's floor, on top of its chests.")]
    public List<ItemChoice> treasureLoot = new List<ItemChoice>();
    [Min(0)] public int treasureLootCount = 3;

    [Header("Chests")]
    [Tooltip("Id from the PrefabRegistry. Leave empty to generate no chests at all.")]
    public string chestPrefabId = "world.chest";

    [Tooltip("Chance an ordinary room contains a chest. The hub itself never does — it gets " +
             "its own chests, see Hub Chests below.")]
    [Range(0f, 1f)] public float chestChancePerRoom = 0.25f;

    [Tooltip("Added to the chance per hop from the hub, so the far end of the run " +
             "is where the rewards are. The total is clamped to 1.")]
    [Range(0f, 0.5f)] public float chestChancePerDepth = 0.05f;

    [Min(0)] public int maxChestsPerRoom = 1;

    [Tooltip("What an ordinary chest is stocked with, by weight.")]
    public List<ItemChoice> chestLoot = new List<ItemChoice>();
    [Min(0)] public int minChestStacks = 1;
    [Min(1)] public int maxChestStacks = 3;

    [Tooltip("Chests guaranteed in every treasure room, on top of the loose treasure loot. " +
             "This is what a key is spent on, so a treasure room is never empty of them.")]
    [Min(0)] public int treasureChests = 1;

    [Tooltip("The treasure rooms' chest table. Falls back to chestLoot when left empty.")]
    public List<ItemChoice> treasureChestLoot = new List<ItemChoice>();
    [Min(0)] public int minTreasureChestStacks = 2;
    [Min(1)] public int maxTreasureChestStacks = 4;

    [Header("Props")]
    [Tooltip("Cover and scenery. Per the project convention these never block vision or " +
             "pathfinding, so they are safe to scatter anywhere walkable.")]
    public List<PrefabChoice> props = new List<PrefabChoice>();
    [Min(0f)] public float propsPerHundredFloorCells = 6f;

    [Tooltip("Props per cluster. Objects in a room were put there by someone: barrels " +
             "stand in threes against a wall, crates get stacked in a corner. Spread one " +
             "at a time over the floor they read as scatter rather than as belongings. " +
             "1..1 restores the old uniform scatter.")]
    [Min(1)] public int propsPerClusterMin = 2;
    [Min(1)] public int propsPerClusterMax = 4;

    [Tooltip("Chance a cluster is anchored against a wall or in a corner rather than out " +
             "in the open. Also what keeps the middle of a room clear to fight in.")]
    [Range(0f, 1f)] public float propWallBias = 0.8f;

    [Header("Corridor ambushes")]
    [Tooltip("Chance a blind alcove gets an enemy standing in it. An alcove is a pocket " +
             "the player walks past without ever having looked into, which makes it the " +
             "layout's natural ambush slot — the generator records them for exactly this.")]
    [Range(0f, 1f)] public float alcoveAmbushChance = 0.35f;

    [Tooltip("Chance a chokepoint gets an enemy posted *beside* it. Never on it: standing " +
             "in the only gap turns a decision into a wall, while standing next to it means " +
             "the player has to choose whether the way through is worth the fight.")]
    [Range(0f, 1f)] public float chokepointGuardChance = 0.2f;

    [Tooltip("Cap on enemies placed outside rooms, over the whole dungeon. Corridors are " +
             "where the player has least room to retreat, so this stays deliberately low.")]
    [Min(0)] public int maxCorridorEnemies = 6;

    [Header("Hub room")]
    [Tooltip("The dungeon's only save station. Spawned in the hub at the middle of the " +
             "map and nowhere else, so saving stays a place the player walks back to.")]
    public string saveStationPrefabId = "world.savestation";

    [Tooltip("The dungeon's only crafting table. Shares the hub with the save station " +
             "for the same reason.")]
    public string craftingTablePrefabId = "world.craftingtable";

    public string lightPrefabId = "world.lamp";

    [Tooltip("Lamps placed around the hub. More than one so it is lit from several sides " +
             "and reads as somewhere to stop, rather than one pool of light in a dark box.")]
    [Min(0)] public int hubLamps = 3;

    [Tooltip("Chests left in the hub, spawned empty. They are the player's own storage, " +
             "not loot — what ends up in them is whatever they decide not to carry.")]
    [Min(0)] public int hubChests = 3;

    [Header("Exit")]
    [Tooltip("Item id of the key that unlocks the exit room's doors. Exactly one is " +
             "generated per dungeon, in a chest on the far side of the map. Leave empty " +
             "to leave the exit unlocked — which also means no key chest is spawned.")]
    public string exitKeyItemId;

    [Header("Room templates")]
    [Tooltip("Hand-authored room interiors. A room that takes one skips random loot and " +
             "props, so the designed layout is not buried under scatter.")]
    public List<DungeonRoomTemplate> templates = new List<DungeonRoomTemplate>();

    [Tooltip("Chance a room that has a fitting template uses it.")]
    [Range(0f, 1f)] public float templateChance = 0.4f;

    /// <summary>
    /// Picks a template that fits the room, by weight. Returns null when none fits or
    /// the roll went against it.
    /// </summary>
    public DungeonRoomTemplate PickTemplate(Room room, DeterministicRandom random)
    {
        if (templates == null || templates.Count == 0) return null;
        if (!random.Chance(templateChance)) return null;

        float total = 0f;
        foreach (var template in templates)
        {
            if (template != null && template.Fits(room)) total += template.Weight;
        }
        if (total <= 0f) return null;

        float roll = random.NextFloat() * total;
        foreach (var template in templates)
        {
            if (template == null || !template.Fits(room)) continue;
            roll -= template.Weight;
            if (roll <= 0f) return template;
        }

        return null;
    }

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
        if (depth <= 0) return 0; // the hub is always safe

        float expected = enemiesAtFirstDepth + (depth - 1) * enemiesPerDepth;
        int whole = Mathf.FloorToInt(expected);
        if (random.Chance(expected - whole)) whole++;

        return Mathf.Clamp(whole, 0, maxEnemiesPerRoom);
    }

    /// <summary>Chance a given room gets a chest, scaled by its depth from the hub.</summary>
    public float ChestChanceFor(int depth) =>
        Mathf.Clamp01(chestChancePerRoom + depth * chestChancePerDepth);

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
        if (propsPerClusterMax < propsPerClusterMin) propsPerClusterMax = propsPerClusterMin;
        if (maxChestStacks < minChestStacks) maxChestStacks = minChestStacks;
        if (maxTreasureChestStacks < minTreasureChestStacks) maxTreasureChestStacks = minTreasureChestStacks;
        foreach (var choice in loot) ClampCounts(choice);
        foreach (var choice in treasureLoot) ClampCounts(choice);
        foreach (var choice in chestLoot) ClampCounts(choice);
        foreach (var choice in treasureChestLoot) ClampCounts(choice);
    }

    private static void ClampCounts(ItemChoice choice)
    {
        if (choice != null && choice.maxCount < choice.minCount) choice.maxCount = choice.minCount;
    }
}
