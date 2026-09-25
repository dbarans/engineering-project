using UnityEngine;

/// <summary>
/// Tuning knobs for <see cref="RoomCorridorGenerator"/>. Kept as an asset so layout
/// variants (small/large dungeon, sparse/dense) can be authored and compared without
/// touching code — which is also what makes the generation metrics worth measuring.
/// </summary>
[CreateAssetMenu(fileName = "DungeonGenerationSettings", menuName = "Generation/Dungeon Settings")]
public class DungeonGenerationSettings : ScriptableObject
{
    [Header("Map")]
    [Tooltip("Map size in cells. One cell is one tile and one pathfinding node.")]
    [Min(16)] public int mapWidth = 96;
    [Min(16)] public int mapHeight = 96;

    [Header("Rooms")]
    [Tooltip("How many rooms to aim for. Rejection sampling may end up with fewer.")]
    [Min(2)] public int targetRoomCount = 12;

    [Tooltip("Generation fails validation below this; the layout is retried with a new seed.")]
    [Min(2)] public int minRoomCount = 8;

    [Min(3)] public int minRoomSize = 6;
    [Min(3)] public int maxRoomSize = 14;

    [Tooltip("Cells of solid rock kept between rooms, so two rooms never share a wall.")]
    [Min(1)] public int roomSpacing = 3;

    [Tooltip("Placement tries per room before giving up on it. Higher = denser maps, slower.")]
    [Min(1)] public int placementAttemptsPerRoom = 40;

    [Header("Hub")]
    [Tooltip("Side of the square hub room reserved at the middle of the map — the one " +
             "place in the dungeon with a save station and a crafting table. It is " +
             "placed before any other room and counts against the target room count, " +
             "and it is clamped to the ordinary room size range.")]
    [Min(3)] public int hubRoomSize = 14;

    [Header("Room shape")]
    [Tooltip("Chance a room is cut to a non-rectangular plan — an L, a T, a ring or a " +
             "cavern. A rectangle is read in one glance from the doorway; a shaped room " +
             "has to be walked. 0 = every room stays a rectangle.")]
    [Range(0f, 1f)] public float shapedRoomChance = 0.6f;

    [Tooltip("How hard the interior pass works to break a room's sightlines with pillars, " +
             "partitions and rubble. 0 = leave rooms empty. Raising it lowers the share of " +
             "a room the player can see from its doorway, which the metrics report.")]
    [Range(0f, 1f)] public float interiorDensity = 0.25f;

    [Tooltip("How hard the outline pass works on a room's walls: chamfering the corners " +
             "and pushing the odd cell of a long wall inwards, so the perimeter has the " +
             "rhythm of masonry instead of the straight edge of a selection box. Only ever " +
             "removes cells, so it can never push a room into its neighbours. 0 = leave " +
             "outlines square.")]
    [Range(0f, 1f)] public float perimeterDetail = 0.6f;

    [Tooltip("Most corridors allowed to meet the hub, or 0 for no cap. The hub sits at " +
             "the middle of the map, so the shortest-corridor rules all point at it and it " +
             "collects eight of them if left alone — a junction rather than the one room " +
             "the player is safe in. The cap also keeps its walls long enough to hang doors " +
             "on: openings crowded together are the ones left as open arches.")]
    [Min(0)] public int maxHubCorridors = 4;

    [Header("Corridors")]
    [Tooltip("Narrowest corridor, and the width used at every doorway.")]
    [Min(1)] public int corridorWidth = 1;

    [Tooltip("Widest a corridor segment may open out to. Corridors vary along their " +
             "length so some stretches are rooms to fight in and others are pinches.")]
    [Min(1)] public int maxCorridorWidth = 3;

    [Tooltip("Width every room opening is cut down to before a door is hung in it. The " +
             "door prefab is one cell wide, so an opening wider than this would get a " +
             "door the player walks straight around. Openings that cannot be narrowed " +
             "safely keep their full width and are left as open arches instead.")]
    [Range(1, 4)] public int doorwayWidth = 1;

    [Tooltip("Chance to keep a corridor that the spanning tree discarded. Loops give " +
             "escape routes, which the stealth and noise systems depend on. 0 = a pure tree.")]
    [Range(0f, 1f)] public float extraLoopChance = 0.25f;

