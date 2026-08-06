using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Line-of-sight over the abstract cell grid.
///
/// Exists to make one design goal checkable. "The player should not take in a whole room
/// at a glance" is the difference between a dungeon that feels oppressive and one that
/// feels like a floor plan, but as prose it cannot be verified, tuned or reported. As a
/// number — the share of a room visible from its own doorway — it can be all three:
/// the interior decorator uses it as the target it works towards, and
/// <see cref="DungeonMetrics"/> reports it so a parameter change can be defended with a
/// figure instead of an impression.
///
/// Deliberately computed on the grid rather than by raycasting the physics world. The
/// layout assembly is engine-free by design, which is what lets it run in tests outside
/// Unity; and generation needs this answer thousands of times per dungeon, long before
/// any collider exists.
///
/// The algorithm is recursive symmetric shadowcasting over the four cardinal quadrants.
/// It is the standard roguelike choice because it is symmetric — if A sees B then B sees
/// A — which naive ray-per-cell casting is not, and asymmetric vision in a stealth game
/// reads as a bug every time.
/// </summary>
public static class VisibilityAnalysis
{
    /// <summary>
    /// Share of the room's walkable cells visible from <paramref name="from"/>, in 0..1.
    ///
    /// 1.0 means the room gives itself away completely from that spot; an empty rectangle
    /// scores almost exactly that. Pillars, partitions and non-rectangular plans push it
    /// down, and below roughly 0.6 the player has to walk in blind to learn the room.
    /// </summary>
    public static float VisibleFraction(DungeonLayout layout, Room room, Vector2Int from, int radius)
    {
        if (layout == null || room == null || room.Area == 0) return 0f;

        var visible = new HashSet<Vector2Int>();
        ComputeVisible(layout, from, radius, visible);

        int total = 0;
        int seen = 0;

        foreach (Vector2Int cell in room.Cells)
        {
            if (!layout.IsWalkable(cell)) continue;
            total++;
            if (visible.Contains(cell)) seen++;
        }

        return total > 0 ? seen / (float)total : 0f;
    }

    /// <summary>
    /// Averaged <see cref="VisibleFraction"/> over every doorway leading into the room,
    /// which is what the player actually experiences: they arrive through an opening, not
    /// by teleporting to the middle.
    ///
    /// Falls back to the room's centre when it has no doorways yet, so the decorator can
    /// still be run before doors are marked.
    /// </summary>
    public static float VisibleFractionFromEntrances(DungeonLayout layout, Room room, int radius)
    {
        if (layout == null || room == null || room.Area == 0) return 0f;

        List<Vector2Int> entrances = Entrances(layout, room);
        if (entrances.Count == 0)
            return VisibleFraction(layout, room, room.Center, radius);

        float total = 0f;
        foreach (Vector2Int entrance in entrances)
            total += VisibleFraction(layout, room, entrance, radius);

        return total / entrances.Count;
    }

    /// <summary>
    /// The room's own cells that sit next to a doorway — where the player stands on the
    /// first step in. The doorway cell itself is outside the room, and measuring from it
    /// would look through the door frame at an angle the player never has.
    /// </summary>
    public static List<Vector2Int> Entrances(DungeonLayout layout, Room room)
    {
        var entrances = new List<Vector2Int>();

        foreach (Vector2Int cell in room.Cells)
        {
            if (!layout.IsWalkable(cell)) continue;

            for (int i = 0; i < Neighbours.Length; i++)
            {
                if (layout[cell + Neighbours[i]] != CellType.Door) continue;
                entrances.Add(cell);
                break;
            }
        }

        return entrances;
    }

    /// <summary>
    /// Fills <paramref name="visible"/> with every cell in line of sight of the origin,
    /// out to <paramref name="radius"/>.
    /// </summary>
    public static void ComputeVisible(DungeonLayout layout, Vector2Int origin, int radius,
        HashSet<Vector2Int> visible)
    {
        visible.Clear();
        if (!layout.Contains(origin.x, origin.y)) return;

        visible.Add(origin);

        // One scan per quadrant. Each quadrant is swept in its own coordinate frame and
        // mapped back through Transform, which is what keeps the core loop free of the
        // eight-way special casing the naive version needs.
        for (int quadrant = 0; quadrant < 4; quadrant++)
            Scan(layout, origin, radius, quadrant, 1, -1f, 1f, visible);
    }

    /// <summary>
    /// Sweeps one row of a quadrant between two slopes, recursing whenever an obstacle
    /// splits the visible span in two.
    /// </summary>
    private static void Scan(DungeonLayout layout, Vector2Int origin, int radius, int quadrant,
        int depth, float slopeStart, float slopeEnd, HashSet<Vector2Int> visible)
    {
        if (slopeStart > slopeEnd || depth > radius) return;

        bool previousWasWall = false;
        float nextSlopeStart = slopeStart;

        int minColumn = Mathf.RoundToInt(depth * slopeStart);
        int maxColumn = Mathf.RoundToInt(depth * slopeEnd);

        for (int column = minColumn; column <= maxColumn; column++)
        {
            Vector2Int cell = origin + Transform(quadrant, depth, column);

            // Slopes of this cell's near and far edges, as seen from the origin.
            float cellStart = (column - 0.5f) / depth;
            float cellEnd = (column + 0.5f) / depth;

            if (cellEnd < slopeStart) continue;
            if (cellStart > slopeEnd) break;

            // Round distance rather than truncating it, so the lit area is a disc and not
            // a diamond — a diamond is instantly readable as an artefact on screen.
            if (depth * depth + column * column <= radius * radius && layout.Contains(cell.x, cell.y))
                visible.Add(cell);

            bool isWall = layout.BlocksVision(cell.x, cell.y);

            if (previousWasWall && !isWall)
            {
                // Leaving an obstacle: the open span resumes at this cell's near edge.
                nextSlopeStart = cellStart;
            }
            else if (!previousWasWall && isWall)
            {
                // Entering an obstacle: recurse into the span that was open up to here,
                // then carry on looking for where the obstacle ends.
                Scan(layout, origin, radius, quadrant, depth + 1, nextSlopeStart, cellStart, visible);
            }

            previousWasWall = isWall;
        }

        // The row ended in open ground, so the remaining span continues one row further.
        if (!previousWasWall)
            Scan(layout, origin, radius, quadrant, depth + 1, nextSlopeStart, slopeEnd, visible);
    }

    /// <summary>Maps a (depth, column) pair in quadrant space onto a grid offset.</summary>
    private static Vector2Int Transform(int quadrant, int depth, int column)
    {
        switch (quadrant)
        {
            case 0: return new Vector2Int(column, depth);   // north
            case 1: return new Vector2Int(column, -depth);  // south
            case 2: return new Vector2Int(depth, column);   // east
            default: return new Vector2Int(-depth, column); // west
        }
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };
}
