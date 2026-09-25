using UnityEngine;

/// <summary>
/// Editor-only tuning aid: draws a static wire circle for every noise radius configured in
/// <see cref="NoiseSettings"/> (walk, sprint, throw landing, shoot), centered on the player,
/// so the ranges can be compared and adjusted without waiting for an actual emission.
/// Toggle <see cref="showRanges"/> off to hide these and see only the live emitted-noise
/// circles drawn by <see cref="NoiseGizmoDebugger"/>.
/// </summary>
public class PlayerNoiseRangeGizmos : MonoBehaviour
{
    [SerializeField] private NoiseSettings noiseSettings;
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;

    private void OnDrawGizmos()
    {
        if (noiseSettings == null || gizmoDebugSettings == null) return;

        if (gizmoDebugSettings.IsVisible(GizmoRanges.PlayerWalkNoise))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, noiseSettings.walkNoiseRadius);
        }

        if (gizmoDebugSettings.IsVisible(GizmoRanges.PlayerSprintNoise))
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, noiseSettings.sprintNoiseRadius);
        }

        if (gizmoDebugSettings.IsVisible(GizmoRanges.PlayerThrowNoise))
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, noiseSettings.throwLandingRadius);
        }

        if (gizmoDebugSettings.IsVisible(GizmoRanges.PlayerShootNoise))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, noiseSettings.shootNoiseRadius);
        }
    }
}
