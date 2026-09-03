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
| 4 — Real clips | in progress — `ui.click` and the walk/sprint footsteps only |
| 5 — Mixer groups (Master / Music / SFX / UI) + per-channel volume | done |

**Almost everything here is still verified by console output rather than by ear.** Three
entries have clips: `ui.click` (`Assets/Audio/UI/UIClick.wav`), plus
`player.footstep.walk` and `player.footstep.sprint`, which **share**
`Assets/Audio/Player/Footsteps/wood01.ogg` and are told apart by cadence alone (3b). Every
other id still has an empty `clips` array and proves itself by logging — the deliberate
deliverable of stage 2, see D2.

**Sneaking is still silent** — it plays its own id, has no clip, and therefore logs. It is
the one gait that should not borrow the walk clip: a creep across floorboards is a
different recording, not the same one spaced further apart.

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
| D9 | **A real `AudioMixer` asset, routed per bank entry**, and a **missing mixer degrades to unrouted playback** | Group volume is the only thing an `AudioSource` cannot do for itself, and it is the whole reason a settings screen can exist. Routing lives on the bank entry rather than at the call site for the same reason clips do (D3): hook sites name a sound, they do not know how it is mixed. The degrade path mirrors D2 and `SoundBank` — a broken or absent asset must not make the game silent. |
| D9a | The mixer asset is **generated by an editor tool, never hand-written** | There is no public API for creating an `AudioMixer` (`AssetDatabase.CreateAsset` does not support it), and a hand-authored `.mixer` YAML file **hangs Unity's asset importer** — tested, not assumed. `AudioMixerSetup` therefore drives Unity's own internal `AudioMixerController` by reflection, which is the same path the `Assets ▸ Create ▸ Audio Mixer` menu item takes. |
| D10 | **One global UI click listener, not a call per button** | Most of this project's buttons are wired to their handler in the Inspector, so there is no single method to hook, and not everything clickable is a `Button` (`SlotView` handles pointer clicks itself). Asking the `EventSystem` the same question it asks itself covers every screen at once, including screens nobody has written yet, and needs no prefab or scene re-authoring. |

**Target folder:** `Assets/Scripts/Audio/`
**Bank asset:** `Assets/Resources/SoundBank.asset`
**Mixer asset:** `Assets/Resources/MainMixer.mixer`

---

## 1. Core pieces

- `Audio/SoundId.cs` — `const string` per sound, grouped by area. The single list of what
  the game can play.
- `Audio/SoundBank.cs` — `ScriptableObject` in `Resources`, `id -> SoundEntry`.
  `SoundEntry` carries `clips[]`, `channel`, `volume`, `pitch` min/max, `spatialBlend`,
  `minDistance`/`maxDistance`, `cooldown`. Singleton access via `SoundBank.Instance`,
  missing asset degrades to log-only rather than throwing (mirrors `KeyBindings`).
- `Audio/AudioService.cs` — the only API hook sites touch:
  - `AudioService.Play(id)` — non-positional (2D-ish, player-centric).
  - `AudioService.PlayAt(id, position)` — positional one-shot, survives its emitter.
  - `AudioService.PlayOn(id, transform)` — positional, follows a moving emitter.
- `Audio/AudioRuntime.cs` — bootstrapped `DontDestroyOnLoad` object owning the
  `AudioSource` pool. Not placed in any scene; created on first play.
- `Audio/AudioChannel.cs` — `Sfx` / `Music` / `Ui` / `Master`. `Sfx` is 0 so that every
  entry serialized before the field existed lands on the group it was already playing
  through.
- `Audio/AudioMixerService.cs` — owns `MainMixer`, maps a channel to its group and to its
  exposed volume parameter, and stores per-channel volume in `PlayerPrefs` under
  `audio.volume.*`. Volumes are set as 0..1 (what a slider hands you) and converted to dB
  here; stored values are pushed into the mixer on play-mode start, because exposed
  parameters reset to their authored 0 dB each session.
- `Audio/UiClickAudio.cs` — bootstrapped listener that plays `ui.click` when a press lands
  on something the `EventSystem` would treat as a click (D10).

## 1a. The mixer

`Assets/Resources/MainMixer.mixer` — **created by `Tools ▸ Audio ▸ Build Audio Mixer`**
rather than authored by hand (D9a). Four groups:

```
Master              exposed as MasterVolume
├── Music           exposed as MusicVolume
├── SFX             exposed as SfxVolume
└── UI              exposed as UiVolume
```

Every pooled `AudioSource` has its `outputAudioMixerGroup` reassigned per play, from the
entry's `channel` — a pooled source is reused across channels, so the group it carried last
time is not the one this sound belongs to.

Volume is changed through `AudioMixerService.SetVolume(channel, 0..1)`, which writes both
the mixer and `PlayerPrefs`. **Nothing calls it yet** — there is no settings screen. It is
the API that screen will use.

`Music` currently has nothing routed to it: the group exists so a music system can be
dropped in without touching the mixer or the routing code.

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
| `surface.glass.step` | `PlayerNoiseEmitter` via `PlayerSurfaceTracker` | replaces the footstep while the player stands on a `NoisySurface` |
| `ui.click` | `UiClickAudio` | one listener for every screen — main menu, pause, death screen, save/load, HUD inventory slots (D10) |

