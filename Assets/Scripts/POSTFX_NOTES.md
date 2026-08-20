# Post-Processing — Notes

Working reference for the horror post-processing stack added on `GU-0070-horror-post-processing`,
kept up to date whenever post-FX code changes are committed.

## Setup

**`Tools ▸ Horror Post FX ▸ Set Up Horror Post-Processing`** does everything. It is idempotent —
re-run it after opening a different scene, or after regenerating assets. `Tools ▸ Horror Post FX ▸
Disable Horror Post-Processing` takes the fullscreen pass back out and switches the volume object
off, for A/B-ing the look without deleting anything.

The setup tool exists because two of the six steps are not things you can reasonably hand-author:
a `VolumeProfile` stores each override as a sub-asset, and adding a renderer feature means writing
both `m_RendererFeatures` and the parallel `m_RendererFeatureMap` inside `Renderer2D.asset`.

## The flag that made all of this a no-op

`Assets/Prefabs/Player.prefab` shipped with `m_RenderPostProcessing: 0` on its `Main Camera`. With
that off, **every Volume in the project renders nothing** — no error, no warning, the profile just
does not apply. If post-FX ever "stops working", check this flag first. The setup tool sets it on
the prefab and on every non-prefab camera in the open scene.

## Which effect lives where, and why

Two layers, split by what each one can express:

| Layer | Holds | Why there |
|---|---|---|
| `Settings/HorrorVolumeProfile.asset` | Tonemapping, Color Adjustments, Split Toning, Vignette, Film Grain, Chromatic Aberration, Bloom | Colour work. Tunable live in the inspector, no shader recompile, and URP already does it efficiently. |
| `Shaders/HorrorFullScreen.shader` | Barrel warp, breathing zoom, scanlines, glitch tearing + colour split | UV-space distortion. **No Volume override can express this** — grading operates per pixel in place, it cannot move a pixel. |

Do not migrate effects across that line. Re-implementing a vignette in the fullscreen shader would
just be a second, worse vignette that the profile cannot tune.

### Profile values worth not "fixing"

- **Tonemapping is Neutral, not ACES.** ACES rolls contrast in a way that fights `DarknessOverlay`
  (0.97 alpha over everything unlit, see `ENEMY_NOTES.md` GU-0036) and crushes what little detail
  is left in the shadows — which is exactly where the game asks the player to look.
- **Bloom threshold is 0.95, deliberately just under 1.** Anything lower and the bloom starts
  eating the pixel art itself rather than only the lamps, and crisp sprites turn to mush. This is
  the single easiest setting to ruin the look with.
- **Film Grain response is 0.8.** High response keeps grain off the bright areas, so lamps stay
  clean and only the dark two-thirds of the frame get noisy.
- **The URP asset is switched to HDR colour grading** by the setup tool. In LDR the grading LUT is
  built in display space and a look this dark bands visibly in every lamp's falloff.

## The reactive layer

`PostProcessing/HorrorPostProcessing.cs` on the `Global Volume` object. A fixed profile makes every
room look identical; this drives the overrides from four inputs, each given its own visual language
so they stay tellable apart:

| Input | Source | Reads as |
|---|---|---|
| **Dread** | missing player health, below 60% | vignette closes in + heartbeat pulse (60→150 bpm), colour drains, grain rises |
| **Alert** | any `EnemyBase` in `EnemyState.FollowPlayer` | tighter vignette + chromatic aberration |
| **Glitch** | player took damage, or a `BrokenLightFlicker` blacked out within 12 units | tearing, colour split, brief exposure dip |
| **Master** | `intensity` field | scales everything; 0 leaves the frame untouched |

Every write is **baseline + reaction**, where the baseline is read out of the profile in `Awake`.
Re-tuning the profile asset therefore keeps working — the reactions ride on top of whatever is
authored rather than replacing it.

It reads `Volume.profile`, **not** `sharedProfile`. The former hands back a runtime copy, so a play
session cannot write its per-frame values back into the asset on disk. Getting this wrong means the
profile slowly drifts to whatever the player's health was when you last hit stop.

### Why enemies and lamps are polled, not injected

Both are spawned at runtime by the dungeon generator (`GENERATION_NOTES.md`), so there is nothing to
wire up in the scene, and `EnemyBase` / `PlayerHealthSystem` raise no events. `RefreshTracking()`
rescans every 2 s and the per-frame code only walks the cached arrays. If `EnemyBase` ever gains a
detection event, the `UpdateAlert` poll should move over to it; the lamp poll should not, because it
watches for the *edge* from lit to dark rather than for a state.

The lamp check is on the edge for a reason: `BrokenLightFlicker` steps its intensity every few
frames, so reacting to the intensity value itself would glitch the screen continuously under any
flickering lamp.

`HorrorPostProcessing.TriggerGlitch(strength)` is public — anything with a reason to rattle the
image (a scripted scare, a door coming down) can fire one without this class knowing about it.

## The fullscreen pass

`PostProcessing/HorrorFullScreenFeature.cs`, injected at `AfterRenderingPostProcessing`. The warp
has to run **on top of** the Volume's grading and bloom; otherwise the tearing gets graded and
re-bloomed and stops reading as a broken signal.

Hand-rolled rather than URP's built-in `FullScreenPassRendererFeature` so the setup tool can assign
the material from script against fields this repo owns, instead of reflecting over URP internals
whose names move between package versions.

Two shader uniforms are set from script via `Shader.SetGlobalFloat` — `_HorrorIntensity` and
`_HorrorGlitch`. **They are declared outside the `UnityPerMaterial` CBUFFER on purpose.** A property
inside that CBUFFER takes its value from the material, which would shadow the global and leave both
permanently at 0.

## Interaction with the vision system

The FOV stencil work (`ENEMY_NOTES.md` GU-0036) all happens during scene rendering, well before any
of this. `DarknessOverlay` is a world-space quad at sorting order 6; post-processing runs after the
whole sorting-layer pass, so the two do not interact and the stencil buffer is untouched here.

Practical consequence: **the darkness is graded and vignetted too.** If the unlit parts of the frame
start looking milky rather than black after a profile change, the cause is Color Adjustments'
contrast or the bloom threshold, not the overlay.
