using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static ProceduralArt;

/// <summary>
/// Draws the health and stamina bars and wires them into the HUD canvas.
///
/// They were three flat rectangles: a white panel at 39% alpha, a grey track and a coloured
/// fill, with no sprite on any of them. That reads as a placeholder debug overlay, and it is
/// the one piece of UI on screen for the whole game.
///
/// Drawn in the same hand-inked idiom as the item icons and the project's furniture — a
/// wrought iron frame, a groove sunk into it, and the fill sitting in the groove like
/// something poured there. Every piece is nine-sliced, so one small sprite stretches to any
/// bar size without the ink line stretching with it: the corners stay the weight they were
/// drawn at, which is the entire reason a border like this has to be sliced rather than
/// scaled.
///
/// <b>Nothing is deleted.</b> The images keep their objects and their layout; this gives
/// them sprites and sets their tint to white so the art shows its own colour rather than
/// being multiplied by the old flat one.
/// </summary>
public static class BarArtGenerator
{
    private const string BarFolder = "Assets/Generation/UI";
    private const string CanvasPrefabPath = "Assets/Prefabs/UI/Canvas.prefab";

    // Small, because it is stretched: everything that has to stay crisp lives in the border,
    // and the middle is one column of pixels repeated across the bar.
    private const int BarWidth = 64;
    private const int BarHeight = 32;

    /// <summary>
    /// Nine-slice border in pixels, as left/bottom/right/top. Wide enough to hold the ink
    /// line and the bevel inside it, which is what must not stretch.
    /// </summary>
    private static readonly Vector4 FrameBorder = new Vector4(16f, 13f, 16f, 13f);
    private static readonly Vector4 TrackBorder = new Vector4(11f, 9f, 11f, 9f);
    private static readonly Vector4 FillBorder = new Vector4(8f, 7f, 8f, 7f);

    private static readonly Color Ink = new Color32(0x1B, 0x17, 0x13, 0xFF);
    private const float InkMin = 2.2f;
    private const float InkMax = 4f;

    private static readonly Color FrameIron = new Color32(0x63, 0x5C, 0x50, 0xFF);
    private static readonly Color GrooveColor = new Color32(0x22, 0x1F, 0x1B, 0xFF);

    // Dried blood rather than a warning red. The bar has to be legible at a glance without
    // being the brightest thing in a game whose whole palette is dust and stone.
    private static readonly Color HealthColor = new Color32(0x8E, 0x2F, 0x2A, 0xFF);

    // Tarnished brass, which reads as effort rather than as a second health bar.
    private static readonly Color StaminaColor = new Color32(0xA1, 0x8A, 0x4E, 0xFF);

    /// <summary>
    /// Draws the three pieces, imports them and assigns them to the HUD canvas. Idempotent
    /// and safe to re-run.
    /// </summary>
    [MenuItem("Tools/Art/Regenerate Bar Art")]
    public static void RegenerateBars()
    {
        Sprite frame = WriteSprite(BuildFrame(), $"{BarFolder}/BarFrame.png", 100f, FrameBorder);
        Sprite track = WriteSprite(BuildTrack(), $"{BarFolder}/BarTrack.png", 100f, TrackBorder);
        Sprite health = WriteSprite(BuildFill(HealthColor, 0xB1u), $"{BarFolder}/BarFillHealth.png", 100f, FillBorder);
        Sprite stamina = WriteSprite(BuildFill(StaminaColor, 0xB2u), $"{BarFolder}/BarFillStamina.png", 100f, FillBorder);

        int wired = Wire(frame, track, health, stamina);

        AssetDatabase.SaveAssets();

        const int expected = 6;
        if (wired == expected)
        {
            Debug.Log($"[BarArt] Redrew four bar sprites in '{BarFolder}' and wired all " +
                      $"{expected} images on the HUD canvas.");
        }
        else
        {
            Debug.LogError($"[BarArt] Drew the sprites but only wired {wired} of {expected} " +
                           "images — the bars will look unchanged in game. See the warnings above.");
        }
    }

