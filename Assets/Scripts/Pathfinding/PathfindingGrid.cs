using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural 2D grid for A*. Walkable/blocked cells are visualized in the Scene view (Gizmos).
///
/// Walkability is stored as a flat <c>bool[]</c> indexed <c>y * width + x</c> rather than a
/// <c>Node[,]</c>: a 500x500 dungeon is 250 000 cells, and one heap object per cell costs ~10 MB
/// and destroys cache locality in the A* inner loop. <see cref="Node"/> is still handed out by
/// <see cref="GetNode"/> for callers that want the old object shape, but it is now built on demand.
///
/// Each walkable cell also carries a connected-region id (4-connectivity, matching the movement
/// rules in <see cref="AStarPathfinder"/>), so "is this target reachable at all?" is an O(1)
/// comparison instead of an exhaustive A* flood over the whole map.
///
/// <see cref="ExecuteAlways"/> is load-bearing, not decoration — the same reason it is on
/// <see cref="DungeonPopulator"/> (see the Stage 7 write-up in <c>GENERATION_NOTES.md</c>).
/// <c>_walkable</c> is never serialized, so without it the gizmo has nothing to draw the
/// moment a baked dungeon scene is opened: a plain <see cref="MonoBehaviour"/> only runs
/// <see cref="Awake"/> when entering Play, and a scene authored by baking runs with
/// <c>buildOnStart</c> off, so nothing else calls <see cref="BuildGrid"/> either. With this,
/// opening the scene alone is enough for the grid to exist and the gizmo to have data.
/// </summary>
[ExecuteAlways]
public class PathfindingGrid : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField] private Vector2 origin = Vector2.zero;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private int width = 20;
    [SerializeField] private int height = 20;
    [Tooltip("Layers checked for obstacles. Trigger colliders on these layers are ignored (see BuildGrid), so an interactable's trigger volume never blocks a path the way its solid collider would.")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("Extra clearance radius for moving agents. Increase when enemy collider is larger than a grid cell center sample.")]
    [SerializeField] private float agentRadius = 0f;

    [Header("Visualization (Scene view only)")]
    [Tooltip("Overall opacity of the grid gizmo. 0 = fully hidden, 1 = colors below at full strength.")]
    [Range(0f, 1f)]
    [SerializeField] private float gizmoOpacity = 0f;
    [Tooltip("Hard cap on gizmo cubes drawn per repaint. A 500x500 grid is 250 000 cells; drawing them all stalls the Scene view. Above the cap, cells are sampled by stride across the whole grid rather than truncated, so raising or lowering this only changes density, never which part of the map is visible.")]
    [SerializeField] private int maxGizmoCells = 30000;
    [SerializeField] private Color walkableColor = new Color(0f, 1f, 0f, 0.3f);
    [SerializeField] private Color blockedColor = new Color(1f, 0f, 0f, 0.5f);

    private bool[] _walkable;
    private int[] _regionIds;
    private int _regionCount;

    // Reused across every OverlapCircle call in BuildGrid to avoid a per-cell allocation —
    // only the count matters, never the collider itself.
    private readonly Collider2D[] _overlapBuffer = new Collider2D[1];

    /// <summary>Bumped by every <see cref="BuildGrid"/>. Lets caches keyed on grid topology invalidate themselves.</summary>
    public int TopologyVersion { get; private set; }

    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;
    public Vector2 Origin => origin;
    public float AgentRadius => agentRadius;

    private void Awake()
    {
        BuildGrid();
    }

    /// <summary>
    /// Resizes the grid to cover a procedurally generated dungeon and rebuilds it.
    /// The authored width/height/origin/cellSize only fit a hand-built scene, so a
    /// generator must call this before the grid is first used.
    ///
    /// <paramref name="gridCellSize"/> exists because the authored <see cref="cellSize"/>
    /// is not a safe default to fall back on: it is whatever was last set by hand in the
    /// inspector, with no guarantee it matches the tilemap the generator just painted.
    /// One dungeon shipped with the tilemap's <c>DungeonRoot</c> scaled 2× and this field
    /// left at an unrelated value from a different scene entirely — the grid sampled a
    /// quarter of the map's actual area, all of it clustered at the corner nearest the
    /// origin, because every cell was checked at half its real spacing. The caller — which
    /// knows the tilemap's actual world-space cell size, via <see cref="DungeonPainter.CellSize"/>
    /// — is the only one in a position to get this right.
    ///
    /// Caller's responsibility: the colliders must already be final. The grid samples
    /// physics, so calling this before <c>CompositeCollider2D.GenerateGeometry()</c>
    /// yields a grid that silently disagrees with the visible walls.
    /// </summary>
    public void Configure(Vector2 gridOrigin, float gridCellSize, int gridWidth, int gridHeight)
    {
        if (gridWidth <= 0 || gridHeight <= 0 || gridCellSize <= 0f)
        {
            Debug.LogError(
                $"[PathfindingGrid] Refusing size {gridWidth}x{gridHeight} at cell size " +
                $"{gridCellSize}; width and height must be positive and cell size must be " +
                "greater than zero.", this);
            return;
        }

        origin = gridOrigin;
        cellSize = gridCellSize;
        width = gridWidth;
        height = gridHeight;
        BuildGrid();
    }

    /// <summary>
    /// Builds or rebuilds the full walkability grid and its connected-region labels.
    /// </summary>
    public void BuildGrid()
    {
        int cellCount = width * height;
        if (_walkable == null || _walkable.Length != cellCount)
        {
            _walkable = new bool[cellCount];
            _regionIds = new int[cellCount];
        }

        float radius = GetObstacleCheckRadius();
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(obstacleMask);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 center = CellToWorld(x, y);
                _walkable[y * width + x] = Physics2D.OverlapCircle(center, radius, filter, _overlapBuffer) == 0;
            }
        }

        BuildRegions();
        TopologyVersion++;
    }

    /// <summary>
    /// Labels every walkable cell with the id of its connected region via flood fill.
    /// Blocked cells get -1, so they never compare equal to anything (including each other).
    ///
    /// 4-connectivity is deliberate and must stay in sync with <see cref="AStarPathfinder"/>:
    /// diagonal steps there require both adjacent orthogonal cells to be walkable, so two areas
    /// touching only at a corner are genuinely not traversable between each other.
    /// </summary>
    private void BuildRegions()
    {
        for (int i = 0; i < _regionIds.Length; i++)
            _regionIds[i] = _walkable[i] ? 0 : -1;

        var queue = new Queue<int>();
        _regionCount = 0;

        for (int start = 0; start < _regionIds.Length; start++)
        {
            if (_regionIds[start] != 0) continue;

            int region = ++_regionCount;
            _regionIds[start] = region;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width;
                int y = index / width;

                if (x > 0) TryEnqueueRegionCell(index - 1, region, queue);
                if (x < width - 1) TryEnqueueRegionCell(index + 1, region, queue);
                if (y > 0) TryEnqueueRegionCell(index - width, region, queue);
                if (y < height - 1) TryEnqueueRegionCell(index + width, region, queue);
            }
        }
    }

    private void TryEnqueueRegionCell(int index, int region, Queue<int> queue)
    {
        if (_regionIds[index] != 0) return;
        _regionIds[index] = region;
        queue.Enqueue(index);
    }

    /// <summary>
    /// Converts cell coordinates to world-space center position.
    /// </summary>
    public Vector2 CellToWorld(int x, int y)
    {
        return origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
    }

    /// <summary>
    /// Converts a flat cell index (<c>y * Width + x</c>) to its world-space center.
    /// </summary>
    public Vector2 IndexToWorld(int index)
    {
        return CellToWorld(index % width, index / width);
    }

    /// <summary>
    /// Converts world position to cell coordinates.
    /// Returns false when outside grid bounds.
    /// </summary>
    public bool WorldToCell(Vector2 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
        y = Mathf.FloorToInt((world.y - origin.y) / cellSize);
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    /// <summary>
    /// True when the cell is inside the grid and not blocked by an obstacle.
    /// Out-of-bounds counts as blocked.
    /// </summary>
    public bool IsWalkable(int x, int y)
    {
        if (_walkable == null || x < 0 || x >= width || y < 0 || y >= height) return false;
        return _walkable[y * width + x];
    }

    /// <summary>True when the world position falls on a walkable cell.</summary>
    public bool IsWalkableWorld(Vector2 world)
    {
        return WorldToCell(world, out int x, out int y) && IsWalkable(x, y);
    }

    /// <summary>
    /// Connected-region id of a cell, or -1 when blocked or out of bounds. Two walkable cells
    /// with different ids can never be connected by a path, which lets callers reject an
    /// impossible route without running a search.
    /// </summary>
    public int GetRegionId(int x, int y)
    {
        if (_regionIds == null || x < 0 || x >= width || y < 0 || y >= height) return -1;
        return _regionIds[y * width + x];
    }

    /// <summary>
    /// Finds the closest walkable cell to (x, y), searching outward ring by ring up to
    /// maxRingRadius cells. Used to rescue searches whose start or target landed inside an
    /// obstacle — an agent nudged into an inflated wall cell, or a player standing right next
    /// to one — which would otherwise report "no path" forever.
    /// </summary>
    public bool TryFindNearestWalkable(int x, int y, int maxRingRadius, out int walkableX, out int walkableY)
    {
        walkableX = x;
        walkableY = y;
        if (IsWalkable(x, y)) return true;

        for (int ring = 1; ring <= maxRingRadius; ring++)
        {
            for (int offsetY = -ring; offsetY <= ring; offsetY++)
            {
                for (int offsetX = -ring; offsetX <= ring; offsetX++)
                {
                    // Only the ring's border; the interior was covered by a previous iteration.
                    if (Mathf.Abs(offsetX) != ring && Mathf.Abs(offsetY) != ring) continue;

                    int candidateX = x + offsetX;
                    int candidateY = y + offsetY;
                    if (!IsWalkable(candidateX, candidateY)) continue;

                    walkableX = candidateX;
                    walkableY = candidateY;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns node by cell coordinates, or null when out of bounds. Built on demand — the grid
    /// itself stores plain arrays, so this allocates and is not meant for the A* inner loop.
    /// </summary>
    public Node GetNode(int x, int y)
    {
        if (_walkable == null || x < 0 || x >= width || y < 0 || y >= height) return null;
        return new Node(x, y, _walkable[y * width + x], CellToWorld(x, y));
    }

    /// <summary>
    /// Returns node for world position, or null when outside grid.
    /// </summary>
    public Node GetNodeAtWorld(Vector2 world)
    {
        if (!WorldToCell(world, out int x, out int y)) return null;
        return GetNode(x, y);
    }

    /// <summary>
    /// Draws the walkability grid in the Scene view. Off by default (gizmoOpacity 0) and capped
    /// at maxGizmoCells: an uncapped 500x500 grid issues a quarter of a million draw calls per
    /// repaint, which alone drops the editor to single-digit fps.
    ///
    /// Cells beyond the cap are skipped by <b>stride</b>, not by stopping partway through a
    /// row-major scan. The scan version always drew the same low-y rows first and cut off
    /// there once the cap was hit — on a map bigger than the cap, that reliably meant one
    /// corner was fully drawn and the rest of the map showed nothing, regardless of where in
    /// the scene anyone was actually looking. A stride draws sparser but reaches every part
    /// of the grid whatever the cap is, which is what "capped" should mean here.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (gizmoOpacity <= 0f || maxGizmoCells <= 0) return;
        if (_walkable == null || _walkable.Length != width * height) return;

        int cellCount = width * height;
        int stride = Mathf.Max(1, Mathf.CeilToInt(cellCount / (float)maxGizmoCells));
        var size = new Vector3(cellSize * 0.9f, cellSize * 0.9f, 0.01f);

        for (int index = 0; index < cellCount; index += stride)
        {
            int x = index % width;
            int y = index / width;

            Color baseColor = _walkable[index] ? walkableColor : blockedColor;
            Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * gizmoOpacity);
            Vector2 center = CellToWorld(x, y);
            Gizmos.DrawCube(new Vector3(center.x, center.y, 0f), size);
        }
    }

    private float GetObstacleCheckRadius()
    {
        return (cellSize * 0.45f) + Mathf.Max(0f, agentRadius);
    }
}

/// <summary>
/// Immutable grid node. A read-only view over one cell, handed out by
/// <see cref="PathfindingGrid.GetNode"/>; the grid no longer stores these.
/// </summary>
public class Node
{
    public int X { get; }
    public int Y { get; }
    public bool Walkable { get; }
    public Vector2 WorldPos { get; }

    public Node(int x, int y, bool walkable, Vector2 worldPos)
    {
        X = x;
        Y = y;
        Walkable = walkable;
        WorldPos = worldPos;
    }
}
