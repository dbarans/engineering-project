using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Grid-based A* pathfinder with 8-direction movement.
///
/// The search cost is kept proportional to the area actually explored, not to the size of the
/// grid — which matters once a dungeon is 500x500 (250 000 cells) and several dozen enemies
/// re-path a few times per second:
/// <list type="bullet">
/// <item>Scratch buffers are cached per <see cref="PathfindingGrid"/> and cleared by
/// <em>version stamping</em> (a per-search id) instead of being wiped cell by cell. Setup is
/// O(explored) rather than O(width * height).</item>
/// <item>The open set is a binary min-heap, so popping the best node is O(log n) instead of the
/// O(n) linear scan a plain list needs — the old version was quadratic in the frontier size.</item>
/// <item>The grid's connected-region ids reject an unreachable target in O(1), instead of
/// discovering it by flooding every walkable cell on the map.</item>
/// <item>A node budget caps the worst case; when it runs out the search returns the best partial
/// route found so far, so an agent keeps moving instead of stalling the frame.</item>
/// </list>
///
/// Diagonal steps cost sqrt(2) and are only allowed when both adjacent orthogonal cells are free,
/// so agents never slip through the corner between two walls.
/// </summary>
public static class AStarPathfinder
{
    /// <summary>Cost of an orthogonal step.</summary>
    public const float StraightCost = 1f;

    /// <summary>Cost of a diagonal step.</summary>
    public const float DiagonalCost = 1.41421356f;

    /// <summary>Default cap on expanded nodes before a search gives up and returns a partial path.</summary>
    public const int DefaultMaxExploredNodes = 4000;

    /// <summary>How far (in cells) a start/target inside an obstacle may be nudged to a free cell.</summary>
    private const int WalkableSearchRings = 3;

    // Slightly inflating the heuristic breaks ties toward nodes closer to the target. It makes the
    // result marginally sub-optimal (by at most this factor) but collapses the plateaus of equal-f
    // nodes that A* otherwise expands across open floor — the dominant cost on a large grid.
    private const float HeuristicTieBreak = 1.001f;

    // Orthogonals first, then diagonals; the corner test below relies on that grouping.
    private static readonly int[] DirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DirY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private class Scratch
    {
        public int Width;
        public int Height;

        public float[] GScore;
        public int[] Parent;
        // Stamp of the search that last touched a cell; anything older reads as "never visited",
        // which is what removes the full-grid clear from every search.
        public int[] Stamp;
        public bool[] Closed;
        public int SearchId;

        // Binary min-heap of cell indices, ordered by FScore. Duplicates are allowed (a cell is
        // pushed again when a cheaper route to it is found) and filtered out on pop via Closed,
        // which is cheaper than maintaining a decrease-key index.
        public int[] Heap;
        public float[] HeapF;
        public int HeapCount;
    }

    private static readonly ConditionalWeakTable<PathfindingGrid, Scratch> ScratchByGrid =
        new ConditionalWeakTable<PathfindingGrid, Scratch>();

    /// <summary>
    /// Finds a path from start to target world position, allocating a new list for the result.
    /// Prefer the overload taking a caller-owned list in per-frame code.
    /// </summary>
    public static List<Vector2> FindPath(PathfindingGrid grid, Vector2 startWorld, Vector2 targetWorld)
    {
        var result = new List<Vector2>();
        FindPath(grid, startWorld, targetWorld, result);
        return result;
    }

