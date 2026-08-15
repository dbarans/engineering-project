using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fills a freshly built dungeon with doors, enemies, loot, props and the hub room's
/// fixtures, turning a walkable maze into a playable run.
///
/// Subscribes to <see cref="DungeonBuilder.Built"/> rather than being called directly,
/// so generation stays ignorant of what content exists.
///
/// Every spawn is driven by a stream derived from the dungeon seed, and every saveable
/// spawn gets a guid derived from the seed and its slot. That is what lets the save
/// system store a seed instead of a world: regenerating produces the same objects under
/// the same identities, and the save only has to record which of them are gone.
///
/// <para>
/// <see cref="ExecuteAlways"/> is load-bearing, not decoration. The subscription to
/// <see cref="DungeonBuilder.Built"/> happens in <see cref="OnEnable"/>, and a plain
/// <see cref="MonoBehaviour"/> has no edit-mode lifecycle — so without it, generating from
/// the builder's context menu raised the event to an empty subscriber list and the dungeon
/// came out as bare geometry: no doors, no enemies, no props. That is the case that
/// matters most here, because the Dungeon scene is authored by baking a generated dungeon
/// into it rather than by generating at runtime.
/// </para>
/// </summary>
[ExecuteAlways]
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
        SpawnCorridorAmbushes(layout, random.Derive("ambushes"));
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
                Vector3 spawnPos = builder.CellCenter(cell);

                GameObject doorInstance = _prefabs.Spawn(content.doorPrefabId, spawnPos, _contentRoot, CellGuid(layout.Seed, "door", cell));

                if (doorInstance != null)
                {
                    bool wallEast = !layout.IsWalkable(x + 1, y);
                    bool wallWest = !layout.IsWalkable(x - 1, y);

                    doorInstance.transform.rotation = wallEast && wallWest
                        ? Quaternion.Euler(0f, 0f, 90f)
                        : Quaternion.identity;

                    CenterOnCollider(doorInstance, spawnPos, builder.CellSize);
                }
            }
        }
    }

    /// <summary>
    /// Nudges a spawned door so its collider — not its prefab's off-centre hinge pivot —
    /// lands exactly on the cell centre. The hinge pivot sits off-centre by design (it has
    /// to, for the swing-open rotation), so positioning the door's root transform at the
    /// cell centre leaves the actual door leaf offset from it, opening a gap at the doorway.
    /// Deriving the correction from the door's own geometry self-corrects for that offset at
    /// any rotation angle, without needing to know the prefab's internal pivot layout or
    /// hand-tune a per-orientation constant against it.
    ///
    /// <para>
    /// The centre is computed from the <b>transform hierarchy</b>, deliberately not from
    /// <see cref="Collider2D.bounds"/>. Bounds are physics-backed, and with
    /// <see cref="Physics2D.autoSyncTransforms"/> off (the default) they do not reflect a
    /// transform written this same frame until the next physics step — which in edit mode
    /// never comes at all. This method runs during edit-mode baking, so reading bounds here
    /// returned an unsynced centre of roughly the origin, and every door was displaced by
    /// nearly its own map coordinate, i.e. clean off the map. <c>TransformPoint</c> is pure
    /// matrix maths on transforms already written, so it is correct the instant the rotation
    /// above is set.
    /// </para>
    ///
    /// <para>
    /// <see cref="Collider2D.offset"/> is the shape centre in the collider's local space for
    /// the box, circle and capsule shapes; a polygon or edge collider whose vertices are not
    /// centred on their own offset would need its bounds instead, and a synced read to go
    /// with them. Doors are box-collidered, and the guard below catches the mismatch loudly
    /// rather than silently flinging a door somewhere if that ever changes.
    /// </para>
    /// </summary>
    private static void CenterOnCollider(GameObject doorInstance, Vector2 cellCenter, float cellSize)
    {
        Collider2D doorCollider = doorInstance.GetComponentInChildren<Collider2D>();
        if (doorCollider == null) return;

        Vector2 colliderCenter = doorCollider.transform.TransformPoint(doorCollider.offset);
        Vector2 correction = cellCenter - colliderCenter;

        // A door's pivot sits a fraction of a cell off centre; anything approaching a whole
        // cell means the centre was misread, and applying it would move the door somewhere
        // unrelated to its doorway. Leaving it at the raw spawn position is wrong by at most
        // that same fraction, and stays visibly *at* the doorway where the fault can be seen.
        if (correction.magnitude > cellSize)
        {
            Debug.LogWarning(
                $"[DungeonPopulator] Door at {cellCenter} wanted a {correction.magnitude:F2}-unit " +
                $"centring correction, more than one {cellSize}-unit cell. Leaving it uncentred; " +
                "its collider geometry likely no longer matches what CenterOnCollider assumes.",
                doorInstance);
            return;
        }

        doorInstance.transform.position += (Vector3)correction;
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

            // Shared across every blocking object spawned in this room — chests, then
            // props — so a later cluster can see what an earlier one (or the chests
            // before it) already occupied. Each cluster used to track spacing only
            // against its own members and reset the list on the next one, which is
            // exactly how a barrel cluster and a table cluster ended up overlapping:
            // neither knew the other existed.
            var roomOccupied = new List<(Vector2Int cell, int footprint)>();

            // An authored interior replaces the room's scatter, so the design is not
            // buried under random loot and props. Enemies still come from the table:
            // difficulty has to keep scaling with depth either way.
            bool authored = ApplyTemplate(layout, room, free, roomRandom, ref slot);

            switch (room.Kind)
            {
                case RoomKind.Hub:
                    // No SpawnEnemies call, and that is the room's whole purpose rather
                    // than an oversight: the hub is the one place in the dungeon nothing
                    // is waiting for the player.
                    SpawnHubFixtures(layout, room, free, ref slot);
                    break;

                case RoomKind.Treasure:
                    if (!authored)
                        SpawnItems(content.treasureLoot, content.treasureLootCount, free, roomRandom);
                    SpawnEnemies(layout, room, free, roomRandom, ref slot);
                    if (!authored)
                    {
                        var treasureTable = content.treasureChestLoot.Count > 0
                            ? content.treasureChestLoot
                            : content.chestLoot;
                        SpawnChests(layout, room, free, roomOccupied, roomRandom.Derive("chests"),
                            content.treasureChests, treasureTable,
                            content.minTreasureChestStacks, content.maxTreasureChestStacks, ref slot);
                    }
                    break;

                default:
                    SpawnEnemies(layout, room, free, roomRandom, ref slot);
                    if (!authored)
                    {
                        SpawnItems(content.loot,
                            roomRandom.RangeInclusive(content.minLootPerRoom, content.maxLootPerRoom),
                            free, roomRandom);

                        var chestRandom = roomRandom.Derive("chests");
                        int chestCount = 0;
                        for (int i = 0; i < content.maxChestsPerRoom; i++)
                        {
                            if (chestRandom.Chance(content.ChestChanceFor(room.DepthFromHub)))
                                chestCount++;
                        }
                        SpawnChests(layout, room, free, roomOccupied, chestRandom, chestCount,
                            content.chestLoot, content.minChestStacks, content.maxChestStacks, ref slot);
                    }
                    break;
            }

            // The hub is left bare on purpose. It is the one room the player has to be
            // able to walk into and use, and prop clusters are placed against the walls —
            // exactly where its fixtures stand.
            if (!authored && room.Kind != RoomKind.Hub)
                SpawnProps(layout, room, free, roomOccupied, roomRandom, ref slot);
        }
    }

    private void SpawnEnemies(DungeonLayout layout, Room room, List<Vector2Int> free,
        DeterministicRandom random, ref int slot)
    {
        int count = content.EnemyCountFor(room.DepthFromHub, random);

        for (int i = 0; i < count; i++)
        {
            var choice = content.PickPrefab(content.enemies, room.DepthFromHub, random);
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

    /// <summary>
    /// How far apart the hub's lamps are kept at most. They exist to light the room from
    /// several sides; two lamps standing together light one side twice and leave the rest
    /// dark. Scaled down to the room in <see cref="SpawnHubFixtures"/> — a fixed distance
    /// that does not fit does not spread the lamps out, it loses them.
    /// </summary>
    private const int MaxLampSpacing = 5;

    /// <summary>
    /// Furnishes the hub — the run's only save station and crafting table, lamps enough
    /// to light it from several sides, and empty chests for the player's own storage.
    ///
    /// The station and table exist exactly once per dungeon because the layout guarantees
    /// exactly one <see cref="RoomKind.Hub"/> room, and nothing else spawns them. That is
    /// the whole design: saving and crafting are somewhere the player has to walk back to,
    /// which stops being true the moment there are two of them.
    ///
    /// Enemies are conspicuously absent, and that is the point of the room rather than an
    /// omission — see the switch in <see cref="SpawnRoomContent"/>, which never calls
    /// <see cref="SpawnEnemies"/> for a hub, and <see cref="SpawnCorridorAmbushes"/>,
    /// which keeps its distance from the room's approaches.
    /// </summary>
    private void SpawnHubFixtures(DungeonLayout layout, Room room, List<Vector2Int> free, ref int slot)
    {
        var placed = new List<(Vector2Int cell, int footprint)>();

        // Widest first, lamps last. Everything here competes for the same wall-side cells,
        // and a lamp is one cell that can go almost anywhere, while a crafting table needs
        // room around it or it ends up drawn into the wall. Placing the lamps first and
        // spacing them out measurably crowded the chests into the middle of the room.
        SpawnFixture(layout, room, free, placed, content.saveStationPrefabId, 0, ref slot);
        SpawnFixture(layout, room, free, placed, content.craftingTablePrefabId, 0, ref slot);

        for (int i = 0; i < content.hubChests; i++)
        {
            GameObject chest = SpawnFixture(layout, room, free, placed,
                content.chestPrefabId, 0, ref slot);

            // Empty on purpose: these are the player's storage, not loot. Written through
            // the same path Stage 9 stocks chests by, so a baked hub chest is emptied in
            // the scene file rather than only in memory.
            if (chest != null) EmptyChest(chest);
        }

        int lampSpacing = Mathf.Clamp(
            room.Bounds.width / Mathf.Max(1, content.hubLamps), 2, MaxLampSpacing);

        for (int i = 0; i < content.hubLamps; i++)
            SpawnFixture(layout, room, free, placed, content.lightPrefabId, lampSpacing, ref slot);
    }

    /// <summary>
    /// Places one hub fixture near a wall but never touching one, clear of the fixtures
    /// already standing. Returns the instance, or null when nothing was placed.
    ///
    /// <paramref name="minSpacing"/> overrides the prefab's own footprint when something
    /// needs to be kept further apart than its size demands — the lamps.
    ///
    /// Failing to place a fixture is worth saying out loud: a dungeon whose save station
    /// silently did not spawn cannot be saved in.
    /// </summary>
    private GameObject SpawnFixture(DungeonLayout layout, Room room, List<Vector2Int> free,
        List<(Vector2Int cell, int footprint)> placed, string prefabId, int minSpacing, ref int slot)
    {
        if (string.IsNullOrEmpty(prefabId)) return null;

        GameObject prefab = _prefabs.Resolve(prefabId);
        int footprint = FootprintCells(prefabId, prefab);
        int spacing = Mathf.Max(footprint, minSpacing);
        bool blocking = BlocksPathfinding(prefab);

        if (!TryTakeFixtureCell(layout, free, placed, footprint, spacing, blocking, out Vector2Int cell))
        {
            Debug.LogWarning(
                $"[DungeonPopulator] No room left in the hub for '{prefabId}' — it was not " +
                "spawned. Raise Hub Room Size on the generation settings.", this);
            return null;
        }

        GameObject instance = _prefabs.Spawn(prefabId, builder.CellCenter(cell), _contentRoot,
            SlotGuid(layout.Seed, room.Index, slot++));
        placed.Add((cell, spacing));
        return instance;
    }

    /// <summary>
    /// Takes a cell for a fixture: as close to a wall as its own size allows without
    /// overlapping one, and far enough from every fixture already placed that they cannot
    /// overlap each other.
    ///
    /// The clearance is what stops a fixture being drawn half inside a wall, and it is
    /// not cosmetic pedantry — since Stage 8 the wall tilemap sorts *above* world sprites,
    /// so the overlapping part is not merely close to the wall, it is painted over by it.
    /// A cell is the unit the generator places on, not the size the thing being placed
    /// actually is: an object <c>n</c> cells across needs <c>n / 2</c> cells of walkable
    /// ground around its own before none of it crosses into rock.
    ///
    /// Three passes, giving up one guarantee at a time. The first wants clearance *and*
    /// a wall just past it, which is what "standing against the wall" looks like once the
    /// object's own width is accounted for. The second keeps the clearance and drops the
    /// wall. Only the third drops the clearance, and it exists so that a hub too cramped
    /// to furnish properly still gets a save station rather than none.
    /// </summary>
    private static bool TryTakeFixtureCell(DungeonLayout layout, List<Vector2Int> free,
        List<(Vector2Int cell, int footprint)> placed, int footprint, int spacing, bool blocking,
        out Vector2Int cell)
    {
        int clearance = footprint / 2;

        // Scaled the same way TryTakeAnchor's is, and for the same reason: a chokepoint
        // marks one cell, but a fixture this wide reaches past a flat radius of one.
        int chokepointRadius = Mathf.CeilToInt(footprint / 2f);

        for (int pass = 0; pass < 3; pass++)
        {
            // Searched from the end, which is the order TryTakeCell consumes in, so the
            // choice stays governed by the same shuffle everything else uses.
            for (int i = free.Count - 1; i >= 0; i--)
            {
                Vector2Int candidate = free[i];

                if (pass < 2 && !HasClearance(layout, candidate, clearance)) continue;

                // "Near a wall" means solid ground exactly one ring beyond the clearance:
                // any closer and the fixture would overlap it, any further and it is
                // standing out in the room.
                if (pass == 0 && HasClearance(layout, candidate, clearance + 1)) continue;

                if (blocking && layout.NearAnyChokepoint(candidate, chokepointRadius)) continue;
                if (!IsClearOfPlaced(candidate, spacing, placed)) continue;

                free.RemoveAt(i);
                cell = candidate;
                return true;
            }
        }

        cell = default;
        return false;
    }

    /// <summary>True when every cell within <paramref name="radius"/> is walkable.</summary>
    private static bool HasClearance(DungeonLayout layout, Vector2Int cell, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!layout.IsWalkable(cell.x + dx, cell.y + dy)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is far enough (Chebyshev) from every cell in
    /// <paramref name="placed"/> that their footprints cannot overlap. Shared by the hub's
    /// own furniture placement and the room-wide prop/chest one — the same question either
    /// way: is there already something standing close enough to this cell to collide with
    /// whatever gets put here next.
    /// </summary>
    private static bool IsClearOfPlaced(Vector2Int candidate, int footprint,
        List<(Vector2Int cell, int footprint)> placed)
    {
        foreach ((Vector2Int cell, int other) in placed)
        {
            if (Chebyshev(candidate, cell) < Mathf.Max(footprint, other)) return false;
        }
        return true;
    }

    /// <summary>
    /// Clears a chest's contents through the same path <see cref="StockChest"/> writes
    /// them, so an emptied chest is emptied in the scene file too rather than only in
    /// memory — the hub is baked into the scene like the rest of the dungeon.
    /// </summary>
    private void EmptyChest(GameObject chest)
    {
        var inventory = chest.GetComponent<ChestInventory>();
        if (inventory == null)
        {
            Debug.LogWarning(
                $"[DungeonPopulator] Prefab id '{content.chestPrefabId}' has no ChestInventory " +
                "— the hub's chest may come with whatever its prefab carries.", chest);
            return;
        }

        inventory.SetStartingItems(System.Array.Empty<(ItemData, int)>());
    }

    /// <summary>
    /// Spawns and stocks up to <paramref name="count"/> chests in the room.
    ///
    /// Reuses <see cref="TryTakeAnchor"/> the same way <see cref="SpawnProps"/> does: the
    /// chest prefab sits on <c>ObstaclePathOnly</c>, so it really does block
    /// <see cref="PathfindingGrid"/>, and that path already keeps blocking spawns off
    /// chokepoints and biases them against a wall — which is also where a chest reads
    /// right. Every chest it places is recorded in <paramref name="occupied"/>, which is
    /// the same room-wide list <see cref="SpawnProps"/> reads and adds to afterwards — a
    /// prop cluster placed later in the room has to see chests placed here, or it can
    /// anchor right against one.
    /// </summary>
    private void SpawnChests(DungeonLayout layout, Room room, List<Vector2Int> free,
        List<(Vector2Int cell, int footprint)> occupied, DeterministicRandom random, int count,
        List<RoomContentSettings.ItemChoice> table, int minStacks, int maxStacks, ref int slot)
    {
        if (string.IsNullOrEmpty(content.chestPrefabId) || count <= 0) return;

        GameObject prefab = _prefabs.Resolve(content.chestPrefabId);
        int spacing = FootprintCells(content.chestPrefabId, prefab);
        bool blocking = BlocksPathfinding(prefab);

        for (int i = 0; i < count; i++)
        {
            if (!TryTakeAnchor(layout, free, blocking, spacing, occupied, random, out Vector2Int cell))
                return;

            GameObject chest = _prefabs.Spawn(content.chestPrefabId, builder.CellCenter(cell),
                _contentRoot, SlotGuid(layout.Seed, room.Index, slot++));
            occupied.Add((cell, spacing));
            if (chest == null) continue;

            StockChest(chest, table, minStacks, maxStacks, random.Derive($"chest{slot}"));
        }
    }

    /// <summary>
    /// Rolls a chest's contents and writes them through <see cref="ChestInventory.SetStartingItems"/>.
    ///
    /// Deliberately not routed through <see cref="Drop"/>/<see cref="WorldItemPickup"/> like
    /// floor loot: floor loot is a world drop captured by <see cref="WorldItemsSaveable"/> by
    /// position, while a chest's contents are entity state captured by
    /// <see cref="ChestSaveable"/> by guid. They are two different persistence paths on
    /// purpose, and a chest's contents live on the chest, not as loose items sitting on it.
    /// </summary>
    private void StockChest(GameObject chest, List<RoomContentSettings.ItemChoice> table,
        int minStacks, int maxStacks, DeterministicRandom random)
    {
        var inventory = chest.GetComponent<ChestInventory>();
        if (inventory == null)
        {
            Debug.LogWarning(
                $"[DungeonPopulator] Prefab id '{content.chestPrefabId}' has no ChestInventory " +
                "— nothing to stock.", chest);
            return;
        }

        int stacks = Mathf.Min(random.RangeInclusive(minStacks, maxStacks), inventory.SlotCount);
        var rolled = new List<(ItemData, int)>(stacks);

        for (int i = 0; i < stacks; i++)
        {
            var choice = content.PickItem(table, random);
            if (choice == null) break;

            ItemData item = ItemDatabase.Instance != null ? ItemDatabase.Instance.Resolve(choice.itemId) : null;
            if (item == null)
            {
                Debug.LogWarning(
                    $"[DungeonPopulator] Item id '{choice.itemId}' is not in the ItemDatabase — " +
                    "chest stack skipped. Run Tools ▸ Save System ▸ Rebuild Item Database.", chest);
                continue;
            }

            rolled.Add((item, random.RangeInclusive(choice.minCount, choice.maxCount)));
        }

        inventory.SetStartingItems(rolled);
    }

    /// <summary>
    /// Fills the room's prop budget in clusters rather than one object at a time.
    ///
    /// The budget is unchanged; only its distribution is. Objects in a room were put there
    /// by someone — barrels stand in threes against a wall, crates get stacked in a corner —
    /// and spread evenly over the floor at one per cell they read as scatter laid over the
    /// room instead of as its contents. Anchoring against a wall does a second job for
    /// free: it keeps the middle of the room clear, which is where the player has to fight.
    ///
    /// Spacing and blocking-avoidance both matter here in a way a 1-cell placement grid
    /// hides. <c>Table.prefab</c>'s own collider is about 2.5 cells wide — cells are the
    /// unit the *generator* places things on, not the size any given prop actually is — so
    /// two tables placed on cells one apart, which is what a naive cluster does, overlap
    /// by roughly half a table. And every prop registered today (`prop.barrel`,
    /// `prop.table`) sits on layer `ObstaclePathOnly`, which really does block
    /// `PathfindingGrid` — contrary to this class's own `RoomContentSettings.props`
    /// tooltip claiming props are always safe to scatter. That mismatch is not fixed here;
    /// it is worth someone reconciling the doc comment with the layer convention, but this
    /// pass works with what actually blocks movement today rather than what a comment says
    /// should.
    ///
    /// <paramref name="occupied"/> is the room-wide record every cluster reads and adds
    /// to, not just its own. Without it, spacing was only ever checked within one
    /// cluster's own members — the list was created fresh for each anchor and thrown away
    /// once that cluster finished, so a barrel cluster and a table cluster placed one
    /// after another had no way to know about each other and could land close enough to
    /// overlap. Chests placed earlier in the room are in this same list too, for the same
    /// reason.
    /// </summary>
    private void SpawnProps(DungeonLayout layout, Room room, List<Vector2Int> free,
        List<(Vector2Int cell, int footprint)> occupied, DeterministicRandom random, ref int slot)
    {
        // The room's own cells, not its bounding box: an L-shaped room covers roughly
        // half its box, and scattering by the box would fill it twice as densely.
        float expected = room.Area * content.propsPerHundredFloorCells / 100f;

        int budget = Mathf.FloorToInt(expected);
        if (random.Chance(expected - budget)) budget++;

        while (budget > 0)
        {
            var choice = content.PickPrefab(content.props, room.DepthFromHub, random);
            if (choice == null) return;

            GameObject prefab = _prefabs.Resolve(choice.prefabId);
            int spacing = FootprintCells(choice.prefabId, prefab);
            bool blocking = BlocksPathfinding(prefab);

            if (!TryTakeAnchor(layout, free, blocking, spacing, occupied, random, out Vector2Int anchor))
                return;

            _prefabs.Spawn(choice.prefabId, builder.CellCenter(anchor), _contentRoot,
                SlotGuid(layout.Seed, room.Index, slot++));
            budget--;
            occupied.Add((anchor, spacing));

            // The rest of the cluster is the same prop, spaced by its own footprint so
            // members never overlap — a heap of one thing standing apart, not stacked.
            int cluster = random.RangeInclusive(
                content.propsPerClusterMin, content.propsPerClusterMax) - 1;

            var placed = new List<Vector2Int>(cluster + 1) { anchor };

            for (int i = 0; i < cluster && budget > 0; i++)
            {
                if (!TryTakeSpaced(layout, free, anchor, placed, spacing, blocking, occupied, out Vector2Int cell))
                    break;

                _prefabs.Spawn(choice.prefabId, builder.CellCenter(cell), _contentRoot,
                    SlotGuid(layout.Seed, room.Index, slot++));
                placed.Add(cell);
                occupied.Add((cell, spacing));
                budget--;
            }
        }
    }

    private readonly Dictionary<string, int> _footprintCache = new Dictionary<string, int>();

    /// <summary>
    /// How many cells a prefab actually occupies: the wider of what it collides with and
    /// what it draws, in world space via the transform's lossy scale, rounded up.
    ///
    /// Both halves are read from serialized data rather than from
    /// <c>Renderer.bounds</c>/<c>Collider2D.bounds</c>, because those are computed from an
    /// object's live placement in a scene and are unreliable — often zero — on a prefab
    /// *asset* that has never been instantiated, which is exactly what
    /// <see cref="PrefabRegistry.Resolve"/> hands back here. `BoxCollider2D.size`,
    /// `Sprite.bounds` and `Transform.lossyScale` all read correctly either way.
    ///
    /// The drawn size counts as much as the collider, and for placement against a wall it
    /// counts for more. Since Stage 8 the wall tilemap draws at sorting order 7, above
    /// ordinary world sprites, so any part of an object overlapping a wall cell is painted
    /// over by the wall — a table whose sprite crosses the boundary does not look like a
    /// table against a wall, it looks like half a table embedded in one. A prefab whose
    /// collider is smaller than its art would be placed flush and lose the difference.
    ///
    /// Falls back to 1 for anything with neither: better to under-space an oddly-shaped
    /// prop than to fail placement for a component this does not understand. Cached per
    /// id — this runs once per cluster, not per cell.
    /// </summary>
    private int FootprintCells(string prefabId, GameObject prefab)
    {
        if (_footprintCache.TryGetValue(prefabId, out int cached)) return cached;

        Vector2 size = Vector2.zero;

        BoxCollider2D box = prefab != null ? prefab.GetComponentInChildren<BoxCollider2D>(true) : null;
        if (box != null) size = Vector2.Max(size, Abs(Vector2.Scale(box.size, box.transform.lossyScale)));

        SpriteRenderer sprite = prefab != null
            ? prefab.GetComponentInChildren<SpriteRenderer>(true)
            : null;
        if (sprite != null) size = Vector2.Max(size, Abs(DrawnSize(sprite)));

        int cells = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(size.x, size.y)));
        _footprintCache[prefabId] = cells;
        return cells;
    }

    /// <summary>
    /// A sprite renderer's drawn size in world units. Sliced and tiled renderers draw at
    /// their own <see cref="SpriteRenderer.size"/> rather than at the sprite's, and this
    /// project uses that mode — reading the sprite alone would mis-measure them.
    /// </summary>
    private static Vector2 DrawnSize(SpriteRenderer renderer)
    {
        Vector2 local = renderer.drawMode == SpriteDrawMode.Simple
            ? (renderer.sprite != null ? (Vector2)renderer.sprite.bounds.size : Vector2.zero)
            : renderer.size;

        return Vector2.Scale(local, renderer.transform.lossyScale);
    }

    private static Vector2 Abs(Vector2 value) => new Vector2(Mathf.Abs(value.x), Mathf.Abs(value.y));

    /// <summary>Known layers on which a collider physically blocks movement (see ENEMY_NOTES §GU-0036).</summary>
    private static readonly string[] BlockingLayerNames =
        { "ObstacleStatic", "ObstacleDynamic", "ObstaclePathOnly" };

    /// <summary>
    /// True when any collider on the prefab sits on a layer <see cref="PathfindingGrid"/>
    /// actually treats as an obstacle.
    ///
    /// Not filtered by <c>isTrigger</c>, on purpose: `PathfindingGrid.BuildGrid` marks a
    /// node unwalkable with a masked `Physics2D.OverlapCircle`, and this project's
    /// `Physics2DSettings.queriesHitTriggers` is `1` — confirmed in
    /// `ProjectSettings/Physics2DSettings.asset` — so a trigger collider on an obstacle
    /// layer blocks the grid exactly like a solid one does. `Table.prefab` is the concrete
    /// case: its `ObstaclePathOnly` collider (the one that actually determines its
    /// footprint) is a trigger, and would have been silently treated as non-blocking here
    /// if this excluded triggers, defeating the whole point of the check.
    /// </summary>
    private static bool BlocksPathfinding(GameObject prefab)
    {
        if (prefab == null) return false;

        foreach (Collider2D collider in prefab.GetComponentsInChildren<Collider2D>(true))
        {
            string layerName = LayerMask.LayerToName(collider.gameObject.layer);
            foreach (string blocking in BlockingLayerNames)
            {
                if (layerName == blocking) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes a cell to start a cluster on, preferring one that touches something solid.
    ///
    /// Falls back to any free cell rather than giving up, because a small room can easily
    /// have had all its wall-side cells taken by enemies and loot already, and a room with
    /// no props at all is a worse outcome than a cluster standing in the open.
    ///
    /// A prop that blocks movement additionally skips <see cref="DungeonLayout.Chokepoints"/>
    /// and their immediate neighbours: those cells are, by definition, the only route
    /// through somewhere, and an anchor placed right there — worse, with a cluster fanned
    /// out from it — can wall off a narrow room arm that the generator itself guaranteed
    /// was connected.
    ///
    /// "Immediate neighbours" is <paramref name="footprint"/>-scaled, not a flat radius of
    /// one. A chokepoint marks a single cell; the object anchored on a *nearby* free cell
    /// can still physically reach it once its real size is accounted for — `Table.prefab`
    /// is close to three cells wide, so a radius of one let a table's own footprint reach
    /// past the buffer and cover a chokepoint the check was meant to keep clear. Half the
    /// footprint, rounded up, is the same clearance <see cref="SpawnFixture"/> uses for the
    /// hub's own furniture, for the same reason.
    ///
    /// Also checked here, against <paramref name="occupied"/>: every blocking object
    /// already standing in the room, from any earlier cluster or chest — not just this
    /// prop's own. An anchor is the start of a new cluster, so it is exactly the placement
    /// most likely to land next to something an earlier pass already put down.
    /// </summary>
    private bool TryTakeAnchor(DungeonLayout layout, List<Vector2Int> free, bool blocking,
        int footprint, List<(Vector2Int cell, int footprint)> occupied, DeterministicRandom random,
        out Vector2Int anchor)
    {
        int chokepointRadius = Mathf.CeilToInt(footprint / 2f);

        if (random.Chance(content.propWallBias))
        {
            // Searched from the end, which is the order TryTakeCell consumes in, so the
            // choice stays governed by the same shuffle everything else uses.
            for (int i = free.Count - 1; i >= 0; i--)
            {
                if (!layout.TouchesSolid(free[i])) continue;
                if (blocking && layout.NearAnyChokepoint(free[i], chokepointRadius)) continue;
                if (!IsClearOfPlaced(free[i], footprint, occupied)) continue;

                anchor = free[i];
                free.RemoveAt(i);
                return true;
            }
        }

        if (!blocking)
        {
            for (int i = free.Count - 1; i >= 0; i--)
            {
                if (!IsClearOfPlaced(free[i], footprint, occupied)) continue;

                anchor = free[i];
                free.RemoveAt(i);
                return true;
            }

            anchor = default;
            return false;
        }

        for (int i = free.Count - 1; i >= 0; i--)
        {
            if (layout.NearAnyChokepoint(free[i], chokepointRadius)) continue;
            if (!IsClearOfPlaced(free[i], footprint, occupied)) continue;

            anchor = free[i];
            free.RemoveAt(i);
            return true;
        }

        anchor = default;
        return false;
    }

    /// <summary>
    /// Takes a still-free cell at least <paramref name="spacing"/> cells (Chebyshev) from
    /// every cell already placed in this cluster, within a bounded radius of the anchor so
    /// the result still reads as one group rather than the whole room being redistributed.
    ///
    /// The pairwise check against every placed member — not just the anchor — is what
    /// keeps a three- or four-strong cluster of a wide prop from overlapping itself: two
    /// members can each be far enough from the anchor and still be right on top of each
    /// other if only the anchor distance is checked.
    ///
    /// The chokepoint check is scaled by <paramref name="spacing"/> the same way
    /// <see cref="TryTakeAnchor"/>'s is, and for the same reason: <paramref name="spacing"/>
    /// already *is* this prop's footprint here, so a fixed radius of one was never
    /// consistent with the value sitting right next to it in the parameter list.
    ///
    /// <paramref name="occupied"/> is checked in addition to <paramref name="placed"/>: the
    /// latter is this cluster's own members, the former is everything else already
    /// standing in the room. A cluster member can be correctly spaced from its own anchor
    /// and siblings and still land against a chest or an earlier cluster's prop if nothing
    /// checks the room-wide list too.
    /// </summary>
    private static bool TryTakeSpaced(DungeonLayout layout, List<Vector2Int> free, Vector2Int anchor,
        List<Vector2Int> placed, int spacing, bool blocking,
        List<(Vector2Int cell, int footprint)> occupied, out Vector2Int cell)
    {
        int radius = spacing * 2;
        int chokepointRadius = Mathf.CeilToInt(spacing / 2f);

        for (int i = free.Count - 1; i >= 0; i--)
        {
            Vector2Int candidate = free[i];
            if (Chebyshev(candidate, anchor) > radius) continue;
            if (blocking && layout.NearAnyChokepoint(candidate, chokepointRadius)) continue;
            if (!IsClearOfPlaced(candidate, spacing, occupied)) continue;

            bool tooClose = false;
            foreach (Vector2Int other in placed)
            {
                if (Chebyshev(candidate, other) >= spacing) continue;
                tooClose = true;
                break;
            }
            if (tooClose) continue;

            free.RemoveAt(i);
            cell = candidate;
            return true;
        }

        cell = default;
        return false;
    }

    private static int Chebyshev(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));


    // ---------------------------------------------------------------- corridors

    /// <summary>
    /// Puts enemies in the two places the layout generator already went to the trouble of
    /// finding, and which nothing had ever read.
    ///
    /// <b>Alcoves</b> are blind pockets carved off the side of a corridor — the mouth is
    /// behind the player by the time the interior enters their view cone, so an alcove is
    /// somewhere they walk straight past without having been able to look in.
    ///
    /// <b>Chokepoints</b> are cells whose removal would split the dungeon: the places a
    /// fight cannot be walked away from. The enemy goes *beside* one, never on it. On it,
    /// the only route is blocked and the player has no choice to make; beside it, they
    /// have to decide whether getting through is worth being seen.
    ///
    /// Both are capped hard by <see cref="RoomContentSettings.maxCorridorEnemies"/>, and
    /// both skip anything near the hub: the first thing a run does must not be an ambush
    /// in a corridor the player has no room to back out of. Enforced twice, for two
    /// different reasons — <see cref="HubKeepOut"/> is a fixed radius around the hub's
    /// walls, while <see cref="TrySpawnAmbush"/>'s depth check catches a long corridor
    /// whose *nearest room* is still the hub well beyond that radius.
    /// </summary>
    private void SpawnCorridorAmbushes(DungeonLayout layout, DeterministicRandom random)
    {
        if (content.maxCorridorEnemies <= 0) return;
        if (content.enemies == null || content.enemies.Count == 0) return;

        Room hub = FindHub(layout);
        var taken = new HashSet<Vector2Int>();
        int placed = 0;
        int slot = 0;

        // Alcoves first: they are the better ambush and there are far fewer of them, so
        // spending the budget here before the chokepoints is the right way round.
        var alcoveRandom = random.Derive("alcoves");
        foreach (Vector2Int cell in layout.Alcoves)
        {
            if (placed >= content.maxCorridorEnemies) break;
            if (!alcoveRandom.Chance(content.alcoveAmbushChance)) continue;
            if (!IsUsableAmbushCell(layout, cell, taken, hub)) continue;

            if (TrySpawnAmbush(layout, cell, alcoveRandom, "alcove", slot++))
            {
                taken.Add(cell);
                placed++;
            }
        }

        var chokeRandom = random.Derive("chokepoints");
        foreach (Vector2Int choke in layout.Chokepoints)
        {
            if (placed >= content.maxCorridorEnemies) break;
            if (!chokeRandom.Chance(content.chokepointGuardChance)) continue;

            if (!TryFindGuardPost(layout, choke, taken, hub, chokeRandom, out Vector2Int post)) continue;

            if (TrySpawnAmbush(layout, post, chokeRandom, "guard", slot++))
            {
                taken.Add(post);
                placed++;
            }
        }
    }

    /// <summary>
    /// A cell next to the chokepoint that is not itself one. Standing on a chokepoint
    /// walls the route off; standing next to it leaves the route open and watched.
    /// </summary>
    private static bool TryFindGuardPost(DungeonLayout layout, Vector2Int choke,
        HashSet<Vector2Int> taken, Room hub, DeterministicRandom random, out Vector2Int post)
    {
        var chokeSet = new HashSet<Vector2Int>(layout.Chokepoints);

        var options = new List<Vector2Int>(4)
        {
            choke + Vector2Int.right, choke + Vector2Int.left,
            choke + Vector2Int.up, choke + Vector2Int.down
        };
        random.Shuffle(options);

        foreach (Vector2Int option in options)
        {
            if (chokeSet.Contains(option)) continue;
            if (!IsUsableAmbushCell(layout, option, taken, hub)) continue;

            post = option;
            return true;
        }

        post = default;
        return false;
    }

    /// <summary>
    /// Cells kept free of ambushes around the hub, measured out from its walls.
    ///
    /// The room pass already never puts an enemy inside the hub — its case in
    /// <see cref="SpawnRoomContent"/> spawns fixtures and nothing else — and an ambush
    /// cell is required to be outside every room anyway. What was left was the corridor
    /// immediately outside: an enemy posted one cell from the doorway is, from inside the
    /// room, something waiting in the hub. The keep-out makes "no enemies at the hub" mean
    /// what a player would take it to mean rather than what the cell test happens to say.
    /// </summary>
    private const int HubKeepOut = 4;

    /// <summary>
    /// True when the cell is open corridor nobody has claimed, and not on the hub's
    /// doorstep. Cells inside rooms are excluded because the room pass has its own budget
    /// and its own guarantees — the start room and the hub being empty among them.
    /// </summary>
    private static bool IsUsableAmbushCell(DungeonLayout layout, Vector2Int cell,
        HashSet<Vector2Int> taken, Room hub)
    {
        if (!layout.IsWalkable(cell)) return false;
        if (taken.Contains(cell)) return false;
        if (layout[cell] == CellType.Door) return false; // never stand in a doorway
        if (hub != null && IsWithin(hub.Bounds, cell, HubKeepOut)) return false;
        return layout.RoomAt(cell) == null;
    }

    /// <summary>True when the cell is inside the bounds grown by <paramref name="margin"/>.</summary>
    private static bool IsWithin(RectInt bounds, Vector2Int cell, int margin)
    {
        return cell.x >= bounds.xMin - margin && cell.x < bounds.xMax + margin &&
               cell.y >= bounds.yMin - margin && cell.y < bounds.yMax + margin;
    }

    private static Room FindHub(DungeonLayout layout)
    {
        foreach (Room room in layout.Rooms)
        {
            if (room.Kind == RoomKind.Hub) return room;
        }
        return null;
    }

    /// <summary>
    /// Spawns one corridor enemy, gated by the depth of the room it is nearest to.
    ///
    /// Corridors have no depth of their own — depth is a property of the room graph — so
    /// the nearest room's is borrowed. Without it every corridor would be treated as depth
    /// zero and the <see cref="RoomContentSettings.PrefabChoice.minDepth"/> gating would
    /// put the late-game enemies in the first hallway of the run.
    /// </summary>
    private bool TrySpawnAmbush(DungeonLayout layout, Vector2Int cell, DeterministicRandom random,
        string kind, int slot)
    {
        int depth = NearestRoomDepth(layout, cell);
        if (depth <= 0) return false; // nearest room is the hub: not a fair opening move

        var choice = content.PickPrefab(content.enemies, depth, random);
        if (choice == null) return false;

        GameObject enemy = _prefabs.Spawn(choice.prefabId, builder.CellCenter(cell), _contentRoot,
            CellGuid(layout.Seed, kind, cell));
        if (enemy == null) return false;

        var behaviour = enemy.GetComponent<EnemyBase>();
        if (behaviour != null) behaviour.SetPlayer(_player);
        return true;
    }

    /// <summary>Depth of the room whose centre is closest, or 0 when there are no rooms.</summary>
    private static int NearestRoomDepth(DungeonLayout layout, Vector2Int cell)
    {
        int best = 0;
        int bestDistance = int.MaxValue;

        foreach (var room in layout.Rooms)
        {
            int distance = Mathf.Abs(room.Center.x - cell.x) + Mathf.Abs(room.Center.y - cell.y);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = room.DepthFromHub;
        }

        return best;
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
            // The hub floor is spoken for by fixtures, and dropping loot there would be
            // the same "something spawned where it shouldn't" defect this pass exists to
            // avoid for enemies.
            if (room.Kind != RoomKind.Hub) candidates.Add(room);
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
