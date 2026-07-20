using UnityEngine;

/// <summary>
/// Detects the player by sight: within range and in line of sight (no walls in between).
/// Assign obstacle layer mask so walls block vision. Vision-based enemies (e.g. SkullGuy)
/// use this component; blind enemy types simply omit it and rely on other detectors.
/// </summary>
public class VisionPlayerDetector : MonoBehaviour, IPlayerDetector
{
    [SerializeField] private float range = 5f;
    [Tooltip("Layers that block line of sight (e.g. walls). Player must not be on these layers.")]
    [SerializeField] private LayerMask obstacleLayers;
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;

    public bool IsPlayerDetected(Transform player)
    {
        if (player == null) return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > range) return false;

        Vector2 origin = transform.position;
        Vector2 target = player.position;
        if (Physics2D.Linecast(origin, target, obstacleLayers))
            return false;

        return true;
    }

    private void OnDrawGizmos()
    {
        if (gizmoDebugSettings != null && !gizmoDebugSettings.IsVisible(GizmoRanges.EnemyVision)) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
