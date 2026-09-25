using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool that populates an <see cref="EnemySpriteAnimator"/> with all SkullGuy clips,
/// loading frames straight from the optimized folders so hundreds of sprites don't have to be
/// assigned by hand. Sets loop flags, frame chaining (walk-start -> loop, walk-end -> idle) and
/// a default frame rate.
///
/// Usage: select the GameObject that has the EnemySpriteAnimator, then
/// Tools > SkullGuy > Load Frames Into Selected.
/// </summary>
public static class SkullGuyFrameLoader
{
    private const string Root = "Assets/Art/SKULL-GUY-optimized";
    private const float DefaultFps = 24f;

    private struct ClipDef
    {
        public string folder;
        public string clipName;
        public bool loop;
        public string next;

        public ClipDef(string folder, string clipName, bool loop, string next)
        {
            this.folder = folder;
            this.clipName = clipName;
            this.loop = loop;
            this.next = next;
        }
    }

    private static readonly ClipDef[] Defs =
    {
        new ClipDef("SKULL-GUY_SPOCZYNEK",           "SPOCZYNEK",     true,  ""),
        new ClipDef("SKULL-GUY_CHOD_LOOP",           "CHOD_LOOP",     true,  ""),
        new ClipDef("SKULL-GUY_CHOD_LOOP_POCZATEK",  "CHOD_POCZATEK", false, "CHOD_LOOP"),
        new ClipDef("SKULL-GUY_CHOD_LOOP_KONIEC",    "CHOD_KONIEC",   false, "SPOCZYNEK"),
        new ClipDef("SKULL-GUY_RYK",                 "RYK",           false, ""),
        new ClipDef("SKULL-GUY_ATAK",                "ATAK",          false, ""),
        new ClipDef("SKULL-GUY_ROZGLADANIE",         "ROZGLADANIE",   true,  ""),
    };

    [MenuItem("Tools/SkullGuy/Load Frames Into Selected")]
    private static void LoadFramesMenu()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("SkullGuy", "Select a GameObject with an EnemySpriteAnimator first.", "OK");
            return;
        }

        var animator = go.GetComponent<EnemySpriteAnimator>();
        if (animator == null)
        {
            EditorUtility.DisplayDialog("SkullGuy", "The selected GameObject has no EnemySpriteAnimator component.", "OK");
            return;
        }

        int clips = LoadInto(animator, out string report, out int totalFrames);
        Debug.Log($"[SkullGuy] Loaded {clips} clips, {totalFrames} frames:\n" + report);
        EditorUtility.DisplayDialog("SkullGuy", $"Loaded {clips} clips ({totalFrames} frames).\n\n" + report, "OK");
    }

    /// <summary>
    /// Populates the given animator's clips from the optimized folders. Returns the clip count
    /// and, via out params, a human-readable report and the total frame count.
    /// </summary>
    public static int LoadInto(EnemySpriteAnimator animator, out string reportText, out int totalFrames)
    {
        var so = new SerializedObject(animator);
        SerializedProperty clipsProp = so.FindProperty("clips");
        clipsProp.ClearArray();

        int clipIndex = 0;
        totalFrames = 0;
        var report = new List<string>();

        foreach (ClipDef def in Defs)
        {
            Sprite[] frames = LoadSprites($"{Root}/{def.folder}");
            if (frames.Length == 0)
            {
                report.Add($"{def.clipName}: 0 frames (folder missing?)");
                continue;
            }

            clipsProp.InsertArrayElementAtIndex(clipIndex);
            SerializedProperty clip = clipsProp.GetArrayElementAtIndex(clipIndex);
            clip.FindPropertyRelative("name").stringValue = def.clipName;
            clip.FindPropertyRelative("fps").floatValue = DefaultFps;
            clip.FindPropertyRelative("loop").boolValue = def.loop;
            clip.FindPropertyRelative("nextClip").stringValue = def.next;

            SerializedProperty framesProp = clip.FindPropertyRelative("frames");
            framesProp.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++)
                framesProp.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

            clipIndex++;
            totalFrames += frames.Length;
            report.Add($"{def.clipName}: {frames.Length} frames");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(animator);

        reportText = string.Join("\n", report);
        return clipIndex;
    }

    /// <summary>Returns the first sprite of the idle clip, for previewing in edit mode.</summary>
    public static Sprite GetIdleFirstFrame()
    {
        Sprite[] frames = LoadSprites($"{Root}/SKULL-GUY_SPOCZYNEK");
        return frames.Length > 0 ? frames[0] : null;
    }

    private static Sprite[] LoadSprites(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return new Sprite[0];

        // Sort by path so zero-padded frame names come out in order.
        return AssetDatabase.FindAssets("t:Sprite", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(s => s != null)
            .ToArray();
    }
}
