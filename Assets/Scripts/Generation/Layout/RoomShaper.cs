using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cuts a placed rectangle down to a room's actual floor plan.
///
/// Placement still proposes rectangles, because rejection sampling against rectangles is
/// cheap and leaves clean rock between rooms. What gets carved is the shape this class
/// returns, so the rectangle ends up being the room's plot rather than the room.
///
/// The shapes are mostly orthogonal on purpose. This is a dungeon, not a cave system:
/// arms, corners and ring corridors read as something that was built and then fell apart,
/// while blobs everywhere read as geology and lose the architecture. <see cref="RoomShape.Cavern"/>
/// exists for contrast and is deliberately the rarest.
///
/// Every shape is produced from the supplied <see cref="DeterministicRandom"/> alone, so
/// a seed fixes the floor plan exactly as it fixes everything else.
/// </summary>
public static class RoomShaper
{
    /// <summary>Smallest side that can lose a bite and still leave usable arms.</summary>
    private const int MinSideForCarving = 7;

    /// <summary>Smallest room that can hold a ring corridor around a solid core.</summary>
    private const int MinSideForRing = 9;

    /// <summary>
    /// Picks a shape for the plot and returns the cells it covers.
    ///
    /// Returns null when the shape came out degenerate — disconnected, or so eaten away
    /// that nothing usable is left. The caller is expected to fall back to the plain
    /// rectangle rather than to drop the room, because a room that fails to be
    /// interesting is still better than a hole in the dungeon.
    /// </summary>
    public static List<Vector2Int> Shape(RectInt plot, RoomShape shape, DeterministicRandom random)
    {
        List<Vector2Int> cells = shape switch
        {
            RoomShape.Ell => CarveCorners(plot, random, 1),
            RoomShape.Tee => CarveCorners(plot, random, 2),
            RoomShape.Ring => CarveRing(plot, random),
            RoomShape.Cavern => CarveCavern(plot, random),
            _ => RectangleCells(plot)
        };

        if (cells == null || cells.Count < MinimumArea(plot)) return null;
        return IsConnected(cells) ? cells : null;
    }

    /// <summary>
    /// Chooses a shape for a plot, weighted by what its size can actually support and by
    /// the room's role.
    ///
    /// Camps stay rectangular: a safe room has to be legible at a glance, and the player
    /// needs to be able to see that they are alone in it. Everything else leans away from
    /// plain rectangles the larger it gets, because a big empty rectangle is the single
    /// worst offender for reading a room in one look.
    /// </summary>
    public static RoomShape PickShape(RectInt plot, DeterministicRandom random)
    {
        int shortSide = Mathf.Min(plot.width, plot.height);

        // Too small to carve into anything but itself.
        if (shortSide < MinSideForCarving) return RoomShape.Rectangle;

        var weights = new List<(RoomShape shape, float weight)>
        {
            (RoomShape.Rectangle, shortSide >= 11 ? 0.8f : 2f),
            (RoomShape.Ell, 2.5f),
            (RoomShape.Tee, shortSide >= 9 ? 1.5f : 0f),
            (RoomShape.Ring, shortSide >= MinSideForRing ? 1.5f : 0f),
            (RoomShape.Cavern, 0.7f)
        };

        float total = 0f;
        foreach (var entry in weights) total += entry.weight;
        if (total <= 0f) return RoomShape.Rectangle;

        float roll = random.NextFloat() * total;
        foreach (var entry in weights)
        {
            roll -= entry.weight;
            if (roll <= 0f) return entry.shape;
        }
        return RoomShape.Rectangle;
    }

    // ---------------------------------------------------------------- shapes

