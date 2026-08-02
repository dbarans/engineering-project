# Procedural Dungeon Generation — Plan & Notes

Living document for the procedural dungeon generation feature (GU-0051).
Written in English to match the rest of the project's documentation (see `ENEMY_NOTES.md`).

## Status

| Stage | State | Commit |
|-------|-------|--------|
| 0 — Foundation | done (scene wiring is a manual editor step) | `feat: [GU-0051] dungeon tilemap foundation` |
| 1 — Layout generator + tests | done | `feat: [GU-0051] deterministic room-and-corridor layout generator` |
| 2 — Painter | done | `feat: [GU-0051] paint generated layouts into tilemaps` |
| 3 — Population | done | `feat: [GU-0051] populate generated dungeons with content` |
| 4 — Save integration | done | `feat: [GU-0051] seed-based save integration` |
| 5 — Polish | metrics + room templates done | `feat: [GU-0051] generation metrics and room templates` |

---

## 0. Design decisions (fixed — do not re-litigate mid-implementation)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **Tilemap** for floors and walls, **prefabs** for everything interactive (doors, barrels, chests, enemies, loot) | `TilemapCollider2D` + `CompositeCollider2D` merges wall colliders into one shape; both `PathfindingGrid` (`Physics2D.OverlapCircle` + `obstacleMask`) and the vision system (`Physics2D.Raycast`/`Linecast` + `obstacleMask`) are layer-based, so they need **zero changes** to see generated geometry |
| D2 | **Rooms + corridors on a graph**, loops allowed (not a pure tree) | Loops give escape routes, which the existing stealth/noise mechanics depend on. Also the easiest family of algorithms to describe and justify in the thesis |
| D3 | Layout generation is **pure C#** — no `MonoBehaviour`, no scene access, only `Vector2Int`/`RectInt` from `UnityEngine` | Makes it unit-testable in EditMode tests, which is a strong point for the thesis; also makes determinism verifiable |
| D4 | **Own deterministic RNG** (FNV-1a string hash + xorshift), never `UnityEngine.Random` | `UnityEngine.Random` is global mutable state; `string.GetHashCode()` is not guaranteed stable across runtimes. Both would break seed reproducibility, which the save system depends on |
| D5 | **One dungeon per scene** for the MVP; multiple floors deferred to Stage 5 | `SceneSaveData` is keyed by scene name, so one-scene-one-dungeon needs no save-model change |
| D6 | Save stores **seed + diffs**, never the tile map | `SceneSaveData.generationSeed` already exists for exactly this. Load = regenerate from seed, then overlay saved entity states |
| D7 | Deterministic entity guids: `{seed}:{roomIndex}:{slotIndex}` | `SaveableEntity.SetGuid` exists and is documented as being there "for a future procedural generator". Deterministic guids mean a regenerated dungeon's entities match the saved ones without a spawn-order dependency |

**Target namespace / folder:** `Assets/Scripts/Generation/`
**Tests:** `Assets/Tests/EditMode/Generation/`

---

## Stage 0 — Foundation (no generation yet)

Prepares the ground so later stages are pure additions. Nothing visible changes in game.

**Work**
1. Create a `Dungeon` scene (copy of a working scene) with an empty `DungeonRoot` object holding two child Tilemaps: `Floor` and `Walls`. `Walls` gets `TilemapCollider2D` + `Rigidbody2D` (Static) + `CompositeCollider2D`, placed on the existing obstacle layer.
2. Author minimal tile assets — one floor tile, one wall tile. Placeholder art is fine; Rule Tiles come later.
3. `Generation/PrefabRegistry.cs` — a `ScriptableObject` mapping stable string ids to prefabs (`enemy.skullguy`, `enemy.blindlistener`, `prop.barrel`, `world.door`, …). This is the missing piece flagged in `SaveManager.RestoreEntities` as "plan §3b".
4. `PathfindingGrid` — add a public `Configure(Vector2 origin, int width, int height)` that sets the bounds and rebuilds. Currently `width`/`height`/`origin` are serialized-only, so a generated dungeon of arbitrary size cannot resize the grid.

**Done when:** the `Dungeon` scene runs exactly like the current static one, tiles can be painted by hand into the Tilemap, and a hand-painted wall both blocks pathfinding and casts a vision shadow.

