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
| 5 — Polish | metrics + room templates done; Rule Tiles / floors / biomes deferred, see below | `feat: [GU-0051] generation metrics and room templates` |
| 6 — Spatial design | done (layout side); needs an editor pass | see §6 below |

**Verification so far is compile-level plus logic-level, not in-editor.** The layout
assembly is engine-free by design, so it was run outside Unity against 500 seeds
(determinism, connectivity, spacing, room roles, loop behaviour, metrics — all passing),
and the whole project compiles clean under Unity's Roslyn. What has *not* been exercised
is anything that needs the editor: painting tiles, collider regeneration, spawning, and
the save round trip. Those need a pass in Unity — see the checklist at the end.

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

## Stage 5 — Tuning and quality

**Done:**

- **Metrics** — `DungeonMetrics.Measure(layout)` reports rooms, corridors, loops, loop ratio, dead ends, doorways, open-cell ratio and depth. Surfaced in the Layout Preview window, including a batch mode that runs N seeds and reports failure and retry rates. This is what makes "this parameter change improved the dungeons" a checkable claim rather than an impression, and these are the figures to quote in the write-up.
- **Room templates** — `DungeonRoomTemplate` + `DungeonSpawnMarker`. A template is a prefab whose children are markers; the populator converts the markers into spawns and skips the room's random loot and props. Layouts stay generated, interiors can be designed. This is the standard answer to "procedural levels feel samey", and how Darkwood mixes authored and generated content.

**Not done, with reasons:**

- **Rule Tiles** — the package (`com.unity.2d.tilemap.extras`) is already installed, and `DungeonPainter` types its tile fields as `TileBase`, so a Rule Tile asset drops straight into them with no code change. What is missing is the art: Rule Tiles pick corner and edge sprites, and the generated placeholders have no such variants. Asset work, not code.

  The placeholders themselves went one step further in the meantime: three seed-picked floor variants plus a separate wall-face tile for walls whose south side is exposed, which is what makes the scene read as rooms rather than as a floor plan. Enough for screenshots; not a substitute for real tiles. `Tools ▸ Dungeon ▸ Regenerate Placeholder Tiles` redraws them, and ordinary setup runs never overwrite art replaced by hand.
- **Multiple floors** — decision D5 keys a dungeon to its scene, and `SceneSaveData` is keyed by scene name, so two floors sharing a scene would collide in the save. Doing it properly means either one scene per floor or extending the save key, which is a save-model change and deserves its own change.
- **Biomes** — per-region tile sets and spawn tables. Same blocker as Rule Tiles: needs art to be worth anything.

---

## Stage 6 — Spatial design for a survival horror dungeon

Stages 0-5 produce a *correct* dungeon: connected, seeded, populated, saveable. They do
not produce an *oppressive* one. A room that is a bare rectangle is read in a single
glance from its doorway and is then spent — there is nothing left to learn by walking
into it, and the view cone has nothing to work against. This stage is about the shape of
the space rather than about what is in it.

### The measurement that drives it

`VisibilityAnalysis.VisibleFraction` answers "what share of this room can be seen from
here", by symmetric shadowcasting over the cell grid — engine-free, so it runs in the
test assembly and during generation, long before any collider exists. Symmetric matters:
if A sees B then B sees A, and asymmetric vision in a stealth game reads as a bug.

`VisibleFractionFromEntrances` averages it over the room's doorways, which is what the
player actually experiences. **1.00 means the room gives itself away completely on the
first step in.** That single number turns "the dungeons feel too open" from an
impression into something that can be tuned and reported.

### What was added

| Piece | File | What it does |
|---|---|---|
| Rooms as cell sets | `DungeonLayout.cs` | `Room` is no longer a `RectInt`. `Bounds` survives as the bounding box for placement and templates; `Cells` is the real shape, `Center` is the medoid (the box centre of an L-shaped room is inside solid rock, and corridors are routed between centres) |
| Non-rectangular plans | `RoomShaper.cs` | Ell, Tee and Ring from composite rectangles — orthogonal, so the dungeon still reads as built rather than as geology. Cavern from cellular automata, filtered to its largest component so no unreachable pockets survive |
| Interior structure | `RoomInteriorDecorator.cs` | Colonnade / Partitions / Island / Collapse, placed in batches and **measured after each batch**, stopping once the room is under a size-scaled visibility target. Any batch that would cut the room in two is rolled back |
| Corridor variety | `CorridorCarver.cs` | Width varies per segment and always pinches to one cell at doorways; optional second bend (a Z cannot be seen down from anywhere, an L can); blind alcoves recorded on the layout as ready-made ambush slots |
| Chokepoints | `Chokepoints.cs` | Hopcroft-Tarjan articulation points over the walkable cells — the places a fight cannot be walked away from. Iterative, because the recursive form overflows the stack on a 96×96 map |

