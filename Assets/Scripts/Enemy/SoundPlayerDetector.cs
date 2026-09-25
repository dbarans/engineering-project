using UnityEngine;

/// <summary>
/// Hearing sensor for enemies. Listens to <see cref="NoiseEvents"/> and registers any noise
/// that is within both its radius and this enemy's hearingRange, optionally blocked by obstacles
/// (walls muffle sound the same way they block vision). Exposes the result as
/// <see cref="INoiseSensor"/>: hearing a noise is a suspicion that sends the enemy to
/// investigate the noise position, not a confirmed player detection.
/// </summary>
public class SoundPlayerDetector : MonoBehaviour, INoiseSensor
{
    [Tooltip("Maximum distance at which this enemy can hear anything, no matter how loud. Cap on the noise radius.")]
    [SerializeField] private float hearingRange = 10f;
    [Tooltip("Layers that block sound (e.g. walls). Leave empty to hear through everything within range.")]
    [SerializeField] private LayerMask hearingBlockerMask;
    [Tooltip("How long (seconds) a heard noise stays fresh. Should exceed the emitter's interval so continuous movement reads as a continuous trail.")]
    [SerializeField] private float heardNoiseRetention = 0.35f;
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;

    private float lastHeardTime = float.NegativeInfinity;
    private Vector2 lastNoisePosition;

    public bool HasFreshNoise => Time.time - lastHeardTime <= heardNoiseRetention;

    public Vector2 LastNoisePosition => lastNoisePosition;

    private void OnEnable()
    {
        NoiseEvents.NoiseEmitted += OnNoiseEmitted;
    }

    private void OnDisable()
    {
        NoiseEvents.NoiseEmitted -= OnNoiseEmitted;
    }

    /// <summary>
    /// Registers a noise if it is audible from this enemy's position: within the noise radius,
    /// within hearingRange, and not blocked by hearingBlockerMask.
    /// </summary>
    private void OnNoiseEmitted(Vector2 position, float radius)
    {
        float distance = Vector2.Distance(transform.position, position);
        if (distance > Mathf.Min(radius, hearingRange)) return;

        if (Physics2D.Linecast(transform.position, position, hearingBlockerMask))
            return;

        lastHeardTime = Time.time;
        lastNoisePosition = position;
    }

    private void OnDrawGizmos()
    {
        if (gizmoDebugSettings != null && !gizmoDebugSettings.IsVisible(GizmoRanges.EnemyHearing)) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hearingRange);
    }
}
