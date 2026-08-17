using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What is placed inside a room, as opposed to <see cref="RoomShape"/>, which is the
/// outline it was cut to.
/// </summary>
public enum InteriorPattern
{
    /// <summary>Nothing. Kept so that not every room is busy — clutter needs contrast.</summary>
    Empty = 0,

    /// <summary>
    /// A grid of free-standing pillars. The strongest pattern per cell spent: walking
    /// through one drags a moving picket of shadows across the floor, and there is always
    /// something just out of sight behind the next column.
    /// </summary>
    Colonnade = 1,

    /// <summary>
    /// Stub walls run in from the room's edge, stopping short of the far side. Cuts the
    /// room into bays that have to be checked one at a time.
    /// </summary>
    Partitions = 2,

    /// <summary>
    /// A single solid mass off-centre. Cheapest way to give a large room a far side, and
    /// the one pattern that never threatens to make a room feel like an obstacle course.
    /// </summary>
    Island = 3,

    /// <summary>
    /// Collapsed masonry banked against the walls and corners. Breaks the straight
    /// sightline along a wall that otherwise hands the player the room's edges for free.
    /// </summary>
    Collapse = 4
}

/// <summary>
/// Fills a room's interior with vision-blocking structure until it stops being readable
/// in a single glance.
///
/// This is the pass that does the most for the game's feel, and the reason it is worth
/// having <see cref="VisibilityAnalysis"/> at all. Rather than scattering a fixed number
/// of pillars and hoping, the decorator places structure and then measures what the room
/// gives away from its doorways, stopping once it is below target. A room's difficulty to
/// read becomes something the generator converges on instead of something it stumbles
/// into.
///
/// Everything it places is a solid cell — <see cref="CellType.Pillar"/> or
/// <see cref="CellType.Rubble"/> — never a prop prefab. Under this project's convention
/// props do not block vision or pathfinding, so a pillar spawned as a prop would look
/// like cover while rays and enemies passed straight through it.
/// </summary>
public static class RoomInteriorDecorator
{
    /// <summary>
    /// Sight radius the targets are calibrated against, in cells. Matches the player's
    /// default view radius: measuring against an unlimited range would call every large
    /// room unreadable regardless of what is in it.
    /// </summary>
    private const int SightRadius = 10;

    /// <summary>Rooms below this area are cramped enough already; clutter only makes them unusable.</summary>
    private const int MinArea = 30;

    /// <summary>
    /// Decorates every room that should be decorated. The Hub is skipped: the player has
    /// to be able to see that the one safe room is safe, and it is also the first room of
    /// a run — the worst possible place to hide something.
    /// </summary>
    public static void Decorate(DungeonLayout layout, LayoutParams p, DeterministicRandom random)
    {
        if (p.InteriorDensity <= 0f) return;

        foreach (Room room in layout.Rooms)
        {
            if (room.Kind == RoomKind.Hub) continue;
            if (room.Area < MinArea) continue;

            DecorateRoom(layout, room, p, random.Derive($"interior{room.Index}"));
        }
    }

    /// <summary>
    /// Places structure in one room, batch by batch, stopping as soon as the room's
    /// visible fraction falls under target.
    ///
    /// Batching rather than placing one cell at a time is a cost decision: a visibility
    /// sweep is far more expensive than a placement, and measuring after every single
    /// pillar would dominate generation time for no extra control.
    /// </summary>
    private static void DecorateRoom(DungeonLayout layout, Room room, LayoutParams p,
        DeterministicRandom random)
    {
        InteriorPattern pattern = PickPattern(room, random);
        if (pattern == InteriorPattern.Empty) return;

        float target = TargetVisibility(room, p);

        for (int batch = 0; batch < 4; batch++)
        {
            if (VisibilityAnalysis.VisibleFractionFromEntrances(layout, room, SightRadius) <= target)
                return;

            List<Vector2Int> proposal = Propose(layout, room, pattern, batch, random);
            if (proposal.Count == 0) return;

            Apply(layout, room, proposal, pattern);
        }
    }

