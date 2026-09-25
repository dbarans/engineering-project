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

    [Tooltip("Set above 1 when the floor tiles are the pieces of one larger image rather " +
             "than interchangeable variants. The tiles are then laid out in row-major order " +
             "across an N by N block and repeated, so the artwork stays continuous across " +
             "cell borders inside a block and only repeats every N cells. Needs exactly " +
             "N*N floor tiles. 1 picks an independent variant per cell from the seed.")]
    [Min(1)] [SerializeField] private int floorMosaicSize = 1;

    [Tooltip("Wall seen from above.")]
    [SerializeField] private TileBase wallTile;

    [Tooltip("Optional. Wall whose south side is exposed, i.e. the face turned towards the " +
             "camera. Without it the dungeon reads as a floor plan rather than as rooms. " +
             "Ignored when the 16 autotile variants below are filled in.")]
    [SerializeField] private TileBase wallFaceTile;

    [Tooltip("Optional, and the thing that makes wall outlines readable. Sixteen variants, " +
             "indexed by which of the four sides are exposed: bit 0 north, 1 east, 2 south, " +
             "3 west. With these filled in every corner, edge and one-cell buttress gets its " +
             "own art instead of the flat slab that made them all look identical. Leave " +
             "empty to fall back to the plain wall/face pair.")]
    [SerializeField] private TileBase[] wallAutotiles;

    [Tooltip("Set above 1 when each exposure mask has several tiles that are the cells of " +
             "one larger block of stonework rather than interchangeable variants. The block " +
             "is laid out row-major and repeated, so the masonry runs continuously across " +
             "cell borders and only repeats every N cells. Needs exactly 16*N*N wall " +
             "autotiles, ordered mask-major: all of mask 0's block, then mask 1's, and so " +
             "on. 1 is the plain one-tile-per-mask set.")]
    [Min(1)] [SerializeField] private int wallMosaicSize = 1;

    [Tooltip("Optional. Free-standing pillars and partition walls inside rooms; falls back " +
             "to the wall tile. Painted into the wall tilemap, so it blocks vision and " +
             "movement exactly like bedrock — which is the entire point of it.")]
    [SerializeField] private TileBase pillarTile;

    [Tooltip("Optional. Collapsed masonry; falls back to the wall tile. Solid like a wall " +
             "despite reading as debris.")]
    [SerializeField] private TileBase rubbleTile;

    [Tooltip("Optional. Floor of the exit room, which is where the run ends. A different " +
             "stone under the player's feet is what tells them the room they just unlocked " +
             "is not another room. Picked per cell from the seed like the ordinary scatter " +
             "floor; the mosaic layout above does not apply. Leave empty to floor the exit " +
             "room like everywhere else.")]
    [SerializeField] private TileBase[] exitFloorTiles;

    [Tooltip("Optional. Marks scattered over the floor. Needs the decal tilemap to be assigned.")]
    [SerializeField] private TileBase[] decalTiles;

    [Tooltip("Per-cell chance of a decal on open floor away from the walls.")]
    [Range(0f, 0.5f)] [SerializeField] private float decalChance = 0.05f;

    [Tooltip("How much more likely a decal is on a cell touching something solid. Dirt, " +
             "cracks and debris collect at the edges of a room; scattered evenly they read " +
             "as noise laid over the floor rather than as wear on it.")]
    [Range(1f, 8f)] [SerializeField] private float decalWallBias = 3.5f;

    [Tooltip("Optional. Painted on exactly one open floor cell of the hub, every generation, " +
             "in addition to (not instead of) that tile's ordinary chance of turning up " +
             "anywhere else via decalTiles — a detail the hub is guaranteed to have, on top " +
             "of the same thing being rare scatter everywhere else. The cell is chosen from " +
             "the seed, so it is the same cell on every rebuild of the same dungeon.")]
    [SerializeField] private TileBase hubGuaranteedDecalTile;

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

    /// <summary>
    /// Cell size in *world* units.
    ///
    /// <see cref="UnityEngine.Grid.cellSize"/> alone is the Grid component's own local
    /// value and does not include the GameObject's transform scale — <see cref="Grid.CellToWorld"/>
    /// applies it, but reading <c>cellSize</c> directly does not. Every scene shipped by
    /// this project has `DungeonRoot` scaled 2×, so the unscaled value under-reported the
    /// real spacing between painted tiles by half. That silently broke everything fed by
    /// this property: <see cref="PathfindingGrid.Configure"/> is the one with the visible
    /// symptom (it sampled a grid a quarter of the map's actual area, in the corner
    /// nearest the origin, because it thought each cell was half as wide as it is), but
    /// this is the one property responsible for both that and <see cref="CellCenter"/>.
    /// </summary>
    public float CellSize => Grid.cellSize.x * Grid.transform.lossyScale.x;

    /// <summary>World position of the centre of a layout cell.</summary>
    public Vector2 CellCenter(Vector2Int cell)
    {
        Vector3 corner = Grid.CellToWorld(new Vector3Int(cell.x, cell.y, 0));
        Vector3 scale = Grid.transform.lossyScale;

        // Half the *world*-space cell, not half of Grid.cellSize's unscaled value — the
        // same distinction CellSize exists to make. Kept per-axis rather than reusing
        // CellSize on both, since a non-square cell is otherwise representable here even
        // though nothing in this project currently authors one.
        return new Vector2(
            corner.x + Grid.cellSize.x * scale.x * 0.5f,
            corner.y + Grid.cellSize.y * scale.y * 0.5f);
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

        TileBase wallFace = wallFaceTile != null ? wallFaceTile : wallTile;
        TileBase pillar = pillarTile != null ? pillarTile : wallTile;
        TileBase rubble = rubbleTile != null ? rubbleTile : wallTile;
        uint seedHash = DeterministicRandom.Hash(layout.Seed);

        bool[] shell = BuildWallShell(layout);
        bool[] exitFloor = BuildExitFloorMask(layout);

        // The whole set or none: a half-filled array would silently paint some walls with a
        // null tile, leaving holes in the structure that look like doorways.
        int wallBlock = Mathf.Max(1, wallMosaicSize);
        bool autotiled = wallAutotiles != null && wallAutotiles.Length == 16 * wallBlock * wallBlock;
        if (autotiled)
        {
            foreach (TileBase tile in wallAutotiles) autotiled &= tile != null;
        }

        for (int y = 0; y < layout.Height; y++)
        {
            int row = y * layout.Width;
            for (int x = 0; x < layout.Width; x++)
            {
                CellType cell = layout[x, y];
                int index = row + x;

                // The one cell of solid rock that gets no wall tile: the way out.
                //
                // The wall tilemap sorts above world sprites (Stage 8), so a door standing
                // on a painted wall cell is not merely close to the wall, it is painted
                // over by it — the exit would be an invisible door in a blank stretch of
                // rock. Leaving the tile out cuts the doorway-shaped gap the door then
                // stands in, and costs nothing structurally: the cell stays solid in the
                // layout, so vision and pathfinding still treat it as a wall, and the
                // door's own collider is what physically blocks the opening.
                if (layout.HasExitDoor && x == layout.ExitDoorCell.x && y == layout.ExitDoorCell.y)
                {
                    walls[index] = null;
                    floors[index] = PickFloor(seedHash, x, y);
                    continue;
                }

                // Pillars and rubble go into the wall tilemap, not the floor one. That is
                // what puts them in the composite collider, and the collider is what both
                // pathfinding and the vision cone actually read — a pillar painted onto
                // the floor layer would look solid and stop nothing.
                switch (cell)
                {
                    case CellType.Pillar:
                        // Two different things wear this cell type, and they need different
                        // tiles. DoorwayNormalizer walls up the excess of a wide opening and
                        // turns the couple of cells nearest the door into Pillar purely so
                        // they read as built jambs rather than as the bedrock the opening was
                        // cut through — those are part of the wall mass and have to stay solid
                        // right across the cell. RoomInteriorDecorator's colonnades are the
                        // other thing: columns standing clear in a room, whose tile carries a
                        // collider shaped like the drawn column, so sight and light pass
                        // through the gaps between them instead of stopping at the cell edge.
                        //
                        // Touching anything solid is what tells them apart, and it is the
                        // right test rather than a convenient one: a column with a wall
                        // against it is a buttress, not a column.
                        walls[index] = TouchesSolid(layout, x, y)
                            ? autotiled
                                ? PickWall(ExposureMask(layout, shell, x, y), wallBlock, x, y)
                                : wallTile
                            : pillar;
                        break;

                    case CellType.Rubble:
                        walls[index] = rubble;
                        break;

                    case CellType.Wall:
                        // Rock deeper than the shell is left empty. It can never be seen
                        // past the shell in front of it, and leaving it out is what turns
                        // the background from a grey slab into black void.
                        if (!shell[index]) { walls[index] = null; break; }

                        walls[index] = autotiled
                            // Every exposed side gets its own edge, so a corner reads as a
                            // corner and a one-cell buttress reads as something sticking out
                            // rather than as more of the same slab.
                            ? PickWall(ExposureMask(layout, shell, x, y), wallBlock, x, y)
                            // Fallback: a wall with open ground to its south shows its face
                            // to the camera; one buried in rock only shows its top.
                            : layout[x, y - 1] != CellType.Wall ? wallFace : wallTile;
                        break;

                    default:
                        walls[index] = null;
                        break;
                }

                // Floor goes under the painted walls too: without it, any gap the wall
                // collider leaves would show the void through the structure. Under the
                // *unpainted* rock it would defeat the point, so that stays empty.
                //
                // Doorways get the ordinary floor, like everything else. They used to get a
                // tile of their own — a dark slab between two lit jambs — which earned its
                // place while the floor was flat generated noise and an opening needed help
                // to read as an opening. Against the real floor art it stopped helping and
                // started hurting: a doorway is the one cell guaranteed to be looked at
                // straight on, and a different tile there breaks the stone that now runs
                // continuously through it, so the threshold reads as a patch rather than as
                // the floor carrying on under the door.
                if (cell != CellType.Wall || walls[index] != null)
                {
                    floors[index] = exitFloor != null && exitFloor[index]
                        ? PickExitFloor(seedHash, x, y)
                        : PickFloor(seedHash, x, y);
                }
                else floors[index] = null;
            }
        }

        floorTilemap.ClearAllTiles();
        wallTilemap.ClearAllTiles();
        floorTilemap.SetTilesBlock(bounds, floors);
        wallTilemap.SetTilesBlock(bounds, walls);

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

        var decals = new TileBase[layout.Width * layout.Height];

        if (decalTiles != null && decalTiles.Length > 0 && decalChance > 0f)
        {
            for (int y = 0; y < layout.Height; y++)
            {
                int row = y * layout.Width;
                for (int x = 0; x < layout.Width; x++)
                {
                    if (!layout.IsWalkable(x, y)) continue;

                    // Doorways stay clean: the threshold tile is already a strong shape, and
                    // anything on top of it stops the opening reading as an opening.
                    if (layout[x, y] == CellType.Door) continue;

                    float chance = decalChance * (layout.TouchesSolid(x, y) ? decalWallBias : 1f);

                    // Two independent draws off one hash: the low half decides whether there
                    // is a mark, the high half which one, so raising the chance does not also
                    // reshuffle every decal already placed.
                    uint hash = DeterministicRandom.Hash(seedHash, x, y);
                    if ((hash & 0xFFFF) / 65535f >= chance) continue;

                    decals[row + x] = decalTiles[(hash >> 16) % (uint)decalTiles.Length];
                }
            }
        }

        // Independent of the scatter above and of decalChance entirely — a guarantee, not
        // an increased chance, so turning decalChance down to look at bare floor does not
        // also take the hub's guaranteed detail with it.
        PaintGuaranteedHubDecal(layout, decals, seedHash);

        decalTilemap.SetTilesBlock(bounds, decals);
    }

    /// <summary>
    /// Salts <see cref="PaintGuaranteedHubDecal"/>'s cell pick away from the ordinary decal
    /// scatter's own hash sequence, purely so the two draws are visibly independent rather
    /// than a coincidence of reusing the same numbers for a different question.
    /// </summary>
    private const uint HubDecalSalt = 0x48554221; // "HUB!" in ASCII, arbitrarily

    /// <summary>
    /// Places <see cref="hubGuaranteedDecalTile"/> on exactly one open floor cell of the
    /// hub room, every generation. Does nothing when no tile is assigned, or the layout
    /// has no hub — which should not happen (<see cref="RoomKind.Hub"/> is always exactly
    /// one per dungeon) but a painter is not the place to assert that.
    ///
    /// The cell is not randomly walked to and accepted on the first hit, because a small
    /// hub with few eligible cells would then favour whichever happened to be tested
    /// first. Every eligible cell in the room gets a hash, and the highest wins — a
    /// uniform pick over the whole eligible set, and deterministic like everything else
    /// derived from the seed.
    /// </summary>
    private void PaintGuaranteedHubDecal(DungeonLayout layout, TileBase[] decals, uint seedHash)
    {
        if (hubGuaranteedDecalTile == null) return;

        Room hub = null;
        foreach (Room room in layout.Rooms)
        {
            if (room.Kind != RoomKind.Hub) continue;
            hub = room;
            break;
        }
        if (hub == null) return;

        bool found = false;
        uint bestHash = 0;
        Vector2Int bestCell = default;

        foreach (Vector2Int cell in hub.Cells)
        {
            if (!layout.IsWalkable(cell) || layout[cell] == CellType.Door) continue;

            uint hash = DeterministicRandom.Hash(seedHash ^ HubDecalSalt, cell.x, cell.y);
            if (found && hash <= bestHash) continue;

            found = true;
            bestHash = hash;
            bestCell = cell;
        }

        if (!found) return;
        decals[bestCell.y * layout.Width + bestCell.x] = hubGuaranteedDecalTile;
    }

    /// <summary>
    /// Which of a wall cell's four sides are exposed, as a bitmask: 1 north, 2 east,
    /// 4 south, 8 west.
    ///
    /// "Exposed" means the neighbour is not itself painted as wall — so open ground, a
    /// doorway, a pillar, and the unpainted void behind the shell all count. The void has
    /// to count, or the outer boundary of the whole structure would be drawn as if it
    /// continued into rock that is not there, and the dungeon would lose its silhouette
    /// against the black.
    /// </summary>
    /// <summary>
    /// Whether any of the four orthogonal neighbours is solid structure. Diagonals are left
    /// out on purpose: two columns touching only at a corner still have a gap between them
    /// that light gets through, and calling that pair a wall would close it.
    /// </summary>
    private static bool TouchesSolid(DungeonLayout layout, int x, int y)
    {
        return IsSolid(layout, x, y + 1) || IsSolid(layout, x + 1, y) ||
               IsSolid(layout, x, y - 1) || IsSolid(layout, x - 1, y);
    }

    /// <summary>
    /// True for the cell types that make up the structure. Outside the map counts as solid,
    /// so a pillar against the border is treated as attached to it.
    /// </summary>
    private static bool IsSolid(DungeonLayout layout, int x, int y)
    {
        if (!layout.Contains(x, y)) return true;

        CellType cell = layout[x, y];
        return cell == CellType.Wall || cell == CellType.Pillar || cell == CellType.Rubble;
    }

    private static int ExposureMask(DungeonLayout layout, bool[] shell, int x, int y)
    {
        int mask = 0;
        if (!IsPaintedWall(layout, shell, x, y + 1)) mask |= 1;
        if (!IsPaintedWall(layout, shell, x + 1, y)) mask |= 2;
        if (!IsPaintedWall(layout, shell, x, y - 1)) mask |= 4;
        if (!IsPaintedWall(layout, shell, x - 1, y)) mask |= 8;
        return mask;
    }

    /// <summary>
    /// True when the cell is drawn as part of the wall mass. Outside the map counts as
    /// wall, so the map border does not get an edge drawn along it.
    /// </summary>
    private static bool IsPaintedWall(DungeonLayout layout, bool[] shell, int x, int y)
    {
        if (!layout.Contains(x, y)) return true;
        return layout[x, y] == CellType.Wall && shell[y * layout.Width + x];
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
    /// Floor variant for a cell.
    ///
    /// Two modes, selected by <see cref="floorMosaicSize"/>:
    ///
    /// <para>
    /// <b>Mosaic</b> (size &gt; 1): the tiles are consecutive pieces of one larger image, so
    /// the cell's position within the block decides which piece goes there and the artwork
    /// runs continuously across the borders inside a block. This is what lets a floor texture
    /// that is not seamless still read as a floor: only the block boundary repeats, every
    /// <see cref="floorMosaicSize"/> cells, instead of every single cell. The floor's own
    /// world position drives it, not the seed — pieces have to line up with their neighbours,
    /// which is the one thing a random pick cannot do.
    /// </para>
    ///
    /// <para>
    /// <b>Scatter</b> (size 1): an independent variant per cell, chosen from the seed and the
    /// coordinates. For tile sets whose members are interchangeable rather than positional.
    /// </para>
    ///
    /// Both are a pure function of the seed and the cell, so the same dungeon always looks the
    /// same — the map is rebuilt from its seed on every load, and a floor that reshuffled each
    /// time would be visibly wrong.
    /// </summary>
    /// <summary>
    /// Marks the cells of the <see cref="RoomKind.Exit"/> room, or returns null when there
    /// is no exit room or no floor art for one. Built once per paint rather than asking
    /// <see cref="DungeonLayout.RoomAt"/> per cell: the paint loop visits every cell of the
    /// map, and the exit room is a few dozen of them.
    /// </summary>
    private bool[] BuildExitFloorMask(DungeonLayout layout)
    {
        if (exitFloorTiles == null || exitFloorTiles.Length == 0) return null;

        bool[] mask = null;
        foreach (Room room in layout.Rooms)
        {
            if (room.Kind != RoomKind.Exit) continue;

            mask ??= new bool[layout.Width * layout.Height];
            foreach (Vector2Int cell in room.Cells)
                mask[cell.y * layout.Width + cell.x] = true;
        }

        return mask;
    }

    /// <summary>
    /// Floor variant for a cell of the exit room. Always the scatter rule, never the
    /// mosaic one: the exit floor is a handful of interchangeable slabs marking a single
    /// room, not a large image that has to line up across the whole map.
    /// </summary>
    private TileBase PickExitFloor(uint seedHash, int x, int y)
    {
        if (exitFloorTiles.Length == 1) return exitFloorTiles[0];

        uint hash = DeterministicRandom.Hash(seedHash ^ ExitFloorSalt, x, y);
        return exitFloorTiles[hash % (uint)exitFloorTiles.Length];
    }

    /// <summary>
    /// Salts the exit floor's variant pick away from the ordinary floor's, so the two do
    /// not draw the same variant index at the same cell — which would make an exit tile
    /// set that mirrors the ordinary one reproduce its pattern exactly.
    /// </summary>
    private const uint ExitFloorSalt = 0x45584954; // "EXIT" in ASCII

    /// <summary>
    /// The wall tile for a cell: its exposure mask picks the block, its position within the
    /// block picks the cell of it.
    ///
    /// Positional rather than a pick from the seed, because these are not interchangeable
    /// variants — they are the pieces of one run of stonework, and only the piece that
    /// belongs at this position continues its neighbours' courses.
    /// </summary>
    private TileBase PickWall(int mask, int size, int x, int y)
    {
        if (size == 1) return wallAutotiles[mask];

        // Floor-modulo, as in PickFloor: a layout origin below zero would otherwise index
        // backwards off the block and throw.
        int column = ((x % size) + size) % size;
        int row = ((y % size) + size) % size;
        return wallAutotiles[mask * size * size + row * size + column];
    }

    private TileBase PickFloor(uint seedHash, int x, int y)
    {
        if (floorTiles.Length == 1) return floorTiles[0];

        if (floorMosaicSize > 1)
        {
            // Floor-modulo, not C#'s remainder: cell coordinates are never negative today,
            // but a layout origin that ever moved below zero would otherwise index backwards
            // off the array and throw, and the bug would look like "the floor is fine except
            // in one corner of the map".
            int size = floorMosaicSize;
            int column = ((x % size) + size) % size;
            int row = ((y % size) + size) % size;
            int index = row * size + column;

            // A short array means the mosaic was only partly generated; fall through to the
            // scatter path rather than throwing, so the dungeon still paints and the fault
            // is visible as a mismatched floor rather than as a failed build.
            if (index < floorTiles.Length) return floorTiles[index];
        }

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
