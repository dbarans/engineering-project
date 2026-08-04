# Player System — Animation Notes

Frame-by-frame sprite animation for the player, built the same way as the enemies: no Unity
Animator, no `.controller`, no `.anim` clips anywhere in the project. A small frame player
swaps `SpriteRenderer.sprite` at a per-clip fps, and a driver script picks the clip from
gameplay state.

## Art folders (Polish → English)

All frames live under `Assets/Art/PLAYER`, 1900×1900 PNGs, one folder per clip.

| Word | Meaning |
| --- | --- |
| `NOGI` | legs |
| `TLOW` | torso (`tułów`) |
| `CHODZENIE` | walking |
| `BIEG` | running / sprint |
| `BRON` | weapon (`broń`) |
| `CELOWANIE` | aiming |
| `STRZAL` | shot / firing (`strzał`) |

| Folder | Frames | Body part | Meaning |
| --- | --- | --- | --- |
| `CHODZENIE_NOGI` | 50 | legs | walk cycle |
| `BIEG_NOGI` | 50 | legs | run cycle |
| `CHEDZENIE_TLOW` | 25 | torso | walk, empty-handed (folder name is a typo for `CHODZENIE_TLOW`) |
| `BIEG_TLOW` | 50 | torso | run, empty-handed |
| `CHODZENIE_TLOW_BRON` | 25 | torso | walk carrying the ranged weapon |
| `CHODZENIE_TLOW_BRON_CELOWANIE` | 25 | torso | walk while aiming down the weapon |
| `CHODZENIE_TLOW_STRZAL` | 25 | torso | firing |

The clip names in code drop the typo: the `CHEDZENIE_TLOW` folder is loaded as the clip
`CHODZENIE_TLOW`. The art folder is left exactly as exported.

There is **no idle/stand art** for either half — everything is locomotion. Standing still
therefore rewinds the current clip to frame 0 and freezes it, instead of walking on the spot.

The **legs go further and clear their sprite entirely** while the player is not moving
(`PlayerAnimationDriver.hideLegsWhenStanding`, on by default): a lone pair of boots frozen
mid-stride reads worse than no boots. The clip and frame are kept, so the legs reappear on the
same frame the moment the player moves again. Turn the flag off to hold frame 0 instead.

## Runtime pieces

- `Animation/SpriteFrameAnimator.cs` — the frame player, shared with the enemies
  (`EnemySpriteAnimator` is now an empty subclass of it, so the enemy prefabs and their
  serialized clip data are untouched).
- `Player/PlayerAnimationDriver.cs` — on the Player root. Holds a reference to the torso
  animator and the legs animator and picks a clip for each every frame.

The player is drawn as two independently rotated halves: `PlayerLegs` turns `Legs` toward the
movement direction, `PlayerAim` turns `Torso` toward the mouse. Each half gets its own
animator and its own clip choice.

### Legs

| Condition | Clip |
| --- | --- |
| sprinting | `BIEG_NOGI` |
| otherwise | `CHODZENIE_NOGI` |

### Torso

Checked in order — the first row that matches wins:

| Condition | Clip |
| --- | --- |
| shot just fired | `CHODZENIE_TLOW_STRZAL` (one-shot, plays to the end) |
| **sprinting** | `BIEG_TLOW` — weapon selected or not |
| ranged weapon selected, prepare button held | `CHODZENIE_TLOW_BRON_CELOWANIE` |
| ranged weapon selected | `CHODZENIE_TLOW_BRON` |
| otherwise | `CHODZENIE_TLOW` |

"Ranged weapon selected" is `PlayerWeaponManager.ActiveWeaponType`, which follows the hotbar
selection — scrolling onto a ranged weapon swaps the torso to the armed clip immediately, and
scrolling off it swaps back.

Sprinting outranks the loadout. There is no armed run art, so the player is drawn empty-handed
while sprinting; sprint and aim never overlap anyway, because `PlayerInputHandler` refuses to
start a charge while sprinting and refuses to start a sprint while aiming. Melee has no
dedicated torso art either and uses the empty-handed clips.

### Tempo sync

Legs cycles are 50 frames and torso walk cycles are 25, so the loader gives the torso half the
fps — one stride lasts the same on both halves (1 s at multiplier 1). The driver then scales
playback with actual `Rigidbody2D` speed against `walkSyncReferenceSpeed` /
`runSyncReferenceSpeed` (defaults 5 / 8, matching `PlayerMovement`), so the feet don't slide.

## Editor setup

