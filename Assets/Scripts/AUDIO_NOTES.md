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
| 4 — Real clips | in progress — the menu and dungeon music, `ui.click`, the walk/sprint footsteps, the player hurt, death, exhaustion, swing and gunshot, both door swings, the unlock, the save station, the chest, the backpack, every enemy voice |
| 5 — Mixer groups (Master / Music / SFX / UI) + per-channel volume | done |

**Most of this is still verified by console output rather than by ear.** Twenty-three entries
have clips: `music.menu` (`Assets/Audio/Music/AMBIENTe.mp3`) and `music.dungeon`
(`Assets/Audio/Music/dark_cavern_ambient_001.ogg`), `ui.click`
(`Assets/Audio/UI/UIClick.wav`), `player.footstep.walk` and `player.footstep.sprint`, which
**share** `Assets/Audio/Player/Footsteps/footstep03.ogg` and are told apart by cadence alone
(3b), `player.hurt` (`Assets/Audio/Player/Hurt/hit2.ogg`), `player.death`
(`Assets/Audio/Player/Death/die1.ogg`), `player.exhausted`
(`Assets/Audio/Player/Exhausted/breathing tirede.wav`), `player.attack.melee`
(`Assets/Audio/Player/Attack/swosh-01.flac`), `player.attack.ranged`
(`Assets/Audio/Player/Attack/M_26Pe.wav`), `surface.glass.step`
(`Assets/Audio/Surfaces/gravel.ogg`), `door.open` and `door.close`, which likewise
**share** `Assets/Audio/Doors/doorOpen_2.ogg`, `door.unlock`
(`Assets/Audio/Doors/doorClose_1.ogg`), `chest.open`
(`Assets/Audio/Chest/doorClose_4.ogg` — `chest.close` is still silent), `backpack.open`
and `backpack.close`, which share `Assets/Audio/UI/clothBelt.ogg`, `savestation.open`
(`Assets/Audio/World/bookFlip1.ogg`), and all five enemy voices — `enemy.idle` (four moans
in `Assets/Audio/Enemy/Idle/`, the only entry with real clip variation, 2a), plus
`enemy.alert`, `enemy.attack`, `enemy.hurt` and `enemy.death` (2b). Every other id still
has an empty `clips` array and proves itself by logging — the deliberate deliverable of
stage 2, see D2.

**Music is the one sound that is not a one-shot**, and it is why `AudioRuntime` grew a
second, unpooled `AudioSource`. A looping track never reports `!isPlaying`, so `Rent`
would hold its pooled source forever and — once 24 voices were live — steal it back
mid-track on the round-robin. Music also needs the one thing no one-shot does: an owner
that can be *stopped*, which is what `AudioService.PlayMusic` / `StopMusic` and the id kept
alongside the source are for.

Both tracks follow the same shape: start in `Start`, stop in `OnDestroy`, on a component
that exists only in its own scene (`MainMenuController`, `GameManager`). `OnDestroy` rather
than the button handler, because the menu can also be left through `SaveManager.Load`, which
loads the saved scene itself. There is one source, so a scene change swaps tracks rather
than layering them, and `StopMusic` reads `AudioRuntime.Current` rather than `Instance` so a
scene being torn down cannot resurrect the runtime on its way out.

**The player is no longer only acted upon**: it grunts when hit, dies audibly, swings and
fires audibly, and runs out of breath audibly — the last being the first sound in the game
that reports a *resource* rather than an event, and the only one the HUD was previously
alone in showing. What is left silent on the player's side is the throw and the dry click
of an empty magazine — and the dry click is the one that costs most, since it is the only
feedback distinguishing "out of ammo" from "the game ignored the trigger"
(`RangedAttack.OnFireBlocked`).

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
| `player.exhausted` | `PlayerStaminaSystem` | raised in `Drain` on the crossing to zero, so every way of spending stamina covers it and an empty bar does not re-gasp per frame |
| `player.attack.melee`, `player.attack.ranged`, `player.attack.dryfire` | `MeleeAttack`, `RangedAttack` | dry-fire is the existing "no ammo" path |
| `player.throw` | `PlayerThrow` | |
| `door.open`, `door.close`, `door.hit`, `door.destroy` | `SimpleDoor` | |
| `door.unlock` | `SimpleDoor` | both deliberate unlocks — the key being spent, and the player-set bolt coming off. Not the sprint ram, which clears `isLocked` by force and already sounds as a hit plus an opening |
| `barricade.build`, `barricade.stagebreak`, `barricade.destroy` | `DoorBarricade` | already emits gameplay noise at the same points |
| `chest.open`, `chest.close` | `ChestInteractable` | |
| `item.pickup`, `item.drop` | `WorldItemPickup` | |
| `enemy.idle` | `EnemyBase` | ambient moan while the enemy has **not** noticed the player — random 4-6 s cadence per enemy, gated on distance (2a) |
| `enemy.alert`, `enemy.attack`, `enemy.hurt`, `enemy.death` | `EnemyBase`, `EnemyMeleeAttack` | alert fires once per hunt, not per re-sighting (2c); attack fires on the wind-up, not on the hit (2b) |
| `surface.glass.step` | `PlayerNoiseEmitter` via `PlayerSurfaceTracker` | replaces the footstep while the player stands on a `NoisySurface` |
| `music.menu` | `MainMenuController` | started in `Start`, stopped in `OnDestroy` so every exit from the menu scene is covered — including the Load button, which leaves through `SaveManager.Load` |
| `music.dungeon` | `GameManager` | same shape, on the one component that lives only in the dungeon scene — so Play, a loaded save and the death screen's restart all start it |
| `ui.click` | `UiClickAudio` | one listener for every screen — main menu, pause, death screen, save/load, HUD inventory slots (D10) |
| `savestation.open` | `SaveStation` | fires only when the save screen actually opens — `SaveLoadUI.Open` is a silent no-op outside Playing |
| `backpack.open`, `backpack.close` | `BackpackUI` | raised in `SetOpen`, below its no-change guard, so every route into the panel (Tab, and the chest and crafting screens closing it) sounds once and re-showing an open panel is silent |

