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
        List<Room> rooms = PlaceRooms(p, random.Derive("rooms"), out int hubIndex,
            out List<int> treasurePlots);
        List<RoomLink> links = ConnectRooms(rooms, treasurePlots, hubIndex, p, random.Derive("links"));

        var layout = new DungeonLayout(seed, p.MapWidth, p.MapHeight, rooms, links);

        CarveRooms(layout, rooms);
        DetailOutlines(layout, rooms, hubIndex, p, random.Derive("outlines"));
        CorridorCarver.CarveAll(layout, rooms, links, hubIndex, p, random.Derive("corridors"));
        DoorwayNormalizer.Apply(layout, rooms, p.DoorwayWidth);

        // Roles are assigned before the interior pass because that pass reads them: the
        // start room and the hub are deliberately left legible, and it cannot know which
        // they are until the graph has been walked.
        AssignRoomRoles(layout, rooms, links, hubIndex, treasurePlots, p);

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
    private static List<Room> PlaceRooms(LayoutParams p, DeterministicRandom random,
        out int hubIndex, out List<int> treasurePlots)
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

        treasurePlots = PlaceTreasurePlots(rooms, p, random.Derive("treasure"));
        return rooms;
    }

    /// <summary>
    /// Places the treasure plots: small rooms sampled after the ordinary ones, on whatever
    /// space is left between them.
    ///
    /// Sampled rather than picked out of the finished map, which is what the treasure room
    /// used to be — whichever room ended up farthest from the hub. That gave one reward
    /// room, wherever the layout happened to leave it, standing open. Sampling gives
    /// several, small enough to seal, and lets the loot be something the player decides to
    /// open rather than something they walk into.
    ///
    /// A few more plots are placed than are wanted, because a plot can still lose the role
    /// later: a corridor may cut a second way into it on its way past, which
    /// <see cref="ValidateTreasureRooms"/> catches. The spares absorb that, and any that
    /// are not needed become ordinary small rooms.
    ///
    /// They are deliberately not counted against <see cref="LayoutParams.TargetRoomCount"/>:
    /// a treasure closet is not one of the rooms the run is made of, and spending a room
    /// budget entry on one would quietly shrink the dungeon each time one fitted.
    ///
    /// Plain rectangles, never run through <see cref="RoomShaper"/>: at four cells to a
    /// side there is nothing to carve that does not just make the room smaller.
    /// </summary>
    private static List<int> PlaceTreasurePlots(List<Room> rooms, LayoutParams p,
        DeterministicRandom random)
    {
        var placed = new List<int>();
        if (p.TreasureRoomCount <= 0) return placed;

        int wanted = p.TreasureRoomCount + TreasurePlotSpares;
        for (int i = 0; i < wanted; i++)
        {
            for (int attempt = 0; attempt < p.PlacementAttemptsPerRoom; attempt++)
            {
                int width = random.RangeInclusive(p.MinTreasureRoomSize, p.MaxTreasureRoomSize);
                int height = random.RangeInclusive(p.MinTreasureRoomSize, p.MaxTreasureRoomSize);

                int maxX = p.MapWidth - width - 1;
                int maxY = p.MapHeight - height - 1;
                if (maxX < 1 || maxY < 1) break;

                var bounds = new RectInt(
                    random.RangeInclusive(1, maxX),
                    random.RangeInclusive(1, maxY),
                    width, height);

                if (Overlaps(rooms, bounds, p.RoomSpacing)) continue;

                placed.Add(rooms.Count);
                rooms.Add(new Room(rooms.Count, bounds) { IsTreasurePlot = true });
                break;
            }
        }

        return placed;
    }

    /// <summary>
    /// Extra treasure plots placed beyond the count actually wanted, to cover the ones a
    /// corridor later spoils by cutting a second way in.
    /// </summary>
    private const int TreasurePlotSpares = 4;

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
    ///
    /// Treasure plots are held out of all of that and given exactly one corridor each, to
    /// their nearest ordinary room. A locked room has to be a dead end or its lock stops
    /// being optional: with a corridor out the far side it takes a piece of the map with
    /// it, and the player without a key has lost a route rather than skipped a room. The
    /// spanning tree would happily route through one, and a loop edge would hand it a
    /// second door.
    /// </summary>
    private static List<RoomLink> ConnectRooms(List<Room> rooms, List<int> treasurePlots,
        int hubIndex, LayoutParams p, DeterministicRandom random)
    {
        var links = new List<RoomLink>();
        if (rooms.Count < 2) return links;

        var treasure = new HashSet<int>(treasurePlots);

        var candidates = new List<(int a, int b, int distance)>();
        for (int a = 0; a < rooms.Count; a++)
        {
            if (treasure.Contains(a)) continue;
            for (int b = a + 1; b < rooms.Count; b++)
            {
                if (treasure.Contains(b)) continue;
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

        // Loop candidates: the shortest slice of the rejected edges, sized by the
        // settings. Shortest first is what keeps a "loop" from being a corridor across the
        // whole map — the rejected list is still in the distance order Kruskal sorted it
        // into.
        int loopPool = Mathf.Max(1, Mathf.RoundToInt(rejected.Count * p.LoopCandidateFraction));
        loopPool = Mathf.Min(loopPool, rejected.Count);
        for (int i = 0; i < loopPool; i++)
        {
            if (random.Chance(p.ExtraLoopChance))
                links.Add(new RoomLink(rejected[i].a, rejected[i].b));
        }

        TrimHubCorridors(links, rooms, hubIndex, p);

        foreach (int plot in treasurePlots)
        {
            int host = NearestOrdinaryRoom(rooms, treasure, plot);
            if (host >= 0) links.Add(new RoomLink(plot, host));
        }

        return links;
    }

    /// <summary>
    /// Drops corridors into the hub until it has no more than
    /// <see cref="LayoutParams.MaxHubCorridors"/> of them, longest first, and never one the
    /// map still needs.
    ///
    /// Pruning afterwards rather than constraining the spanning tree: a degree-bounded
    /// spanning tree is a harder problem than the one being solved, and the loop edges are
    /// added after the tree anyway, so a cap enforced during the tree would be exceeded
    /// again a few lines later. Here the whole graph exists and the question is only which
    /// of the hub's corridors are redundant.
    ///
    /// Redundant is checked, not assumed: a link is dropped only when the rooms it joined
    /// are still connected without it. That is what makes this safe to run after loops
    /// have been added — the loops are usually what makes a hub corridor redundant in the
    /// first place — and it is also why the cap is a target rather than a guarantee. On a
    /// map whose loops all landed elsewhere, the hub keeps the corridors the dungeon cannot
    /// do without.
    ///
    /// Longest first because a long corridor into the middle of the map is the one that
    /// reads least like a door and most like a passage that happens to end there.
    /// </summary>
    private static void TrimHubCorridors(List<RoomLink> links, List<Room> rooms, int hubIndex,
        LayoutParams p)
    {
        if (hubIndex < 0 || p.MaxHubCorridors <= 0) return;

        var hubLinks = new List<int>();
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].RoomA == hubIndex || links[i].RoomB == hubIndex) hubLinks.Add(i);
        }
        if (hubLinks.Count <= p.MaxHubCorridors) return;

        hubLinks.Sort((left, right) =>
        {
            int byLength = LinkLength(rooms, links[right]).CompareTo(LinkLength(rooms, links[left]));
            return byLength != 0 ? byLength : left.CompareTo(right);
        });

        var dropped = new HashSet<int>();
        int remaining = hubLinks.Count;

        foreach (int index in hubLinks)
        {
            if (remaining <= p.MaxHubCorridors) break;

            dropped.Add(index);
            if (StaysConnected(links, dropped, rooms.Count)) remaining--;
            else dropped.Remove(index);
        }

        for (int i = links.Count - 1; i >= 0; i--)
        {
            if (dropped.Contains(i)) links.RemoveAt(i);
        }
    }

    private static int LinkLength(List<Room> rooms, RoomLink link)
    {
        Vector2Int delta = rooms[link.RoomA].Center - rooms[link.RoomB].Center;
        return Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
    }

    /// <summary>
    /// True when every room the links still join is reachable from the first of them —
    /// union-find over the links that are not in <paramref name="dropped"/>.
    ///
    /// Rooms with no link at all are ignored rather than counted as a failure: the treasure
    /// closets have not been connected yet at this point, and a room the tree never reached
    /// is a separate fault the layout validator catches on the cells themselves.
    /// </summary>
    private static bool StaysConnected(List<RoomLink> links, HashSet<int> dropped, int roomCount)
    {
        var union = new UnionFind(roomCount);
        var linked = new HashSet<int>();
        int components = 0;

        for (int i = 0; i < links.Count; i++)
        {
            if (dropped.Contains(i)) continue;

            RoomLink link = links[i];
            if (linked.Add(link.RoomA)) components++;
            if (linked.Add(link.RoomB)) components++;
            if (union.Union(link.RoomA, link.RoomB)) components--;
        }

        return components <= 1;
    }

    /// <summary>
    /// The ordinary room nearest the given treasure plot — the one room it is given a
    /// corridor to. Ties go to the lower index so the choice cannot depend on placement
    /// order. Returns -1 on a map with no ordinary room at all, in which case the plot is
    /// left unconnected and the role pass drops it: a sealed pocket nothing reaches is not
    /// a locked room, it is a hole in the map.
    /// </summary>
    private static int NearestOrdinaryRoom(List<Room> rooms, HashSet<int> treasure, int plot)
    {
        int best = -1;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < rooms.Count; i++)
        {
            if (i == plot || treasure.Contains(i)) continue;

            Vector2Int delta = rooms[i].Center - rooms[plot].Center;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            if (distance >= bestDistance) continue;

            best = i;
            bestDistance = distance;
        }

        return best;
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
    private static void DetailOutlines(DungeonLayout layout, List<Room> rooms, int hubIndex,
        LayoutParams p, DeterministicRandom random)
    {
        if (p.PerimeterDetail <= 0f) return;

        foreach (var room in rooms)
        {
            // The hub keeps its four straight walls, for the same reason it is never shaped
            // and never decorated: it is the one room that has to be read at a glance, and
            // a chamfered corner or a buttress is one more thing in it to resolve.
            if (room.Index == hubIndex) continue;

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
        int hubIndex, List<int> treasurePlots, LayoutParams p)
    {
        if (rooms.Count == 0) return;

        var adjacency = BuildAdjacency(rooms.Count, links);
        for (int i = 0; i < rooms.Count; i++)
        {
            rooms[i].Kind = i == hubIndex ? RoomKind.Hub : RoomKind.Normal;
            rooms[i].Degree = adjacency[i].Count;
        }

        // Before the other roles, so none of them can be handed to a room that is about to
        // be locked: every later pass skips a Treasure room.
        foreach (int index in treasurePlots) rooms[index].Kind = RoomKind.Treasure;
        ValidateTreasureRooms(layout, rooms, p);

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

        if (exit != null) MarkKeyRoom(rooms, adjacency, exit);
    }

    /// <summary>
    /// Decides which treasure plots keep the role, and demotes the rest to ordinary rooms.
    /// Placement decides where they go; this decides which of them are locked, and it is a
    /// correctness pass rather than a taste one — each rule below is a way a locked door
    /// costs the player something the run needs:
    ///
    /// <list type="bullet">
    /// <item><b>Dead end</b>, measured as <see cref="CountEntrances"/> 1 — one way in, read
    /// off the geometry. <see cref="ConnectRooms"/> gives each plot a single graph edge, but
    /// the graph is not what the player walks: a corridor routed past the room can open into
    /// it without a link being recorded, and a room checked only for
    /// <see cref="Room.Degree"/> 1 was locked with a second door standing open on the far
    /// side. That is exactly the case this rule exists to catch.</item>
    /// <item><b>Sealable</b>: every doorway cell can hold a door and no two of them are
    /// adjacent. The populator hangs one leaf per opening and leaves jambless openings as
    /// open arches, so a room failing either test is one whose lock has a hole beside
    /// it.</item>
    /// <item><b>Small</b>, per <see cref="LayoutParams.TreasureMaxArea"/> — a backstop on
    /// the plot size rather than a real filter.</item>
    /// </list>
    ///
    /// Plots beyond <see cref="LayoutParams.TreasureRoomCount"/> are demoted too: the
    /// spares exist to cover the ones a corridor spoils, not to add rooms when nothing was
    /// spoiled. Lowest index first, so which spare survives does not depend on placement
    /// order.
    ///
    /// A demoted plot is an ordinary room in every respect, which is the point of demoting
    /// rather than discarding: the space is already carved and connected, and a room the
    /// player can walk into is a better outcome than a hole in the map.
    /// </summary>
    private static void ValidateTreasureRooms(DungeonLayout layout, List<Room> rooms, LayoutParams p)
    {
        int kept = 0;

        foreach (var room in rooms)
        {
            if (room.Kind != RoomKind.Treasure) continue;

            bool lockable = room.Degree == 1 &&
                            CountEntrances(layout, room) == 1 &&
                            room.Area <= p.TreasureMaxArea &&
                            CanBeSealed(layout, room);

            if (lockable && kept < p.TreasureRoomCount) kept++;
            else room.Kind = RoomKind.Normal;
        }
    }

    /// <summary>
    /// True when every way into the room is an opening a single door leaf can close.
    /// See <see cref="ValidateTreasureRooms"/> for why a room that fails this must not be
    /// locked.
    /// </summary>
    private static bool CanBeSealed(DungeonLayout layout, Room room)
    {
        var doorways = DoorwayCells(layout, room);
        if (doorways.Count == 0) return false; // sealed already, or reached some other way

        foreach (Vector2Int cell in doorways)
        {
            if (!layout.HasDoorJambs(cell)) return false;

            // Adjacent doorway cells are one opening wider than one leaf: the populator
            // hangs a door on the first and skips the second, leaving a gap beside it.
            if (doorways.Contains(cell + Vector2Int.right) ||
                doorways.Contains(cell + Vector2Int.up))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The <see cref="CellType.Door"/> cells on the room's perimeter — every opening
    /// leading into it.
    /// </summary>
    public static HashSet<Vector2Int> DoorwayCells(DungeonLayout layout, Room room)
    {
        var doorways = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in room.Cells)
        {
            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2Int neighbour = cell + Neighbours[i];
                if (room.Contains(neighbour)) continue;
                if (layout.Contains(neighbour.x, neighbour.y) &&
                    layout[neighbour] == CellType.Door)
                    doorways.Add(neighbour);
            }
        }

        return doorways;
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
    /// Flags the room holding the exit key: the locked treasure room farthest from the
    /// exit, so the way out and the thing that opens it sit at opposite ends of the map.
    ///
    /// The key lives in a treasure room rather than in a room of its own. A dedicated key
    /// room meant a trip the player made for one item and nothing else, in a room that was
    /// otherwise ordinary and gave no sign of what was in it. Putting the key behind one of
    /// the locks the player is already choosing between makes opening treasure rooms the
    /// way the key is found — and makes the choice of which door to spend a key on
    /// something the run can turn on.
    ///
    /// Distance is straight-line, with hops over the room graph breaking ties. That is a
    /// deliberate reversal of the first version, which scored hops alone: on a 200×200 map
    /// with 30 rooms hop counts are small integers that tie constantly and correlate poorly
    /// with what the map looks like, while the player's sense of "these are at opposite ends
    /// of the dungeon" is spatial. Hops still break ties, so the corridor reality is not
    /// thrown away entirely.
    ///
    /// Falls back to the farthest ordinary room when the dungeon has no treasure rooms at
    /// all — <c>treasureRoomCount</c> set to 0, or every plot demoted. The key has to be
    /// somewhere reachable or the run cannot be finished, and that outweighs where it would
    /// ideally sit.
    /// </summary>
    private static void MarkKeyRoom(List<Room> rooms, List<int>[] adjacency, Room exit)
    {
        int[] hops = BreadthFirstDepths(adjacency, exit.Index);

        Room best = PickKeyRoom(rooms, hops, exit, RoomKind.Treasure)
                    ?? PickKeyRoom(rooms, hops, exit, RoomKind.Normal);

        if (best != null) best.HoldsExitKey = true;
    }

    /// <summary>
    /// The room of the given kind farthest from the exit, ties broken by hop count and then
    /// by the lower index so the choice cannot depend on placement order. Rooms the graph
    /// cannot reach are skipped: a key behind no corridor at all is a key that cannot be
    /// fetched.
    /// </summary>
    private static Room PickKeyRoom(List<Room> rooms, int[] hops, Room exit, RoomKind kind)
    {
        Room best = null;
        long bestDistance = -1;
        int bestHops = -1;

        foreach (var room in rooms)
        {
            if (room.Index == exit.Index || room.Kind != kind) continue;

            int roomHops = hops[room.Index];
            if (roomHops < 0) continue;

            long distance = SquaredDistance(room.Center, exit.Center);
            if (distance < bestDistance) continue;
            if (distance == bestDistance && roomHops <= bestHops) continue;

            best = room;
            bestDistance = distance;
            bestHops = roomHops;
        }

        return best;
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

        // Treasure closets excluded — including the ones demoted back to ordinary rooms,
        // which is what the plot flag is for. They are sampled on top of the room budget,
        // and counting them would let a map with too few real rooms pass by having fitted
        // a couple of locked cupboards.
        int placed = 0;
        foreach (var room in layout.Rooms)
            if (!room.IsTreasurePlot) placed++;

        if (placed < parameters.MinRoomCount)
        {
            failure = $"only {placed} rooms placed, {parameters.MinRoomCount} required";
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
