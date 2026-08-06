using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Classic rooms-and-corridors generator, extended with loops.
///
/// Pipeline: place non-overlapping rooms by rejection sampling, connect them with a
/// minimum spanning tree over their centres so the dungeon is guaranteed traversable,
/// then add back a fraction of the discarded edges so the layout contains cycles.
/// The cycles are the point: a tree dungeon has exactly one route between any two
/// rooms, which makes stealth pointless because a chasing enemy can never be shaken.
///
/// Every random decision comes from <see cref="DeterministicRandom"/>, so a seed fully
/// determines the result.
/// </summary>
public sealed class RoomCorridorGenerator : IDungeonLayoutGenerator
{
    /// <summary>
    /// Why the last <see cref="Generate"/> call failed validation, or null when it
    /// succeeded. Reported rather than logged: this assembly stays free of engine
    /// side effects so it can run inside plain unit tests, and the caller — which knows
    /// the scene context — decides how loudly to complain.
    /// </summary>
    public string LastFailureReason { get; private set; }

    /// <summary>False when every attempt failed and the best effort was returned.</summary>
    public bool LastGenerationSucceeded { get; private set; }

    /// <summary>Attempts the last <see cref="Generate"/> call needed; 1 means first try.</summary>
    public int LastAttemptCount { get; private set; }