    /// <summary>
    /// Bites one or two rectangular chunks out of the plot's corners. One bite gives an
    /// L, two adjacent bites give a T or a U depending on which corners were taken.
    ///
    /// The bite is always strictly smaller than the plot in both axes, so the remaining
    /// arms stay wide enough to walk down and the room cannot fall apart.
    /// </summary>
    private static List<Vector2Int> CarveCorners(RectInt plot, DeterministicRandom random, int bites)
    {
        var removed = new HashSet<Vector2Int>();
        var corners = new List<int> { 0, 1, 2, 3 };
        random.Shuffle(corners);

        for (int i = 0; i < bites && i < corners.Count; i++)
        {
            // Between a third and a bit over half of each side: smaller leaves the shape
            // unreadable, larger starts pinching the arms down to nothing.
            int biteWidth = random.RangeInclusive(plot.width / 3, plot.width * 4 / 7);
            int biteHeight = random.RangeInclusive(plot.height / 3, plot.height * 4 / 7);
            if (biteWidth <= 0 || biteHeight <= 0) continue;

            bool right = corners[i] == 1 || corners[i] == 2;
            bool top = corners[i] >= 2;

            int xMin = right ? plot.xMax - biteWidth : plot.xMin;
            int yMin = top ? plot.yMax - biteHeight : plot.yMin;

            for (int y = yMin; y < yMin + biteHeight; y++)
            {
                for (int x = xMin; x < xMin + biteWidth; x++)
                    removed.Add(new Vector2Int(x, y));
            }
        }

        var cells = new List<Vector2Int>(plot.width * plot.height);
        for (int y = plot.yMin; y < plot.yMax; y++)
        {
            for (int x = plot.xMin; x < plot.xMax; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!removed.Contains(cell)) cells.Add(cell);
            }
        }
        return cells;
    }

    /// <summary>
    /// Leaves a solid block in the middle, so the room becomes a corridor running round
    /// it. This is the strongest shape in the set for the vision system: the player has
    /// to pick a side, and anything following them can come the other way.
    /// </summary>
    private static List<Vector2Int> CarveRing(RectInt plot, DeterministicRandom random)
    {
        // The walkable ring is two or three cells wide. One cell reads as a corridor
        // rather than a room, and four is wide enough to see across the core.
        int ringWidth = random.RangeInclusive(2, 3);

        int coreWidth = plot.width - 2 * ringWidth;
        int coreHeight = plot.height - 2 * ringWidth;
        if (coreWidth < 2 || coreHeight < 2) return null;

        var core = new RectInt(plot.xMin + ringWidth, plot.yMin + ringWidth, coreWidth, coreHeight);

        var cells = new List<Vector2Int>(plot.width * plot.height - coreWidth * coreHeight);
        for (int y = plot.yMin; y < plot.yMax; y++)
        {
            for (int x = plot.xMin; x < plot.xMax; x++)
            {
                if (x >= core.xMin && x < core.xMax && y >= core.yMin && y < core.yMax) continue;
                cells.Add(new Vector2Int(x, y));
            }
        }
        return cells;
    }

    /// <summary>
    /// Random fill smoothed by the usual 4-5 cellular automata rule, then reduced to its
    /// largest connected component.
    ///
    /// The component filter is not optional. Smoothing reliably leaves detached pockets,
    /// and a pocket is a piece of floor no corridor reaches and no player can stand in —
    /// invisible on a screenshot, fatal to the connectivity check later.
    /// </summary>
    private static List<Vector2Int> CarveCavern(RectInt plot, DeterministicRandom random)
    {
        int width = plot.width;
        int height = plot.height;
        var open = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // The border is seeded solid so the cave pulls away from the plot edge
                // and leaves rock between neighbouring rooms.
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                open[x, y] = !border && random.Chance(0.56f);
            }
        }

        for (int pass = 0; pass < 4; pass++) open = Smooth(open, width, height);

        var cells = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (open[x, y]) cells.Add(new Vector2Int(plot.xMin + x, plot.yMin + y));
            }
        }

        return LargestComponent(cells);
    }

    private static bool[,] Smooth(bool[,] open, int width, int height)
    {
        var next = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int solid = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = x + dx;
                        int ny = y + dy;
                        // Outside the plot counts as solid, which is what pulls the cave
                        // inwards instead of letting it spill over the edge.
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height || !open[nx, ny]) solid++;
                    }
                }

                next[x, y] = solid < 5;
            }
        }

        return next;
    }

    // ---------------------------------------------------------------- helpers

    private static List<Vector2Int> RectangleCells(RectInt plot)
    {
        var cells = new List<Vector2Int>(plot.width * plot.height);
        for (int y = plot.yMin; y < plot.yMax; y++)
        {
            for (int x = plot.xMin; x < plot.xMax; x++)
                cells.Add(new Vector2Int(x, y));
        }
        return cells;
    }

    /// <summary>
    /// A shaped room has to keep enough of its plot to be worth the placement. Below this
    /// the plot is better spent on a plain rectangle.
    /// </summary>
    private static int MinimumArea(RectInt plot)
    {
        return Mathf.Max(9, plot.width * plot.height / 4);
    }

    private static bool IsConnected(List<Vector2Int> cells)
    {
        return LargestComponent(cells).Count == cells.Count;
    }

    /// <summary>
    /// The biggest 4-connected group in the set. Iteration follows the list rather than
    /// the lookup set, so the choice between equally sized components is deterministic.
    /// </summary>
    private static List<Vector2Int> LargestComponent(List<Vector2Int> cells)
    {
        var remaining = new HashSet<Vector2Int>(cells);
        var best = new List<Vector2Int>();
        var component = new List<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        foreach (Vector2Int start in cells)
        {
            if (!remaining.Remove(start)) continue;

            component.Clear();
            queue.Clear();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                component.Add(cell);

                for (int i = 0; i < Neighbours.Length; i++)
                {
                    Vector2Int next = cell + Neighbours[i];
                    if (remaining.Remove(next)) queue.Enqueue(next);
                }
            }

            if (component.Count > best.Count) best = new List<Vector2Int>(component);
        }

        return best;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };
}