    /// <summary>
    /// Fills <paramref name="result"/> with waypoints from start to target and reports whether the
    /// target was actually reached.
    ///
    /// A false return still leaves the best partial route in <paramref name="result"/> (empty when
    /// nothing usable was found): the caller can walk as close as the map allows, while AI code
    /// reading <see cref="IPathStatusProvider.HasReachablePath"/> still learns the target is out of
    /// reach and can pick another one.
    /// </summary>
    /// <param name="maxExploredNodes">Node budget; values &lt;= 0 fall back to <see cref="DefaultMaxExploredNodes"/>.</param>
    public static bool FindPath(
        PathfindingGrid grid,
        Vector2 startWorld,
        Vector2 targetWorld,
        List<Vector2> result,
        int maxExploredNodes = DefaultMaxExploredNodes)
    {
        if (result == null) return false;
        result.Clear();
        if (grid == null) return false;
        if (maxExploredNodes <= 0) maxExploredNodes = DefaultMaxExploredNodes;

        if (!grid.WorldToCell(startWorld, out int startX, out int startY)) return false;
        if (!grid.WorldToCell(targetWorld, out int targetX, out int targetY)) return false;

        // An agent nudged into an inflated wall cell, or a player standing right against one, used
        // to mean "no path" for as long as it lasted — and a full-map flood every frame while it did.
        if (!grid.TryFindNearestWalkable(startX, startY, WalkableSearchRings, out startX, out startY)) return false;
        if (!grid.TryFindNearestWalkable(targetX, targetY, WalkableSearchRings, out targetX, out targetY)) return false;

        // Different connected regions can never be joined by a path. O(1) instead of exploring
        // every reachable cell only to fail.
        if (grid.GetRegionId(startX, startY) != grid.GetRegionId(targetX, targetY)) return false;

        int width = grid.Width;
        int height = grid.Height;
        int start = startY * width + startX;
        int target = targetY * width + targetX;
        if (start == target) return true;

        Scratch scratch = GetScratch(grid, width, height);
        int searchId = NextSearchId(scratch);

        float[] gScore = scratch.GScore;
        int[] parent = scratch.Parent;
        int[] stamp = scratch.Stamp;
        bool[] closed = scratch.Closed;

        scratch.HeapCount = 0;
        Touch(scratch, start, searchId);
        gScore[start] = 0f;
        parent[start] = -1;
        HeapPush(scratch, start, Heuristic(startX, startY, targetX, targetY));

        int explored = 0;
        int bestIndex = start;
        float bestHeuristic = Heuristic(startX, startY, targetX, targetY);

        while (scratch.HeapCount > 0)
        {
            int current = HeapPop(scratch);
            if (closed[current]) continue; // stale duplicate left over from a cheaper re-push
            closed[current] = true;

            if (current == target)
                return ReconstructPath(grid, parent, start, target, result);

            if (++explored >= maxExploredNodes) break;

            int x = current % width;
            int y = current / width;
            float currentG = gScore[current];

            for (int i = 0; i < DirX.Length; i++)
            {
                int nextX = x + DirX[i];
                int nextY = y + DirY[i];
                if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) continue;
                if (!grid.IsWalkable(nextX, nextY)) continue;

                bool diagonal = i >= 4;
                // No corner cutting: a diagonal step needs both orthogonal neighbours free,
                // otherwise the agent would clip through the corner where two walls meet.
                if (diagonal && (!grid.IsWalkable(nextX, y) || !grid.IsWalkable(x, nextY))) continue;

                int next = nextY * width + nextX;
                if (stamp[next] == searchId && closed[next]) continue;

                float tentativeG = currentG + (diagonal ? DiagonalCost : StraightCost);
                if (stamp[next] == searchId && tentativeG >= gScore[next]) continue;

                Touch(scratch, next, searchId);
                gScore[next] = tentativeG;
                parent[next] = current;

                float heuristic = Heuristic(nextX, nextY, targetX, targetY);
                if (heuristic < bestHeuristic)
                {
                    bestHeuristic = heuristic;
                    bestIndex = next;
                }

                HeapPush(scratch, next, tentativeG + heuristic);
            }
        }

        // Out of budget or out of reachable cells: hand back the route to whatever came closest,
        // so the agent still makes progress this frame.
        if (bestIndex != start)
            ReconstructPath(grid, parent, start, bestIndex, result);

