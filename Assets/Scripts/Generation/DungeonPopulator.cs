using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fills a freshly built dungeon with doors, enemies, loot, props and the camp room's
/// fixtures, turning a walkable maze into a playable run.
///
/// Subscribes to <see cref="DungeonBuilder.Built"/> rather than being called directly,
/// so generation stays ignorant of what content exists.
///
/// Every spawn is driven by a stream derived from the dungeon seed, and every saveable
/// spawn gets a guid derived from the seed and its slot. That is what lets the save
/// system store a seed instead of a world: regenerating produces the same objects under
/// the same identities, and the save only has to record which of them are gone.
/// </summary>
public class DungeonPopulator : MonoBehaviour
{
    private const string ContentRootName = "GeneratedContent";

    [Header("Wiring")]
    [SerializeField] private DungeonBuilder builder;
    [SerializeField] private RoomContentSettings content;

    [Tooltip("Optional. Resolved from Resources when left empty.")]
    [SerializeField] private PrefabRegistry registry;

    private Transform _contentRoot;
    private PrefabRegistry _prefabs;
    private WorldItemPickup _pickup;
    private Transform _player;

    private void OnEnable()
    {
        if (builder != null) builder.Built += Populate;
    }

    private void OnDisable()
    {
        if (builder != null) builder.Built -= Populate;
    }

    /// <summary>
    /// Spawns everything for the given layout, replacing whatever a previous build left
    /// behind.
    /// </summary>
    public void Populate(DungeonLayout layout)
    {
        if (layout == null) return;

        _prefabs = registry != null ? registry : PrefabRegistry.Instance;
        if (content == null || _prefabs == null)
        {
            Debug.LogWarning(
                "[DungeonPopulator] No content settings or prefab registry — the dungeon " +
                "will be empty.", this);
            return;
        }

        // Resolved once per build: the scene lookups are far too slow to repeat per spawn.
        _pickup = FindFirstObjectByType<WorldItemPickup>();
        _player = ResolvePlayer();

        if (_pickup == null && WantsLoot())
        {
            // Worth saying out loud: everything else still spawns, so a dungeon with no
            // loot anywhere looks like a bad roll rather than a missing scene component.
            Debug.LogWarning(
                "[DungeonPopulator] No WorldItemPickup in the scene — no loot will be " +
                "spawned, including the guaranteed items. Run Tools ▸ Slot Inventory ▸ " +
                "Build UI & Wire Scene.", this);
        }

        ResetContentRoot();

        var random = new DeterministicRandom(layout.Seed).Derive("content");
        SpawnDoors(layout);
        SpawnRoomContent(layout, random.Derive("rooms"));
        SpawnGuaranteedItems(layout, random.Derive("guaranteed"));
    }

    // ---------------------------------------------------------------- doors