## 3. Editor tool

`Tools ▸ Audio ▸ Build Sound Bank` — creates `Assets/Resources/SoundBank.asset` and
back-fills an entry for every id declared on `SoundId` (found by reflection), so the asset
always lists the full set and the team only has to drag clips in. Re-running adds newly
declared ids and **never** touches entries that already have clips or tuned values.

`Tools ▸ Audio ▸ Build Audio Mixer` — creates `Assets/Resources/MainMixer.mixer` with the
Master/Music/SFX/UI layout and the four exposed volume parameters (D9a). Also
non-destructive: an existing mixer is checked and reported on, never rebuilt, so re-running
it is how you confirm the asset still matches what `AudioMixerService` looks for.

## 3a. One behaviour change worth knowing about

`PlayerNoiseEmitter` previously only advanced its `nextEmitTime` cadence when it actually
emitted gameplay noise — so while sneaking (radius 0) the timer sat in the past and the
first walking step after a sneak fired instantly.

Footstep *audio* has to run on that same cadence while sneaking, so the timer now advances
whenever the player is moving, regardless of noise radius. The visible consequence: going
sneak → walk can delay the first gameplay noise emission by up to one `emitInterval`
(0.2 s by default). Judged acceptable — it makes the cadence uniform rather than
mode-dependent — but it is a real change to stealth timing, not a pure addition.

## 3b. Footstep cadence is not the noise cadence

They shared one timer at first. They no longer do, and the reason matters if anyone is
tempted to merge them again.

`emitInterval` (0.2 s) is a **sampling rate**, not a rhythm: `SoundPlayerDetector` keeps a
heard noise "fresh" for `heardNoiseRetention` (0.35 s), so emissions have to arrive faster
than that or continuous movement stops reading as a continuous trail and enemies lose the
player mid-corridor. Raising it to stride speed would be a stealth regression dressed up as
an audio setting.

The footstep *sound* is paced by the gait instead, per mode on `PlayerNoiseEmitter`:

| Mode | Interval | Why |
|---|---|---|
| Walk | 0.5 s | asked for — steps land with an audible pause between them |
| Sprint | 0.25 s | asked for — the clip's own length, so steps run back to back with no silence at all. The gaplessness is what separates a run from a fast walk, and it is why sprint reuses the walk clip without sounding like one |
| Sneak | 0.7 s | the only feedback that sneaking is working, since it emits no gameplay noise |

Sprint's interval is therefore **tied to the clip**, not to a gait: replacing `wood01.ogg`
with a recording of a different length reopens (or overlaps) the gap, and the interval has
to move with it.

The `cooldown` on a footstep entry in the bank is unrelated and stays a *guard* (D6) — each
one sits well under its matching interval, so it never becomes the thing setting the pace.
Driving the rhythm from `cooldown` instead would also quantise it to multiples of
`emitInterval`: a 0.5 s cooldown against a 0.2 s poll yields steps 0.6 s apart, not 0.5.

## 4. What is deliberately NOT here

- **No music / ambience track.** That is a separate concern with its own looping and
  crossfade needs; this is the SFX layer. The `Music` mixer group is in place for it.
- **No settings screen.** The mixer and `AudioMixerService.SetVolume` exist, but nothing
  calls them — there are no sliders. That belongs with the settings screen, which is where
  `KeyBindings` (GU-0060) is also headed.
- **No UI sound beyond the click.** No hover, no back, no panel open/close. One id, one
  listener; each of those would want its own trigger point and its own argument for
  existing.
- **No keyboard/gamepad "submit" click.** `UiClickAudio` listens for pointer presses.
  Nothing in this project navigates its UI by keyboard — the pause menu explicitly clears
  the `EventSystem` selection — so there is currently nothing to hear.
- **No footstep surface variation by tile** (stone vs wood). Tiles still carry no surface
  tagging. The one exception is object-based rather than tile-based: a `NoisySurface`
  volume (GU-0073, broken glass) replaces the footstep sound *and* the gameplay noise
  radius while the player stands in it — see `PlayerSurfaceTracker`.

---

## Checklist for the next person (needs the editor)

1. Run **Tools ▸ Audio ▸ Build Audio Mixer**. It reports the four groups and the four
   exposed parameters it found; all eight should say yes/exposed. **Commit the generated
   `MainMixer.mixer` afterwards**, the same way `SoundBank.asset` is committed — the tool
   exists to create it once and to check it later, not to be re-run per clone.
2. Run **Tools ▸ Audio ▸ Build Sound Bank**.
3. Play. Every hook logs `[Audio] ♪ <id>` with its position — walk, attack, open a door,
   barricade it, get hit. That is the whole system proving itself with no clips present.
   The one exception is a click on any button, which you should *hear*.
4. Drop `.wav`/`.ogg` files into `Assets/Audio/`, drag them onto the matching entries in
   `SoundBank.asset`. Logging stops for that id the moment it has a clip; everything else
   keeps logging until it is filled in too.

If the mixer is missing or wrong, nothing breaks: every sound still plays, unrouted, and
the console says so once.
