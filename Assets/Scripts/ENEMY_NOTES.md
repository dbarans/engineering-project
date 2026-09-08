# Enemy System — Notes

Working reference for the Enemy system, maintained across branches (`GU-0032-enemy-sound-tracking`, `GU-0033-enemy-investigate-linger`, ...) and kept up to date whenever Enemy-related code changes are committed.

## Idle enemies moan — `enemy.idle`

`EnemyBase` now plays an ambient moan every 4-6 s while it has **not** noticed the player,
gated on `idleSoundRadius` (26 units, gizmo `GizmoRanges.EnemyIdleSound`) — deliberately
wider than the player's own `FieldOfView.viewRadius` (15), because hearing what you cannot
see is the point. Four clips in `Assets/Audio/Enemy/Idle/`, picked at random per play.

The audible reach is **two** numbers and the smaller wins: this gate, and the `enemy.idle`
entry's `maxDistance` (30) where the rolloff hits silence. Move them together or neither
moves.

It sits on `EnemyBase` rather than on a component of its own **on purpose** — enemies are
spawned by `DungeonPopulator` and restored by `SaveManager`, so a component that has to be
dragged onto a prefab is one the next enemy type will silently be missing (the same trap
GU-0051 documents for `player`). Set `idleSoundRadius = 0` to opt an enemy out.

Everything except `FollowPlayer` counts as idle, investigating included: that state already
has `enemy.alert` and `enemy.attack`, which are the cues that say *you have been seen*, and
a moan over them blurs them. Full reasoning, and the tuning knob for how dense the moaning
feels, in `AUDIO_NOTES.md` 2a.

The radius does **not** touch detection — no AI behaviour changes at that boundary, it only
decides whether the sound is worth playing at all.

## The alert bark is latched per hunt

`EnemyBase.UpdateAlertAudio()` replaced the old `stateBefore != FollowPlayer` transition
check. `hasAlertedThisHunt` latches when the enemy barks and clears only in `Idle`,
`ReturnToPatrol` or `WanderNearLastPosition` — the states that mean it gave up. Investigating
does **not** clear it, because picking a trail back up is the same hunt.

Without the latch, a player using cover the way `PlayerHiding` intends re-entered
`FollowPlayer` every `detectionMemoryDuration` (1.5 s) and got barked at each time.

**`stateBefore` is gone from `UpdateStateMachine()`** — it existed only for that check.
Anything needing a "state changed this tick" signal has to reintroduce it.

## GU-0051: runtime-spawned enemies get their player injected

The procedural dungeon generator (`Generation/`, see `GENERATION_NOTES.md`) spawns enemies that exist in no authored scene. `EnemyBase.player` is a serialized `Transform`, and **a prefab asset cannot hold a reference to a scene object** — so a spawned enemy started with `player == null`, and `IsPlayerDetected()` returns `false` immediately on null. The enemy would patrol forever and never react to anything, with no error anywhere.

Two paths now cover it:

- **`EnemyBase.SetPlayer(Transform)`** — explicit injection, called by `DungeonPopulator` right after `Instantiate`. This is the normal path; the populator already resolved the player once for the whole build, so it costs nothing per enemy.
- **`EnemyBase.Start()`** — falls back to `GameManager.GetPlayer()`, then to `FindFirstObjectByType<PlayerMovement>()`, but **only when `player` is still null**. This covers enemies that `SaveManager` respawns from the `PrefabRegistry` on load, where the save layer has no business knowing what a player is. Scene-placed enemies keep their inspector reference and skip it entirely.

Enemies spawned without waypoints are already handled by the existing patrol code — they wander around their spawn position.

## Hiding under a table — concealment beats every detector

New `Table.prefab` (art `Art/FURNITURE_pngy_stol.png`): a barrel-like obstacle while standing, a hiding spot while crouching (Ctrl — crouch is the existing `Sneak` movement mode, not a new action).

**`Interfaces/IPlayerConcealment.cs`** (`bool IsConcealed`) — implemented by `Player/PlayerHiding.cs` on the Player prefab. `EnemyBase.IsPlayerDetected()` checks `IsPlayerConcealed()` **first**, before `alwaysDetectRange` and before any `IPlayerDetector`, so a hidden player cannot be found even point blank. The reference is resolved lazily off the serialized `player` transform and re-resolved if that transform is reassigned. Hearing needed no change: crouching is already silent in `PlayerNoiseEmitter`, and crouch cannot be released while hidden. A chase already in progress still lingers for `detectionMemoryDuration`, then investigates the last known position and gives up — the intended stealth behaviour, not a bug.

**Layer `CrouchPassable` (12)**, added to `ProjectSettings/TagManager.asset`. The table is split across two colliders because "blocks pathfinding", "blocks a standing body" and "counts as hidden" are three different shapes:
- **Root `Table`, layer `ObstaclePathOnly` (11), trigger box over the full sprite** — the concealment footprint (`World/CrouchHideout.cs`), and the pathfinding blocker. Reusing layer 11 is deliberate: `PathfindingGrid.obstacleMask` already includes it for `Barrel`, so **no scene-side mask edits are needed**, and `FieldOfView.obstacleMask` (768) still ignores it, so vision passes over the table and the FOV stencil covers the ground under it (see the Barrel gotcha in GU-0036).
- **Child `Solid`, layer `CrouchPassable` (12), non-trigger box inset 0.12 local units per side** — the physical obstacle. `PlayerHiding` adds layer 12 to the player collider's `excludeLayers` while crouching and removes it otherwise, so a crouching player walks through and a standing one is blocked. Enemies never exclude it, so the table blocks them at all times.

The inset matters: the player only ever leaves a hideout on `OnTriggerExit2D`, and the solid box is smaller than the trigger, so by the time crouch unlocks the player is provably clear of the box it is about to re-collide with. 0.12 local ≈ 0.03 world at the prefab's 0.25 scale — 3× the 0.01 default contact offset.

