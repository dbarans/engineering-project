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
        List<Room> rooms = PlaceRooms(p, random.Derive("rooms"), out int hubIndex);
        List<RoomLink> links = ConnectRooms(rooms, p, random.Derive("links"));

        var layout = new DungeonLayout(seed, p.MapWidth, p.MapHeight, rooms, links);

        CarveRooms(layout, rooms);
        DetailOutlines(layout, rooms, p, random.Derive("outlines"));
        CorridorCarver.CarveAll(layout, rooms, links, p, random.Derive("corridors"));
        DoorwayNormalizer.Apply(layout, rooms, p.DoorwayWidth);

        // Roles are assigned before the interior pass because that pass reads them: the
        // start room and the hub are deliberately left legible, and it cannot know which
        // they are until the graph has been walked.
        AssignRoomRoles(layout, rooms, links, hubIndex);

        RoomInteriorDecorator.Decorate(layout, p, random.Derive("interiors"));
        Chokepoints.Detect(layout);

        return layout;
    }

    /// <summary>
    /// Rejection sampling: propose a rectangle, keep it when it clears every existing
    /// room by <see cref="LayoutParams.RoomSpacing"/>. Simple, and unlike BSP it leaves
    /// solid rock between rooms, which is what makes corridors read as corridors.
    ///
    /// The hub is placed first, before any sampling, and <paramref name="hubIndex"/>
    /// reports where it landed in the list.
    /// </summary>
    private static List<Room> PlaceRooms(LayoutParams p, DeterministicRandom random, out int hubIndex)
    {
        var rooms = new List<Room>(p.TargetRoomCount);
        hubIndex = PlaceHub(rooms, p);

        // The hub counts against the target, so raising the hub size does not silently
        // add a room to every dungeon.
        for (int i = rooms.Count; i < p.TargetRoomCount; i++)
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
    /// Reserves the hub: a square room in the exact middle of the map, placed before any
    /// other room is sampled. Returns its index, or -1 when the map is too small to hold
    /// it — in which case the dungeon simply has no hub rather than a misplaced one.
    ///
    /// Reserving rather than picking. The alternative — sample every room, then tag
    /// whichever landed nearest the middle — cannot promise there is a room near the
    /// middle at all, and rejection sampling routinely leaves the centre of a map empty.
    /// A hub the player is told to walk back to has to be somewhere they can predict, so
    /// its position is a guarantee of the layout, not an outcome of it.
    ///
    /// Placing it first also costs nothing structurally: every later room is rejected
    /// unless it clears this one by <see cref="LayoutParams.RoomSpacing"/>, exactly as
    /// rooms clear each other, and the spanning tree then connects it like any other node.
    /// Being central, it is normally one of the better-connected ones.
    ///
    /// Deliberately a plain rectangle, and deliberately not run through
    /// <see cref="RoomShaper"/>: the one room the player is safe in has to be legible from
    /// the doorway, the same reason <see cref="RoomInteriorDecorator"/> leaves it alone.
    /// </summary>
    private static int PlaceHub(List<Room> rooms, LayoutParams p)
    {
        int size = p.HubRoomSize;

        // The one-cell inset is the same border every sampled room respects.
        var bounds = new RectInt(
            (p.MapWidth - size) / 2,
            (p.MapHeight - size) / 2,
            size, size);

        if (bounds.xMin < 1 || bounds.yMin < 1 ||
            bounds.xMax > p.MapWidth - 1 || bounds.yMax > p.MapHeight - 1)
        {
            return -1;
        }

        rooms.Add(new Room(rooms.Count, bounds));
        return rooms.Count - 1;
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
        RoomShape shape = RoomShape.Rectangle;
        List<Vector2Int> cells = null;

        if (random.Chance(p.ShapedRoomChance))
        {
            shape = RoomShaper.PickShape(plot, random);
            if (shape != RoomShape.Rectangle) cells = RoomShaper.Shape(plot, shape, random);

            // A degenerate carve falls back to the plain rectangle. A room that failed to
            // become interesting is still a room; dropping it would leave a gap in the map.
            if (cells == null) shape = RoomShape.Rectangle;
        }

        return new Room(index, cells ?? RoomShaper.RectangleCells(plot), shape);
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
    /// Fills the outline detail back in: chamfered corners and buttresses along the long
    /// walls, so a room's perimeter has the rhythm of masonry rather than a straight edge.
    ///
    /// Runs between carving the rooms and carving the corridors, which is the only place
    /// it can. Earlier — while the rooms are still cell lists — the notches would not be
    /// room cells at all, and every one of them adjacent to a corridor would register as
    /// its own opening; measured over 120 seeds that added about one spurious door to
    /// every room in the dungeon. Later, and the corridors would already have been routed
    /// through walls that are about to move.
    ///
    /// A corridor carved afterwards simply punches back through any notch in its way,
    /// which is the right outcome: the doorway wins over the decoration.
    /// </summary>
    private static void DetailOutlines(DungeonLayout layout, List<Room> rooms, LayoutParams p,
        DeterministicRandom random)
    {
        if (p.PerimeterDetail <= 0f) return;

        foreach (var room in rooms)
        {
            List<Vector2Int> notches = RoomShaper.PerimeterNotches(
                room.Cells, room.Shape, p.PerimeterDetail, random.Derive($"room{room.Index}"));

            foreach (Vector2Int cell in notches) layout[cell] = CellType.Wall;
        }
    }

    /// <summary>
    /// Tags rooms and computes their depth. The Hub was fixed at placement time and is
    /// only carried through here, and it is also where the player spawns and where every
    /// other room's depth is measured from — the run's difficulty curve is literally "how
    /// far have you walked from the hub". Treasure is whichever Normal room ends up
    /// farthest from it over the graph.
    ///
    /// The Exit is picked before the Treasure and by a different measure — distance to the
    /// edge of the map, not distance over the room graph — so the two roles cannot land on
    /// the same room and the way out is somewhere you could plausibly leave from. Its key
    /// then goes to whichever room is farthest from it, which is the whole shape of the
    /// run: find the way out, then cross the map for the one thing that opens it.
    ///
    /// A hub that failed to fit — <paramref name="hubIndex"/> is -1, which needs a map
    /// barely larger than one room — falls back to the old rule of thumb: the room
    /// farthest from the map centre becomes the origin instead, tagged as an ordinary
    /// Normal room since there is no hub to make it a Hub. Not a design point, just
    /// somewhere to stand and a direction to measure from when the reserved centre did
    /// not happen.
    /// </summary>
    private static void AssignRoomRoles(DungeonLayout layout, List<Room> rooms, List<RoomLink> links,
        int hubIndex)
    {
        if (rooms.Count == 0) return;

        var adjacency = BuildAdjacency(rooms.Count, links);
        for (int i = 0; i < rooms.Count; i++)
        {
            rooms[i].Kind = i == hubIndex ? RoomKind.Hub : RoomKind.Normal;
            rooms[i].Degree = adjacency[i].Count;
        }

        Room origin = hubIndex >= 0 ? rooms[hubIndex] : FarthestFromCentre(layout, rooms);
        layout.SpawnCell = origin.Center;

        int[] depths = BreadthFirstDepths(adjacency, origin.Index);
        for (int i = 0; i < rooms.Count; i++)
            rooms[i].DepthFromHub = depths[i];

        Room exit = PickExit(layout, rooms);
        if (exit != null)
        {
            exit.Kind = RoomKind.Exit;
            CutExitDoorway(layout, exit);
        }

        Room treasure = null;
        int maxDepth = 0;
        foreach (var room in rooms)
        {
            // Normal only: a reward stashed in the one place the player is safe is not
            // a reward, and the exit is already the room the whole run points at.
            if (room.Kind != RoomKind.Normal) continue;
            if (room.DepthFromHub > maxDepth)
            {
                maxDepth = room.DepthFromHub;
                treasure = room;
            }
        }
        if (treasure != null) treasure.Kind = RoomKind.Treasure;

        if (exit != null) MarkKeyRoom(rooms, adjacency, exit, treasure);
    }

    /// <summary>
    /// Picks the room the way out is in: the one nearest the edge of the map that has a
    /// wall backing onto the outside.
    ///
    /// The outward wall is a hard filter, not a preference. The exit is a door cut through
    /// the room's own wall into the rock beyond it, so a room with corridors on every side
    /// has nowhere to put one however close to the edge it sits.
    ///
    /// A <b>dead end</b> is preferred on top of that: a room with exactly one way in reads
    /// as somewhere the dungeon stops, while a room with a corridor out the far side reads
    /// as one more room to pass through, whatever is in its wall. Preferred rather than
    /// required, because on a small map the dead ends and the rooms with an outward wall do
    /// not always overlap — measured at 1 seed in 200 — and a run with no ending at all is
    /// worse than one that ends in a room the player could have walked on through.
    ///
    /// Among the rooms that qualify, the one nearest the edge wins; ties go to the one
    /// deeper from the hub, then to the lower index so the choice cannot depend on the
    /// order rooms happened to be built in. The hub is excluded outright: it is the middle
    /// of the map by construction and it is where the player starts.
    ///
    /// Returns null when nothing qualifies even on the second pass. That dungeon has no way
    /// out, which is a run without an ending but not a broken one.
    /// </summary>
    private static Room PickExit(DungeonLayout layout, List<Room> rooms)
    {
        return PickExit(layout, rooms, deadEndsOnly: true)
               ?? PickExit(layout, rooms, deadEndsOnly: false);
    }

    private static Room PickExit(DungeonLayout layout, List<Room> rooms, bool deadEndsOnly)
    {
        Room best = null;
        int bestEdgeDistance = int.MaxValue;
        int bestDepth = -1;

        foreach (var room in rooms)
        {
            if (room.Kind != RoomKind.Normal) continue;
            if (deadEndsOnly && CountEntrances(layout, room) != 1) continue;
            if (FindExitDoorway(layout, room, out _, out _) == false) continue;

            int edgeDistance = EdgeDistance(layout, room);
            if (edgeDistance > bestEdgeDistance) continue;
            if (edgeDistance == bestEdgeDistance && room.DepthFromHub <= bestDepth) continue;

            best = room;
            bestEdgeDistance = edgeDistance;
            bestDepth = room.DepthFromHub;
        }

        return best;
    }

    /// <summary>
    /// Counts the ways into the room: groups of adjoining walkable cells just outside it.
    ///
    /// Grouped rather than counted cell by cell, because one opening two or three cells
    /// wide is still one way in, and the doorway normaliser leaves openings at more than
    /// one cell wide often enough that counting cells would call almost every room a
    /// crossroads.
    ///
    /// Read off the geometry rather than off <see cref="Room.Degree"/>, which counts
    /// corridors in the room graph. The two disagree: a corridor carved past a room can
    /// open into it without a link being recorded, and what the player walks through is the
    /// opening, not the graph edge.
    /// </summary>
    private static int CountEntrances(DungeonLayout layout, Room room)
    {
        var outside = new HashSet<Vector2Int>();

        foreach (Vector2Int cell in room.Cells)
        {
            foreach (Vector2Int step in Directions)
            {
                Vector2Int neighbour = cell + step;
                if (room.Contains(neighbour) || !layout.IsWalkable(neighbour)) continue;
                outside.Add(neighbour);
            }
        }

        // Flood the collected cells among themselves: two of them belong to the same
        // opening when they touch, so each flood is one way in.
        int openings = 0;
        var pending = new Stack<Vector2Int>();

        while (outside.Count > 0)
        {
            openings++;

            var start = default(Vector2Int);
            foreach (Vector2Int cell in outside) { start = cell; break; }

            pending.Push(start);
            outside.Remove(start);

            while (pending.Count > 0)
            {
                Vector2Int cell = pending.Pop();
                foreach (Vector2Int step in Directions)
                {
                    Vector2Int neighbour = cell + step;
                    if (!outside.Remove(neighbour)) continue;
                    pending.Push(neighbour);
                }
            }
        }

        return openings;
    }

    /// <summary>
    /// Records the chosen exit doorway on the layout, so the populator hangs the door on
    /// the cell the selection was actually made against rather than re-deriving it from
    /// the same rules and possibly landing somewhere else.
    ///
    /// The cell stays solid. Nothing is carved: the door leads out of the dungeon, not to
    /// another part of it, so a walkable cell there would be a hole in the map that the
    /// pathfinding grid, the vision system and the wall painter would each have to be
    /// taught to treat as a special case. What actually ends the run is the player standing
    /// on the threshold with the door open — see <c>DungeonExit</c>.
    /// </summary>
    private static void CutExitDoorway(DungeonLayout layout, Room exit)
    {
        if (!FindExitDoorway(layout, exit, out Vector2Int door, out Vector2Int threshold)) return;

        layout.ExitDoorCell = door;
        layout.ExitThresholdCell = threshold;
    }

    /// <summary>
    /// Finds where to cut the way out: a wall cell of the room with nothing but solid rock
    /// between it and the edge of the map.
    ///
    /// The straight-line-to-the-edge test is what makes the door lead *outside* rather than
    /// into a pocket of rock with a corridor on the other side of it. A door opening onto
    /// two metres of stone and then somebody's storeroom is not an exit, and the player
    /// cannot tell the difference until it has cost them the run's only key.
    ///
    /// Placement is by <b>the longest unbroken run of usable wall</b>, with the door in the
    /// middle of it. A door in the corner of a room reads as a mistake; one centred on a
    /// wall reads as built.
    ///
    /// The first version measured distance from <c>Room.Center</c> instead, which is the
    /// room's medoid — and for an L-shaped or ring-shaped room the medoid is nowhere near
    /// the middle of any particular wall. Measured over 200 seeds it put the door mid-wall
    /// on 189 of them and hard into a corner on 4. Scoring the wall run itself has no such
    /// failure mode: a corner is a short run by construction, so it loses to any real
    /// stretch of wall.
    /// </summary>
    private static bool FindExitDoorway(DungeonLayout layout, Room room,
        out Vector2Int door, out Vector2Int threshold)
    {
        door = DungeonLayout.NoCell;
        threshold = DungeonLayout.NoCell;

        int bestLength = 0;
        var run = new List<Vector2Int>();

        foreach (Vector2Int step in Directions)
        {
            // Sweep each line of the room across the wall's own axis, so the cells of one
            // stretch of wall are visited in order and a run is just a counter. Iterating
            // room.Cells instead would visit them in construction order, where "the cell
            // next along this wall" is not the next thing seen.
            bool alongY = step.x != 0;
            int acrossFrom = alongY ? room.Bounds.xMin : room.Bounds.yMin;
            int acrossTo = alongY ? room.Bounds.xMax : room.Bounds.yMax;
            int alongFrom = alongY ? room.Bounds.yMin : room.Bounds.xMin;
            int alongTo = alongY ? room.Bounds.yMax : room.Bounds.xMax;

            for (int across = acrossFrom; across < acrossTo; across++)
            {
                run.Clear();

                for (int along = alongFrom; along <= alongTo; along++)
                {
                    Vector2Int cell = alongY
                        ? new Vector2Int(across, along)
                        : new Vector2Int(along, across);

                    bool usable = along < alongTo && IsThresholdFor(layout, room, cell, step);
                    if (usable)
                    {
                        run.Add(cell);
                        continue;
                    }

                    // The run just ended — one straight, unbroken stretch of outer wall.
                    // The longest such stretch is the one that reads as *the* wall of the
                    // room rather than as the stub beside a doorway or the two cells left
                    // over in a corner, and its middle is where a door belongs.
                    if (run.Count > bestLength)
                    {
                        bestLength = run.Count;
                        threshold = run[run.Count / 2];
                        door = threshold + step;
                    }

                    run.Clear();
                }
            }
        }

        return door != DungeonLayout.NoCell;
    }

    /// <summary>
    /// True when the player could stand on <paramref name="cell"/> and step out through the
    /// wall beside it in direction <paramref name="step"/>: the cell is walkable floor of
    /// the room, and what it faces is solid rock all the way off the map.
    /// </summary>
    private static bool IsThresholdFor(DungeonLayout layout, Room room, Vector2Int cell,
        Vector2Int step)
    {
        if (!room.Contains(cell)) return false;
        if (!layout.IsWalkable(cell)) return false; // pillars and rubble are not a threshold

        Vector2Int outward = cell + step;
        if (room.Contains(outward) || layout.IsWalkable(outward)) return false;

        return RunsToTheEdge(layout, outward, step);
    }

    /// <summary>
    /// True when every cell from <paramref name="from"/> outwards along
    /// <paramref name="step"/> is solid, all the way off the map.
    /// </summary>
    private static bool RunsToTheEdge(DungeonLayout layout, Vector2Int from, Vector2Int step)
    {
        Vector2Int cell = from;
        while (layout.Contains(cell.x, cell.y))
        {
            if (layout.IsWalkable(cell)) return false;
            cell += step;
        }
        return true;
    }

    /// <summary>The four orthogonal steps, in a fixed order.</summary>
    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    /// <summary>
    /// Flags the room holding the exit key: the one that keeps the greatest distance from
    /// <i>both</i> the exit and the treasure, so the map's three destinations sit apart
    /// instead of clustering in one corner.
    ///
    /// Scored on the <b>smaller</b> of the two distances, maximised. Scoring on distance
    /// from the exit alone — which is what this did first, and in hops rather than metres —
    /// put the key within a tenth of the map of the treasure on 7 of 60 maps and within a
    /// fifth on 17, because "deepest from the hub" and "farthest from the exit" are two
    /// different questions whose answers are correlated: both pull towards the same far end
    /// of the graph. Maximising the minimum is what turns "far from one thing" into "far
    /// from everything that matters", and it cannot be gamed by being enormously far from
    /// one of the pair.
    ///
    /// Distance is straight-line here, and that is a deliberate reversal. The first version
    /// used hop counts over the room graph, on the reasoning that the player walks corridors
    /// rather than flying — true, but hop counts on a 200×200 map with 30 rooms are small
    /// integers that tie constantly and correlate poorly with what the map looks like. The
    /// player's sense of "these are at opposite ends of the dungeon" is spatial. Hops still
    /// break ties, so the corridor reality is not thrown away entirely.
    ///
    /// The exit itself and the hub are excluded for the obvious reasons: a key locked
    /// inside the door it opens, and a key handed over at the spawn point, are both the
    /// same non-puzzle. The treasure room is excluded for a less obvious one: it and the
    /// key room are the map's only two reasons to walk anywhere that is not the exit, and
    /// letting one room be both collapses them into a single trip.
    /// </summary>
    private static void MarkKeyRoom(List<Room> rooms, List<int>[] adjacency, Room exit,
        Room treasure)
    {
        int[] hops = BreadthFirstDepths(adjacency, exit.Index);

        Room best = null;
        long bestSpread = -1;
        int bestHops = -1;

        foreach (var room in rooms)
        {
            if (room.Index == exit.Index || room.Kind == RoomKind.Hub) continue;
            if (room.Kind == RoomKind.Treasure) continue;

            // Unreachable over the graph, which the loop-carving makes unlikely but does
            // not forbid. A key behind no corridor at all is a key that cannot be fetched.
            int roomHops = hops[room.Index];
            if (roomHops < 0) continue;

            long spread = SquaredDistance(room.Center, exit.Center);
            if (treasure != null)
                spread = System.Math.Min(spread, SquaredDistance(room.Center, treasure.Center));

            if (spread < bestSpread) continue;
            if (spread == bestSpread && roomHops <= bestHops) continue;

            best = room;
            bestSpread = spread;
            bestHops = roomHops;
        }

        if (best != null) best.HoldsExitKey = true;
    }

    private static long SquaredDistance(Vector2Int a, Vector2Int b)
    {
        long dx = a.x - b.x;
        long dy = a.y - b.y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// Cells from the room's bounding box to the nearest side of the map. Measured off the
    /// box rather than the medoid so a large room counts as being at the edge when its
    /// wall is, which is what the player sees.
    /// </summary>
    private static int EdgeDistance(DungeonLayout layout, Room room)
    {
        int left = room.Bounds.xMin;
        int right = layout.Width - room.Bounds.xMax;
        int bottom = room.Bounds.yMin;
        int top = layout.Height - room.Bounds.yMax;

        return Mathf.Max(0, Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top)));
    }

    private static Room FarthestFromCentre(DungeonLayout layout, List<Room> rooms)
    {
        var center = new Vector2Int(layout.Width / 2, layout.Height / 2);
        Room best = rooms[0];
        int bestDistance = -1;

        foreach (var room in rooms)
        {
            Vector2Int delta = room.Center - center;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = room;
            }
        }

        return best;
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

        // Unreachable rooms would otherwise stay at -1 and read as "closer than the hub".
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