Two new cell types, `Pillar` and `Rubble`, carry the interior structure. **They are cells,
not prop prefabs, and that is not an implementation detail.** This project's convention is
that props never block vision or pathfinding, so a pillar spawned as a prop would look
like cover while rays and enemies passed straight through it. As cells they land in the
wall tilemap, join the composite collider, and are seen by `FieldOfView` and
`PathfindingGrid` with zero changes to either — the same argument as decision D1.
`IsWalkable` was rewritten as an explicit allow-list (`Floor | Door`) rather than
"not `Wall`", so the next solid cell type cannot silently become walkable.

### Measured result

500 seeds per configuration, run outside Unity. Zero validation failures, zero retries,
and every walkable cell reachable from spawn in every layout.

| Configuration | Mean room visibility | Worst room | Interior solids | ms/dungeon |
|---|---|---|---|---|
| Stage 5 behaviour (rectangles, empty) | **0.913** | 0.726 | 0 | 3.5 |
| Stage 6 defaults | **0.740** | 0.458 | 113 | 6.9 |

The `interiorDensity` knob moves the figure monotonically and predictably, which is the
evidence that the feedback loop converges rather than just scattering more clutter:

| `interiorDensity` | 0.00 | 0.25 | 0.50 | 0.75 | 1.00 |
|---|---|---|---|---|---|
| mean visibility | 0.887 | 0.769 | 0.736 | 0.711 | 0.693 |
| worst room | 0.609 | 0.506 | 0.456 | 0.407 | 0.364 |

**`shapedRoomChance` does not move visibility at all** (0.730 / 0.737 / 0.735 at 0 / 0.5 /
1.0) — and that is the loop working as designed rather than a bug. A shaped room already
occludes itself, so the decorator hits the target with fewer solids (141 → 99). Shape is a
**variety** knob; density is the **oppression** knob. Worth knowing before tuning either.

Start and Camp rooms are deliberately left legible (camp measures ~0.95). A safe room the
player cannot verify is empty is not a safe room, and the first room of a run is the worst
possible place to hide something.

### Not done

- **Chokepoint-aware population.** The chokepoints are detected and stored but nothing
  reads them yet. The intended use is placing the heaviest enemy *beside* one rather than
  *on* it, so passing becomes a decision instead of a wall.
- **Alcove-aware population.** `layout.Alcoves` is likewise recorded and unused; it is the
  natural spawn list for something that should be behind the player before they see it.
- **Cost of a colonnade on the FOV mesh.** Every pillar adds four silhouette edges inside
  the view cone, and `OcclusionMeshBuilder` refines edges on top of that. The densest
  rooms have not been profiled in play mode. If frame time suffers, the lever is
  `interiorDensity`, not the mesh builder.

### In-editor checklist for this stage

1. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** again — it now also creates `PillarTile` and
   `RubbleTile` and wires them to the painter. Existing art is not overwritten.
2. **Tools ▸ Dungeon ▸ Layout Preview** ▸ Generate. The report gained two lines: shaped
   room share, interior solids, alcoves, chokepoints, and the visibility pair.
3. Generate in the scene and confirm the thing this whole stage rests on: **standing in a
   colonnade, the pillars cast moving shadows and hide the far side of the room.** If they
   do not, they were painted into the floor tilemap instead of the wall one, or the
   composite collider did not regenerate.
4. Walk a corridor and confirm the width changes along it and pinches at doorways.

---

## Risk register

| Risk | Severity | Mitigation |
|------|----------|-----------|
| Composite collider regenerated after the pathfinding grid → invisible walls for A* | High | Explicit ordering in `DungeonBuilder`, asserted in Stage 2 |
| `string.GetHashCode()` instability breaks seed reproducibility | High | Own FNV-1a hash (D4), covered by a determinism test |
| Generated layouts are technically valid but boring | Medium | Stage 5.3 room templates; Stage 6 shaping and interior structure, with the visibility metric as the checkable target; metrics overlay to spot degenerate layouts early |
| Interior structure seals off part of a room, wasting a whole generation attempt | Medium | The decorator rolls back any batch that splits the room, checked room-locally by flood fill — far cheaper than letting the map-wide validator discard the dungeon |
| Dense colonnades cost too much in the FOV mesh rebuild | Medium | Not yet profiled; `interiorDensity` is the lever if it bites |
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

---

## In-editor checklist

Everything below needs the Unity editor and has not been run yet.

1. Open a scene that has a `PathfindingGrid` (e.g. `Dominik 04`), save it as `Dungeon`.
2. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps.** Creates the tilemaps, tiles, settings and registry, and wires painter, builder and populator.
3. **Tools ▸ Dungeon ▸ Layout Preview** ▸ Generate. Confirms the generator without touching the scene; the batch button reports the failure rate over N seeds.
4. Select `DungeonRoot`, right-click the `DungeonBuilder` header ▸ **Generate (random seed)**. A dungeon should appear, with the player standing in the start room.
5. Enter Play mode and check the two things the whole design rests on: **generated walls block the player's field of view**, and **an enemy paths around them instead of through them**. If either fails, the suspect is the collider ordering in `DungeonPainter.RebuildColliders`.
6. Walk to the camp room, save at the typewriter, quit, load. The same dungeon must come back, with dead enemies still dead and collected loot still gone.
7. Tune `RoomContentSettings` — the starting spawn table is a guess, not a design.
