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
| 7 — Concept-art pass | in progress: scale, void, thresholds, decals done; Rule Tiles blocked on art | see §7 below |

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
