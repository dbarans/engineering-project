using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides where a room's openings are and cuts the ones that get a door down to a fixed
/// width.
///
/// This exists because of a mismatch that only shows up once doors are actually spawned.
/// The door prefab is one cell wide — <c>Door_System</c> has a 1×1 collider — but a room's
/// opening is whatever width the corridor happened to arrive at, which after the corridor
/// pass varies from one cell to four. Marking the middle cell of a four-wide gap and
/// putting a door on it produces a door the player simply walks around, so it is not a
/// door at all: it is scenery with a collider.
///
/// <see cref="CorridorCarver"/> does pinch corridors down, but not where you would think.
/// Its runs go from room *centre* to room centre, so the pinch lands three cells from the
/// centre of a room — inside it, where the cells are already floor and narrowing is a
/// no-op — and at the bends. The width at the point a corridor crosses a room's boundary
/// is not controlled there at all, and cannot be without routing corridors between
/// boundaries instead of centres.
///
/// So the width is imposed here instead, after the fact, by walling up the excess.
///
/// Openings that cannot be reduced safely keep their full width and get no door. That is
/// deliberate rather than a fallback: a wide arch is a perfectly good way into a room, and
/// the alternative — forcing every opening to door width — would either disconnect the
/// dungeon or fill it with identical one-cell gaps.
/// </summary>
public static class DoorwayNormalizer
{
    /// <summary>Cells either side of a doorway that read as its frame rather than as wall.</summary>
    private const int JambDepth = 2;

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    /// <summary>
    /// Finds every room opening, narrows the ones it can to <paramref name="doorwayWidth"/>
    /// and marks those as <see cref="CellType.Door"/>. Openings left at full width are not
    /// marked, so nothing downstream tries to hang a door in them.
    /// </summary>
    public static void Apply(DungeonLayout layout, List<Room> rooms, int doorwayWidth)
    {
        if (rooms == null || rooms.Count == 0) return;

        doorwayWidth = Mathf.Max(1, doorwayWidth);

        // Connectivity is checked against a fixed origin rather than the spawn cell,
        // because room roles have not been assigned yet at this point in the pipeline.
        // Any walkable cell does: reachability is symmetric.
        Vector2Int origin = rooms[0].Center;

        var candidates = new List<Vector2Int>();
        var candidateSet = new HashSet<Vector2Int>();
        var clump = new List<Vector2Int>();
        var kept = new List<Vector2Int>();

        foreach (var room in rooms)
        {
            candidates.Clear();
            candidateSet.Clear();

            // Walk the cells just outside the room. Phrased against the room's own cells
            // rather than the four sides of its bounding box, because a room is not
            // necessarily a rectangle and an L-shaped one has border cells inside its box.
            foreach (Vector2Int cell in room.Cells)
            {
                for (int i = 0; i < Neighbours.Length; i++)
                {
                    Vector2Int next = cell + Neighbours[i];
                    if (room.Contains(next) || !candidateSet.Add(next)) continue;

                    if (IsOpening(layout, rooms, next)) candidates.Add(next);
                    else candidateSet.Remove(next);
                }
            }

            // Iteration follows the list rather than the set, so the result cannot depend
            // on hash ordering — which would break the seed-rebuilds-the-same-dungeon
            // guarantee in a way that is very hard to notice.
            foreach (Vector2Int start in candidates)
            {
                if (!candidateSet.Remove(start)) continue;

                CollectClump(candidateSet, start, clump);
                NarrowOpening(layout, clump, kept, doorwayWidth, origin);
            }
        }
    }