**Why first:** this proves D1 end-to-end (Tilemap geometry is respected by A* *and* by FOV) before a single line of generator code exists. If this fails, the whole plan changes.

---

## Stage 1 — Layout generator (pure C#, fully testable)

The algorithmic core. Produces an abstract layout; still paints nothing.

**Files**
- `Generation/CellType.cs` — `enum { Wall, Floor, Door }`
- `Generation/Room.cs` — `RectInt bounds`, `int index`, `RoomKind kind` (`Start`, `Normal`, `Camp`, `Treasure`)
- `Generation/DungeonLayout.cs` — `CellType[,] cells`, `List<Room> rooms`, corridor cells, `Vector2Int spawnCell`, room adjacency graph
- `Generation/DungeonGenerationSettings.cs` — `ScriptableObject`: map size, room count range, room size range, corridor width, extra-loop chance, spacing
- `Generation/DeterministicRandom.cs` — FNV-1a seed hashing + xorshift128; `Next(int, int)`, `NextFloat()`, `Pick<T>(IList<T>)`
- `Generation/IDungeonLayoutGenerator.cs` — `DungeonLayout Generate(string seed, DungeonGenerationSettings settings)`
- `Generation/RoomCorridorGenerator.cs` — the implementation

**Algorithm**
1. Place N non-overlapping rooms by rejection sampling within the map bounds, with a minimum spacing.
2. Build a Delaunay-ish neighbour graph (nearest-neighbour pairs are sufficient at this scale), take a minimum spanning tree to guarantee connectivity.
3. Add back a fraction of the discarded edges (`extraLoopChance`) to create loops — D2.
4. Carve L-shaped corridors for every kept edge.
5. Mark `Door` cells where a corridor meets a room wall.
6. Tag rooms: the room farthest from the map centre becomes `Start`; a mid-distance dead-end room becomes `Camp`; the room farthest from `Start` becomes `Treasure`.
7. Validate: every floor cell reachable from `spawnCell` via flood fill. On failure, retry with a derived seed, up to N attempts, then log and return the best attempt.

**Tests** (`Assets/Tests/EditMode/Generation/RoomCorridorGeneratorTests.cs`)
- Same seed → identical layout (compare a hash of the cell array)
- Different seeds → different layouts
- All floor cells reachable from spawn (flood fill)
- Rooms never overlap and never touch the map border
- Room count within the configured range
- 500 random seeds produce zero validation failures

**Debug tooling:** an editor menu item that generates a layout and dumps it as ASCII to the console — lets you eyeball 20 layouts in seconds without any rendering.

**Done when:** the tests pass and the ASCII dumps look like plausible dungeons.

**Thesis value:** this is the chapter. Deterministic, testable, measurable (room count, corridor length, loop ratio, generation time).

---

## Stage 2 — Painter (layout → world geometry)

**Files**
- `Generation/DungeonPainter.cs` — writes `DungeonLayout` into the Floor/Walls Tilemaps
- `Generation/DungeonBuilder.cs` — `MonoBehaviour` orchestrator: holds settings + registry refs, exposes `Build(string seed)`

**Work**
1. `DungeonPainter.Paint(layout)` — clear both tilemaps, `SetTiles` in bulk (not per-cell; bulk is an order of magnitude faster), then `CompositeCollider2D.GenerateGeometry()`.
2. `DungeonBuilder.Build(seed)` — generate layout → paint → `PathfindingGrid.Configure(...)` → move the player to `spawnCell`.
3. Editor button on `DungeonBuilder` ("Generate with seed" / "Generate random") so you can iterate without entering play mode.

**Done when:** pressing Generate produces a walkable dungeon, an enemy dropped into it paths correctly through corridors, and the player's FOV is properly blocked by generated walls.

**Watch out:** `CompositeCollider2D.GenerateGeometry()` must finish **before** `PathfindingGrid.Configure()` — the grid samples physics, and stale geometry silently produces a grid that disagrees with the visible walls. This ordering is the single most likely source of "the enemy walks through walls" bugs.

---

## Stage 3 — Population (layout → content)

Turns a walkable maze into a playable level. Expect the most iteration here.

**Files**
- `Generation/DungeonPopulator.cs`
- `Generation/RoomContentSettings.cs` — per-`RoomKind` spawn tables

