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
| 7 — Concept-art pass | done; wall autotiling unblocked it, see §8 | see §7 below |
| 8 — Outline detail, wall autotiling and placement | done, measured; needs an editor pass | see §8 below |
| 9 — Loot chests | done; needs an editor pass | see §9 below |
| 10 — Central hub | done, measured; needs an editor pass | see §10 below |
| 11 — Player spawns in the hub | done, measured; needs an editor pass | see §11 below |
| 12 — GameManager's spawn point drifts out of sync | fixed in code; **baked scenes need one manual regenerate** | see §12 below |
| 13 — Pathfinding grid covered a quarter of the map | fixed in code; `Dungeon.unity` also hand-edited | see §13 below |
| 14 — Pathfinding gizmo was invisible | fixed in code; `Dungeon.unity` also hand-edited | see §14 below |
| 15 — Blocking objects, pillar count, door alignment | done, measured; door fix reworked twice, confirmed in editor | see §15 below |
| 16 — Real floor art and paper litter | done, previewed outside Unity; **needs a Setup run and an editor look** | see §16 below |
| 17 — Doors on one jamb, threshold tile, pillars in passages, doors in shadow, table pathfinding | fixed, measured over 200 seeds; **needs an editor look** | see §17 below |
| 18 — Mushroom and sleeping-bag decals | done; **needs a Setup run and an editor look** | see §18 below |
| 19 — Statues, a scene-wiring gap, a build-order bug, and a fragile footprint formula | fixed, one confirmed by static proof rather than measurement; **needs an editor look** | see §19 below |
| 22 — Treasure rooms: small locked loot rooms holding the exit key | done in code, measured over 500 + 200 seeds; **needs an editor look** | see §22 below |
| 23 — A way round: two-route metric and loop tuning | done in code, swept over 6 settings and measured on 200 seeds; **needs an editor look** | see §23 below |
| 24 — The hub was a crossroads: corridor cap, clearance, straight walls | done in code, measured over 60 + 200 seeds; **needs an editor look** | see §24 below |
| 25 — Pillars matched to the walls, an exit floor, and procedural prop art | done in code, previewed outside Unity; **needs two tool runs and an editor look** | see §25 below |
| 26 — Light between the pillars, four new decals, door and plank art | done in code, previewed outside Unity; **needs the three tool runs and an editor look** | see §26 below |
| 27 — Inked art: item icons, HUD bars, three floor decals | done in code, previewed outside Unity; **needs three tool runs and an editor look** | see §27 below |

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
- `Generation/Room.cs` — `RectInt bounds`, `int index`, `RoomKind kind` (originally `Start`, `Normal`, `Camp`, `Treasure`; `Camp` became `Hub` in §10, and `Start` was folded into `Hub` in §11 — current values are `Hub`, `Normal`, `Treasure`)
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
6. `Camp` room: `SaveStation` + a `StationaryLightSource`. (Superseded by §10: the camp is now the central `Hub`, and it also carries the crafting table.)
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

Start and Camp rooms are deliberately left legible (camp measures ~0.95); §10 renamed Camp
to Hub and the same exemption follows it. A safe room the
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

## Stage 7 — Closing the gap to the concept art

Stages 0-6 made the dungeon *correct* and *interesting to move through*. Held next to the
concept art it still read wrong, and the reasons turned out to be mostly renderer-side
rather than layout-side.

### What the concept art does that the generator did not

| | Concept art | Was |
|---|---|---|
| Figure/ground | Built structure standing in **black void** | Uniform slab of grey rock with tunnels bored through it |
| Wall | Masonry with courses, corners and a lit top edge | One flat top tile plus one face tile |
| Doorway | A framed opening with jambs | An unmarked one-cell gap |
| Floor | Cracks, stains, spilled grit, worn edges | Three flat variants picked per cell |
| Scale | Two to four rooms fill the screen | Sixty cells across — the map read as a maze, not as architecture |

### What was changed

- **Void instead of bedrock.** `DungeonPainter.BuildWallShell` dilates outwards from the
  open cells and paints only the rock within `wallShellThickness` (default 1). Everything
  deeper is left unpainted, and the floor under it is left out too.

  The thickness has to stay well under half the room spacing, and the first attempt did
  not: thickness 2 against spacing 4 meant the rock between any two rooms was within the
  shell from *both* sides, so all of it was painted and the only void left was the outline
  around the whole dungeon. It looked exactly like the slab it was meant to replace. At
  thickness 1 against spacing 5 each gap keeps three black cells and the rooms read as
  separate structures, which is what the concept art does.

  This is safe for one specific reason worth writing down: the shell is grown from *every*
  open cell at once, so it fully encloses every walkable region. Nothing can see, path or
  walk into the rock behind it, and the composite collider built from the shell alone
  stops exactly what the full slab stopped. It is a rendering change with no gameplay
  consequence — which is the only reason it is allowed to be this cheap.

- **Doorways are a fixed width now, and this was a real bug, not a cosmetic one.**

  `Door_System.prefab` has a 1×1 collider — one cell. The old `MarkDoors` found each
  opening in a room's border, grouped it into a connected clump, and marked *the middle
  cell of the clump* as `CellType.Door`. But nothing had ever constrained how wide that
  clump was: corridor width varies from 1 to 3 along its length by design, so a room could
  easily be entered through a three- or four-cell gap. `DungeonPopulator` then hung one
  door prefab in the middle of it, and the player walked around the door. It was not a
  door — it was scenery with a collider.

  `CorridorCarver` looked like it already prevented this, and its own comment claimed it
  did ("corridors always narrow to this near a room"). It does not. Runs are routed from
  room *centre* to room centre, so a run's pinched ends land inside the room — where the
  cells are already floor and narrowing is a no-op — and at the bends. The width where a
  corridor crosses a room's boundary was never controlled at all, and cannot be without
  routing between boundaries instead of centres. The constant is renamed `PinchWidth` and
  its comment now says what it actually does.

  New pass, `Layout/DoorwayNormalizer.cs`, replacing `MarkDoors`: it finds the same
  opening clumps, and for each one walls up the excess so exactly `doorwayWidth` cells
  survive, centred, then marks those as `Door`. One prefab per door cell, so the opening is
  filled whatever the width is set to.

  Two cases are deliberately left alone rather than forced:
  - **Openings that wrap a corner.** Not a doorway at all — that is a room whose side is
    open onto a corridor running past it. Narrowing it would leave a gap in a wall where
    no door could plausibly hang.
  - **Openings whose narrowing would disconnect the dungeon.** Checked by walling the
    excess, running `CountReachable` against `CountWalkable`, and reverting if they
    disagree. Cheaper than reasoning about which openings are load-bearing.

  Both keep their full width and get **no** `Door` mark, so they read as open arches and
  the populator never tries to hang a door in them. That is the answer to "one consistent
  size if there are to be doors, otherwise it does not matter": a passage either is
  door-width and has a door, or is an arch and has none. There is no in-between where a
  door half-blocks a gap.

- **Content never spawned when generating from the editor.** Separate from the width
  problem above, and it had been true since Stage 3 without being noticed, because every
  check of the populator had been done in Play mode.

  `DungeonPopulator` subscribes to `DungeonBuilder.Built` in `OnEnable`. A plain
  `MonoBehaviour` has no edit-mode lifecycle, so `OnEnable` never ran there; the builder's
  context-menu Generate raised `Built` to an empty subscriber list and produced bare
  geometry — no doors, no enemies, no props. Everything else was wired correctly the whole
  time: `doorPrefabId` is `world.door`, the registry resolves it, the populator is in the
  scene with its references set. Only the subscription was missing.

  This is the case that matters most for this project, because the Dungeon scene is
  authored by *baking* — generate, then save the scene — rather than by generating at
  runtime. `DungeonPopulator.ResetContentRoot` already branched on `Application.isPlaying`
  to use `DestroyImmediate`, so edit-mode population had clearly been intended; nothing
  ever invoked it. Fixed with `[ExecuteAlways]` on the populator, which is load-bearing
  rather than decorative and is commented as such.

- **Baked content kept its prefab link.** Falls straight out of the above. `Object.Instantiate`
  in edit mode returns a *detached* copy — visually identical, but no longer an instance of
  anything, so later edits to `Door_System.prefab` would never reach the doors baked into
  the scene. `PrefabRegistry.Spawn` now routes through `PrefabUtility.InstantiatePrefab`
  when it runs in the editor outside Play mode. Runtime behaviour is untouched, guarded by
  both `#if UNITY_EDITOR` and `!Application.isPlaying`.

- **Thresholds.** `DoorwayTile` is generated and wired at last (the painter had the field
  and a floor-tile fallback, so doorways had been silently invisible). It is drawn with
  jambs on its left and right, and `DungeonPainter.OrientDoorways` gives it a quarter turn
  wherever the passage runs east-west, so the frame is always across the opening rather
  than along it.

- **Decal layer.** A third tilemap, `Decals`, between the floor and the walls, with no
  collider. Cracks, a stain and loose chippings, scattered by
  `DungeonPainter.PaintDecals` from the seed and the cell coordinates — so they survive a
  rebuild-from-seed like the floor variants do — and biased by `decalWallBias` towards the
  cells that touch something solid. Wear collects at the edges of a room; spread evenly it
  reads as noise laid *over* the floor instead of damage *to* it.

  A separate layer rather than more floor variants, because otherwise the floor tile set
  is the product of the number of floors and the number of marks that can appear on one.

- **Palette inverted.** The placeholder tiles were a light warm masonry around dark rooms.
  On screen that reads as the wall being the subject and the room being a hole knocked in
  it — the opposite of the concept art, and the opposite of what this renderer wants. See
  the lighting note under "Not done" for why the floor has to carry the tonal range.

- **Scale.** `DungeonGenerationSettings.asset` went from 96×96 / 12 rooms / 6-14 cells to
  **160×160 / 16 rooms / 12-22 cells**, spacing 3→5, corridors 1→2 wide.

  This landed in two steps and the first one was wrong in an instructive way. The initial
  pass *shrank* the map to 64×64, on the reasoning that the old layouts read as a maze
  rather than as architecture. They did — but the cause was the camera showing sixty cells
  at once, not the map being sixty cells wide. Shrinking the map fixed the symptom by
  making the dungeon too short to play. The camera is the lever for how much is on screen;
  the map size is the lever for how long the run is. They are not substitutes.

  Sizing the map is a packing problem, so it is worth writing the arithmetic down. Rooms
  are placed by rejection sampling against a spacing ring, so they effectively tile with a
  pitch of `average room side + spacing` — here 17 + 5 = 22 cells. A 160-cell map therefore
  has about (160/22)² ≈ 49 room slots in a perfect grid, and random placement realistically
  achieves 30-50% of a perfect packing, so 16 rooms is comfortable. At 144 it would have
  been about 36 slots and 16 rooms would have sat right at the optimistic edge — which
  shows up not as a crash but as `minRoomCount` failing, twelve reseeded attempts, and a
  warning from `DungeonBuilder`.

  `minRoomCount` is 12 rather than 16 for exactly that reason: the target is what placement
  aims at, the minimum is what makes a layout unacceptable, and they should not be the same
  number or every unlucky seed is thrown away.

  The one cost that scales with map area is `PathfindingGrid.BuildGrid`, which runs a
  `Physics2D.OverlapCircle` per cell: 25 600 of them at 160×160, against 9 216 at the
  original 96×96. It is a one-off at dungeon build, not per frame, so it is not a concern —
  but it is the thing to look at first if generation ever starts to feel slow.

  The asset was also stale:
  it predated `maxCorridorWidth`, `doubleBendChance`, `alcoveChance`, `shapedRoomChance`
  and `interiorDensity`, so those had been running on their C# defaults rather than on
  anything authored. They are written out explicitly now.

  Corridor width 2 does not soften the doorway pinch: `CorridorCarver.DoorwayWidth` is a
  constant 1 and independent of the setting, so the beat at the threshold survives the
  wider halls.

### Not done

- **Rule Tiles for the walls.** This is the single largest remaining difference and it is
  blocked on art, not on code: it needs a wall sheet cut into top, south face, four inner
  and four outer corners. Until then walls are still two flat tiles and corners do not
  turn. When it lands, `wallFaceTile` and the `layout[x, y - 1]` test in the painter both
  go away — the rule tile subsumes them.
- **Buttresses and chamfered corners** on room perimeters (`RoomShaper`), which is the
  rhythm most visible in the concept art's wall runs.
- **Doorway jamb cells** — the frame is currently painted, not built. Two `Pillar` cells
  either side of each threshold would make it geometry the FOV can actually cast from.
- **Floor medallions** in large rooms (`RoomInteriorDecorator`).
- **Clustered props.** `DungeonPopulator` still scatters props uniformly; the concept art
  groups them against walls and in corners.
- **Lighting — and there is nothing to do here, which is worth writing down** so the next
  person does not reach for the obvious wrong lever.

  This project does not light the scene with URP 2D lights. It has its own screen-space
  system: `Vision/VisionMaskRenderer.cs` draws the player's `FieldOfView` mesh and every
  `StationaryLightSource` into a single offscreen `_VisionMask` with `BlendOp Max`, and
  `Shaders/DarknessOverlay.shader` then takes every pixel to black in proportion to how
  little of that mask reaches it (`_Darkness` 0.97). On top of that, occupants — enemies,
  barrels — are hard-clipped at the vision boundary by the stencil prepass described in
  `ENEMY_NOTES.md` §GU-0036.

  Adding a URP `Global Light 2D` would fight all of it: it dims the inside of the vision
  cone as much as the outside, while `DarknessOverlay` is already taking the outside to
  near-black, so the only visible result is that the lit area gets muddier. The dungeon is
  *already* almost entirely black at runtime.

  Which means the atmosphere lever is not a light at all — it is **the tile palette**,
  because the lit floor inside the cone is the only place any tonal range survives. That
  is why the Stage 7 palette was inverted to a light floor against dark stone rather than
  left as dark-on-light and dimmed afterwards. The remaining levers, in order of how much
  they do: floor and wall colours, `DarknessOverlay._Darkness`, and placing
  `StationaryLightSource` lamps through `DungeonPopulator` so rooms have their own pools
  of light for the mask to pick up.

### In-editor checklist for this stage

1. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** — required, not optional. It creates the
   `Decals` tilemap, the `DoorwayTile` and the four decal tiles, and wires all of them.
   Without it the painter has null decal references and simply skips that pass.
   `PillarTile` and `RubbleTile` from Stage 6 have never been generated either, so pillars
   and rubble are still falling back to the plain wall tile.
2. **Tools ▸ Dungeon ▸ Regenerate Placeholder Tiles** — also required, and only because
   the palette changed. `Setup` never overwrites an existing PNG (by design, so hand-made
   art survives a re-run), so the old warm tiles stay on disk until this is run.
3. Set the camera so roughly 28×16 cells fill the frame. This is the lever that makes the
   dungeon read as architecture; the map size is not a substitute for it.
4. **Tools ▸ Dungeon ▸ Layout Preview ▸ Generate, a dozen times.** The packing figures
   above are arithmetic, not measurement — the layout assembly targets netstandard 2.1 and
   will not load under the .NET Framework host, so it could not be exercised headlessly
   from outside the editor this time. The preview reports the room count directly. Expect
   14-16; a run of results at or below `minRoomCount` (12) means the map is too tight for
   the target and wants either more area or a smaller `maxRoomSize`.
5. Generate, and check the two things that could be wrong in a way compiling cannot catch:
   **doorway tiles are turned the right way** (jambs across the opening, not along it),
   and **no walkable area is open to the void** — if any is, `wallShellThickness` is being
   read as 0 or the shell dilation is four-connected instead of eight.
6. Confirm the void actually separates the rooms rather than only outlining the map. If it
   does not, `wallShellThickness` has crept back up towards half the room spacing.
7. Confirm decals sit *under* the walls and *over* the floor, and that nothing on the
   decal layer blocks movement.
8. **Walk up to a door and try to get past it.** This is the check the whole doorway pass
   exists for: every opening that has a door prefab in it must be fully blocked by that
   prefab. If any door can be walked around, its opening was not narrowed — look for a
   corner-wrapping clump or a reverted narrowing.
9. Confirm arches still appear. If *every* opening is door-width the pass is over-eager;
   if none are, `doorwayWidth` is not reaching `LayoutParams` from the asset.
10. **Judge the look in Play mode, not in the Scene view.** Everything outside the vision
   cone is black at runtime and neither `DarknessOverlay` nor the FOV mesh exists outside
   Play, so the Scene view shows a flat, fully-lit map that the player never sees.

---

## Stage 8 — Outline detail and content placement

Everything on the Stage 7 "Not done" list that was *not* waiting on art. Four of the five
items were sitting next to the blocked one rather than behind it, and two of them were
reading data the generator had been computing and throwing away since Stage 6.

Unlike Stages 6 and 7, this one was **measured rather than reasoned about**: 150 seeds per
configuration, through a headless harness (see below).

### What was changed

- **Room outlines have a rhythm.** `RoomShaper.PerimeterNotches` bites the corners off
  diagonally and pushes about one cell in six of a long wall inwards. A wall that runs
  twenty cells unbroken reads as a boundary, and its corners read as a selection box;
  neither survives contact with a room the player is meant to believe was built. Applied to
  plain rectangles as well as carved shapes, which is where it matters most. Tuned by
  `perimeterDetail` on the settings asset, 0 restores square outlines.

  **Where this runs is the whole design, and the first attempt had it wrong.** The obvious
  place is inside `BuildRoom`, trimming the cell list before the `Room` is constructed.
  Measured over 120 seeds that added roughly **one spurious door to every room**: a trimmed
  cell is no longer a room cell, so every notch next to a corridor registered as its own
  opening in `DoorwayNormalizer`. Doors per map went 47.6 → 65.3.

  Moved to `RoomCorridorGenerator.DetailOutlines`, between carving the rooms and carving
  the corridors, where it turns cells solid *without* removing them from `Room.Cells`. They
  stop being walkable and stay part of the room, so the doorway search never sees them.
  Doors per map: 47.6 → 47.5. A corridor carved afterwards simply punches back through any
  notch in its way, which is the right precedence — the doorway beats the decoration.

- **Doorways are framed.** `DoorwayNormalizer` was already walling up the excess when it
  narrowed an opening; those cells are now `Pillar` instead of `Wall`. Mechanically
  identical — both solid, both stop vision — but the painter gives `Pillar` its own tile,
  so the cells flanking a door read as built jambs rather than as the bedrock the opening
  was cut through. It is the cheapest possible version of the concept art's framed doorway,
  because the frame is made of cells that were going to be filled in anyway. Measured at
  **59.7 jamb cells against 47.8 doors**, so most openings get one.

- **Props come in clusters.** The per-room budget is unchanged; only its distribution is.
  `SpawnProps` now picks an anchor — biased hard towards a cell touching something solid —
  and puts two to four of *the same* prop around it. Objects in a room were put there by
  someone: barrels stand in threes against a wall, crates get stacked in a corner. One per
  cell over the whole floor reads as scatter laid over the room rather than as its
  contents. The wall bias does a second job for free: it keeps the middle of the room
  clear, which is where the player has to fight.

- **Alcoves and chokepoints are finally read.** Both have been detected and stored since
  Stage 6 and nothing had ever looked at them. `SpawnCorridorAmbushes` places a small,
  hard-capped number of enemies outside rooms:
  - in **alcoves**, because an alcove is a pocket whose mouth is behind the player by the
    time its interior enters their view cone — the layout's own ambush slot;
  - **beside** a **chokepoint**, never on it. On it, the only route is blocked and the
    player has no decision to make. Beside it, they have to decide whether getting through
    is worth being seen.

  Corridors have no depth of their own, so the nearest room's `DepthFromStart` is borrowed
  for the enemy table's `minDepth` gating — without it every corridor would count as depth
  zero and put late-game enemies in the first hallway. Anything at depth 0 is skipped
  outright: an ambush in the corridor out of the start room is not a fair opening move.

- **Wall autotiling, which is what makes all of the above visible.** The outline work above
  was measurable and invisible, and that is worth being blunt about: ~10 notches per room
  were being cut, and every one of them was painted with the same flat tile as the wall it
  sat in. A one-cell bump drawn in the same twelve pixels of stone as its neighbours is not
  a bump; it is nothing. Held against the concept art the rooms still looked like boxes.

  `DungeonPainter` now picks a wall tile from a **sixteen-entry array indexed by which of
  the four sides are exposed** — bit 0 north, 1 east, 2 south, 3 west. Exposed means "not
  painted as wall", so open ground, doorways, pillars *and the void behind the shell* all
  count. The void has to count, or the outer boundary of the structure would be drawn as
  though it continued into rock that is not there and the dungeon would lose its silhouette
  against the black.

  Measured over 20 maps, the masks come out as follows — and the distribution is the reason
  this works at all:

  | Mask | Share | What it is |
  |---|---|---|
  | 5 (N+S), 10 (E+W) | 32% each | thin wall, open on both sides |
  | 3, 6, 9, 12 | ~5% each | corners, ~430 per map |
  | 7, 11, 13, 14 | ~1.5% each | three sides open — the buttress peninsulas, ~125 per map |
  | 0 (interior) | **0.4%** | fully buried |

  Almost nothing is interior. Every wall cell gets an edge, so corners read as corners and
  a one-cell buttress reads as something sticking out.

  **The sixteen sprites are generated, not authored** — one drawing routine parameterised
  by the mask, in the same placeholder style as everything else. This is the thing that had
  been listed as "blocked on art" since Stage 5, and it did not need to be: it needed a
  wall sheet cut into corners *or* sixteen procedural variants, and the second is a
  half-page of code. When the real sheet arrives it can fill the same sixteen slots or be
  swapped for a Rule Tile; the painter asks for a tile per mask and does not care which.

  The old `wallTile`/`wallFaceTile` pair survives as a fallback, used when the array is not
  fully populated. All sixteen or none — a half-filled array would paint some walls with a
  null tile and leave holes that look like doorways.

