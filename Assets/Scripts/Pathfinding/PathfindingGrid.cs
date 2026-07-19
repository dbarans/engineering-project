using UnityEngine;

/// <summary>
/// Procedural 2D grid for A*. Walkable/blocked cells are visualized in the Scene view (Gizmos).
/// </summary>
public class PathfindingGrid : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField] private Vector2 origin = Vector2.zero;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private int width = 20;
    [SerializeField] private int height = 20;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("Extra clearance radius for moving agents. Increase when enemy collider is larger than a grid cell center sample.")]
    [SerializeField] private float agentRadius = 0f;

    [Header("Visualization (Scene view only)")]
    [Tooltip("Overall opacity of the grid gizmo. 0 = fully hidden, 1 = colors below at full strength.")]
    [Range(0f, 1f)]
    [SerializeField] private float gizmoOpacity = 1f;
    [SerializeField] private Color walkableColor = new Color(0f, 1f, 0f, 0.3f);
    [SerializeField] private Color blockedColor = new Color(1f, 0f, 0f, 0.5f);

    private Node[,] _nodes;

    public Node[,] Nodes => _nodes;
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
    /// Builds or rebuilds the full walkability grid.
    /// </summary>
    public void BuildGrid()
    {
        _nodes = new Node[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 center = CellToWorld(x, y);
                bool walkable = !Physics2D.OverlapCircle(center, GetObstacleCheckRadius(), obstacleMask);

                _nodes[x, y] = new Node(x, y, walkable, center);
            }
        }
    }

    /// <summary>
    /// Converts cell coordinates to world-space center position.
    /// </summary>
    public Vector2 CellToWorld(int x, int y)
    {
        return origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
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
    /// Returns node by cell coordinates, or null when out of bounds.
    /// </summary>
    public Node GetNode(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return null;
        return _nodes[x, y];
    }

    /// <summary>
    /// Returns node for world position, or null when outside grid.
    /// </summary>
    public Node GetNodeAtWorld(Vector2 world)
    {
        if (!WorldToCell(world, out int x, out int y)) return null;
        return GetNode(x, y);
    }

    private void OnDrawGizmos()
    {
        if (gizmoOpacity <= 0f) return;

        Node[,] toDraw = _nodes;
        if (toDraw == null && Application.isPlaying == false)
        {
            toDraw = PreviewGrid();
        }

        if (toDraw == null) return;

        for (int x = 0; x < toDraw.GetLength(0); x++)
        {
            for (int y = 0; y < toDraw.GetLength(1); y++)
            {
                Node n = toDraw[x, y];
                if (n == null) continue;

                Color baseColor = n.Walkable ? walkableColor : blockedColor;
                Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * gizmoOpacity);
                Vector3 center = new Vector3(n.WorldPos.x, n.WorldPos.y, 0f);
                Gizmos.DrawCube(center, new Vector3(cellSize * 0.9f, cellSize * 0.9f, 0.01f));
            }
        }
    }

    private Node[,] PreviewGrid()
    {
        if (width <= 0 || height <= 0) return null;
        var preview = new Node[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 center = origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
                bool walkable = !Physics2D.OverlapCircle(center, GetObstacleCheckRadius(), obstacleMask);
                preview[x, y] = new Node(x, y, walkable, center);
            }
        }
        return preview;
    }

    private float GetObstacleCheckRadius()
    {
        return (cellSize * 0.45f) + Mathf.Max(0f, agentRadius);
    }
}

/// <summary>
/// Immutable grid node used by pathfinding.
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
