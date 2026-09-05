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
        SoundId.PlayerExhausted,
    };

    /// <summary>
    /// Ids that belong to the interface rather than the world, so they route to the UI
    /// mixer group and are never positional — a click has no place in the dungeon to come
    /// from. Listed explicitly rather than matched on the <c>"ui."</c> prefix, to keep the
    /// id strings free of meaning the rest of the system would have to agree on.
    /// </summary>
    private static readonly HashSet<string> UiIds = new HashSet<string>
    {
        SoundId.UiClick,
        SoundId.BackpackOpen,
        SoundId.BackpackClose,
    };

    /// <summary>
    /// Ids that are music rather than sound effects: routed to the Music group and never
    /// positional. Kept separate from <see cref="UiIds"/> because the two differ in more
    /// than the group they land on — a UI sound is a one-shot, these loop and are started
    /// and stopped by name (<c>AudioService.PlayMusic</c>).
    /// </summary>
    private static readonly HashSet<string> MusicIds = new HashSet<string>
    {
        SoundId.MusicMenu,
        SoundId.MusicDungeon,
    };

    /// <summary>
    /// Ids that fire on a tight cadence and need a retrigger guard. These are a floor, not
    /// a rhythm — the footstep rhythm is <c>PlayerNoiseEmitter</c>'s per-mode step
    /// intervals, and each of these sits well under the matching one so the guard never
    /// becomes the thing setting the pace. The rest is protection against a hook site
    /// accidentally firing per-frame.
    /// </summary>
    private static readonly Dictionary<string, float> Cooldowns = new Dictionary<string, float>
    {
        { SoundId.PlayerFootstepWalk, 0.15f },
        { SoundId.PlayerFootstepSprint, 0.12f },
        { SoundId.PlayerFootstepSneak, 0.2f },
        { SoundId.DoorHit, 0.1f },
        { SoundId.EnemyHurt, 0.1f },

        // Same guard as EnemyHurt, for the opposite reason. There is only one player, but
        // several enemies can land on them at once, and this id is non-positional — two
        // simultaneous hits would be one clip doubled on itself rather than two voices from
        // two places. Far under EnemyMeleeAttack's 1.5 s per-enemy cooldown, so genuine
        // repeat hits still sound.
        { SoundId.PlayerHurt, 0.1f },

        // Each enemy paces its own moan (EnemyBase.idleSoundInterval*), but the guard here is
        // global — AudioService keys cooldowns on the id, not on the emitter. That is the
        // useful shape for once: it stops two enemies whose independent timers happen to
        // coincide from firing as one doubled voice, while sitting far enough under the 4-6 s
        // interval that it never becomes the thing setting the pace.
        { SoundId.EnemyIdle, 0.5f },

        // A pointer can only press once per frame, but mouse and touchscreen are polled
        // separately and a rapid double-click should still sound twice — short enough to
        // be inaudible as a limit, long enough to swallow a doubled press.
        { SoundId.UiClick, 0.05f },

        // Tab is a keyboard toggle with no animation to sit behind, so it can be flipped
        // faster than the clip is long. Well under a deliberate open-close, long enough that
        // holding the key down cannot stack the sound on itself.
        { SoundId.BackpackOpen, 0.1f },
        { SoundId.BackpackClose, 0.1f },
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
        int rerouted = 0;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            string id = entry.FindPropertyRelative("id").stringValue;
            if (string.IsNullOrEmpty(id)) continue;

            existing.Add(id);

            // Back-fills the channel on entries that predate the field. Sfx is 0, which is
            // also what a missing field deserializes to, so a UI id sitting on Sfx is
            // indistinguishable from one that was never assigned — and there is no reason
            // anyone would deliberately route a UI sound into the world bus. Anything
            // already pointing somewhere else is left alone.
            SerializedProperty channel = entry.FindPropertyRelative("channel");
            if (UiIds.Contains(id) && channel.intValue == (int)AudioChannel.Sfx)
            {
                channel.intValue = (int)AudioChannel.Ui;
                rerouted++;
            }
        }

        int added = 0;
        foreach (string id in DeclaredSoundIds())
        {
            if (existing.Contains(id)) continue;

            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);

            bool isUi = UiIds.Contains(id);
            bool isMusic = MusicIds.Contains(id);

            entry.FindPropertyRelative("id").stringValue = id;
            // InsertArrayElementAtIndex copies the previous element, so every field has to
            // be written explicitly — an inherited clips array would silently alias the
            // neighbour's clips.
            entry.FindPropertyRelative("clips").arraySize = 0;
            // intValue rather than enumValueIndex: the index is the position in the enum's
            // declaration, and AudioChannel's numbers are assigned explicitly precisely
            // because they have to stay stable. The value is what is serialized.
            entry.FindPropertyRelative("channel").intValue =
                (int)(isMusic ? AudioChannel.Music : isUi ? AudioChannel.Ui : AudioChannel.Sfx);
            entry.FindPropertyRelative("volume").floatValue = 1f;
            entry.FindPropertyRelative("pitchMin").floatValue = 0.95f;
            entry.FindPropertyRelative("pitchMax").floatValue = 1.05f;
            // UI sound never comes from a place, so it is folded in here rather than
            // repeated in NonPositionalIds.
            entry.FindPropertyRelative("spatialBlend").floatValue =
                isUi || isMusic || NonPositionalIds.Contains(id) ? 0f : 1f;
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

        if (rerouted > 0)
            summary += $"\n{rerouted} entr{(rerouted == 1 ? "y" : "ies")} moved onto the UI mixer group.";

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