    /// <summary>
    /// Points the canvas's bar images at the sprites.
    ///
    /// Each one's tint goes to white at the same time. The images carried their colour as a
    /// flat tint because they had no sprite; leaving it would multiply the art by it, and a
    /// red fill sprite tinted red comes out nearly black.
    /// </summary>
    private static int Wire(Sprite frame, Sprite track, Sprite health, Sprite stamina)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath) == null)
        {
            Debug.LogWarning($"[BarArt] No canvas prefab at '{CanvasPrefabPath}' — sprites drawn but not wired.");
            return 0;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(CanvasPrefabPath);
        try
        {
            int wired = 0;
            wired += Apply(contents, "HealthBarPanel", frame);
            wired += Apply(contents, "StaminaBarPanel", frame);
            wired += Apply(contents, "HealthBar", track);
            wired += Apply(contents, "StaminaBar", track);
            wired += Apply(contents, "HealthBar/Fill", health);
            wired += Apply(contents, "StaminaBar/Fill", stamina);

            if (wired > 0) PrefabUtility.SaveAsPrefabAsset(contents, CanvasPrefabPath);
            return wired;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// Finds an image by name, or by "parent/child" when the name alone is ambiguous — both
    /// bars have a child called Fill.
    /// </summary>
    private static int Apply(GameObject root, string path, Sprite sprite)
    {
        // Returning quietly here is what hid the whole failure the first time this ran: the
        // sprites were drawn, the wiring did nothing, and the log still said everything was
        // fine because the count it reports was simply zero.
        if (sprite == null)
        {
            Debug.LogWarning($"[BarArt] No sprite to assign to '{path}' — the drawing was " +
                             "written but never imported. Re-run the tool.");
            return 0;
        }

        string parentName = null;
        string name = path;
        int slash = path.IndexOf('/');
        if (slash >= 0)
        {
            parentName = path.Substring(0, slash);
            name = path.Substring(slash + 1);
        }

        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.name != name) continue;
            if (parentName != null && (image.transform.parent == null ||
                                       image.transform.parent.name != parentName)) continue;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            // The canvas holds twenty-six nested prefab instances. A component that belongs
            // to one of them keeps its values in the *instance's* override list, not in this
            // prefab's own serialized data, so assigning the field and saving is not enough
            // — the assignment has to be recorded as an override or it is dropped on save.
            // Harmless on the objects that are not nested, which is why it is unconditional
            // rather than guarded by a check that would have to be right.
            if (PrefabUtility.IsPartOfPrefabInstance(image))
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);

            EditorUtility.SetDirty(image);
            return 1;
        }

        // Name the images that *are* there. A silent "not found" on a prefab with thirty
        // Images is a dead end; the list says immediately whether the object was renamed or
        // whether the search is looking at the wrong prefab entirely.
        var found = new System.Collections.Generic.List<string>();
        foreach (Image image in root.GetComponentsInChildren<Image>(true)) found.Add(image.name);

        Debug.LogWarning($"[BarArt] No image at '{path}' on the canvas — nothing assigned. " +
                         $"Images present: {string.Join(", ", found)}");
        return 0;
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>
    /// The outer frame: a band of iron with the middle cut out, so the track shows through
    /// it. The hole is what makes this work nine-sliced — the stretched centre is
    /// transparent, so no amount of stretching touches the drawn border.
    /// </summary>
    private static ArtCanvas BuildFrame()
    {
        var canvas = new ArtCanvas(BarWidth, BarHeight);
        var centre = new Vector2(BarWidth * 0.5f, BarHeight * 0.5f);

        Render(canvas, point =>
        {
            float outer = RoundedBox(point, centre, new Vector2(BarWidth * 0.5f - 1.5f, BarHeight * 0.5f - 1.5f), 6f);
            float inner = RoundedBox(point, centre, new Vector2(BarWidth * 0.5f - 12f, BarHeight * 0.5f - 10f), 4f);
            return Subtract(outer, inner);
        }, (point, distance) =>
        {
            Color color = Shade(FrameIron, Noise(point, 0.09f, 0xC1u, 2) * 0.14f);

            // Pitting, in patches. Wrought iron in a wet cellar is not a smooth surface, and
            // an even fill here would read as a UI panel rather than as a thing.
            float pit = Mathf.SmoothStep(-0.05f, 0.15f, Noise(point, 0.18f, 0xC2u));
            return Shade(color, -pit * 0.12f);
        }, 0xC3u);

        return canvas;
    }

    /// <summary>The groove the fill sits in: near-black, with the ink line round it.</summary>
    private static ArtCanvas BuildTrack()
    {
        var canvas = new ArtCanvas(BarWidth, BarHeight);
        var centre = new Vector2(BarWidth * 0.5f, BarHeight * 0.5f);

        Render(canvas,
            point => RoundedBox(point, centre, new Vector2(BarWidth * 0.5f - 3f, BarHeight * 0.5f - 3f), 4f),
            (point, distance) =>
            {
                Color color = Shade(GrooveColor, Noise(point, 0.12f, 0xD1u, 2) * 0.06f);

                // Darker along the top inside edge, so the groove reads as sunk into the
                // frame rather than laid on it. The one place a gradient earns its keep:
                // a recess has no silhouette of its own to say it is a recess.
                float fromTop = Mathf.Clamp01((point.y - (BarHeight - 11f)) / 8f);
                return Shade(color, -fromTop * 0.05f);
            }, 0xD2u);

        return canvas;
    }

    /// <summary>
    /// The fill. Flat colour with a wash over it and a lighter band along the top, which is
    /// the whole of the modelling — enough to keep it from looking like a coloured rectangle,
    /// not enough to look lit.
    /// </summary>
    private static ArtCanvas BuildFill(Color baseColor, uint salt)
    {
        var canvas = new ArtCanvas(BarWidth, BarHeight);
        var centre = new Vector2(BarWidth * 0.5f, BarHeight * 0.5f);

        Render(canvas,
            point => RoundedBox(point, centre, new Vector2(BarWidth * 0.5f - 5f, BarHeight * 0.5f - 5f), 3f),
            (point, distance) =>
            {
                Color color = Shade(baseColor, Noise(point, 0.1f, salt, 2) * 0.1f);

                float fromTop = Mathf.Clamp01((point.y - BarHeight * 0.55f) / 6f);
                color = Shade(color, fromTop * 0.06f);

                float fromBottom = Mathf.Clamp01((BarHeight * 0.4f - point.y) / 7f);
                return Shade(color, -fromBottom * 0.07f);
            }, salt ^ 0xFFu);

        return canvas;
    }

    /// <summary>
    /// Renders one shape with the inked edge, the same way <c>ItemIconGenerator</c> does.
    /// A thinner line than the icons use, because these are drawn at 32 pixels and stretched
    /// rather than at 128 and shown whole.
    /// </summary>
    private static void Render(ArtCanvas canvas, System.Func<Vector2, float> field,
        System.Func<Vector2, float, Color> wash, uint salt)
    {
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                float distance = Wobble(field(point), point, 0.7f, 0.11f, salt);

                float coverage = Coverage(distance);
                if (coverage <= 0f) continue;

                Color color = wash(point, distance);
                color = Color.Lerp(color, Ink, EdgeBand(distance, InkWidth(point, InkMin, InkMax, salt, 0.1f)));
                canvas.Blend(x, y, color, coverage);
            }
        }
    }
}
