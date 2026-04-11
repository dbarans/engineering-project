using UnityEngine;

/// <summary>
/// Detects player when in range and in line of sight (no walls in between).
/// Assign obstacle layer mask so walls block vision.
/// </summary>
public class VisionPlayerDetector : MonoBehaviour, IPlayerDetector
{
    [SerializeField] private float range = 5f;
    [Tooltip("Layers that block line of sight (e.g. walls). Player must not be on these layers.")]
    [SerializeField] private LayerMask obstacleLayers;

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
}