    public DungeonLayout Generate(string seed, LayoutParams parameters)
    {
        LayoutParams p = parameters.Sanitized();
        seed ??= string.Empty;

        DungeonLayout best = null;
        int bestRoomCount = -1;
        LastFailureReason = null;
        LastGenerationSucceeded = false;

        for (int attempt = 0; attempt < p.MaxGenerationAttempts; attempt++)
        {
            LastAttemptCount = attempt + 1;

            // Each attempt derives its own stream rather than continuing the previous
            // one, so attempt N is reproducible without replaying attempts 0..N-1.
            var random = new DeterministicRandom(seed).Derive($"attempt{attempt}");
            DungeonLayout layout = BuildOnce(seed, p, random);

            if (Validate(layout, p, out string failure))
            {
                LastGenerationSucceeded = true;
                return layout;
            }

            LastFailureReason = failure;
            if (layout.Rooms.Count > bestRoomCount)
            {
                bestRoomCount = layout.Rooms.Count;
                best = layout;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------- pipeline

    private static DungeonLayout BuildOnce(string seed, LayoutParams p, DeterministicRandom random)
    {
        List<Room> rooms = PlaceRooms(p, random.Derive("rooms"));
        List<RoomLink> links = ConnectRooms(rooms, p, random.Derive("links"));

        var layout = new DungeonLayout(seed, p.MapWidth, p.MapHeight, rooms, links);

        CarveRooms(layout, rooms);
        CorridorCarver.CarveAll(layout, rooms, links, p, random.Derive("corridors"));
        MarkDoors(layout, rooms);

        // Roles are assigned before the interior pass because that pass reads them: the
        // start room and the camp are deliberately left legible, and it cannot know which
        // they are until the graph has been walked.
        AssignRoomRoles(layout, rooms, links);

        RoomInteriorDecorator.Decorate(layout, p, random.Derive("interiors"));
        Chokepoints.Detect(layout);

        return layout;
    }

    /// <summary>
    /// Rejection sampling: propose a rectangle, keep it when it clears every existing
    /// room by <see cref="LayoutParams.RoomSpacing"/>. Simple, and unlike BSP it leaves
    /// solid rock between rooms, which is what makes corridors read as corridors.
    /// </summary>
    private static List<Room> PlaceRooms(LayoutParams p, DeterministicRandom random)
    {
        var rooms = new List<Room>(p.TargetRoomCount);

        for (int i = 0; i < p.TargetRoomCount; i++)
        {
            for (int attempt = 0; attempt < p.PlacementAttemptsPerRoom; attempt++)
            {
                int width = random.RangeInclusive(p.MinRoomSize, p.MaxRoomSize);
                int height = random.RangeInclusive(p.MinRoomSize, p.MaxRoomSize);

                // The one-cell inset keeps a solid border around the whole map, so no
                // room ever opens onto the void.
                int maxX = p.MapWidth - width - 1;
                int maxY = p.MapHeight - height - 1;
                if (maxX < 1 || maxY < 1) continue;

                var bounds = new RectInt(
                    random.RangeInclusive(1, maxX),
                    random.RangeInclusive(1, maxY),
                    width, height);

                if (Overlaps(rooms, bounds, p.RoomSpacing)) continue;

                rooms.Add(BuildRoom(rooms.Count, bounds, p, random));
                break;
            }
        }

        return rooms;
    }

    /// <summary>
    /// Turns an accepted plot into a room, cut to a non-rectangular plan when the
    /// settings ask for it.
    ///
    /// Spacing was already checked against the full plot, so carving can only ever move
    /// the room's cells further from its neighbours — a shaped room never invalidates a
    /// placement decision that was made before it.
    /// </summary>
    private static Room BuildRoom(int index, RectInt plot, LayoutParams p, DeterministicRandom random)
    {
        if (!random.Chance(p.ShapedRoomChance)) return new Room(index, plot);

        RoomShape shape = RoomShaper.PickShape(plot, random);
        if (shape == RoomShape.Rectangle) return new Room(index, plot);

        List<Vector2Int> cells = RoomShaper.Shape(plot, shape, random);

        // A degenerate carve falls back to the plain rectangle. A room that failed to
        // become interesting is still a room; dropping it would leave a gap in the map.
        return cells != null ? new Room(index, cells, shape) : new Room(index, plot);
    }

    private static bool Overlaps(List<Room> rooms, RectInt candidate, int spacing)
    {
        foreach (var room in rooms)
        {
            var inflated = new RectInt(
                room.Bounds.xMin - spacing,
                room.Bounds.yMin - spacing,
                room.Bounds.width + 2 * spacing,
                room.Bounds.height + 2 * spacing);

            bool separated =
                candidate.xMin >= inflated.xMax || candidate.xMax <= inflated.xMin ||
                candidate.yMin >= inflated.yMax || candidate.yMax <= inflated.yMin;

            if (!separated) return true;
        }
        return false;
    }

    /// <summary>
    /// Kruskal's minimum spanning tree over room-centre distances, plus a random
    /// fraction of the rejected edges. Only the shortest edges are considered for
    /// loops, otherwise a "loop" can be a corridor crossing the entire map.
    /// </summary>
    private static List<RoomLink> ConnectRooms(List<Room> rooms, LayoutParams p, DeterministicRandom random)
    {
        var links = new List<RoomLink>();
        if (rooms.Count < 2) return links;

        var candidates = new List<(int a, int b, int distance)>();
        for (int a = 0; a < rooms.Count; a++)
        {
            for (int b = a + 1; b < rooms.Count; b++)
            {
                Vector2Int delta = rooms[a].Center - rooms[b].Center;
                candidates.Add((a, b, Mathf.Abs(delta.x) + Mathf.Abs(delta.y)));
            }
        }

        // Ties broken by index so the sort is stable regardless of List.Sort internals —
        // an unstable comparison here would quietly break determinism.
        candidates.Sort((left, right) =>
        {
            int byDistance = left.distance.CompareTo(right.distance);
            if (byDistance != 0) return byDistance;
            int byA = left.a.CompareTo(right.a);
            return byA != 0 ? byA : left.b.CompareTo(right.b);
        });

        var union = new UnionFind(rooms.Count);
        var rejected = new List<(int a, int b, int distance)>();

        foreach (var edge in candidates)
        {
            if (union.Union(edge.a, edge.b))
                links.Add(new RoomLink(edge.a, edge.b));
            else
                rejected.Add(edge);
        }

        // Loop candidates: the shortest quarter of the rejected edges.
        int loopPool = Mathf.Max(1, rejected.Count / 4);
        for (int i = 0; i < loopPool; i++)
        {
            if (random.Chance(p.ExtraLoopChance))
                links.Add(new RoomLink(rejected[i].a, rejected[i].b));
        }

        return links;
    }

    private static void CarveRooms(DungeonLayout layout, List<Room> rooms)
    {
        foreach (var room in rooms)
        {
            foreach (Vector2Int cell in room.Cells)
                layout[cell] = CellType.Floor;
        }
    }

    /// <summary>
    /// A doorway is the corridor cell in a room's opening. Marking the corridor side
    /// rather than the room side puts the door prefab in the gap, not inside the room.
    ///
    /// Openings are found by walking the room's border — the cells just outside it —
    /// and grouping the open ones into connected clumps, one door per clump. Grouping
    /// matters: a corridor running alongside a room would otherwise mark its whole
    /// length and produce a row of doors where there is really one wide opening.
    ///
    /// Phrased against the room's cells rather than the four sides of its bounding box,
    /// because a room is no longer necessarily a rectangle and an L-shaped one has
    /// border cells inside its own bounding box.
    /// </summary>
    private static void MarkDoors(DungeonLayout layout, List<Room> rooms)
    {
        var candidates = new List<Vector2Int>();
        var candidateSet = new HashSet<Vector2Int>();

        foreach (var room in rooms)
        {
            candidates.Clear();
            candidateSet.Clear();

            foreach (Vector2Int cell in room.Cells)
            {
                for (int i = 0; i < Neighbours.Length; i++)
                {
                    Vector2Int next = cell + Neighbours[i];
                    if (room.Contains(next) || !candidateSet.Add(next)) continue;

                    if (IsDoorCandidate(layout, rooms, next)) candidates.Add(next);
                    else candidateSet.Remove(next);
                }
            }

            MarkOpeningCentres(layout, candidates, candidateSet);
        }
    }

    /// <summary>
    /// Splits the border cells into connected openings and marks the middle of each.
    /// Iteration follows the candidate list rather than the set, so the result cannot
    /// depend on hash ordering.
    /// </summary>
    private static void MarkOpeningCentres(DungeonLayout layout,
        List<Vector2Int> candidates, HashSet<Vector2Int> remaining)
    {
        var opening = new List<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        foreach (Vector2Int start in candidates)
        {
            if (!remaining.Remove(start)) continue;

            opening.Clear();
            queue.Clear();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                opening.Add(cell);

                for (int i = 0; i < Neighbours.Length; i++)
                {
                    Vector2Int next = cell + Neighbours[i];
                    if (remaining.Remove(next)) queue.Enqueue(next);
                }
            }

            layout[Medoid(opening)] = CellType.Door;
        }
    }

    /// <summary>The cell of a clump nearest its own centre, ties broken by coordinate.</summary>
    private static Vector2Int Medoid(List<Vector2Int> cells)
    {
        long sumX = 0, sumY = 0;
        foreach (Vector2Int cell in cells)
        {
            sumX += cell.x;
            sumY += cell.y;
        }

        var centre = new Vector2Int(
            Mathf.RoundToInt(sumX / (float)cells.Count),
            Mathf.RoundToInt(sumY / (float)cells.Count));

        Vector2Int best = cells[0];
        int bestDistance = int.MaxValue;

        foreach (Vector2Int cell in cells)
        {
            int distance = Mathf.Abs(cell.x - centre.x) + Mathf.Abs(cell.y - centre.y);
            if (distance > bestDistance) continue;

            if (distance < bestDistance ||
                cell.y < best.y || (cell.y == best.y && cell.x < best.x))
            {
                bestDistance = distance;
                best = cell;
            }
        }

        return best;
    }

    private static bool IsDoorCandidate(DungeonLayout layout, List<Room> rooms, Vector2Int cell)
    {
        if (!layout.IsWalkable(cell)) return false;

        // Corridors can clip a neighbouring room when two rooms nearly touch; a cell
        // inside any room is never a doorway.
        foreach (var room in rooms)
        {
            if (room.Contains(cell)) return false;
        }
        return true;
    }

    /// <summary>
    /// Tags rooms and computes their depth. Start is the room farthest from the map
    /// centre (it reads as an entrance rather than as "the middle"), Treasure is the
    /// room farthest from Start over the graph, and Camp is the deepest dead end that
    /// is neither — a dead end because a safe room the player can be chased through
    /// is not a safe room.
    /// </summary>
    private static void AssignRoomRoles(DungeonLayout layout, List<Room> rooms, List<RoomLink> links)
    {
        if (rooms.Count == 0) return;

        var adjacency = BuildAdjacency(rooms.Count, links);
        for (int i = 0; i < rooms.Count; i++)
        {
            rooms[i].Kind = RoomKind.Normal;
            rooms[i].Degree = adjacency[i].Count;
        }

        var center = new Vector2Int(layout.Width / 2, layout.Height / 2);
        Room start = rooms[0];
        int bestDistance = -1;
        foreach (var room in rooms)
        {
            Vector2Int delta = room.Center - center;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                start = room;
            }
        }

        start.Kind = RoomKind.Start;
        layout.SpawnCell = start.Center;

        int[] depths = BreadthFirstDepths(adjacency, start.Index);
        for (int i = 0; i < rooms.Count; i++)
            rooms[i].DepthFromStart = depths[i];

        Room treasure = null;
        int maxDepth = 0;
        foreach (var room in rooms)
        {
            if (room.Kind == RoomKind.Start) continue;
            if (room.DepthFromStart > maxDepth)
            {
                maxDepth = room.DepthFromStart;
                treasure = room;
            }
        }
        if (treasure != null) treasure.Kind = RoomKind.Treasure;

        Room camp = null;
        foreach (var room in rooms)
        {
            if (room.Kind != RoomKind.Normal) continue;
            if (camp == null || IsBetterCamp(room, camp)) camp = room;
        }
        if (camp != null) camp.Kind = RoomKind.Camp;
    }

    /// <summary>
    /// Dead ends beat through-rooms, and among equals the deeper room wins: a safe room
    /// the player can be chased straight through is not safe, and one right next to the
    /// entrance is not worth reaching.
    /// </summary>
    private static bool IsBetterCamp(Room candidate, Room current)
    {
        bool candidateIsDeadEnd = candidate.Degree <= 1;
        bool currentIsDeadEnd = current.Degree <= 1;

        if (candidateIsDeadEnd != currentIsDeadEnd) return candidateIsDeadEnd;
        return candidate.DepthFromStart > current.DepthFromStart;
    }

    private static List<int>[] BuildAdjacency(int roomCount, List<RoomLink> links)
    {
        var adjacency = new List<int>[roomCount];
        for (int i = 0; i < roomCount; i++) adjacency[i] = new List<int>();

        foreach (var link in links)
        {
            adjacency[link.RoomA].Add(link.RoomB);
            adjacency[link.RoomB].Add(link.RoomA);
        }
        return adjacency;
    }

    private static int[] BreadthFirstDepths(List<int>[] adjacency, int origin)
    {
        var depths = new int[adjacency.Length];
        for (int i = 0; i < depths.Length; i++) depths[i] = -1;

        var queue = new Queue<int>();
        depths[origin] = 0;
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int next in adjacency[current])
            {
                if (depths[next] != -1) continue;
                depths[next] = depths[current] + 1;
                queue.Enqueue(next);
            }
        }

