using UnityEngine;

/// <summary>
/// Plain value carrying everything <see cref="RoomCorridorGenerator"/> needs.
///
/// Separate from <see cref="DungeonGenerationSettings"/> on purpose: the asset is the
/// authoring surface, this struct is the input. Because it is not a
/// <c>ScriptableObject</c>, the generator can be exercised entirely outside the Unity
/// runtime — which is what makes the layout unit-testable.
/// </summary>
public struct LayoutParams
{
    public int MapWidth;
    public int MapHeight;

    public int TargetRoomCount;
    public int MinRoomCount;
    public int MinRoomSize;
    public int MaxRoomSize;
    public int RoomSpacing;
    public int PlacementAttemptsPerRoom;

    /// <summary>Side of the square hub room reserved at the middle of the map.</summary>
    public int HubRoomSize;

    /// <summary>
    /// Most corridors allowed to meet the hub. 0 lifts the cap.
    ///
    /// The hub is the middle of the map by construction, so every shortest-edge rule in
    /// the generator points at it: left alone it collects eight corridors and reads as a
    /// junction rather than as the one room the player is safe in. Capping it is also what
    /// keeps its walls long enough to hang doors on — openings crowded together are the
    /// ones the doorway pass has to leave as open arches.
    /// </summary>
    public int MaxHubCorridors;

    public int CorridorWidth;
    public int MaxCorridorWidth;
    public int DoorwayWidth;
    public float ExtraLoopChance;

    /// <summary>
    /// Share of the corridors the spanning tree rejected that are even considered as loops,
    /// shortest first. <see cref="ExtraLoopChance"/> is then rolled against each of them, so
    /// the two multiply: the chance says how eagerly a candidate is taken, this says how
    /// many there are to take.
    ///
    /// Kept separate because they fail differently. Raising only the chance saturates — once
    /// it is 1 every candidate in the pool is already taken and the map cannot get any more
    /// connected — while raising only the pool starts admitting corridors that cross half
    /// the map to join two rooms that were never near each other.
    /// </summary>
    public float LoopCandidateFraction;
    public float DoubleBendChance;
    public float AlcoveChance;

    public float ShapedRoomChance;
    public float InteriorDensity;
    public float PerimeterDetail;

    /// <summary>
    /// How many rooms are tagged <see cref="RoomKind.Treasure"/> — the small dead ends
    /// locked behind a craftable key, one of which holds the exit key. 0 turns them off
    /// entirely, which also means the exit key falls back to an ordinary room.
    /// </summary>
    public int TreasureRoomCount;

    /// <summary>Side range of a treasure plot. Deliberately below
    /// <see cref="MinRoomSize"/>: these are closets somebody sealed, and at the size of an
    /// ordinary room a locked door reads as the dungeon withholding a wing of itself.</summary>
    public int MinTreasureRoomSize;
    public int MaxTreasureRoomSize;

    /// <summary>Largest floor area a treasure room may end up with and still stay locked.
    /// A backstop on the size range above rather than a knob to tune — a plot that somehow
    /// came out larger than a closet is demoted to an ordinary room instead of being
    /// locked.</summary>
    public int TreasureMaxArea;

    public int MaxGenerationAttempts;

    /// <summary>Values matching the defaults on a fresh settings asset.</summary>
    public static LayoutParams Default => new LayoutParams
    {
        MapWidth = 96,
        MapHeight = 96,
        TargetRoomCount = 12,
        MinRoomCount = 8,
        MinRoomSize = 6,
        MaxRoomSize = 14,
        RoomSpacing = 3,
        PlacementAttemptsPerRoom = 40,
        HubRoomSize = 14,
        MaxHubCorridors = 4,
        CorridorWidth = 1,
        MaxCorridorWidth = 3,
        DoorwayWidth = 1,
        ExtraLoopChance = 0.25f,
        LoopCandidateFraction = 0.25f,
        DoubleBendChance = 0.35f,
        AlcoveChance = 0.04f,
        ShapedRoomChance = 0.6f,
        InteriorDensity = 0.25f,
        PerimeterDetail = 0.6f,
        TreasureRoomCount = 3,
        MinTreasureRoomSize = 4,
        MaxTreasureRoomSize = 6,
        TreasureMaxArea = 80,
        MaxGenerationAttempts = 12
    };

