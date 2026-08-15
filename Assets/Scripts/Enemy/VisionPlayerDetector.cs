using UnityEngine;

/// <summary>
/// Detects the player by sight: within range, within the facing cone, and in line of sight
/// (no walls in between). Assign obstacle layer mask so walls block vision. Vision-based
/// enemies (e.g. SkullGuy) use this component; blind enemy types simply omit it and rely on
/// other detectors.
/// </summary>
public class VisionPlayerDetector : MonoBehaviour, IPlayerDetector
{
    [SerializeField] private float range = 5f;
    [Tooltip("Layers that block line of sight (e.g. walls). Player must not be on these layers.")]
    [SerializeField] private LayerMask obstacleLayers;
    [Header("Field of view")]
    [Tooltip("Vision cone angle in degrees, centered on the enemy's current facing direction. 360 = full circle, sees in every direction regardless of facing (old behavior).")]
    [Range(1f, 360f)]
    [SerializeField] private float viewAngle = 360f;
    [Tooltip("How fast the facing direction can turn to follow movement, in degrees/second. Keeps the cone from snapping instantly on a direction change.")]
    [SerializeField] private float turnSpeedDeg = 360f;
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;

    private Vector3 lastPosition;
    private float facingAngleDeg;

    private void Awake()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        if (viewAngle >= 360f) return;

        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        if (delta.sqrMagnitude < 0.0001f) return;

        float targetAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        facingAngleDeg = Mathf.MoveTowardsAngle(facingAngleDeg, targetAngle, turnSpeedDeg * Time.deltaTime);
    }
    /// <inheritdoc />
    public float DetectionRange => range;

    public bool IsPlayerDetected(Transform player)
    {
        if (player == null) return false;

        Vector2 origin = transform.position;
        Vector2 target = player.position;

        float distance = Vector2.Distance(origin, target);
        if (distance > range) return false;

        if (viewAngle < 360f)
        {
            float angleToPlayer = Mathf.Atan2(target.y - origin.y, target.x - origin.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(facingAngleDeg, angleToPlayer)) > viewAngle * 0.5f)
                return false;
        }

        if (Physics2D.Linecast(origin, target, obstacleLayers))
            return false;

        return true;
    }

    private void OnDrawGizmos()
    {
        if (gizmoDebugSettings != null && !gizmoDebugSettings.IsVisible(GizmoRanges.EnemyVision)) return;

        Gizmos.color = Color.yellow;

        if (viewAngle >= 360f)
        {
            Gizmos.DrawWireSphere(transform.position, range);
            return;
        }

        Vector3 origin = transform.position;
        float halfAngle = viewAngle * 0.5f;
        Vector3 leftEdge = DirectionForAngle(facingAngleDeg - halfAngle);
        Vector3 rightEdge = DirectionForAngle(facingAngleDeg + halfAngle);

        Gizmos.DrawLine(origin, origin + leftEdge * range);
        Gizmos.DrawLine(origin, origin + rightEdge * range);

        const int arcSegments = 24;
        Vector3 previousPoint = origin + leftEdge * range;
        for (int i = 1; i <= arcSegments; i++)
        {
            float angle = facingAngleDeg - halfAngle + viewAngle * i / arcSegments;
            Vector3 point = origin + DirectionForAngle(angle) * range;
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }

    private static Vector3 DirectionForAngle(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
    }
}
