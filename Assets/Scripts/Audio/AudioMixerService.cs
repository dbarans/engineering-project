using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Owns <c>Assets/Resources/MainMixer.mixer</c> — the one mixer every sound is routed
/// through — and is the only place that knows the mapping from an <see cref="AudioChannel"/>
/// to a mixer group and to an exposed volume parameter.
///
/// Loaded from <c>Resources</c> like <see cref="SoundBank"/>, <see cref="KeyBindings"/> and
/// <see cref="PrefabRegistry"/>: no scene wiring, works from any component, and a missing
/// asset degrades instead of throwing. With no mixer present every sound still plays — it
/// just goes straight to the listener and cannot be turned down per channel (AUDIO_NOTES.md
/// D9).
///
/// Volumes are stored linearly (0..1, what a settings slider hands you) and converted to
/// decibels here, because the mixer works in dB and a slider that maps linearly to dB feels
/// wrong everywhere except the top of its travel.
/// </summary>
public static class AudioMixerService
{
    /// <summary>Path under Resources the mixer is loaded from.</summary>
    public const string ResourcesPath = "MainMixer";

    /// <summary>Volume below which a channel is snapped to silence rather than a tiny dB value.</summary>
    private const float SilenceThreshold = 0.0001f;

    /// <summary>The mixer's own floor. Anything quieter is inaudible anyway.</summary>
    private const float MinDecibels = -80f;

    private const string PrefKeyPrefix = "audio.volume.";

    private static AudioMixer _mixer;
    private static bool _warnedMissing;

    private static readonly Dictionary<AudioChannel, AudioMixerGroup> GroupCache =
        new Dictionary<AudioChannel, AudioMixerGroup>();

    /// <summary>
    /// The project-wide mixer, or null when the asset does not exist. Warns once rather
    /// than per lookup — group resolution happens on every played sound.
    /// </summary>
    public static AudioMixer Mixer
    {
        get
        {
            if (_mixer == null && !_warnedMissing)
            {
                _mixer = Resources.Load<AudioMixer>(ResourcesPath);
                if (_mixer == null)
                {
                    _warnedMissing = true;
                    Debug.LogWarning(
                        $"[AudioMixerService] No '{ResourcesPath}' asset in Resources — sounds " +
                        "will play unrouted and per-channel volume will do nothing.");
                }
            }
            return _mixer;
        }
    }

    /// <summary>
    /// The mixer group a channel plays through, or null when there is no mixer. Null is a
    /// supported output: an <c>AudioSource</c> with no group plays straight to the listener.
    /// </summary>
    public static AudioMixerGroup GroupFor(AudioChannel channel)
    {
        // A miss is cached as deliberately as a hit: this runs on every played sound, and
        // re-resolving a group that is not there would warn once per footstep.
        if (GroupCache.TryGetValue(channel, out AudioMixerGroup cached)) return cached;

        AudioMixerGroup group = FindGroup(channel);
        GroupCache[channel] = group;
        return group;
    }

    /// <summary>Stored volume for a channel, 0..1. Defaults to full when never set.</summary>
    public static float GetVolume(AudioChannel channel)
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKey(channel), 1f));
    }

    /// <summary>
    /// Sets a channel's volume (0..1), applies it to the mixer and remembers it. Safe to
    /// call with no mixer present — the value is still stored, and takes effect once the
    /// asset exists.
    /// </summary>
    public static void SetVolume(AudioChannel channel, float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKey(channel), volume);
        Apply(channel, volume);
    }

    /// <summary>
    /// Pushes every stored volume into the mixer. Called on play-mode start, because
    /// exposed parameters live on the asset and reset to their authored 0 dB each session.
    /// </summary>
    public static void ApplyStoredVolumes()
    {
        if (Mixer == null) return;

        Apply(AudioChannel.Master, GetVolume(AudioChannel.Master));
        Apply(AudioChannel.Music, GetVolume(AudioChannel.Music));
        Apply(AudioChannel.Sfx, GetVolume(AudioChannel.Sfx));
        Apply(AudioChannel.Ui, GetVolume(AudioChannel.Ui));
    }

    /// <summary>Name of the exposed parameter driving this channel's volume.</summary>
    public static string VolumeParameter(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Music: return "MusicVolume";
            case AudioChannel.Ui: return "UiVolume";
            case AudioChannel.Master: return "MasterVolume";
            default: return "SfxVolume";
        }
    }

    private static void Apply(AudioChannel channel, float volume)
    {
        AudioMixer mixer = Mixer;
        if (mixer == null) return;

        string parameter = VolumeParameter(channel);
        if (mixer.SetFloat(parameter, LinearToDecibels(volume))) return;

        // SetFloat also returns false for reasons that are not a wiring problem — it does
        // nothing outside play mode, for one — so the parameter's existence is confirmed
        // separately before blaming the asset. GetFloat answers that in any mode.
        if (mixer.GetFloat(parameter, out _)) return;

        Debug.LogWarning(
            $"[AudioMixerService] Mixer has no exposed parameter '{parameter}' — run " +
            $"Tools ▸ Audio ▸ Build Audio Mixer, or expose the {channel} group's volume on " +
            $"'{ResourcesPath}' by hand, to make that channel adjustable.");
    }

    /// <summary>
    /// A slider's 0..1 as decibels. Logarithmic because loudness is: a linear slider mapped
    /// straight onto dB spends most of its travel in a range that already sounds silent.
    /// </summary>
    private static float LinearToDecibels(float volume)
    {
        if (volume <= SilenceThreshold) return MinDecibels;
        return Mathf.Log10(Mathf.Clamp01(volume)) * 20f;
    }

    /// <summary>
    /// Resolves a channel to its group by full path.
    ///
    /// <c>FindMatchingGroups</c> is a substring match on the group path, not an exact one —
    /// asking it for "Master" returns Master <em>and</em> every child, since each child's
    /// path starts with it. Hence the leaf-name check: without it, Master would resolve to
    /// whichever child happened to come back first.
    /// </summary>
    private static AudioMixerGroup FindGroup(AudioChannel channel)
    {
        AudioMixer mixer = Mixer;
        if (mixer == null) return null;

        string leaf = GroupName(channel);
        string path = channel == AudioChannel.Master ? leaf : $"Master/{leaf}";

        foreach (AudioMixerGroup group in mixer.FindMatchingGroups(path))
        {
            if (group != null && group.name == leaf) return group;
        }

        Debug.LogWarning(
            $"[AudioMixerService] '{ResourcesPath}' has no group at '{path}' — {channel} " +
            "sounds will play unrouted.");
        return null;
    }

    private static string GroupName(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Music: return "Music";
            case AudioChannel.Ui: return "UI";
            case AudioChannel.Master: return "Master";
            default: return "SFX";
        }
    }

    private static string PrefKey(AudioChannel channel) => PrefKeyPrefix + channel.ToString().ToLowerInvariant();

    /// <summary>
    /// Loads the mixer and restores saved volumes at play-mode start, and clears the cached
    /// asset so a domain reload with reloading disabled does not keep the previous session's
    /// references (or its "already warned" flag).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _mixer = null;
        _warnedMissing = false;
        GroupCache.Clear();
    }

    /// <summary>
    /// Applied after the scene loads rather than in <see cref="ResetStatics"/>: Resources
    /// loading is not allowed that early in the startup sequence.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreVolumes()
    {
        ApplyStoredVolumes();
    }
}
