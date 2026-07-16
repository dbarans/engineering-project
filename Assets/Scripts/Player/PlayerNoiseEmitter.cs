using UnityEngine;

/// <summary>
/// Emits movement noise for the player through <see cref="NoiseEvents"/> at a fixed interval.
/// Noise radius depends on the movement mode: sneaking and standing still are silent,
/// walking is audible at walkNoiseRadius, sprinting carries farther (sprintNoiseRadius).
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerNoiseEmitter : MonoBehaviour
{
    [Tooltip("Seconds between noise emissions while moving.")]
    [SerializeField] private float emitInterval = 0.2f;
    [Tooltip("How far walking footsteps carry (world units).")]
    [SerializeField] private float walkNoiseRadius = 4f;
    [Tooltip("How far sprinting footsteps carry (world units).")]
    [SerializeField] private float sprintNoiseRadius = 8f;

    private PlayerMovement movement;
    private float nextEmitTime;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (Time.time < nextEmitTime) return;

        float radius = CurrentNoiseRadius();
        if (radius <= 0f) return;

        nextEmitTime = Time.time + emitInterval;
        NoiseEvents.Emit(transform.position, radius);
    }

    /// <summary>
    /// Noise radius for the current movement state: 0 when silent (standing still or sneaking).
    /// </summary>
    private float CurrentNoiseRadius()
    {
        if (!movement.IsMoving) return 0f;

        switch (movement.CurrentMode)
        {
            case PlayerMovement.MovementMode.Sneak:
                return 0f;
            case PlayerMovement.MovementMode.Sprint:
                return sprintNoiseRadius;
            default:
                return walkNoiseRadius;
        }
    }
}