    /// <summary>
    /// One connected group of border cells: a single opening. Grouping matters — a
    /// corridor running alongside a room would otherwise be read as a row of separate
    /// doorways where there is really one long gap.
    /// </summary>
    private static void CollectClump(HashSet<Vector2Int> remaining, Vector2Int start,
        List<Vector2Int> clump)
    {
        clump.Clear();

        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            clump.Add(cell);

            for (int i = 0; i < Neighbours.Length; i++)
            {
                if (remaining.Remove(cell + Neighbours[i])) queue.Enqueue(cell + Neighbours[i]);
            }
        }
    }

    /// <summary>
    /// Cuts one opening down to the door width, or leaves it alone when it cannot be cut.
    ///
    /// The cells that survive are the middle ones, so the doorway lines up with the
    /// corridor behind it instead of hugging one jamb.
    /// </summary>
    private static void NarrowOpening(DungeonLayout layout, List<Vector2Int> clump,
        List<Vector2Int> kept, int doorwayWidth, Vector2Int origin)
    {
        // An opening that wraps a corner is not a doorway — it is a room whose side is
        // open onto a corridor running past it. Narrowing that produces a gap in a wall
        // at a place a door could not plausibly hang, so it is left as an arch.
        if (!IsCollinear(clump, out bool alongY)) return;

        if (clump.Count <= doorwayWidth)
        {
            // Already no wider than a door. Nothing to wall up; the prefabs fill the gap.
            foreach (Vector2Int cell in clump) layout[cell] = CellType.Door;
            return;
        }

        clump.Sort((a, b) => alongY ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        // A collinear clump is contiguous by construction — it was grown four-way over
        // the candidate cells — so the middle run is just a slice.
        int first = (clump.Count - doorwayWidth) / 2;

        kept.Clear();
        for (int i = first; i < first + doorwayWidth; i++) kept.Add(clump[i]);

        // The excess is walled up, and the couple of cells nearest the door become Pillar
        // rather than Wall. Both are solid and both stop vision, so this changes nothing
        // mechanically — but the painter gives Pillar its own tile, so the cells flanking
        // a door read as built jambs instead of as the bedrock the opening was cut through.
        //
        // Only the nearest few, and that limit is doing real work. Making the whole excess
        // Pillar looked right on a three-cell opening and absurd on a wide one: a corridor
        // running the length of a room's wall is one opening fourteen cells across, and
        // walling it produced fourteen pillars in a row — a colonnade embedded in a wall
        // rather than a door frame. Measured at 3.4 runs of six or more per map.
        int last = first + doorwayWidth - 1;
        for (int i = 0; i < clump.Count; i++)
        {
            if (i >= first && i <= last) continue;

            int distance = i < first ? first - i : i - last;
            layout[clump[i]] = distance <= JambDepth ? CellType.Pillar : CellType.Wall;
        }

        // Walling up part of an opening can cut the dungeon in two — most obviously when
        // what looked like one wide opening was really the only route through, and the
        // cells now being walled were carrying it. Cheaper to try it and check than to
        // reason about which openings are load-bearing.
        if (RoomCorridorGenerator.CountReachable(layout, origin) == layout.CountWalkable())
        {
            foreach (Vector2Int cell in kept) layout[cell] = CellType.Door;
            return;
        }

        foreach (Vector2Int cell in clump) layout[cell] = CellType.Floor;
    }

    /// <summary>
    /// True when every cell shares an x or shares a y. <paramref name="alongY"/> then says
    /// which way the opening runs, so it can be sorted along itself.
    /// </summary>
    private static bool IsCollinear(List<Vector2Int> clump, out bool alongY)
    {
        alongY = false;
        if (clump.Count == 0) return false;

        bool sameX = true;
        bool sameY = true;

        for (int i = 1; i < clump.Count; i++)
        {
            if (clump[i].x != clump[0].x) sameX = false;
            if (clump[i].y != clump[0].y) sameY = false;
        }

        // A single cell is both; treating it as running along x is arbitrary and fine.
        alongY = sameX;
        return sameX || sameY;
    }

    /// <summary>
    /// True when the cell is open ground outside every room — the corridor side of an
    /// opening. Marking the corridor side rather than the room side is what puts the door
    /// prefab in the gap instead of inside the room.
    /// </summary>
    private static bool IsOpening(DungeonLayout layout, List<Room> rooms, Vector2Int cell)
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
}
