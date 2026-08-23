using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using static ProceduralArt;

/// <summary>
/// Draws the tiles that lie on the floor in the hand-inked idiom and hands them to
/// <see cref="DungeonSceneSetup"/> to be scattered with the rest.
///
/// Separate from the generated decals in that file on purpose, and drawn at a different
/// resolution. Those are 32-pixel placeholders that share the tile grid; these are 128, the
/// same as <c>ArtTilePixels</c>, because the floor they lie on is the hand-drawn
/// <c>FURNITURE_pngy_podloga</c> art at that resolution. A 32-pixel decal on a 128-pixel
/// floor is four times chunkier than the stone it is lying on, which is exactly the
/// mismatch this set exists to avoid.
///
/// Three things nobody would build but that a cellar collects: a smashed pot, a length of
/// chain someone left, and a rat that did not get out, lying on its side. They are scenery,
/// not loot — under this project's convention a decal never blocks movement or sight, so
/// none of them needs a collider or a cell type.
///
/// The rats that *did* get out are not in here at all. A live one is an object with
/// <see cref="WanderingRat"/> on it, drawn and built by <c>RatSetup</c>; this file only
/// ever draws the dead one.
/// </summary>
public static class InkedTileGenerator
{
    private const string TilesFolder = "Assets/Generation/Tiles";

    /// <summary>Matches <c>DungeonSceneSetup.ArtTilePixels</c> — the resolution the floor art is cut at.</summary>
    private const int DecalPixels = 128;

    private static readonly Color Ink = new Color32(0x1B, 0x17, 0x13, 0xFF);
    private const float InkMin = 3f;
    private const float InkMax = 5.5f;

    private static readonly Color ClayColor = new Color32(0x8C, 0x6A, 0x4E, 0xFF);
    private static readonly Color ClayInner = new Color32(0x5E, 0x46, 0x33, 0xFF);

    private static readonly Color ChainColor = new Color32(0x67, 0x63, 0x5C, 0xFF);
    private static readonly Color ChainRust = new Color32(0x76, 0x50, 0x34, 0xFF);

    private static readonly Color FurColor = new Color32(0x4E, 0x47, 0x40, 0xFF);
    private static readonly Color TailColor = new Color32(0x6B, 0x5C, 0x54, 0xFF);

    // Measured off FURNITURE_pngy_podloga: the hand-drawn floor averages #3E3B33, a dark
    // warm brown. Every tile that has to sit on it is keyed to that, which is the whole
    // point of this pass — the generated rubble was #39413F to #4E5855, a cool grey nearly
    // twice as light, and it read as a slab of static dropped into the room.
    private static readonly Color FloorStone = new Color32(0x3E, 0x3B, 0x33, 0xFF);
    private static readonly Color RubbleStone = new Color32(0x4A, 0x45, 0x3B, 0xFF);
    private static readonly Color RubbleShadow = new Color32(0x2A, 0x27, 0x21, 0xFF);
    private static readonly Color SlabStone = new Color32(0x47, 0x43, 0x3A, 0xFF);
    private static readonly Color MortarColor = new Color32(0x2E, 0x2B, 0x25, 0xFF);

    /// <summary>
    /// Draws the inked decals and returns their tiles, creating the assets on first run.
    /// Called by <see cref="DungeonSceneSetup"/> during setup, and again with
    /// <paramref name="overwrite"/> from its redraw menu item.
    /// </summary>
    public static Tile[] EnsureInkedDecals(bool overwrite = false)
    {
        var specs = new (string name, Action<ArtCanvas> draw)[]
        {
            ("DecalShards", DrawShards),
            ("DecalChain", DrawChain),
            ("DecalRat", DrawRat)
        };

        var tiles = new List<Tile>();
        foreach (var (name, draw) in specs)
        {
            Tile tile = EnsureTile(name, draw, overwrite);
            if (tile != null) tiles.Add(tile);
        }

        return tiles.Count > 0 ? tiles.ToArray() : null;
    }

