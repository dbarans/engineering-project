using System.Collections.Generic;
using UnityEngine;

/// <summary>What occupies one cell of a generated dungeon.</summary>
public enum CellType
{
    /// <summary>Solid rock. Painted into the walls tilemap, blocks movement and vision.</summary>
    Wall = 0,

    /// <summary>Open ground.</summary>
    Floor = 1,

    /// <summary>Open ground marking a room entrance — where door prefabs are placed.</summary>
    Door = 2,

    /// <summary>
    /// A free-standing pillar or partition wall inside a room. Solid and vision-blocking
    /// in exactly the same way as <see cref="Wall"/>; it is a separate type only so the
    /// painter can give it its own art and the populator can tell it apart from bedrock.
    ///
    /// It has to be a cell rather than a prop prefab. The project's convention is that
    /// props never block vision or pathfinding, so a pillar spawned as a prop would look
    /// like an obstacle while rays passed straight through it.
    /// </summary>
    Pillar = 3,

    /// <summary>
    /// Collapsed masonry. Impassable and vision-blocking like <see cref="Pillar"/>, but
    /// read as knee-height debris rather than as structure, and placed against walls and
    /// in corners to break the straight sightlines a bare room hands the player for free.
    /// </summary>
    Rubble = 4
}

/// <summary>Role a room plays in the run; drives what gets spawned inside it.</summary>
public enum RoomKind
{
    /// <summary>
    /// The one safe room of the dungeon, holding the save station and the crafting table.
    /// Where the player starts, and the origin every other room's depth is measured
    /// from. Always empty of enemies, always at the middle of the map, and always exactly
    /// one per dungeon — saving, crafting and the spawn point are meant to be somewhere
    /// the player has to walk back to, which only works while there is a single such
    /// place to walk to. There used to be a separate <c>Start</c> role; once the player's
    /// spawn point and the safe room became the same place, keeping both meant one of them
    /// was a second safe room with nothing to justify it.
    /// </summary>
    Hub = 0,

    /// <summary>Ordinary room: enemies and loot scale with distance from the hub.</summary>
    Normal = 1,

    /// <summary>The room farthest from the hub over the room graph, holding the run's reward.</summary>
    Treasure = 2
}

/// <summary>
/// Floor plan a room was cut to. Rooms stopped being plain rectangles because a
/// rectangle is read in a single glance from the doorway and is then spent: there is
/// nothing left to find out by walking into it.
/// </summary>
public enum RoomShape
{
    /// <summary>Plain rectangle. Kept deliberately — without it the others lose contrast.</summary>
    Rectangle = 0,

    /// <summary>Two arms meeting at a corner; the far arm cannot be seen from the near one.</summary>
    Ell = 1,

    /// <summary>Three arms off a spine, so there are two blind areas instead of one.</summary>
    Tee = 2,

    /// <summary>
    /// Rectangle around a solid core. The player has to commit to one way round, and
    /// whatever is following can take the other.
    /// </summary>
    Ring = 3,

    /// <summary>Overlapping lobes smoothed by cellular automata; reads as collapsed rock.</summary>
    Cavern = 4
}

/// <summary>
/// One room of a generated dungeon, held as the set of cells it occupies.
///
/// Deliberately not a <see cref="RectInt"/> any more. The rectangle survives as
/// <see cref="Bounds"/> for placement and template fitting, but the room's actual shape
/// is <see cref="Cells"/> — that is what lets a room be L-shaped, ring-shaped or
/// cave-like, which is the whole point of the shaping pass.
/// </summary>
public sealed class Room
{
    private readonly List<Vector2Int> _cells;
    private readonly HashSet<Vector2Int> _lookup;

    /// <summary>Index into <see cref="DungeonLayout.Rooms"/>; also part of entity guids.</summary>
    public int Index { get; }

    /// <summary>
    /// Axis-aligned box containing every cell of the room. For anything but a
    /// <see cref="RoomShape.Rectangle"/> this is larger than the room itself, so it is
    /// only good for placement, spacing and template footprints — never for "is this
    /// cell in the room", which is what <see cref="Contains"/> is for.
    /// </summary>
    public RectInt Bounds { get; }

    /// <summary>The floor plan this room was cut to.</summary>
    public RoomShape Shape { get; }

    /// <summary>
    /// Every cell belonging to the room, in a deterministic order. Some of them may have
    /// been turned into pillars or rubble afterwards, so walkability is still a question
    /// for <see cref="DungeonLayout.IsWalkable(Vector2Int)"/>, not for this list.
    /// </summary>
    public IReadOnlyList<Vector2Int> Cells => _cells;

    /// <summary>Cell count. The honest area measure once rooms stop being rectangles.</summary>
    public int Area => _cells.Count;

    /// <summary>Role in the run; assigned after the graph is built.</summary>
    public RoomKind Kind { get; internal set; }

    /// <summary>Hop count from the hub over the room graph; 0 for the hub itself.</summary>
    public int DepthFromHub { get; internal set; }

    /// <summary>Number of corridors attached to this room; 1 means a dead end.</summary>
    public int Degree { get; internal set; }

