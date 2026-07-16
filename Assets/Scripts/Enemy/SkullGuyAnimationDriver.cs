using UnityEngine;

/// <summary>
/// Bridges the enemy AI (<see cref="EnemyBase"/>) to the sprite-frame player
/// (<see cref="EnemySpriteAnimator"/>): reads the current AI state and movement each frame,
/// requests the matching animation clip, and rotates the sprite to face its target.
///
/// Mapping:
///   Idle / ReturnToPatrol, not moving  -> SPOCZYNEK
///   moving                             -> CHOD_POCZATEK -> CHOD_LOOP -> CHOD_KONIEC (auto-chained)
///   InvestigateLastKnown / -Noise, standing -> ROZGLADANIE
///   just detected the player           -> RYK (one-shot)
///   attack (triggered by combat code)  -> ATAK (one-shot, via TriggerAttack)
/// </summary>
[RequireComponent(typeof(EnemyBase))]
public class SkullGuyAnimationDriver : MonoBehaviour
{
    // Clip names — must match the clip names set on EnemySpriteAnimator (see the loader tool).
    private const string Idle = "SPOCZYNEK";
    private const string WalkStart = "CHOD_POCZATEK";
    private const string WalkLoop = "CHOD_LOOP";
    private const string WalkEnd = "CHOD_KONIEC";
    private const string Roar = "RYK";
    private const string Attack = "ATAK";
    private const string LookAround = "ROZGLADANIE";

    [SerializeField] private EnemySpriteAnimator animator;
    [Tooltip("Speed (units/sec) above which the enemy is considered moving.")]
    [SerializeField] private float moveThreshold = 0.05f;

    [Header("Facing")]
    [Tooltip("Rotate the sprite to face its target. Disable if the art is not directional.")]
    [SerializeField] private bool rotateToFaceTarget = true;
    [Tooltip("Degrees added so the art's forward matches +X (right). If facing is 90° off, try 90 / -90 / 180.")]
    [SerializeField] private float spriteForwardOffsetDeg = 0f;
    [Tooltip("Turn speed in degrees/second.")]
    [SerializeField] private float turnSpeedDeg = 540f;

    [Header("Walk sync")]
    [Tooltip("Movement speed at which the walk animation plays at its authored fps. Lower this if the feet slide forward, raise it if they slide backward.")]
    [SerializeField] private float walkSyncReferenceSpeed = 3f;
    [SerializeField] private float minWalkSpeedMultiplier = 0.3f;
    [SerializeField] private float maxWalkSpeedMultiplier = 3f;

    private EnemyBase enemy;
    private Transform visual;
    private EnemyState prevState;
    private Vector3 lastPosition;
    private Vector3 velocity;
    private float smoothedSpeed;

    private void Awake()
    {
        enemy = GetComponent<EnemyBase>();
        if (animator == null)
            animator = GetComponentInChildren<EnemySpriteAnimator>();
        visual = animator != null ? animator.transform : transform;
    }

    private void Start()
    {
        prevState = enemy.CurrentState;
        lastPosition = transform.position;
        animator?.Play(Idle);
    }

    /// <summary>Plays the attack animation on demand (called by SkullGuyEnemy combat code).</summary>
    public void TriggerAttack()
    {
        animator?.Play(Attack, true);
    }

    private void Update()
    {
        if (animator == null)
            return;

        UpdateVelocity();
        bool moving = velocity.magnitude > moveThreshold;
        EnemyState state = enemy.CurrentState;

        // Facing runs every frame — the enemy keeps facing its target even while roaring/attacking.
        UpdateFacing(state, moving);

        // While a one-shot (roar/attack) is still in progress, don't change the animation.
        // Once it has finished (holding its last frame), release and pick the next state.
        if ((animator.IsPlaying(Roar) || animator.IsPlaying(Attack)) && !animator.IsFinished)
        {
            prevState = state;
            return;
        }

        string clip = animator.CurrentClipName;

        // Roar once when the player is first spotted (entering FollowPlayer from a calm state).
        if (state == EnemyState.FollowPlayer &&
            prevState != EnemyState.FollowPlayer &&
            prevState != EnemyState.InvestigateLastKnown)
        {
            animator.Play(Roar);
        }
        else if (moving)
        {
            // Whatever non-walk clip we were on (idle, look-around, roar/attack end), start walking.
            if (clip != WalkStart && clip != WalkLoop)
                animator.Play(WalkStart); // chains to WalkLoop
        }
        else if (state == EnemyState.InvestigateLastKnown || state == EnemyState.InvestigateNoise)
        {
            // Standing while investigating -> look around.
            if (clip != LookAround)
                animator.Play(LookAround);
        }
        else
        {
            // Not moving: play the stop transition when coming from walking, else settle on idle.
            if (clip == WalkStart || clip == WalkLoop)
                animator.Play(WalkEnd); // chains to Idle
            else if (clip != WalkEnd && clip != Idle)
                animator.Play(Idle);
        }

        SyncWalkTempo();

        prevState = state;
    }

    /// <summary>
    /// Scales walk-clip playback with actual movement speed so the stride matches the ground
    /// speed (no foot sliding). Other clips play at their authored fps.
    /// </summary>
    private void SyncWalkTempo()
    {
        string clip = animator.CurrentClipName;
        bool isWalkClip = clip == WalkStart || clip == WalkLoop || clip == WalkEnd;

        if (isWalkClip && walkSyncReferenceSpeed > 0.01f)
        {
            float ratio = smoothedSpeed / walkSyncReferenceSpeed;
            animator.SpeedMultiplier = Mathf.Clamp(ratio, minWalkSpeedMultiplier, maxWalkSpeedMultiplier);
        }
        else
        {
            animator.SpeedMultiplier = 1f;
        }
    }

    private void UpdateVelocity()
    {
        Vector3 pos = transform.position;
        velocity = Time.deltaTime > 0f ? (pos - lastPosition) / Time.deltaTime : Vector3.zero;
        lastPosition = pos;

        // Smooth the speed used to drive walk tempo so it doesn't jitter frame to frame.
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, velocity.magnitude, 10f * Time.deltaTime);
    }

    private void UpdateFacing(EnemyState state, bool moving)
    {
        if (!rotateToFaceTarget || visual == null)
            return;

        Vector2 dir;
        if (state == EnemyState.FollowPlayer
            || state == EnemyState.InvestigateLastKnown
            || state == EnemyState.InvestigateNoise)
            dir = enemy.CurrentTargetPosition - transform.position;
        else if (moving)
            dir = velocity;
        else
            return; // idle — keep current facing

        if (dir.sqrMagnitude < 0.0001f)
            return;

        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + spriteForwardOffsetDeg;
        Quaternion targetRot = Quaternion.Euler(0f, 0f, targetAngle);
        visual.rotation = Quaternion.RotateTowards(visual.rotation, targetRot, turnSpeedDeg * Time.deltaTime);
    }
}
