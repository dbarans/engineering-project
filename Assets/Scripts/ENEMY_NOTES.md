# Enemy System — Notes

Working reference for the Enemy system, maintained across branches (`GU-0032-enemy-sound-tracking`, `GU-0033-enemy-investigate-linger`, ...) and kept up to date whenever Enemy-related code changes are committed.

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
- **Perf pass (unrelated to sound tracking but touches enemy pathfinding): ~30ms → ~12ms/frame in editor.** Root cause was `Vision/FieldOfView.cs` sweeping a full 360° at full ray resolution just to render the small near-vision/lantern circle whenever the cone was narrower than 360° — fixed by sampling the cone at full `raysPerDegree` and the area outside it at a much coarser `nearCircleRaysPerDegree`; cone quality/resolution unchanged. Also: `FieldOfView` reuses its vertex/UV/triangle buffers instead of allocating every `LateUpdate`; `Vision/HideableObject.cs` now staggers its per-object visibility linecast across `checkEveryNFrames` (default 3) instead of checking every frame; `Pathfinding/AStarPathfinder.cs` caches its scratch grids (gScore/closed/parent arrays) per `PathfindingGrid` instead of allocating them on every `FindPath` call — relevant here because every chasing enemy re-paths roughly every `repathInterval`.
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
- `Update()` — if dead: skip; otherwise `UpdateStateMachine()` → `ResolveUnreachablePatrolWaypoint()` → `Move()` if `ShouldMove()`.
- **`IsPlayerDetected()`** — unconditional within `alwaysDetectRange`, otherwise any of the cached `IPlayerDetector` components (`VisionPlayerDetector`, `SoundPlayerDetector`, ...). Detected if any check succeeds.
- `UpdateStateMachine()` — transition table (below). A successful detection stamps `lastDetectionTime`; `playerInRange` stays true until `detectionMemoryDuration` elapses since the last real detection. Updates `lastKnownPlayerPosition`/`hasLastKnownPlayerPosition` whenever `playerInRange == true`, regardless of state and regardless of which detector triggered it.
- `GetInvestigateTargetPosition()` — overshoots the last known position along the enemy→player vector.
- Patrol: `AdvanceWaypointIfReached()`, `HasValidWaypoint()`, `SelectClosestWaypoint()`, `EffectiveWaypointReachedThreshold()` (accounts for `IMovementArrivalTolerance`), `ResolveUnreachablePatrolWaypoint()` (switches waypoint when `IPathStatusProvider.HasReachablePath == false`).
- `TakeDamage(float)` — clamps hp, calls `OnDamageTaken` (virtual) or `OnDeath` (abstract) at 0 hp.
- `Knockback(Vector2 dir, float force)` — impulse via `Rigidbody2D.AddForce`, zeroes velocity after 0.12s (coroutine).
- `GetTargetPosition()` / `Move()` (virtual) — `movementStrategy.Move(...)`, speed multiplied by `chaseSpeedMultiplier` in `FollowPlayer`/`InvestigateLastKnown`.
- `CaptureSaveState()` / `RestoreSaveState()` — see Saving section.

**Public API:** `CurrentState`, `CurrentTargetPosition`, `CurrentHealth`, `MaxHealth`, `IsDead`.

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
