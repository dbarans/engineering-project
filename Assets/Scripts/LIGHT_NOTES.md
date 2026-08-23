# Lighting — Notes

Working reference for the light system as it stands after `GU-0076-torch-lamp-glass`, which gave
light a colour, gave the lamp a warmer and much softer pool, spread lamps through the dungeon, and
put a craftable torch in the player's hand. Kept up to date whenever lighting code is committed.

## The pipeline in one paragraph

Nothing in this game is lit by Unity's renderer. Every light source raycasts its own visible area
into a mesh (`Vision/OcclusionMeshBuilder.cs`) and draws that mesh, through
`Shaders/VisionMaskWriter.shader`, into one offscreen texture — the **vision mask**
(`Vision/VisionMaskRenderer.cs`, published as the global `_VisionMask`). Two things then read that
mask: `Shaders/DarknessOverlay.shader`, a quad over the whole scene that darkens by how little
light reaches each pixel, and `Shaders/SpriteFovMasked.shader`, which fades out sprites nothing
lights. There are exactly two kinds of light source: `Vision/FieldOfView.cs` (the player's cone
plus the circle around them) and `Light/StationaryLightSource.cs` (a lamp).

## What the mask holds

| Channel | Meaning |
|---|---|
| Alpha | How lit the pixel is: 1 fully lit, 0 pitch dark, a gradient over each light's rim. |
| RGB | The colour that light **casts** on what it lights, weighted by how strongly it reaches. |

The texture was `R8` and is now `ARGB32`. Alpha means exactly what the old single channel meant, so
every consumer that only asks "can this be seen" is unchanged.

**"Casts" is the load-bearing word.** RGB is not how bright the light is, it is how much the light
colours the scene. Plain eyesight casts *nothing* — black — and leaves the world its own colours; a
lamp or a torch casts amber. That is not a stylistic choice, it is what makes the mask combinable:
lights are merged with `BlendOp Max`, per channel, and if the player's own vision wrote white it
would win every channel and erase the colour of every lamp it happened to overlap — which is
precisely the ground the player is looking at. Writing black instead means a light with nothing to
say about colour can never out-max one that has something to say.

`BlendOp Max` itself is not up for renegotiation: adding lights instead would make the overlapping
rims of two lamps brighter than either lamp, and a visible seam appears where they meet.

## Getting the colour onto the screen

`DarknessOverlay` does both jobs in one pass, using premultiplied alpha (`Blend One
OneMinusSrcAlpha`) instead of the usual source-alpha blend: alpha darkens, RGB adds the tint. With
RGB at zero it behaves exactly like the plain black overlay it replaces.

The tint added is only the part of the cast colour that is *not* neutral grey
(`rgb - min(r, g, b)`), scaled by `_TintStrength` (0.4 on `Materials/DarknessOverlay.mat`). So a
white light contributes nothing at all and a warm one contributes only its warmth. Passing the raw
RGB instead would brighten as much as it tinted, and every lit floor would drift towards white.

`_TintStrength` is the one knob to turn if the look is wrong. It is additive, so it brightens as it
warms; much above ~0.8 the floor under a lamp blows out to flat orange.

## Everything burns

There is no electricity in this game, so `FlameFlicker` is the only `ILightIntensity` in the
project: a gentle Perlin wobble, seeded per instance so two lights in view never waver together.
The lamp and the torch differ only in their numbers — the lamp at `strength 0.12 / speed 1.6`
behind its glass, the torch at `0.18 / 3.5` out in the open air.

`BrokenLightFlicker` — the failing-bulb component, with its flutter bursts and blackouts — was
deleted with this pass. Its whole premise was a badly wired lamp, which this setting cannot have.
Should a light ever need to *fail* rather than waver, it wants a new component (guttering fuel,
not a loose connection), not that one back.

## The lamp

`StationaryLightSource` gained two fields, both pushed per lamp through a `MaterialPropertyBlock`
alongside the flicker's intensity — which is what lets every light in the scene share one mask
material and still look like a different kind of light:

- **`lightColor`** — default `(1, 0.68, 0.34)`. An oil flame is the only warm thing in a dungeon
  otherwise lit by colourless vision, and that is what separates a *lit* room from a *seen* one at a
  glance.
- **`edgeSoftness`** — default `0.6`, i.e. the outer 60% of the radius is a fade. A hard-edged disc
  of light reads as a decal on the floor; a lamp should bleed into the dark.