    /// <summary>
    /// How much of itself a room is allowed to give away, scaled by size.
    ///
    /// A small room cannot be made mysterious and should not be tried on: forcing one
    /// under target just fills it with columns until it is unwalkable. A large hall, on
    /// the other hand, has to be broken up or it plays as an empty field.
    /// </summary>
    private static float TargetVisibility(Room room, LayoutParams p)
    {
        float size = Mathf.InverseLerp(MinArea, 220f, room.Area);
        float target = Mathf.Lerp(0.85f, 0.45f, size);

        // The density knob shifts the whole curve rather than replacing it, so tuning it
        // cannot make small rooms denser than large ones.
        return Mathf.Clamp01(target - (p.InteriorDensity - 0.5f) * 0.3f);
    }

    private static InteriorPattern PickPattern(Room room, DeterministicRandom random)
    {
        // A ring room already has its core; adding more would leave a walkway, not a room.
        if (room.Shape == RoomShape.Ring)
            return random.Chance(0.4f) ? InteriorPattern.Collapse : InteriorPattern.Empty;

        // A cavern is irregular enough that regular columns look pasted on.
        if (room.Shape == RoomShape.Cavern)
            return random.Chance(0.7f) ? InteriorPattern.Collapse : InteriorPattern.Empty;

        var weights = new List<(InteriorPattern pattern, float weight)>
        {
            (InteriorPattern.Empty, 0.6f),
            (InteriorPattern.Colonnade, room.Area >= 60 ? 2.2f : 0.8f),
            (InteriorPattern.Partitions, room.Area >= 70 ? 2f : 0.5f),
            (InteriorPattern.Island, 1.2f),
            (InteriorPattern.Collapse, 1.4f)
        };

        float total = 0f;
        foreach (var entry in weights) total += entry.weight;

        float roll = random.NextFloat() * total;
        foreach (var entry in weights)
        {
            roll -= entry.weight;
            if (roll <= 0f) return entry.pattern;
        }
        return InteriorPattern.Empty;
    }

    // ---------------------------------------------------------------- proposals

    private static List<Vector2Int> Propose(DungeonLayout layout, Room room,
        InteriorPattern pattern, int batch, DeterministicRandom random)
    {
        switch (pattern)
        {
            case InteriorPattern.Colonnade: return ProposeColonnade(layout, room, batch, random);
            case InteriorPattern.Partitions: return ProposePartition(layout, room, random);
            case InteriorPattern.Island: return ProposeIsland(layout, room, random);
            case InteriorPattern.Collapse: return ProposeCollapse(layout, room, random);
            default: return new List<Vector2Int>();
        }
    }

    /// <summary>
    /// A lattice of single pillars. Spacing tightens on later batches, which is how the
    /// pattern converges: the first pass is airy, and only a room that is still too open
    /// gets a denser grid.
    /// </summary>
    private static List<Vector2Int> ProposeColonnade(DungeonLayout layout, Room room, int batch,
        DeterministicRandom random)
    {
        int spacing = Mathf.Max(2, 4 - batch);
        int offsetX = random.Range(0, spacing);
        int offsetY = random.Range(0, spacing);

        var proposal = new List<Vector2Int>();

        foreach (Vector2Int cell in room.Cells)
        {
            if (!layout.IsWalkable(cell)) continue;

            // Modulo on the raw coordinate keeps the lattice aligned across the whole
            // room even when the room is not a rectangle.
            if (Mod(cell.x + offsetX, spacing) != 0 || Mod(cell.y + offsetY, spacing) != 0) continue;
            if (TouchesOpening(layout, cell)) continue;

            proposal.Add(cell);
        }

        return proposal;
    }

