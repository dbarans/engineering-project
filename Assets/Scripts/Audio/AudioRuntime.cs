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


    /// <summary>
    /// The runtime if one exists, without creating it. <see cref="Instance"/> bootstraps on
    /// access, which is right for playing a sound and wrong for stopping one: a scene being
    /// torn down calls StopMusic from OnDestroy, and resurrecting the runtime there would
    /// build a GameObject Unity is in the middle of cleaning up.
    /// </summary>
    public static AudioRuntime Current => _instance;

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
        // Reassigned per play rather than once when the source is created: a pooled source
        // is reused across channels, so the group it carried last time is not the one this
        // sound belongs to. Null (no mixer asset) plays straight to the listener.
        source.outputAudioMixerGroup = AudioMixerService.GroupFor(entry.channel);
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
    /// The one source music plays through, deliberately outside <see cref="_pool"/>. A
    /// looping track never reports <c>!isPlaying</c>, so a pooled source would be rented
    /// forever and — once the pool filled — eventually stolen mid-track by Rent's
    /// round-robin steal. Its own source also gives music the thing one-shots never need:
    /// something that can be stopped.
    /// </summary>
    private AudioSource _musicSource;

    /// <summary>Id of the track currently playing, so a repeat request does not restart it.</summary>
    private string _musicId;

    /// <summary>
    /// Starts a looping track, or does nothing when that same track is already playing —
    /// re-entering the menu scene should not restart a track that never stopped.
    /// </summary>
    public void PlayMusic(string id, AudioClip clip, SoundBank.SoundEntry entry)
    {
        if (clip == null || entry == null) return;
        if (_musicId == id && _musicSource != null && _musicSource.isPlaying) return;

        if (_musicSource == null)
        {
            var go = new GameObject("MusicAudio");
            go.transform.SetParent(transform, worldPositionStays: false);
            _musicSource = go.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
        }

        _musicId = id;
        _musicSource.clip = clip;
        _musicSource.outputAudioMixerGroup = AudioMixerService.GroupFor(entry.channel);
        _musicSource.volume = entry.volume;
        // No pitch jitter, unlike PlayClip: the entry's range exists to keep a repeated
        // one-shot from fatiguing, and a music track that plays back a few percent fast
        // is out of tune with itself, not varied.
        _musicSource.pitch = 1f;
        _musicSource.spatialBlend = entry.spatialBlend;
        _musicSource.loop = true;
        _musicSource.Play();
    }

    /// <summary>Stops whatever track is playing. Safe to call when there is none.</summary>
    public void StopMusic()
    {
        _musicId = null;
        if (_musicSource == null) return;
        _musicSource.Stop();
        _musicSource.clip = null;
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
