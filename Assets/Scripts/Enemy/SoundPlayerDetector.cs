using UnityEngine;

/// <summary>
/// Detects the player by hearing instead of sight. The player is heard only while
/// moving in Walk or Sprint mode and within range; Sneak mode is silent and never detected.
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
        if (playerMovement == null || playerMovement.CurrentMode == PlayerMovement.MovementMode.Sneak)
            return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > hearingRange) return false;

        Vector2 origin = transform.position;
        Vector2 target = player.position;
        if (Physics2D.Linecast(origin, target, hearingBlockerMask))
            return false;

        return true;
    }
}
