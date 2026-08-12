using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the pool of <see cref="AudioSource"/>s every sound is played through. Bootstrapped
/// on play into a <c>DontDestroyOnLoad</c> object — never placed in a scene — the same
/// pattern <see cref="SaveDebugHotkeys"/> uses, so no scene needs wiring for audio to work
/// and a generated dungeon is no different from an authored one.
///
/// Pooling rather than an AudioSource per emitter, because most things that make a sound
/// here are destroyed in the same frame they make it: a picked-up item, a breaking
/// barricade stage, a dying enemy. A source living on the emitter would be destroyed
/// mid-playback and cut its own sound off. A pooled source outlives the emitter and
/// finishes (AUDIO_NOTES.md D5).
///
/// Sources are reused once free; the pool only grows when every existing source is still
/// playing, and it is capped so a runaway hook site cannot spawn unbounded objects.
/// </summary>
[DisallowMultipleComponent]
public class AudioRuntime : MonoBehaviour
{
    /// <summary>
    /// Ceiling on simultaneous sounds. Past this, the oldest source is stolen — a dropped
    /// sound is strictly better than an unbounded object count, and at this many voices
    /// nobody can pick out the one that got cut.
    /// </summary>
    private const int MaxSources = 24;

    private static AudioRuntime _instance;

    private readonly List<AudioSource> _pool = new List<AudioSource>();
    private int _nextStealIndex;

    /// <summary>The live runtime, created on first use.</summary>
    public static AudioRuntime Instance
    {
        get
        {
            if (_instance == null) Bootstrap();
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        var go = new GameObject(nameof(AudioRuntime));
        _instance = go.AddComponent<AudioRuntime>();
        DontDestroyOnLoad(go);
    }

    /// <summary>
    /// Plays one clip with the given settings at a world position. <paramref name="follow"/>
    /// keeps the source attached to a moving emitter; pass null for a fixed point.
    /// </summary>
    public void PlayClip(AudioClip clip, SoundBank.SoundEntry entry, Vector3 position, Transform follow)
    {
        if (clip == null || entry == null) return;

        AudioSource source = Rent();
        if (source == null) return;

        Transform sourceTransform = source.transform;
        if (follow != null)
        {
            sourceTransform.SetParent(follow, worldPositionStays: false);
            sourceTransform.localPosition = Vector3.zero;
        }
        else
        {
            // Stays parented to the runtime rather than being detached to the scene root.
            // A detached source belongs to the active scene and is destroyed on the next
            // scene load, which would quietly fill the pool with dead entries that are
            // never reused and never replaced.
            sourceTransform.SetParent(transform, worldPositionStays: false);
            sourceTransform.position = position;
        }

        source.clip = clip;
        source.volume = entry.volume;
        source.pitch = Random.Range(entry.pitchMin, entry.pitchMax);
        source.spatialBlend = entry.spatialBlend;
        source.minDistance = entry.minDistance;
        source.maxDistance = entry.maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.loop = false;
        source.Play();
    }

    /// <summary>
    /// A free source, a new one, or (at the cap) the oldest in-flight source. Never null
    /// except when the runtime is being torn down.
    /// </summary>
    private AudioSource Rent()
    {
        foreach (var source in _pool)
        {
            if (source != null && !source.isPlaying) return Reset(source);
        }

        if (_pool.Count < MaxSources)
        {
            var go = new GameObject($"PooledAudio_{_pool.Count}");
            go.transform.SetParent(transform, worldPositionStays: false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            _pool.Add(source);
            return source;
        }

        // Everything is busy: round-robin so the same source is not always the victim.
        _nextStealIndex = (_nextStealIndex + 1) % _pool.Count;
        AudioSource stolen = _pool[_nextStealIndex];
        if (stolen != null) stolen.Stop();
        return Reset(stolen);
    }

    /// <summary>
    /// Detaches a source from whatever emitter it was following last time. Without this a
    /// reused source stays parented to an object that may since have been destroyed, which
    /// would take the pooled source down with it.
    /// </summary>
    private AudioSource Reset(AudioSource source)
    {
        if (source == null) return null;
        source.transform.SetParent(transform, worldPositionStays: false);
        return source;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Drops the reference on play-mode start when domain reload is disabled.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }
}
