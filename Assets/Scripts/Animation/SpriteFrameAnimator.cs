using System;
using UnityEngine;

/// <summary>
/// Lightweight sprite-frame player. Plays named clips by swapping the SpriteRenderer's
/// sprite at a per-clip frame rate. Purpose-built for pre-rendered animations with large
/// frame counts, where a full Animator + huge AnimationClips would be heavy to author.
///
/// Non-looping clips can chain to another clip on completion (e.g. walk-start -> walk-loop)
/// and raise <see cref="ClipFinished"/>.
///
/// Used by the enemies (<see cref="EnemySpriteAnimator"/>, driven by SkullGuyAnimationDriver)
/// and by the player's torso and legs (driven by <see cref="PlayerAnimationDriver"/>).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFrameAnimator : MonoBehaviour
{
    [Serializable]
    public class Clip
    {
        [Tooltip("Logical name used by code to request this clip (e.g. SPOCZYNEK, CHOD_LOOP).")]
        public string name;
        public Sprite[] frames;
        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;
        public bool loop = true;
        [Tooltip("For non-looping clips: clip to play automatically when this one ends. Empty = hold last frame.")]
        public string nextClip;
    }

    [SerializeField] private Clip[] clips;

    private SpriteRenderer spriteRenderer;
    private Clip current;
    private int frameIndex;
    private float timer;
    private bool completed;
    private float speedMultiplier = 1f;
    private bool hidden;

    /// <summary>
    /// Runtime playback speed multiplier (1 = clip's authored fps). Used to sync a walk cycle
    /// to actual movement speed and avoid foot sliding.
    /// </summary>
    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Freezes playback on the current frame without losing the current clip. Used for body
    /// parts whose art is all locomotion: standing still holds a neutral frame instead of
    /// cycling a walk loop on the spot.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// Clears the SpriteRenderer's sprite without losing the current clip or frame; unsetting it
    /// puts the same frame straight back. For body parts that should simply not be drawn in some
    /// state — the player's legs have no idle art, so standing still hides them rather than
    /// showing a stride frozen mid-step.
    /// </summary>
    public bool Hidden
    {
        get => hidden;
        set
        {
            if (hidden == value) return;
            hidden = value;
            if (spriteRenderer == null) return;
            if (hidden) spriteRenderer.sprite = null;
            else ShowCurrentFrame();
        }
    }

    /// <summary>Raised when a non-looping clip reaches its last frame. Argument is the clip name.</summary>
    public event Action<string> ClipFinished;

    /// <summary>Name of the currently playing clip, or null.</summary>
    public string CurrentClipName => current != null ? current.name : null;

    public bool IsPlaying(string clipName) => current != null && current.name == clipName;

    /// <summary>True when the current non-looping clip has reached and is holding its last frame.</summary>
    public bool IsFinished => current != null && completed;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// Plays the named clip from its first frame. Does nothing if that clip is already
    /// playing (unless <paramref name="restartIfSame"/>) or if the name is unknown/empty.
    /// </summary>
    public void Play(string clipName, bool restartIfSame = false)
    {
        if (current != null && current.name == clipName && !restartIfSame)
            return;

        Clip clip = Find(clipName);
        if (clip == null || clip.frames == null || clip.frames.Length == 0)
            return;

        current = clip;
        frameIndex = 0;
        timer = 0f;
        completed = false;
        ShowCurrentFrame();
    }

    /// <summary>Returns the current clip to its first frame without changing which clip is playing.</summary>
    public void Rewind()
    {
        frameIndex = 0;
        timer = 0f;
        completed = false;
        ShowCurrentFrame();
    }

    /// <summary>
    /// Holds the current clip on the frame at <paramref name="normalized"/> — 0 the first frame,
    /// 1 the last — and stops it advancing on its own (it leaves the animator
    /// <see cref="Paused"/>; playing another clip or clearing Paused resumes normal playback).
    ///
    /// For a clip that is the readout of a gameplay value rather than something happening over
    /// time: the axe's wind-up follows how far the swing is charged, so it reaches its last
    /// frame exactly when the attack is fully charged and sits there until the player swings or
    /// lets go, instead of cycling.
    /// </summary>
    public void Scrub(float normalized)
    {
        if (current == null || current.frames == null || current.frames.Length == 0)
            return;

        Paused = true;
        completed = false;
        timer = 0f;
        frameIndex = Mathf.Clamp(Mathf.RoundToInt(normalized * (current.frames.Length - 1)),
                                 0, current.frames.Length - 1);
        ShowCurrentFrame();
    }

    /// <summary>
    /// Pushes the current frame to the renderer. Does nothing while <see cref="Hidden"/>, so a
    /// hidden part stays blank even if its clip keeps advancing underneath.
    /// </summary>
    private void ShowCurrentFrame()
    {
        if (hidden || spriteRenderer == null || current == null || current.frames == null) return;
        if (frameIndex >= 0 && frameIndex < current.frames.Length)
            spriteRenderer.sprite = current.frames[frameIndex];
    }

    private void Update()
    {
        if (current == null || completed || Paused || current.fps <= 0f)
            return;

        float effectiveFps = current.fps * speedMultiplier;
        if (effectiveFps <= 0f)
            return;

        timer += Time.deltaTime;
        float frameDuration = 1f / effectiveFps;

        while (timer >= frameDuration)
        {
            timer -= frameDuration;
            int next = frameIndex + 1;

            if (next >= current.frames.Length)
            {
                if (current.loop)
                {
                    next = 0;
                }
                else
                {
                    frameIndex = current.frames.Length - 1;
                    ShowCurrentFrame();
                    completed = true;

                    string finishedName = current.name;
                    string chain = current.nextClip;
                    ClipFinished?.Invoke(finishedName);
                    if (!string.IsNullOrEmpty(chain))
                        Play(chain, true);
                    return;
                }
            }

            frameIndex = next;
            ShowCurrentFrame();
        }
    }

    private Clip Find(string clipName)
    {
        if (clips == null) return null;
        foreach (Clip c in clips)
            if (c != null && c.name == clipName)
                return c;
        return null;
    }
}