- **Jamb runs capped, fixing a defect this stage introduced.** Making the whole walled-up
  excess `Pillar` looked right on a three-cell opening and absurd on a wide one. A corridor
  running the length of a room's wall is a single opening fourteen cells across, and
  walling it produced **fourteen pillars in a row** — a colonnade embedded in a wall rather
  than a door frame. Measured at 3.4 runs of six or more per map, max 14. Only the two
  cells nearest the door are `Pillar` now (`DoorwayNormalizer.JambDepth`); the rest stays
  `Wall`.

### Measured, over 150 seeds at the shipped settings

| | `perimeterDetail` 0 | `perimeterDetail` 0.6 |
|---|---|---|
| Failed validation | 0/150 | 0/150 |
| Maps with unreachable cells | 0/150 | 0/150 |
| Rooms | 16.0 | 16.0 |
| Walkable cells | 4773 | 4632 |
| Outline notches | 0 | 154.3 |
| Door cells | 47.8 | 47.8 |
| Mean room visibility | 0.465 | 0.457 |
| Worst room visibility | 0.288 | 0.262 |

Outline detail costs about 3% of the floor, leaves doorway counts untouched, and moves
both visibility figures slightly *down* — which is the direction Stage 6 established as
better, since a room that cannot be read in one glance from its doorway still has
something to find out.

### The headless harness — worth knowing about

Stage 7's numbers were arithmetic because the layout assembly targets netstandard 2.1 and
will not load under the .NET Framework host, and the obvious workarounds all failed. The
way through is not to reference `Grave.Generation.Layout.dll` at all: compile
`Assets/Scripts/Generation/Layout/*.cs` **directly** into a `net8.0` console project that
references only `UnityEngine.CoreModule.dll`. The layout code touches nothing but
`Vector2Int`, `RectInt` and `Mathf`, all of which are pure managed structs, so it runs
outside Unity exactly as it does inside. That is what made this stage measurable, and it is
how any future generator change should be checked before it reaches the editor.

### Not done

- **Cost of a colonnade on the FOV mesh** — still unprofiled, and now more relevant, since
  jamb pillars add silhouette edges at every doorway. Needs Play mode; cannot be measured
  headlessly.
- Everything in Stage 7 that is genuinely blocked on art: Rule Tiles and biomes.
- **Floor medallions** in large rooms — deferred with Rule Tiles, since it wants its own
  sprite rather than a recoloured floor variant.

### In-editor checklist for this stage

1. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** — required. It generates the sixteen
   `WallTile_NN` variants and wires them to the painter. Without it the array is empty,
   the painter falls back to the old flat pair, and none of the outline work is visible.
2. **Tools ▸ Dungeon ▸ Layout Preview ▸ Generate** a few times and read the shaped-room
   and visibility lines; they should sit near the table above.
3. Generate in the scene and look at a room's **outline**: corners cut diagonally, the odd
   cell of each long wall pushed in. If every wall is dead straight, `perimeterDetail` did
   not reach `LayoutParams` from the asset.
4. Look at a **doorway**: it should have a pillar cell to one or both sides. These cast FOV
   shadows, unlike the painted threshold tile, so standing off to one side of a door should
   now hide part of the room beyond it.
5. Check that **props stand in groups against walls**, not spread evenly over the floor,
   and that the middle of a room is clear.
6. Walk a corridor and confirm the ambushes read as intended: something waiting in a side
   pocket, and something posted next to a pinch rather than blocking it.

### Two fixes after seeing it in play, not measurable headlessly

- **The autotile "face" was a 3D illusion, and this is a top-down game.** The south-exposed
  variant drew a lit cap and a shadow gradient the same way the old `wallFaceTile` always
  had, to read as "wall seen at an angle". In-game, on a wall spanning many cells, it read
  as exactly that — a perspective view — which is wrong for this camera. Removed: every
  side now gets the same flat rim treatment. `WallFaceTile`/`wallFaceTile` (the pre-autotile
  fallback pair) still has the old effect; it only matters if the autotile array is
  incomplete, and was left alone rather than touched speculatively.

- **Walls were nearly invisible outside the light, and the whole outline/autotile pass was
  going largely unseen because of it.** Confirmed by reading `OcclusionMeshBuilder.Cast`:
  the FOV mesh's rays stop at `hit.point` on the wall collider, so the mesh's vertices sit
  exactly on the floor/wall boundary and never enter a wall cell. The vision mask is
  therefore 0 over the entire interior of every wall tile, `DarknessOverlay` (`_Darkness`
  0.97) takes it to near-black, and none of the courses, joints or corner variants this
  stage added were legible except in a sliver at the mask's edge fade.

  Fixed without touching the FOV/stencil system at all: `WallSortingOrder` moved from -50
  to **7**, above `DarknessOverlayQuad`'s sorting order (6, confirmed on the scene
  instance). The overlay simply never draws over the wall tilemap now, so walls (and the
  pillars and rubble sharing that tilemap) render at their own colour everywhere,
  regardless of vision. This is the Darkwood read of it: architecture stays dimly legible
  by its own dark, desaturated art the way a ruin's silhouette does outside a flashlight
  beam, while floor, loot and anything standing on it are still properly fog-of-war'd.
  Occupants (enemies, barrels) are untouched — they still vanish outside the FOV stencil
  exactly as before; this only exempts the *static* tilemap from the separate darkening
  pass, which is a different mechanism.

  **Known interaction, not fixed here:** this also puts walls above ordinary world sprites
  (order ~0-2), door leaf included (order 1). `Door_System`'s leaf is presently 2.8 cells
  long — a fix to 1 cell was made and then reverted earlier in this branch's history, at
  the other developer's request, since the door system is being redone separately. A leaf
  that wide swings across the jamb cells beside its own doorway, and those cells are now
  drawn *after* it. Expect the open door to visually clip behind the wall/pillar tiles it
  overlaps until the door prefab is refitted. Flagged for whoever picks that up next, not
  addressed here.

- **Prop clusters overlapped and blocked passages.** Reported after playing, not
  measurable from the layout alone: the clustering added earlier in this stage placed
  prop cluster members one cell apart, which assumes every prop fits in one cell. It
  does not. `Table.prefab`'s own collider is about 2.5×1.3 cells — read directly, see
  below — so two tables one cell apart overlap by roughly half a table, and a table
  anchored near a narrow point can physically wall it off: `PathfindingGrid`'s per-node
  check radius is `cellSize * 0.45`, and a footprint that wide reaches well past the
  neighbouring node's own check circle.

  Two fixes in `DungeonPopulator`, both driven by measuring the actual prefab rather than
  assuming a size:

  - **`FootprintCells`** reads a prefab's `BoxCollider2D.size` through its
    `Transform.lossyScale` — not `Renderer.bounds`/`Collider2D.bounds`, which are
    unreliable (often zero) on a prefab *asset* that has never been instantiated, which is
    exactly what `PrefabRegistry.Resolve` hands back before anything is spawned. Cluster
    members are now spaced by this measured size (`TryTakeSpaced`, checked pairwise
    against every member already placed, not just the anchor — two members can each be far
    enough from the anchor and still land on each other if only that distance is checked).
  - **`BlocksPathfinding`** walks the prefab's colliders and checks their layer against
    `ObstacleStatic`/`ObstacleDynamic`/`ObstaclePathOnly` — the layers
    `PathfindingGrid.obstacleMask` actually reads. **Not filtered by `isTrigger`**: this
    project's `Physics2DSettings.queriesHitTriggers` is `1` (confirmed in
    `ProjectSettings/Physics2DSettings.asset`), so a trigger collider on an obstacle layer
    blocks the grid exactly like a solid one. This is not a hypothetical — `Table.prefab`'s
    own `ObstaclePathOnly` collider (root object, the one that sets its footprint) *is* a
    trigger, and an early version of this check excluded triggers and would have called
    every table harmless. Props found to block movement this way are kept off
    `layout.Chokepoints` and their immediate neighbours (`DungeonLayout.NearAnyChokepoint`,
    a new lazily-built lookup) — the same reasoning as the corridor-ambush chokepoint rule
    above: that cell is the only route through somewhere, and a prop parked on it defeats a
    connectivity guarantee the generator worked to provide.

  Left alone, flagged rather than fixed: `RoomContentSettings.props`'s own tooltip says
  props "never block vision or pathfinding", and both currently-registered props
  (`prop.barrel`, `prop.table`) are on `ObstaclePathOnly`, which by definition blocks
  pathfinding. The comment is stale relative to the layer convention `ENEMY_NOTES.md`
  §GU-0036 established for `Barrel`; reconciling it is a documentation call for whoever
  owns that convention, not a generation change.

---

## Stage 9 — Loot chests

`GU-0057`. Chests are the reward the treasure room was missing: prior stages left it with
loose floor loot and nothing that reads as a container worth finding.

### What was added

- `ChestInventory.SetStartingItems(...)` — the only path the populator uses to stock a
  chest. It writes `startingItems`, never `Container`: the container is runtime-only
  state, and the Dungeon scene is authored by *baking* a generated dungeon (generate in
  edit mode, then save the scene, same as every other Stage 7/8 spawn), so anything
  poured straight into the container would vanish the moment the scene reloads. Outside
  Play mode it also calls `EditorUtility.SetDirty`, which is load-bearing for the same
  reason `[ExecuteAlways]` was for the populator in Stage 7 — without it a baked chest
  looks stocked in memory and comes back empty after a domain reload.
- `DungeonPopulator.SpawnChests`/`StockChest` — a new pass in `SpawnRoomContent`, inserted
  **after enemies and before props** in both the `default` and `Treasure` cases. That
  position is not arbitrary: chests share the per-room `slot` counter that feeds
  `SlotGuid` (D7), so where a new spawn call lands in the sequence renumbers everything
  after it. Once chosen, this order should not move.
- `RoomContentSettings` — `chestChancePerRoom` + `chestChancePerDepth` (rewards bias
  towards the far end of the run, same shape as the enemy count curve),
  `chestLoot`/`treasureChestLoot` tables, and a guaranteed `treasureChests` count so the
  Treasure room always has at least one. Start and Camp rooms never get one — same rule as
  enemies, for the same reason.
- Chest spawns reuse `TryTakeAnchor(..., blocking: true, ...)` unchanged from the props
  pass: the chest prefab sits on `ObstaclePathOnly`, so it is read as blocking, which keeps
  it off chokepoints and biases it against a wall — the same spot it would read right in
  visually.
- Chest contents are **not** routed through `Drop`/`WorldItemPickup` like floor loot.
  Floor loot is a world drop, captured by `WorldItemsSaveable` by position; a chest's
  contents are entity state, captured by `ChestSaveable` (`GU-0053`) by guid. Writing
  through `SetStartingItems` keeps chest loot on the two-path model the save system
  already assumes rather than inventing a third one.
- `PrefabRegistry` needed no change — `world.chest` was already registered.
- **`Chest.prefab` sorting order — raised to 8, then reverted to 0, and this dead end is
  worth recording so nobody repeats it.** The chest anchored against a wall — which is
  where `propWallBias`-style placement puts it on purpose — had its own top-left corner
  drawn over by the wall tile bleeding past its cell edge, the same interaction Stage 8
  flagged for the door leaf. Bumping the sorting order above `WallSortingOrder` (7) looked
  like the same fix. It is not: a chest is an **occupant** in the `GU-0036` sense (see
  `ENEMY_NOTES.md`), stencil-clipped outside the FOV the same as `Barrel` and the enemies,
  and that clip only works at the draw order the pipeline actually expects — masked
  sprites at 0, `FovMaskWriter` at 5, `DarknessOverlay` at 6, walls at 7. An order of 8 sits
  *above all of it*, so the chest stopped respecting the stencil test altogether and
  rendered on top of the entire scene regardless of vision, which is a worse defect than
  the one-corner wall bleed it was chasing. Reverted to 0 — the same order `Barrel` uses —
  and the wall-corner bleed is left as what it already was for every other wall-hugging
  occupant: a known, accepted limitation, not something to solve by moving sort order
  again.

- **Chest wired into the GU-0036 stencil pipeline, matching `Barrel`.** Without this a
  chest is visible through walls and fog alike, which defeats the entire point of a hidden
  reward. `SpriteRenderer.material` set to `Materials/SpriteFovMasked.mat` (design-time
  default, `_StencilComp` unset so it still renders normally outside Play), plus a
  `FovMaskedSpriteRuntime` component (`clippedMaterial: Materials/SpriteFovMaskedClipped.mat`)
  on the same GameObject as the `SpriteRenderer` — `Awake()` swaps to the real clipped
  material at runtime, which is the only place the fixed-function `Stencil { Comp Equal }`
  block is actually honored (see the `GU-0036` write-up in `ENEMY_NOTES.md` for why a
  `MaterialPropertyBlock` does not work here).

- **The chest's reach trigger moved to a child GameObject, on its own layer.** A deeper
  version of the same "flush against a wall" problem, found after the sorting fix still
  looked wrong up close: `Chest.prefab` carried *two* `BoxCollider2D`s on the same root
  GameObject — the 1×1 physical footprint (`ObstaclePathOnly`, exactly one cell) and a
  2.33×2.33 trigger for player reach. Unity layers are per-GameObject, not per-collider, so
  the reach trigger was silently on `ObstaclePathOnly` too — more than double the chest's
  real width, counted as a pathfinding obstacle it was never meant to be. A hand-placed
  chest with clearance around it never showed this; a chest the generator deliberately
  anchors flush against a wall pushed that oversized trigger into the wall's own space,
  both tightening `PathfindingGrid`'s walkable area near the wall by more than the chest
  actually occupies and reading, up close, as if the chest's colliders were embedded in the
  wall.

  Fixed by giving the reach trigger its own child object, `ReachTrigger`, on layer
  `Interactable` — nothing in `PathfindingGrid.obstacleMask` or the vision system's
  obstacle mask treats that layer as blocking, so the reach zone can freely reach into
  neighbouring cells (including a wall cell, which is fine — a chest flush against a wall
  should still be reachable) without affecting what is walkable or visible. `Awake`/trigger
  events on a child do not reach a parent's `MonoBehaviour` automatically, so
  `ChestReachForwarder` relays them to `ChestInteractable` by `SendMessage`, the identical
  pattern `DoorCollisionForwarder` already uses for the door. The root GameObject keeps
  only the 1×1 non-trigger collider, so it is now exactly the chest's real footprint —
  nothing bigger.

### Not done

- **Stocking template-marker chests.** A room template's `Prefab` marker with id
  `world.chest` spawns an empty chest; giving markers their own loot table is a
  `DungeonSpawnMarker` change and belongs with the template work, not here.
- **Locked chests.** `GU-0045` added key-opened doors; a locked chest would want the same
  treatment and is a separate change.
- **Mimics / trapped chests.**

### In-editor checklist for this stage

1. Fill `RoomContentSettings.asset`'s `chestLoot`/`treasureChestLoot` if retuning — the ids
   are `ItemData.Id` guids, not readable strings; read them off the asset or the
   `ItemDatabase`, do not guess.
2. Generate a dungeon. Chests should appear against walls, none in the start room, at
   least one in the treasure room.
3. Enter Play mode, open a chest with `E`: it should have items in it. An empty panel
   means either the loot table is empty or stocking went to `Container` instead of
   `startingItems`.
4. Take an item, save, quit, load: the chest comes back still missing that item, and every
   other chest keeps its own independent contents (the `GU-0053` guarantee).
5. Regenerate with the same seed twice: identical chest positions and identical contents.

---

## Stage 10 — The central hub

`GU-0065`. One room in the middle of the map holds a save station and a crafting table,
and it is the only place in the game that has either.

### What changed, and the one decision the rest follows from

| # | Decision | Rationale |
|---|----------|-----------|
| D8 | The hub is **reserved before any other room is placed**, not chosen afterwards | A hub is only a landmark while the player can predict where it is. Rejection sampling routinely leaves the middle of a map empty, so "tag whichever room came out nearest the centre" cannot promise a central room — it promises *a* room, somewhere. Reserving makes the position a guarantee of the layout rather than an outcome of it |
| D9 | `RoomKind.Camp` **becomes** `RoomKind.Hub` rather than joining it | The camp already held the save station. Keeping both roles would put two save stations in a dungeon, which is exactly the thing this change exists to prevent |

`RoomCorridorGenerator.PlaceHub` writes a square room centred on the map into the room
list first, and `PlaceRooms` samples the remaining `TargetRoomCount - 1` around it. Nothing
downstream needed a special case: later rooms are rejected unless they clear the hub by
`RoomSpacing` exactly as they clear each other, and the spanning tree connects it like any
other node. `AssignRoomRoles` now only carries the role through, and excludes the hub from
both Start and Treasure — a reward stashed in the one room the player is safe in is not a
reward, and starting in the hub would delete the walk that makes it worth anything.

*Superseded by §11: the player does now start in the hub, `RoomKind.Start` no longer
exists, and the last sentence above is exactly backwards — see below for why.*

The hub is built as a plain rectangle and never passed through `RoomShaper`, and
`RoomInteriorDecorator` skips it under its existing Start/Camp rule. Same reason as before:
the player has to be able to see that the safe room is empty.

### What this trades away, stated plainly

The camp was chosen as **the deepest dead end** available, on the argument written into
`IsBetterCamp` that a safe room the player can be chased *through* is not safe. A room at
the centre of the map is the opposite of a dead end — it measures **3.67 corridors on
average**, so it can be entered from several sides at once and never has the one-way-in
property the camp was picked for.

That is inherent in what was asked for, not an oversight: "in the middle of the map" and
"a dead end" are mutually exclusive on this generator. It is recorded here because if the
hub later feels unsafe to sit in, the lever is a door or a scripted quiet zone, **not**
moving the hub off the centre — that would give up the landmark this stage exists to
create.

### Fixtures

`DungeonPopulator.SpawnHubFixtures` places the save station, the crafting table, empty
chests and lamps, each through `SpawnFixture`:

- **Near a wall but never touching one.** This is the correction to the first version of
  this stage, which anchored fixtures on a wall-adjacent cell the way `SpawnProps` does and
  produced a crafting table with half of it inside the wall.

  The cause is not placement drift, it is Stage 8's sorting order. `WallSortingOrder` is 7,
  **above** ordinary world sprites, so any part of an object that overlaps a wall cell is
  not merely close to the wall — it is painted over by it. `CraftingTable.prefab` draws
  1.48×0.78 cells (9.84×5.2 at scale 0.15, sprite and collider identical, draw mode
  Simple), so centred on a cell touching a wall it puts ~0.24 cells under the wall tile at
  each end, and the wall wins. The chest corner bleed Stage 9 recorded and accepted is the
  same interaction; this is the general fix for it on generated fixtures.

  `TryTakeFixtureCell` therefore requires `footprint / 2` cells of walkable ground around
  the anchor, and then prefers the cells where solid rock starts *exactly one ring beyond*
  that — which is what "standing against the wall" looks like once the object's own width
  is accounted for. Three passes: clearance and a wall just past it, then clearance
  anywhere, then anything free at all. Only the last gives up the guarantee, and it exists
  so a hub too cramped to furnish still gets a save station rather than none.
- **Measured by what it draws, not only by what it collides with.** `FootprintCells` now
  takes the larger of the `BoxCollider2D` and the drawn sprite size (`Sprite.bounds`, or
  `SpriteRenderer.size` for sliced/tiled renderers), both read from serialized data so they
  are correct on a prefab asset that has never been instantiated. A prefab whose art is
  wider than its collider — which is the normal case, not an exotic one — would otherwise
  be placed flush and lose exactly the difference to the wall.
- **Spaced pairwise** against everything already placed, by the wider of the two. A
  crafting table sitting inside the save station is the failure mode.
- **Widest first, lamps last.** All of these compete for the same wall-side cells. A lamp
  is one cell and fits almost anywhere; a crafting table needs room around it. Placing the
  lamps first measurably crowded the chests out — 11 in 600 fell through to the last-resort
  pass at `hubRoomSize` 12. Reordered, that is 0.
- **Lamp spacing scales with the room**, capped at `MaxLampSpacing` = 5. Lamps are spread
  so the hub is lit from several sides rather than from one corner, but a fixed distance
  that does not fit does not spread lamps out — it loses them.
- **Blocking-aware**, reusing `BlocksPathfinding`, so a fixture never lands on or beside a
  chokepoint.
- **Loud on failure.** A dungeon whose save station silently did not spawn cannot be saved
  in, so a fixture that finds no cell logs a warning naming the prefab and the knob to raise.

Chests are spawned **empty**, through `ChestInventory.SetStartingItems` with nothing in it
— which also clears whatever the prefab itself carries. They are the player's own storage
rather than loot, and `ChestSaveable` (`GU-0053`) already persists their contents by guid,
so what the player leaves there survives a save. Counts are `hubLamps` and `hubChests` on
`RoomContentSettings`, both 3.

