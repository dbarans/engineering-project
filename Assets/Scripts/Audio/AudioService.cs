using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The only audio API hook sites touch. Call sites name a <see cref="SoundId"/>; everything
/// else — clips, volume, pitch jitter, 2D/3D, pooling — is configuration in
/// <see cref="SoundBank"/>.
///
/// <b>Currently silent by design.</b> The project has no audio assets, so every entry has
/// no clips and each call logs <c>[Audio] ♪ id</c> instead of playing. That is what makes
/// it possible to wire and review every hook before a single <c>.wav</c> exists: run the
/// game, watch the console, see the sound design happen in text. Assigning clips to an
/// entry switches that id from logging to audible with no code change (AUDIO_NOTES.md D2).
/// </summary>
public static class AudioService
{
    /// <summary>Last play time per id, for <see cref="SoundBank.SoundEntry.cooldown"/>.</summary>
    private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();

    /// <summary>
    /// Plays a sound with no position — for things happening to the player themselves,
    /// where direction would be meaningless. Still honours the entry's spatialBlend, so an
    /// entry left at the positional default simply plays at the listener.
    /// </summary>
    public static void Play(string id)
    {
        PlayInternal(id, Vector3.zero, null, positional: false);
    }

    /// <summary>
    /// Plays a sound at a fixed world position. Outlives the object that triggered it, so
    /// it is safe to call immediately before destroying the emitter.
    /// </summary>
    public static void PlayAt(string id, Vector3 position)
    {
        PlayInternal(id, position, null, positional: true);
    }

    /// <summary>
    /// Plays a sound that follows a moving emitter — use when the source travels far enough
    /// during playback for a fixed point to sound wrong. For anything short, prefer
    /// <see cref="PlayAt"/>: it does not care whether the emitter survives.
    /// </summary>
    public static void PlayOn(string id, Transform emitter)
    {
        if (emitter == null) return;
        PlayInternal(id, emitter.position, emitter, positional: true);
    }

    /// <summary>
    /// Starts a looping track and leaves it playing across scene loads until
    /// <see cref="StopMusic"/> is called. Separate from <see cref="Play"/> because music is
    /// the one sound that outlives the moment it started: it needs an owner that can be
    /// stopped, and it must not sit in the one-shot pool where a busy scene would
    /// eventually steal its source (AUDIO_NOTES.md D5).
    ///
    /// Calling it again with the id already playing is a no-op, so a menu scene loaded a
    /// second time picks the track up where it was instead of restarting it.
    /// </summary>
    public static void PlayMusic(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        SoundBank.SoundEntry entry = SoundBank.Instance != null ? SoundBank.Instance.Resolve(id) : null;
        AudioClip clip = PickClip(entry);

        if (clip == null)
        {
            // Same placeholder contract as the one-shots: an entry with no clips proves the
            // hook by logging rather than by playing (D2).
            Debug.Log($"[Audio] ♪ (music) {id}");
            return;
        }

        AudioRuntime runtime = AudioRuntime.Instance;
        if (runtime == null) return;

        runtime.PlayMusic(id, clip, entry);
    }

    /// <summary>Stops the current track. Safe when nothing is playing.</summary>
    public static void StopMusic()
    {
        // Current, not Instance: this is called from OnDestroy as a scene unloads, and
        // Instance would build a runtime just to tell it there is nothing to stop.
        AudioRuntime runtime = AudioRuntime.Current;
        if (runtime != null) runtime.StopMusic();
    }

    private static void PlayInternal(string id, Vector3 position, Transform follow, bool positional)
    {
        if (string.IsNullOrEmpty(id)) return;

        SoundBank.SoundEntry entry = SoundBank.Instance != null ? SoundBank.Instance.Resolve(id) : null;

        if (IsOnCooldown(id, entry)) return;

        AudioClip clip = PickClip(entry);
        if (clip == null)
        {
            // The placeholder path — and the normal one until clips are authored.
            Debug.Log(positional
                ? $"[Audio] ♪ {id} @ {position}"
                : $"[Audio] ♪ {id}");
            return;
        }

        // Explicit == null rather than ?.: Unity's overloaded equality is what recognises a
        // destroyed object, and ?. tests plain reference null, so it would happily call
        // through to a runtime that was torn down (the same trap EditorSetupUtility
        // documents for ??).
        AudioRuntime runtime = AudioRuntime.Instance;
        if (runtime == null) return;

        runtime.PlayClip(clip, entry, position, follow);
    }

    /// <summary>
    /// Whether this id played too recently. Also stamps the play time, so callers that get
    /// through do not each need to remember to. Ids with no entry are rate-limited too —
    /// otherwise an unconfigured footstep would flood the console several times a second.
    /// </summary>
    private static bool IsOnCooldown(string id, SoundBank.SoundEntry entry)
    {
        float cooldown = entry != null ? entry.cooldown : 0f;

        if (cooldown > 0f &&
            LastPlayed.TryGetValue(id, out float last) &&
            Time.unscaledTime - last < cooldown)
        {
            return true;
        }

        LastPlayed[id] = Time.unscaledTime;
        return false;
    }

    /// <summary>A random clip from the entry, or null when it has none (the logging path).</summary>
    private static AudioClip PickClip(SoundBank.SoundEntry entry)
    {
        if (entry?.clips == null || entry.clips.Length == 0) return null;

        // Single-clip entries are the common case; skip the RNG so repeated sounds stay
        // deterministic when there is nothing to vary.
        if (entry.clips.Length == 1) return entry.clips[0];
        return entry.clips[Random.Range(0, entry.clips.Length)];
    }

    /// <summary>Clears cooldown history on play-mode start when domain reload is disabled.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        LastPlayed.Clear();
    }
}
