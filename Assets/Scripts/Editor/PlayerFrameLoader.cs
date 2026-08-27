using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool that populates the player's two <see cref="SpriteFrameAnimator"/>s (torso and
/// legs) straight from the folders under Assets/Art/PLAYER, so hundreds of sprites don't have
/// to be assigned by hand. The counterpart of <see cref="SkullGuyFrameLoader"/>.
///
/// Folder names are Polish: NOGI = legs, TLOW = torso ("tułów"), CHODZENIE = walking,
/// BIEG = running, BRON = weapon (the pistol), STRZELBA = shotgun, CELOWANIE = aiming,
/// NAPIECIE = wind-up (the axe's equivalent of aiming), STRZAL = shot/swing. CHEDZENIE_TLOW is
/// a typo for CHODZENIE_TLOW and is mapped to the clean clip name here rather than renamed on
/// disk, so the art folder stays exactly as the artist exported it.
///
/// Usage: Tools > Player > Setup Animations with the Player root selected.
/// </summary>
public static class PlayerFrameLoader
{
    private const string Root = "Assets/Art/PLAYER";

    private struct ClipDef
    {
        public string folder;
        public string clipName;
        public float fps;
        public bool loop;

        public ClipDef(string folder, string clipName, float fps, bool loop)
        {
            this.folder = folder;
            this.clipName = clipName;
            this.fps = fps;
            this.loop = loop;
        }
    }

    // Legs cycles are exported at 50 frames and torso walk cycles at 25, so the torso runs at
    // half the fps to keep one stride the same length on both halves (1 second per cycle at
    // multiplier 1; PlayerAnimationDriver scales that with actual movement speed).
    private static readonly ClipDef[] LegsDefs =
    {
        new ClipDef("CHODZENIE_NOGI", "CHODZENIE_NOGI", 50f, true),
        new ClipDef("BIEG_NOGI",      "BIEG_NOGI",      50f, true),
    };

    // One carry / aim / attack triple per weapon, plus the two unarmed cycles. Everything runs
    // at the torso's 25 fps so an armed walk keeps step with the legs; the attack clips are
    // shorter (13 and 10 frames against the pistol's 25), which is what makes the axe swing and
    // the shotgun blast snappier than the pistol shot rather than any fps difference.
    private static readonly ClipDef[] TorsoDefs =
    {
        new ClipDef("CHEDZENIE_TLOW",                    "CHODZENIE_TLOW",                    25f, true),
        new ClipDef("BIEG_TLOW",                         "BIEG_TLOW",                         50f, true),
        new ClipDef("CHODZENIE_TLOW_BRON",               "CHODZENIE_TLOW_BRON",               25f, true),
        new ClipDef("CHODZENIE_TLOW_BRON_CELOWANIE",     "CHODZENIE_TLOW_BRON_CELOWANIE",     25f, true),
        new ClipDef("CHODZENIE_TLOW_STRZAL",             "CHODZENIE_TLOW_STRZAL",             25f, false),
        new ClipDef("CHODZENIE_TLOW_STRZELBA",           "CHODZENIE_TLOW_STRZELBA",           25f, true),
        new ClipDef("CHODZENIE_TLOW_STRZELBA_CELOWANIE", "CHODZENIE_TLOW_STRZELBA_CELOWANIE", 25f, true),
        new ClipDef("CHODZENIE_TLOW_STRZELBA_STRZAL",    "CHODZENIE_TLOW_STRZELBA_STRZAL",    25f, false),
        new ClipDef("CHODZENIE_TLOW_AXE",                "CHODZENIE_TLOW_AXE",                25f, true),
        // The wind-up is a single pull-back, not a cycle. PlayerAnimationDriver scrubs it by
        // charge progress rather than playing it, so its fps only matters as a fallback.
        new ClipDef("CHODZENIE_TLOW_AXE_NAPIECIE",       "CHODZENIE_TLOW_AXE_NAPIECIE",       25f, false),
        new ClipDef("CHODZENIE_TLOW_AXE_STRZAL",         "CHODZENIE_TLOW_AXE_STRZAL",         25f, false),
    };

