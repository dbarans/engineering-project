using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Grid-based A* pathfinder (4-direction movement). Scratch buffers (score/visited/parent grids)
/// are cached per <see cref="PathfindingGrid"/> and reused across searches instead of being
/// allocated on every call — each chasing enemy re-paths every repathInterval, so this avoids
/// repeated GC allocations proportional to grid size × enemy count × repath frequency.
/// </summary>
public static class AStarPathfinder
{
    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    private class Scratch
    {
        public int Width;
        public int Height;
        public float[,] GScore;
        public bool[,] Closed;
        public bool[,] InOpen;
        public int[,] ParentX;
        public int[,] ParentY;
        public readonly List<Vector2Int> Open = new List<Vector2Int>();
    }

    private static readonly ConditionalWeakTable<PathfindingGrid, Scratch> ScratchByGrid = new ConditionalWeakTable<PathfindingGrid, Scratch>();

    /// <summary>
    /// Finds a path from start to target world position.
    /// Returns an empty list when no route is available.
    /// </summary>
    public static List<Vector2> FindPath(PathfindingGrid grid, Vector2 startWorld, Vector2 targetWorld)
    {
        var result = new List<Vector2>();
        if (grid == null) return result;

        if (!grid.WorldToCell(startWorld, out int startX, out int startY)) return result;
        if (!grid.WorldToCell(targetWorld, out int targetX, out int targetY)) return result;

        Node startNode = grid.GetNode(startX, startY);
        Node targetNode = grid.GetNode(targetX, targetY);
        if (startNode == null || targetNode == null) return result;
        if (!targetNode.Walkable) return result;

        int width = grid.Width;
        int height = grid.Height;

        Scratch scratch = GetScratch(grid, width, height);
        float[,] gScore = scratch.GScore;
        bool[,] closed = scratch.Closed;
        bool[,] inOpen = scratch.InOpen;
        int[,] parentX = scratch.ParentX;
        int[,] parentY = scratch.ParentY;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                gScore[x, y] = float.PositiveInfinity;
                closed[x, y] = false;
                inOpen[x, y] = false;
                parentX[x, y] = -1;
                parentY[x, y] = -1;
            }
        }

        var open = scratch.Open;
        open.Clear();
        var start = new Vector2Int(startX, startY);
        var target = new Vector2Int(targetX, targetY);

        gScore[start.x, start.y] = 0f;
        open.Add(start);
        inOpen[start.x, start.y] = true;

        while (open.Count > 0)
        {
            int currentIndex = GetBestOpenIndex(open, gScore, target);
            Vector2Int current = open[currentIndex];

            open.RemoveAt(currentIndex);
            inOpen[current.x, current.y] = false;
            closed[current.x, current.y] = true;

            if (current == target)
            {
                return ReconstructPath(grid, parentX, parentY, start, target);
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height) continue;
                if (closed[next.x, next.y]) continue;

                Node nextNode = grid.GetNode(next.x, next.y);
                if (nextNode == null || !nextNode.Walkable) continue;

                float tentativeG = gScore[current.x, current.y] + 1f;
                if (tentativeG >= gScore[next.x, next.y]) continue;

                parentX[next.x, next.y] = current.x;
                parentY[next.x, next.y] = current.y;
                gScore[next.x, next.y] = tentativeG;

                if (!inOpen[next.x, next.y])
                {
                    open.Add(next);
                    inOpen[next.x, next.y] = true;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the cached scratch buffers for this grid, (re)allocating them only when the
    /// grid's dimensions changed since the last search (or on first use).
    /// </summary>
    private static Scratch GetScratch(PathfindingGrid grid, int width, int height)
    {
        if (!ScratchByGrid.TryGetValue(grid, out Scratch scratch))
        {
            scratch = new Scratch();
            ScratchByGrid.Add(grid, scratch);
        }

        if (scratch.Width != width || scratch.Height != height)
        {
            scratch.Width = width;
            scratch.Height = height;
            scratch.GScore = new float[width, height];
            scratch.Closed = new bool[width, height];
            scratch.InOpen = new bool[width, height];
            scratch.ParentX = new int[width, height];
            scratch.ParentY = new int[width, height];
        }

        return scratch;
    }

    private static int GetBestOpenIndex(List<Vector2Int> open, float[,] gScore, Vector2Int target)
    {
        int bestIndex = 0;
        Vector2Int bestCell = open[0];
        float bestF = gScore[bestCell.x, bestCell.y] + Heuristic(bestCell, target);

        for (int i = 1; i < open.Count; i++)
        {
            Vector2Int cell = open[i];
            float g = gScore[cell.x, cell.y];
            float h = Heuristic(cell, target);
            float f = g + h;

            if (f < bestF || (Mathf.Approximately(f, bestF) && h < Heuristic(bestCell, target)))
            {
                bestIndex = i;
                bestCell = cell;
                bestF = f;
            }
        }

        return bestIndex;
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static List<Vector2> ReconstructPath(
        PathfindingGrid grid,
        int[,] parentX,
        int[,] parentY,
        Vector2Int start,
        Vector2Int target)
    {
        var reversed = new List<Vector2>();
        Vector2Int current = target;

        while (current != start)
        {
            Node node = grid.GetNode(current.x, current.y);
            if (node == null) break;
            reversed.Add(node.WorldPos);

            int px = parentX[current.x, current.y];
            int py = parentY[current.x, current.y];
            if (px < 0 || py < 0) break;
            current = new Vector2Int(px, py);
        }

        reversed.Reverse();
        return reversed;
    }
}
