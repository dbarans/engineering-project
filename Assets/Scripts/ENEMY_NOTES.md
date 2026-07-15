# Enemy System — Notes

Working reference for the Enemy system, maintained for branch `GU-0032-enemy-sound-tracking` and kept up to date whenever Enemy-related code changes are committed.

## Sound tracking implementation status

Done:
- `SoundPlayerDetector : IPlayerDetector` — hears the player within `hearingRange`, only while `PlayerMovement.CurrentMode != Sneak` (sneaking is silent), optionally muffled by `hearingBlockerMask`.
- `PlayerMovement.CurrentMode` — public getter, source of truth for whether the player is currently making noise.
- `PlayerMovement.IsMoving` — checks actual Rigidbody2D velocity against `movingSpeedThreshold`. Needed because `CurrentMode` alone isn't enough: releasing the sneak key while standing still switches the mode to Walk without the player actually moving, which was making `SoundPlayerDetector` falsely trigger. `SoundPlayerDetector` now requires both `CurrentMode != Sneak` and `IsMoving`.
- `EnemyBase.IsPlayerDetected()` refactored: built-in vision (`HasVisionOfPlayer`, disabled when `visionDistance <= 0`) OR any number of additional `IPlayerDetector` components on the object (cached in `Awake` via `GetComponents<IPlayerDetector>()`).
- `BlindListenerEnemy : EnemyBase` — first enemy type with no vision, `[RequireComponent(SoundPlayerDetector)]`, `visionDistance = 0`.
- `EnemyBase.alwaysDetectRange` (default 0.5) — player is always detected within this distance regardless of vision/hearing, added after observing that very-close-range detection could fail (line-of-sight raycasts giving false negatives right next to the enemy). Checked first in `IsPlayerDetected()`, before any detector runs.
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
- Detection: `player`, `visionDistance`, `visionBlockerMask`, `investigateOvershootDistance`, `investigateArrivalThreshold`
- Patrol: `waypoints[]`, `waypointReachedThreshold`, `waypointPauseDuration`, `unreachableWaypointRetryInterval`, `skipInvestigateWhenLostPlayer`

**Key methods:**
- `Awake()` — caches `currentHealth`, `GetComponent<IMovementStrategy>`, `GetComponents<IPlayerDetector>()` (additional detectors), freezes Rigidbody2D rotation.
- `Update()` — if dead: skip; otherwise `UpdateStateMachine()` → `ResolveUnreachablePatrolWaypoint()` → `Move()` if `ShouldMove()`.
- **`IsPlayerDetected()`** — `HasVisionOfPlayer()` (built-in vision, disabled when `visionDistance <= 0`) OR any of `additionalDetectors` (`IPlayerDetector.IsPlayerDetected`, e.g. `SoundPlayerDetector`). Detected if any check succeeds.
- `HasVisionOfPlayer()` — distance check (`visionDistance`) + `Physics2D.Linecast(enemyPos, playerPos, visionBlockerMask)`.
- `UpdateStateMachine()` — transition table (below). Updates `lastKnownPlayerPosition`/`hasLastKnownPlayerPosition` whenever `playerInRange == true`, regardless of state and regardless of which detector triggered it.
- `GetInvestigateTargetPosition()` — overshoots the last known position along the enemy→player vector.
- Patrol: `AdvanceWaypointIfReached()`, `HasValidWaypoint()`, `SelectClosestWaypoint()`, `EffectiveWaypointReachedThreshold()` (accounts for `IMovementArrivalTolerance`), `ResolveUnreachablePatrolWaypoint()` (switches waypoint when `IPathStatusProvider.HasReachablePath == false`).
- `TakeDamage(float)` — clamps hp, calls `OnDamageTaken` (virtual) or `OnDeath` (abstract) at 0 hp.
- `Knockback(Vector2 dir, float force)` — impulse via `Rigidbody2D.AddForce`, zeroes velocity after 0.12s (coroutine).
- `GetTargetPosition()` / `Move()` (virtual) — `movementStrategy.Move(...)`, speed multiplied by `chaseSpeedMultiplier` in `FollowPlayer`/`InvestigateLastKnown`.
- `CaptureSaveState()` / `RestoreSaveState()` — see Saving section.

**Public API:** `CurrentState`, `CurrentTargetPosition`, `CurrentHealth`, `MaxHealth`, `IsDead`.

### State machine (`UpdateStateMachine`)
- **Idle** → `FollowPlayer` if `playerInRange`; otherwise `AdvanceWaypointIfReached()`.
- **FollowPlayer** → on losing the player: if `!skipInvestigateWhenLostPlayer && hasLastKnownPlayerPosition` → `InvestigateLastKnown` (computes overshoot target); otherwise → `SelectClosestWaypoint()` + `ReturnToPatrol`.
- **InvestigateLastKnown** → back to `FollowPlayer` if the player is detected again; once within `investigateArrivalThreshold` → clears the flag, `SelectClosestWaypoint()`, → `ReturnToPatrol`.
- **ReturnToPatrol** → `FollowPlayer` if detected; otherwise → `Idle` once within `EffectiveWaypointReachedThreshold()` of the waypoint (or no valid waypoint).

**Note:** `IsPlayerDetected()` is evaluated once per frame at the top of `UpdateStateMachine` and reused by every state — any new `IPlayerDetector` affects all transitions uniformly.

---

## Concrete enemy types

- **`Enemy/SkullGuyEnemy.cs`** — vision-based, caches `SkullGuyAnimationDriver` in `Awake`. `Attack()` plays the attack animation (hit logic not implemented yet). `OnDeath()` = `SetActive(false)` (placeholder).
- **`Enemy/BlindListenerEnemy.cs`** — blind, hearing only (`SoundPlayerDetector`, required). `visionDistance` must be `0` in the Inspector. `OnDeath()` = `SetActive(false)`.
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
`MonoBehaviour, IPlayerDetector`. Fields: `range`, `obstacleLayers`. Duplicates the built-in vision logic in `EnemyBase` — leftover scaffolding from before the refactor, **still not wired up/used by any prefab** (SkullGuy's vision goes through the built-in `EnemyBase` fields, not this component). Worth considering: either remove as dead code, or eventually rewrite `EnemyBase` so vision also goes through this class (unify the two paths).

### `Enemy/SoundPlayerDetector.cs`
`MonoBehaviour, IPlayerDetector`. Fields: `hearingRange`, `hearingBlockerMask`. `IsPlayerDetected`: gets `PlayerMovement` via `player.GetComponent<PlayerMovement>()`, returns `false` when the component is missing or `CurrentMode == Sneak`, then does the standard distance + linecast check.

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

1. A real sound-emission system (footsteps, other sources) instead of relying purely on `MovementMode`.
2. Dedicated visuals/animations for `BlindListenerEnemy` (currently a SkullGuy placeholder).
3. `VisionPlayerDetector.cs` — dead/unwired code, consider removing or unifying SkullGuy's vision through it.
4. A possible "heard something" animation cue analogous to `RYK`.
