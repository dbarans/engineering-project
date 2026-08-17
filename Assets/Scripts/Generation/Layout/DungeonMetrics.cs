using System.Text;
using UnityEngine;

/// <summary>
/// Measurements of a generated layout.
///
/// Exists because "the dungeons look better now" is not a claim that can be checked.
/// These numbers make a parameter change comparable across a batch of seeds, and they
/// are the figures worth reporting when the generator is written up.
/// </summary>
public readonly struct DungeonMetrics
{
    public readonly int Rooms;
    public readonly int Corridors;

    /// <summary>Corridors beyond a spanning tree — that is, the number of cycles.</summary>
    public readonly int Loops;

    /// <summary>Rooms with a single corridor. Good for camps, bad in quantity.</summary>
    public readonly int DeadEnds;

    public readonly int Doorways;
    public readonly int WalkableCells;
    public readonly int TotalCells;

    /// <summary>Hops from the hub to the farthest reachable room.</summary>
    public readonly int MaxDepth;

    /// <summary>Rooms the corridor graph never reaches; must be 0 in a valid layout.</summary>
    public readonly int UnreachableRooms;

    /// <summary>Rooms that were cut to something other than a plain rectangle.</summary>
    public readonly int ShapedRooms;

    /// <summary>Pillar and rubble cells placed inside rooms by the interior pass.</summary>
    public readonly int InteriorSolids;

    /// <summary>Blind pockets opened off corridor sides.</summary>
    public readonly int Alcoves;

    /// <summary>
    /// Walkable cells whose removal would split the dungeon. Some are wanted — they are
    /// where a fight becomes unavoidable — but a high count means there is nowhere to run.
    /// </summary>
    public readonly int ChokepointCells;

    /// <summary>
    /// Mean share of a room visible from its own doorways, 0..1, over every room.
    ///
    /// The headline number for how oppressive the dungeon reads. Empty rectangles sit
    /// near 0.95: the room gives itself away on the first step in and holds nothing back.
    /// Shaping and interior structure pull it down, and the lower it goes the more of the
    /// dungeon has to be learned by walking into it.
    /// </summary>
    public readonly float MeanRoomVisibility;

    /// <summary>The most exposed room on the map — the weakest link, not the average.</summary>
    public readonly float WorstRoomVisibility;

    public DungeonMetrics(int rooms, int corridors, int loops, int deadEnds, int doorways,
        int walkableCells, int totalCells, int maxDepth, int unreachableRooms,
        int shapedRooms, int interiorSolids, int alcoves, int chokepointCells,
        float meanRoomVisibility, float worstRoomVisibility)
    {
        Rooms = rooms;
        Corridors = corridors;
        Loops = loops;
        DeadEnds = deadEnds;
        Doorways = doorways;
        WalkableCells = walkableCells;
        TotalCells = totalCells;
        MaxDepth = maxDepth;
        UnreachableRooms = unreachableRooms;
        ShapedRooms = shapedRooms;
        InteriorSolids = interiorSolids;
        Alcoves = alcoves;
        ChokepointCells = chokepointCells;
        MeanRoomVisibility = meanRoomVisibility;
        WorstRoomVisibility = worstRoomVisibility;
    }

    /// <summary>Share of the map that is open ground, 0..1.</summary>
    public float OpenRatio => TotalCells > 0 ? WalkableCells / (float)TotalCells : 0f;

    /// <summary>Cycles per room. Around 0.2-0.4 gives escape routes without a maze.</summary>
    public float LoopRatio => Rooms > 0 ? Loops / (float)Rooms : 0f;

    /// <summary>Share of rooms that are not plain rectangles.</summary>
    public float ShapedRatio => Rooms > 0 ? ShapedRooms / (float)Rooms : 0f;

    /// <summary>
    /// Sight radius the visibility figures are measured at, in cells. Matches the
    /// player's default view radius — measured without a limit, every large room would
    /// score as unreadable no matter what is in it, and the number would say nothing.
    /// </summary>
    private const int SightRadius = 10;

    public static DungeonMetrics Measure(DungeonLayout layout)
    {
        if (layout == null) return default;

        int deadEnds = 0;
        int maxDepth = 0;
        int unreachable = 0;
        int shaped = 0;
        float visibilityTotal = 0f;
        float worstVisibility = 1f;
        int measured = 0;

        foreach (var room in layout.Rooms)
        {
            if (room.Degree <= 1) deadEnds++;
            if (room.Shape != RoomShape.Rectangle) shaped++;

            // Unreachable rooms carry int.MaxValue and would swamp the maximum.
            if (room.DepthFromHub == int.MaxValue) unreachable++;
            else maxDepth = Mathf.Max(maxDepth, room.DepthFromHub);

            float visible = VisibilityAnalysis.VisibleFractionFromEntrances(layout, room, SightRadius);
            visibilityTotal += visible;
            worstVisibility = Mathf.Min(worstVisibility, visible);
            measured++;
        }

        int doorways = 0;
        int interiorSolids = 0;
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                CellType cell = layout[x, y];
                if (cell == CellType.Door) doorways++;
                else if (cell == CellType.Pillar || cell == CellType.Rubble) interiorSolids++;
            }
        }

        int loops = Mathf.Max(0, layout.Links.Count - Mathf.Max(0, layout.Rooms.Count - 1));

        return new DungeonMetrics(
            layout.Rooms.Count,
            layout.Links.Count,
            loops,
            deadEnds,
            doorways,
            layout.CountWalkable(),
            layout.Width * layout.Height,
            maxDepth,
            unreachable,
            shaped,
            interiorSolids,
            layout.Alcoves.Count,
            layout.Chokepoints.Count,
            measured > 0 ? visibilityTotal / measured : 0f,
            measured > 0 ? worstVisibility : 0f);
    }

    /// <summary>Human-readable summary for the preview window and logs.</summary>
    public string ToReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            $"rooms {Rooms}   corridors {Corridors}   loops {Loops} ({LoopRatio:F2}/room)   " +
            $"dead ends {DeadEnds}   doors {Doorways}");
        builder.AppendLine(
            $"open cells {WalkableCells}/{TotalCells} ({OpenRatio * 100f:F1}%)   " +
            $"deepest room {MaxDepth} hops from the hub");
        builder.AppendLine(
            $"shaped rooms {ShapedRooms}/{Rooms} ({ShapedRatio * 100f:F0}%)   " +
            $"interior solids {InteriorSolids}   alcoves {Alcoves}   chokepoints {ChokepointCells}");
        builder.Append(
            $"room visibility mean {MeanRoomVisibility:F2}   worst {WorstRoomVisibility:F2}   " +
            "(1.00 = the room is read in one glance)");

        if (UnreachableRooms > 0)
            builder.Append($"   UNREACHABLE ROOMS: {UnreachableRooms}");

        return builder.ToString();
    }
}
