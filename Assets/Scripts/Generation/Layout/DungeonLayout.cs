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
    Door = 2
}

/// <summary>Role a room plays in the run; drives what gets spawned inside it.</summary>
public enum RoomKind
{
    /// <summary>Where the player starts. Always empty of enemies.</summary>
    Start = 0,

    /// <summary>Ordinary room: enemies and loot scale with distance from Start.</summary>
    Normal = 1,

    /// <summary>Safe room with a save station and a light source. Always empty of enemies.</summary>
    Camp = 2,

    /// <summary>The deepest room, holding the run's reward.</summary>
    Treasure = 3
}

/// <summary>One rectangular room of a generated dungeon.</summary>
public sealed class Room
{
    /// <summary>Index into <see cref="DungeonLayout.Rooms"/>; also part of entity guids.</summary>
    public int Index { get; }

    /// <summary>Floor area of the room in cell coordinates, walls excluded.</summary>
    public RectInt Bounds { get; }

    /// <summary>Role in the run; assigned after the graph is built.</summary>
    public RoomKind Kind { get; internal set; }

    /// <summary>Hop count from the Start room over the room graph; 0 for Start itself.</summary>
    public int DepthFromStart { get; internal set; }

    /// <summary>Number of corridors attached to this room; 1 means a dead end.</summary>
    public int Degree { get; internal set; }

    /// <summary>Cell at the middle of the room, rounded down.</summary>
    public Vector2Int Center =>
        new Vector2Int(Bounds.xMin + Bounds.width / 2, Bounds.yMin + Bounds.height / 2);

    public Room(int index, RectInt bounds)
    {
        Index = index;
        Bounds = bounds;
        Kind = RoomKind.Normal;
    }

    /// <summary>True when the cell lies inside this room's floor area.</summary>
    public bool Contains(Vector2Int cell)
    {
        return cell.x >= Bounds.xMin && cell.x < Bounds.xMax &&
               cell.y >= Bounds.yMin && cell.y < Bounds.yMax;
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

    public int Width { get; }
    public int Height { get; }

    /// <summary>Seed this layout was generated from; stored in saves to rebuild it.</summary>
    public string Seed { get; }

    /// <summary>Cell the player spawns on — the centre of the Start room.</summary>
    public Vector2Int SpawnCell { get; internal set; }

    public IReadOnlyList<Room> Rooms => _rooms;
    public IReadOnlyList<RoomLink> Links => _links;

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

    /// <summary>True when the cell can be walked on — anything that is not a wall.</summary>
    public bool IsWalkable(int x, int y)
    {
        return this[x, y] != CellType.Wall;
    }

    public bool IsWalkable(Vector2Int cell) => IsWalkable(cell.x, cell.y);

    /// <summary>Returns the room containing the cell, or null when it is a corridor or wall.</summary>
    public Room RoomAt(Vector2Int cell)
    {
        foreach (var room in _rooms)
        {
            if (room.Contains(cell)) return room;
        }
        return null;
    }

    /// <summary>Number of non-wall cells; used by the metrics overlay and by tests.</summary>
    public int CountWalkable()
    {
        int count = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_cells[x, y] != CellType.Wall) count++;
            }
        }
        return count;
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
                    _ => '#'
                });
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
