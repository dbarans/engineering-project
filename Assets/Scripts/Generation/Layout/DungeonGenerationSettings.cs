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

    [Header("Corridors")]
    [Min(1)] public int corridorWidth = 1;

    [Tooltip("Chance to keep a corridor that the spanning tree discarded. Loops give " +
             "escape routes, which the stealth and noise systems depend on. 0 = a pure tree.")]
    [Range(0f, 1f)] public float extraLoopChance = 0.25f;

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
            CorridorWidth = corridorWidth,
            ExtraLoopChance = extraLoopChance,
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

        // A room plus its spacing ring has to fit the map twice over, otherwise
        // rejection sampling can never reach the target count.
        int maxUsable = Mathf.Min(mapWidth, mapHeight) / 2 - roomSpacing - 1;
        if (maxUsable >= 3 && maxRoomSize > maxUsable) maxRoomSize = maxUsable;
        if (minRoomSize > maxRoomSize) minRoomSize = maxRoomSize;
    }
}