    /// <summary>
    /// Collapsed masonry, as three interchangeable variants. Solid like a wall despite
    /// reading as debris — the painter puts it in the wall tilemap, so the cell blocks
    /// movement and sight whatever the drawing looks like.
    ///
    /// Drawn with the floor showing between the chunks, which the earlier 32-pixel version
    /// could not do: it filled the cell with speckle on a four-pixel lattice, and a room
    /// with a collapse in it came out as a rectangle of noise. The painter floors rubble
    /// cells like any other non-wall cell, so the gaps have real ground under them.
    /// </summary>
    public static Tile[] EnsureRubble(bool overwrite = false)
    {
        return EnsureSet(overwrite, Tile.ColliderType.Grid,
            ("RubbleStoneA", c => DrawRubble(c, 0x301u)),
            ("RubbleStoneB", c => DrawRubble(c, 0x302u)),
            ("RubbleStoneC", c => DrawRubble(c, 0x303u)));
    }

    /// <summary>
    /// The exit room's floor: one laid flagstone per cell, in mortar.
    ///
    /// A flagstone per cell rather than a course of slabs running across cells, because a
    /// pattern that crosses a cell border has to line up with its neighbour, and the painter
    /// picks these per cell from the seed — neighbours are not the same variant and could
    /// never be made to match. One slab per cell is the only pattern that tiles under a
    /// random pick.
    ///
    /// It says "different room" by being <b>worked</b>, not by being brighter. The version
    /// this replaces was two shades lighter than the floor around it on the theory that the
    /// exit has to register inside the vision cone; against the real floor art that came out
    /// as a pale patch rather than as a change of surface.
    /// </summary>
    public static Tile[] EnsureExitFloor(bool overwrite = false)
    {
        return EnsureSet(overwrite, Tile.ColliderType.None,
            ("ExitFloorStoneA", c => DrawExitFloor(c, 0x401u)),
            ("ExitFloorStoneB", c => DrawExitFloor(c, 0x402u)),
            ("ExitFloorStoneC", c => DrawExitFloor(c, 0x403u)));
    }

    /// <summary>Draws a set of variants and returns their tiles, skipping any that fail.</summary>
    private static Tile[] EnsureSet(bool overwrite, Tile.ColliderType collider,
        params (string name, Action<ArtCanvas> draw)[] specs)
    {
        var tiles = new List<Tile>();
        foreach (var (name, draw) in specs)
        {
            Tile tile = EnsureTile(name, draw, overwrite, collider);
            if (tile != null) tiles.Add(tile);
        }

        return tiles.Count > 0 ? tiles.ToArray() : null;
    }

