using UnityEngine;

/// <summary>
/// Emits movement noise for the player through <see cref="NoiseEvents"/> at a fixed interval.
/// Noise radius depends on the movement mode: sneaking and standing still are silent,
/// walking is audible at walkNoiseRadius, sprinting carries farther (sprintNoiseRadius).
///
/// The floor gets the last word. While the player stands on a <see cref="NoisySurface"/>
/// (broken glass), that surface's per-mode ranges replace the ones configured here and its
/// step sound replaces the footstep — which is how sneaking, silent everywhere else, still
/// gives the player away on glass.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerNoiseEmitter : MonoBehaviour
{
    [Tooltip("Seconds between noise emissions while moving.")]
    [SerializeField] private float emitInterval = 0.2f;
    [Tooltip("Shared noise ranges asset — walking/sprinting radii are read from here.")]
    [SerializeField] private NoiseSettings noiseSettings;

    private PlayerMovement movement;
    private PlayerSurfaceTracker surfaces;
    private float nextEmitTime;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();

        // Added here rather than required on the prefab: the tracker is pure bookkeeping
        // for this class, and a [RequireComponent] would only add it to prefabs someone
        // happens to open in the editor.
        surfaces = GetComponent<PlayerSurfaceTracker>();
        if (surfaces == null) surfaces = gameObject.AddComponent<PlayerSurfaceTracker>();
    }

    private void Update()
    {
        if (Time.time < nextEmitTime) return;
        if (!movement.IsMoving) return;

        nextEmitTime = Time.time + emitInterval;

        // Audio first, and unconditionally while moving: sneaking emits zero gameplay
        // noise but the player still hears their own careful footsteps. That asymmetry is
        // the reason audio does not simply ride along on NoiseEvents — see AUDIO_NOTES.md D1.
        AudioService.PlayAt(StepSoundId(), transform.position);

        float radius = CurrentNoiseRadius();
        if (radius <= 0f) return;

        NoiseEvents.Emit(transform.position, radius);
    }

    /// <summary>
    /// Sound for this step: the surface underfoot when there is one, otherwise the
    /// footstep for the current movement mode.
    /// </summary>
    private string StepSoundId()
    {
        string surfaceSound = surfaces.StepSoundFor(movement.CurrentMode);
        if (!string.IsNullOrEmpty(surfaceSound)) return surfaceSound;

        return FootstepSoundId();
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
    /// Noise radius for the current movement state: 0 when silent (standing still, or
    /// sneaking on ordinary floor).
    ///
    /// A noisy surface underfoot wins outright rather than adding to the mode radius — the
    /// two describe the same footstep, and the loudest description of it is the true one.
    /// It is also checked before the <c>noiseSettings</c> guard on purpose: glass has to
    /// stay audible even in a scene where the shared asset was never assigned.
    /// </summary>
    private float CurrentNoiseRadius()
    {
        if (!movement.IsMoving) return 0f;

        float surfaceRadius = surfaces.NoiseRadiusFor(movement.CurrentMode);
        if (surfaceRadius > 0f) return surfaceRadius;

        if (noiseSettings == null) return 0f;

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