The hub gets **no props**. `SpawnProps` anchors clusters against walls, which is precisely
where the fixtures stand, and the one room that has to be usable is the wrong place to
spend the prop budget.

### Nothing spawns in the hub, and what that had to mean

The room pass never put an enemy in the hub — its case in `SpawnRoomContent` calls
`SpawnHubFixtures` and nothing else — and `SpawnCorridorAmbushes` already refused any cell
inside a room. What was left was the corridor immediately outside: an ambush placed one
cell from the doorway is, seen from inside the room, something waiting in the hub.

`IsUsableAmbushCell` now also rejects anything within `HubKeepOut` = 4 cells of the hub's
bounds, so "no enemies at the hub" means what a player would take it to mean rather than
what the cell test happened to say.

**This governs spawning only.** Nothing stops an enemy *walking* into the hub during play —
it is a through-room with several corridors into it (see the trade above). If the hub needs
to be inviolable rather than merely unpopulated, that is a pathfinding or behaviour change,
not a generation one.

`craftingTablePrefabId` is new on `RoomContentSettings`; `world.craftingtable` was already
in the registry and needed nothing. `hubRoomSize` is new on `DungeonGenerationSettings`,
clamped into the ordinary room size range *after* `MaxRoomSize` has been fitted to the map,
so the reserved centre can never be a room shape the generator would otherwise have
rejected. Both are written explicitly into the authored assets rather than left on their C#
defaults — the Stage 7 lesson about stale assets.

### Saves made before this change

Existing saves do not survive it, and there is no way to make them. A save stores a seed
(D6), and the same seed now places a room where there was none, shifts every later
rejection-sampled room, and renumbers the hub's own spawn slots — the crafting table takes
the slot the lamp used to have. Loading an old save regenerates a *different* dungeon and
overlays entity state onto guids that no longer exist. This is inherent to changing the
generator rather than a defect in this stage: any layout change has the same consequence,
which is the cost of storing seeds instead of worlds. Delete old saves.

### Measured, headlessly, through the Stage 8 harness

| | shipped 160×160, hub 16 | tests 64×48, hub 14→9 | shipped, pure tree |
|---|---|---|---|
| Seeds | 200 | 200 | 50 |
| Validation failures | 0 | 0 | 0 |
| Layouts with unreachable cells | 0 | 0 | 0 |
| Retries | 1 | 1 | 1 |
| Layouts without exactly one hub | 0 | 0 | 0 |
| Hub off the map centre | 0 | 0 | 0 |
| Hub tagged Start or Treasure | 0 | 0 | 0 |
| Spacing violations | 0 | 0 | 0 |
| Mean rooms | 16.00 | 8.00 | 16.00 |
| Mean hub degree | 3.67 | 2.99 | 2.34 |

**Mean rooms lands exactly on the target in every configuration**, which is the number that
answers the only real risk in reserving space: the hub does not cost a room, because it
*is* one of them. The 14→9 column is the clamp working — `SmallParams` caps rooms at 9.

Fixture placement was measured the same way, by mirroring `TryTakeFixtureCell` against
generated layouts — 200 seeds, eight fixtures per hub (station, table, 3 chests, 3 lamps):

| `hubRoomSize` | 12 | 14 | **16 (shipped)** | 20 |
|---|---|---|---|---|
| Fixtures overlapping rock | 0 | 0 | **0** | 0 |
| Fixtures overlapping each other | 0 | 0 | **0** | 0 |
| Placed by the last-resort pass | 0 | 0 | **0** | 0 |
| Fixtures not placed (of 1600) | 40 | 1 | **3** | 0 |

Every unplaced fixture is a lamp, which is the right thing to lose: the clearance and
spacing rules are never relaxed to fit one in, and the station, table and chests place in
every single seed at every size tested. At the shipped size that is 3 missing lamps in
1600, or roughly one dungeon in 200 logging one warning.

Isolation was checked separately, since the hub is the one room a run cannot do without.
Forcing `MaxGenerationAttempts` to 1 over 500 seeds so the retry loop cannot hide anything:
**1 attempt in 500 left a room cut off, and it was an ordinary room, never the hub.** Being
central, the hub is the best-connected node in the graph and the least likely thing in the
dungeon to be walled off. The existing validate-and-reseed loop catches that case as it
always did.

### Not done

- **Making the hub feel safe rather than merely being safe.** See the trade above — it is a
  through-room now. Nothing stops an enemy wandering in.
- **A door or threshold that marks the hub as different.** It is currently distinguished
  only by what stands in it.
- **Hub-aware ambush placement.** `SpawnCorridorAmbushes` skips depth 0 (beside the start
  room) but knows nothing about the hub, so a guard can be posted in the corridor right
  outside it.
- **`CraftingTable.prefab` carries its player-reach trigger on the root, on
  `ObstaclePathOnly`** — measured from the asset: a 19.84×15.2 trigger at scale 0.15, so
  ~3.0×2.3 cells, against a physical collider of ~1.5×0.8. This is the identical defect
  Stage 9 found on `Chest.prefab` and fixed by moving the trigger to a child on
  `Interactable`. `queriesHitTriggers` is 1, so those ~3×2 cells are unwalkable to
  `PathfindingGrid` even though the table is not that big. Consequences are mild — the
  table stands against a hub wall, `BlocksPathfinding` already keeps it off chokepoints,
  and player movement is governed by the smaller solid collider — but it is now in every
  generated dungeon rather than in hand-placed scenes only. Left as a prefab change for
  whoever picks up the Stage 9 pattern, not done here.
- **Anything in the other scenes.** `Demo.unity` has hand-placed crafting tables and save
  stations; this change governs generated dungeons only. If "the only such place in the
  game" is to hold literally, those scene-authored ones want removing, which is a scene
  edit rather than a generation change.

### In-editor checklist for this stage

1. Generate a dungeon. **The middle of the map is a square room**, and it has a save
   station, a crafting table, three lamps and three chests around its edges.
2. There is **exactly one** save station and **one** crafting table in the whole dungeon.
   `GeneratedContent` is flat, so a glance down the hierarchy is enough.
3. **No fixture is drawn into a wall.** This is the check the clearance rule exists for. If
   any of them is half-buried, `FootprintCells` is under-measuring that prefab — look at
   whether its art is bigger than both its collider and its sprite bounds.
4. Walk into the hub and use the fixtures: `E` at the station saves, `E` at the table opens
   crafting, `E` at a chest opens it and **it is empty**. Put something in one, save, load,
   and it is still there.
5. **No enemies in the hub, and none waiting in the corridor just outside it.** One that
   walked in on its own during play is expected — the keep-out governs spawning, not
   patrol routes.
6. Regenerate with the same seed twice: the hub, its fixtures and their guids are identical.
7. Save in the hub, quit, load. The dungeon comes back with the hub in the same place — it
   is rebuilt from the seed like everything else, so a hub that moved means the seed did
   not round-trip, not that placement is random.

   Superseded by §11: this stage originally had the player start far from the centre and
   walk to the hub. The player now spawns *in* the hub instead — see the checklist there.

---

## Stage 11 — The player spawns in the hub, and `RoomKind.Start` is gone

`GU-0065` again. Stage 10 kept the old "farthest room from the map centre" role — renamed
nothing, called it `Start`, spawned the player there, and picked it as the origin the
difficulty curve is measured from. The player was meant to walk from Start to the hub.
This stage removes that walk: **the player now spawns in the hub itself.**

### Why this could not be a one-line change

Once the player's spawn point moves to the hub, `RoomKind.Start` has nothing left to do.
Its entire job was (a) being where the player begins and (b) being the origin of
`DepthFromStart`, the hop-count every difficulty number in `RoomContentSettings` reads.
Both moved to the hub. Keeping `Start` around regardless would tag some arbitrary
far-from-centre room as a *second* enemy-free, template-free, undecorated safe room — which
directly contradicts D9 from Stage 10, the decision that collapsed Camp into Hub for
exactly this reason: two safe roles is one too many once the player only starts in one
place.

So `RoomKind.Start` is deleted rather than kept dormant. `RoomKind` is now
`{ Hub, Normal, Treasure }`, and `Room.DepthFromStart` is renamed `Room.DepthFromHub` —
because leaving the old name on a value now measured from a different room would be wrong
in a way nobody reading the code would have reason to suspect.

**Nothing serialized these values**, which is what made the rename safe: rooms are rebuilt
from a seed every load (D6), never written to a save file, and grepping the project found
no authored `DungeonRoomTemplate` prefab whose `allowedKinds` array stores an enum int
against `Start`. If either had been true, this would have been a data migration, not a
rename.

### What changed

`RoomCorridorGenerator.AssignRoomRoles` no longer picks a farthest-from-centre room and
tags it `Start`. Instead:

- **The hub is the BFS origin.** `BreadthFirstDepths(adjacency, origin.Index)` now starts
  from the hub's index, not Start's. `layout.SpawnCell = origin.Center` follows the same
  origin, so the spawn point and the depth-zero room are, by construction, the same room —
  there is no longer a separate step that could disagree with itself.
- **Treasure is unchanged in spirit**: still the Normal room with the greatest
  `DepthFromHub`. It reads differently now only because "far from Start" and "far from the
  hub" used to point in *opposite* directions along the same walk (see below) and now don't.
- **A fallback for when the hub does not exist.** `PlaceHub` can return -1 on a map too
  small for even the minimum hub size (Stage 10's own defensive case, effectively
  unreachable once `LayoutParams.Sanitized` clamps `HubRoomSize` into the room-size range,
  but still handled). `FarthestFromCentre` reproduces the old Start-picking rule as a
  fallback origin in that case — tagged `Normal`, not `Hub`, since there is no hub to
  promote it to. This is not a design point, just somewhere to stand.

Every other file that read `RoomKind.Start` or `DepthFromStart` was updated to match:
`RoomInteriorDecorator` (now skips only `Hub`, its check for `Start` deleted rather than
kept unreachable), `DungeonRoomTemplate.Fits` (excludes `Hub` where it excluded `Start`),
`DungeonPopulator`'s room-content switch (the `case RoomKind.Start: break;` arm no longer
exists — there is no such kind to match), and every depth-scaling read in
`DungeonPopulator`/`RoomContentSettings` (enemy count, prop table, chest chance, corridor
ambush gating) now reads `DepthFromHub`.

### A real bug this pass found, not introduced by it

`DungeonPopulator.SpawnGuaranteedItems` — the pass that tops up critical items like lamp
fuel — built its candidate room list by excluding `RoomKind.Start` only. Since Stage 10
introduced `Hub` as a *third* kind, that filter had been silently wrong the whole time:
a `Hub` room was a valid drop candidate, meaning guaranteed-item loot could land on the hub
floor among the fixtures. Nothing caught it because the hub's `FreeCells` list is thin —
most of the floor is claimed by fixtures — so it would have taken an unlucky seed to
actually place something there, and nobody had looked. Now excludes `Hub` instead, which is
what the comment beside it always implied it should do.

### Why "start far, walk to the hub" was backwards for difficulty anyway

This is worth stating plainly because it explains why this is not just a cosmetic rename.
With Start as the depth origin and the player walking from Start toward the hub, the rooms
*right next to the hub* — the first ones a player exploring outward from the hub would ever
see, back when spawn was still at Start — sat at **high** `DepthFromStart`, because Start
was deliberately placed as far from the hub as the map allowed. Difficulty would have
ramped in the wrong direction the moment the spawn point moved to the hub without also
moving the depth origin: hard rooms beside the safe room, easy ones far away from it. Using
the hub as the origin for both **at once** is not two independent decisions that happened
to land together — moving the spawn point without moving the depth origin would have been
the actual bug.

### Not done

- **Treasure's distance from the hub is not floored.** A pathological layout could in
  principle place the Treasure room close to the hub if that happens to be the deepest
  Normal room available — nothing currently guards against a short, unsatisfying treasure
  hunt the way `HubKeepOut` guards against a spawn-adjacent ambush. Not observed in the 200-
  seed measurement below, but not structurally prevented either.
- **The fallback origin (`FarthestFromCentre`) is untested by seed**, since
  `LayoutParams.Sanitized` makes `hubIndex == -1` effectively unreachable through the public
  `Generate` API — reaching it would need calling private methods directly. Left as
  defensive code with a comment rather than a forced test.

### Measured, headlessly

Re-ran the Stage 10 harness with the origin change: 200 seeds at the shipped settings, plus
a fresh check that the player's actual spawn cell and the hub's centre are the same cell,
and that the hub always measures depth 0.

| | shipped 160×160 | tests 64×48 |
|---|---|---|
| Seeds | 200 | 200 |
| Validation failures | 0 | 0 |
| Unreachable cells | 0 | 0 |
| Not exactly one hub | 0 | 0 |
| Hub off map centre | 0 | 0 |
| Hub tagged Treasure | 0 | 0 |
| **Spawn cell ≠ hub centre** | **0** | **0** |
| **Hub depth ≠ 0** | **0** | **0** |
| Spacing violations | 0 | 0 |

Every number that was clean before the change stayed clean, and the two new ones — spawn
and depth both anchored to the hub — are exactly zero mismatches across 400 seeds. The
fixture-placement measurements from Stage 10 are unaffected: nothing about where fixtures
go depends on which room the player starts in, only on which room the hub is, and that
did not change.

### In-editor checklist for this stage

1. Generate a dungeon and enter Play mode. **The player starts standing inside the hub**,
   next to the save station and crafting table, not walking in from elsewhere.
2. Look at a room several corridors away from the hub: it should feel harder than one just
   outside the hub — more enemies, higher chest chance. That gradient now runs outward from
   the hub in every direction, not from one edge of the map toward the centre.
3. Find the Treasure room (deepest from the hub) and confirm it is a genuine walk away, not
   one hop from spawn.
4. Regenerate with the same seed twice: the player lands on the exact same cell both times.
5. Save immediately after spawning, quit, load: the player comes back standing in the hub,
   not at the map's old Start position.

---

## Stage 12 — GameManager's spawn point drifts out of sync with the hub

`GU-0065` again. Stage 11 made the player spawn in the hub — inside `DungeonBuilder.Build()`,
via `MovePlayerToSpawn`. That fixed the *live-generation* path. It did not fix what the
Dungeon scene actually runs.

### The scene this project actually plays

The Dungeon scene is authored by baking (Stage 7): generate once in the editor, save the
scene, and at runtime `buildOnStart` is off — `DungeonBuilder.Build()` never runs during a
normal Play session, because the geometry and content are already sitting in the scene file.
Confirmed by reading it directly: `Dungeon.unity`'s `DungeonBuilder` has `buildOnStart: 0`.

The only thing that positions the player at game start in that scene is
`GameManager.TeleportPlayerToSpawn()`, called from `Awake()` because `startGameOnAwake: 1`.
It reads one Transform — a plain empty GameObject named `PlayerSpawnPoint`, wired to
`GameManager.playerSpawnPoint` — and moves the player there. **`DungeonBuilder.Build()` had
never touched that Transform.** It only ever moved the live player object during the bake
session; the marker a later Play session actually reads was left wherever it happened to
be — in the checked-in scene, `(112.9, 114.7, 0)`, whatever position it held before the hub
existed. Stage 11's fix never had a chance to run in this scene, and the player kept
teleporting to a stale marker with no connection to the generated dungeon.

### The fix

`DungeonBuilder` gained an optional field, `playerSpawnMarker`. `MovePlayerToSpawn` now
sets its position to the hub's centre alongside the live player's, every time it runs —
including in edit mode while baking, with no live player present, since that is exactly
when the marker a later Play session will read needs to be correct.

Wiring it by hand in every dungeon scene is the kind of step that gets skipped, so
`DungeonSceneSetup.WirePlayerSpawnMarker` does it automatically: **Tools ▸ Dungeon ▸ Setup
Scene Tilemaps** now finds this scene's `GameManager`, reads whatever Transform its own
`playerSpawnPoint` field already points at, and wires the same Transform into the builder.
Neither component needs to know about the other's existence beyond that one shared
reference — `GameManager` still just teleports to a Transform, unchanged, which is also why
this costs nothing for the hand-built scenes (`Main`, `Demo`) that have no `DungeonBuilder`
to wire it to.

`Dungeon.unity` had the reference added directly. **Only that scene** — per direction,
scene edits in this pass are scoped to `Dungeon.unity` and nothing else, even where another
scene (`DungeonBN.unity`) is set up identically and would take the same one-line fix.
Whoever owns that scene can pick up the same reference (`GameManager.playerSpawnPoint`
already points at a `PlayerSpawnPoint` object there too) by hand, or by running **Tools ▸
Dungeon ▸ Setup Scene Tilemaps** in it — the wiring code is scene-agnostic and works
wherever it is run; it is only my own direct scene edits that stay confined to `Dungeon.unity`.

### What this does not fix by itself

**The wiring is fixed; the position baked into `Dungeon.unity` right now is not.** I can
edit the scene's YAML safely to add a field reference — that is a mechanical, unambiguous
change. I cannot safely hand-compute where `PlayerSpawnPoint` *should* sit: `CellCenter`
composes the dungeon's grid origin with the `Grid` transform's own position **and a 2×
local scale**, and getting that arithmetic wrong would silently leave the marker at a
different wrong position instead of visibly failing. That is worse than leaving it stale
and saying so.

**Required, once, in the editor:** open `Dungeon.unity`, select `DungeonRoot`, right-click
the `DungeonBuilder` header ▸ **Generate (current seed)**, then save the scene. That call
was always going to move the live player and repaint the geometry; it will now also snap
`PlayerSpawnPoint` to the hub, and saving the scene keeps it there for every future Play
session.

`DungeonBN.unity` was left alone entirely (see "Scope" above) rather than fixed to the same
standard as `Dungeon.unity` — worth noting it would not have needed the manual regenerate
step even if it had been touched, since its `buildOnStart` is already `1` and `Build()` runs
at the start of every session there regardless.

### Scope

Scene edits in Stages 12 onward are confined to `Dungeon.unity` only, on explicit
direction — earlier drafts of this stage and the two after it also hand-edited
`DungeonBN.unity` to the same values; those edits were reverted. The *code* fixes
(`DungeonBuilder`, `PathfindingGrid`, `DungeonSceneSetup`) are not scene-specific and apply
equally to any scene that uses them; only the direct, by-hand data edits to a checked-in
scene file are scoped down. `DungeonBN.unity` is therefore still carrying whatever stale
values it had before this work, for whoever owns it to pick up separately.

### Not done

- **No test covers this.** The bug was entirely about a *baked scene's* stored state
  disagreeing with a *live* generator run — not something the layout assembly's headless
  harness can see, since it never touches a `Transform` or a scene file. Catching a class of
  bug like this would need an integration test that loads the actual scene, which is outside
  what runs headlessly today.
- **`GameManager` still has no idea a dungeon exists.** This fix keeps two independent
  systems' state in sync by convention (`DungeonBuilder` writes the Transform
  `GameManager` reads) rather than by `GameManager` asking `DungeonBuilder` directly. That
  is deliberate — `GameManager` works identically for hand-built scenes this way — but it
  means any *third* system that also wants "where does the player start" has the same
  trap waiting for it: read the marker, not the layout, or duplicate this same drift.

### In-editor checklist for this stage

1. Open `Dungeon.unity`. **Before regenerating**, note `PlayerSpawnPoint`'s position in the
   inspector — expect it to disagree with where the hub visibly sits in the Scene view.
2. Select `DungeonRoot` ▸ `DungeonBuilder` context menu ▸ **Generate (current seed)**.
   `PlayerSpawnPoint` should jump to the hub's centre. If it does not move at all, the
   `playerSpawnMarker` reference did not survive whatever edited the scene — reselect
   `Tools ▸ Dungeon ▸ Setup Scene Tilemaps` to re-wire it.
3. Save the scene.
4. Enter Play mode from a **cold launch of this scene** (not by pressing Play while already
   in it from a previous session) — `startGameOnAwake` fires once at `Awake`, so this is the
   path the original bug lived on. The player should appear standing in the hub, not at the
   map's edge or in solid rock.

---

## Stage 13 — The pathfinding grid covered a quarter of the map

Reported directly: the navigation grid was not covering the whole generated dungeon.

### Root cause

`DungeonPainter.CellSize` read `Grid.cellSize.x` — the Tilemap `Grid` component's own
local-space cell size — and handed it straight to `PathfindingGrid.Configure`. That value
excludes the GameObject's transform scale; `Grid.CellToWorld`/`WorldToCell` apply it, but
reading `cellSize` off the component directly does not, and nothing about the property
name warns that it is lying about world-space size the moment the transform isn't 1:1.

`Dungeon.unity`'s `DungeonRoot` is scaled **2×** (`m_LocalScale: {2, 2, 2}`, confirmed as
the root transform itself — `m_Father: {fileID: 0}`, so no parent contributes further
scale). `Grid.cellSize.x` is `1`, so every tile is actually 2 world units wide, and
`DungeonPainter.CellSize` was reporting half that. `PathfindingGrid.Configure(origin, width,
height)` never took a cell size parameter at all — it left whatever was already serialized
on the component untouched, which was `0.68`, a value with no connection to either the
correct one (2) or the wrong one this bug would have computed (1). Read directly out of the
checked-in scene: `origin` and `width`/`height` (160×160) were already correct — only
`cellSize` was stale.