    [MenuItem("Tools/Player/Reimport Frames")]
    private static void ReimportFrames()
    {
        AssetDatabase.ImportAsset(PlayerFrameImporter.TargetFolder,
            ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Player] Reimported {PlayerFrameImporter.TargetFolder} with PlayerFrameImporter settings.");
    }

    [MenuItem("Tools/Player/Load Frames Into Selected")]
    private static void LoadFramesMenu()
    {
        GameObject go = Selection.activeGameObject;
        var animator = go != null ? go.GetComponent<SpriteFrameAnimator>() : null;
        if (animator == null)
        {
            EditorUtility.DisplayDialog("Player", "Select the Torso or Legs GameObject (it needs a SpriteFrameAnimator).", "OK");
            return;
        }

        bool isLegs = go.name.ToLowerInvariant().Contains("leg") || go.name.ToLowerInvariant().Contains("nogi");
        int clips = LoadInto(animator, isLegs, out string report, out int frames);
        Report(isLegs ? "Legs" : "Torso", clips, frames, report);
    }

    /// <summary>
    /// Populates one animator with either the legs or the torso clip set. Returns the clip
    /// count and, via out params, a human-readable report and the total frame count.
    /// </summary>
    public static int LoadInto(SpriteFrameAnimator animator, bool legs, out string reportText, out int totalFrames)
    {
        ClipDef[] defs = legs ? LegsDefs : TorsoDefs;

        var so = new SerializedObject(animator);
        SerializedProperty clipsProp = so.FindProperty("clips");
        clipsProp.ClearArray();

        int clipIndex = 0;
        totalFrames = 0;
        var report = new List<string>();

        foreach (ClipDef def in defs)
        {
            Sprite[] frames = LoadSprites($"{Root}/{def.folder}");
            if (frames.Length == 0)
            {
                report.Add($"{def.clipName}: 0 frames (folder missing, or frames still imported as Multiple — run Tools > Player > Reimport Frames)");
                continue;
            }

            clipsProp.InsertArrayElementAtIndex(clipIndex);
            SerializedProperty clip = clipsProp.GetArrayElementAtIndex(clipIndex);
            clip.FindPropertyRelative("name").stringValue = def.clipName;
            clip.FindPropertyRelative("fps").floatValue = def.fps;
            clip.FindPropertyRelative("loop").boolValue = def.loop;
            clip.FindPropertyRelative("nextClip").stringValue = string.Empty;

            SerializedProperty framesProp = clip.FindPropertyRelative("frames");
            framesProp.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++)
                framesProp.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

            clipIndex++;
            totalFrames += frames.Length;
            report.Add($"{def.clipName}: {frames.Length} frames @ {def.fps} fps");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(animator);

        reportText = string.Join("\n", report);
        return clipIndex;
    }

    /// <summary>First frame of a clip set's default clip, for previewing in edit mode.</summary>
    public static Sprite GetPreviewFrame(bool legs)
    {
        Sprite[] frames = LoadSprites($"{Root}/{(legs ? "CHODZENIE_NOGI" : "CHEDZENIE_TLOW")}");
        return frames.Length > 0 ? frames[0] : null;
    }

    internal static void Report(string what, int clips, int frames, string report)
    {
        Debug.Log($"[Player] {what}: loaded {clips} clips, {frames} frames:\n{report}");
        EditorUtility.DisplayDialog("Player", $"{what}: {clips} clips ({frames} frames).\n\n{report}", "OK");
    }

    private static Sprite[] LoadSprites(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return new Sprite[0];

        // Frames of this clip only, never a neighbour's: several clip folders are prefixes of
        // others (CHODZENIE_TLOW_AXE of CHODZENIE_TLOW_AXE_NAPIECIE, CHODZENIE_TLOW_STRZELBA of
        // both its aim and shot folders), so anything that reached in from a sibling would
        // silently double a walk cycle's length.
        // Sort by path so zero-padded frame names come out in order.
        return AssetDatabase.FindAssets("t:Sprite", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.StartsWith(folder + "/", System.StringComparison.Ordinal) &&
                        p.IndexOf('/', folder.Length + 1) < 0)
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(s => s != null)
            .ToArray();
    }
}
