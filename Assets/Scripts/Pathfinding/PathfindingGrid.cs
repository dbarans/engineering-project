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

    [Header("Visualization (Scene view only)")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color walkableColor = new Color(0f, 1f, 0f, 0.3f);
    [SerializeField] private Color blockedColor = new Color(1f, 0f, 0f, 0.5f);

    private Node[,] _nodes;

    public Node[,] Nodes => _nodes;
    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;
    public Vector2 Origin => origin;

    private void Awake()
    {
        BuildGrid();
    }

    public void BuildGrid()
    {
        _nodes = new Node[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 center = CellToWorld(x, y);
                bool walkable = !Physics2D.OverlapCircle(center, cellSize * 0.45f, obstacleMask);

                _nodes[x, y] = new Node(x, y, walkable, center);
            }
        }
    }

    public Vector2 CellToWorld(int x, int y)
    {
        return origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
    }

    public bool WorldToCell(Vector2 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
        y = Mathf.FloorToInt((world.y - origin.y) / cellSize);
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    public Node GetNode(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return null;
        return _nodes[x, y];
    }

    public Node GetNodeAtWorld(Vector2 world)
    {
        if (!WorldToCell(world, out int x, out int y)) return null;
        return GetNode(x, y);
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

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

                Gizmos.color = n.Walkable ? walkableColor : blockedColor;
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
                bool walkable = !Physics2D.OverlapCircle(center, cellSize * 0.45f, obstacleMask);
                preview[x, y] = new Node(x, y, walkable, center);
            }
        }
        return preview;
    }
}

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