    /// <summary>
    /// A cell of the room near its middle — the medoid, not the centre of
    /// <see cref="Bounds"/>. For an L-shaped or ring-shaped room the box centre lands in
    /// solid rock, and corridors are carved between room centres, so using it would start
    /// corridors inside walls.
    /// </summary>
    public Vector2Int Center { get; }

    /// <summary>Builds a room from its cells. The collection must not be empty.</summary>
    public Room(int index, IReadOnlyList<Vector2Int> cells, RoomShape shape)
    {
        Index = index;
        Shape = shape;
        Kind = RoomKind.Normal;

        _cells = new List<Vector2Int>(cells);
        _lookup = new HashSet<Vector2Int>(_cells);

        Bounds = ComputeBounds(_cells);
        Center = ComputeMedoid(_cells);
    }

    /// <summary>Convenience for a plain rectangular room.</summary>
    public Room(int index, RectInt bounds)
        : this(index, CellsOf(bounds), RoomShape.Rectangle)
    {
    }

    /// <summary>True when the cell belongs to this room.</summary>
    public bool Contains(Vector2Int cell) => _lookup.Contains(cell);

    private static List<Vector2Int> CellsOf(RectInt bounds)
    {
        var cells = new List<Vector2Int>(bounds.width * bounds.height);
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                cells.Add(new Vector2Int(x, y));
        }
        return cells;
    }

    private static RectInt ComputeBounds(List<Vector2Int> cells)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        foreach (Vector2Int cell in cells)
        {
            if (cell.x < minX) minX = cell.x;
            if (cell.y < minY) minY = cell.y;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y > maxY) maxY = cell.y;
        }

        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// The cell closest to the arithmetic centre of the room. Ties are broken by
    /// coordinate so the result cannot depend on the order the cells were produced in.
    /// </summary>
    private static Vector2Int ComputeMedoid(List<Vector2Int> cells)
    {
        long sumX = 0, sumY = 0;
        foreach (Vector2Int cell in cells)
        {
            sumX += cell.x;
            sumY += cell.y;
        }

        var centroid = new Vector2Int(
            Mathf.RoundToInt(sumX / (float)cells.Count),
            Mathf.RoundToInt(sumY / (float)cells.Count));

        Vector2Int best = cells[0];
        int bestDistance = int.MaxValue;

        foreach (Vector2Int cell in cells)
        {
            int distance = Mathf.Abs(cell.x - centroid.x) + Mathf.Abs(cell.y - centroid.y);
            if (distance > bestDistance) continue;

            if (distance < bestDistance ||
                cell.y < best.y || (cell.y == best.y && cell.x < best.x))
            {
                bestDistance = distance;
                best = cell;
            }
        }

        return best;
    }
}

/// <summary>A corridor connecting two rooms.</summary>
public readonly struct RoomLink
{
    public readonly int RoomA;
    public readonly int RoomB;

    public RoomLink(int roomA, int roomB)
    {
        RoomA = roomA;
        RoomB = roomB;
    }
}

/// <summary>
/// The abstract result of dungeon generation: a grid of cell types plus the rooms and
/// corridors that produced it. Contains no Unity scene objects — painting it into
/// tilemaps and populating it with content are separate, later stages.
/// </summary>
public sealed class DungeonLayout
{
    private readonly CellType[,] _cells;
    private readonly List<Room> _rooms;
    private readonly List<RoomLink> _links;
    private readonly List<Vector2Int> _alcoves = new List<Vector2Int>();
    private readonly List<Vector2Int> _chokepoints = new List<Vector2Int>();

    public int Width { get; }
    public int Height { get; }

    /// <summary>Seed this layout was generated from; stored in saves to rebuild it.</summary>
    public string Seed { get; }

    /// <summary>Cell the player spawns on — the centre of the hub.</summary>
    public Vector2Int SpawnCell { get; internal set; }

    public IReadOnlyList<Room> Rooms => _rooms;
    public IReadOnlyList<RoomLink> Links => _links;

    /// <summary>
    /// Blind pockets carved off the sides of corridors. Recorded rather than rediscovered
    /// later by heuristics, because they are the layout's ready-made ambush slots: a spot
    /// the player walks past without being able to look into it.
    /// </summary>
    public IReadOnlyList<Vector2Int> Alcoves => _alcoves;

    /// <summary>
    /// Cells whose removal would split the dungeon in two — the places where a fight
    /// cannot be walked away from. Populated by <see cref="Chokepoints"/> analysis.
    /// </summary>
    public IReadOnlyList<Vector2Int> Chokepoints => _chokepoints;

    /// <summary>Lazily-built lookup backing <see cref="NearAnyChokepoint"/>.</summary>
    private HashSet<Vector2Int> _chokepointSet;

