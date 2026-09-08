using UnityEngine;

/// <summary>
/// Plays a footstep every time this enemy takes a stride, so the player can hear one coming —
/// and hear roughly how fast it is moving — without seeing it.
///
/// Deliberately the player's own footstep clip, pitched down and turned down in the
/// <see cref="SoundBank"/> entry rather than here: same floor, same boots, a different body.
/// Distance does the rest of the work. The entry is positional with a linear rolloff, so an
/// enemy two rooms away is genuinely quieter than one in this one, and past the entry's
/// <c>maxDistance</c> it costs nothing to have played at all.
///
/// Cadence follows actual ground speed rather than a fixed timer: a chasing enemy (chase speed
/// multiplier) steps that much faster, which is the cue that it has seen you. Pacing off measured
/// movement rather than the AI state also means a blocked enemy grinding against a wall goes
/// quiet, instead of marching on the spot.
/// </summary>
[RequireComponent(typeof(EnemyBase))]
public class EnemyFootstepAudio : MonoBehaviour
{
    [Tooltip("Speed (units/sec) below which the enemy counts as standing still and takes no steps.")]
    [Min(0f)]
    [SerializeField] private float moveThreshold = 0.15f;

    [Tooltip("Seconds between steps at referenceSpeed. Faster movement scales this down proportionally.")]
    [Min(0.05f)]
    [SerializeField] private float stepInterval = 0.55f;

    [Tooltip("Movement speed the interval above is authored for. Set it to the enemy's ordinary moveSpeed; chasing then steps faster on its own.")]
    [Min(0.01f)]
    [SerializeField] private float referenceSpeed = 1.8f;

    [Tooltip("Fastest the cadence may get, however fast the enemy is moving. Stops a knockback or a throttled catch-up step from firing a burst of footsteps.")]
    [Min(0.05f)]
    [SerializeField] private float minStepInterval = 0.22f;

    [Tooltip("Slowest the cadence may get. Keeps a crawling enemy audible instead of silent for seconds at a time.")]
    [Min(0.05f)]
    [SerializeField] private float maxStepInterval = 1.2f;

    private EnemyBase enemy;
    private Vector3 lastPosition;
    private float smoothedSpeed;
    private float nextStepTime;

    private void Awake()
    {
        enemy = GetComponent<EnemyBase>();
        lastPosition = transform.position;
    }

    private void Update()
    {
        // Parked enemies (EnemyBase culling) are far past the clip's audible range anyway, and a
        // dead one has stopped walking by definition.
        if (enemy.IsDead || enemy.IsCulled)
        {
            lastPosition = transform.position;
            return;
        }

        UpdateSpeed();

        if (smoothedSpeed < moveThreshold)
        {
            // The timer does not run while standing: the first step out of a stop lands as the
            // enemy starts moving, not after waiting out a cadence it stood through.
            nextStepTime = 0f;
            return;
        }

        if (Time.time < nextStepTime) return;

        nextStepTime = Time.time + StepIntervalForCurrentSpeed();
        AudioService.PlayAt(SoundId.EnemyFootstep, transform.position);
    }

    private void UpdateSpeed()
    {
        Vector3 position = transform.position;
        float speed = Time.deltaTime > 0f ? (position - lastPosition).magnitude / Time.deltaTime : 0f;
        lastPosition = position;

        // Smoothed, because the AI moves in throttled steps when the player is far away: raw
        // per-frame speed there alternates between a large jump and zero, and would flip the
        // enemy between sprinting and standing several times a second.
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 10f * Time.deltaTime);
    }

    /// <summary>Stride length scaled by how fast the enemy is actually covering ground.</summary>
    private float StepIntervalForCurrentSpeed()
    {
        float scaled = stepInterval * referenceSpeed / Mathf.Max(0.01f, smoothedSpeed);
        return Mathf.Clamp(scaled, minStepInterval, Mathf.Max(minStepInterval, maxStepInterval));
    }
}