**Crouch lock.** While `PlayerHiding.IsHidden` (inside a hideout *and* in Sneak), `PlayerInputHandler.CrouchLocked` keeps the player crouched: releasing Ctrl, or draining stamina mid-sprint, cannot stand them up into the table, and sprint is refused. They leave by walking out; `PlayerHiding.InHideoutChanged` then makes the input handler re-derive the mode from the keys actually held (`ApplyMovementModifierState`, extracted from `OnMovementModifierCanceled`). Overlapping hideouts are counted, not flagged, so walking from one table straight into another never un-hides the player mid-step.

`PlayerMovement` gained `MovementModeChanged` (raised only on a real change) so `PlayerHiding` can toggle passability without polling.

The table's `SpriteRenderer` sits at sorting order 1, above the player's 0, so it covers the player underneath. Since a standing player can never overlap the solid box, this only ever draws over the player while they are hidden.

**The table deliberately does NOT use `SpriteFovMasked` / `FovMaskedSpriteRuntime` — do not "fix" it to match `Barrel`.** It carries the plain `Sprite-Lit-Default`, like the floor and wall sprites, so outside the FOV it is dimmed by `DarknessOverlay` rather than hard-clipped away. It was first built as a copy of `Barrel` (masked + clipped) and that was wrong: a table is a *hiding spot*, and clipping it means the player cannot see anywhere to hide until it is already inside the vision cone, which fights the stealth loop. This is the GU-0036 rule applied as written — only "occupants" of the world (enemies, items) vanish completely; environment and furniture are dimmed.

## GU-0036: removed HideableObject — enemies/items are now stencil-clipped at the vision boundary

`Vision/HideableObject.cs` toggled `renderer.enabled` on a single-point `FieldOfView.IsVisible()` check, which hid the whole sprite at once — a hard pop. Removed it entirely (from `SkullGuyEnemy.prefab`, `BlindListenerEnemy.prefab`, the added-component overrides in `Dominik.unity`/`Maks.unity`, and the `Ensure<HideableObject>(go)` call in `Editor/SkullGuySetup.cs`).

Relying on `DarknessOverlay` alone was not enough: its alpha is 0.97, so the covered half of an enemy was merely *darkened*, still faintly visible through the fog. The requirement is a hard per-pixel cut — an enemy straddling the boundary shows only its visible half, the shadow half not at all. Implemented with a stencil prepass:

- **`Shaders/FovStencilPrepass.shader`** — draws the FOV mesh with `ColorMask 0`, writing only stencil = 1 over the visible area. Rendered *before* regular sprites: `FieldOfView.CreateStencilPrepass()` (called from `Awake`) creates a child renderer sharing the FOV mesh, with `stencilPrepassSortingOrder` (default −10, serialized) below every sprite.
- **`Shaders/SpriteFovMasked.shader` + `Materials/SpriteFovMasked.mat`** — an unlit URP 2D sprite shader with `Stencil { Ref 1 Comp Equal }`: pixels outside the FOV stencil are not drawn at all. Assigned as the sprite material on `SkullGuyEnemy`, `BlindListenerEnemy`, and `Barrel` prefabs (any future hideable object just needs this material).
- Environment sprites (floor, walls) keep their normal material on purpose — they are dimmed by the fog (`FovMaskWriter` edge fade + `DarknessOverlay` at 0.97 alpha), not clipped. Only "occupants" of the world vanish completely outside the player's vision.
- Draw order per frame (all sorting layer `Default`): stencil prepass (−10) → sprites (0; masked ones clip against stencil) → `FovMaskWriter` FOV mesh (5; rewrites stencil, fades the edge) → `DarknessOverlay` (6; solid dark where stencil ≠ 1).

If per-object visibility culling is ever needed for performance, it should disable renderers only well outside `viewRadius` (margin + hysteresis) — never drive the visible edge.

**Gotcha found while wiring up `Barrel.prefab`:** it was on layer `ObstacleStatic` (8), the same layer both `FieldOfView.obstacleMask` (vision raycasts) and `PathfindingGrid.obstacleMask` (walkability) test against. With the old darken-only masking this was invisible (the sprite still rendered, just dimmed), but with hard stencil clipping it made the barrel disappear everywhere — the FOV raycasts stopped at the barrel's own collider, so the mesh never covered the ground under it and the stencil test always failed.

Moving it straight to `Default` fixed visibility but silently broke pathfinding too, since `Default` isn't in `PathfindingGrid.obstacleMask` either — enemies started walking straight through it. `FieldOfView` and `PathfindingGrid` had accidentally been sharing one mask (both defaulted to `ObstacleStatic | ObstacleDynamic`), so there was no existing layer meaning "blocks movement but not vision."

Fixed properly with a new layer, **`ObstaclePathOnly` (11)**, added to `ProjectSettings/TagManager.asset`:
- `Barrel.prefab` → layer `ObstaclePathOnly`.
- `PathfindingGrid` (in `Dominik 04.unity`) → `obstacleMask` extended from `768` (`ObstacleStatic|ObstacleDynamic`) to `2816` (+ `ObstaclePathOnly`), so it still blocks enemy paths.
- `FieldOfView.obstacleMask` left at `768` — unchanged, so vision still passes over it.

Any future small prop that should physically block movement without blocking vision (per the vision-blocking convention — only walls/trees block vision) belongs on `ObstaclePathOnly`, not `ObstacleStatic`/`ObstacleDynamic`. If a scene has its own `PathfindingGrid` instance, remember its `obstacleMask` is a per-instance serialized value, not the script default — new scenes need `ObstaclePathOnly` added to that mask too.

**`_MainTex_ST`/`TRANSFORM_TEX` is REQUIRED — do not remove it.** `SpriteFovMasked.shader` must keep `float4 _MainTex_ST` in its `CBUFFER` and `OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex)` in the vertex stage. Removing it (attempted twice, to silence the "2D SRP Batcher disabled" console warning below) makes these sprites vanish entirely — at least the `Barrel` sprite has a non-identity UV rect that only maps correctly through `_MainTex_ST`. The SRP Batcher warning it triggers is purely cosmetic (perf-only; batching disabled for this one material) and is the accepted cost of this material. **Do not chase that warning by touching `_MainTex_ST` again.**