    /// <summary>
    /// Forces the values into a self-consistent range. Generation reads these in inner
    /// loops, so it validates once here rather than defending on every use.
    /// </summary>
    public LayoutParams Sanitized()
    {
        var p = this;
        p.MapWidth = Mathf.Max(16, p.MapWidth);
        p.MapHeight = Mathf.Max(16, p.MapHeight);
        p.MinRoomSize = Mathf.Max(3, p.MinRoomSize);
        p.MaxRoomSize = Mathf.Max(p.MinRoomSize, p.MaxRoomSize);
        p.RoomSpacing = Mathf.Max(1, p.RoomSpacing);
        p.TargetRoomCount = Mathf.Max(2, p.TargetRoomCount);
        p.MinRoomCount = Mathf.Clamp(p.MinRoomCount, 2, p.TargetRoomCount);
        p.PlacementAttemptsPerRoom = Mathf.Max(1, p.PlacementAttemptsPerRoom);
        p.CorridorWidth = Mathf.Max(1, p.CorridorWidth);
        p.MaxCorridorWidth = Mathf.Max(p.CorridorWidth, p.MaxCorridorWidth);

        // One door prefab is spawned per doorway cell, so this is also how many doors end
        // up side by side in one opening. Past a few it stops reading as a door and starts
        // reading as a fence.
        p.DoorwayWidth = Mathf.Clamp(p.DoorwayWidth, 1, 4);
        p.ExtraLoopChance = Mathf.Clamp01(p.ExtraLoopChance);
        p.LoopCandidateFraction = Mathf.Clamp01(p.LoopCandidateFraction);
        p.DoubleBendChance = Mathf.Clamp01(p.DoubleBendChance);
        p.ShapedRoomChance = Mathf.Clamp01(p.ShapedRoomChance);
        p.InteriorDensity = Mathf.Clamp01(p.InteriorDensity);
        p.PerimeterDetail = Mathf.Clamp01(p.PerimeterDetail);
        p.MaxGenerationAttempts = Mathf.Max(1, p.MaxGenerationAttempts);
        p.TreasureRoomCount = Mathf.Max(0, p.TreasureRoomCount);
        p.MinTreasureRoomSize = Mathf.Max(3, p.MinTreasureRoomSize);
        p.MaxTreasureRoomSize = Mathf.Max(p.MinTreasureRoomSize, p.MaxTreasureRoomSize);
        p.TreasureMaxArea = Mathf.Max(0, p.TreasureMaxArea);

        // Rolled per corridor cell, so even a modest value covers a map in pockets.
        p.AlcoveChance = Mathf.Clamp(p.AlcoveChance, 0f, 0.25f);

        // A room must leave room for its spacing ring and the one-cell map border.
        int usable = Mathf.Min(p.MapWidth, p.MapHeight) - 2 * (p.RoomSpacing + 1);
        if (usable >= 3) p.MaxRoomSize = Mathf.Min(p.MaxRoomSize, usable);
        p.MinRoomSize = Mathf.Min(p.MinRoomSize, p.MaxRoomSize);

        // Held to the same range as a sampled room, and clamped after MaxRoomSize has
        // been fitted to the map, so the reserved centre can never be a room shape the
        // rest of the generator would have rejected.
        p.HubRoomSize = Mathf.Clamp(p.HubRoomSize, p.MinRoomSize, p.MaxRoomSize);
        p.MaxHubCorridors = Mathf.Max(0, p.MaxHubCorridors);
        return p;
    }
}
