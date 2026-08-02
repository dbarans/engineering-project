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

    /// <summary>Hops from the start room to the farthest reachable room.</summary>
    public readonly int MaxDepth;

    /// <summary>Rooms the corridor graph never reaches; must be 0 in a valid layout.</summary>
    public readonly int UnreachableRooms;

    public DungeonMetrics(int rooms, int corridors, int loops, int deadEnds, int doorways,
        int walkableCells, int totalCells, int maxDepth, int unreachableRooms)
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
    }

    /// <summary>Share of the map that is open ground, 0..1.</summary>
    public float OpenRatio => TotalCells > 0 ? WalkableCells / (float)TotalCells : 0f;

    /// <summary>Cycles per room. Around 0.2-0.4 gives escape routes without a maze.</summary>
    public float LoopRatio => Rooms > 0 ? Loops / (float)Rooms : 0f;

    public static DungeonMetrics Measure(DungeonLayout layout)
    {
        if (layout == null) return default;

        int deadEnds = 0;
        int maxDepth = 0;
        int unreachable = 0;
        foreach (var room in layout.Rooms)
        {
            if (room.Degree <= 1) deadEnds++;

            // Unreachable rooms carry int.MaxValue and would swamp the maximum.
            if (room.DepthFromStart == int.MaxValue) unreachable++;
            else maxDepth = Mathf.Max(maxDepth, room.DepthFromStart);
        }

        int doorways = 0;
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
                if (layout[x, y] == CellType.Door) doorways++;
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
            unreachable);
    }

    /// <summary>Two-line human-readable summary for the preview window and logs.</summary>
    public string ToReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            $"rooms {Rooms}   corridors {Corridors}   loops {Loops} ({LoopRatio:F2}/room)   " +
            $"dead ends {DeadEnds}   doors {Doorways}");
        builder.Append(
            $"open cells {WalkableCells}/{TotalCells} ({OpenRatio * 100f:F1}%)   " +
            $"deepest room {MaxDepth} hops from start");

        if (UnreachableRooms > 0)
            builder.Append($"   UNREACHABLE ROOMS: {UnreachableRooms}");

        return builder.ToString();
    }
}