**Actual cause of "sprite invisible everywhere, not just outside FOV":** the stencil test in `SpriteFovMasked.shader` (`Stencil { Ref 1 Comp Equal }`) only ever passes where `FieldOfView`'s stencil prepass wrote stencil = 1 — and that prepass is created and updated by `MonoBehaviour.Awake()`/`LateUpdate()`, which only run in **Play mode**. In the Editor (Scene view outside Play, Prefab isolation view, Project window thumbnails) nothing ever writes the stencil, so `Comp Equal` always fails and the sprite renders nothing at all — not a bug in the masking logic itself, just no "vision" existing yet in those contexts.

Fixed by making the comparison mode itself a per-material property, `_StencilComp` (defaults to `Always = 8` baked into `Materials/SpriteFovMasked.mat`, so Editor contexts show the sprite normally), referenced in the Stencil block as `Comp [_StencilComp]`.

First attempt drove this per-instance via a `MaterialPropertyBlock` (`Vision/FovMaskedSpriteRuntime.cs` calling `SetInt("_StencilComp", ...)` in `Awake()`) — **this does not work**: property blocks only override values actually read from the shader's `CBUFFER` at the HLSL level; a fixed-function `Stencil { Comp [_Name] }` reference is resolved from the material's baked serialized state, not re-evaluated per draw call, so the block silently did nothing and every sprite stayed fully visible (`Comp Always`) even at runtime.

Fixed properly with **two materials, same shader**: `Materials/SpriteFovMasked.mat` (authored default, `_StencilComp` unset → shader default `Always`, used at design time) and `Materials/SpriteFovMaskedClipped.mat` (`_StencilComp: 3` i.e. `Equal`, baked in). `FovMaskedSpriteRuntime.Awake()` now just swaps `SpriteRenderer.sharedMaterial` to the clipped variant — a real material asset, so the baked fixed-function state is honored. `clippedMaterial` is a serialized field, wired on `Barrel`, `SkullGuyEnemy`, and `BlindListenerEnemy` (same GameObject as the `SpriteRenderer` in each case — the `EnemyModel` child for the two enemies, root for `Barrel`). Any future `SpriteFovMasked` sprite needs this component + the clipped material reference too, or it will only ever show the design-time (fully visible) material, even in Play.

## GU-0033: post-investigation behavior (return to patrol vs wander)

Previously, every investigation (`InvestigateLastKnown`/`InvestigateNoise`) that ran out without re-detecting the player always ended the same way: `SelectClosestWaypoint()` + `ReturnToPatrol`. Now this is a per-enemy choice:

