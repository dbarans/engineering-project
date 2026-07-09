using System;
using UnityEngine;

/// <summary>
/// Lightweight sprite-frame player. Plays named clips by swapping the SpriteRenderer's
/// sprite at a per-clip frame rate. Purpose-built for pre-rendered enemy animations with
/// large frame counts, where a full Animator + huge AnimationClips would be heavy to author
/// and would duplicate the AI state machine already present in <see cref="EnemyBase"/>.
///
/// Non-looping clips can chain to another clip on completion (e.g. walk-start -> walk-loop)
/// and raise <see cref="ClipFinished"/>.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EnemySpriteAnimator : MonoBehaviour
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

    /// <summary>
    /// Runtime playback speed multiplier (1 = clip's authored fps). Used to sync a walk cycle
    /// to actual movement speed and avoid foot sliding.
    /// </summary>
    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = Mathf.Max(0f, value);
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
        spriteRenderer.sprite = clip.frames[0];
    }

    private void Update()
    {
        if (current == null || completed || current.fps <= 0f)
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
                    spriteRenderer.sprite = current.frames[frameIndex];
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
            spriteRenderer.sprite = current.frames[frameIndex];
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