    /// <summary>
    /// True when the cell is a chokepoint or within <paramref name="radius"/> cells
    /// (Chebyshev) of one.
    ///
    /// For anything that physically blocks movement — a prop with a real collider, not
    /// just a spawn marker — a chokepoint is not merely inconvenient to stand on, it is
    /// the one cell holding two halves of the dungeon together. Built as a set on first
    /// use rather than scanning <see cref="Chokepoints"/> per query, since population
    /// calls this once per candidate cell and there can be hundreds of both.
    /// </summary>
    public bool NearAnyChokepoint(Vector2Int cell, int radius)
    {
        if (_chokepointSet == null) _chokepointSet = new HashSet<Vector2Int>(_chokepoints);
        if (_chokepointSet.Count == 0) return false;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (_chokepointSet.Contains(cell + new Vector2Int(dx, dy))) return true;
            }
        }
        return false;
    }

    public DungeonLayout(string seed, int width, int height, List<Room> rooms, List<RoomLink> links)
    {
        Seed = seed;
        Width = width;
        Height = height;
        _cells = new CellType[width, height];
        _rooms = rooms ?? new List<Room>();
        _links = links ?? new List<RoomLink>();
    }

    /// <summary>Cell accessor; reading out of bounds yields <see cref="CellType.Wall"/>.</summary>
    public CellType this[int x, int y]
    {
        get => Contains(x, y) ? _cells[x, y] : CellType.Wall;
        internal set { if (Contains(x, y)) _cells[x, y] = value; }
    }

    public CellType this[Vector2Int cell]
    {
        get => this[cell.x, cell.y];
        internal set => this[cell.x, cell.y] = value;
    }

    /// <summary>True when the coordinates are inside the map.</summary>
    public bool Contains(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    /// <summary>
    /// True when the cell can be walked on. Stated as an explicit allow-list rather than
    /// "not wall": pillars and rubble are open ground's opposite, and a rule phrased the
    /// other way round would silently let every new solid cell type become walkable.
    /// </summary>
    public bool IsWalkable(int x, int y)
    {
        CellType cell = this[x, y];
        return cell == CellType.Floor || cell == CellType.Door;
    }

    public bool IsWalkable(Vector2Int cell) => IsWalkable(cell.x, cell.y);

    /// <summary>
    /// True when the cell stops a line of sight. Identical to "not walkable" today, and
    /// kept separate anyway because the two answers are asked by different systems and
    /// will not stay identical the first time a grate or a low railing shows up.
    /// </summary>
    public bool BlocksVision(int x, int y) => !IsWalkable(x, y);

    public bool BlocksVision(Vector2Int cell) => BlocksVision(cell.x, cell.y);

    /// <summary>
    /// True when any of the four neighbours is solid — the cell is against a wall, a
    /// pillar or a pile of rubble rather than out in the open.
    ///
    /// Shared because two passes want the same notion of "at the edge of the room" for
    /// opposite-looking reasons that are really the same one: wear collects along the
    /// walls, and so do the things people leave standing.
    /// </summary>
    public bool TouchesSolid(int x, int y)
    {
        return !IsWalkable(x - 1, y) || !IsWalkable(x + 1, y) ||
               !IsWalkable(x, y - 1) || !IsWalkable(x, y + 1);
    }

    public bool TouchesSolid(Vector2Int cell) => TouchesSolid(cell.x, cell.y);

    /// <summary>Returns the room containing the cell, or null when it is a corridor or wall.</summary>
    public Room RoomAt(Vector2Int cell)
    {
        foreach (var room in _rooms)
        {
            if (room.Contains(cell)) return room;
        }
        return null;
    }

    /// <summary>Number of walkable cells; used by the metrics overlay and by tests.</summary>
    public int CountWalkable()
    {
        int count = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (IsWalkable(x, y)) count++;
            }
        }
        return count;
    }

    internal void AddAlcove(Vector2Int cell) => _alcoves.Add(cell);

    internal void SetChokepoints(IEnumerable<Vector2Int> cells)
    {
        _chokepointSet = null; // stale after a re-detect; NearAnyChokepoint rebuilds lazily
        _chokepoints.Clear();
        _chokepoints.AddRange(cells);
    }

    /// <summary>
    /// Stable hash of the whole cell grid. Two layouts with the same hash are identical,
    /// which is how the determinism tests compare generator runs without dumping grids.
    /// </summary>
    public uint ContentHash()
    {
        unchecked
        {
            uint hash = 2166136261;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    hash = (hash ^ (byte)_cells[x, y]) * 16777619;
            }
            return hash;
        }
    }

    /// <summary>
    /// Renders the layout as ASCII (row 0 last, so it reads the same way it looks in
    /// the scene). Used by the editor debug dump — eyeballing twenty of these is far
    /// faster than painting twenty dungeons.
    /// </summary>
    public string ToAscii()
    {
        var builder = new System.Text.StringBuilder((Width + 1) * Height);
        for (int y = Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < Width; x++)
            {
                if (SpawnCell.x == x && SpawnCell.y == y) { builder.Append('@'); continue; }
                builder.Append(_cells[x, y] switch
                {
                    CellType.Floor => '.',
                    CellType.Door => '+',
                    CellType.Pillar => 'o',
                    CellType.Rubble => '%',
                    _ => '#'
                });
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
