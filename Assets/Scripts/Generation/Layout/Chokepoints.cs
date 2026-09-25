using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds the cells a dungeon cannot afford to lose — the ones whose removal would split
/// it in two.
///
/// These are the places where a fight cannot be walked away from, and knowing where they
/// are is what lets the rest of the game treat them as such: an enemy posted beside one
/// (never on it) turns a corridor into a decision, and the count of them says whether a
/// layout offers escape routes at all. A dungeon with no chokepoints has no tension; one
/// that is all chokepoints has no stealth, because every route is the only route.
///
/// Articulation points of the walkable-cell graph, by the standard Hopcroft-Tarjan
/// depth-first numbering. Iterative rather than recursive on purpose: a 96x96 map has
/// thousands of connected floor cells and the recursive form overflows the stack on the
/// larger settings.
/// </summary>
public static class Chokepoints
{
    /// <summary>Finds the chokepoints and records them on the layout.</summary>
    public static void Detect(DungeonLayout layout)
    {
        if (layout == null) return;

        var index = new Dictionary<Vector2Int, int>();
        var cells = new List<Vector2Int>();

        // Built by scanning the grid rather than the rooms, so corridors and alcoves are
        // included — corridors are where nearly every chokepoint actually is.
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                if (!layout.IsWalkable(x, y)) continue;

                var cell = new Vector2Int(x, y);
                index[cell] = cells.Count;
                cells.Add(cell);
            }
        }

        if (cells.Count == 0)
        {
            layout.SetChokepoints(System.Array.Empty<Vector2Int>());
            return;
        }

        var found = new List<Vector2Int>();
        var discovery = new int[cells.Count];
        var low = new int[cells.Count];
        var parent = new int[cells.Count];
        var isArticulation = new bool[cells.Count];

        for (int i = 0; i < cells.Count; i++)
        {
            discovery[i] = -1;
            parent[i] = -1;
        }

        int timer = 0;
        for (int root = 0; root < cells.Count; root++)
        {
            if (discovery[root] >= 0) continue;
            Walk(layout, cells, index, root, discovery, low, parent, isArticulation, ref timer);
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (isArticulation[i]) found.Add(cells[i]);
        }

        layout.SetChokepoints(found);
    }

    /// <summary>
    /// One depth-first traversal from a root, carrying its own explicit stack.
    ///
    /// Each frame remembers which neighbour it was up to, so the walk can be suspended
    /// and resumed the way recursion would do implicitly.
    /// </summary>
    private static void Walk(DungeonLayout layout, List<Vector2Int> cells,
        Dictionary<Vector2Int, int> index, int root,
        int[] discovery, int[] low, int[] parent, bool[] isArticulation, ref int timer)
    {
        var stack = new Stack<(int node, int neighbour)>();
        int rootChildren = 0;

        discovery[root] = low[root] = timer++;
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            (int node, int neighbour) = stack.Pop();

            if (neighbour < Neighbours.Length)
            {
                // Queue this frame's next neighbour before descending, so the traversal
                // resumes here when the child is finished.
                stack.Push((node, neighbour + 1));

                Vector2Int next = cells[node] + Neighbours[neighbour];
                if (!index.TryGetValue(next, out int child)) continue;
                if (child == parent[node]) continue;

                if (discovery[child] >= 0)
                {
                    // Back edge: the child is already on the current path, so it offers a
                    // way round and lowers this node's reach.
                    low[node] = Mathf.Min(low[node], discovery[child]);
                    continue;
                }

                parent[child] = node;
                discovery[child] = low[child] = timer++;
                if (node == root) rootChildren++;
                stack.Push((child, 0));
                continue;
            }

            // Frame exhausted: fold its result into its parent.
            int up = parent[node];
            if (up < 0) continue;

            low[up] = Mathf.Min(low[up], low[node]);

            // No route from this subtree reaches above its parent, so cutting the parent
            // strands the subtree. The root is the exception: it has no parent, and is a
            // chokepoint only when it has more than one independent subtree.
            if (up != root && low[node] >= discovery[up]) isArticulation[up] = true;
        }

        if (rootChildren > 1) isArticulation[root] = true;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };
}