The consequence compounds through simple multiplication. A `PathfindingGrid` samples
`width × cellSize` world units across; at the shipped `160 × 0.68 = 108.8`, against the
dungeon's actual `160 × 2 = 320`. The sampled area is **(108.8/320)² ≈ 11.6%** of the map,
and because sampling starts at `origin` and walks outward one (wrong-sized) cell at a time,
that 11.6% is not spread across the map — it is packed into the corner nearest the origin.
Pathfinding requests for anything past there would land in space the grid never sampled and
report unwalkable, in a dungeon that is otherwise completely fine.

### The fix

Two changes, so the value is derived once and threaded through rather than fixed in two
disconnected places:

- **`DungeonPainter.CellSize`** now multiplies by `Grid.transform.lossyScale.x`, so it
  reports the true world-space cell size regardless of what the root's transform does.
  `CellCenter` had the identical bug one line down — it added half of the *unscaled*
  `Grid.cellSize` to an already-correctly-scaled corner from `Grid.CellToWorld`, so every
  spawn position was off-centre by a quarter of the real cell width. Fixed the same way,
  per axis rather than reusing the (uniform-only) `CellSize` property, so a non-square cell
  remains representable even though nothing here currently authors one.
- **`PathfindingGrid.Configure`** gained a `float gridCellSize` parameter and now writes it
  into the private `cellSize` field alongside origin/width/height, rejecting zero or
  negative like the existing size checks. Leaving `cellSize` untouched was the second half
  of the bug: even a caller that passed the right value nowhere had to actually store it.
  `DungeonBuilder.Build()` now passes `painter.CellSize` through.
- **`DungeonSceneSetup.WarnOnCellSizeMismatch`** compared the Tilemap `Grid`'s raw
  (unscaled) `cellSize` against `PathfindingGrid.CellSize` — which, after this fix, is
  correctly expressed in world units, so the old comparison would flag a scaled
  `DungeonRoot` as a mismatch even when everything is fine. Now compares both sides in
  world units (`grid.cellSize * grid.transform.lossyScale`). Left as an early heads-up
  rather than removed: `Configure` self-corrects the grid on every build regardless, so a
  mismatch here can no longer leave anything broken — it can only warn before the first
  Generate.

### Why `Dungeon.unity` needed a direct edit, not just the code fix

`Dungeon.unity` has `PathfindingGrid.cellSize` serialized as `0.68`. `Awake()` calls
`BuildGrid()` unconditionally using whatever is currently serialized — `Configure()` only
overwrites it when `DungeonBuilder.Build()` actually runs, which for `Dungeon.unity`
(`buildOnStart: 0`, authored by baking) is *never*, at runtime, outside a save load. The
code fix alone would have left that scene sampling the wrong 26.9% width forever, exactly
the same shape of gap Stage 12 found in the spawn marker.

Unlike the spawn marker, this one was safe to hand-fix directly: `origin` (`{-46.1,
-44.7}`) already matched `Grid.CellToWorld(0,0,0)` — no computation needed — and the
correct `cellSize` is `Grid.cellSize.x (1) × the root's own lossyScale.x (2) = 2`, read
straight off two values already in the file rather than derived through an opaque transform
chain. `PathfindingGrid.cellSize` was changed from `0.68` to `2` directly.

Scene edits stay confined to `Dungeon.unity` per the scope note in Stage 12 — `DungeonBN.unity`
was not touched, and still carries its own stale `0.68`. It would self-correct on its own
next Play session regardless, since its `buildOnStart` is `1`, but nothing here made that
happen sooner.

### Not done

- **No regenerate was required for this one** — unlike Stage 12, the fix is complete as
  checked in. Regenerating is still worth doing to pick up Stage 12's spawn-marker sync in
  the same pass, not because this stage left anything pending.
- **The 2× scale on `DungeonRoot` itself was not investigated or changed.** It is very
  possibly a mistake in its own right — this project's tile art is imported at a PPU that
  assumes one tile is one world unit (`DungeonSceneSetup.TilePixels`'s own comment says so
  explicitly), and a 2×-scaled root would render every tile at double that size on screen,
  which is a different, visible symptom nobody has reported. Left alone because it is a
  rendering/framing question this fix does not need answered — the code now derives the
  correct pathfinding coverage from whatever scale the root actually has, so nothing here
  depends on that scale being 1, 2, or anything else. Worth a second look if the dungeon
  looks larger on screen than the camera framing in `GENERATION_NOTES.md` §7 describes.

### In-editor checklist for this stage

1. Open `Dungeon.unity` and select the `PathfindingGrid` object. Turn `gizmoOpacity` up from
   0 temporarily and enter Play (or use the Scene view if the grid was already built) — the
   coloured cells should extend across the **entire** painted dungeon, corner to corner, not
   stop a third of the way in.
2. Walk (or send an enemy) to a room in the far corner of the map, away from the origin.
   Before this fix that area had no pathfinding coverage at all; confirm A* finds a route
   there now.
3. Regenerate (`DungeonBuilder` context menu ▸ Generate) and confirm the grid's `cellSize`
   in the inspector updates to match the tilemap's real spacing — it should equal
   `painter.CellSize`, not silently keep whatever was there before.
4. If `DungeonRoot`'s scale is ever changed on purpose, no dungeon-side code needs touching
   — `DungeonPainter.CellSize` and everything downstream of it reads the transform live.

---

## Stage 14 — The pathfinding gizmo was invisible

Reported directly, right after Stage 13: turning `gizmoOpacity` up still showed nothing.
Three causes, layered — the first two were real but not sufficient on their own, which is
why fixing them alone did not make the gizmo appear.

### Cause 1 (real, not sufficient alone) — `walkableColor`'s alpha was 0.0275, not 0.3

`Dungeon.unity` had `walkableColor: {r: 0, g: 1, b: 0, a: 0.02745098}`. `OnDrawGizmos`
multiplies that alpha by `gizmoOpacity` before drawing, so even at full `gizmoOpacity`
(`1`) a walkable cell rendered at 2.75% opacity — visually indistinguishable from nothing,
especially against the near-black `DarknessOverlay` this project renders with. The
component's own C# default is `0.3`; whatever set the scene value to a tenth of that was a
scene edit, not a code path. Reset to `0.3`.

### Cause 2 (real, not sufficient alone) — the cap always drew the same corner

`maxGizmoCells` was `5000` in `Dungeon.unity`. Stage 13 established the map is
160×160 = 25 600 cells. `OnDrawGizmos` drew in row-major order — `y` outer loop, `x` inner
— incrementing a counter and returning the instant it hit the cap. With a 5000-cell budget
on a 25 600-cell grid, that is **the bottom ~31 rows of a 160-row map, full stop** — every
row above `y ≈ 31` got zero gizmo cubes, unconditionally, regardless of `gizmoOpacity` or
color. The hub sits at the map's centre, around `y ≈ 80`. Whoever was looking at the hub
was looking at a region the old code could never draw, capped or not.

Fixed structurally, not just by raising the number: `OnDrawGizmos` now walks the flat cell
index by a **stride** — `Mathf.CeilToInt(cellCount / (float)maxGizmoCells)` — instead of a
contiguous run from the start. A grid under the cap draws at stride 1 (everything, as
before). A grid over the cap draws every *n*th cell across the **entire** map instead of
every cell across **part** of it. Verified arithmetically (not just by inspection) for
25 600 cells at a 30 000 cap (stride 1, all drawn), 250 000 cells at the same cap (stride 9,
≈27 778 drawn, under the cap) — the drawn count never exceeds the cap in either case. The
default cap was also raised, `5000 → 30000`, comfortably above the shipped map's 25 600
cells while staying well under the 250 000-cell stall threshold the original comment
already established.

### Cause 3, the one that actually explains "still nothing" — the grid was never built

Fixing 1 and 2 and still seeing nothing is what exposed this one. `_walkable` is a private,
**non-serialized** array, populated only by `BuildGrid()`, called only from `Awake()` or
`Configure()`. `PathfindingGrid` is a plain `MonoBehaviour`: `Awake()` does not run merely
from opening a scene in the editor, only from entering Play — and `Dungeon.unity` runs with
`buildOnStart: 0`, so `Configure()` (called from `DungeonBuilder.Build()`) does not run
either unless someone explicitly triggers Generate. Opening the scene and looking, without
doing either of those first, means `_walkable == null`, and `OnDrawGizmos` early-returns on
exactly that check — no color or cap setting anywhere could have mattered.

This is the identical shape of problem `DungeonPopulator` already had and already fixed, in
Stage 7: a plain `MonoBehaviour`'s edit-mode lifecycle does not include `Awake`, so anything
that has to be ready the moment a baked scene is opened needs `[ExecuteAlways]`. Added to
`PathfindingGrid` for the same reason. With it, `Awake()` — and therefore `BuildGrid()` —
runs when the scene loads in the editor, not only when Play starts, so the grid has data
and the gizmo has something to draw without any manual step.

### Why `Dungeon.unity` needed a direct edit for causes 1 and 2, but not cause 3

`gizmoOpacity`, `maxGizmoCells` and `walkableColor` are `[SerializeField]` fields with no
runtime code path that ever corrects them — pure authoring data, read as-is, so a mistaken
scene value stays mistaken until someone reads and fixes it directly. `Dungeon.unity`'s
`walkableColor` and `maxGizmoCells` were changed directly, and nowhere else — per the scope
note in Stage 12, `DungeonBN.unity` was left untouched and still carries its own stale
`0.0275` alpha and (missing, so code-default) `maxGizmoCells`.

Cause 3 is different in kind: `[ExecuteAlways]` is a code change, not scene data, so it
applies to *every* scene using `PathfindingGrid` the moment the script recompiles —
including `DungeonBN.unity`, without touching that scene's file at all. That is not a scope
violation of the "only `Dungeon.unity`" instruction; a code fix was never scene-scoped to
begin with, the same way Stage 13's `Configure`/`CellSize` fixes already apply everywhere
without needing a per-scene edit for the *logic* half of that bug.

### Not done

- **The stride sampling is still uniform, not camera-relative.** A very large map at a
  tight cap draws a sparse dusting across the whole thing rather than a dense view of
  wherever the Scene view camera happens to be pointed. Fine at the sizes this project
  actually uses (stride 1 up to 30 000 cells, comfortably past the shipped 25 600), and
  not worth the added complexity of reading `SceneView.currentDrawingSceneView` unless a
  future map size makes it necessary.
- **`DungeonBN.unity`'s `walkableColor` is still stale at 2.75% alpha**, per the scope note.
  Causes 2 and 3 are both code fixes (`OnDrawGizmos`'s stride logic, `[ExecuteAlways]`), so
  that scene gets those for free without a scene edit — only its low `walkableColor` alpha,
  which is scene data, remains unfixed there.

### In-editor checklist for this stage

1. Open `Dungeon.unity` **without entering Play and without running Generate**. The gizmo
   should already show something, immediately — this is the check for cause 3 specifically,
   and the one that would have looked identical to "still broken" before this fix regardless
   of how correct causes 1 and 2's fixes were.
2. Look at the hub, at the map's centre — not just near the origin corner. Gizmo cubes
   should cover it exactly as densely as anywhere else on the map.
3. Green (walkable) cells should be faintly but clearly visible, not a barely-perceptible
   tint.
4. Confirm nothing looks sparse at the shipped map size: 25 600 cells is under the 30 000
   cap, so every cell should be drawn (stride 1), not a dusting.

---

## Stage 15 — Blocking props, too many pillars, and a door alignment regression

Three reports at once: props spawning somewhere that blocks passage, too many pillars, and
doors misaligned since a recent change. Three separate causes, one each.

### 1 — The chokepoint check's radius never scaled with the object's own size

`TryTakeAnchor` and `TryTakeSpaced` both call `DungeonLayout.NearAnyChokepoint(cell, radius)`
to reject a placement too close to a cell whose removal would split the dungeon. The radius
was hardcoded to `1` in every call, regardless of how big the object actually is —
`TryTakeSpaced` even had the object's real footprint sitting in its own parameter list
(`spacing`) and still checked chokepoints with a flat `1`. A chokepoint marks a single cell;
an object wider than the buffer around it can still physically reach one even while its
*anchor* cell passes the check.

Fixed by scaling the radius to the object's own footprint — `Mathf.CeilToInt(footprint / 2f)`,
the same half-footprint clearance `SpawnFixture` already uses for the hub's furniture — in
all three places that call `NearAnyChokepoint`: `TryTakeAnchor`, `TryTakeSpaced`, and
`TryTakeFixtureCell` (the hub had the identical flat-`1` bug).

**Measured** (200 seeds, shipped settings): simulating a footprint-5 object with the old
flat radius of 1 let **49 423 of 643 527** accepted placements have a footprint that still
touched a chokepoint. With the fix, across the same run and separately at footprint 1 and 3
(`Table.prefab`'s actual size), **zero**. Footprint 3 alone happened not to show the bug in
this measurement — its floor-division half-width (1) coincides with the old flat radius —
which is exactly why the fix reasons from the object's real size rather than from what one
specific prefab's dimensions happen to round to.

### 2 — Prop clusters never checked against each other, or against chests

Every cluster's spacing was tracked in a `placed` list created fresh inside the `while`
loop in `SpawnProps` and discarded once that cluster finished. A barrel cluster and a table
cluster placed one after another in the same room had no way to know about each other —
each only avoided overlapping *its own* members. Chests, spawned earlier in the same room by
`SpawnChests`, were invisible to props for the same reason: different call, different local
list.

Fixed with one list, `roomOccupied`, created once per room in `SpawnRoomContent` and threaded
through `SpawnChests` then `SpawnProps` — every chest and every prop (anchor and cluster
member alike) is added to it, and `TryTakeAnchor`/`TryTakeSpaced` now check it in addition to
their existing chokepoint and same-cluster checks. `IsClearOfFixtures`, the pairwise
footprint-spacing check written for the hub's own furniture in Stage 10, is exactly this
check already — renamed `IsClearOfPlaced` and reused rather than duplicated.

**Measured** (200 seeds, 3000 rooms, alternating barrel/table clusters mirroring
`SpawnProps`'s own loop): **9210 overlapping pairs** without the shared list, **0** with it —
at a cost of 310 fewer props placed out of ~36 000 (0.9%), the ones that would only have fit
by overlapping something already there.

### 3 — Door alignment regressed from the Stage 13 `CellCenter` fix

Reported as "doors are too small, there's a gap" right after Stage 13 shipped — the
timing is the diagnostic clue, not a coincidence.

`SpawnDoors`'s rotated-door branch (used wherever a corridor runs north–south through the
doorway) nudges the spawned door with a hardcoded `pos.x += 1f`, to re-centre it after a
90-degree turn swings its off-centre pivot sideways. That constant was necessarily tuned by
eye against whatever `DungeonPainter.CellCenter` returned *at the time* — and Stage 13
changed what that is. Before Stage 13, `CellCenter` offset a cell's corner by half of the
Tilemap `Grid` component's own **unscaled** cell size — always `1`, by this project's "one
tile is one world unit" PPU convention (`DungeonSceneSetup.TilePixels`) — giving `0.5`
world units, regardless of the `DungeonRoot` transform's scale. After Stage 13, it correctly
offsets by half the **true world-space** cell size, `painter.CellSize * 0.5`, which is `1.0`
on the shipped 2×-scaled `DungeonRoot`. Every spawn position moved by that `0.5`-unit delta
in both axes — imperceptible for a barrel or an enemy, precise enough to open a visible gap
at a doorway sized to the cell.

First fix attempt subtracted that same, exactly-known delta from the rotated branch's
compensation (`pos.x += 1f - (builder.CellSize * 0.5f - 0.5f)`) and left the unrotated
(`else`) branch untouched, on the reasoning that it spawns straight at `CellCenter(cell)`
with no hand-tuned hack to have regressed. That reasoning was wrong in a way only the editor
could show: **confirmed by screenshot on an unrotated (east–west) door** — FOV leaked through
a sliver at the top of the closed door, in the shipped 2×-scaled hub room. So the gap was
never really about the Stage 13 delta specifically; it is about `Door_System`'s origin (what
`SpawnDoors` positions at `CellCenter`) not being the centre of the closed door's own
collider in the first place. The prefab's hinge pivot (`Door_Pivot`, at local `(0.25, -0.25)`
under `Door_System`) has to sit off-centre for the swing-open rotation to look right, but that
same offset means the *closed* door's collider centre sits about a quarter-cell away from
`Door_System`'s origin — in **both** axes, regardless of rotation. The old `pos.x += 1f`
hack partially masked this for the rotated branch by accident (it was tuned by eye against a
door that already had this offset baked in); the unrotated branch was never covering it at
all, hence the gap the user found.

**Replaced the whole position hack** with `DungeonPopulator.CenterOnCollider`: after setting
the door's rotation, work out where the door's collider actually is and shift the door by the
difference between that and the intended cell centre. This self-corrects for the pivot offset
at any rotation angle without needing to know the prefab's internal geometry or hand-tune a
constant against it — the `legacyCenterOffsetDelta` arithmetic is gone from `SpawnDoors`
entirely.

#### The first attempt at that used `Collider2D.bounds`, and put every door off the map

Worth recording, because the failure is a general Unity trap and the symptom was spectacular
rather than subtle. `Collider2D.bounds` is **physics**-backed. `Physics2D.autoSyncTransforms`
is off by default, so a collider's bounds do not reflect a transform written earlier in the
same frame until the next physics step — and this code runs during **edit-mode baking**,
where no physics step ever comes. The read returned an unsynced centre of roughly the world
origin, making the correction `cellCenter - 0`, which lands each door at *twice* its map
coordinate. Every door left the map.

The replacement takes the collider centre from the transform hierarchy instead —
`doorCollider.transform.TransformPoint(doorCollider.offset)` — which is pure matrix maths on
transforms already written, correct the instant the rotation is set, with no physics and no
sync call. Reasoning it through against `Door_System.prefab`'s hierarchy (`Door_Pivot` at
local `(0.25, -0.25)`, `Door_Visual` at `(0, 0.5)` scaled `0.2` in x, box offset ≈ `0`) the
correction is **(-0.249, -0.250)**, magnitude 0.35 — a quarter cell, and that `+0.25` in y is
exactly the sliver the screenshot showed above the closed door. `CenterOnCollider` also now
refuses any correction larger than one cell, logging instead: a misread centre leaves the
door visibly at its doorway where the fault can be seen, rather than silently on the far side
of the map.

Compiles clean (Roslyn check against the full project, 0 errors). **Still needs the in-editor
walk documented below** — the arithmetic above is derived, not observed, and the previous
attempt is a standing reminder that derivation alone did not catch a physics-lifecycle bug.

### Not done

- **The gap Table.prefab actually posed, per the footprint-3 measurement above, was already
  geometrically zero even before this fix** — its footprint happens to round such that a
  flat radius of 1 was sufficient. The fix is still correct and still needed: it removes a
  dependency on that coincidence for every other footprint, current or future.
- **No headless check exists for the door offset**, and cannot — it is a rendering-alignment
  question about where a sprite's pivot sits relative to its collider, which the layout
  assembly's engine-free test harness has no way to observe. Confirming this one needs the
  editor.
- **Non-blocking props still get no cross-cluster spacing check against `roomOccupied`
  through the wall-bias search path's fallback**, only through the two loops that do run —
  in practice moot, since every prop registered today (`prop.barrel`, `prop.table`) is
  blocking, but worth knowing if a genuinely non-blocking prop is ever added.

### In-editor checklist for this stage

1. Regenerate a dungeon and look through several rooms for barrels or tables standing
   inside a wall, inside a pillar, or inside each other. None should.
2. Compare pillar/rubble density against a memory of the previous default: rooms should
   read as noticeably less cluttered, without going back to bare rectangles — `interiorDensity`
   moved 0.5 → 0.25 in the same pass (Stage 6's own measured table: 0.736 → 0.769 mean room
   visibility, i.e. rooms give away more of themselves at a glance, which is the intended
   trade for fewer pillars).
3. **Walk up to both a rotated door** (north–south corridor) **and an unrotated door**
   (east–west corridor, the one the screenshot showed leaking) and check each fully seals
   the opening — no FOV sliver past any edge when closed. `CenterOnCollider` treats both
   branches the same way now, so both need checking; if either still shows a gap, note which
   edge and how wide, since that tells me whether `Collider2D.bounds` disagreed with what
   is actually visible (e.g. the collider not matching the sprite) rather than a centring
   problem.

---

## Stage 16 — The real floor art, and paper on the ground

Two assets were sitting in `Assets/Art` unused: `FURNITURE_pngy_podloga.png`, a cobbled floor,
and `FURNITURE_pngy_papier.png`, loose sheets of paper. Both now feed the generator. Neither is
in a form a tilemap can consume directly, and the two problems are opposite ones.

### 1 — The floor is a slab, not a tile

`podloga` is one 3045×1913 painting of a stretch of cobbles, with torn edges and a
semi-transparent fringe. It is not seamless and it is not a tile. Cutting independent per-cell
variants out of it — the shape the existing `floorTiles` array expects — puts a hard
discontinuity at **every** cell border, because stones are sliced mid-stone on all four sides.
More variants do not help: the mismatch is at every edge rather than occasionally, so the floor
reads as a grid of patches no matter how many patches there are.