    private void SpawnDoors(DungeonLayout layout)
    {
        if (string.IsNullOrEmpty(content.doorPrefabId)) return;

        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                if (layout[x, y] != CellType.Door) continue;

                var cell = new Vector2Int(x, y);
                // Keyed by cell, not by a running index: a door's identity must not
                // shift when an unrelated room gains an extra doorway.
                _prefabs.Spawn(content.doorPrefabId, builder.CellCenter(cell), _contentRoot,
                    CellGuid(layout.Seed, "door", cell));
            }
        }
    }

    // ---------------------------------------------------------------- rooms

    private void SpawnRoomContent(DungeonLayout layout, DeterministicRandom random)
    {
        foreach (var room in layout.Rooms)
        {
            // Per-room stream keyed by index, so retuning one room's contents cannot
            // shift every later room's rolls.
            var roomRandom = random.Derive($"room{room.Index}");
            List<Vector2Int> free = FreeCells(layout, room, roomRandom);
            int slot = 0;

            // An authored interior replaces the room's scatter, so the design is not
            // buried under random loot and props. Enemies still come from the table:
            // difficulty has to keep scaling with depth either way.
            bool authored = ApplyTemplate(layout, room, free, roomRandom, ref slot);

            switch (room.Kind)
            {
                case RoomKind.Start:
                    break; // deliberately empty: the player must not open their eyes in a fight

                case RoomKind.Camp:
                    SpawnCampFixtures(layout, room, free, ref slot);
                    break;

                case RoomKind.Treasure:
                    if (!authored)
                        SpawnItems(content.treasureLoot, content.treasureLootCount, free, roomRandom);
                    SpawnEnemies(layout, room, free, roomRandom, ref slot);
                    break;

                default:
                    SpawnEnemies(layout, room, free, roomRandom, ref slot);
                    if (!authored)
                    {
                        SpawnItems(content.loot,
                            roomRandom.RangeInclusive(content.minLootPerRoom, content.maxLootPerRoom),
                            free, roomRandom);
                    }
                    break;
            }

            if (!authored) SpawnProps(layout, room, free, roomRandom, ref slot);
        }
    }

    private void SpawnEnemies(DungeonLayout layout, Room room, List<Vector2Int> free,
        DeterministicRandom random, ref int slot)
    {
        int count = content.EnemyCountFor(room.DepthFromStart, random);

        for (int i = 0; i < count; i++)
        {
            var choice = content.PickPrefab(content.enemies, room.DepthFromStart, random);
            if (choice == null) return;
            if (!TryTakeCell(free, out Vector2Int cell)) return;

            GameObject enemy = _prefabs.Spawn(choice.prefabId, builder.CellCenter(cell),
                _contentRoot, SlotGuid(layout.Seed, room.Index, slot++));
            if (enemy == null) continue;

            // A prefab asset cannot reference a scene object, so a spawned enemy starts
            // with no player and would never detect anything without this.
            var behaviour = enemy.GetComponent<EnemyBase>();
            if (behaviour != null) behaviour.SetPlayer(_player);
        }
    }

    private void SpawnCampFixtures(DungeonLayout layout, Room room, List<Vector2Int> free, ref int slot)
    {
        if (!string.IsNullOrEmpty(content.saveStationPrefabId) &&
            TryTakeCell(free, out Vector2Int stationCell))
        {
            _prefabs.Spawn(content.saveStationPrefabId, builder.CellCenter(stationCell),
                _contentRoot, SlotGuid(layout.Seed, room.Index, slot++));
        }

        if (!string.IsNullOrEmpty(content.lightPrefabId) &&
            TryTakeCell(free, out Vector2Int lightCell))
        {
            _prefabs.Spawn(content.lightPrefabId, builder.CellCenter(lightCell),
                _contentRoot, SlotGuid(layout.Seed, room.Index, slot++));
        }
    }

    private void SpawnProps(DungeonLayout layout, Room room, List<Vector2Int> free,
        DeterministicRandom random, ref int slot)
    {
        // The room's own cells, not its bounding box: an L-shaped room covers roughly
        // half its box, and scattering by the box would fill it twice as densely.
        float expected = room.Area * content.propsPerHundredFloorCells / 100f;

        int count = Mathf.FloorToInt(expected);
        if (random.Chance(expected - count)) count++;

        for (int i = 0; i < count; i++)
        {
            var choice = content.PickPrefab(content.props, room.DepthFromStart, random);
            if (choice == null) return;
            if (!TryTakeCell(free, out Vector2Int cell)) return;

            _prefabs.Spawn(choice.prefabId, builder.CellCenter(cell), _contentRoot,
                SlotGuid(layout.Seed, room.Index, slot++));
        }
    }

    // ---------------------------------------------------------------- templates

    /// <summary>
    /// Converts an authored room template's markers into spawns. Returns false when no
    /// template fits or the roll went against it, leaving the room to the random tables.
    ///
    /// Only the markers are instantiated — the template prefab itself is never placed in
    /// the scene, so it stays pure authoring data and cannot leak stray objects.
    /// </summary>
    private bool ApplyTemplate(DungeonLayout layout, Room room, List<Vector2Int> free,
        DeterministicRandom random, ref int slot)
    {
        DungeonRoomTemplate template = content.PickTemplate(room, random);
        if (template == null) return false;

        Vector2Int anchor = template.AnchorIn(room);

        foreach (DungeonSpawnMarker marker in template.Markers)
        {
            if (marker == null) continue;

            Vector2Int cell = anchor + marker.CellOffset;

            // A marker pushed outside the room by a bad footprint would otherwise spawn
            // into rock or into the corridor beyond.
            if (!room.Contains(cell) || !layout.IsWalkable(cell)) continue;

            // Reserve the cell whether or not the marker fires, so a marker that rolled
            // against itself still leaves the designed gap.
            free.Remove(cell);

            if (!random.Chance(marker.Chance)) continue;

            switch (marker.Kind)
            {
                case SpawnMarkerKind.Prefab:
                    if (!string.IsNullOrEmpty(marker.Id))
                    {
                        _prefabs.Spawn(marker.Id, builder.CellCenter(cell), _contentRoot,
                            SlotGuid(layout.Seed, room.Index, slot++));
                    }
                    break;

                case SpawnMarkerKind.Item:
                    Drop(marker.Id, random.RangeInclusive(marker.MinCount, marker.MaxCount), cell);
                    break;

                case SpawnMarkerKind.KeepClear:
                    break; // the reservation above was the whole point
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- loot

    private void SpawnItems(List<RoomContentSettings.ItemChoice> pool, int count,
        List<Vector2Int> free, DeterministicRandom random)
    {
        for (int i = 0; i < count; i++)
        {
            var choice = content.PickItem(pool, random);
            if (choice == null) return;
            if (!TryTakeCell(free, out Vector2Int cell)) return;

            Drop(choice.itemId, random.RangeInclusive(choice.minCount, choice.maxCount), cell);
        }
    }

    /// <summary>
    /// Tops the dungeon up to a minimum number of a critical item — lamp fuel, normally.
    /// Spawned last, spread across rooms, because a run that cannot be lit cannot be
    /// finished and per-room rolls alone put no floor on the total.
    /// </summary>
    private void SpawnGuaranteedItems(DungeonLayout layout, DeterministicRandom random)
    {
        if (string.IsNullOrEmpty(content.guaranteedItemId) || content.guaranteedItemDrops <= 0)
            return;

        var candidates = new List<Room>();
        foreach (var room in layout.Rooms)
        {
            if (room.Kind != RoomKind.Start) candidates.Add(room);
        }
        if (candidates.Count == 0) return;

        for (int i = 0; i < content.guaranteedItemDrops; i++)
        {
            Room room = candidates[i % candidates.Count];
            List<Vector2Int> cells = FreeCells(layout, room, random.Derive($"drop{i}"));
            if (!TryTakeCell(cells, out Vector2Int cell)) continue;

            Drop(content.guaranteedItemId, 1, cell);
        }
    }

    /// <summary>
    /// Spawns one drop through <see cref="WorldItemPickup.SpawnAt"/> rather than
    /// instantiating a prefab, so generated loot is indistinguishable from a player
    /// drop. That is also why loot needs no guid: <see cref="WorldItemsSaveable"/>
    /// already captures every drop by position.
    /// </summary>
    private void Drop(string itemId, int count, Vector2Int cell)
    {
        if (_pickup == null || string.IsNullOrEmpty(itemId)) return;

        ItemData item = ItemDatabase.Instance != null ? ItemDatabase.Instance.Resolve(itemId) : null;
        if (item == null)
        {
            Debug.LogWarning(
                $"[DungeonPopulator] Item id '{itemId}' is not in the ItemDatabase — drop " +
                "skipped. Run Tools ▸ Save System ▸ Rebuild Item Database.", this);
            return;
        }

        _pickup.SpawnAt(item, count, builder.CellCenter(cell));
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// The room's floor cells in a seed-determined order, minus anything next to a
    /// doorway. Consuming from one shuffled list is what guarantees no two spawns ever
    /// land on the same cell.
    /// </summary>
    private static List<Vector2Int> FreeCells(DungeonLayout layout, Room room, DeterministicRandom random)
    {
        var cells = new List<Vector2Int>(room.Area);

        foreach (Vector2Int cell in room.Cells)
        {
            // A room cell is not automatically open ground: the interior pass turns some
            // of them into pillars and rubble.
            if (!layout.IsWalkable(cell)) continue;
            if (IsNextToDoorway(layout, cell)) continue; // never block an opening
            cells.Add(cell);
        }

        random.Shuffle(cells);
        return cells;
    }

    private static bool IsNextToDoorway(DungeonLayout layout, Vector2Int cell)
    {
        return layout[cell.x + 1, cell.y] == CellType.Door ||
               layout[cell.x - 1, cell.y] == CellType.Door ||
               layout[cell.x, cell.y + 1] == CellType.Door ||
               layout[cell.x, cell.y - 1] == CellType.Door;
    }

    private static bool TryTakeCell(List<Vector2Int> free, out Vector2Int cell)
    {
        if (free.Count == 0)
        {
            cell = default;
            return false;
        }

        int last = free.Count - 1;
        cell = free[last];
        free.RemoveAt(last);
        return true;
    }

    /// <summary>Deterministic identity for a room spawn: same seed and slot, same guid.</summary>
    private static string SlotGuid(string seed, int roomIndex, int slot)
    {
        return $"{seed}:{roomIndex}:{slot}";
    }

    /// <summary>Deterministic identity for something placed at a fixed cell, such as a door.</summary>
    private static string CellGuid(string seed, string category, Vector2Int cell)
    {
        return $"{seed}:{category}:{cell.x}:{cell.y}";
    }

    /// <summary>
    /// All generated content lives under one child object, so a rebuild drops the
    /// previous dungeon's objects wholesale instead of hunting them down.
    /// </summary>
    private void ResetContentRoot()
    {
        Transform stale = _contentRoot != null ? _contentRoot : transform.Find(ContentRootName);
        if (stale != null)
        {
            // Destroy is deferred to the end of the frame in play mode. Detaching and
            // renaming first keeps the outgoing objects from being found by the build
            // that is about to run.
            stale.name = ContentRootName + " (discarded)";
            stale.SetParent(null, false);

            if (Application.isPlaying) Destroy(stale.gameObject);
            else DestroyImmediate(stale.gameObject);
        }

        var root = new GameObject(ContentRootName);
        root.transform.SetParent(transform, false);
        _contentRoot = root.transform;
    }

    /// <summary>True when the content table would place any item at all.</summary>
    private bool WantsLoot()
    {
        return content.maxLootPerRoom > 0 ||
               content.treasureLootCount > 0 ||
               content.guaranteedItemDrops > 0;
    }

    private static Transform ResolvePlayer()
    {
        var gameManager = FindFirstObjectByType<GameManager>();
        GameObject player = gameManager != null ? gameManager.GetPlayer() : null;
        if (player != null) return player.transform;

        var movement = FindFirstObjectByType<PlayerMovement>();
        return movement != null ? movement.transform : null;
    }
}