## 2a. Idle enemy ambience (`enemy.idle`)

The first sound in the game that exists to be *atmosphere* rather than feedback: an enemy
that has not noticed the player moans every 4-6 s, so a room announces its occupant before
anything is in sight. Clips are four ghost moans by **qubodup** (`Ghost Moans`) in
`Assets/Audio/Enemy/Idle/`, 3.0-5.7 s each — the only bank entry with real clip variation
so far, which is exactly the case D6 exists for.

It lives on `EnemyBase` rather than in a component of its own, for the same reason
`AudioRuntime` and `UiClickAudio` bootstrap themselves: enemies are spawned procedurally by
`DungeonPopulator` and restored from saves by `SaveManager`, so anything that has to be
dragged onto a prefab is a thing the next enemy type will be missing. Every `EnemyBase`
subclass gets it, and `idleSoundRadius = 0` opts one out.

Three decisions worth not re-deriving:

- **Everything except `FollowPlayer` counts as idle**, investigating included. `FollowPlayer`
  is the state that already has a voice — `enemy.alert` on entry, `enemy.attack` while it
  lands blows — and those two are what tell the player they have been seen. A moan over them
  blurs the one cue that has to stay unambiguous. Investigating is still idle on purpose:
  the enemy is looking, not looking *at you*, and hearing it search nearby is the point.
- **`idleSoundRadius` (26) is a gate, not a volume curve.** The entry's 3D falloff already
  decides loudness. Without the gate every enemy in the dungeon would still be *playing* —
  burning the 24-source pool (D5) on sounds attenuated to inaudibility and stealing voices
  from the ones the player can actually hear. Drawn as a gizmo (`GizmoRanges.EnemyIdleSound`);
  it is not a detection range and nothing about the AI changes at that boundary.
- **The reach is two numbers, and the smaller one wins.** The gate decides whether the
  sound plays; the entry's `maxDistance` (30) decides where the linear rolloff hits silence.
  Raising one without the other does nothing — a gate past `maxDistance` gates in sounds
  that are already inaudible, and a `maxDistance` past the gate is falloff nobody reaches.
  They are set deliberately wider than the player's own `FieldOfView.viewRadius` (15):
  hearing a thing you cannot see is the entire reason this sound exists. Cost of widening
  is quadratic — the 14 → 26 change put ~3.4× as many enemies in earshot at once, so past
  this point watch the pool before the volume.
- **The timer is pushed forward while the gate is shut**, not left running down. A free
  timer would fire the instant the player crossed the line, and a room of enemies that all
  idled through the same long silence would greet them in unison.

**The interval is measured start-to-start, and the clips are long.** At the 4 s end of the
range a 5.7 s moan overlaps the next one from the same enemy. That is the asked-for cadence
and it reads as intended for a haunted cellar, but it is the knob to turn first if the
moaning feels constant: raise `idleSoundIntervalMin` past the longest clip.

`AudioService.PlayAt`, never `PlayOn`: these clips outlive the enemy that started one (they
run seconds and an enemy can be killed mid-moan), and a pooled source parented to a
destroyed object goes down with it. The cost is that the moan does not travel with a
patrolling enemy — acceptable for a sound that is meant to be *somewhere over there*.

## 2b. The enemy's combat voice (`enemy.alert`, `enemy.attack`, `enemy.hurt`, `enemy.death`)

