using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates and refreshes <c>Assets/Resources/SoundBank.asset</c>, back-filling an entry for
/// every id declared on <see cref="SoundId"/>.
///
/// Ids are found by reflection over <see cref="SoundId"/>'s public string constants rather
/// than listed here a second time — a hand-maintained copy would drift the first time
/// someone adds a sound, and the whole point of the constants is that they are the one
/// list.
///
/// Additive and non-destructive: existing entries keep their clips and tuning, only
/// genuinely new ids are appended. That makes this safe to re-run every time a sound is
/// added, which is exactly when it is needed.
/// </summary>
public static class SoundBankSetup
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = ResourcesFolder + "/" + SoundBank.ResourcesPath + ".asset";

    /// <summary>Ids whose sound is player-centric, so direction carries no information.</summary>
    private static readonly HashSet<string> NonPositionalIds = new HashSet<string>
    {
        SoundId.PlayerHurt,
        SoundId.PlayerDeath,
    };

    /// <summary>
    /// Ids that fire on a tight cadence and need a retrigger guard. Footsteps run every
    /// ~0.2 s from PlayerNoiseEmitter; the rest is protection against a hook site
    /// accidentally firing per-frame.
    /// </summary>
    private static readonly Dictionary<string, float> Cooldowns = new Dictionary<string, float>
    {
        { SoundId.PlayerFootstepWalk, 0.15f },
        { SoundId.PlayerFootstepSprint, 0.12f },
        { SoundId.PlayerFootstepSneak, 0.2f },
        { SoundId.DoorHit, 0.1f },
        { SoundId.EnemyHurt, 0.1f },
    };

    [MenuItem("Tools/Audio/Build Sound Bank")]
    public static void Build()
    {
        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(AssetPath);
        bool created = false;

        if (bank == null)
        {
            Directory.CreateDirectory(ResourcesFolder);
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, AssetPath);
            created = true;
        }

        var serialized = new SerializedObject(bank);
        SerializedProperty entries = serialized.FindProperty("entries");

        var existing = new HashSet<string>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            string id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
            if (!string.IsNullOrEmpty(id)) existing.Add(id);
        }

        int added = 0;
        foreach (string id in DeclaredSoundIds())
        {
            if (existing.Contains(id)) continue;

            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);

            entry.FindPropertyRelative("id").stringValue = id;
            // InsertArrayElementAtIndex copies the previous element, so every field has to
            // be written explicitly — an inherited clips array would silently alias the
            // neighbour's clips.
            entry.FindPropertyRelative("clips").arraySize = 0;
            entry.FindPropertyRelative("volume").floatValue = 1f;
            entry.FindPropertyRelative("pitchMin").floatValue = 0.95f;
            entry.FindPropertyRelative("pitchMax").floatValue = 1.05f;
            entry.FindPropertyRelative("spatialBlend").floatValue = NonPositionalIds.Contains(id) ? 0f : 1f;
            entry.FindPropertyRelative("minDistance").floatValue = 3f;
            entry.FindPropertyRelative("maxDistance").floatValue = 25f;
            entry.FindPropertyRelative("cooldown").floatValue =
                Cooldowns.TryGetValue(id, out float cooldown) ? cooldown : 0f;

            added++;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();

        string summary = created
            ? $"Created '{AssetPath}' with {added} sound entries."
            : $"Refreshed '{AssetPath}': {added} new entr{(added == 1 ? "y" : "ies")} added, " +
              $"{existing.Count} left untouched.";

        Debug.Log($"[Sound Bank] {summary}");
        EditorUtility.DisplayDialog("Sound Bank",
            summary + "\n\n" +
            "Entries with no clips log to the console instead of playing, so every hook " +
            "can be verified before any audio exists. Drop clips onto an entry to make it " +
            "audible — no code change needed.", "OK");

        Selection.activeObject = bank;
    }

    /// <summary>Every public string constant declared on <see cref="SoundId"/>.</summary>
    private static IEnumerable<string> DeclaredSoundIds()
    {
        return typeof(SoundId)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue())
            .Where(id => !string.IsNullOrEmpty(id));
    }
}
