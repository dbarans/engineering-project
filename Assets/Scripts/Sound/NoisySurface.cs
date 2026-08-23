using UnityEngine;

/// <summary>
/// A patch of floor that gives the player away when they walk over it — broken glass by
/// default (see <c>Tools ▸ World ▸ Build Broken Glass Prefab</c>).
///
/// The stealth point of the surface is that it overrides <see cref="PlayerNoiseEmitter"/>'s
/// own movement-mode ranges, including the one case that matters most: sneaking is normally
/// completely silent (radius 0), and on glass it is not. Picking your way over shards
/// carefully is quieter than running across them, but it is never free — so a glass patch
/// turns a corridor the player could previously cross unheard into a decision.
///
/// Ranges are expressed as multipliers of <see cref="baseNoiseSettings"/> rather than as
/// absolute numbers, so that "glass is louder than ordinary floor" is guaranteed by
/// construction instead of by whoever last tuned two unrelated fields. Walk and sprint
/// multiply their own ordinary radius and are clamped to at least 1x; sneak has no ordinary
/// radius to multiply (it is 0 by design), so it instead takes a fraction of the ordinary
/// walk radius — which is still strictly louder than the silence it replaces for any
/// multiplier above 0.
///
/// Implemented as an override handed to the player rather than as a noise source of its
/// own, because the cadence question ("how often does crossing this floor make a sound")
/// is already answered by the footstep timer in <see cref="PlayerNoiseEmitter"/>. A patch
/// emitting on its own timer would double up with the footsteps underneath it and drift
/// out of step with the player's stride.
///
/// Registration is push-based: the trigger lives here, on the patch, so the player needs no
/// scanning of its surroundings — see <see cref="PlayerSurfaceTracker"/> for what happens
/// when patches overlap.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class NoisySurface : MonoBehaviour
{
    [Tooltip("Tag of the entity this surface reacts to. Enemies walk over glass too, but " +
             "an enemy betraying its own position to itself is not a mechanic.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Noise multipliers")]
    [Tooltip("Baseline this surface multiplies — assign the same asset PlayerNoiseEmitter " +
             "uses. Without one, this surface logs a warning and falls back to silence " +
             "rather than guessing at absolute numbers.")]
    [SerializeField] private NoiseSettings baseNoiseSettings;

    [Tooltip("Walking on this surface carries this many times farther than an ordinary " +
             "walking step. Clamped to at least 1x, so glass can never be quieter than " +
             "plain floor.")]
    [Min(1f)] [SerializeField] private float walkLoudnessMultiplier = 1.5f;

    [Tooltip("Sprinting on this surface carries this many times farther than an ordinary " +
             "sprinting step — running over shards is the loudest thing the player can do " +
             "short of firing a gun. Clamped to at least 1x.")]
    [Min(1f)] [SerializeField] private float sprintLoudnessMultiplier = 1.3f;

    [Tooltip("Sneaking is silent (radius 0) on ordinary floor, so there is nothing to " +
             "multiply for it. This is instead a fraction of the ordinary WALK radius: " +
             "the whole reason this surface exists is that sneaking here is no longer " +
             "free, while staying quieter than walking is what keeps sneaking worth doing.")]
    [Range(0.01f, 1f)] [SerializeField] private float sneakLoudnessMultiplier = 0.6f;

    [Header("Audio")]
    [Tooltip("Sound played instead of the ordinary footstep while the player is on this " +
             "surface. Ids come from SoundId.")]
    [SerializeField] private string stepSoundId = SoundId.SurfaceGlassStep;

    /// <summary>Sound to play in place of the regular footstep while standing here.</summary>
    public string StepSoundId => stepSoundId;

    /// <summary>
    /// Noise radius for the given movement mode. Returns 0 (ordinary silence) only when
    /// <see cref="baseNoiseSettings"/> is unassigned — there is otherwise no configuration
    /// that can make this surface quieter than plain floor, by construction.
    /// </summary>
    public float NoiseRadiusFor(PlayerMovement.MovementMode mode)
    {
        if (baseNoiseSettings == null)
        {
            Debug.LogWarning($"[NoisySurface] '{name}' has no Base Noise Settings assigned " +
                              "— treating it as silent instead of guessing at a radius.", this);
            return 0f;
        }

        switch (mode)
        {
            case PlayerMovement.MovementMode.Sneak:
                return baseNoiseSettings.walkNoiseRadius * sneakLoudnessMultiplier;
            case PlayerMovement.MovementMode.Sprint:
                return baseNoiseSettings.sprintNoiseRadius * Mathf.Max(1f, sprintLoudnessMultiplier);
            default:
                return baseNoiseSettings.walkNoiseRadius * Mathf.Max(1f, walkLoudnessMultiplier);
        }
    }

    private void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    /// <summary>
    /// Tuning aid: the three ranges this patch gives the player, drawn only while it is
    /// selected. Selection-scoped rather than behind a <see cref="GizmoDebugSettings"/>
    /// flag because a dungeon holds dozens of patches — drawn unconditionally they would
    /// bury every other gizmo in the scene.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (baseNoiseSettings == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, NoiseRadiusFor(PlayerMovement.MovementMode.Sneak));
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, NoiseRadiusFor(PlayerMovement.MovementMode.Walk));
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, NoiseRadiusFor(PlayerMovement.MovementMode.Sprint));
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        var tracker = PlayerSurfaceTracker.For(other);
        if (tracker != null) tracker.Enter(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        var tracker = PlayerSurfaceTracker.For(other);
        if (tracker != null) tracker.Exit(this);
    }

    /// <summary>
    /// A patch destroyed or disabled while the player is standing on it would otherwise
    /// never raise its exit, leaving the player permanently loud on ordinary floor.
    /// </summary>
    private void OnDisable()
    {
        PlayerSurfaceTracker.ForgetEverywhere(this);
    }
}
