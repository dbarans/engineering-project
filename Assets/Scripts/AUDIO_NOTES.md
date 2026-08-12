# Audio System — Plan & Notes

Living document for the audio feature (GU-0063). Written in English to match the rest of
the project's documentation (see `ENEMY_NOTES.md`, `GENERATION_NOTES.md`).

## Status

| Stage | State |
|-------|-------|
| 0 — Design decisions | done, below |
| 1 — Core (SoundId, SoundBank, AudioService, runtime pool) | done |
| 2 — Hook sites (footsteps, combat, doors, barricade, chest, pickup, enemy, player hurt) | done |
| 3 — Editor tool (build/refresh the bank asset) | done |
| 4 — Real clips | **not started — waiting on audio assets** |

**Verification is compile-level plus in-editor console output, not audible.** No `.wav`/`.ogg`
files exist in the project yet, so every hook currently proves itself by logging instead of
playing. That is the deliberate deliverable of this stage — see D2.

---

## The problem this solves

The project had **zero audio**: `Assets/Audio` was empty and no script referenced
`AudioSource` or `AudioClip` anywhere.

That is worse than it sounds, because the game already has a fully built system for noise
*as a stealth mechanic* — `NoiseEvents`, `PlayerNoiseEmitter`, `SoundPlayerDetector`,
`NoiseSettings`, gunfire and barricade-building emissions. Enemies hear the player
perfectly. **The player hears nothing.** Sneaking is therefore guesswork: there is no
feedback telling you that you just made a noise an enemy could act on.

---

## 0. Design decisions (fixed — do not re-litigate mid-implementation)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **Audio is separate from `NoiseEvents`**, not layered onto it | They answer different questions. `NoiseEvents` = "what can an *enemy* hear" (gameplay). Audio = "what does the *player* hear" (feedback). They overlap often but not always: a sneaking player makes no gameplay noise yet should still hear their own quiet footsteps, and an enemy's own attack should be audible to the player without feeding the enemy's own ears. Coupling them would force one to distort the other. |
| D2 | **A missing clip logs to the console instead of failing** | No audio assets exist yet. Every hook can therefore be written, wired and reviewed *now*, and each one announces itself in the console when it fires. Dropping real clips in later needs no code change — only the bank asset. |
| D3 | **String ids, resolved through a Resources-loaded `SoundBank`** | Same shape as `PrefabRegistry` (`world.door`) and `ItemDatabase`: hook sites name a sound, they never hold an `AudioClip` reference. That keeps prefabs free of audio wiring and means one asset lists everything the game can play. |
| D4 | Ids live as `const string` on **`SoundId`**, never typed inline | A typo in a raw string is a silent no-op. Constants also give the editor tool something to enumerate, so the bank can be generated pre-populated. |
| D5 | **Pooled `AudioSource`s on one bootstrapped object**, not per-emitter components | Hook sites are all over the codebase (doors, chests, enemies, projectiles) and many are on objects that are destroyed the same frame they make their sound — a pickup, a breaking barricade, a dying enemy. A component-per-emitter would cut its own sound off. Pool pattern also matches `SaveDebugHotkeys`'s `[RuntimeInitializeOnLoadMethod]` bootstrap. |
| D6 | Per-entry **clip variation, volume, pitch jitter and a retrigger cooldown** | Footsteps fire every ~0.2 s; one clip at one pitch is the single most fatiguing thing in a stealth game. The cooldown also protects against a hook site accidentally firing every frame. |
| D7 | **Positional (3D) by default, 2D opt-in per entry** | A dungeon crawler's whole point is locating things you cannot see. UI clicks and player-body sounds are the exceptions, so `spatialBlend` is per-entry rather than global. |
| D8 | Never `AudioListener` gymnastics — one listener stays on the camera | Nothing here needs to move it, and moving it is the usual cause of "audio is panned wrong" bugs. |

**Target folder:** `Assets/Scripts/Audio/`
**Bank asset:** `Assets/Resources/SoundBank.asset`

---

## 1. Core pieces

- `Audio/SoundId.cs` — `const string` per sound, grouped by area. The single list of what
  the game can play.