Cut as a **mosaic** instead. One square region is cut into a 4×4 block of pieces, and
`DungeonPainter.PickFloor` selects by position — `(x mod 4, y mod 4)` — rather than by seed.
The pieces were adjacent in the source, so they line up with each other exactly: stone runs
unbroken across every border inside a block, and only the block boundary repeats, every 4
cells instead of every 1. `floorMosaicSize` on the painter switches between this and the old
per-cell scatter, and the setup writes it, so a project without the art still gets the
generated placeholders and the old behaviour.

**Which square to cut matters more than expected**, and the first two attempts were both
visibly wrong when rendered out and looked at:

- *Nearest the middle of the image* (the obvious choice, to stay away from the torn edges)
  landed on a region straddling the painting's lighting gradient. One side of the block was
  brighter than the other, so every block boundary became a visible step — a grid of faint
  rectangles.
- Scoring against moss then failed to find any, because the test was the intuitive one: green
  above **both** red and blue. This moss is olive. Measured, its green runs only ~10 above red
  — under the threshold — but 59 to 65 above blue, against 8 for stone. Green-minus-blue
  separates them cleanly; green-against-both matches none of it.

The search now scores every fully opaque candidate on moss content (weighted heavily) plus the
tone mismatch between opposite edges, since those are the edges that end up adjacent when the
block repeats. On the shipped art that picks (1472, 576) at 1024², downsampled 2× to a 512px
block: **0.319% moss and 1.86 edge tone**, against 0.000%-detected/uneven for the first attempt.

**Verified by rendering it out and looking at it** — the slicing was reimplemented outside Unity
and the block tiled 3×3 into a 12×12-cell image, which is the only way to see a seam or a repeat
at all. Per-cell seams: gone. Tonal step at block boundaries: gone.

### 2 — The paper is several sheets on one canvas

`papier` holds four sheets, three of them overlapping into one clump. The first pass took each
connected clump as a decal, giving a lone sheet and a pile of three. That is the cheap reading
of the image and it is wrong twice over: every pile then lands at the one angle and arrangement
the artist happened to draw, and the sheets inside it cannot be told apart or spread out.

Splitting the clump into its three sheets is not the fix either — an overlapped sheet is drawn
with a bite taken out of it by the one on top, so extracting it yields a notched shape rather
than a sheet, and no amount of component-tracing recovers pixels that were never painted.

So **exactly one sheet is taken** — the isolated one — and every decal is *composed* from it:
each sheet placed at its own angle, its own size jitter and its own offset, with a group being
simply a tile carrying several of them. `PaperSheetCounts` (`{1,1,1,2,2,3}`) sets both the
number of variants and the mix, since the painter picks between decals uniformly: mostly lone
dropped pages, with a couple of small scatters. Rotation is a full 0–360°, so no two decals
share an angle.

The one sheet is identified as the **smallest** group, since a clump contains two or more
sheets and is therefore larger than any one of them. That holds for this art and for any redraw
keeping at least one sheet clear; if a redraw ever overlapped every sheet, the code warns rather
than silently cutting up a pile.

Two things here are easy to get wrong and are worth not rediscovering:

- **Sheets are drawn destination-to-source, not source-to-destination.** Walking the sheet and
  scattering its pixels forward leaves unwritten gaps wherever rounding sends two source pixels
  to the same destination, which on a rotated sprite is a dusting of pinholes across it.
- **Sampling and blending are both premultiplied.** Interpolating straight RGB across the
  sheet's edge drags in the colour of fully transparent pixels — black — and rings every page
  in a dark fringe. Same reason the floor's resample is premultiplied, except the floor is
  opaque throughout and never shows it, while paper is nearly all edge.

Decals go into the existing decal tilemap — no collider, drawn over the floor and under the
walls — which is what paper on a floor should be, and means `decalChance` and `decalWallBias`
already place it, litter collecting at room edges.

**Sheets cannot spill out of their cell**, which would show as paper sliced in half at a cell
border. Worst case, at 45° and maximum jitter: a half-diagonal of
`0.5 × (0.42 × 128 ÷ 255 × 1.12) × √(211² + 255²) = 39.1px`, plus a maximum centre offset of
`0.17 × 128 = 21.8px`, is 60.9 against the tile's 64px half-width. That is a bound on the
geometry, not a property of the particular random numbers that came out.

### Also

Tiles cut from art are 128px with their PPU set to match, so they still cover exactly one world
unit and sit on the same tilemap as the 32px generated placeholders without either being scaled
to the other. They also import bilinear rather than point: point filtering keeps the
placeholders' deliberate pixel grain crisp, but on downsampled painted art it aliases stonework
into shimmering speckle as the camera moves.

### Not done

- **The moss tuft still repeats every 4 cells.** 0.319% is the *minimum* across all 126
  candidate squares — the art has moss scattered throughout, so no crop avoids it. It is the
  one landmark in an otherwise uniform texture, and the eye finds it in a flat lit preview.
  Whether it matters in game is a genuinely different question, because the dungeon is rendered
  under a vision cone with everything else dark, and a 4-cell repeat is rarely all on screen
  and lit at once. **This is the thing to judge in the editor.** If it does read as a pattern,
  the next step is several mosaic blocks chosen per block-position from the seed, which
  dissolves the grid at the cost of reintroducing a seam at block boundaries only.
- **The old `FloorTile`/`FloorTileB`/`FloorTileC` assets are left in place**, unused while the
  art is present. Deleting assets is not something a setup re-run should do, and they are the
  fallback if the art ever goes missing.
- **Papers are decoration, not readable items.** They are decals, so they cannot be picked up
  or interacted with. Worth knowing if notes-as-lore is ever wanted — that would be a prop or
  an interactable, not this.
- **Only one of the four drawn sheets is used.** The other three are unrecoverable as
  individual sheets, being drawn overlapped (see above), so every page in the dungeon is the
  same sheet at a different angle and size. Not visible in practice — sheets of paper are the
  same shape as each other — but if genuinely different pages are wanted, the cheapest route is
  the artist drawing the four sheets *separated* on the canvas, at which point this code picks
  all four up with no changes beyond taking every group instead of the smallest.
- **Paper is now 6 of 10 decal variants**, up from 2 of 6, because the mix is expressed as the
  number of variants. If that reads as too much litter, the fix is `PaperSheetCounts`, not
  `decalChance` — the latter changes how much of everything there is.

### 3 — Neither the floor nor the paper was actually wired into either scene

Reported as "paper 0 looks right, I never see paper 1 anywhere" — asked about the tiles
directly, which was the tell, because both tiles are fine: `DecalPaperA.png` and
`DecalPaperB.png` both render correctly on their own (checked by eye). The bug was never in the
art or the composite code. It was that **neither scene's `DungeonPainter` had ever been told the
new tiles exist.**

`Setup Scene Tilemaps` cuts the art and calls `WireGenerator`, which is the only place that
writes `floorTiles`, `floorMosaicSize` and `decalTiles` onto the painter component. Checking
`Dungeon.unity` and `DungeonBN.unity` directly (`grep decalTiles`) showed both painters still
serialized with exactly the four original crack/stain/grit decals and the three original
`FloorTile`/`B`/`C` placeholders — the mosaic and every paper tile were present as assets on
disk and referenced by nothing in either scene. `RegenerateTiles` doesn't touch scene wiring
either, by design (see its own doc comment) — it only recuts the PNGs, so a floor or decal set
that was never wired in the first place stays never wired no matter how many times it runs.

**A dungeon regenerated in either scene was never going to show *any* paper**, not "paper 1
specifically" — the report undersold the bug relative to what was actually broken. It also means
the whole of Stage 16's Section 1 (the floor mosaic) has never appeared in either scene until
now, on top of the paper.

Fixed by hand-editing both scenes' serialized `decalTiles` and `floorTiles` arrays to the full
sets (10 and 16 entries respectively, by GUID) and adding `floorMosaicSize: 4`, rather than
running the editor tool — there is no Unity instance available to run it. This is the same
outcome `Setup Scene Tilemaps` would have produced for these two fields, done directly because
the tool couldn't be. Everything else `WireGenerator` sets (wall tiles, doorway, pillar, rubble,
the builder and populator references) was already correct and untouched.

**Not verified in the editor** — same limitation as the rest of this stage. What can be
verified without it: field counts match (`floorTiles` 16, `decalTiles` 10) and every GUID in
both edits matches an existing `.asset.meta` in `Assets/Generation/Tiles`, checked by grep
against both scenes after the edit.

### In-editor checklist for this stage

1. Open `Dungeon.unity` (or `DungeonBN.unity`) and select the `DungeonRoot`'s `DungeonPainter`.
   Confirm `Floor Tiles` shows 16 entries and `Decal Tiles` shows 10, with `Floor Mosaic Size`
   at 4 — these were hand-patched into the scene YAML rather than written by the tool (see §3),
   so this is the first real check that the patch took. If either count is wrong, running
   **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** rewires both from whatever is on disk in
   `Assets/Generation/Tiles` and is safe to run again — it does not duplicate or overwrite
   existing tile assets, only the component references.
2. Regenerate a dungeon and look at a large room's floor. Stone should run continuously across
   cell borders — no grid of patches, no rectangular tonal steps.
3. Walk a room and watch the moss. Judge whether the 4-cell repeat reads as a pattern *in game
   lighting*, not in the scene view with everything lit — that difference is the whole question.
4. Check paper is showing up, at a believable size against the player and the barrels, and that
   no two pages sit at the same angle. `decalChance` on the painter tunes how much litter there
   is; paper is 6 of 10 decal variants now, so if the floor reads as too papery that is the
   knob, or drop entries from `PaperSheetCounts`.

---

## Stage 17 — Doors on one jamb, the threshold tile, pillars in passages, doors in shadow, table pathfinding

Five reports from playing the generated dungeon. Measured wherever the claim could be —
the layout assembly is engine-free, so it was run headlessly over 200 seeds at the shipped
settings and each claim checked as a number before anything was changed. That immediately
settled which reports were what they looked like and which were not.

### 1 — "Two doors side by side, not fixed to the wall on both sides"

The obvious reading is that the layout put two doorway cells next to each other. **It never
does**: across 200 seeds, adjacent `Door` cell pairs came to **0**, and the closest two doorways
ever get is 2 cells apart. So the pair in the screenshot was not two halves of one wide opening.

The real fault is one door per opening, hung on nothing. `SpawnDoors` tested only the east/west
pair to decide rotation and took "no" to mean "north/south then", without ever checking:

```csharp
bool wallEast = !layout.IsWalkable(x + 1, y);
bool wallWest = !layout.IsWalkable(x - 1, y);
doorInstance.transform.rotation = wallEast && wallWest ? …90° : …identity;
```

A doorway cell with, say, solid to the north but open to the south falls into the `else` and gets
an upright leaf with open floor down one side. Measured: **1131 of 9483 doorway cells (11.9%),
5.7 per map, on 196 of 200 maps** have neither a solid east/west nor a solid north/south pair.
Not an edge case, and two of them near each other is exactly the screenshot.

Fixed by testing both pairs and spawning nothing when neither is solid. Those cells stay open
arches — which is already what the layout does with an opening too wide to narrow, so it is the
existing convention rather than a new one (see `DoorwayNormalizer`, which leaves an opening too
wide to narrow unmarked for exactly this reason).

**Measured after the fix**, same 200 seeds: 8348 doors spawn (41.7 per map, down from 47.4),
**0** of them unjambed, 4146 rotated against 4202 upright — an even split, which is the sanity
check that the newly-tested axis is real and not just always answering the same way. No map is
left without doors.

### 2 — The threshold tile broke the floor it now sits in

Found while fixing §1, not separately reported. `doorwayTile` painted a dark slab with two lit
jambs on every doorway cell, and `OrientDoorways` turned it to face along its passage. That
earned its place when the floor was flat generated noise and an opening needed help reading as
an opening. Against the real floor art (Stage 16) it does the opposite: a doorway is the one
cell that is always looked at straight on, and a different tile there cuts the stone that now
runs continuously through it, so the threshold reads as a patch rather than as the floor
carrying on under the door.

Doorway cells take the ordinary floor now. `doorwayTile` and `OrientDoorways` are gone (the
rotation existed only to orient that tile, and rotating a *mosaic* tile would tear the very
continuity the mosaic is for), along with the setup's `DoorwayTile` generation and its wiring.
The `TileStyle.Doorway` drawing routine is kept, unused, as the record of what the threshold
looked like. The stale `doorwayTile:` line left in both scenes' YAML is harmless — Unity drops
serialized fields that no longer exist on the class.

### 3 — "A pillar blocking the way"

Real, and the most common of the four. `RoomInteriorDecorator.Apply` writes a batch of solid
cells and rolls back if `RoomStaysWhole` fails. That check asks whether the room is still in one
piece — and a pillar dropped into a one-cell gap leaves it perfectly connected whenever any other
way round exists. So the rollback never fires, and the pillar stays: floor either side of it,
wall above and below, sitting square in the channel.

Worth being precise about what is and is not broken here. **Connectivity is never actually lost**
— 0 of 200 maps had a single stranded walkable cell, before or after. The generator's own
validation was doing its job. The complaint is about how it reads, and it reads as a blocked
corridor whether or not a detour exists.

Fixed with `ClearPassagePlugs`, which re-opens any cell of a batch that ends up with floor on both
sides of one axis and solid on both sides of the other. Run to a fixed point, because opening one
cell can leave a neighbour of the same batch newly flanked and so newly a plug; each pass only
turns solid into floor, so it terminates.

**Measured, 200 seeds:** plugging pillars away from any doorway **1948 → 202, down 90%**. Total
pillars fell only 1.2% (26102 → 25786), so the interior patterns are intact rather than
dismantled. Connectivity still 0 stranded cells.

Doorway jambs are deliberately untouched: those come from `DoorwayNormalizer` walling an opening
down to door width, are *supposed* to sit beside a gap, and never appear in a batch this sees.
They are the 745 plugging cells that remain next to a door.

### 4 — Doors rendered in shadow while walls stayed lit

Nothing to do with materials, and nothing to do with the vision mask — the door leaf does not use
the FOV-masked material at all. It is purely sorting order. `DarknessOverlayQuad` draws at order
**6**; `WallSortingOrder` is **7**, which is *why* walls read as lit — they are simply drawn over
the darkness. The door leaf was at **0**, i.e. underneath it.

Raised the leaf to **8** and the three barricade stages to **9/10/11**, keeping their relative
order. 8 rather than 7 also fixes a second thing, noted as known-and-unaddressed in
`DungeonSceneSetup`'s own comment on `WallSortingOrder`: a leaf wider than its cell used to draw
partly *behind* the jamb tiles it swings across. Above the walls, it no longer can.

### 5 — The table was not blocking pathfinding

Fallout from Stage 15's `useTriggers = false`, and a real gap rather than a regression of that
change. The table is deliberately two colliders: a **trigger on the root**, on `ObstaclePathOnly`,
which is the `CrouchHideout` footprint the player hides inside, and a **solid child**, `Solid`,
on `CrouchPassable` — the layer `PlayerHiding` makes the player's body stop colliding with while
crouched, so a crouching player fits under the table. Stage 15 made the pathfinding grid ignore
triggers, correctly, but `PathfindingGrid.obstacleMask` covered layers 8, 9 and 11 and not 13, so
with the trigger ignored nothing about the table was left for pathfinding to sample and enemies
walked through it.

The mask is now 11008 — layers 8, 9, 11 and **13** — in both scenes. Adding `CrouchPassable` is
what makes the solid child do the blocking, which is what was wanted: the trigger is a gameplay
volume, not a navigation hint. Checked every prefab for the same shape of problem: only `Table`
has a non-trigger collider on a layer outside the pathfinding mask. `Barrel` is layer 11 with a
**non-trigger** collider, so it was never affected by the trigger change and still blocks
correctly.

Layer 13 stays out of `FieldOfView`'s mask (4864: layers 8, 9, 12), so a table blocks movement
without blocking sight, per the project's standing rule that only walls and trees occlude.

### Not done

- **`ObstaclePathOnly` (layer 11) is now close to dead** for pathfinding, since the one thing
  using it does so with a trigger and triggers are ignored. It stays in the mask because a
  non-trigger collider placed there would still be a legitimate path-only obstacle; worth
  revisiting if nothing ever uses it that way.
- **The 11.9% of openings that lose their door are not compensated for** (§1). They become
  arches. Whether the dungeon wants a door there at all is a layout question — the opening
  genuinely has no jamb to hang one on — and forcing one would mean the normaliser walling a
  cell to build a frame, which is a change to generation rather than to spawning.
- **~202 plugging pillars remain across 200 seeds** (about 1 per map, down from ~10, §3). They
  are not from `RoomInteriorDecorator`'s batches, which are now clean by construction, so they
  come from another pass — most likely `RoomShaper`'s perimeter detail, which pushes the odd
  wall cell inwards after the interiors are placed. Catching those needs either the same
  treatment there or one global sweep at the end of generation, skipping cells beside a doorway
  so the jambs survive. Left alone for now: a global sweep late in the pipeline can undo shaping
  the earlier passes did on purpose, and that is worth measuring properly rather than bolting on.