Four clips from the *monster_sfx_pack* set, one each, so the variation is pitch jitter
alone; the pack has six more files if the repetition starts to show.

- `Assets/Audio/Enemy/Alert/MonsterAlert01.wav` — 0.65 s bark on spotting the player.
- `Assets/Audio/Enemy/Attack/MonsterAttack01.wav` — 0.64 s growl on the swing.
- `Assets/Audio/Enemy/Hurt/MonsterHurt01.wav` — 0.34 s grunt on taking a hit.
- `Assets/Audio/Enemy/Death/MonsterDeath01.wav` — 1.0 s, the longest of the four.

Three of the four hooks were already firing at the right moment and merely logging; the
alert needed a latch once it became audible (2c). Worth knowing why these moments are
right, because they are the kind of thing a later refactor "tidies" into the wrong place:

- `EnemyBase.UpdateAlertAudio()` plays the bark **once per hunt**, latched, not once per
  transition into `FollowPlayer`. See 2c — this is the one hook that did have to change.
- `EnemyMeleeAttack.Attack()` plays the swing **on the wind-up, not on the hit landing**.
  `attackHitDelay` (0.4 s) exists to give the player a window to dodge, and a swing you
  cannot hear until it connects removes that window. The clip is 0.64 s against a 1.5 s
  `attackCooldown`, so consecutive swings from one enemy never overlap.
- `EnemyBase.TakeDamage()` picks between `enemy.death` and `enemy.hurt` on the health that
  is *already* deducted, so the grunt fires only on a hit the enemy survives — a killing
  blow gets the death sound instead, never both. It is also the **only** way an enemy dies:
  every subclass reaches `OnDeath()` through here, so nothing can die silently.
- The death sound is **the case D5 was written for, now real.** `OnDeath()` ends in
  `Destroy(gameObject)` a frame later, and the clip runs a full second — a component-owned
  `AudioSource` would cut itself off mid-death-rattle. `PlayAt` hands the sound to a pooled
  source parented to `AudioRuntime`, which outlives the corpse. Do not "improve" this to
  `PlayOn`.

Cooldowns here split on one question — *would a suppressed repeat cost the player something
they could not learn elsewhere?* — and that is the clearest illustration of what the field
is for:

- **`enemy.alert` and `enemy.attack` have none.** `AudioService` keys cooldowns on the id,
  so a guard would silence the *second* enemy of a pair spotting you, or swinging at you,
  together. Both of those are the highest-information sounds in the game — where a threat
  is, and that there is more than one of it — and a pack alerting in unison reading as one
  thick bark is the cheaper problem.
- **`enemy.hurt` keeps its 0.1 s.** `RangedAttack.projectilesPerShot` above 1 is a shotgun
  pattern, and at close range those pellets land on the same enemy within milliseconds;
  without the guard one trigger pull stacks several grunts into one distorted noise. The
  cost — two *different* enemies hit within 0.1 s producing a single grunt between them — is
  the cheaper trade here, because unlike the other two it costs the player no information
  they did not already have from their own shot.

## 2c. The alert barks once per hunt, not once per sighting

`enemy.alert` used to fire on every transition into `FollowPlayer`. That was defensible
while it only logged, and wrong the moment it had a clip.

Losing the player drops the chase into an investigate state; re-finding them from there is a
*second* transition in. So a player ducking in and out of cover — the core stealth loop, and
exactly what `PlayerHiding` and the table exist to encourage — got barked at every
`detectionMemoryDuration` (1.5 s). **An alert that fires that often stops meaning "it has
found you" and becomes texture.**

`EnemyBase.UpdateAlertAudio()` now latches `hasAlertedThisHunt` on the bark and clears it
only in `Idle`, `ReturnToPatrol` or `WanderNearLastPosition` — the three states that mean
the enemy gave up and the trail went cold. Investigating deliberately does **not** clear it:
the enemy is still hunting, and picking the trail back up is the same hunt continuing. The
next real sighting after it gives up barks again.

Side effect worth knowing: `stateBefore` in `UpdateStateMachine()` existed only to catch
that transition and is gone. Anything that needs a "state changed this tick" signal has to
reintroduce it rather than assume it is there.

A save loaded while an enemy is mid-chase barks once on the first tick, because the latch is
not serialized. That reads as intended — you are being hunted, and it says so — but it is a
consequence of the flag being runtime-only, not a decision anyone made per-enemy.

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
| Sprint | 0.27 s | asked for — the clip's own length, so steps run back to back with no silence at all. The gaplessness is what separates a run from a fast walk, and it is why sprint reuses the walk clip without sounding like one |
| Sneak | 0.7 s | the only feedback that sneaking is working, since it emits no gameplay noise |

Sprint's interval is therefore **tied to the clip**, not to a gait: replacing `footstep03.ogg`
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