    /// <summary>
    /// Loads a decal's tile, drawing the texture when it is missing or a redraw was asked
    /// for. Art replaced by hand survives an ordinary setup run, the same rule the rest of
    /// the generated tiles follow.
    /// </summary>
    private static Tile EnsureTile(string name, Action<ArtCanvas> draw, bool overwrite,
        Tile.ColliderType collider = Tile.ColliderType.None)
    {
        string texturePath = $"{TilesFolder}/{name}.png";
        string tilePath = $"{TilesFolder}/{name}.asset";

        Sprite sprite;
        if (overwrite || AssetDatabase.LoadAssetAtPath<Sprite>(texturePath) == null)
        {
            var canvas = new ArtCanvas(DecalPixels, DecalPixels);
            draw(canvas);

            // Pixels-per-unit equal to the canvas side, so one decal covers exactly one cell
            // however many pixels it is drawn at.
            sprite = WriteSprite(canvas, texturePath, DecalPixels);
        }
        else
        {
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
        }

        if (sprite == null) return null;

        var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, tilePath);
        }

        tile.sprite = sprite;
        tile.colliderType = collider;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>
    /// Renders one shape with a wobbling contour and an uneven ink line, the same way the
    /// item icons are drawn. Duplicated rather than shared with
    /// <c>ItemIconGenerator</c> because the two want different line weights — an icon is
    /// shown at 128 pixels and these are shown at whatever a tile is on screen.
    /// </summary>
    private static void Fill(ArtCanvas canvas, Func<Vector2, float> field,
        Func<Vector2, float, Color> wash, uint salt)
    {
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                float distance = Wobble(field(point), point, 1.8f, 0.05f, salt);

                float coverage = Coverage(distance);
                if (coverage <= 0f) continue;

                Color color = wash(point, distance);
                color = Color.Lerp(color, Ink, EdgeBand(distance, InkWidth(point, InkMin, InkMax, salt, 0.04f)));
                canvas.Blend(x, y, color, coverage);
            }
        }
    }

    /// <summary>Flat colour with a blotchy wash and dirt gathering at the contour.</summary>
    private static Func<Vector2, float, Color> Wash(Color baseColor, uint salt, float blotch = 0.08f)
    {
        return (point, distance) =>
        {
            Color color = Shade(baseColor, Noise(point, 0.03f, salt, 2) * blotch * 2f);
            color = Shade(color, Noise(point, 0.12f, salt ^ 0x9E3779B9u, 2) * blotch);

            float edge = Mathf.Clamp01(1f + distance / 10f);
            return Shade(color, -edge * 0.08f);
        };
    }

    /// <summary>
    /// A heap of collapsed masonry: broken blocks of the wall's stone, piled where they
    /// fell, with the floor showing between them.
    ///
    /// The blocks are placed on a jittered grid and rotated, rather than scattered freely.
    /// Free scatter leaves holes in some tiles and clumps in others, and a rubble tile that
    /// is half empty stops reading as rubble; a jittered grid guarantees cover while still
    /// looking unplanned.
    /// </summary>
    private static void DrawRubble(ArtCanvas canvas, uint salt)
    {
        var random = new DeterministicRandom($"rubble{salt}");

        // A shadow under the heap, so the blocks sit on the floor instead of floating on it.
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                float pool = Ellipse(point, new Vector2(64f, 60f), new Vector2(56f, 50f));
                pool = Wobble(pool, point, 6f, 0.02f, salt);

                float coverage = Coverage(pool);
                if (coverage > 0f) canvas.Blend(x, y, RubbleShadow, coverage * 0.5f);
            }
        }

        // Nine blocks on a three-by-three grid, each nudged off its cell and turned.
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                var at = new Vector2(
                    26f + column * 38f + (random.NextFloat() * 2f - 1f) * 11f,
                    26f + row * 38f + (random.NextFloat() * 2f - 1f) * 11f);

                var half = new Vector2(
                    Mathf.Lerp(11f, 20f, random.NextFloat()),
                    Mathf.Lerp(9f, 16f, random.NextFloat()));

                float turn = random.NextFloat() * 180f;
                uint blockSalt = salt ^ (uint)(row * 3 + column) * 0x9E3779B9u;

                // Each block a shade of its own, spread across the range rather than drawn
                // at random, so no heap comes out uniformly dark or uniformly light.
                float tone = ((row * 3 + column) * 0.6180339887f) % 1f;
                Color stone = Shade(RubbleStone, (tone - 0.5f) * 0.09f);

                Fill(canvas,
                    p => RoundedBox(Rotate(p, at, -turn), at, half, 3f),
                    Wash(stone, blockSalt, 0.07f), blockSalt);
            }
        }
    }

    /// <summary>
    /// One laid flagstone, inset in its mortar. The inset is what makes the tile repeat
    /// cleanly: the drawing never touches the cell border, so nothing has to line up with
    /// whatever variant the painter puts next to it.
    /// </summary>
    private static void DrawExitFloor(ArtCanvas canvas, uint salt)
    {
        // Mortar first, across the whole cell — this is a floor tile, so it has to be
        // opaque or the void shows through the room.
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                Color color = Shade(MortarColor, Noise(point, 0.04f, salt, 2) * 0.09f);
                canvas.Blend(x, y, color, 1f);
            }
        }

        var centre = new Vector2(64f, 64f);
        float Slab(Vector2 p) => RoundedBox(p, centre, new Vector2(56f, 56f), 7f);

        Fill(canvas, Slab, (point, distance) =>
        {
            // Keyed a shade off the floor's own colour rather than picked to contrast with
            // it: the exit reads as a different surface because it is cut and laid, not
            // because it is a different colour.
            Color color = Wash(SlabStone, salt, 0.07f)(point, distance);
            return Shade(color, Noise(point, 0.09f, salt ^ 0x5Au, 2) * 0.05f);
        }, salt);

        // A chip out of one corner and a crack across the face, so a room of these does not
        // read as tiling however few variants there are.
        var random = new DeterministicRandom($"exit{salt}");
        var from = new Vector2(random.NextFloat() * 40f + 14f, 14f);
        var to = new Vector2(random.NextFloat() * 40f + 74f, 114f);
        Crack(canvas, Slab, from, to, salt);
    }

    /// <summary>A hairline crack across a slab, tapering at both ends.</summary>
    private static void Crack(ArtCanvas canvas, Func<Vector2, float> clip, Vector2 from,
        Vector2 to, uint salt)
    {
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                if (clip(point) > -InkMin) continue;

                Vector2 axis = to - from;
                float along = Mathf.Clamp01(Vector2.Dot(point - from, axis) / Vector2.Dot(axis, axis));
                float width = 1.5f * Mathf.Lerp(0.2f, 1f, Mathf.Sin(along * Mathf.PI));

                float distance = Wobble(Segment(point, from, to, width), point, 2.2f, 0.06f, salt);
                float coverage = Coverage(distance);
                if (coverage > 0f) canvas.Blend(x, y, Ink, coverage * 0.55f);
            }
        }
    }

    /// <summary>
    /// A smashed pot: the base still recognisable and the wall of it in pieces around it.
    /// The curve of each shard is what says pottery — flat triangles would read as stone
    /// chips, which the floor already has a decal for.
    /// </summary>
    private static void DrawShards(ArtCanvas canvas)
    {
        // The base, seen from above: a ring, because a pot broken off at the foot leaves one.
        var basePoint = new Vector2(52f, 58f);
        Fill(canvas, p => Subtract(
                Circle(p, basePoint, 21f),
                Circle(p, basePoint, 13f)),
            Wash(ClayColor, 0xE1u), 0xE1u);

        Fill(canvas, p => Circle(p, basePoint, 13f), Wash(ClayInner, 0xE2u, 0.05f), 0xE2u);

        // Four shards thrown clear, each an arc — a slice of a ring rather than a lump.
        Shard(canvas, new Vector2(92f, 84f), 26f, 8f, 35f, 0xE3u);
        Shard(canvas, new Vector2(30f, 96f), 20f, 7f, -60f, 0xE4u);
        Shard(canvas, new Vector2(96f, 34f), 17f, 6f, 140f, 0xE5u);
        Shard(canvas, new Vector2(64f, 20f), 14f, 6f, 20f, 0xE6u);
    }

    /// <summary>One shard: a slice of a ring, so it keeps the curve of the pot it came off.</summary>
    private static void Shard(ArtCanvas canvas, Vector2 centre, float radius, float thickness,
        float degrees, uint salt)
    {
        Fill(canvas, p =>
        {
            // The ring itself, then cut down to an arc by a half-plane through its centre.
            float ring = Mathf.Abs(Circle(p, centre, radius)) - thickness * 0.5f;

            Vector2 local = Rotate(p, centre, -degrees) - centre;
            float wedge = Mathf.Max(-local.y, Mathf.Abs(local.x) - radius * 0.85f);
            return Intersect(ring, wedge);
        }, Wash(ClayColor, salt), salt);
    }

    /// <summary>
    /// A length of chain, dropped in a lazy curve. Drawn link by link along the curve rather
    /// than as one strip, because the gaps between links are the only thing that makes it a
    /// chain and not a rope.
    /// </summary>
    private static void DrawChain(ArtCanvas canvas)
    {
        const int links = 8;
        for (int i = 0; i < links; i++)
        {
            float t = i / (float)(links - 1);

            // A shallow S laid across the tile, so it reads as dropped rather than laid out.
            var at = new Vector2(
                Mathf.Lerp(24f, 104f, t),
                64f + Mathf.Sin(t * Mathf.PI * 1.5f) * 22f - 4f);

            // Alternate links stand on edge, which is how a chain lying flat actually falls.
            bool edgeOn = i % 2 == 1;
            var radii = edgeOn ? new Vector2(6.5f, 12f) : new Vector2(12.5f, 7.5f);
            float lean = Mathf.Lerp(-28f, 24f, t);

            uint salt = 0xF0u + (uint)i;
            Fill(canvas, p =>
            {
                Vector2 local = Rotate(p, at, -lean);
                float outer = Ellipse(local, at, radii);
                float inner = Ellipse(local, at, radii - new Vector2(3.6f, 3.6f));
                return Subtract(outer, inner);
            }, (point, distance) =>
            {
                Color color = Wash(ChainColor, salt, 0.07f)(point, distance);
                float rust = Mathf.SmoothStep(-0.1f, 0.1f, Noise(point, 0.05f, 0xF00Du));
                return Color.Lerp(color, ChainRust, rust * 0.6f);
            }, salt);
        }
    }

    /// <summary>
    /// A dead rat, lying on its side: the flank turned up, all four legs out the same way
    /// and stiff, the tail trailing behind it. The tail is the whole silhouette — without
    /// it the body is an anonymous lump, and with it the shape is unmistakable at any size.
    ///
    /// The pose is what says dead, and it has to, because the dungeon now also has a live
    /// rat walking about (<see cref="WanderingRat"/>, drawn by <c>RatSetup</c>). That one
    /// is seen from above with its legs under it; this one could not stand up if it wanted
    /// to, and at a glance across a lit room that difference is the only one available.
    /// </summary>
    private static void DrawRat(ArtCanvas canvas)
    {
        var body = new Vector2(58f, 74f);
        var head = new Vector2(90f, 68f);

        // The tail first, so the body sits on top of where it joins. Slack rather than
        // curled: nothing is holding it.
        Fill(canvas, p => Union(
                Segment(p, new Vector2(34f, 78f), new Vector2(16f, 84f), 3.4f),
                Segment(p, new Vector2(16f, 84f), new Vector2(8f, 100f), 3f)),
            Wash(TailColor, 0xA1u, 0.05f), 0xA1u);

        // The legs: all four out of the belly side, stiff, and *parallel* rather than
        // splayed. Parallel is the whole of it — legs fanned out under a body read as an
        // animal standing on them however dead the rest of the drawing is, and no living
        // rat holds all four in one direction. Drawn before the body so they come out from
        // under it.
        var thigh = new Vector2(-3f, -15f);
        var curl = new Vector2(-9f, -7f);
        foreach (var hip in new[]
                 {
                     new Vector2(42f, 66f), new Vector2(52f, 64f),
                     new Vector2(68f, 64f), new Vector2(78f, 66f)
                 })
        {
            // Bent at the joint and curled at the toes, which is the second half of the
            // pose: a straight leg is a leg being held out, and nothing here is holding
            // anything.
            Vector2 knee = hip + thigh;
            Vector2 foot = knee + curl;
            Fill(canvas, p => Union(
                    Union(Segment(p, hip, knee, 3f), Segment(p, knee, foot, 2.6f)),
                    Ellipse(p, foot, new Vector2(4f, 3f))),
                Wash(TailColor, 0xA2u, 0.05f), 0xA2u);
        }

        Fill(canvas, p => SmoothUnion(
                Ellipse(p, body, new Vector2(26f, 16f)),
                Ellipse(p, head, new Vector2(13f, 11f)),
                9f),
            Wash(FurColor, 0xA3u), 0xA3u);

        // One ear and the snout. One, not two: the other ear is under the head, which is
        // the whole point of drawing the animal on its side.
        Fill(canvas, p => Circle(p, new Vector2(88f, 79f), 7f), Wash(TailColor, 0xA4u, 0.05f), 0xA4u);
        Fill(canvas, p => Circle(p, new Vector2(104f, 63f), 4.5f), Wash(TailColor, 0xA5u, 0.05f), 0xA5u);
    }
}
