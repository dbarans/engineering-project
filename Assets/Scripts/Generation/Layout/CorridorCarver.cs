using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Carves the corridors between rooms.
///
/// A corridor used to be a one-cell tunnel taking one turn. That is the shortest route
/// between two rooms and also the least interesting: uniform width means the player
/// always knows exactly how much room they have to dodge in, and a single elbow means
/// most of a corridor can be read from either end.
///
/// Three things change that, in rising order of how much they matter:
/// width that varies along the run, so a corridor has wide stretches to fight in and
/// pinches to be caught in; blind alcoves off the sides, which are somewhere for
/// something to be standing that the player walks straight past; and an optional second
/// bend, which removes the end-to-end sightline entirely.
/// </summary>
public static class CorridorCarver
{
    /// <summary>
    /// Width a run is held at for its first and last few cells, whatever the segment
    /// width says. The pinch is doing real work in the middle of a corridor: a narrowing
    /// is the moment the player has to commit, and the natural place for a fight to
    /// become unavoidable.
    ///
    /// It is <em>not</em> what sets the width of a room's doorway, despite what this
    /// constant used to be called. Runs go from room centre to room centre, so a run's
    /// ends fall inside a room (where the cells are already floor and narrowing does
    /// nothing) and at the bends — never at the boundary a corridor actually crosses into
    /// a room. Doorway width is imposed afterwards by <see cref="DoorwayNormalizer"/>.
    /// </summary>
    private const int PinchWidth = 1;

    /// <summary>Cells at each end of a run held at <see cref="PinchWidth"/>.</summary>
    private const int PinchLength = 3;

    /// <summary>Carves every link, then records the alcoves that were opened off them.</summary>
    public static void CarveAll(DungeonLayout layout, List<Room> rooms, List<RoomLink> links,
        LayoutParams p, DeterministicRandom random)
    {
        for (int i = 0; i < links.Count; i++)
        {
            RoomLink link = links[i];
            Carve(layout, rooms[link.RoomA].Center, rooms[link.RoomB].Center, p,
                random.Derive($"corridor{i}"));
        }

        if (p.AlcoveChance > 0f)
            CarveAlcoves(layout, rooms, p, random.Derive("alcoves"));
    }

    /// <summary>
    /// One corridor, as two or three axis-aligned runs.
    ///
    /// The three-run form puts the turn at a random point along the main axis instead of
    /// at one end, which is what kills the sightline: an L can be seen down from the
    /// corner, a Z cannot be seen down from anywhere.
    /// </summary>
    private static void Carve(DungeonLayout layout, Vector2Int from, Vector2Int to,
        LayoutParams p, DeterministicRandom random)
    {
        bool doubleBend = random.Chance(p.DoubleBendChance);
        bool horizontalFirst = random.Chance(0.5f);

        if (!doubleBend)
        {
            if (horizontalFirst)
            {
                CarveRun(layout, from, new Vector2Int(to.x, from.y), p, random);
                CarveRun(layout, new Vector2Int(to.x, from.y), to, p, random);
            }
            else
            {
                CarveRun(layout, from, new Vector2Int(from.x, to.y), p, random);
                CarveRun(layout, new Vector2Int(from.x, to.y), to, p, random);
            }
            return;
        }

        if (horizontalFirst)
        {
            int mid = MidPoint(from.x, to.x, random);
            var a = new Vector2Int(mid, from.y);
            var b = new Vector2Int(mid, to.y);

            CarveRun(layout, from, a, p, random);
            CarveRun(layout, a, b, p, random);
            CarveRun(layout, b, to, p, random);
        }
        else
        {
            int mid = MidPoint(from.y, to.y, random);
            var a = new Vector2Int(from.x, mid);
            var b = new Vector2Int(to.x, mid);

            CarveRun(layout, from, a, p, random);
            CarveRun(layout, a, b, p, random);
            CarveRun(layout, b, to, p, random);
        }
    }

    /// <summary>
    /// A turning point in the middle third of the span. Nearer the ends and the corridor
    /// degenerates back into an L.
    /// </summary>
    private static int MidPoint(int from, int to, DeterministicRandom random)
    {
        int min = Mathf.Min(from, to);
        int max = Mathf.Max(from, to);
        int span = max - min;
        if (span < 4) return min + span / 2;

        return random.RangeInclusive(min + span / 3, max - span / 3);
    }

    /// <summary>
    /// One straight run, split into segments that each get their own width.
    ///
    /// Width is applied symmetrically around the centre line so a widening does not drag
    /// the corridor sideways off the room centres it was routed between.
    /// </summary>
    private static void CarveRun(DungeonLayout layout, Vector2Int from, Vector2Int to,
        LayoutParams p, DeterministicRandom random)
    {
        Vector2Int step = from.x == to.x
            ? new Vector2Int(0, to.y > from.y ? 1 : -1)
            : new Vector2Int(to.x > from.x ? 1 : -1, 0);

        int length = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);
        if (length == 0)
        {
            CarveCross(layout, from, PinchWidth, step);
            return;
        }

