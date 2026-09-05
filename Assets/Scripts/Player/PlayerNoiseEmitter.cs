using UnityEngine;

/// <summary>
/// Emits movement noise for the player through <see cref="NoiseEvents"/> at a fixed interval.
/// Noise radius depends on the movement mode: sneaking and standing still are silent,
/// walking is audible at walkNoiseRadius, sprinting carries farther (sprintNoiseRadius).
///
/// Also plays the footstep the player hears — on its own timer. The two cadences answer
/// different questions and are deliberately not the same number: a footstep sound is paced
/// by the stride, while the noise emission is a sampling rate, and has to stay fast enough
/// that continuous movement reads as a continuous trail inside
/// <see cref="SoundPlayerDetector"/>'s retention window. Slowing the noise emission down to
/// stride speed would punch hearing gaps into every walk (AUDIO_NOTES.md 3b).
///
/// The floor gets the last word. While the player stands on a <see cref="NoisySurface"/>
/// (broken glass), that surface's per-mode ranges replace the ones configured here and its
/// step sound replaces the footstep — which is how sneaking, silent everywhere else, still
/// gives the player away on glass.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerNoiseEmitter : MonoBehaviour
{
    [Tooltip("Seconds between gameplay noise emissions while moving — what enemies get to " +
             "hear. Not the footstep cadence; that is per movement mode below. Keep this " +
             "below SoundPlayerDetector's Heard Noise Retention or enemies lose the trail " +
             "between emissions.")]
    [SerializeField] private float emitInterval = 0.2f;
    [Tooltip("Shared noise ranges asset — walking/sprinting radii are read from here.")]
    [SerializeField] private NoiseSettings noiseSettings;

    [Header("Footstep audio")]
    [Tooltip("Seconds between footstep sounds while walking.")]
    [Min(0.01f)] [SerializeField] private float walkStepInterval = 0.5f;

    [Tooltip("Seconds between footstep sounds while sprinting. Set to the length of the " +
             "footstep clip (0.25 s for wood01) so sprinting steps run back to back with " +
             "no silence between them — that gaplessness is what makes a run read as a " +
             "run. Retune it if the clip is replaced with one of a different length.")]
    [Min(0.01f)] [SerializeField] private float sprintStepInterval = 0.25f;

    [Tooltip("Seconds between footstep sounds while sneaking. Longer than walking: this is " +
             "the only feedback the player gets that they are moving carefully, since " +
             "sneaking emits no gameplay noise at all.")]
    [Min(0.01f)] [SerializeField] private float sneakStepInterval = 0.7f;

    private PlayerMovement movement;
    private PlayerSurfaceTracker surfaces;
    private float nextEmitTime;
    private float nextStepSoundTime;

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
        if (!movement.IsMoving) return;

        // Neither timer advances while standing still, so the first step after a pause
        // lands immediately rather than waiting out a cadence the player was not moving
        // through.
        if (Time.time >= nextStepSoundTime)
        {
            nextStepSoundTime = Time.time + StepSoundInterval();

            // Unconditional while moving: sneaking emits zero gameplay noise but the player
            // still hears their own careful footsteps. That asymmetry is the reason audio
            // does not simply ride along on NoiseEvents — see AUDIO_NOTES.md D1.
            AudioService.PlayAt(StepSoundId(), transform.position);
        }

        if (Time.time < nextEmitTime) return;

        nextEmitTime = Time.time + emitInterval;

        float radius = CurrentNoiseRadius();
        if (radius <= 0f) return;

        NoiseEvents.Emit(transform.position, radius);
    }

    /// <summary>
    /// How long until the next footstep sound, for the mode the player is in right now.
    ///
    /// Read when a step is scheduled rather than when it plays, so changing gait mid-stride
    /// costs at most the remainder of the step already booked instead of retiming one that
    /// is halfway done.
    /// </summary>
    private float StepSoundInterval()
    {
        switch (movement.CurrentMode)
        {
            case PlayerMovement.MovementMode.Sneak:
                return sneakStepInterval;
            case PlayerMovement.MovementMode.Sprint:
                return sprintStepInterval;
            default:
                return walkStepInterval;
        }
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
