using UnityEngine;

/// <summary>
/// Detects the player by hearing instead of sight. The player is heard only while actually
/// moving in Walk or Sprint mode and within range; standing still or Sneak mode is silent.
/// Optionally blocked by obstacles (e.g. walls muffle sound the same way they block vision).
/// </summary>
public class SoundPlayerDetector : MonoBehaviour, IPlayerDetector
{
    [SerializeField] private float hearingRange = 4f;
    [Tooltip("Layers that block sound (e.g. walls). Leave empty to hear through everything within range.")]
    [SerializeField] private LayerMask hearingBlockerMask;

    public bool IsPlayerDetected(Transform player)
    {
        if (player == null) return false;

        PlayerMovement playerMovement = player.GetComponent<PlayerMovement>();
        if (playerMovement == null) return false;
        if (playerMovement.CurrentMode == PlayerMovement.MovementMode.Sneak) return false;
        if (!playerMovement.IsMoving) return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > hearingRange) return false;

        Vector2 origin = transform.position;
        Vector2 target = player.position;
        if (Physics2D.Linecast(origin, target, hearingBlockerMask))
            return false;

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hearingRange);
    }
}