- **None of this is verified in the editor.** Everything above is a headless measurement of the
  layout plus two rendering changes (the threshold tile's removal, the sorting order) that
  cannot be checked without rendering. The numbers say the layouts no longer contain these
  shapes; they do not say the dungeon looks right.

### In-editor checklist for this stage

1. Regenerate a dungeon and look along a few corridors: every door should meet solid wall or
   pillar at both ends of its leaf. Openings with no frame should simply be gaps, no door.
2. Look at a doorway floor — the stone should run through it unbroken, with no darker slab or
   rotated patch marking the threshold.
3. Walk a few corridors: no pillar should be standing in a one-cell gap with floor either side
   of it. About one per map may still be, per "Not done" above.
4. Doors should now be lit like walls rather than sitting under the darkness, including when
   swung open across a wall.
5. Walk an enemy into a table (or watch one path around one) — it should treat the table as
   solid — then crouch and confirm the player still fits under it, which is layer 13 doing two
   jobs and this change only touching the pathfinding half. Confirm the table still does not
   block vision either.

---

## Stage 18 — Mushroom and sleeping-bag decals

Three sprites added to `Assets/Art`: `mushroom.png`, `mushroom02.png` (a cluster of caps), and
`sleepingbag01.png`. Asked for as decals — everything else drawn from these sprites becomes a
prop/prefab by hand instead, so this stage only covers the three that go through the generator.

Structurally simpler than Stage 16's paper: each file is already exactly one thing to place,
with nothing overlapping that needs separating out first. `EnsureNatureDecals` crops straight to
the artwork's opaque region (via `FindOpaqueGroups`, so a stray anti-aliasing fleck outside the
real drawing can't widen the crop the way a plain alpha bounding box would) and composites it
once per tile with a random rotation and a small scale jitter — reusing `CompositeRotated` and
`ExtractGroup`, the same building blocks a lone paper sheet uses. Fill is 0.8 of the tile's
longest side rather than paper's 0.42: these read as ground cover you notice, not litter you
glance past.

**Rarity is not a second weighted-pick system.** `DungeonPainter.PaintDecals` already picks
uniformly over the decal array; asking it to weight entries would mean threading a weight
through the hash-based picker for every decal type, generated and hand-drawn alike, for the sake
of three sprites. Cheaper and just as correct: write the *same* `Tile` reference into the
returned array `weight` times. Three mushroom-source repeats each against one sleeping-bag
repeat is a 6:1 mix — mushrooms common, a dropped sleeping bag a small find. Tune by editing
`NatureDecals`' weight column in `DungeonSceneSetup.cs`, not by adding selection logic.

Distinct decal tiles: 10 → 13 (4 generated + 6 paper + 3 nature). The painter's `Decal Tiles`
array is longer than that, at 17, because each nature tile's reference is repeated by its
weight — 3 mushroom, 3 mushroom-cluster, 1 sleeping bag — so the uniform picker's odds land on
the intended 6:1 mix without knowing weights exist.

**Not generated yet.** Unlike Stage 16, no PNGs were fabricated outside Unity this time: with
`WriteArtTile` only writing a texture when the file is missing, a hand-approximated placeholder
would permanently block the real Setup output the next time `Setup Scene Tilemaps` runs without
`overwrite`, rather than being replaced by it. Since the floor and paper art was in fact produced
by you running the tool in the editor rather than by anything fabricated outside it, the same
path is correct here too. Compiles clean against the full project; nothing about the actual
cropped images, their rotation, or their placement in a cell has been seen by anyone yet.

### In-editor checklist for this stage

1. Run **Tools ▸ Dungeon ▸ Setup Scene Tilemaps**. It should cut three new tiles into
   `Assets/Generation/Tiles` — `DecalMushroomA`, `DecalMushroomB`, `DecalSleepingBag` — and wire
   the painter's `Decal Tiles` to 17 entries (up from 10): 4 generated + 6 paper + the 7 nature
   slots described above.
2. Regenerate a dungeon and confirm mushrooms and the sleeping bag actually appear, at a
   believable size and with varied rotation — no two should sit at the same angle by
   construction, but confirm none reads as stamped or oversized against the barrels and table.
3. Judge the mix by eye: mushrooms should read as noticeably more common than the sleeping bag.
   If the balance feels off, or nature decals are crowding out paper and the generated marks,
   `NatureDecals`' weights and `decalChance` are the two knobs, in that order.

---

## Stage 19 — Statues, a scene-wiring gap, a build-order bug, and a fragile footprint formula

Four reports at once, arriving in the middle of each other, so they are numbered by report
rather than by when each was found. §2 and §3 were found investigating §1's "collide with each
other" half; §1's "not fixed to pathfinding" half turned out to be a real bug that also explains
why the sleeping bag from Stage 18 was never seen despite being generated.

### 1a — Tables did not show as blocked on the pathfinding gizmo, at all

Not a gizmo problem, not the trigger/layer question Stage 17 already fixed — the collider
genuinely was not there yet when the grid sampled it. `DungeonBuilder.Build` configured
`PathfindingGrid` (a physics query) immediately after painting, then fired `Built`, whose only
subscriber is `DungeonPopulator.Populate` — the thing that spawns every table, statue, barrel
and chest. So the grid always sampled physics *before* a single prop existed. Painting's own
colliders (the composite collider on the wall tilemap) were already final by then, which is
exactly what the surrounding comment was checking for, so nothing about wall blocking ever
looked wrong — only content that arrives through `Populate` was affected, silently, regardless
of its layer or trigger flag, which is why Stage 17's mask fix did not fix this on its own.

Fixed by reordering: paint, fire `Built` (spawns everything, synchronously — a C# multicast
delegate invocation blocks until every subscriber returns), *then* configure the grid.
`DungeonPopulator` was checked for the reverse dependency first — does it need the grid to be
valid before it runs — and it does not: every placement decision in it reads
`DungeonLayout.IsWalkable`, the pre-physics abstract layout, never the live `PathfindingGrid`
component. The class doc on `DungeonBuilder` asserted the opposite ("the grid must be valid
before anything spawns, because spawn placement checks walkability") — conflating the two
different kinds of "walkable" — and has been corrected along with the reorder.

This is also, very likely, why the Stage 18 sleeping bag was reported as never appearing even
though its tile existed on disk and was described as generating: it was never the placement
logic, it was that the scene had not been rewired since Stage 18 shipped (§2 below) — a
separate, compounding gap, not this one, but worth naming since both were live at once and
either alone would have hidden the sleeping bag.

### 1b — "Tables still collide with other tables"

Investigated at length and **not conclusively reproduced analytically**. `FootprintCells`'
spacing check, `IsClearOfPlaced`, `TryTakeAnchor` and `TryTakeSpaced` were each worked through
by hand against the table's actual measurements (a 9.84×5.2 local-unit collider at 0.18 prefab
scale, spawned under a `DungeonRoot` scaled 2×, on a grid whose cells are also 2 world units) —
every check comes out requiring *more* real-world clearance than two tables need, not less. The
minimum-allowed spacing case leaves roughly half a world unit of gap by this arithmetic.

Two things came out of the attempt anyway, both worth keeping regardless of whether they were
the cause:

- **§3 below**: the footprint formula was only numerically correct by a coincidence (cell size
  happening to equal `DungeonRoot`'s scale) and has been made correct by construction instead.
  It produces the *same* number today, so this by itself does not explain an already-observed
  overlap — but it does mean the *next* person to retune either value would have silently
  reintroduced real overlap with no code change of their own to blame.
- **The likeliest actual explanation is staleness**, not a live bug: §1a means every dungeon
  generated before this fix had its tables spawn with no working pathfinding block at all, and
  the baked `Dungeon.unity` may be carrying tables placed by an even older build than that. A
  regenerate after this stage's fixes is the next real data point, not another round of static
  reasoning — see the checklist.

Also actioned directly, independent of the investigation: **`prop.table`'s weight halved**
(0.6 → 0.3) per request, so there are fewer tables regardless of the spacing question.

### 2 — The scene was never rewired for Stage 18's nature decals

`Setup Scene Tilemaps` is the only thing that writes `decalTiles` and `floorMosaicSize` onto the
painter — the same gap Stage 17 hit for the floor mosaic and paper. Checking `Dungeon.unity`
directly found `decalTiles` still at 10 entries (4 generated + 6 paper) despite
`DecalMushroomA/B` and `DecalSleepingBag` existing on disk with real timestamps, meaning
**Regenerate Placeholder Tiles was run — which only recuts art — and Setup Scene Tilemaps,
which wires the scene, was not.** The tool's own naming makes that an easy mix-up.

Hand-patched to 17 entries (mushroom ×3 each, sleeping bag ×1, matching `NatureDecals`' weights)
the same way Stage 17's wiring gap was patched, since there is no Unity instance here to run the
tool itself.

### 3 — `FootprintCells` divided by nothing

Flagged investigating §1b, not separately reported. The formula converted a prefab's world size
straight to a cell count with no division step at all:

```csharp
int cells = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(size.x, size.y)));
```

That silently assumes one world unit is one cell. It is not — cells are 2 world units on the
shipped grid — so this should have under-counted by half. It didn't, because `size` itself was
measured from the *prefab asset*, which has no parent and therefore no idea it is about to be
instantiated under a `DungeonRoot` scaled 2×; the missing "×2 for the real spawn scale" and the
missing "÷2 for the cell size" cancelled exactly, because both factors happen to be 2 in this
project today. Correct answer, wrong reason — and a reason that stops being true the moment
either number changes on its own.

Fixed to compute honestly: prefab asset size × `_contentRoot.lossyScale` (the scale it will
actually spawn at) ÷ `builder.CellSize`. Also extended to measure a `CircleCollider2D` — needed
for the statues below, which have no `BoxCollider2D` at all and previously fell back to sprite
bounds alone.

### 4 — Statues

Two prefabs added (`Statue01`, `Statue02`), each a `CircleCollider2D`, non-trigger, on layer
`Default`. Registered as `prop.statue01`/`prop.statue02` in `PrefabRegistry` and in
`RoomContentSettings.props`, the same list `prop.barrel` and `prop.table` are in, weight 0.3
each — rarer than a barrel, matching the table's own new weight. Layer changed to
`ObstaclePathOnly` (11) on both, the same layer `Barrel` already uses: already in
`PathfindingGrid.obstacleMask` (blocks pathfinding) and not in `FieldOfView`'s mask (does not
block sight), which is the project's standing convention for this kind of prop.

They fall into the same shared clustering system every other prop uses
(`propsPerClusterMin`/`Max`), which has no concept of "this prop type does not cluster" — a room
could get 2–4 of the same statue standing together. Not addressed here; see "Not done".

### 5 — A guaranteed sleeping bag in the hub

Asked for on top of Stage 18's random scatter, not instead of it: exactly one sleeping-bag decal
in the hub every generation, in addition to its existing rare chance of turning up anywhere else.
`DungeonPainter` gained a dedicated `hubGuaranteedDecalTile` slot — separate from the `decalTiles`
pool, because the pool has no concept of "this one is special" once it is flattened into an
array — and a pass that finds `RoomKind.Hub`, hashes every eligible cell (walkable, not a
doorway) with a salted seed so the pick does not just replay the ordinary scatter's own numbers,
and paints the tile on whichever hashes highest. Runs regardless of `decalChance`, deliberately:
turning that down to inspect bare floor should not also remove the one decal that is meant to
always be there.

### Not done

- **Statues cluster like barrels.** The shared `propsPerClusterMin`/`Max` system has no
  per-prefab override, so a statue can be placed 2–4 at once same as a barrel would. A single
  imposing statue reads very differently from a small crowd of identical ones; giving individual
  `PrefabChoice` entries their own cluster range (or a `neverClusters` flag) is a
  `RoomContentSettings` change this stage did not make.
- **§1b's table-table overlap is not confirmed fixed**, only investigated without finding a
  live bug in the placement arithmetic itself. The checklist below is the actual test.
- **Nothing in this stage is verified in the editor.** The build-order fix, the footprint
  formula, and the guaranteed hub decal are all either headless-unverifiable (they depend on
  physics sampling and rendering) or hand-reasoned rather than measured.

### In-editor checklist for this stage

1. Regenerate a dungeon. Open the painter's inspector first and confirm `Decal Tiles` reads 17
   and `Hub Guaranteed Decal Tile` is set to `DecalSleepingBag` — the scene patch from §2 taking
   is a precondition for everything else here being visible at all.
2. Turn the pathfinding gizmo on and look under a table and a statue: both should read red now.
   This is the direct test of §1a — if either is still walkable, the build-order fix did not
   take, or something else is spawning outside `DungeonPopulator.Populate`.
3. Walk several rooms' worth of tables specifically, looking for physical overlap. This is the
   real test §1b never got — note whether it still happens, and if so, roughly how close (touch?
   overlap by half a table?) since that distinguishes "still needs a code fix" from "was just
   stale content that regenerating already cleared."
4. Confirm statues spawn, block movement, and do not block the player's line of sight.
5. Find the hub and confirm exactly one sleeping bag is on its floor, every time the dungeon is
   regenerated (try two or three different seeds).

---

## Stage 20 — Props standing inside walls, and a weight slider for the spawn tables

### 1 — Props overlapped the map geometry

Reported with screenshots: a statue's circle collider sunk well into the wall tiles beside it.
This is the same class of mistake §1b of Stage 19 went looking for and did not find, but on the
*wall* side rather than between two props — and here it reproduces exactly.

A cell is the unit the generator places on, not the size the thing being placed actually is.
`TryTakeFixtureCell`, which furnishes the hub, already accounted for that: it requires
`HasClearance(footprint / 2)` and reads "against a wall" as solid ground exactly one ring
*past* that clearance. The prop and chest path, `TryTakeAnchor`, did not check clearance at
all, and its wall-bias pass actively preferred cells satisfying `layout.TouchesSolid(cell)` —
i.e. cells directly adjacent to rock. `Statue01`'s collider is a 1.59-radius circle at 0.8
prefab scale, spawned under a 2× `DungeonRoot` on 2-unit cells: about 2.6 world units, three
cells across. Anchored on a wall-side cell, more than half a cell of it is inside the wall,
which is both a physical overlap and (since Stage 8's sorting order) painted over by the wall
tilemap.

Fixed by giving `TryTakeAnchor` and `TryTakeSpaced` the same clearance rule the hub's fixtures
already used, so "against a wall" means the object's own edge touches it rather than its centre
cell doing so. The two near-identical fallback loops in `TryTakeAnchor` (blocking and
non-blocking, differing only in the chokepoint test) were merged while the clearance check was
being added to both. Props with a one-cell footprint — barrels, candles — are unaffected:
clearance is 0 and the wall test reduces to the old `TouchesSolid`.

### 2 — Spawn frequency was tunable but not legible

The per-entry `weight` on `PrefabChoice` and `ItemChoice` has always controlled how often
something appears, but as a bare float field it could not answer the question it was being used
to ask. Whether `0.3` is rare or common depends entirely on the other entries in the same list,
which the inspector never showed, so tuning a table meant summing the column by hand.

`Editor/SpawnChoiceDrawer.cs` draws each entry as a slider plus the share it currently works
out to (`0.3` of a four-entry table reads `15%`). The slider's top end is 3, raised to fit any
entry already authored above it so opening the inspector can never clamp a weight it cannot
reach. The share is computed over the whole list with no depth gating — gating is per-room and
depth-dependent, so any single figure for it would be wrong nearly everywhere — and `Min Depth`
sits directly beneath it to say when the entry is eligible at all.

No serialized data changed: this is presentation over the existing `weight` field, so existing
`RoomContentSettings` assets are untouched.

### In-editor checks this stage needs

1. Regenerate a few seeds and look specifically at statues and tables placed against walls:
   their sprites and colliders must stop at the wall, not cross it.
2. Confirm rooms are not visibly emptier than before — the clearance rule rejects candidate
   cells, so a cramped room may now place fewer props than it did.
3. Open `RoomContentSettings` and check the sliders read sensibly, and that dragging one moves
   every share in that list.

---

## Stage 21 — The way out: a door in the wall and the one key that opens it

Until now a run had no ending. The dungeon had a deepest room with a reward in it, and then
the player kept playing. This stage gives the run a goal that can be reached and a lock that
makes reaching it a journey rather than a walk.

### The shape of it

Three pieces, deliberately placed at opposite ends of the map from each other:

- **`RoomKind.Exit`** — the room the way out is in. Picked for being **against the edge of
  the map**, not for being deep in the room graph. An exit is a way *out* of a dungeon, and
  the middle of the map is not somewhere you can leave from.
- **The exit door** — a key-locked door cut into that room's **outer wall**, opening onto
  nothing but the rock between the room and the edge of the map. Recorded on the layout as
  `ExitDoorCell`, with the floor cell in front of it as `ExitThresholdCell`.
- **`Room.HoldsExitKey`** — the room holding the chest with the key, chosen as the room
  **farthest from the exit over the room graph**. Hops, not metres: the player walks
  corridors, and two rooms either side of one wall can be a dozen rooms apart to walk
  between. *(Superseded by §22: the key now lives in one of the locked treasure rooms, and
  the distance is measured straight-line with hops breaking ties. There is no room dedicated
  to the key any more.)*

### The lock is on the way out, not on the way in

The first version of this stage locked the exit room's *entrances*, which made the room
unenterable and therefore invisible: a player without the key met a locked door in a corridor,
which says nothing about what is behind it. Moving the lock to a door in the far wall inverts
that. The room is entered through ordinary doors, the player stands inside it looking at a
door that plainly leads outside, and the only thing missing is the key. A locked door in the
far wall of a room you are standing in says exactly one thing.

It also removed a whole class of failure. Sealing a room meant *every* opening into it had to
be able to take a door leaf — and the populator refuses to hang a door on an opening with no
jambs, measured at 11.9% of doorway cells back in Stage 17 — so one bad opening made the lock
decorative. A single door in a wall the generator chose has no such dependency.

### The exit room is a dead end

A room with a corridor out the far side reads as one more room to pass through, whatever is
in its wall; a room with a single way in reads as somewhere the dungeon stops. So the exit is
picked from rooms with exactly **one entrance**.

Entrances are counted off the geometry — groups of adjoining walkable cells just outside the
room — rather than off `Room.Degree`, which counts corridors in the room graph. The two
disagree: a corridor carved past a room can open into it without a link being recorded, and
what the player walks through is the opening, not the graph edge. They are grouped rather
than counted cell by cell, because one opening three cells wide is still one way in.

Preferred, not required. On a small map the dead ends and the rooms with an outward wall do
not always overlap — measured at **1 seed in 200** — and the generator falls back to any room
with an outward wall rather than producing a dungeon with no ending. Measured after the
change: 199 of 200 exit rooms are dead ends, and all 200 still have a way out.

### The door leads outside, and nothing is carved

`FindExitDoorway` only accepts a wall cell with **solid rock in a straight line from it to
the edge of the map**. A door opening onto two metres of stone and then somebody's storeroom
is not an exit, and the player cannot tell the difference until it has cost them the run's
only key.

The cell **stays solid**. Nothing is carved out for the door: a walkable cell there would be
a hole in the map that the pathfinding grid, the vision system and the wall painter would
each have to be taught to treat as a special case. The door's own collider is what physically
blocks the opening.

The one concession the painter makes is to skip the **wall tile** on that cell. The wall
tilemap sorts above world sprites (Stage 8), so a door standing on a painted wall cell is not
merely close to the wall — it is painted over by it, and the exit would be an invisible door
in a blank stretch of rock. Leaving the tile out cuts the doorway-shaped gap the door stands
in, while the layout still calls the cell solid.

### Putting the door somewhere that looks deliberate

The door goes in the **middle of the longest unbroken run of usable wall**. A door in the
corner of a room reads as a mistake; one centred on a wall reads as built.

The first version scored candidates by distance from `Room.Center` instead — which is the
room's *medoid*, and for an L-shaped or ring-shaped room the medoid is nowhere near the
middle of any particular wall. Measured over 200 seeds that put the door mid-wall on 189 of
them and hard into a corner on 4, which is exactly what turned up on the first look at a
generated map. Scoring the wall run itself has no such failure mode: a corner is a short run
by construction, so it loses to any real stretch of wall. Re-measured after the change: **no
seed puts the door in a corner**, 177 of 200 sit dead centre, and the remainder are centred
on the usable stretch of a wall whose other half has a corridor running behind it — which is
the correct answer rather than a miss.

### Three defects found on the first playtests

**The lock was never applied at all.** `SpawnExit` looked the door component up with
`doorInstance.GetComponent<SimpleDoor>()`, and the door prefab's root is a rig —
`Door_System`, carrying the barricade and the collision forwarder — while `SimpleDoor` lives
on the `Door_Visual` child that actually swings. The lookup returned null on every seed, so
both the key lock and the reinforcement were skipped and the threshold was bound to no door,
which made it fire on contact. Three symptoms, one cause: the exit opened without the key, it
could be rammed, and walking in ended the run.

The only sign was a warning nobody was looking for. Worth remembering as a class of bug:
`GetComponent` against a prefab whose root is a rig fails silently, and a generator that only
*warns* when the component is missing will keep producing dungeons that look right.


**The exit door opened without the key.** `SimpleDoor.RequireKey` wrote `requiresKeyToOpen`
and never told the editor about it, so the lock existed in memory only: the door was locked
until the next domain reload and openable by anyone afterwards. The Dungeon scene is authored
by baking, so "until the next domain reload" is the entire lifetime of the configuration.
`ChestInventory.SetStartingItems` already carried a `SetDirty` for exactly this reason — the
door path simply never got one. It now calls both `EditorUtility.SetDirty` and
`PrefabUtility.RecordPrefabInstancePropertyModifications`; generated doors are prefab
instances on purpose, and an override that was never recorded is discarded when the instance
next reconciles with its prefab.

**The lock lapsed once it had been used.** Immunity to melee and to sprint ramming was keyed
off `requiresKeyToOpen`, and spending the key clears that flag — so the exit door became
breakable the moment it had been opened once, and a destroyed door would have left a hole
through the outer wall of the map. Immunity now hangs off a separate `isReinforced` flag that
nothing clears. The exit door is reinforced whether or not there is a key for it: without a
key it is meant to be openable, but never by shoulder-charging it or hacking it apart.

### Two doors back to back in one corridor

Spotted on a generated map and not caused by any of the exit work — it predates all of it.
`DoorwayNormalizer` can mark two adjoining cells of the same passage as `CellType.Door`, and
`SpawnDoors` hung a leaf on every Door cell that had jambs, so both got one: two doors in
series in a corridor one cell wide.

Measured at the shipped settings (200×200, 30 rooms): **44 adjacent door-cell pairs across 60
maps**, so roughly one map in one and a half had at least one. `SpawnDoors` now remembers the
cells it has already doored and skips any cell adjoining one; the first in scan order takes
the door.

Cells adjoining *across* a passage rather than along it are not affected, because neither of
them passes the jamb test in the first place — an opening two cells wide has nothing to hang
a leaf on and stays an arch, which is the Stage 17 behaviour.

### Why the exit is picked before the treasure

`AssignRoomRoles` tags the exit first, then the treasure from what is left. If the treasure
went first the two could land on the same room on maps where the deepest room also happens to
sit at the edge, collapsing two separate destinations into one. Picking the exit by a
different measure than the treasure — edge distance versus graph depth — is what keeps them
at opposite ends of the run rather than merely usually apart.

### The interior decorator now skips the exit room

Structural, not cosmetic: the threshold cell is chosen before the decorator runs, and a pillar
or a pile of rubble dropped on it would make the way out unreachable with nothing reporting a
fault.

### What the treasure room is for, now that there is a critical path

Adding the key gave the map a critical path — key, then exit — and that immediately raised
the question of why anyone would walk anywhere else. The treasure room is the answer, but it
was not paying for the detour: it held a pistol, a sword, a shotgun and 5–15 coins, and
**the game has no vendor of any kind**, so the coins buy nothing and the weapons are ones the
player probably already has. An optional room that pays in dead weight is not an optional
room, it is filler.

With no story to motivate exploring, the only thing that can justify a detour in a survival
game is resources that change the odds. This project has exactly such an economy and the
treasure room was ignoring it: **Ink** is what a save costs (`SaveCost`), and ammunition is
what a fight costs. Both tables are now Ink plus Bullet and Shell, which turns the room into
an arithmetic decision — spend the walk and the risk, buy back saves and ammunition — with
no fiction required.

Lamp fuel would belong here too and is deliberately absent: `ILightFuel` exists as an
interface for a planned consumable, but there is no fuel item to put in a table yet.

**The key room can no longer be the treasure room.** They are the map's only two reasons to
walk anywhere that is not the exit, and one room holding both collapses them into a single
trip — the player fetches the key and picks the reward up on the way past, and the optional
detour stops being a decision.

### The key and the treasure kept landing next to each other

Spotted on a generated map: the key room, the treasure room and the exit all clustered in one
corner. Measured at the shipped settings, the key sat within **a tenth of the map diagonal**
of the treasure on 7 of 60 seeds and within a fifth on 17.

The cause is that "deepest from the hub" and "farthest from the exit" are two different
questions whose answers are **correlated** — both pull towards the same far end of the room
graph — so scoring the key on distance from the exit alone was never going to keep it away
from the treasure. Adding hops-from-the-treasure as a tie-break barely helped: 17 down to 14.

What worked was changing the measure. The key room is now scored on the **smaller of its two
distances**, to the exit and to the treasure, maximised — "far from everything that matters"
rather than "far from one thing", and it cannot be gamed by being enormously far from one of
the pair. Distance is now straight-line rather than hops, which reverses the original
reasoning: the player does walk corridors, but hop counts on a 200×200 map with 30 rooms are
small integers that tie constantly and correlate poorly with what the map looks like, and the
sense of "opposite ends of the dungeon" is spatial. Hops still break ties.

Re-measured over 60 seeds: the closest key-to-treasure pair is now **40–50% of the diagonal**,
and key-to-exit likewise — no seed under 40% for either. Before the change both had a tail
reaching down to 5%.

### The key is not loot

`SpawnKeyChest` writes the key into the chest directly instead of going through `StockChest`
and a weighted table. A weighted table can roll a zero, and the one object the run cannot be
finished without must not be subject to a dice roll. For the same reason the call sits outside
the `authored` guard that suppresses random content in template rooms: a key room that
happened to draw a hand-authored interior still gets its chest.

### Measured across 200 seeds

At the shipped settings (64×48, 8 rooms): **200 of 200 seeds produced an exit room, a way out
and a key**, with no invariant violations — no exit that was also the hub or the treasure, no
key inside the room it opens, no door cell accidentally carved walkable, no exit door whose
outward ray runs back into the dungeon. The exit room landed within six cells of the map edge
every time, and the key sat 50–70% of the map diagonal away from it on 68% of seeds, never
closer than 20%.

### What ends the game

`DungeonExit` is a trigger on the threshold cell, bound to the exit door and firing only once
that door is open. It uses `OnTriggerStay2D` rather than `OnTriggerEnter2D`, and that is not
an accident: the player has to be standing in front of the door to open it, so by the time the
door swings they are already inside the trigger and Enter has long since fired. With Enter
alone the run only ended if the player stepped off the threshold and back onto it after
unlocking.

It calls `GameManager.WinGame()`, which is a new `GameState.Victory` kept separate from
`GameOver`. Both end the run, but everything that listens wants to tell them apart — the
screen it shows, the music it plays, and whether the save is a corpse or a finished run.

**Still to wire:** nothing in the UI listens for `GameState.Victory` yet, so today the run
ends with a state change and a log line rather than a screen.

### Finding it in the editor

`DungeonBuilder` draws scene-view gizmos for every room with a role — hub blue, treasure
magenta, exit green, key room amber — plus a circle on the exit door cell and a line from it
to its threshold. It works on a baked scene with no layout in memory by regenerating the
layout from the inspector seed, which is sound because generation is a pure function of the
seed and the settings. All of it is inside `#if UNITY_EDITOR`.

### In-editor checklist

1. Regenerate the dungeon and find the exit room — against the map edge, floored in the
   `exitFloorTiles` variants, empty of enemies, loot and props, with a visible door in its
   outer wall.
2. Try that door without the key: it must refuse to open, refuse to be rammed, and take no
   melee damage.
3. Check there is no hole in the map behind it — the cell is solid, and the rock beyond it is
   painted as normal.
4. Find the key chest on the far side of the map; it holds exactly one key and nothing else.
5. Open the exit door with the key — the key is consumed — and stand on the threshold. The
   console logs the run as complete.

---

## Stage 22 — Treasure rooms: small locked loot rooms, one of them holding the exit key

Two things were wrong with the reward layout this replaces. The treasure room was whichever
room ended up farthest from the hub — one room, wherever the layout happened to leave it,
standing open and costing the player nothing but walking. And the exit key sat in a room of
its own, an ordinary room that gave no sign of what was in it and existed to be walked to
once.

This stage replaces both with one arrangement: **several small locked rooms, and the key is
in one of them.**

### The shape of it

- **`RoomKind.Treasure`** is now a closet-sized dead-end room, sampled by the generator
  (4–6 cells to a side) on top of the room budget rather than picked out of the finished
  map. Three per dungeon at the shipped settings.
- **Its door takes the craftable key** (`Door Key`, the silver one), not the golden exit
  key. The player can always open one in principle; what they are deciding is whether *this*
  door is worth a key, the scrap and the detour, with only its position on the map to judge
  by.
- **`Room.HoldsExitKey`** now lands on the treasure room farthest from the exit. There is no
  dedicated key room any more: the player finds the key by opening the rooms they wanted to
  open anyway, which is what makes the choice of which door to spend a key on something the
  run can turn on.
- **What is inside**: the treasure chest table and loose treasure loot, and no enemies —
  nothing walked into a sealed closet, and one locked in is one the player can hear and
  never reach.

### The three rules that keep the lock optional rather than punishing

Each is a correctness condition, checked rather than assumed
(`RoomCorridorGenerator.ValidateTreasureRooms`, plus the tests in
`RoomCorridorGeneratorTests`):

1. **Always a dead end**, measured on the geometry — `CountEntrances`, groups of adjoining
   walkable cells just outside the room — and not on the room graph. `ConnectRooms` holds
   treasure plots out of the spanning tree and out of the loop edges and gives each one a
   single corridor to its nearest ordinary room, so `Room.Degree` is 1 by construction; but
   the graph is not what the player walks. A corridor routed *past* the room punches into it
   without a link being recorded. Checking `Degree` alone locked rooms with a second door
   standing open on the far side, at **16.4% of plots over 200 shipped-settings seeds** —
   which is what showed up in the editor as a treasure room that was not a dead end. The
   entrance count catches it; the spare plots (four, up from two) absorb the demotions it
   causes.
2. **Always closable.** Every doorway cell must be able to hold a door leaf and no two of
   them may be adjacent — the same test the populator applies when deciding where to hang
   doors, shared through `DungeonLayout.HasDoorJambs` so the two passes cannot disagree. A
   room advertised as locked that is walked into through the arch beside its door is worse
   than an unlocked one.
3. **Nothing else the run needs is behind a lock.** Never the hub and never the exit — those
   roles are assigned after the treasure plots are tagged, and both skip them — and the
   guaranteed lamp fuel is spawned anywhere but a treasure room. The exit key is the one
   deliberate exception, and it is safe because its lock is *craftable*: `Door Key` costs
   3 × `Scrap`, and scrap is in both the floor-loot and the chest tables.

A plot that fails 1 or 2 (a corridor cut a second way in on its way past) is **demoted to an
ordinary room** rather than discarded: the space is already carved and connected, and a room
the player can walk into beats a hole in the map. Four spare plots are sampled beyond the
count wanted so a demotion does not cost the dungeon a treasure room; spares that are not
needed are demoted too. `Room.IsTreasurePlot` records that a room was sampled as a closet
whether or not it kept the role, so a demoted one is still not charged to the room budget.

### Why sampled rather than picked

The first implementation of this locked whichever small dead end the finished map happened
to contain. Measured over 500 seeds at the default settings that left **68% of dungeons with
no locked room at all** — the dead ends a layout produces are mostly already spoken for by
the exit and the key room, and what is left is rarely small. The feature existed and was
almost never seen.

Measured after the change, 500 seeds on the tests' cramped 64×48 map and 200 on the shipped
200×200 one:

| Figure | Cramped (8 rooms) | Shipped (30 rooms) |
|--------|-------------------|--------------------|
| Treasure rooms per dungeon | 2.98 | 3.00 |
| Seeds with all 3 | 98.2% | 99.5% |
| Seeds with fewer than 2 | 0 | 0 |
| Mean floor area | 24.2 cells | 24.8 cells |
| Rooms charged to the budget | 8.00 | 30.00 |
| Rooms breaking any of the three rules | 0 | 0 |
| Dungeons where the exit key is in a treasure room | 100% | 100% |
| Seeds with unreachable cells | 0 | 0 |

### Settings

`DungeonGenerationSettings`: `treasureRoomCount` (0 turns them off, which also sends the exit
key back to an ordinary room), `minTreasureRoomSize` / `maxTreasureRoomSize`,
`treasureMaxArea` as a backstop. Note the interaction with the loop settings measured in
§23 — more corridors means more closets clipped and demoted.
`RoomContentSettings`: `treasureKeyItemId`, plus the treasure loot and chest fields that were
already there — they now apply to every treasure room rather than to the one deep one, which
is worth a retune.

### In-editor checklist for this stage

1. Regenerate the dungeon and look for the magenta **TREASURE** gizmos — small rooms off a
   single corridor — and the amber **KEY (Treasure)** one among them.
2. Walk to one. The door must refuse to open and refuse to be rammed, and the HUD toast must
   name the key it wants.
3. Craft a Door Key, open it: the key is consumed, the door stays open, chests inside.
4. Save inside one, load: the door is still open and the looted chest is still empty.
5. Confirm the golden key is inside a locked room and that lamp fuel never is.

## Stage 23 — A way round: measuring and tuning how much of the map has two routes

"Loops give escape routes" has been in the design since D2, and `extraLoopChance` has been
there to provide them since Stage 1. What was missing was any way to tell whether they
landed anywhere useful. `LoopRatio` counts cycles per room and says nothing about *where*
they are: three loops clustered in one corner leave the rest of the map a tree, and the
number looks the same either way.

### The measurement

`DungeonMetrics.RoomsWithTwoRoutes` — rooms reachable from the hub without crossing a
**bridge**, a corridor whose loss would disconnect the graph. That is the hub's
two-edge-connected component, found with the standard depth-first low-link scan (iterative,
so the settings cannot imply a stack depth) and then flooded over the non-bridge edges.
`TwoRouteRatio` divides it by the rooms that are *supposed* to have a way round.

Treasure closets are excluded from that denominator — every sampled one, not only the ones
that kept the lock, because all of them are held out of the corridor graph's loop edges and
so can never have a second route however the settings are turned up. Counting them would put
a ceiling on the ratio that no setting could lift.

It is printed by the Layout Preview window with everything else:
`rooms with a way round 29/30 (97%)   one way in by design 7`.

### The new setting

`loopCandidateFraction` — the share of the corridors the spanning tree rejected that are
considered as loops at all, shortest first. It multiplies with `extraLoopChance` rather than
replacing it, and the two fail differently: with only the chance raised the map saturates
(at 1.0 every candidate in the pool is already taken and it cannot get more connected), while
with only the pool raised the generator starts admitting corridors that cross half the map to
join two rooms that were never near each other. Shortest-first ordering is what keeps the
second failure at bay for the values below.

### The sweep

40 seeds per row on the shipped map (200×200, 30 rooms), everything else at the shipped
settings:

| `extraLoopChance` | `loopCandidateFraction` | Rooms with a way round | Loops | Corridors | Open ground | Chokepoint cells |
|---|---|---|---|---|---|---|
| 0.094 (what shipped) | 0.25 | 53% | 10 | 46 | 15.2% | 375 |
| **0.35** | **0.35** | **98%** | **51** | **87** | **21.0%** | **226** |
| 0.50 | 0.35 | 99% | 71 | 107 | 23.1% | 233 |
| 0.50 | 0.50 | 100% | 102 | 138 | 27.9% | 223 |
| 0.70 | 0.50 | 100% | 143 | 179 | 31.2% | 230 |
| 1.00 | 0.50 | 100% | 203 | 239 | 35.2% | 230 |

0.35 / 0.35 is what the asset now ships. It is the knee of the curve: it takes the map from
half of it being a tree to nearly all of it having a way round, and it drops chokepoint cells
from 375 to 226 — the corridors where a fight cannot be avoided. Past it the returns are a
percentage point or two, paid for by doubling the corridors and opening a third of the map,
which is a different dungeon rather than a better-connected one.

### The interaction to watch

More corridors means more of them routed past the treasure closets, and a corridor that
clips a closet gives it a second way in and costs it its lock (§22). Measured at the shipped
map: 2.98 treasure rooms per dungeon at the old loop settings, 2.90 at 0.35/0.35, 2.38 at
0.5/0.5 and 2.08 at 1.0/0.5. The spare plots were raised from two to four to absorb it.
**Turning the loop settings up much further needs `treasureRoomCount` or the spares raised
with them**, or dungeons quietly start shipping with one locked room instead of three.

### Final configuration, 200 seeds on the shipped map

| Figure | Value |
|--------|-------|
| Rooms with a way round | 97% |
| Loops / corridors | 50 / 86 |
| Open ground | 20.9% |
| Chokepoint cells | 228 |
| Treasure rooms per dungeon | 2.90 (all three on 91.5% of seeds) |
| Treasure rooms breaking a rule | 0 |
| Dungeons where the exit key is in a treasure room | 100% |
| Rooms charged to the room budget | 30.00 |
| Broken layouts | 0 |

The 4.1 leftover closets per dungeon — plots sampled as spares and demoted — stay in the map
as small unlocked dead-end rooms with ordinary contents. That is the cost of the arrangement,
and it is deliberate: the space is already carved and connected, so the alternative is a hole
in the map or a corridor to a wall.

---

## Stage 24 — The hub was a crossroads: corridor cap, clearance, and its four straight walls

Turning the loops up (§23) made a fault visible that had been there all along. The hub is
the middle of the map by construction, and every shortest-corridor rule in the generator
points at the middle: the spanning tree hangs branches off it, the loop pass adds more, and
corridors between rooms on opposite sides of the map are routed straight past it. Measured
over 60 seeds at the shipped settings, the hub had **8.0 corridors** and **0.83 openings per
hub that no door could be hung in** — some of them the full eight cells of one side. The one
room the player is meant to be safe in read as a junction with holes in it.

Three separate causes, three fixes.

### 1. It collected corridors — `maxHubCorridors`

A new setting, 4 by default. Enforced by pruning after the graph is built rather than by
constraining the spanning tree: a degree-bounded spanning tree is a harder problem than the
one being solved, and the loop edges are added after the tree anyway, so a cap enforced
during it would be exceeded a few lines later. Corridors are dropped longest-first — a long
corridor into the middle of the map is the one that reads least like a door — and only when
the rooms it joined are **still connected without it**, checked with union-find.

That check is what makes the cap a target rather than a guarantee. Over 200 seeds, 78.5% of
hubs come in at or under 4 corridors and the mean is **4.2**, down from 8.0. The rest are
seeds whose loops all landed elsewhere, where the hub keeps corridors the dungeon cannot do
without — the right outcome, since the alternative is a room nothing can reach.

### 2. Corridors ran along its walls — clearance

A corridor passing the hub at one cell's distance opens its whole side into the passage,
and the doorway pass correctly refuses to hang a door there: there is nothing to hang it
between. `CorridorCarver` now keeps a **two-cell clearance** around the hub for corridors
that are merely passing by, and re-rolls the corridor's shape up to six times to find one
that misses. Re-rolling rather than routing around: the shapes a corridor may take are
already a small fixed set — one bend or two, either axis first, the turn anywhere in the
middle third — and one of them usually misses, while a path bending its way around an
obstacle would be a fourth shape that looks nothing like the other three. If none of them
miss, the corridor is carved anyway; a crossed clearance beats a room cut off.

The hub's *own* corridors have to reach it, so for those only the **bends** are kept out of
the clearance. A corridor that turns a corner just outside the hub runs along its wall for
those few cells and opens them — the same fault arrived at from the other direction. Kept
out, the last run comes at the wall straight on and crosses it in one cell: a doorway.

That second half is what did most of the work. Arches per hub, over 60 seeds: **0.83** →
0.45 with the clearance alone → **0.10** with the bends kept out too, and the eight-cell
openings went from 8 seeds in 60 to 1.

### 3. Its corners were being chamfered — no outline detail

`DetailOutlines` was notching every room's perimeter, the hub included. It is now skipped
there, for the same reason it is never shaped and never decorated: the one room that has to
be read at a glance should not have a chamfered corner or a buttress in it to resolve.

### Measured after all three, 60 seeds on the shipped map

| Figure | Before | After |
|--------|--------|-------|
| Corridors into the hub | 8.02 | 4.18 |
| Hub openings with a door | 3.30 | 2.98 |
| Hub openings left as an arch | 0.83 | 0.10 |
| Hubs with no arch at all (200 seeds) | — | 88.5% |
| Hub floor area | 8×8, unnotched | 8×8, unnotched |
| Rooms with a way round | 98% | 98% |
| Treasure rooms per dungeon | 2.95 | 2.97 (all three on 96.7% of seeds) |
| Broken layouts | 0 | 0 |

Nothing else moved: the loop settings, the treasure rooms and the connectivity guarantees
all measure the same on either side of the change.

### In-editor checklist for this stage

1. Regenerate and look at the hub: four straight walls, three or four doorways, a door in
   every one of them.
2. Walk out of each and confirm none of them is a hole into a corridor running past.
3. Check the save station and crafting table still have wall to stand against.

---

---

## 25 — Pillars matched to the walls, an exit floor, and procedural prop art

### The pillar did not belong to the wall it was made of

`PillarTile` was drawn as a smooth disc of `#6E7A76` shaded from the top down to `#252B2A`,
while the wall next to it is `#3B4442` masonry in courses with `#272E2D` joints and a drawn
rim on every exposed side. Three separate mismatches, each enough on its own:

- **Two shades too light.** `PillarColor` was the same value as `WallCapColor`, the lightest
  stone in the palette, so a pillar was brighter than any wall around it.
- **Smooth where the wall is coursed.** No joints, no stones, no chipped corners — the four
  things the wall spends its whole texture budget on.
- **Lit from the top.** The wall tiles deliberately carry *no* faked height, because the game
  is seen from directly above and a lit cap reads as a wall viewed at an angle. The pillar
  did exactly that.

On top of which, everything outside the disc was filled with `FloorColor` — flat placeholder
grey stamped over the hand-drawn floor art from §16, so a pillar came with a square patch of
the old floor around it.

The fix reuses the wall: the pillar is now the `WallTop` masonry clipped to a round plan,
with the same `RimDepth` edge an exposed wall side gets, and **transparent** outside the
column. The painter already floors pillar cells like any other open cell, so the real floor
shows through. `Tile.ColliderType.Grid` is unchanged, so the cell still blocks the whole
square — vision and pathfinding read exactly what they read before.

This matters more than a colonnade would suggest, because `DoorwayNormalizer` builds doorway
jambs out of `Pillar` cells. The mismatch was sitting in the one place the player looks at
head-on.

### The exit room looked like every other room

`DungeonPainter.exitFloorTiles` has existed since the exit door went in, along with
`BuildExitFloorMask` and `PickExitFloor` — and nothing ever generated tiles to put in it, so
the slot stayed empty and the mask returned null. `ExitFloorTile` A–C now fill it: cut
flagstones, two by two per tile with wobbled joints, one tone per slab spread by the golden
ratio, and the wall's mottle over the top so it is recognisably the same stone worked to a
different end. Lighter than the ordinary floor rather than darker, per the palette rule from
§7 — the floor is the lightest thing on screen, so the exit has to sit at the top of that
range to register inside the vision cone at all.

### Props on a magenta checker

`Sack01`, `Sack02`, `Statue01` and `Statue02` all pointed at the same `PlaceholderProp.png`
— a magenta checkerboard — and `Candle01` and `Lamp` at `swirly-circle.png` from the GDS UI
pack. Six props, two stand-ins, nothing that says what any of them is. `EnemyCorpse` was a
blank white square.

`PropArtGenerator` (`Tools ▸ Dungeon ▸ Regenerate Prop Art`) draws all seven from above, in
the dungeon's palette, and re-points each prefab's `SpriteRenderer` at the result:

| Prop | What it draws |
|------|---------------|
| `Sack01` | A tied sack, slumped: an ellipse with a bunched neck, a cord ring and folds radiating from it |
| `Sack02` | The same sack split, with grain spilled to one side |
| `Statue01` | A figure on a round plinth — head, shoulders, arms as three masses separated by hard dark gaps; the plinth is the wall's stone with the same rim |
| `Statue02` | The same statue headless, a rough stump where the neck was, chips on the plinth |
| `Candle01` | A stub in its own pool of wax, with a hot core — hard bands, because it lands about a third of a tile across on screen |
| `Lamp` | A caged lantern: iron ring, three straight bars over lit glass, handle across the top |
| `EnemyCorpse` | Face down, arms out, on what it left on the floor |

**Sizing is what makes it a drop-in.** Every sprite is 32×32 and its pixels-per-unit is
derived from the world size of the stand-in it replaces (2.56 for the checker, 1.28 for the
swirl, 1.0 for the corpse sheet), so no prefab transform is touched and nothing already
placed in a scene changes size. The `Lamp` prefab has 13 instances in `Dungeon.unity`; they
keep their footprint exactly.

The chest was redrawn too, in `ChestSetup`, where it belongs. It was a front elevation — a
body with a lid band along its top edge — which from overhead read as a chest lying on its
back. It is now a planked lid inside an iron-bound frame, two bands across it and a brass
lock plate on the front edge. `ChestSetup.RegenerateChestSprite` is new: `EnsureChestSprite`
deliberately never overwrites an existing file so real art survives a re-run, which also
meant it could never update its own placeholder.

### How this was checked without the editor

The drawing routines are pure — `PixelColor`, `ChestPixel` and `PropArtGenerator.PixelColor`
take coordinates and a `DeterministicRandom` and return a `Color`, touching nothing else —
so they were invoked by reflection from a plain `net8.0` host against Unity's
`UnityEngine.CoreModule`, composited over the generated floor and written out as PNGs. That
is what caught the two drawings that compiled fine and looked wrong: the statue's head sat
*inside* its shoulders and the two merged into one lozenge, and the lantern's bars were
keyed on the angle to the centre, which draws a pinwheel rather than bars.

It cannot check anything past the sprite itself. Import settings, the pixels-per-unit
arithmetic, sorting against the wall tilemap and how any of it looks in the vision cone need
the editor.

### In-editor checklist for this stage

1. **Tools ▸ Dungeon ▸ Regenerate Placeholder Tiles** — redraws the pillar and writes the
   three new `ExitFloorTile` assets.
2. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** — required, and only because `exitFloorTiles`
   is a slot the scene has never had filled. Without it the exit room floors like everywhere
   else and nothing looks broken, which is the failure mode to watch for.
3. **Tools ▸ Dungeon ▸ Regenerate Prop Art** — draws the seven props, re-points their
   prefabs and redraws the chest.
4. Regenerate a dungeon and look at a doorway: the jambs should be the same stone as the
   wall they interrupt, with floor art visible around them, not a grey patch.
5. Find the exit room and confirm the flagstones read as a different surface from inside the
   vision cone — that is the only place the player ever sees them.
6. Check prop sizes against what they were. Nothing should have moved or resized; if
   something did, the pixels-per-unit for that prop is wrong, not its transform.

---

---

## 26 — Light between the pillars, four new decals, door and plank art

### A colonnade cast one shadow instead of eight

`PillarTile` carried `Tile.ColliderType.Grid`, so however small the column was drawn, the
whole cell blocked. `FieldOfView` linecasts against those colliders, so a row of columns
threw one unbroken band of shadow — the light never got between them. Making the column
round in §25 only made the mismatch easier to see.

The tile is now `ColliderType.Sprite`: the collider follows the drawn column, so each one
casts its own shadow with light in the gaps. `PathfindingGrid` overlap-tests a circle at the
cell centre, which the column still covers, so the cell stays unwalkable — nothing about
pathing changes.

**That change alone would have put holes in every doorway**, and this is the part worth
remembering. `DoorwayNormalizer` walls up the excess of a wide opening and makes the couple
of cells nearest the door `Pillar` rather than `Wall` — purely cosmetic when it was written,
because "both are solid and both stop vision". With a sprite-shaped collider that stopped
being true: jambs would have become round columns with daylight around them.

So the painter now tells the two apart. A `Pillar` cell touching solid structure
orthogonally is part of the wall mass and gets the wall's tile and its full-cell collider; a
`Pillar` cell standing clear on all four sides is a column and gets the round tile. Diagonals
deliberately do not count — two columns touching at a corner still have a gap light gets
through, and calling that pair a wall would close it.

The upshot is that doorway jambs now read as built wall with the wall's own rim outlining
them, which is closer to what §17 was after than a round column in a door frame ever was.

### Four decals that are not damage

Everything scattered on the floor was wear — two cracks, a stain, chippings. Four additions,
and they are things that *live* down there rather than things that happened to the stone:

| Decal | Notes |
|-------|-------|
| `DecalMushrooms` | A clump of three to six caps, each a half-disc with a shaded underside over a short stem. Clumped, not scattered: three drawn together read as growth, three spread evenly read as three unrelated dots |
| `DecalBones` | Three to five long bones, each a shaft walked end to end with a knuckle at both ends. No skull — at 32 pixels a skull is four pixels of eye socket and reads as a smudge |
| `DecalMoss` | Grown outwards from a point, every pixel kept on a probability falling with distance, so the patch frays at its edge instead of ending on a line. The only real hue in the palette |
| `DecalPuddle` | One body of water with a wandering outline and a sheen along its upper edge. The sheen is the thing that says the surface is wet, and it is on a fixed side so every puddle in a room agrees about where the light is |

`DecalVariantCount` went 4 → 8. Nothing else needed changing: the painter picks uniformly
from `decalTiles`, so they are in the scatter the moment the setup tool fills the array.

Two of them needed tuning after looking at them rather than after reasoning about them. The
moss was drawn so sparse it read as green noise — probability fell off linearly from the
centre, so most of the patch sat under a coin flip; flattening the falloff filled the middle
and kept the frayed edge. The puddle's sheen was invisible at half alpha over near-black.

### The door was a slice of table art

`Door_System`'s leaf pointed at `FURNITURE_pngy_stol_0` — a 984×520 crop of the table
sprite, squashed to a leaf by a scale of 0.102 × 0.22 with another 0.2 × 1 on its parent.
The three barricade stages and the Plank item's icon all pointed at `Plank.png`, a 32×32
white square.

- **`Plank.png`** is now a sawn board: grain along its length, darker end grain at both ends,
  two nail heads. Redrawing that one file re-boards all three barricade stages *and* fixes
  the inventory icon, with no prefab edit at all. Kept square on purpose — it is used at
  three different scales under a parent with a non-uniform scale, so a sprite with a strong
  aspect ratio of its own would come out stretched differently at every stage.
- **The door leaf** is a new 16×64 sprite: three boards along its length, two iron bands
  across them and a ring at the end away from the hinge.

The leaf is the one prop whose transform is recomputed rather than preserved, because its
stand-in had a completely different shape. The generator measures the sprite's world size,
divides out the parent's scale and sets the sprite child's `localScale` to land at
**0.201 × 1.144** world units — what the old setup worked out to. `Door_Visual` is left
alone: it carries the `BoxCollider2D` that blocks the opening, and scaling it would resize
that collider.

### How this was checked, and what it did not check

The decal routines were moved off `Texture2D` onto a plain `Color[]` buffer, and the plank
drawing was split into a pure `PlankPixel`. Both for the same reason the rest of the drawing
code is shaped that way: `Texture2D` is native and cannot run outside Unity, so anything
that touches it cannot be looked at without opening the editor. The blend in the stain case
also stops looking like graphics work once it is plain array access.

Everything in this stage was rendered outside Unity and looked at. What that cannot reach:

- **Whether the sprite-shaped collider is the shape it should be.** Unity generates the
  physics shape from the sprite's tight mesh at import. It has never been looked at.
- **Which end of the door leaf the hinge is on.** The ring is drawn at one end; if
  `Door_Pivot` puts the hinge there, it is on the wrong end and the sprite wants flipping.
- **Which way the barricade planks lie.** `Door_Visual`'s 0.2 × 1 scale squashes anything
  under it fivefold in x, and that transform is not safe to change from here.

### In-editor checklist for this stage

1. **Tools ▸ Dungeon ▸ Regenerate Placeholder Tiles**, then **Setup Scene Tilemaps**, then
   **Tools ▸ Dungeon ▸ Regenerate Prop Art**. The third one also redraws the chest and the
   plank.
2. Stand a player next to a colonnade. **Light must reach between the columns** — that is the
   whole point of this stage. One unbroken band of shadow means the tile is still on `Grid`,
   or the sprite has no physics shape.
3. Walk up to a wide doorway and check the jambs are solid: no light through them, and the
   player cannot slip past the frame.
4. Check a door: the leaf should cover its opening exactly as before, and swing on the same
   hinge. If it is the wrong size, the target in `PropArtGenerator.Props` is wrong, not the
   prefab.
5. Barricade a door and look at the three stages.
6. Regenerate a few times and confirm the new decals turn up without crowding the floor —
   `decalChance` was tuned when there were four variants and there are now eight.

---

---

## 27 — Inked art: item icons, HUD bars, three floor decals

### A second art pipeline, and why there had to be one

Everything generated up to here is 32-pixel art decided one pixel at a time: a chain of hard
tests, point filtering, crisp on a grid. That is right for tiles. It is wrong for an
inventory icon, which is 128 pixels, never tiled, and shown over a near-black panel where a
stair-stepped edge is the first thing the eye finds.

`ProceduralArt` is the second pipeline. Shapes are **signed distance fields** — every
primitive answers "how far is this point from my edge, negative inside" — which gives three
things a per-pixel switch cannot: an anti-aliased edge (coverage is a smoothstep over the
last pixel and a half), an ink line (the band just inside the edge), and a sense of how deep
inside a form a point is, so a wash can pool towards a contour with no lighting model at all.

### The style was wrong on the first pass, and the fix was not "more detail"

The first set came out smooth, rounded, specular-lit — small 3D renders. Technically clean
and completely foreign to the barrel standing next to them.

The project's own furniture (`FURNITURE_pngy_beczka`, `_stol`, `_maszyna`) is hand-inked
illustration, and four things carry that:

| | |
|---|---|
| **Thick ink round every silhouette** | and *uneven along its length*, the way a brush pen is pressed harder in places. A border of constant width reads as a stroke applied by a program however dark it is |
| **Flat desaturated fills** | with a blotchy wash — no gradients anywhere |
| **Detail drawn as strokes** | plank seams, cracks, wrap lines. A change of material is a drawn line, never a fade |
| **Contours that wander** | nothing in the reference is a true circle or a straight edge |

So the shading model was thrown out. There is no specular highlight and no rounded shading
anywhere in `ItemIconGenerator` now: `Wobble` perturbs every contour off its ideal,
`InkWidth` makes the line breathe, and `Wash` is two octaves of blotch plus dirt gathering at
the edge. That is the whole of it.

### What was drawn

**Seven item icons** (`Tools ▸ Art ▸ Regenerate Item Icons`), 128 px at 100 PPU — the same
size and PPU as the GDS icons they replace, so a dropped item keeps its world size. Ink,
bandage, scrap, musket cartridge, shotgun shell, firewood, axe.

Three of them took a second pass because they compiled fine and did not read:

- The **axe** was a sliver. The classic silhouette comes entirely from *where the bites are
  taken*: carving circles out above and below **near the eye** pinches the blade at the back
  and leaves the front edge bulging. Biting the front instead produces a knife.
- The **cartridge** read as a flask with a handle until the ball went to the nose. A musket
  is loaded with paper, powder and ball, and nose-up is the only arrangement that reads as
  ammunition. The cord is drawn wider than the tube on purpose — a tie that stops at the
  edges of what it is tying reads as a painted stripe.
- The **bandage** was a blank oval. Shadowed bands were too soft to survive; three ink
  strokes across the roll do the job.

**Four HUD bar sprites** (`Tools ▸ Art ▸ Regenerate Bar Art`), nine-sliced. The bars were
three flat rectangles with no sprite on any of them — a white panel at 39% alpha, a grey
track, a coloured fill — which is the only UI on screen for the whole game. Now a wrought
iron frame, a groove sunk into it, and a fill sitting in the groove: dried blood for health,
tarnished brass for stamina.

The frame is a band with the middle cut out, which is what makes it work nine-sliced — the
stretched centre is transparent, so no amount of stretching touches the drawn border. Each
image's tint also goes to white, because they carried their colour as a flat tint and leaving
it would multiply the art by it; a red fill tinted red comes out nearly black.

**Three floor decals** (`InkedTileGenerator`, folded into the existing scatter): a smashed
pot, a length of chain, a dead rat. Drawn at **128 px, not 32** — the floor they lie on is
the hand-drawn art at that resolution, and a 32-pixel decal on it is four times chunkier than
the stone it is lying on. They flow into `decalTiles` through `CombineDecals`, so the painter
scatters them with everything else and nothing else had to change.

The chain needed its links overlapping rather than merely spaced; as separate rings it read
as scattered washers. The rat's feet had to be tucked in — splayed, it read as a spider.

### Nothing was deleted

The GDS icons stay on disk untouched; only the `icon` field on seven `ItemData` assets moves.
The bar images keep their objects and their layout. Reverting any of it is dragging the old
sprite back.

### Rubble and the exit floor, measured against the floor they lie on

Seen in game, the collapse in a room came out as a pale blue-grey rectangle of static
dropped onto the ground. The cause was a number nobody had ever checked: the hand-drawn
floor art averages **`#3E3B33`**, a dark warm brown, and the generated tiles sitting on it
were

| Tile | Was | Against a `#3E3B33` floor |
|------|-----|---------------------------|
| `RubbleTile` | `#39413F` / `#4E5855` speckle on a 4 px lattice | cool grey, up to twice as light |
| `ExitFloorTile` | `#6A746F` flagstones | *deliberately* lighter, per the §25 reasoning — and against the real art that reads as a pale patch, not a change of surface |

Both are redrawn in `InkedTileGenerator` at 128 px, keyed to the measured floor colour:

- **Rubble** is nine broken blocks on a jittered three-by-three grid, each rotated, with the
  floor showing between them — which the 32-pixel version could not do at all. The painter
  floors rubble cells like any other non-wall cell, so the gaps have real ground under them.
  Free scatter was tried first and rejected: it leaves some tiles half empty, and a rubble
  tile that is half empty stops reading as rubble.
- **The exit floor** is one laid flagstone per cell in mortar, inset so the drawing never
  touches the cell border. A course of slabs running *across* cells cannot work here — the
  painter picks the variant per cell from the seed, so neighbours are not the same tile and
  could never be made to line up. One slab per cell is the only pattern that tiles under a
  random pick. It says "different room" by being **worked**, not by being brighter.

`InkedDecalGenerator` was renamed `InkedTileGenerator`, since it now draws floor tiles as
well as decals. The 32-pixel `EnsureExitFloorTiles` went with the change; its two
`TileStyle` cases stay, the way `TileStyle.Doorway` did when the painter stopped using it.
The old PNG and `.asset` files are left on disk untouched, simply unreferenced.

### The corpse was a third the size of the thing it was the corpse of

Same class of mistake, found the same way — by measuring instead of eyeballing. The opaque
bounding box of each sprite, in world units:

| | visible size |
|---|---|
| skull guy, standing | **1.66 × 2.38** |
| barrel | 0.91 × 0.91 |
| enemy corpse | **0.78 × 0.84** |

`EnemyCorpse`'s stand-in sheet happened to be one world unit, and §25's rule was "keep every
prop at the size of the sprite it replaces" — so the rule faithfully preserved a size that
was wrong to begin with. Its `WorldUnits` is now 2.6, putting the drawn body at about 2.2
units: a shade shorter than the enemy stood up, which is right for something lying spread
out. It is the one prop in that table deliberately *not* kept at its old size, and the spec's
doc comment now says so.

### In-editor checklist for this stage

1. **Tools ▸ Art ▸ Regenerate Item Icons** and **Tools ▸ Art ▸ Regenerate Bar Art**.
2. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** — picks up the three new decals and rewires
   the rubble and exit-floor slots onto the new 128-pixel tiles.
3. Generate a dungeon and find a collapse. It should read as broken blocks lying on the
   floor, with ground visible between them — not as a filled rectangle of any colour.
3. Open the inventory and look at the seven icons **against the slot backgrounds**, which is
   the only place they are ever seen. They were drawn over that colour but never shown on it.
4. Look at the bars at their real size. The nine-slice borders were checked by emulating the
   slicing outside Unity, not by Unity doing it — if the corners look stretched, the border
   values in `BarArtGenerator` are wrong, not the art.
5. Take damage and spend stamina, and check the fills read at low values, where only the
   left-hand slice of the sprite is visible.
6. Regenerate a dungeon and find the new decals. There are now eleven variants in the
   scatter; `decalChance` was tuned when there were four.

---

---

## 28 — The rat got up and walked off

### A decal was the wrong container for the one thing that moves

`DecalRat` was drawn in Stage 27 and painted flat into the decal tilemap with the cracks,
the moss and the chippings. It was the best-read drawing of the three and the least worth
having, for a reason none of the others share: a dungeon's cracks are meant to sit still,
and a rat is the only thing on that list that a player would expect to react to them being
there. Painted into a tilemap it cannot. A tile is a picture of a cell, not an object — it
has no position of its own to change, no update, and nothing to hold a behaviour.

So the rat became two things that used to be one.

**A live rat** is now an object: `Assets/Prefabs/Rat.prefab`, a sprite and
`WanderingRat`, built and drawn by `RatSetup` (`Tools ▸ Art ▸ Regenerate Rat`). It dashes a
metre or two, sits still for a second or three, and bolts when the player comes within
three units. It is drawn from directly above with its nose along +X, and the component
rotates it into whichever direction it is travelling.

**A dead rat** stays a decal, redrawn: lying on its side, with all four legs out the same
way, bent at the joint and curled at the toes. The pose is doing all the work. The first
attempt kept the Stage 27 top-down body and only splayed the legs, and it read as a rat
*standing* — legs fanned out under a body are a stance however dead the rest of the drawing
is. Parallel legs are not a stance, which is the whole point: no living animal holds all
four in one direction.

Keeping both is the reason the redraw was worth doing at all. Two rats in a cellar, one of
which is scurrying and one of which is not, say something about the place; one rat that is
sometimes a decal and sometimes an object would just look like a bug.

### What it does not have

No collider, no health, no interaction, no `SaveableEntity`, no guid. Under this project's
convention only walls block movement and sight, and a rat is not a wall — see
`project_vision_blocking_convention`. Nothing about where a given rat stood is worth
restoring across a save, so nothing tries to.

Which floor it may cross is asked of `PathfindingGrid.IsWalkableWorld` rather than of
physics, because there is no collider to be stopped by anything. That answers the wall
question and the prop question in one call: the grid samples obstacle colliders when it is
built, so a rat will not scurry through a barrel either. A scene with no grid at all (the
hand-built ones) gets a rat that wanders freely rather than one that refuses to move.

### Spawning

`DungeonPopulator.SpawnRats`, one roll per room: `ratChancePerRoom` (0.3) decides whether a
room has any, then one or two of them. Not the exit room — the run ends the instant the
player steps into it.

Deliberately **not** routed through `FreeCells` like every other spawn. That list is the
room's placement budget, and a cell taken out of it is a cell no chest or prop can use. A
rat occupies nothing: no collider, and it has walked off its spawn cell within seconds. Two
of them sharing a tile for one frame costs nothing, while spending real floor on scenery
that moves would make rooms measurably emptier of the things the player can use. It is also
outside the `authored` guard that suppresses random props in a templated room — a rat is not
part of anyone's arrangement of a room, which is exactly why an authored interior has no
reason to exclude it.

### How this was checked

Both drawings were rendered outside Unity and looked at, by the reflection route in these
notes: compile the project to a library with Roslyn, invoke the private `DrawRat` on a real
`ArtCanvas`, composite the result over the floor's mid stone and write a PNG by hand. That
is what caught the dead rat reading as a standing one, which nothing short of opening the
editor would otherwise have shown. `RatSetup.DrawSprite` was split so the drawing is a pure
`ArtCanvas` method with no asset database in it, which is the shape that stays checkable.

The compile is clean. Nothing about the movement itself has been run — `WanderingRat` needs
a play session and a human's eyes.

### In-editor checklist for this stage

1. **Tools ▸ Art ▸ Regenerate Rat**. Draws `Assets/Generation/Props/Rat.png`, builds the
   prefab and binds it as `critter.rat`. **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** does the
   same on a fresh checkout, but only when the prefab is missing.
2. **Tools ▸ Dungeon ▸ Regenerate Placeholder Tiles** for the dead rat — an ordinary setup
   run leaves existing art alone, so without this the old top-down decal stays on disk.
3. Regenerate a dungeon and watch a lit room for a few seconds. The rats should read as
   moving at the edge of the vision cone before they read as rats.
4. Walk at one. It should give ground and keep giving it, and it should not walk into a
   wall, through a barrel, or out of a doorway it could not fit through.
5. Check the size against a barrel. The sprite is 0.6 units in the prefab and the content
   root scales it 2×, so a rat is a little over half a cell nose to tail.
6. Find a dead one on the floor and check it does not read as a live one that has stopped.

---

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

---

## GU-0076 — Lamps outside the hub, and glass that looks like glass

Two content changes, both in the generator's tables rather than in its algorithms.

**Lamps are no longer hub-only.** `RoomContentSettings.lampChancePerRoom` (0.55) is rolled once
per lamp up to `maxLampsPerRoom` (2) for every ordinary room, and `DungeonPopulator.SpawnRoomLamps`
places them with the same wall-hugging fixture placement the hub uses — before the room's props,
since both compete for the cells along a wall and a lamp stranded in the middle of a room is the
worse outcome. A lamp that finds no cell is skipped without a warning: it makes a room darker, not
a run unplayable. The lighting side of this (colour, fade, the held torch) is in `LIGHT_NOTES.md`.

**The pillar's collider is round at last.** `PillarTile` has always been drawn as a disc and
always carried `Tile.ColliderType.Sprite` so the collider would follow it — but a sprite with no
authored physics shape falls back to one Unity generates, and that fallback is a box the size of
the whole sprite. Every free-standing column was therefore colliding, and cutting the player's
field of view, as a full square cell: a colonnade cast one unbroken band of shadow with corners
none of the drawn columns have. `DungeonSceneSetup.AssignCircularPhysicsShape` now writes an
explicit 16-sided outline of `PillarRadius` through the sprite data provider API, so the shadow
matches the stone. Run **Tools ▸ Dungeon ▸ Setup Scene Tilemaps** (or Regenerate Placeholder
Tiles) to apply it to the tile already on disk.

**`prop.brokenglass` has real art.** `Editor/BrokenGlassArt.cs` breaks a pane at one point, radiates
cracks from it, pulls the wedges apart and kicks a few of them clear, then powders the gaps — four
variants, drawn deterministically from the variant's name. `World/SpriteVariant.cs` on the prefab
picks which one an instance draws, and its rotation, by hashing its own spawn position, so the
clusters the prop table places read as several things broken rather than one picture stamped three
times. Deterministic on purpose: the same seed rebuilds the same debris, and none of it is saved.
