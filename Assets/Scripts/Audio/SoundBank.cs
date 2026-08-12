using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps <see cref="SoundId"/> strings to clips and playback settings — the audio-side
/// counterpart of <see cref="PrefabRegistry"/>, and loaded the same way (from
/// <c>Assets/Resources</c>, no scene wiring).
///
/// An entry with no clips is not an error: while the project has no audio assets, that is
/// every entry, and <see cref="AudioService"/> logs the sound to the console instead of
/// playing it. Dropping real clips onto an entry later switches it from logging to audible
/// with no code change anywhere (AUDIO_NOTES.md D2).
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "Audio/Sound Bank")]
public class SoundBank : ScriptableObject
{
    /// <summary>Path under Resources the runtime instance is loaded from.</summary>
    public const string ResourcesPath = "SoundBank";

    /// <summary>Clips and playback settings for one <see cref="SoundId"/>.</summary>
    [Serializable]
    public class SoundEntry
    {
        [Tooltip("Stable id from SoundId, e.g. \"door.open\". Must match exactly.")]
        public string id;

        [Tooltip("Clips for this sound. More than one picks a random clip per play. Empty = log to console instead of playing.")]
        public AudioClip[] clips = Array.Empty<AudioClip>();

        [Range(0f, 1f)]
        [Tooltip("Playback volume.")]
        public float volume = 1f;

        [Range(0.1f, 3f)]
        [Tooltip("Lowest random pitch. Keep a little below Pitch Max so repeated sounds (footsteps) do not sound mechanical.")]
        public float pitchMin = 0.95f;

        [Range(0.1f, 3f)]
        [Tooltip("Highest random pitch.")]
        public float pitchMax = 1.05f;

        [Range(0f, 1f)]
        [Tooltip("0 = 2D (equally loud everywhere), 1 = fully positional. Positional is the default; use 0 for sounds coming from the player's own body or the UI.")]
        public float spatialBlend = 1f;

        [Min(0f)]
        [Tooltip("Distance within which the sound is at full volume.")]
        public float minDistance = 3f;

        [Min(0f)]
        [Tooltip("Distance beyond which the sound is inaudible.")]
        public float maxDistance = 25f;

        [Min(0f)]
        [Tooltip("Minimum seconds between two plays of this id. Guards against a hook site firing every frame; 0 = no limit.")]
        public float cooldown = 0f;
    }

    [SerializeField] private List<SoundEntry> entries = new List<SoundEntry>();

    private Dictionary<string, SoundEntry> _byId;
    private static SoundBank _instance;

    /// <summary>
    /// The project-wide bank from Resources, or null when the asset does not exist.
    /// Null is a supported state: <see cref="AudioService"/> falls back to logging every
    /// sound, which is exactly the behaviour wanted before any clips are authored. It
    /// warns once rather than per call, since that path fires many times a second.
    /// </summary>
    public static SoundBank Instance
    {
        get
        {
            if (_instance == null && !_warnedMissing)
            {
                _instance = Resources.Load<SoundBank>(ResourcesPath);
                if (_instance == null)
                {
                    _warnedMissing = true;
                    Debug.LogWarning(
                        $"[SoundBank] No '{ResourcesPath}' asset in Resources — every sound will " +
                        "be logged instead of played. Run Tools ▸ Audio ▸ Build Sound Bank.");
                }
            }
            return _instance;
        }
    }

    private static bool _warnedMissing;

    /// <summary>All configured entries, for the editor tool.</summary>
    public IReadOnlyList<SoundEntry> Entries => entries;

    /// <summary>Returns the entry for an id, or null when the bank does not define it.</summary>
    public SoundEntry Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) BuildLookup();
        return _byId.TryGetValue(id, out var entry) ? entry : null;
    }

    private void OnEnable()
    {
        _byId = null; // the list may have been edited; relearn lazily
    }

    private void BuildLookup()
    {
        _byId = new Dictionary<string, SoundEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
            if (!_byId.TryAdd(entry.id, entry))
                Debug.LogError($"[SoundBank] Duplicate sound id '{entry.id}' — the later entry is ignored.", this);
        }
    }

    /// <summary>
    /// Clears the cached singleton so a domain reload with reloading disabled does not
    /// keep a bank (or a stale "already warned" flag) from the previous play session.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _warnedMissing = false;
    }
}