- **`EnemyBase.postInvestigateBehavior`** (`PostInvestigateBehavior` enum: `ReturnToPatrol` / `WanderNearLastPosition`; `Randomized` added later, see GU-0088) — Inspector field under "After losing the player". Read by the new `EndInvestigation()` helper, which both `InvestigateLastKnown` and `InvestigateNoise` call once they reach their target without re-detecting the player.
- **`WanderNearLastPosition`** (new `EnemyState`) — instead of returning to fixed patrol waypoints (which don't make sense for an arbitrary spot where the player was lost), the enemy repeatedly picks a random point within `wanderRadius` of `wanderAnchor` (captured as the enemy's position the moment investigation ended) and walks there at normal `moveSpeed` (not chase speed). On arrival, picks a new random point — indefinitely, until it re-detects the player (→ `FollowPlayer`) or hears a fresh noise (→ `InvestigateNoise`, same as every other passive state).
- Unreachable wander targets are handled the same way as unreachable patrol waypoints: `ResolveUnreachableWanderTarget()` checks `IPathStatusProvider.HasReachablePath` on a throttled retry (`unreachableWaypointRetryInterval`) and repicks.
- Magenta gizmo (`wanderRadius`, drawn from the enemy's current position) shown when `postInvestigateBehavior == WanderNearLastPosition`, alongside the existing red `alwaysDetectRange` circle.
- Save/restore: `WanderNearLastPosition` is transient like the other investigate states — not persisted; `RestoreSaveState` maps it back to `ReturnToPatrol` (same reasoning as `InvestigateLastKnown`/`InvestigateNoise`: the private target isn't saved, and heading for the nearest waypoint is indistinguishable to the player). Appended last in the enum so old saves keep decoding correctly.
- No prefab/scene YAML edits needed — new fields just take their C# defaults (`ReturnToPatrol`, `wanderRadius = 4`) until set in the Inspector.
- **Enemies with no waypoints configured no longer stand still.** `EnemyBase.homePosition` (then named `spawnPosition`) is captured in `Awake()`. In `Idle`, if `!HasValidWaypoint()` (no `waypoints` assigned), the enemy sets `wanderAnchor = spawnPosition` and enters `WanderNearLastPosition` — reusing the exact same wander mechanic, just anchored at spawn instead of the last-seen-player spot. This runs continuously (no exit back to `Idle` on its own), and is independent of `postInvestigateBehavior`: a waypoint-less enemy that loses a chase still ends up back in `Idle` first (`ReturnToPatrol` immediately falls through to `Idle` when there's no valid waypoint), then resumes wandering from its spawn point, not from wherever the chase ended.

## GU-0088: idle variety (guards vs roamers, and where they go after a chase)

Every waypoint-less enemy used to behave identically: roam around its spawn forever, and always
end a lost chase the one way its prefab was configured. A dungeon full of one prefab therefore
read as one enemy copy-pasted. Two per-enemy choices now vary it:

- **`EnemyBase.idleBehavior`** (`IdleBehavior`: `Wander` / `Guard` / `Randomized`, default `Randomized`,
  `guardChance = 0.4`) — only applies to enemies with **no waypoints**; a configured patrol route
  still wins. `Guard` holds `homePosition` and never roams; `Wander` is the old roam-around-the-post
  behaviour.
- **`postInvestigateBehavior`** gained `Randomized` (appended last, saved ints stay valid) with
  `wanderAfterLosingChance = 0.5`. A resolved `Guard` always overrides it to `ReturnToPatrol` —
  otherwise a single chase would permanently relocate a guard off the spot it was placed on.
- **Rolls are hashed, not `Random`** (`PersonalityRoll(salt)`): a Murmur-style mix of the post
  position, so the answer is identical every frame, across a scene reload, and across save/load,
  yet different per enemy. Neighbouring posts get decorrelated values, so a corridor of enemies
  does not come out uniform.
- **`spawnPosition` → `homePosition`**, now also persisted (`EnemySaveState.homePos`). Runtime-spawned
  enemies get their position from the save *after* `Awake` ran, so without it a loaded guard would
  treat the instantiation point as home — and, since the roll is seeded from the post, would also
  change personality. Old saves (null `homePos`) keep the post picked up on spawn.
- **`ReturnToPatrol` without waypoints now means "walk back to the post"** instead of falling
  straight through to `Idle`: `GetTargetPosition` returns `homePosition`, `ShouldMove` allows the
  walk, and arrival (`IsAtPost()`, `EffectiveWaypointReachedThreshold`) hands over to `Idle`. This is
  what makes "returns to its place" actually visible for enemies that never had a route.
- **Losing the player with `skipInvestigateWhenLostPlayer`** now goes through `EndInvestigation()`
  too, instead of hardcoding `SelectClosestWaypoint()` + `ReturnToPatrol` — otherwise the
  wander-where-you-lost-him branch could only ever be reached via a noise investigation.
- Gizmos: cyan post marker + line to it for a resolved `Guard`; magenta `wanderRadius` circle for a
  roamer, drawn around the live `wanderAnchor` while wandering and around the post otherwise.
### Searching where the trail went cold (and why the enemy used to freeze)

`SkullGuy` would stop dead after the player escaped. Two causes, both fixed:

1. **An investigation could never end.** `InvestigateLastKnown` / `InvestigateNoise` left their state
   *only* by getting within `investigateArrivalThreshold` of the target. Nothing handled a target the
   pathfinder cannot reach (behind a door, inside a wall) — unlike patrol waypoints and wander points,
   which already had `Resolve...` helpers — so the enemy stood on a dead path indefinitely. Added
   `ResolveUnreachableInvestigateTarget()` (same throttled `IPathStatusProvider.HasReachablePath`
   pattern) plus `investigateTimeout` (8 s) as a safety net, armed on the tick the enemy enters an
   investigate state and refreshed whenever a fresh noise moves the trail forward.
   `GetInvestigateTargetPosition()` also drops the `investigateOvershootDistance` when the overshot
   point is not walkable — the overshoot exists to clear doorways and corners, which is exactly where
   it lands in a wall. `investigateArrivalThreshold` went 0.35 → 0.7 on both prefabs: the A* route ends
   at a **cell centre** (`cellSize 0.55`, so up to ~0.39 away from the real target), and 0.35 could not
   be satisfied at all on an off-centre target.
2. **Nobody searched.** Reaching the last-known position ran `EndInvestigation()`, which immediately
   sent the enemy home or froze it there. Now `EndInvestigation()` **always** starts a search:
   `WanderNearLastPosition` anchored where the trail died, for `searchDuration` (8 s SkullGuy / 10 s
   BlindListener). Only when that timer runs out does the per-enemy choice apply — head back to the
   post (`searchEndTime = now + searchDuration`) or adopt the spot for good
   (`searchEndTime = Infinity`, the roamer that rolled `WanderNearLastPosition`). The idle
   roam-around-home branch also sets `Infinity`, since home is not something to give up on.

### Sweeping onward instead of searching on the spot (`SearchAhead`)

Poking around in circles where the trail died still read as an enemy that had given up. What a
guard would actually do is carry on the way you went. `EnemyState.SearchAhead` (appended last in the
enum) does that, and it is now what `EndInvestigation()` starts; the random wander-search survives
only as the fallback for an enemy with no `IWalkabilityProbe` behind it.

- **Heading**: `UpdateTravelDirection()` keeps a note of which way the enemy is actually moving,
  sampled over distance (0.2 units) rather than per tick so a slow walk does not turn into noise.
  The sweep starts on that heading, snapped to a cardinal - dungeon corridors are axis-aligned, so a
  diagonal would only ever cut a corner into a wall.
- **Steps**: each step aims `searchStepDistance` ahead. On arrival `AdvanceSearchStep()` re-probes
  the four cardinals (never the way it came from - backtracking would make every corridor a
  junction) with `IsPassageOpen`, three samples deep so a door frame or pillar partway along does
  not read as open ground.
  - one way on (straight, or round a bend) - not a decision, keep **running** at chase speed;
  - two or more - a junction: pick one at random, and from there it is guessing rather than
    chasing, so `searchIsWalking` flips and it drops to **walking** speed;
  - none - dead end, sweep over.
- **Ends** on `searchDuration` (hard cap), on the `maxSearchJunctions`-th fork, at a dead end, or on
  a step the pathfinder cannot reach (`ResolveUnreachableInvestigateTarget` handles `SearchAhead`
  too, re-deciding from where it stands). Then `FinishSearch()` applies the per-enemy choice: adopt
  this patch and roam it, or head back to the post. `hasAlertedThisHunt` deliberately does *not*
  clear during a sweep - it is still the same hunt.
- Gizmo while playing: line to the next step target, red while running, green once walking.

### The coarse-grid arrival bug (why enemies froze in `Dungeon.unity`)

`AStarPathfinder` builds a route out of **cell centres** and never appends the exact requested
point, so an enemy can only ever stop within about half a cell diagonal of its target.
`Dungeon.unity` configures `PathfindingGrid.cellSize = 2`, i.e. up to **1.41 units** off - against an
`investigateArrivalThreshold` of 0.35. The arrival test could not pass at all, and since arrival was
the only exit from an investigate state, the enemy stood still. (`Bartek 01.unity` uses 0.55, where
0.35 merely fails often.)

`IWalkabilityProbe` gained `WalkableSampleSize` (`PathfindingMovement` returns
`PathfindingGrid.CellSize`), and `EnemyBase` derives its distances from it instead of trusting the
authored numbers:

- `EffectiveArrivalThreshold` = max(`investigateArrivalThreshold`, 0.75 cell) - used by every
  investigate, wander and sweep-step arrival test.
- `EffectiveWaypointReachedThreshold()` = same floor, so a guard walking back to its post is not
  chasing a threshold the grid cannot deliver either.
- `EffectiveSearchStepDistance` >= one cell, `EffectiveSearchProbeDistance` >= 1.5 cells - a probe
  landing inside the enemy's own cell reports every direction open, which would make every corridor
  read as a crossroads.

So the full arc is now: lose the player → run at chase speed to where they were last seen (overshoot
past the corner) → poke around that area for several seconds → then either walk back to the post or
settle in - and the middle of that arc is the corridor sweep below, not pottering on the spot.

- Prefabs updated: `SkullGuyEnemy` (`idleBehavior: 2`, `guardChance 0.4`, `postInvestigateBehavior: 2`,
  `wanderAfterLosingChance 0.5`), `BlindListenerEnemy` (`guardChance 0.3`, `wanderAfterLosingChance 0.6`
  — the blind one is more interesting when it keeps moving), and the two scene overrides in
  `Bartek 01.unity` switched to `Randomized`. `SkullGuyEnemy`'s `waypoints` array held 4 empty slots
  (`HasValidWaypoint()` false, but `waypoints.Length == 4` — a trap for any length-based branch);
  emptied to `[]`. Sweep tuning on the prefabs: `searchStepDistance 1.5`, `searchProbeDistance 1.6`,
  `maxSearchJunctions` 3 (SkullGuy) / 4 (BlindListener) - all floored by the grid cell size at
  runtime, so they matter only on a fine grid.

## GU-0088: idle wandering stays in the room, and distant enemies are parked

### The idle stroll across half the dungeon

`PickRandomWanderTarget()` vetted candidates with `IWalkabilityProbe.IsWalkable` alone. A point two
steps past a wall passes that test, and nothing downstream objects: the target is not *unreachable*
(so `ResolveUnreachableWanderTarget` stays quiet, `HasReachablePath` is true), it just costs a walk
around the room, down the corridor and back up the far side. The enemy dutifully takes it and tours
the map on what was supposed to be idle pottering.

- `IsWanderCandidateUsable()` now also requires `IsRouteLocallyClear()` - the straight line from the
  enemy to the candidate must stay on walkable ground, sampled every half navigation cell so nothing
  thinner than a wall slips between two samples. Wandering is a room-scale behaviour; if it needs a
  route, it is the wrong point.
- `wanderTargetTimeout` (6 s) as the recovery net: a wander point not reached in time is dropped and
  another picked, whatever the geometry turned out to be.
- `GetInvestigateTargetPosition()` applies the same line test to the `investigateOvershootDistance`
  point - it used to check only that the overshot point was walkable, which happily accepts open
  ground on the *far side* of the wall the player just ducked behind.

### Culling: parked enemies (`cullDistance`, default 60)

Throttling (`throttledTickInterval`) only slows distant enemies down - each one still runs the full
state machine and a full A* search (up to `maxExploredNodes`, 4000) several times a second, on the
far side of a 200x200 map, for a patrol nobody can see. `UpdateCulling()` runs before everything
else in `Update` and parks an enemy past `cullDistance`: no state machine, no path search, no
movement, one distance comparison per frame.

- The distance is floored at the enemy's own full-update radius (widest sensor + `fullUpdateMargin`)
  plus 4, so parking can never hide an enemy that could still detect the player.
- 10% hysteresis between parking and waking, so an enemy on the boundary does not thrash.
- Parking calls `IPathStatusProvider.ReleaseCachedPath()` (new): `PathfindingMovement` clears *and*
  `TrimExcess`es its route list and forgets its last target. The A* scratch buffers are shared per
  grid (`ConditionalWeakTable` in `AStarPathfinder`), so the per-enemy route list is the only
  pathfinding memory an idle enemy holds - and now it does not hold it.
- Waking resets `lastTickTime`, `nextThrottledTickTime` and the travel-direction sample.
  `tickSpeedScale` compensates movement for a long tick, so without the reset the first tick after
  a park would be scaled by however long the enemy stood parked and teleport it across the room.
- `SkullGuyAnimationDriver` early-returns while `EnemyBase.IsCulled`, and re-bases `lastPosition` on
  the way back, so a parked enemy is not stepping animation frames either and does not read one
  enormous stride on its first frame back.

### `EnemyFootstepAudio` - enemies you can hear walking

New component on both enemy prefabs, playing `SoundId.EnemyFootstep` on a cadence derived from
measured ground speed (`stepInterval * referenceSpeed / speed`, clamped 0.22-1.2 s), so a chase
sounds faster than a patrol. It reuses the player's footstep clip, quieter and pitched down in the
`SoundBank` entry, and positional with a linear rolloff out to 18 units - the falloff is how the
player locates something they cannot see. Skipped entirely while `EnemyBase.IsCulled`. Details and
the reasoning behind the bank values are in `AUDIO_NOTES.md` 2d.

## Sound tracking implementation status

Done:
- **Noise emission system** (`Sound/NoiseEvents.cs`) — global static event bus: anything noisy calls `NoiseEvents.Emit(position, radius)`; listeners subscribe to `NoiseEvents.NoiseEmitted`. Radius = how far the noise carries. Subscribers are cleared on play-mode start (`RuntimeInitializeOnLoadMethod`) in case domain reload is disabled. Extension point for thrown objects, doors, gunfire, distractions.
- `PlayerNoiseEmitter` (on the Player prefab) — emits movement noise every `emitInterval` (0.2 s): silent when standing still or sneaking, `walkNoiseRadius` (4) when walking, `sprintNoiseRadius` (8) when sprinting. Uses `PlayerMovement.CurrentMode` + `IsMoving`.
- `SoundPlayerDetector : IPlayerDetector` — event-driven: listens to `NoiseEvents`, registers a noise when within both the noise radius and its own `hearingRange` cap and not blocked by `hearingBlockerMask`; a heard noise counts as detection for `heardNoiseRetention` (0.35 s) to bridge emission intervals. Since only the player emits noise, any heard noise = player detected.
- `PlayerMovement.CurrentMode` — public getter for the current movement mode.
- `PlayerMovement.IsMoving` — checks actual Rigidbody2D velocity against `movingSpeedThreshold`. Needed because `CurrentMode` alone isn't enough: releasing the sneak key while standing still switches the mode to Walk without the player actually moving.
- `EnemyBase.IsPlayerDetected()` refactored: detection is fully component-based — the enemy queries every `IPlayerDetector` on the object (cached in `Awake` via `GetComponents<IPlayerDetector>()`). There is no built-in vision in `EnemyBase` anymore; sight lives in `VisionPlayerDetector`, hearing in `SoundPlayerDetector`. A blind enemy simply has no `VisionPlayerDetector` attached.
- `BlindListenerEnemy : EnemyBase` — first enemy type with no vision, `[RequireComponent(SoundPlayerDetector)]`, no `VisionPlayerDetector` on the prefab.
- `EnemyBase.alwaysDetectRange` (default 0.5) — player is always detected within this distance regardless of vision/hearing, added after observing that very-close-range detection could fail (line-of-sight raycasts giving false negatives right next to the enemy). Checked first in `IsPlayerDetected()`, before any detector runs.
- `EnemyBase.detectionMemoryDuration` (default 1.5 s) — after all detectors lose the player, the enemy keeps treating them as detected for this long. Prevents the blind enemy from instantly dropping the chase the moment the player stops moving or starts sneaking (and the sighted enemy from forgetting on a momentary line-of-sight break).
- Perf cleanups: `EnemyBase` caches its `Rigidbody2D` (used by `Knockback`).
- **`InvestigateNoise` state + `INoiseSensor`** — hearing is now suspicion, not confirmed detection. `SoundPlayerDetector` implements `INoiseSensor` (`HasFreshNoise`, `LastNoisePosition`) instead of `IPlayerDetector`; a heard noise sends the enemy to investigate the noise position (`InvestigateNoise`, chase speed, updates target as new noises arrive — follows the trail), while `FollowPlayer` requires a real detection (`VisionPlayerDetector` or `alwaysDetectRange`). Blind enemy now confirms the player only at point-blank range. Enum value appended last so saved `aiState` ints stay valid; restore maps it to `ReturnToPatrol` like `InvestigateLastKnown`.
- **Throwable distraction** — `Player/PlayerThrow.cs` (on the Player prefab) throws a "rock" toward the mouse cursor (temporary direct key binding, default G — to be replaced with a proper PlayerControls action in the editor). The projectile (`Sound/NoiseProjectile.cs`) is built entirely in code (runtime-generated circle sprite, Rigidbody2D, collider ignoring the player's own colliders) and emits `NoiseEvents.Emit(landingNoiseRadius = 10)` on first collision or after `maxFlightTime` — luring hearing-based enemies to the landing spot via `InvestigateNoise`.
- **Melee attack as a shared component** — `Enemy/EnemyMeleeAttack.cs` (`[RequireComponent(EnemyBase)]`, on both SkullGuy and BlindListener prefabs): fields `attackRange` (1.2), `attackCooldown` (1.5), `attackDamage` (20), `attackHitDelay` (0.4). When chasing, in range, off cooldown → raises the `AttackStarted` event and lands the hit after `attackHitDelay` if the player is still in range (`PlayerHealthSystem.TakeDamage`), giving the player a dodge window. `SkullGuyAnimationDriver` subscribes to `AttackStarted` to play ATAK; attack logic lived briefly in `SkullGuyEnemy`, which is now slim again (death handling only). `EnemyBase` exposes `public Transform Player` for companion components.
- **Perf pass (unrelated to sound tracking but touches enemy pathfinding): ~30ms → ~12ms/frame in editor.** Root cause was `Vision/FieldOfView.cs` sweeping a full 360° at full ray resolution just to render the small near-vision/lantern circle whenever the cone was narrower than 360° — fixed by sampling the cone at full `raysPerDegree` and the area outside it at a much coarser `nearCircleRaysPerDegree`; cone quality/resolution unchanged. Also: `FieldOfView` reuses its vertex/UV/triangle buffers instead of allocating every `LateUpdate`; `Vision/HideableObject.cs` (since removed, see GU-0036 above) staggered its per-object visibility linecast across `checkEveryNFrames` (default 3) instead of checking every frame; `Pathfinding/AStarPathfinder.cs` caches its scratch grids (gScore/closed/parent arrays) per `PathfindingGrid` instead of allocating them on every `FindPath` call — relevant here because every chasing enemy re-paths roughly every `repathInterval`.
- Prefab `Assets/Prefabs/BlindListenerEnemy.prefab` (cloned from SkullGuyEnemy — visuals/animation are a placeholder, to be replaced).
- Editor tool `Editor/BlindListenerEnemySetup.cs` (**untracked, local-only**) — `Tools > BlindListenerEnemy > Create Prefab From SkullGuy`, clones the prefab, swaps the enemy component, adds `SoundPlayerDetector`. Historical note: the first version destroyed `SkullGuyEnemy` before adding `BlindListenerEnemy`, which Unity silently blocked (because `SkullGuyAnimationDriver` has `[RequireComponent(EnemyBase)]`) and left both components attached at once. Fixed: now `AddComponent<BlindListenerEnemy>` first, then `DestroyImmediate(oldEnemy)`.

Still to do (deferred — "sound will be added later"):
- A real sound-emission system (footsteps, other noise sources) — `SoundPlayerDetector` currently relies purely on `MovementMode`, not actual audio events.
- Dedicated art/animations for `BlindListenerEnemy` (currently reuses SkullGuy's clips).
- Possibly a separate "heard something" animation cue (cf. `RYK` on SkullGuy) in a new animation driver for this type.
- `EnemySaveState`/`EnemySaveable` needed no changes — `BlindListenerEnemy` uses the same `EnemyState`, saving works unmodified.

**Important when placing an instance in a scene:** the `player` field on `EnemyBase` (and therefore `BlindListenerEnemy`) is NOT auto-assigned by the prefab or by `BlindListenerEnemySetup` — it must be manually assigned in the Inspector on the scene instance (same as SkullGuy). Missing this assignment means the enemy never detects the player at all (`if (player == null) return false;` in `IsPlayerDetected`).

---

## General overview

The core of the system is `EnemyBase` (abstract MonoBehaviour) — a self-contained state machine that owns player detection, patrol, chase, and health/damage, delegating movement to a pluggable `IMovementStrategy`.

---

## Core: `Enemy/EnemyBase.cs`

`EnemyState` enum: `Idle`, `FollowPlayer`, `InvestigateLastKnown`, `ReturnToPatrol`.

**Fields (inspector):**
- Stats: `maxHealth`, `moveSpeed`, `chaseSpeedMultiplier`
- Detection: `player`, `alwaysDetectRange`, `detectionMemoryDuration`, `investigateOvershootDistance`, `investigateArrivalThreshold` (sight/hearing ranges live on the detector components)
- Patrol: `waypoints[]`, `waypointReachedThreshold`, `waypointPauseDuration`, `unreachableWaypointRetryInterval`, `skipInvestigateWhenLostPlayer`

**Key methods:**
- `Awake()` — caches `currentHealth`, `GetComponent<IMovementStrategy>`, `GetComponents<IPlayerDetector>()` (additional detectors), freezes Rigidbody2D rotation.
- `Start()` (virtual) — resolves `player` only if still null; see the GU-0051 section above.
- `Update()` — if dead: skip; otherwise `UpdateStateMachine()` → `ResolveUnreachablePatrolWaypoint()` → `Move()` if `ShouldMove()`.
- **`IsPlayerDetected()`** — unconditional within `alwaysDetectRange`, otherwise any of the cached `IPlayerDetector` components (`VisionPlayerDetector`, `SoundPlayerDetector`, ...). Detected if any check succeeds.
- `UpdateStateMachine()` — transition table (below). A successful detection stamps `lastDetectionTime`; `playerInRange` stays true until `detectionMemoryDuration` elapses since the last real detection. Updates `lastKnownPlayerPosition`/`hasLastKnownPlayerPosition` whenever `playerInRange == true`, regardless of state and regardless of which detector triggered it.
- `GetInvestigateTargetPosition()` — overshoots the last known position along the enemy→player vector.
- Patrol: `AdvanceWaypointIfReached()`, `HasValidWaypoint()`, `SelectClosestWaypoint()`, `EffectiveWaypointReachedThreshold()` (accounts for `IMovementArrivalTolerance`), `ResolveUnreachablePatrolWaypoint()` (switches waypoint when `IPathStatusProvider.HasReachablePath == false`).
- `TakeDamage(float)` — clamps hp, calls `OnDamageTaken` (virtual) or `OnDeath` (abstract) at 0 hp.
- `Knockback(Vector2 dir, float force)` — impulse via `Rigidbody2D.AddForce`, zeroes velocity after 0.12s (coroutine).
- `GetTargetPosition()` / `Move()` (virtual) — `movementStrategy.Move(...)`, speed multiplied by `chaseSpeedMultiplier` in `FollowPlayer`/`InvestigateLastKnown`.
- `CaptureSaveState()` / `RestoreSaveState()` — see Saving section.

**Public API:** `CurrentState`, `CurrentTargetPosition`, `CurrentHealth`, `MaxHealth`, `IsDead`, `Player`, `SetPlayer(Transform)`.

### State machine (`UpdateStateMachine`)
- **Idle** → `FollowPlayer` if `playerInRange`; else → `InvestigateNoise` if a fresh noise was heard; otherwise `AdvanceWaypointIfReached()`.
- **FollowPlayer** → on losing the player: if `!skipInvestigateWhenLostPlayer && hasLastKnownPlayerPosition` → `InvestigateLastKnown` (computes overshoot target); otherwise → `SelectClosestWaypoint()` + `ReturnToPatrol`.
- **InvestigateLastKnown** → back to `FollowPlayer` if the player is detected again; → `InvestigateNoise` on a fresh noise; once within `investigateArrivalThreshold` → clears the flag, `SelectClosestWaypoint()`, → `ReturnToPatrol`.
- **InvestigateNoise** → `FollowPlayer` if the player is detected; otherwise keeps updating the target to the newest heard noise (trail following); on arrival within `investigateArrivalThreshold` → `SelectClosestWaypoint()`, → `ReturnToPatrol`.
- **ReturnToPatrol** → `FollowPlayer` if detected; → `InvestigateNoise` on a fresh noise; otherwise → `Idle` once within `EffectiveWaypointReachedThreshold()` of the waypoint (or no valid waypoint).

**Note:** `IsPlayerDetected()` is evaluated once per frame at the top of `UpdateStateMachine` and reused by every state — any new `IPlayerDetector` affects all transitions uniformly.

---

## Concrete enemy types

- **`Enemy/SkullGuyEnemy.cs`** — vision-based, caches `SkullGuyAnimationDriver` in `Awake`. `Attack()` plays the attack animation (hit logic not implemented yet). `OnDeath()` = `SetActive(false)` (placeholder).
- **`Enemy/BlindListenerEnemy.cs`** — blind, hearing only (`SoundPlayerDetector`, required; no `VisionPlayerDetector` attached). `OnDeath()` = `SetActive(false)`.
- **`Enemy/TestEnemy.cs`** — minimal subclass for testing, `OnDeath()` just deactivates the object.

---

## Detection

### `Interfaces/IFacingProvider.cs`
`float FacingAngleDeg { get; }` — degrees, 0 = +X, CCW positive. Implemented by `SkullGuyAnimationDriver` (which now tracks its facing angle in a field and applies it to the sprite only when `rotateToFaceTarget`), consumed by `VisionPlayerDetector`. Exists so the vision cone and the sprite can never disagree: previously the detector derived facing from per-frame position delta, which froze at a stale angle whenever the enemy stood still or attacked without moving.

### `Interfaces/IPlayerDetector.cs`
```csharp
public interface IPlayerDetector {
    bool IsPlayerDetected(Transform player);
}
```
Common interface for every way of detecting the player. `EnemyBase` combines built-in vision with any number of components implementing this interface (OR).

### `Enemy/VisionPlayerDetector.cs`
`MonoBehaviour, IPlayerDetector`. Fields: `range`, `obstacleLayers`, `viewAngle` (default 360 = old omnidirectional behavior), `turnSpeedDeg`. The single source of sight-based detection: distance check + facing-cone check (if `viewAngle < 360`) + `Physics2D.Linecast` against `obstacleLayers`. The cone direction comes from `IFacingProvider` when the enemy has one (`SkullGuyAnimationDriver`), so it matches the sprite exactly — including while standing still. Without a provider it falls back to `EnemyBase.CurrentTargetPosition`, then to travelled distance, turning at `turnSpeedDeg`. Attached to `SkullGuyEnemy` and `TestEnemy` prefabs (range 10, mask = ObstacleStatic/ObstacleDynamic, migrated from the old built-in `EnemyBase` vision fields). Draws a yellow range gizmo (circle, or a cone arc when `viewAngle < 360`) when selected.

**GU-0061: facing cone (FOV).** Previously the detector was a plain circle — an enemy facing away from the player still saw them at any distance. `viewAngle` narrows this to a cone centered on the enemy's own tracked facing direction (`facingAngleDeg`, updated in `Update()` from frame-to-frame position delta via `Mathf.Atan2`, eased toward the movement direction at `turnSpeedDeg`/s via `Mathf.MoveTowardsAngle` so the cone doesn't snap). Deliberately self-contained — it does **not** read `SkullGuyAnimationDriver`'s `visual` rotation, because that transform's angle has `spriteForwardOffsetDeg` baked in for art alignment and would skew the cone by that offset; tracking raw movement delta on the detector's own transform keeps the cone true to actual travel direction independent of any animation driver being present. Only runs the per-frame tracking when `viewAngle < 360` (skipped entirely for old-behavior enemies, no perf cost). `EnemyBase.alwaysDetectRange` still bypasses the cone entirely (checked before any detector) — point-blank detection stays omnidirectional by design.

### `Enemy/SoundPlayerDetector.cs`
`MonoBehaviour, INoiseSensor`. Fields: `hearingRange` (cap on how far this enemy hears anything), `hearingBlockerMask`, `heardNoiseRetention`. Subscribes to `NoiseEvents.NoiseEmitted` (OnEnable/OnDisable); a noise registers when `distance <= min(noiseRadius, hearingRange)` and the linecast against `hearingBlockerMask` is clear. Exposes `HasFreshNoise` (last noise younger than `heardNoiseRetention`) and `LastNoisePosition` — consumed by the `InvestigateNoise` state, not by the detection loop. Draws a cyan range gizmo when selected.

### `Sound/NoiseEvents.cs` + `Player/PlayerNoiseEmitter.cs`
The emission side: static event bus + the player's movement-noise emitter (see status section above for parameters).

---

## Animation

### `Animation/SpriteFrameAnimator.cs` (+ `Enemy/EnemySpriteAnimator.cs`)
`[RequireComponent(SpriteRenderer)]`. Its own simple frame player (not Unity Animator), driven by named `Clip`s (`name`, `frames[]`, `fps`, `loop`, `nextClip` for chaining non-looping clips). `Play(name, restartIfSame)`, `Rewind()`, `SpeedMultiplier`, `Paused`, `ClipFinished` event, `IsPlaying`, `IsFinished`, `CurrentClipName`. `EnemySpriteAnimator` is now an empty subclass kept so the enemy prefabs and `SkullGuyAnimationDriver` keep their component reference; the player's torso/legs use the base type directly (see `PLAYER_NOTES.md`).

### `Enemy/SkullGuyAnimationDriver.cs`
`[RequireComponent(EnemyBase)]`. Maps `EnemyBase.CurrentState`/movement to clips: `SPOCZYNEK` (idle), `CHOD_POCZATEK→CHOD_LOOP→CHOD_KONIEC` (walk), `ROZGLADANIE` (investigate), `RYK` (roar, one-shot on entering `FollowPlayer`), `ATAK` (one-shot, cooldown, when in `attackRange` of `CurrentTargetPosition`). Also handles facing (`UpdateFacing`) and walk tempo (`SyncWalkTempo`). Currently reused as-is by `BlindListenerEnemy` (same component, same SkullGuy clip set).

---

## Movement

- **`Interfaces/IMovementStrategy.cs`** — `Move(Transform agent, Vector3 target, float speed)`, resolved via `GetComponent<IMovementStrategy>()` in `Awake`.
- **`IPathStatusProvider.HasReachablePath`** — optional, used to detect blocked patrol waypoints.
- **`IMovementArrivalTolerance.StopDistanceFromTarget`** — optional, arrival threshold is never smaller than the strategy's actual stopping distance.
- **`Movement/SimpleDirectMovement.cs`** — straight-line movement, stops within `stoppingDistance`.
- **`Movement/PathfindingMovement.cs`** — `PathfindingGrid` + `AStarPathfinder.FindPath`, re-paths on interval/target change, `HasReachablePath`, gizmo debug drawing.

---

## Health / damage

No separate component — health lives directly in `EnemyBase` (`currentHealth`, `maxHealth`, `TakeDamage`, `Knockback`, `IsDead`, `OnDamageTaken`/`OnDeath`). The player has its own separate `Player/PlayerHealthSystem.cs`, unrelated to enemies.

---

## Save state

### `Saving/EnemySaveable.cs`
`[RequireComponent(EnemyBase, SaveableEntity)]`, `ISaveableComponent`, `TypeTag = "enemy"`. Serializes `EnemyBase.CaptureSaveState()`/`RestoreSaveState()` (Newtonsoft JSON).

### `Saving/Model/EnemySaveState.cs`
DTO: `health`, `aiState` (int cast of `EnemyState`), `waypointIndex`, `lastKnownPlayerPos` (float[2] or null). Needed no changes for `BlindListenerEnemy` — this type uses the same `EnemyState` and fields as the rest.

---

## Open topics / TODO

1. More noise sources on the bus: doors, gunfire (thrown-rock distraction is done). Replace PlayerThrow's direct key read with a PlayerControls action and integrate with the inventory (limited rocks).
2. Dedicated visuals/animations for `BlindListenerEnemy` (currently a SkullGuy placeholder).
3. A possible "heard something" animation cue analogous to `RYK`.
4. Player death — `PlayerHealthSystem` has no death handling; enemy attacks now deal damage, so health can reach zero with no consequence.
5. Distinct attack tuning per enemy type if needed — both currently share the same `EnemyMeleeAttack` defaults.
6. Death handling — `OnDeath()` is `SetActive(false)`; needs a death animation and a persisted "dead" flag in the save state.