**Work**
1. Compute each room's graph distance from `Start` — this is the difficulty axis.
2. Doors: instantiate the `Door_System` prefab on every `Door` cell.
3. Enemies: per room, count and type drawn from a distance-weighted table. `Start` and `Camp` rooms always empty.
4. Loot: `WorldItemPickup` on floor cells away from doorways; guarantee a minimum of light fuel per dungeon so the run is never unwinnable.
5. Props: barrels/tables scattered as cover — must not block corridors (respect the vision-blocking convention: props never block vision or pathfinding).
6. `Camp` room: `SaveStation` + a `StationaryLightSource`.
7. Every spawned object with a `SaveableEntity` gets `SetGuid($"{seed}:{roomIndex}:{slotIndex}")` and its registry `prefabId` recorded — D7.

**Done when:** a generated dungeon can be played start to finish: fight or sneak past enemies, find fuel, reach the camp, save.

**Watch out:** an object spawned on top of a wall cell, or two objects on the same cell. Keep a per-room occupancy set and assert on collisions in the editor.

---

## Stage 4 — Save integration

**Work**
1. `DungeonBuilder` subscribes to the existing `SaveManager.BuildWorld` event — already documented in `SaveManager` as "the future procedural generator's entry point". Read `SceneSaveData.generationSeed` and build from it.
2. New game: generate a fresh seed, store it so the first save writes it out. This needs a small "current run" holder — `GameManager` is the natural place.
3. Implement the deferred prefab-registry respawn path in `SaveManager.RestoreEntities`: today a saved guid with no live entity logs a warning and skips. With deterministic guids (D7) regeneration recreates every entity, so this path should rarely fire — but implement it anyway for entities the generator spawns conditionally.
4. `EnemySaveable` / `WorldItemsSaveable` need no changes if D7 holds. Verify rather than assume.

**Ordering inside `BuildWorld`:** generate → paint → collider geometry → grid → populate. Only then does `RestoreRoutine` proceed to entity restore. Getting this wrong means restored enemies land in a dungeon that no longer matches.

**Tests**
- Manual: save mid-dungeon → quit → load → identical dungeon, dead enemies still dead, picked-up loot still gone, opened doors still open.
- EditMode: same seed generates byte-identical layout across two separate generator instances.

**Done when:** the full save/load round trip is indistinguishable from the static-scene behaviour.

---

## Stage 5 — Tuning and quality (optional, do as time allows)

Ordered by value-to-effort:

1. **Rule Tiles** for walls — automatic corner/edge sprite selection. Purely visual, big perceived-quality jump.
2. **Metrics + debug overlay** — generation time, room count, corridor length, loop ratio, dead-end count. Directly usable as thesis measurements.
3. **Room templates** — hand-authored prefab room interiors dropped into generated room rectangles (this is how Darkwood mixes authored and generated content). The best answer to "procedural levels feel samey".
4. **Multiple floors** — `DungeonBuilder` takes a floor index, seed becomes `{runSeed}:{floor}`, stairs prefab in the `Treasure` room. Requires either one scene per floor or extending the save key beyond scene name.
5. **Biomes** — per-region tile sets and spawn tables.

---

## Risk register

| Risk | Severity | Mitigation |
|------|----------|-----------|
| Composite collider regenerated after the pathfinding grid → invisible walls for A* | High | Explicit ordering in `DungeonBuilder`, asserted in Stage 2 |
| `string.GetHashCode()` instability breaks seed reproducibility | High | Own FNV-1a hash (D4), covered by a determinism test |
| Generated layouts are technically valid but boring | Medium | Stage 5.3 room templates; metrics overlay to spot degenerate layouts early |
| Spawn placement collides with geometry | Medium | Per-room occupancy set, editor-time assertions |
| Guid collisions between generated and scene-authored entities | Low | Generated guids are namespaced by seed prefix, which no editor-assigned guid can produce |

---

## Order of work

Stages are strictly sequential — each one ends at a state you can run and demo:

```
Stage 0  foundation      → hand-painted tiles work with A* and FOV
Stage 1  layout + tests  → ASCII dungeons in the console, tests green
Stage 2  painter         → walkable generated dungeon on screen
Stage 3  population      → playable run
Stage 4  save            → complete feature
Stage 5  polish          → as time allows
```