        // Unreachable rooms would otherwise stay at -1 and read as "closer than Start".
        for (int i = 0; i < depths.Length; i++)
            if (depths[i] < 0) depths[i] = int.MaxValue;

        return depths;
    }

    // ---------------------------------------------------------------- validation

    /// <summary>
    /// Rejects layouts that would produce a broken run: too few rooms, or any walkable
    /// cell the player cannot reach. The flood fill is the important one — a corridor
    /// carved into an isolated pocket is invisible on a screenshot but fatal in play.
    /// </summary>
    public static bool Validate(DungeonLayout layout, LayoutParams parameters, out string failure)
    {
        if (layout == null)
        {
            failure = "layout is null";
            return false;
        }

        if (layout.Rooms.Count < parameters.MinRoomCount)
        {
            failure = $"only {layout.Rooms.Count} rooms placed, {parameters.MinRoomCount} required";
            return false;
        }

        int reachable = CountReachable(layout, layout.SpawnCell);
        int walkable = layout.CountWalkable();
        if (reachable != walkable)
        {
            failure = $"{walkable - reachable} of {walkable} walkable cells unreachable from spawn";
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Four-way flood fill from a starting cell over everything that is not wall.</summary>
    public static int CountReachable(DungeonLayout layout, Vector2Int origin)
    {
        if (!layout.IsWalkable(origin)) return 0;

        var visited = new bool[layout.Width, layout.Height];
        var queue = new Queue<Vector2Int>();
        visited[origin.x, origin.y] = true;
        queue.Enqueue(origin);
        int count = 0;

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            count++;

            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2Int next = cell + Neighbours[i];
                if (!layout.Contains(next.x, next.y)) continue;
                if (visited[next.x, next.y] || !layout.IsWalkable(next)) continue;

                visited[next.x, next.y] = true;
                queue.Enqueue(next);
            }
        }

        return count;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    /// <summary>Disjoint-set forest with union by size; used to build the spanning tree.</summary>
    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _size;

        public UnionFind(int count)
        {
            _parent = new int[count];
            _size = new int[count];
            for (int i = 0; i < count; i++)
            {
                _parent[i] = i;
                _size[i] = 1;
            }
        }

        public int Find(int node)
        {
            while (_parent[node] != node)
            {
                _parent[node] = _parent[_parent[node]]; // path halving
                node = _parent[node];
            }
            return node;
        }

        /// <summary>Merges the two sets; false when they were already the same set.</summary>
        public bool Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB) return false;

            if (_size[rootA] < _size[rootB]) (rootA, rootB) = (rootB, rootA);
            _parent[rootB] = rootA;
            _size[rootA] += _size[rootB];
            return true;
        }
    }
}
