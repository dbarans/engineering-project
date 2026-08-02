using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Turns an abstract <see cref="DungeonLayout"/> into scene geometry: floor and wall
/// tiles, and the collider that both pathfinding and vision read.
///
/// It also owns cell-to-world conversion. Everything downstream — spawn placement,
/// the pathfinding grid origin — goes through here, so there is exactly one definition
/// of where cell (0,0) is.
/// </summary>
[RequireComponent(typeof(Grid))]
public class DungeonPainter : MonoBehaviour
{
    [Header("Tilemaps")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap wallTilemap;

    [Header("Tiles")]
    [SerializeField] private TileBase floorTile;
    [SerializeField] private TileBase wallTile;

    [Tooltip("Optional. Painted on doorway cells to make openings readable; falls back to the floor tile.")]
    [SerializeField] private TileBase doorwayTile;

    private Grid _grid;

    /// <summary>The tilemap grid; cell size here must match the pathfinding cell size.</summary>
    public Grid Grid => _grid != null ? _grid : _grid = GetComponent<Grid>();

    /// <summary>World position of the lower-left corner of cell (0,0).</summary>
    public Vector2 Origin => Grid.CellToWorld(Vector3Int.zero);

    /// <summary>Cell size in world units, taken from the tilemap grid.</summary>
    public float CellSize => Grid.cellSize.x;

    /// <summary>World position of the centre of a layout cell.</summary>
    public Vector2 CellCenter(Vector2Int cell)
    {
        Vector3 corner = Grid.CellToWorld(new Vector3Int(cell.x, cell.y, 0));
        return new Vector2(corner.x + Grid.cellSize.x * 0.5f, corner.y + Grid.cellSize.y * 0.5f);
    }

    /// <summary>Layout cell containing a world position.</summary>
    public Vector2Int WorldToCell(Vector2 world)
    {
        Vector3Int cell = Grid.WorldToCell(world);
        return new Vector2Int(cell.x, cell.y);
    }

    /// <summary>
    /// Clears both tilemaps and paints the layout, then rebuilds the collider.
    /// Returns false when the component is not wired up, so the caller can abort
    /// instead of producing an empty dungeon the player falls through.
    /// </summary>
    public bool Paint(DungeonLayout layout)
    {
        if (layout == null) return false;
        if (!Validate()) return false;

        var bounds = new BoundsInt(0, 0, 0, layout.Width, layout.Height, 1);
        int count = layout.Width * layout.Height;

        var floors = new TileBase[count];
        var walls = new TileBase[count];
        TileBase doorway = doorwayTile != null ? doorwayTile : floorTile;

        for (int y = 0; y < layout.Height; y++)
        {
            int row = y * layout.Width;
            for (int x = 0; x < layout.Width; x++)
            {
                CellType cell = layout[x, y];
                // Floor is painted under walls too: without it, any gap the wall
                // collider leaves would show the empty background.
                floors[row + x] = cell == CellType.Door ? doorway : floorTile;
                walls[row + x] = cell == CellType.Wall ? wallTile : null;
            }
        }

        floorTilemap.ClearAllTiles();
        wallTilemap.ClearAllTiles();
        floorTilemap.SetTilesBlock(bounds, floors);
        wallTilemap.SetTilesBlock(bounds, walls);

        RebuildColliders();
        return true;
    }

    /// <summary>
    /// Forces the wall collider to match the tiles that were just painted.
    ///
    /// This is the ordering trap called out in the plan. Tilemap collider updates are
    /// deferred to the end of the frame, and the composite is only regenerated on top
    /// of whatever the tilemap collider has already produced. Skipping either call
    /// leaves physics describing the *previous* dungeon, which shows up as enemies
    /// walking through walls and vision passing through solid rock — with the tiles
    /// on screen looking perfectly correct.
    /// </summary>
    public void RebuildColliders()
    {
        var collider = wallTilemap.GetComponent<TilemapCollider2D>();
        if (collider != null) collider.ProcessTilemapChanges();

        var composite = wallTilemap.GetComponent<CompositeCollider2D>();
        if (composite != null) composite.GenerateGeometry();

        // Queries run against the physics world's transforms, not the scene's.
        Physics2D.SyncTransforms();
    }

    private bool Validate()
    {
        if (floorTilemap == null || wallTilemap == null)
        {
            Debug.LogError("[DungeonPainter] Floor or wall tilemap is not assigned.", this);
            return false;
        }

        if (floorTile == null || wallTile == null)
        {
            Debug.LogError("[DungeonPainter] Floor or wall tile asset is not assigned.", this);
            return false;
        }

        if (wallTilemap.GetComponent<TilemapCollider2D>() == null)
        {
            Debug.LogError(
                "[DungeonPainter] The wall tilemap has no TilemapCollider2D — generated walls " +
                "would block neither movement nor vision. Run Tools > Dungeon > Setup Scene Tilemaps.",
                this);
            return false;
        }

        return true;
    }
}
