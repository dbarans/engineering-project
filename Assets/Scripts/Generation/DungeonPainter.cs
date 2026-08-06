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
    [Tooltip("Floor variants. More than one breaks up the wallpaper effect; the variant " +
             "for a cell is chosen from the dungeon seed, so it is stable across rebuilds.")]
    [SerializeField] private TileBase[] floorTiles;

    [Tooltip("Wall seen from above.")]
    [SerializeField] private TileBase wallTile;

    [Tooltip("Optional. Wall whose south side is exposed, i.e. the face turned towards the " +
             "camera. Without it the dungeon reads as a floor plan rather than as rooms.")]
    [SerializeField] private TileBase wallFaceTile;

    [Tooltip("Optional. Free-standing pillars and partition walls inside rooms; falls back " +
             "to the wall tile. Painted into the wall tilemap, so it blocks vision and " +
             "movement exactly like bedrock — which is the entire point of it.")]
    [SerializeField] private TileBase pillarTile;

    [Tooltip("Optional. Collapsed masonry; falls back to the wall tile. Solid like a wall " +
             "despite reading as debris.")]
    [SerializeField] private TileBase rubbleTile;

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

        TileBase doorway = doorwayTile != null ? doorwayTile : floorTiles[0];
        TileBase wallFace = wallFaceTile != null ? wallFaceTile : wallTile;
        TileBase pillar = pillarTile != null ? pillarTile : wallTile;
        TileBase rubble = rubbleTile != null ? rubbleTile : wallTile;
        uint seedHash = DeterministicRandom.Hash(layout.Seed);

        for (int y = 0; y < layout.Height; y++)
        {
            int row = y * layout.Width;
            for (int x = 0; x < layout.Width; x++)
            {
                CellType cell = layout[x, y];

                // Floor is painted under walls too: without it, any gap the wall
                // collider leaves would show the empty background.
                floors[row + x] = cell == CellType.Door ? doorway : PickFloor(seedHash, x, y);

                // Pillars and rubble go into the wall tilemap, not the floor one. That is
                // what puts them in the composite collider, and the collider is what both
                // pathfinding and the vision cone actually read — a pillar painted onto
                // the floor layer would look solid and stop nothing.
                switch (cell)
                {
                    case CellType.Pillar:
                        walls[row + x] = pillar;
                        continue;

                    case CellType.Rubble:
                        walls[row + x] = rubble;
                        continue;

                    case CellType.Wall:
                        // A wall with open ground to its south shows its face to the
                        // camera; one buried in rock only ever shows its top.
                        walls[row + x] = layout[x, y - 1] != CellType.Wall ? wallFace : wallTile;
                        continue;

                    default:
                        walls[row + x] = null;
                        continue;
                }
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
    /// Floor variant for a cell, chosen from the seed and the coordinates so the same
    /// dungeon always looks the same — the map is rebuilt from its seed on every load,
    /// and a floor that reshuffled each time would be visibly wrong.
    /// </summary>
    private TileBase PickFloor(uint seedHash, int x, int y)
    {
        if (floorTiles.Length == 1) return floorTiles[0];

        uint hash = DeterministicRandom.Hash(seedHash, x, y);
        return floorTiles[hash % (uint)floorTiles.Length];
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

        if (floorTiles == null || floorTiles.Length == 0 || floorTiles[0] == null)
        {
            Debug.LogError("[DungeonPainter] No floor tile assigned.", this);
            return false;
        }

        if (wallTile == null)
        {
            Debug.LogError("[DungeonPainter] No wall tile assigned.", this);
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