- `Audio/SoundBank.cs` — `ScriptableObject` in `Resources`, `id -> SoundEntry`.
  `SoundEntry` carries `clips[]`, `volume`, `pitch` min/max, `spatialBlend`, `minDistance`
  /`maxDistance`, `cooldown`. Singleton access via `SoundBank.Instance`, missing asset
  degrades to log-only rather than throwing (mirrors `KeyBindings`).
- `Audio/AudioService.cs` — the only API hook sites touch:
  - `AudioService.Play(id)` — non-positional (2D-ish, player-centric).
  - `AudioService.PlayAt(id, position)` — positional one-shot, survives its emitter.
  - `AudioService.PlayOn(id, transform)` — positional, follows a moving emitter.
- `Audio/AudioRuntime.cs` — bootstrapped `DontDestroyOnLoad` object owning the
  `AudioSource` pool. Not placed in any scene; created on first play.

## 2. Hook sites

| Sound | Where | Note |
|---|---|---|
| `player.footstep.walk` / `.sprint` / `.sneak` | `PlayerNoiseEmitter` | Cadence already exists there for gameplay noise; sneak deliberately still *plays* audio while emitting **zero** gameplay noise — D1 in practice |
| `player.hurt`, `player.death` | `PlayerHealthSystem` | |
| `player.attack.melee`, `player.attack.ranged`, `player.attack.dryfire` | `MeleeAttack`, `RangedAttack` | dry-fire is the existing "no ammo" path |
| `player.throw` | `PlayerThrow` | |
| `door.open`, `door.close`, `door.hit`, `door.destroy` | `SimpleDoor` | |
| `barricade.build`, `barricade.stagebreak`, `barricade.destroy` | `DoorBarricade` | already emits gameplay noise at the same points |
| `chest.open`, `chest.close` | `ChestInteractable` | |
| `item.pickup`, `item.drop` | `WorldItemPickup` | |
| `enemy.alert`, `enemy.attack`, `enemy.hurt`, `enemy.death` | `EnemyBase`, `EnemyMeleeAttack` | alert fires on the transition *into* chase, not every frame of it |

## 3. Editor tool

`Tools ▸ Audio ▸ Build Sound Bank` — creates `Assets/Resources/SoundBank.asset` and
back-fills an entry for every id declared on `SoundId` (found by reflection), so the asset
always lists the full set and the team only has to drag clips in. Re-running adds newly
declared ids and **never** touches entries that already have clips or tuned values.

## 3a. One behaviour change worth knowing about

`PlayerNoiseEmitter` previously only advanced its `nextEmitTime` cadence when it actually
emitted gameplay noise — so while sneaking (radius 0) the timer sat in the past and the
first walking step after a sneak fired instantly.

Footstep *audio* has to run on that same cadence while sneaking, so the timer now advances
whenever the player is moving, regardless of noise radius. The visible consequence: going
sneak → walk can delay the first gameplay noise emission by up to one `emitInterval`
(0.2 s by default). Judged acceptable — it makes the cadence uniform rather than
mode-dependent — but it is a real change to stealth timing, not a pure addition.

## 4. What is deliberately NOT here

- **No music / ambience track.** That is a separate concern with its own looping and
  crossfade needs; this is the SFX layer.
- **No mixer groups / volume settings.** Belongs with the settings screen, which is
  where `KeyBindings` (GU-0060) is also headed. `SoundBank` is the natural place to add a
  master volume later.
- **No footstep surface variation** (stone vs wood). Needs surface tagging on tiles that
  does not exist yet.

---

## Checklist for the next person (needs the editor)

1. Run **Tools ▸ Audio ▸ Build Sound Bank**.
2. Play. Every hook logs `[Audio] ♪ <id>` with its position — walk, attack, open a door,
   barricade it, get hit. That is the whole system proving itself with no clips present.
3. Drop `.wav`/`.ogg` files into `Assets/Audio/`, drag them onto the matching entries in
   `SoundBank.asset`. Logging stops for that id the moment it has a clip; everything else
   keeps logging until it is filled in too.
