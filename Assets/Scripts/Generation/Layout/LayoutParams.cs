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

    public int CorridorWidth;
    public int MaxCorridorWidth;
    public int DoorwayWidth;
    public float ExtraLoopChance;
    public float DoubleBendChance;
    public float AlcoveChance;

    public float ShapedRoomChance;
    public float InteriorDensity;
    public float PerimeterDetail;

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
        CorridorWidth = 1,
        MaxCorridorWidth = 3,
        DoorwayWidth = 1,
        ExtraLoopChance = 0.25f,
        DoubleBendChance = 0.35f,
        AlcoveChance = 0.04f,
        ShapedRoomChance = 0.6f,
        InteriorDensity = 0.5f,
        PerimeterDetail = 0.6f,
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
        p.DoubleBendChance = Mathf.Clamp01(p.DoubleBendChance);
        p.ShapedRoomChance = Mathf.Clamp01(p.ShapedRoomChance);
        p.InteriorDensity = Mathf.Clamp01(p.InteriorDensity);
        p.PerimeterDetail = Mathf.Clamp01(p.PerimeterDetail);
        p.MaxGenerationAttempts = Mathf.Max(1, p.MaxGenerationAttempts);

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
        return p;
    }
}