        return false;
    }

    /// <summary>
    /// Marks a cell as belonging to the current search, resetting its per-search state the first
    /// time it is touched. This is what replaces clearing the whole grid up front.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Touch(Scratch scratch, int index, int searchId)
    {
        if (scratch.Stamp[index] == searchId) return;
        scratch.Stamp[index] = searchId;
        scratch.Closed[index] = false;
    }

    /// <summary>
    /// Octile distance: the exact cost of an unobstructed 8-direction walk, so the heuristic stays
    /// admissible for diagonal movement (Manhattan would over-estimate and distort paths).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Heuristic(int x, int y, int targetX, int targetY)
    {
        int dx = x > targetX ? x - targetX : targetX - x;
        int dy = y > targetY ? y - targetY : targetY - y;
        int min = dx < dy ? dx : dy;
        return ((dx + dy) + (DiagonalCost - 2f * StraightCost) * min) * HeuristicTieBreak;
    }

    /// <summary>
    /// Returns the cached scratch buffers for this grid, (re)allocating them only when the grid's
    /// dimensions changed since the last search (or on first use).
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
            int cellCount = width * height;
            scratch.Width = width;
            scratch.Height = height;
            scratch.GScore = new float[cellCount];
            scratch.Parent = new int[cellCount];
            scratch.Stamp = new int[cellCount];
            scratch.Closed = new bool[cellCount];
            scratch.SearchId = 0;
            scratch.Heap = new int[256];
            scratch.HeapF = new float[256];
            scratch.HeapCount = 0;
        }

        return scratch;
    }

    /// <summary>
    /// Hands out the next search id, wiping the stamps on the (astronomically rare) wrap so a
    /// stale stamp can never be mistaken for a fresh one.
    /// </summary>
    private static int NextSearchId(Scratch scratch)
    {
        if (scratch.SearchId == int.MaxValue)
        {
            System.Array.Clear(scratch.Stamp, 0, scratch.Stamp.Length);
            scratch.SearchId = 0;
        }

        return ++scratch.SearchId;
    }

    private static void HeapPush(Scratch scratch, int index, float fScore)
    {
        if (scratch.HeapCount == scratch.Heap.Length)
        {
            System.Array.Resize(ref scratch.Heap, scratch.Heap.Length * 2);
            System.Array.Resize(ref scratch.HeapF, scratch.HeapF.Length * 2);
        }

        int child = scratch.HeapCount++;
        scratch.Heap[child] = index;
        scratch.HeapF[child] = fScore;

        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (scratch.HeapF[parent] <= scratch.HeapF[child]) break;
            Swap(scratch, parent, child);
            child = parent;
        }
    }

    private static int HeapPop(Scratch scratch)
    {
        int top = scratch.Heap[0];
        int last = --scratch.HeapCount;
        scratch.Heap[0] = scratch.Heap[last];
        scratch.HeapF[0] = scratch.HeapF[last];

        int parent = 0;
        while (true)
        {
            int left = parent * 2 + 1;
            if (left >= scratch.HeapCount) break;

            int smallest = left;
            int right = left + 1;
            if (right < scratch.HeapCount && scratch.HeapF[right] < scratch.HeapF[left])
                smallest = right;

            if (scratch.HeapF[parent] <= scratch.HeapF[smallest]) break;
            Swap(scratch, parent, smallest);
            parent = smallest;
        }

        return top;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(Scratch scratch, int a, int b)
    {
        (scratch.Heap[a], scratch.Heap[b]) = (scratch.Heap[b], scratch.Heap[a]);
        (scratch.HeapF[a], scratch.HeapF[b]) = (scratch.HeapF[b], scratch.HeapF[a]);
    }

    /// <summary>
    /// Walks the parent chain back from <paramref name="end"/> and writes the waypoints into
    /// <paramref name="result"/> in travel order. Always returns true; the caller decides what a
    /// path to something other than the real target means.
    /// </summary>
    private static bool ReconstructPath(
        PathfindingGrid grid,
        int[] parent,
        int start,
        int end,
        List<Vector2> result)
    {
        int current = end;
        while (current != start && current >= 0)
        {
            result.Add(grid.IndexToWorld(current));
            current = parent[current];
        }

        result.Reverse();
        return true;
    }
}