        // Segments of four to nine cells: shorter reads as noise, longer and a corridor
        // only gets one width over its whole length again.
        int segmentLength = random.RangeInclusive(4, 9);
        int width = PickWidth(p, random);

        for (int i = 0; i <= length; i++)
        {
            if (i > 0 && i % segmentLength == 0)
            {
                width = PickWidth(p, random);
                segmentLength = random.RangeInclusive(4, 9);
            }

            // Both ends pinch back down regardless of the segment's width.
            int effective = i < PinchLength || i > length - PinchLength
                ? PinchWidth
                : width;

            CarveCross(layout, from + step * i, effective, step);
        }
    }

    /// <summary>
    /// Width for one segment. Weighted towards the narrow end: wide stretches only read
    /// as wide if most of the dungeon is not.
    /// </summary>
    private static int PickWidth(LayoutParams p, DeterministicRandom random)
    {
        float roll = random.NextFloat();
        int width = roll < 0.55f ? p.CorridorWidth
            : roll < 0.85f ? p.CorridorWidth + 1
            : p.MaxCorridorWidth;

        return Mathf.Clamp(width, 1, Mathf.Max(1, p.MaxCorridorWidth));
    }

    /// <summary>Carves one slice of corridor, centred on the run's centre line.</summary>
    private static void CarveCross(DungeonLayout layout, Vector2Int centre, int width, Vector2Int step)
    {
        // Perpendicular to the direction of travel.
        var across = new Vector2Int(step.y, step.x);
        if (across == Vector2Int.zero) across = Vector2Int.right;

        int half = width / 2;
        for (int offset = -half; offset <= width - half - 1; offset++)
            CarveCell(layout, centre + across * offset);
    }

    // ---------------------------------------------------------------- alcoves

    /// <summary>
    /// Opens blind pockets off the sides of corridors.
    ///
    /// An alcove is a cell the player walks past without ever having looked into: the
    /// mouth is behind them by the time its interior enters the view cone. That makes it
    /// the layout's natural ambush slot, which is why they are recorded on the layout
    /// rather than rediscovered by the populator guessing at geometry later.
    /// </summary>
    private static void CarveAlcoves(DungeonLayout layout, List<Room> rooms, LayoutParams p,
        DeterministicRandom random)
    {
        // Iterating the grid rather than replaying the corridor routes keeps this working
        // no matter how the corridors were carved, including where two of them merged.
        for (int y = 2; y < layout.Height - 2; y++)
        {
            for (int x = 2; x < layout.Width - 2; x++)
            {
                var cell = new Vector2Int(x, y);
                if (layout[cell] != CellType.Floor) continue;
                if (IsInAnyRoom(rooms, cell)) continue;
                if (!random.Chance(p.AlcoveChance)) continue;

                TryCarveAlcove(layout, rooms, cell, random);
            }
        }
    }

    private static void TryCarveAlcove(DungeonLayout layout, List<Room> rooms, Vector2Int from,
        DeterministicRandom random)
    {
        var directions = new List<Vector2Int>(Neighbours);
        random.Shuffle(directions);

        foreach (Vector2Int direction in directions)
        {
            int depth = random.RangeInclusive(1, 2);
            var pocket = new List<Vector2Int>(depth);

            bool usable = true;
            for (int i = 1; i <= depth; i++)
            {
                Vector2Int cell = from + direction * i;

                // The pocket must be cut out of untouched rock and must not break into
                // anything — an alcove that turns out to be a shortcut is just a corridor.
                if (layout[cell] != CellType.Wall || IsInAnyRoom(rooms, cell) ||
                    OpensOntoSomethingElse(layout, cell, from))
                {
                    usable = false;
                    break;
                }
                pocket.Add(cell);
            }

            if (!usable || pocket.Count == 0) continue;

            foreach (Vector2Int cell in pocket) layout[cell] = CellType.Floor;
            layout.AddAlcove(pocket[pocket.Count - 1]);
            return;
        }
    }

    /// <summary>
    /// True when the candidate cell touches open ground other than the corridor it is
    /// being cut from, which would make the pocket a passage instead of a dead end.
    /// </summary>
    private static bool OpensOntoSomethingElse(DungeonLayout layout, Vector2Int cell, Vector2Int origin)
    {
        for (int i = 0; i < Neighbours.Length; i++)
        {
            Vector2Int next = cell + Neighbours[i];
            if (next == origin) continue;
            if (layout[next] != CellType.Wall) return true;
        }
        return false;
    }

    private static bool IsInAnyRoom(List<Room> rooms, Vector2Int cell)
    {
        foreach (Room room in rooms)
        {
            if (room.Contains(cell)) return true;
        }
        return false;
    }

    /// <summary>
    /// Carves one corridor cell, refusing the outermost ring so the map keeps a solid
    /// border even when a room centre sits close to the edge.
    /// </summary>
    private static void CarveCell(DungeonLayout layout, Vector2Int cell)
    {
        if (cell.x < 1 || cell.y < 1 || cell.x >= layout.Width - 1 || cell.y >= layout.Height - 1)
            return;

        layout[cell] = CellType.Floor;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };
}