    /// <summary>
    /// One stub wall run in from an edge of the room, stopping two or more cells short of
    /// the opposite side so it divides the room without cutting it in half.
    /// </summary>
    private static List<Vector2Int> ProposePartition(DungeonLayout layout, Room room,
        DeterministicRandom random)
    {
        RectInt bounds = room.Bounds;
        bool horizontal = random.Chance(0.5f);

        int length = horizontal
            ? random.RangeInclusive(bounds.width / 3, bounds.width * 2 / 3)
            : random.RangeInclusive(bounds.height / 3, bounds.height * 2 / 3);
        if (length < 2) return new List<Vector2Int>();

        var proposal = new List<Vector2Int>();

        if (horizontal)
        {
            int y = random.RangeInclusive(bounds.yMin + 1, bounds.yMax - 2);
            bool fromLeft = random.Chance(0.5f);
            int startX = fromLeft ? bounds.xMin : bounds.xMax - length;

            for (int x = startX; x < startX + length; x++)
                TryAdd(layout, room, new Vector2Int(x, y), proposal);
        }
        else
        {
            int x = random.RangeInclusive(bounds.xMin + 1, bounds.xMax - 2);
            bool fromBottom = random.Chance(0.5f);
            int startY = fromBottom ? bounds.yMin : bounds.yMax - length;

            for (int y = startY; y < startY + length; y++)
                TryAdd(layout, room, new Vector2Int(x, y), proposal);
        }

        return proposal;
    }

