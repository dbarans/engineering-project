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

                    if (wallEast && wallWest)
                    {
                        doorInstance.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                        Vector3 pos = doorInstance.transform.position;
                        pos.x += 1f; 
                        doorInstance.transform.position = pos;
                    }
                    else
                    {
                        doorInstance.transform.rotation = Quaternion.identity;
                    }
                }
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
    /// </summary>
    private void SpawnProps(DungeonLayout layout, Room room, List<Vector2Int> free,
        DeterministicRandom random, ref int slot)
    {
        // The room's own cells, not its bounding box: an L-shaped room covers roughly
        // half its box, and scattering by the box would fill it twice as densely.
        float expected = room.Area * content.propsPerHundredFloorCells / 100f;

        int budget = Mathf.FloorToInt(expected);
        if (random.Chance(expected - budget)) budget++;

        while (budget > 0)
        {
            var choice = content.PickPrefab(content.props, room.DepthFromStart, random);
            if (choice == null) return;

            GameObject prefab = _prefabs.Resolve(choice.prefabId);
            int spacing = FootprintCells(choice.prefabId, prefab);
            bool blocking = BlocksPathfinding(prefab);

            if (!TryTakeAnchor(layout, free, blocking, random, out Vector2Int anchor)) return;

            _prefabs.Spawn(choice.prefabId, builder.CellCenter(anchor), _contentRoot,
                SlotGuid(layout.Seed, room.Index, slot++));
            budget--;

            // The rest of the cluster is the same prop, spaced by its own footprint so
            // members never overlap — a heap of one thing standing apart, not stacked.
            int cluster = random.RangeInclusive(
                content.propsPerClusterMin, content.propsPerClusterMax) - 1;

            var placed = new List<Vector2Int>(cluster + 1) { anchor };

            for (int i = 0; i < cluster && budget > 0; i++)
            {
                if (!TryTakeSpaced(layout, free, anchor, placed, spacing, blocking, out Vector2Int cell))
                    break;

                _prefabs.Spawn(choice.prefabId, builder.CellCenter(cell), _contentRoot,
                    SlotGuid(layout.Seed, room.Index, slot++));
                placed.Add(cell);
                budget--;
            }
        }
    }

    private readonly Dictionary<string, int> _footprintCache = new Dictionary<string, int>();

    /// <summary>
    /// A prefab's footprint in cells: its <see cref="BoxCollider2D"/> size (world-space,
    /// via the transform's lossy scale), rounded up to the wider side.
    ///
    /// Read from the collider's own serialized size rather than from
    /// <c>Renderer.bounds</c>/<c>Collider2D.bounds</c>, because those are computed from an
    /// object's live placement in a scene and are unreliable — often zero — on a prefab
    /// *asset* that has never been instantiated, which is exactly what
    /// <see cref="PrefabRegistry.Resolve"/> hands back here. `BoxCollider2D.size` and
    /// `Transform.lossyScale` are plain serialized data and read correctly either way.
    ///
    /// Falls back to 1 for anything without a <see cref="BoxCollider2D"/>: better to
    /// under-space an oddly-shaped prop than to fail placement for a collider type this
    /// does not understand. Cached per id — this runs once per cluster, not per cell.
    /// </summary>
    private int FootprintCells(string prefabId, GameObject prefab)
    {
        if (_footprintCache.TryGetValue(prefabId, out int cached)) return cached;

        int cells = 1;
        BoxCollider2D box = prefab != null ? prefab.GetComponentInChildren<BoxCollider2D>(true) : null;
        if (box != null)
        {
            Vector2 worldSize = Vector2.Scale(box.size, box.transform.lossyScale);
            cells = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(worldSize.x, worldSize.y)));
        }

        _footprintCache[prefabId] = cells;
        return cells;
    }

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
    /// </summary>
    private bool TryTakeAnchor(DungeonLayout layout, List<Vector2Int> free, bool blocking,
        DeterministicRandom random, out Vector2Int anchor)
    {
        if (random.Chance(content.propWallBias))
        {
            // Searched from the end, which is the order TryTakeCell consumes in, so the
            // choice stays governed by the same shuffle everything else uses.
            for (int i = free.Count - 1; i >= 0; i--)
            {
                if (!layout.TouchesSolid(free[i])) continue;
                if (blocking && layout.NearAnyChokepoint(free[i], 1)) continue;

                anchor = free[i];
                free.RemoveAt(i);
                return true;
            }
        }

        if (!blocking) return TryTakeCell(free, out anchor);

        for (int i = free.Count - 1; i >= 0; i--)
        {
            if (layout.NearAnyChokepoint(free[i], 1)) continue;

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
    /// </summary>
    private static bool TryTakeSpaced(DungeonLayout layout, List<Vector2Int> free, Vector2Int anchor,
        List<Vector2Int> placed, int spacing, bool blocking, out Vector2Int cell)
    {
        int radius = spacing * 2;

        for (int i = free.Count - 1; i >= 0; i--)
        {
            Vector2Int candidate = free[i];
            if (Chebyshev(candidate, anchor) > radius) continue;
            if (blocking && layout.NearAnyChokepoint(candidate, 1)) continue;

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
    /// both skip anything near the start room: the first thing a run does must not be an
    /// ambush in a corridor the player has no room to back out of.
    /// </summary>
    private void SpawnCorridorAmbushes(DungeonLayout layout, DeterministicRandom random)
    {
        if (content.maxCorridorEnemies <= 0) return;
        if (content.enemies == null || content.enemies.Count == 0) return;

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
            if (!IsUsableAmbushCell(layout, cell, taken)) continue;

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

            if (!TryFindGuardPost(layout, choke, taken, chokeRandom, out Vector2Int post)) continue;

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
        HashSet<Vector2Int> taken, DeterministicRandom random, out Vector2Int post)
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
            if (!IsUsableAmbushCell(layout, option, taken)) continue;

            post = option;
            return true;
        }

        post = default;
        return false;
    }

    /// <summary>
    /// True when the cell is open corridor nobody has claimed. Cells inside rooms are
    /// excluded because the room pass has its own budget and its own guarantees — the
    /// start room being empty among them.
    /// </summary>
    private static bool IsUsableAmbushCell(DungeonLayout layout, Vector2Int cell,
        HashSet<Vector2Int> taken)
    {
        if (!layout.IsWalkable(cell)) return false;
        if (taken.Contains(cell)) return false;
        if (layout[cell] == CellType.Door) return false; // never stand in a doorway
        return layout.RoomAt(cell) == null;
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
        if (depth <= 0) return false; // right beside the start room: not a fair opening move

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
            best = room.DepthFromStart;
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