    [Tooltip("Share of the discarded corridors that are considered as loops at all, " +
             "shortest first. Works with the chance above rather than instead of it: the " +
             "chance decides how eagerly a candidate is taken, this decides how many there " +
             "are. Raise both to make most of the map reachable by more than one route — " +
             "the Layout Preview reports that directly as \"rooms with a way round\". " +
             "Past roughly half, the pool starts offering corridors that cross the map to " +
             "join rooms that were never near each other.")]
    [Range(0f, 1f)] public float loopCandidateFraction = 0.25f;

    [Tooltip("Chance a corridor takes two turns instead of one. An L can be seen down " +
             "from its corner; a Z cannot be seen down from anywhere.")]
    [Range(0f, 1f)] public float doubleBendChance = 0.35f;

    [Tooltip("Per-cell chance of opening a blind pocket off a corridor. These are the " +
             "layout's ambush slots — somewhere the player walks past without looking in.")]
    [Range(0f, 0.25f)] public float alcoveChance = 0.04f;

    [Header("Treasure rooms")]
    [Tooltip("How many small dead-end rooms are locked behind a craftable key and stocked " +
             "with the run's rewards. One of them also holds the exit key. These are the " +
             "dungeon's decisions: the player weighs a door against a key and a detour. " +
             "0 turns them off, which also sends the exit key back to an ordinary room.")]
    [Min(0)] public int treasureRoomCount = 3;

    [Tooltip("Size range of a treasure plot. Below the ordinary minimum room size on " +
             "purpose: these are closets, and at the size of a real room a locked door " +
             "reads as the dungeon withholding a wing of itself.")]
    [Min(3)] public int minTreasureRoomSize = 4;
    [Min(3)] public int maxTreasureRoomSize = 6;

    [Tooltip("Backstop on the range above: a treasure plot that ends up with more floor " +
             "than this is demoted to an ordinary unlocked room.")]
    [Min(0)] public int treasureMaxArea = 80;

    [Header("Validation")]
    [Tooltip("Retries with a derived seed when a layout fails validation.")]
    [Min(1)] public int maxGenerationAttempts = 12;

    /// <summary>Converts the authored asset into the generator's plain input value.</summary>
    public LayoutParams ToParams()
    {
        return new LayoutParams
        {
            MapWidth = mapWidth,
            MapHeight = mapHeight,
            TargetRoomCount = targetRoomCount,
            MinRoomCount = minRoomCount,
            MinRoomSize = minRoomSize,
            MaxRoomSize = maxRoomSize,
            RoomSpacing = roomSpacing,
            PlacementAttemptsPerRoom = placementAttemptsPerRoom,
            HubRoomSize = hubRoomSize,
            MaxHubCorridors = maxHubCorridors,
            CorridorWidth = corridorWidth,
            MaxCorridorWidth = maxCorridorWidth,
            DoorwayWidth = doorwayWidth,
            ExtraLoopChance = extraLoopChance,
            LoopCandidateFraction = loopCandidateFraction,
            DoubleBendChance = doubleBendChance,
            AlcoveChance = alcoveChance,
            ShapedRoomChance = shapedRoomChance,
            InteriorDensity = interiorDensity,
            PerimeterDetail = perimeterDetail,
            TreasureRoomCount = treasureRoomCount,
            MinTreasureRoomSize = minTreasureRoomSize,
            MaxTreasureRoomSize = maxTreasureRoomSize,
            TreasureMaxArea = treasureMaxArea,
            MaxGenerationAttempts = maxGenerationAttempts
        }.Sanitized();
    }

    /// <summary>
    /// Clamps mutually inconsistent inspector values instead of failing at generation
    /// time, where the cause would be far less obvious.
    /// </summary>
    private void OnValidate()
    {
        if (maxRoomSize < minRoomSize) maxRoomSize = minRoomSize;
        if (minRoomCount > targetRoomCount) minRoomCount = targetRoomCount;
        if (maxCorridorWidth < corridorWidth) maxCorridorWidth = corridorWidth;

        // A room plus its spacing ring has to fit the map twice over, otherwise
        // rejection sampling can never reach the target count.
        int maxUsable = Mathf.Min(mapWidth, mapHeight) / 2 - roomSpacing - 1;
        if (maxUsable >= 3 && maxRoomSize > maxUsable) maxRoomSize = maxUsable;
        if (minRoomSize > maxRoomSize) minRoomSize = maxRoomSize;

        hubRoomSize = Mathf.Clamp(hubRoomSize, minRoomSize, maxRoomSize);
        if (maxTreasureRoomSize < minTreasureRoomSize) maxTreasureRoomSize = minTreasureRoomSize;
    }
}