1. **Tools > Player > Reimport Frames** — required once. The frames arrive sliced as
   `Multiple` with per-frame tight crops; a pivot centered on each frame's own crop makes the
   character jitter, and `LoadAssetAtPath<Sprite>` cannot load them at all.
   `Editor/PlayerFrameImporter.cs` forces `Single` + centered pivot on the full canvas.
2. **Tools > Player > Setup Animations** with the Player selected — adds a
   `SpriteFrameAnimator` to `Torso` and `Legs`, loads their frames, and adds + wires
   `PlayerAnimationDriver` on the root.

`Tools > Player > Load Frames Into Selected` reloads just one half (torso or legs, decided by
the object's name) after the art changes.

## Facing — the art is drawn +Y, the player's forward is +X

The frames are drawn top-down **facing up (+Y)**: in `CHODZENIE_TLOW_CELOWANIE00` the revolver
points straight up the canvas, in `CHODZENIE00` the boots point up.

The player's forward is **+X**, and `Direction` is what defines it: it sits at `Torso` local
`(0.548, 0, 0)` with identity rotation, `MeleeWeapon` / `RangedWeapon` hang off it, and
`RangedAttack` fires along `shootPoint.right`. So the art needs a **−90°** turn.

That turn must **not** go on `Torso`. `Direction`, the weapon, the shoot point and the aim
lines are all children of `Torso` — turning `Torso` swings the weapon 90° off the crosshair
while the sprite looks right. Instead:

- **Torso** — the setup tool creates a `TorsoVisual` child of `Torso`, rotated −90°, and moves
  the `SpriteRenderer` (material, colour, mask interaction preserved) plus the
  `SpriteFrameAnimator` onto it. `Torso` itself carries no art and stays exactly on the aim
  axis, so `PlayerAim` rotates it with no offset. Same shape as `SkullGuySetup`, where the
  enemy sprite lives on a `Visual` child rather than on the logic root.
- **Legs** — `Legs` has no children, so the turn goes straight on the transform:
  `PlayerLegs.spriteForwardOffsetDeg = -90` (which is also its default — the original
  hardcoded `angle - 90f` says the old placeholder legs faced +Y too).

If the art is ever re-exported facing a different way, change `ArtForwardOffsetDeg` in
`Editor/PlayerAnimationSetup.cs` and re-run the tool — it drives both halves. Never add an
offset to `PlayerAim`.

## Draw order (legs under torso)

Both halves ship at `Order in Layer 0` on the `Default` layer with the same z, and the project
uses the default transparency sort mode — so nothing decides the tie and the legs can pop over
the torso from frame to frame.

`Tools > Player > Setup Animations` fixes this by putting a `SortingGroup` on `PlayerModel`
(order 0) and ordering the halves inside it: legs 0 < torso 1 < carried weapon 2.

Two reasons it is a sorting group rather than plain per-renderer orders:

- Order 1 on the `Default` layer is already used by `Table`, `Door_System` and `CraftingTable`,
  which are deliberately drawn over the player so `PlayerHiding` can put it under a table.
  Raising the torso to 1 would make the player draw over the table it is hiding under. The
  group keeps the whole player at order 0 against the world.
- The group must go on **`PlayerModel`, never on the player root**: `FieldOfView` parents its
  stencil prepass (order −10) and its mask mesh (order 5) to the root, and pulling those into a
  sorting group would break the vision masking.

For reference, the orders in use project-wide: FOV stencil prepass −10, sprites 0, tables /
doors 1, dropped items 5, FOV mask mesh 5, darkness overlay 6, melee area indicator 10.

## Tuning knobs

- `PlayerFrameImporter.PixelsPerUnit` (300) — on-screen size of the player. The `Torso` and
  `Legs` transforms still carry the old placeholder scales (0.58 / 0.76); reset them to 1 and
  size the player from the PPU so the two halves stay in proportion.
- `PlayerAnimationSetup.ArtForwardOffsetDeg` (−90) — art facing, see above. Never put an offset
  on `PlayerAim`.
- `PlayerAnimationDriver` walk/run reference speeds — raise if the feet slide backward.

## Known follow-ups

- **The frames are not trimmed.** `SKULL-GUY-optimized` was pre-processed offline to a shared
  512×485 canvas; the player frames are raw 1900×1900 (70 MB on disk, ~275 frames), so the
  importer clamps `maxTextureSize` to 512 and the character ends up at roughly 160 px of
  usable detail. Cropping all frames offline to one shared tight canvas would buy both sharper
  art and lower memory — that is the single biggest win available here.
- No idle art for either half (see above).
- No armed-sprint and no melee torso art.
