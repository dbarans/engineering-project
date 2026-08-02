# Enemy System — Notes

Working reference for the Enemy system, maintained across branches (`GU-0032-enemy-sound-tracking`, `GU-0033-enemy-investigate-linger`, ...) and kept up to date whenever Enemy-related code changes are committed.

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

- **`EnemyBase.postInvestigateBehavior`** (`PostInvestigateBehavior` enum: `ReturnToPatrol` / `WanderNearLastPosition`) — Inspector field under "After losing the player". Read by the new `EndInvestigation()` helper, which both `InvestigateLastKnown` and `InvestigateNoise` call once they reach their target without re-detecting the player.
- **`WanderNearLastPosition`** (new `EnemyState`) — instead of returning to fixed patrol waypoints (which don't make sense for an arbitrary spot where the player was lost), the enemy repeatedly picks a random point within `wanderRadius` of `wanderAnchor` (captured as the enemy's position the moment investigation ended) and walks there at normal `moveSpeed` (not chase speed). On arrival, picks a new random point — indefinitely, until it re-detects the player (→ `FollowPlayer`) or hears a fresh noise (→ `InvestigateNoise`, same as every other passive state).
- Unreachable wander targets are handled the same way as unreachable patrol waypoints: `ResolveUnreachableWanderTarget()` checks `IPathStatusProvider.HasReachablePath` on a throttled retry (`unreachableWaypointRetryInterval`) and repicks.
- Magenta gizmo (`wanderRadius`, drawn from the enemy's current position) shown when `postInvestigateBehavior == WanderNearLastPosition`, alongside the existing red `alwaysDetectRange` circle.
- Save/restore: `WanderNearLastPosition` is transient like the other investigate states — not persisted; `RestoreSaveState` maps it back to `ReturnToPatrol` (same reasoning as `InvestigateLastKnown`/`InvestigateNoise`: the private target isn't saved, and heading for the nearest waypoint is indistinguishable to the player). Appended last in the enum so old saves keep decoding correctly.
- No prefab/scene YAML edits needed — new fields just take their C# defaults (`ReturnToPatrol`, `wanderRadius = 4`) until set in the Inspector.
- **Enemies with no waypoints configured no longer stand still.** `EnemyBase.spawnPosition` is captured in `Awake()`. In `Idle`, if `!HasValidWaypoint()` (no `waypoints` assigned), the enemy sets `wanderAnchor = spawnPosition` and enters `WanderNearLastPosition` — reusing the exact same wander mechanic, just anchored at spawn instead of the last-seen-player spot. This runs continuously (no exit back to `Idle` on its own), and is independent of `postInvestigateBehavior`: a waypoint-less enemy that loses a chase still ends up back in `Idle` first (`ReturnToPatrol` immediately falls through to `Idle` when there's no valid waypoint), then resumes wandering from its spawn point, not from wherever the chase ended.

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

### `Interfaces/IPlayerDetector.cs`
```csharp
public interface IPlayerDetector {
    bool IsPlayerDetected(Transform player);
}
```
Common interface for every way of detecting the player. `EnemyBase` combines built-in vision with any number of components implementing this interface (OR).

### `Enemy/VisionPlayerDetector.cs`
`MonoBehaviour, IPlayerDetector`. Fields: `range`, `obstacleLayers`. The single source of sight-based detection: distance check + `Physics2D.Linecast` against `obstacleLayers`. Attached to `SkullGuyEnemy` and `TestEnemy` prefabs (range 10, mask = ObstacleStatic/ObstacleDynamic, migrated from the old built-in `EnemyBase` vision fields). Draws a yellow range gizmo when selected.

### `Enemy/SoundPlayerDetector.cs`
`MonoBehaviour, INoiseSensor`. Fields: `hearingRange` (cap on how far this enemy hears anything), `hearingBlockerMask`, `heardNoiseRetention`. Subscribes to `NoiseEvents.NoiseEmitted` (OnEnable/OnDisable); a noise registers when `distance <= min(noiseRadius, hearingRange)` and the linecast against `hearingBlockerMask` is clear. Exposes `HasFreshNoise` (last noise younger than `heardNoiseRetention`) and `LastNoisePosition` — consumed by the `InvestigateNoise` state, not by the detection loop. Draws a cyan range gizmo when selected.

### `Sound/NoiseEvents.cs` + `Player/PlayerNoiseEmitter.cs`
The emission side: static event bus + the player's movement-noise emitter (see status section above for parameters).

---

## Animation

### `Enemy/EnemySpriteAnimator.cs`
`[RequireComponent(SpriteRenderer)]`. Its own simple frame player (not Unity Animator), driven by named `Clip`s (`name`, `frames[]`, `fps`, `loop`, `nextClip` for chaining non-looping clips). `Play(name, restartIfSame)`, `SpeedMultiplier`, `ClipFinished` event, `IsPlaying`, `IsFinished`, `CurrentClipName`.

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