`Prefabs/Lamp.prefab` carries those defaults, with the radius raised `4.15 → 5.5` so the *fully*
lit core stays roughly the size it was while the fade reaches much further.

The same prefab had `obstacleMask` set to nothing, so its light passed straight through walls. It
is now `4864` — ObstacleStatic, ObstacleDynamic, Door — the same mask the player's field of view
raycasts against.

## The held torch

An item is a light source because of what is on the item, not because of its name: `ItemData` has

- **`lightRadius`** — how far it lights around the player. 0 (the default) means it is not a light.
- **`lightColor`** — the colour that light casts.

`Light/HeldTorch.cs` on the player watches the hotbar selection, and while a light-source item is
selected it calls `FieldOfView.SetHeldLight(lit, radius, colour, intensity)` every frame. Three
things change while lit:

- the cone widens from the base `viewAngle` (100°) to `heldViewAngle` (130°) and reaches out from
  the base `viewRadius` (15) to `heldViewRadius` (20) — a torch is meant to light up more of the
  room, not just further down it;
- the 360° circle behind the player (the old "lantern" path, renamed to *held light*) grows to
  `radius`, the value `HeldTorch` passes in from the item's own `lightRadius`;
- everything the view draws is tinted by the flame's colour instead of by `viewTint`.

The cone's own angle and radius are not item-driven — `heldViewAngle`/`heldViewRadius` are tuned
once for "a torch is out", not per item, the same way the base `viewAngle`/`viewRadius` are tuned
once for bare-handed. Only the near-circle radius and the colour come from the item. Aiming still
wins over the torch: `SetAimNarrowing` narrows `TargetViewAngle` down from whichever base angle
(held or bare-handed) is currently in play, so lining up a shot narrows the view exactly as it
would with no torch in hand.

The wavering comes from a `FlameFlicker` sitting next to `HeldTorch` — the same `ILightIntensity`
component a lamp uses, so the torch burns by the project's existing flame code rather than by a
second copy of it.

**The whole view is tinted while a torch burns, not only the circle.** The cone and the circle are
one mesh sharing one centre vertex, so the region flag along it interpolates and a per-region
colour would fade from white at the player's feet to amber at the rim — worse than either extreme.
Tinting all of it is also the truer read: with a torch in hand, everything the player sees *is* lit
by the torch.

## Where lamps come from

The hub is furnished with `hubLamps` as before. Ordinary rooms now get their own:
`RoomContentSettings.lampChancePerRoom` (0.55) is rolled once per lamp up to `maxLampsPerRoom` (2),
so most rooms hold one lamp, a few hold two, and a few hold none — and it is the unlit ones that
make the rest worth walking towards.

They are placed by the same wall-hugging fixture placement the hub's lamps use, and placed *before*
the room's props, because both want the cells along a wall and a room reads far worse with its
light out in the middle than with one barrel fewer against the wall. A lamp that finds no cell is
silently skipped: unlike a missing save station, it is a darker room rather than a broken run.

## Crafting a torch

**1 Wood + 1 Alcohol → 1 Torch**, at the crafting table.

Table-gated on purpose. Both ingredients are ordinary floor loot, so how much light a run has is
decided by what the player finds; making them at the hub means light is something to walk back for
and stock up on, which is what turns "go out with two torches" into a decision. Moving it to the
always-available list is one line in `Editor/CraftingBoxSetup.cs` if that proves wrong in play.

## Editor steps

Both are idempotent, and both must be run once for this branch's content to exist:

1. **Tools ▸ Items ▸ Build Torch** — draws the icon (`Editor/TorchArt.cs`), creates the item and
   the recipe, adds the recipe to the crafting table prefab, and makes sure the player prefab
   carries `HeldTorch` + `FlameFlicker`.
2. **Tools ▸ World ▸ Build Broken Glass Prefab** — redraws the four glass patches
   (`Editor/BrokenGlassArt.cs`) and rebuilds the prefab around them.

Regenerating the dungeon afterwards is what puts the new lamps in the world.

## Known limits

- **No fuel.** A torch burns forever once crafted. `ILightFuel` is the seam this would hang off
  (`StationaryLightSource` already honours it); `HeldTorch` does not read it yet.
- **Colours combine per channel.** Two lights of the same warm family overlap fine. A cold light
  next to a warm one would produce the per-channel maximum of the two, which is not a colour either
  lamp is. Nothing in the game does this today.
- **The numbers are chosen, not tuned.** None of this has been run in the editor — radius, softness
  and `_TintStrength` were picked by reasoning about the floor palette, and the first play session
  should expect to move them.
