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

    [Tooltip("Optional. Draws over the floor and under the walls, with no collider: cracks, " +
             "stains and spilled grit. A separate layer because a decal has to be able to " +
             "sit on any floor variant without multiplying the floor tile set by the " +
             "number of marks that can appear on it.")]
    [SerializeField] private Tilemap decalTilemap;

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

    [Tooltip("Optional. Marks scattered over the floor. Needs the decal tilemap to be assigned.")]
    [SerializeField] private TileBase[] decalTiles;

    [Tooltip("Per-cell chance of a decal on open floor away from the walls.")]
    [Range(0f, 0.5f)] [SerializeField] private float decalChance = 0.05f;

    [Tooltip("How much more likely a decal is on a cell touching something solid. Dirt, " +
             "cracks and debris collect at the edges of a room; scattered evenly they read " +
             "as noise laid over the floor rather than as wear on it.")]
    [Range(1f, 8f)] [SerializeField] private float decalWallBias = 3.5f;

    [Header("Shape")]
    [Tooltip("How many cells of rock are painted around the open areas. Rock further in " +
             "than this is left unpainted and reads as void — which is what makes the " +
             "dungeon look like built structure floating in black rather than like a " +
             "solid slab with tunnels bored through it. 0 paints every wall cell.\n\n" +
             "It only pays off while it stays well under half the room spacing: at " +
             "thickness t the gap between two rooms loses 2t cells to painted wall, and " +
             "once that reaches the spacing there is no void left between them and the " +
             "whole map fills back in as one slab.")]
    [Min(0)] [SerializeField] private int wallShellThickness = 1;

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

        bool[] shell = BuildWallShell(layout);

        for (int y = 0; y < layout.Height; y++)
        {
            int row = y * layout.Width;
            for (int x = 0; x < layout.Width; x++)
            {
                CellType cell = layout[x, y];
                int index = row + x;

                // Pillars and rubble go into the wall tilemap, not the floor one. That is
                // what puts them in the composite collider, and the collider is what both
                // pathfinding and the vision cone actually read — a pillar painted onto
                // the floor layer would look solid and stop nothing.
                switch (cell)
                {
                    case CellType.Pillar:
                        walls[index] = pillar;
                        break;

                    case CellType.Rubble:
                        walls[index] = rubble;
                        break;

                    case CellType.Wall:
                        // Rock deeper than the shell is left empty. It can never be seen
                        // past the shell in front of it, and leaving it out is what turns
                        // the background from a grey slab into black void.
                        if (!shell[index]) { walls[index] = null; break; }

                        // A wall with open ground to its south shows its face to the
                        // camera; one buried in rock only ever shows its top.
                        walls[index] = layout[x, y - 1] != CellType.Wall ? wallFace : wallTile;
                        break;

                    default:
                        walls[index] = null;
                        break;
                }

                // Floor goes under the painted walls too: without it, any gap the wall
                // collider leaves would show the void through the structure. Under the
                // *unpainted* rock it would defeat the point, so that stays empty.
                if (cell == CellType.Door) floors[index] = doorway;
                else if (cell != CellType.Wall || walls[index] != null) floors[index] = PickFloor(seedHash, x, y);
                else floors[index] = null;
            }
        }

        floorTilemap.ClearAllTiles();
        wallTilemap.ClearAllTiles();
        floorTilemap.SetTilesBlock(bounds, floors);
        wallTilemap.SetTilesBlock(bounds, walls);

        if (doorwayTile != null) OrientDoorways(layout);
        PaintDecals(layout, bounds, seedHash);

        RebuildColliders();
        return true;
    }

    /// <summary>
    /// Scatters wear marks over the open floor, biased towards the cells that touch
    /// something solid.
    ///
    /// Rolled from the seed and the coordinates like the floor variants, and for the same
    /// reason: the dungeon is rebuilt from its seed on every load, and marks that moved
    /// between loads would be read as the room having changed.
    /// </summary>
    private void PaintDecals(DungeonLayout layout, BoundsInt bounds, uint seedHash)
    {
        if (decalTilemap == null) return;

        decalTilemap.ClearAllTiles();
        if (decalTiles == null || decalTiles.Length == 0 || decalChance <= 0f) return;

        var decals = new TileBase[layout.Width * layout.Height];

        for (int y = 0; y < layout.Height; y++)
        {
            int row = y * layout.Width;
            for (int x = 0; x < layout.Width; x++)
            {
                if (!layout.IsWalkable(x, y)) continue;

                // Doorways stay clean: the threshold tile is already a strong shape, and
                // anything on top of it stops the opening reading as an opening.
                if (layout[x, y] == CellType.Door) continue;

                float chance = decalChance * (TouchesSolid(layout, x, y) ? decalWallBias : 1f);

                // Two independent draws off one hash: the low half decides whether there
                // is a mark, the high half which one, so raising the chance does not also
                // reshuffle every decal already placed.
                uint hash = DeterministicRandom.Hash(seedHash, x, y);
                if ((hash & 0xFFFF) / 65535f >= chance) continue;

                decals[row + x] = decalTiles[(hash >> 16) % (uint)decalTiles.Length];
            }
        }

        decalTilemap.SetTilesBlock(bounds, decals);
    }

    /// <summary>True when any of the four neighbours is not open ground.</summary>
    private static bool TouchesSolid(DungeonLayout layout, int x, int y)
    {
        return !layout.IsWalkable(x - 1, y) || !layout.IsWalkable(x + 1, y) ||
               !layout.IsWalkable(x, y - 1) || !layout.IsWalkable(x, y + 1);
    }

    /// <summary>
    /// Turns the threshold tile to face along the passage it sits in.
    ///
    /// The tile is drawn with its jambs on the left and right, which is right for a
    /// doorway walked through north-south and ninety degrees wrong for one walked through
    /// east-west. Rotating is a per-cell call and cannot go through
    /// <see cref="Tilemap.SetTilesBlock"/>, but there are only a handful of doorways in a
    /// dungeon, so it costs nothing to do it afterwards.
    /// </summary>
    private void OrientDoorways(DungeonLayout layout)
    {
        Matrix4x4 quarterTurn = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f));

        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                if (layout[x, y] != CellType.Door) continue;

                // Solid above and below means the jambs are the north and south stubs,
                // so the passage runs east-west and the tile has to turn.
                bool blockedNorthSouth = !layout.IsWalkable(x, y - 1) && !layout.IsWalkable(x, y + 1);
                if (!blockedNorthSouth) continue;

                floorTilemap.SetTransformMatrix(new Vector3Int(x, y, 0), quarterTurn);
            }
        }
    }

    /// <summary>
    /// Marks the wall cells that are close enough to open ground to be worth painting.
    ///
    /// Everything deeper is dropped, which is safe for exactly one reason: the shell is
    /// measured outwards from every open cell at once, so it completely encloses every
    /// walkable region. Nothing can reach, see or path into the rock behind it, and the
    /// collider built from the shell alone stops everything the full slab would have.
    ///
    /// Done as repeated dilation rather than a distance transform because the map is a
    /// few thousand cells and this runs once per dungeon.
    /// </summary>
    private bool[] BuildWallShell(DungeonLayout layout)
    {
        int width = layout.Width;
        int height = layout.Height;
        var shell = new bool[width * height];

        if (wallShellThickness <= 0)
        {
            for (int i = 0; i < shell.Length; i++) shell[i] = true;
            return shell;
        }

        // Seed the frontier with the open cells themselves. They are not walls, so they
        // are cleared out of the mask again at the end; they are only here to grow from.
        var frontier = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                CellType cell = layout[x, y];
                if (cell == CellType.Floor || cell == CellType.Door)
                    frontier[y * width + x] = true;
            }
        }

        var next = new bool[width * height];
        for (int step = 0; step < wallShellThickness; step++)
        {
            System.Array.Clear(next, 0, next.Length);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!frontier[y * width + x]) continue;

                    // Eight-connected: a four-connected shell leaves the diagonal outside
                    // a room corner unpainted, and the corner then reads as a chipped notch.
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (!layout.Contains(nx, ny)) continue;

                            int index = ny * width + nx;
                            if (shell[index] || layout[nx, ny] != CellType.Wall) continue;

                            shell[index] = true;
                            next[index] = true;
                        }
                    }
                }
            }

            (frontier, next) = (next, frontier);
        }

        return shell;
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
