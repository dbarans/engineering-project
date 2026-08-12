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
    [Tooltip("Shared noise ranges asset — walking/sprinting radii are read from here.")]
    [SerializeField] private NoiseSettings noiseSettings;

    private PlayerMovement movement;
    private float nextEmitTime;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (Time.time < nextEmitTime) return;
        if (!movement.IsMoving) return;

        nextEmitTime = Time.time + emitInterval;

        // Audio first, and unconditionally while moving: sneaking emits zero gameplay
        // noise but the player still hears their own careful footsteps. That asymmetry is
        // the reason audio does not simply ride along on NoiseEvents — see AUDIO_NOTES.md D1.
        AudioService.PlayAt(FootstepSoundId(), transform.position);

        float radius = CurrentNoiseRadius();
        if (radius <= 0f) return;

        NoiseEvents.Emit(transform.position, radius);
    }

    /// <summary>Footstep sound for the current movement mode.</summary>
    private string FootstepSoundId()
    {
        switch (movement.CurrentMode)
        {
            case PlayerMovement.MovementMode.Sneak:
                return SoundId.PlayerFootstepSneak;
            case PlayerMovement.MovementMode.Sprint:
                return SoundId.PlayerFootstepSprint;
            default:
                return SoundId.PlayerFootstepWalk;
        }
    }

    /// <summary>
    /// Noise radius for the current movement state: 0 when silent (standing still or sneaking).
    /// </summary>
    private float CurrentNoiseRadius()
    {
        if (noiseSettings == null || !movement.IsMoving) return 0f;

        switch (movement.CurrentMode)
        {
            case PlayerMovement.MovementMode.Sneak:
                return 0f;
            case PlayerMovement.MovementMode.Sprint:
                return noiseSettings.sprintNoiseRadius;
            default:
                return noiseSettings.walkNoiseRadius;
        }
    }
}
