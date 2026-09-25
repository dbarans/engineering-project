using System.Collections.Generic;
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

    /// <summary>
    /// Rooms reachable from the hub without crossing a corridor that is the only way
    /// through — the hub's two-edge-connected component. These are the rooms the player
    /// can reach, and leave, by more than one route.
    ///
    /// <see cref="Loops"/> counts cycles and says nothing about where they are: three loops
    /// clustered in one corner of the map leave the rest of it a tree. This counts the rooms
    /// that actually benefit, which is what the stealth and noise systems depend on — an
    /// escape route only helps if it exists where the player is standing.
    /// </summary>
    public readonly int RoomsWithTwoRoutes;

    /// <summary>
    /// Rooms deliberately built with one way in, excluded from <see cref="TwoRouteRatio"/>:
    /// a treasure closet is supposed to be a dead end, and counting it would make the map
    /// look worse the more of them are asked for.
    ///
    /// Every sampled closet, not only the ones that kept the lock. A plot demoted to an
    /// ordinary room is still held out of the corridor graph's loop edges, so it can never
    /// have a way round however the loop settings are turned up — counting it would put a
    /// ceiling on the ratio that no setting could lift.
    /// </summary>
    public readonly int SingleRouteByDesign;

    public DungeonMetrics(int rooms, int corridors, int loops, int deadEnds, int doorways,
        int walkableCells, int totalCells, int maxDepth, int unreachableRooms,
        int shapedRooms, int interiorSolids, int alcoves, int chokepointCells,
        float meanRoomVisibility, float worstRoomVisibility,
        int roomsWithTwoRoutes, int singleRouteByDesign)
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
        RoomsWithTwoRoutes = roomsWithTwoRoutes;
        SingleRouteByDesign = singleRouteByDesign;
    }

    /// <summary>Share of the map that is open ground, 0..1.</summary>
    public float OpenRatio => TotalCells > 0 ? WalkableCells / (float)TotalCells : 0f;

    /// <summary>Cycles per room. Around 0.2-0.4 gives escape routes without a maze.</summary>
    public float LoopRatio => Rooms > 0 ? Loops / (float)Rooms : 0f;

    /// <summary>
    /// Share of the rooms that are meant to have a way round, 0..1, that do — the headline
    /// number for "can the player get out of here another way". Below about 0.5 the map is
    /// mostly a tree and a fight met in a corridor is a fight to the end; raise
    /// <c>extraLoopChance</c> or <c>loopCandidateFraction</c> to lift it.
    /// </summary>
    public float TwoRouteRatio
    {
        get
        {
            int eligible = Rooms - SingleRouteByDesign;
            return eligible > 0 ? RoomsWithTwoRoutes / (float)eligible : 0f;
        }
    }

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

        int byDesign = 0;
        foreach (var room in layout.Rooms)
            if (room.IsTreasurePlot) byDesign++;

        int twoRoutes = CountRoomsWithTwoRoutes(layout);

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
            measured > 0 ? worstVisibility : 0f,
            twoRoutes,
            byDesign);
    }

    /// <summary>
    /// Rooms in the hub's two-edge-connected component: reachable from the hub without
    /// crossing a bridge — a corridor whose loss would disconnect the graph.
    ///
    /// Bridges are found by the standard depth-first low-link scan; the hub's component is
    /// then flooded over the edges that are left. The hub itself counts, because the player
    /// is standing in it, but only when it has a way round of its own: on a pure tree every
    /// edge is a bridge, the flood reaches one room, and the answer reported is 0. "How many
    /// rooms have an alternative" is the question, and standing alone in a tree is not one.
    ///
    /// Iterative rather than recursive. A 30-room graph would not overflow a stack, but the
    /// depth is a property of the settings, and settings are meant to be tunable without
    /// anyone having to think about the call depth they imply.
    /// </summary>
    private static int CountRoomsWithTwoRoutes(DungeonLayout layout)
    {
        int rooms = layout.Rooms.Count;
        if (rooms == 0) return 0;

        var adjacency = new List<(int room, int edge)>[rooms];
        for (int i = 0; i < rooms; i++) adjacency[i] = new List<(int, int)>();

        for (int e = 0; e < layout.Links.Count; e++)
        {
            RoomLink link = layout.Links[e];
            if (link.RoomA < 0 || link.RoomA >= rooms) continue;
            if (link.RoomB < 0 || link.RoomB >= rooms) continue;

            adjacency[link.RoomA].Add((link.RoomB, e));
            adjacency[link.RoomB].Add((link.RoomA, e));
        }

        HashSet<int> bridges = FindBridges(adjacency, rooms);

        int hub = 0;
        for (int i = 0; i < rooms; i++)
        {
            if (layout.Rooms[i].Kind == RoomKind.Hub) { hub = i; break; }
        }

        var seen = new bool[rooms];
        var pending = new Queue<int>();
        seen[hub] = true;
        pending.Enqueue(hub);
        int reached = 0;

        while (pending.Count > 0)
        {
            int room = pending.Dequeue();
            reached++;

            foreach (var (neighbour, edge) in adjacency[room])
            {
                if (bridges.Contains(edge) || seen[neighbour]) continue;
                seen[neighbour] = true;
                pending.Enqueue(neighbour);
            }
        }

        return reached > 1 ? reached : 0;
    }

    /// <summary>
    /// Indices of the links that are bridges: an edge is one when nothing in the subtree
    /// below it reaches back past its parent. Plain iterative Tarjan, one entry on the
    /// explicit stack per room, resumed at the neighbour it had got to.
    /// </summary>
    private static HashSet<int> FindBridges(List<(int room, int edge)>[] adjacency, int rooms)
    {
        var bridges = new HashSet<int>();
        var discovery = new int[rooms];
        var low = new int[rooms];
        var parentEdge = new int[rooms];
        var nextNeighbour = new int[rooms];

        for (int i = 0; i < rooms; i++) discovery[i] = -1;

        int timer = 0;
        var stack = new Stack<int>();

        for (int root = 0; root < rooms; root++)
        {
            if (discovery[root] >= 0) continue;

            discovery[root] = low[root] = timer++;
            parentEdge[root] = -1;
            nextNeighbour[root] = 0;
            stack.Push(root);

            while (stack.Count > 0)
            {
                int room = stack.Peek();

                if (nextNeighbour[room] < adjacency[room].Count)
                {
                    var (neighbour, edge) = adjacency[room][nextNeighbour[room]++];

                    // The edge walked in on, not merely the room walked in from: two rooms
                    // joined by a pair of corridors are two ways round, and skipping by room
                    // would call both of them the same way back.
                    if (edge == parentEdge[room]) continue;

                    if (discovery[neighbour] >= 0)
                    {
                        low[room] = Mathf.Min(low[room], discovery[neighbour]);
                        continue;
                    }

                    discovery[neighbour] = low[neighbour] = timer++;
                    parentEdge[neighbour] = edge;
                    nextNeighbour[neighbour] = 0;
                    stack.Push(neighbour);
                    continue;
                }

                stack.Pop();
                if (stack.Count == 0) continue;

                int parent = stack.Peek();
                low[parent] = Mathf.Min(low[parent], low[room]);
                if (low[room] > discovery[parent]) bridges.Add(parentEdge[room]);
            }
        }

        return bridges;
    }

    /// <summary>Human-readable summary for the preview window and logs.</summary>
    public string ToReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            $"rooms {Rooms}   corridors {Corridors}   loops {Loops} ({LoopRatio:F2}/room)   " +
            $"dead ends {DeadEnds}   doors {Doorways}");
        builder.AppendLine(
            $"rooms with a way round {RoomsWithTwoRoutes}/{Rooms - SingleRouteByDesign} " +
            $"({TwoRouteRatio * 100f:F0}%)   one way in by design {SingleRouteByDesign}");
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