    /// <summary>A single off-centre block, so the room gains a far side to walk round.</summary>
    private static List<Vector2Int> ProposeIsland(DungeonLayout layout, Room room,
        DeterministicRandom random)
    {
        RectInt bounds = room.Bounds;

        int width = random.RangeInclusive(2, Mathf.Max(2, bounds.width / 3));
        int height = random.RangeInclusive(2, Mathf.Max(2, bounds.height / 3));

        // Kept one cell clear of the room's box on every side, so the island can never
        // fuse with the surrounding rock and pinch the room into two halves.
        int x = random.RangeInclusive(bounds.xMin + 1, Mathf.Max(bounds.xMin + 1, bounds.xMax - width - 1));
        int y = random.RangeInclusive(bounds.yMin + 1, Mathf.Max(bounds.yMin + 1, bounds.yMax - height - 1));

        var proposal = new List<Vector2Int>();
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
                TryAdd(layout, room, new Vector2Int(x + dx, y + dy), proposal);
        }
        return proposal;
    }

    /// <summary>
    /// Rubble banked against the room's edges. Deliberately edge-biased: the middle of a
    /// room is where the player expects obstacles, while a clean wall line is what lets
    /// them read the room's extent without walking it.
    /// </summary>
    private static List<Vector2Int> ProposeCollapse(DungeonLayout layout, Room room,
        DeterministicRandom random)
    {
        var proposal = new List<Vector2Int>();

        foreach (Vector2Int cell in room.Cells)
        {
            if (!layout.IsWalkable(cell)) continue;
            if (TouchesOpening(layout, cell)) continue;

            int solidNeighbours = 0;
            for (int i = 0; i < Neighbours.Length; i++)
            {
                if (!layout.IsWalkable(cell + Neighbours[i])) solidNeighbours++;
            }
            if (solidNeighbours == 0) continue;

            // Corners collapse more readily than straight walls.
            float chance = solidNeighbours >= 2 ? 0.45f : 0.18f;
            if (random.Chance(chance)) proposal.Add(cell);
        }

        return proposal;
    }

    // ---------------------------------------------------------------- application

    /// <summary>
    /// Writes a proposal into the layout, then checks the room is still in one piece and
    /// rolls the whole batch back if it is not.
    ///
    /// The rollback is the important part. A partition wall that happens to reach the far
    /// side, or rubble that closes the last gap round an island, seals off a corner — and
    /// a sealed corner is a piece of floor the player can see but never reach. The
    /// generator's own connectivity check would catch it later and throw away the entire
    /// dungeon; catching it here costs one flood fill and keeps the layout.
    /// </summary>
    private static void Apply(DungeonLayout layout, Room room, List<Vector2Int> proposal,
        InteriorPattern pattern)
    {
        CellType solid = pattern == InteriorPattern.Collapse ? CellType.Rubble : CellType.Pillar;

        foreach (Vector2Int cell in proposal) layout[cell] = solid;

        ClearPassagePlugs(layout, proposal);

        if (RoomStaysWhole(layout, room)) return;

        foreach (Vector2Int cell in proposal) layout[cell] = CellType.Floor;
    }

    /// <summary>
    /// Re-opens any cell of the batch that ended up wedged across a passage one cell wide.
    ///
    /// <see cref="RoomStaysWhole"/> does not catch these, and cannot: it asks whether the room
    /// is still in one piece, and a pillar dropped into a one-cell gap leaves it perfectly
    /// connected as long as some other way round exists. The result is a pillar sitting square
    /// in a channel with floor on either side of it and wall above and below — which reads as a
    /// blocked passage no matter how sound the connectivity is, and is what the player
    /// complains about. Measured across 200 seeds at the shipped settings: 1948 such cells away
    /// from any doorway, on nearly every map.
    ///
    /// Doorway jambs are untouched — those come from <see cref="DoorwayNormalizer"/> walling an
    /// opening down to door width, are supposed to sit beside a gap, and are not in any batch
    /// this ever sees.
    ///
    /// Repeated to a fixed point because opening one cell can expose the next: clearing a plug
    /// turns solid into floor, which can leave a neighbour of the same batch newly flanked by
    /// floor on one axis and so newly a plug itself. Each pass only ever turns solid into
    /// floor, so the set of solid cells shrinks and the loop terminates.
    /// </summary>
    private static void ClearPassagePlugs(DungeonLayout layout, List<Vector2Int> proposal)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Vector2Int cell in proposal)
            {
                if (layout.IsWalkable(cell)) continue; // already opened by an earlier pass
                if (!PlugsPassage(layout, cell)) continue;

                layout[cell] = CellType.Floor;
                changed = true;
            }
        }
    }

    /// <summary>
    /// True when a solid cell has open floor on both sides of one axis and solid on both sides
    /// of the other — i.e. it is the plug in a one-cell-wide channel rather than a pillar
    /// standing in open ground.
    ///
    /// A pillar in the middle of a room has floor on all four sides and is not this; one set
    /// against a wall has solid on one side and is not this either. Only the wedged case
    /// matches, which is what keeps this from dismantling the interior patterns wholesale.
    /// </summary>
    private static bool PlugsPassage(DungeonLayout layout, Vector2Int cell)
    {
        bool west = layout.IsWalkable(cell.x - 1, cell.y);
        bool east = layout.IsWalkable(cell.x + 1, cell.y);
        bool south = layout.IsWalkable(cell.x, cell.y - 1);
        bool north = layout.IsWalkable(cell.x, cell.y + 1);

        return (west && east && !south && !north)
            || (south && north && !west && !east);
    }

    /// <summary>
    /// True when every walkable cell of the room can still reach every other one without
    /// leaving the room. Checked room-locally rather than over the whole map, because a
    /// room whose halves are only joined by a detour through a corridor is exactly the
    /// case this is meant to reject.
    /// </summary>
    private static bool RoomStaysWhole(DungeonLayout layout, Room room)
    {
        Vector2Int start = default;
        int walkable = 0;
        bool found = false;

        foreach (Vector2Int cell in room.Cells)
        {
            if (!layout.IsWalkable(cell)) continue;
            walkable++;
            if (found) continue;
            start = cell;
            found = true;
        }

        if (!found) return false;

        var visited = new HashSet<Vector2Int> { start };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        int reached = 0;

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            reached++;

            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2Int next = cell + Neighbours[i];
                if (!room.Contains(next) || !layout.IsWalkable(next)) continue;
                if (visited.Add(next)) queue.Enqueue(next);
            }
        }

        return reached == walkable;
    }

    private static void TryAdd(DungeonLayout layout, Room room, Vector2Int cell,
        List<Vector2Int> proposal)
    {
        if (!room.Contains(cell) || !layout.IsWalkable(cell)) return;
        if (TouchesOpening(layout, cell)) return;
        proposal.Add(cell);
    }

    /// <summary>
    /// True when the cell is a doorway or sits next to one. Blocking an opening is the
    /// one placement that can make a room unenterable, so it is refused outright rather
    /// than left to the connectivity check.
    /// </summary>
    private static bool TouchesOpening(DungeonLayout layout, Vector2Int cell)
    {
        if (layout[cell] == CellType.Door) return true;

        for (int i = 0; i < Neighbours.Length; i++)
        {
            if (layout[cell + Neighbours[i]] == CellType.Door) return true;
        }
        return false;
    }

    /// <summary>Modulo that stays non-negative for negative operands, unlike <c>%</c>.</summary>
    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };
}
