using UnityEngine;

/// <summary>
/// Drives the player's two-part sprite animation, the counterpart of
/// <see cref="SkullGuyAnimationDriver"/> on the enemy side. The player is drawn as two
/// independently rotated halves — legs follow the movement direction
/// (<see cref="PlayerLegs"/>), the torso follows the aim direction (<see cref="PlayerAim"/>) —
/// so each half gets its own <see cref="SpriteFrameAnimator"/> and its own clip choice.
///
/// Legs (clip picked from movement mode):
///   sprinting -> BIEG_NOGI, otherwise CHODZENIE_NOGI
///
/// Torso (sprinting first, then what the hotbar has selected and what the weapon is doing):
///   firing                    -> CHODZENIE_TLOW_STRZAL (one-shot, plays to the end)
///   sprinting                 -> BIEG_TLOW, weapon or not
///   ranged selected, aiming   -> CHODZENIE_TLOW_BRON_CELOWANIE
///   ranged selected, carrying -> CHODZENIE_TLOW_BRON
///   otherwise                 -> CHODZENIE_TLOW
///
/// The art is all locomotion — there is no idle/stand clip — so standing still freezes the clip
/// on its first frame rather than cycling a walk loop on the spot. The legs go further and clear
/// their sprite entirely (<see cref="hideLegsWhenStanding"/>): a lone pair of boots frozen
/// mid-stride reads worse than no boots at all.
/// </summary>
public class PlayerAnimationDriver : MonoBehaviour
{
    // Clip names — must match the names written by PlayerFrameLoader.
    private const string LegsWalk = "CHODZENIE_NOGI";
    private const string LegsRun = "BIEG_NOGI";
    private const string TorsoWalk = "CHODZENIE_TLOW";
    private const string TorsoRun = "BIEG_TLOW";
    private const string TorsoWeapon = "CHODZENIE_TLOW_BRON";
    private const string TorsoAim = "CHODZENIE_TLOW_BRON_CELOWANIE";
    private const string TorsoShoot = "CHODZENIE_TLOW_STRZAL";

    [Header("References")]
    [Tooltip("Animator on the TorsoVisual child. Torso itself carries no art — it stays on the " +
             "aim axis so Direction and the weapon keep following the crosshair.")]
    [SerializeField] private SpriteFrameAnimator torso;
    [Tooltip("Animator on the Legs object (the half PlayerLegs rotates).")]
    [SerializeField] private SpriteFrameAnimator legs;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerWeaponManager weapons;

    [Header("Walk sync")]
    [Tooltip("Movement speed at which the walk clips play at their authored fps. Lower this if the feet slide forward, raise it if they slide backward.")]
    [SerializeField] private float walkSyncReferenceSpeed = 5f;
    [Tooltip("Same, for the sprint clips.")]
    [SerializeField] private float runSyncReferenceSpeed = 8f;
    [SerializeField] private float minSpeedMultiplier = 0.3f;
    [SerializeField] private float maxSpeedMultiplier = 3f;

    [Header("Standing still")]
    [Tooltip("Clear the legs' sprite while the player is not moving. There is no idle art for " +
             "the legs, so the alternative is a stride frozen mid-step. Off = hold frame 0.")]
    [SerializeField] private bool hideLegsWhenStanding = true;

    [Header("Shooting")]
    [Tooltip("Play the firing animation even when the player is standing still. The other torso clips only advance while moving.")]
    [SerializeField] private bool animateShotWhileStanding = true;

    private Rigidbody2D body;
    private float smoothedSpeed;

    private void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (weapons == null) weapons = GetComponent<PlayerWeaponManager>();
        body = movement != null ? movement.GetComponent<Rigidbody2D>() : null;
    }

    private void OnEnable()
    {
        if (weapons != null)
            weapons.WeaponFired += TriggerShot;
    }

    private void OnDisable()
    {
        if (weapons != null)
            weapons.WeaponFired -= TriggerShot;
    }

    /// <summary>
    /// Plays the firing animation on the torso. Subscribed to
    /// <see cref="PlayerWeaponManager.WeaponFired"/>; only the ranged weapon has firing art.
    /// </summary>
    public void TriggerShot()
    {
        if (torso == null) return;
        if (weapons == null || weapons.ActiveWeaponType != WeaponType.Ranged) return;

        torso.Play(TorsoShoot, true);
        if (animateShotWhileStanding)
            torso.Paused = false;
    }

    private void Update()
    {
        bool moving = movement != null && movement.IsMoving;
        bool sprinting = movement != null && movement.IsSprinting;

        UpdateSmoothedSpeed();
        float tempo = SpeedMultiplierFor(sprinting);

        DriveLegs(moving, sprinting, tempo);
        DriveTorso(moving, sprinting, tempo);
    }

    private void DriveLegs(bool moving, bool sprinting, float tempo)
    {
        if (legs == null) return;

        legs.Play(sprinting ? LegsRun : LegsWalk);
        legs.SpeedMultiplier = tempo;
        legs.Hidden = hideLegsWhenStanding && !moving;
        SetAnimating(legs, moving);
    }

    private void DriveTorso(bool moving, bool sprinting, float tempo)
    {
        if (torso == null) return;

        // A shot in progress owns the torso until it reaches its last frame.
        if (torso.IsPlaying(TorsoShoot) && !torso.IsFinished)
        {
            torso.SpeedMultiplier = 1f;
            if (animateShotWhileStanding)
                torso.Paused = false;
            return;
        }

        torso.Play(TorsoClip(sprinting));
        torso.SpeedMultiplier = tempo;
        SetAnimating(torso, moving);
    }

    /// <summary>
    /// Which torso clip the current state calls for.
    ///
    /// Sprinting outranks the loadout: the run cycle plays whether or not a weapon is selected.
    /// There is no armed run art, so the player is drawn empty-handed while sprinting — the two
    /// never overlap in practice anyway, since <see cref="PlayerInputHandler"/> refuses to start
    /// a charge while sprinting and refuses to start a sprint while aiming.
    /// </summary>
    private string TorsoClip(bool sprinting)
    {
        if (sprinting)
            return TorsoRun;

        bool ranged = weapons != null && weapons.ActiveWeaponType == WeaponType.Ranged;
        if (!ranged)
            return TorsoWalk;

        bool aiming = weapons.ActiveWeapon != null && weapons.ActiveWeapon.IsCharging;
        return aiming ? TorsoAim : TorsoWeapon;
    }

    /// <summary>
    /// Runs or freezes a body part. Freezing snaps back to the clip's first frame so the
    /// player stands in a neutral pose instead of holding a mid-stride frame.
    /// </summary>
    private static void SetAnimating(SpriteFrameAnimator animator, bool animate)
    {
        if (animate)
        {
            animator.Paused = false;
        }
        else if (!animator.Paused)
        {
            animator.Rewind();
            animator.Paused = true;
        }
    }

    /// <summary>
    /// Scales clip playback with actual movement speed so the stride matches the ground speed
    /// (no foot sliding), the same trick <see cref="SkullGuyAnimationDriver"/> uses.
    /// </summary>
    private float SpeedMultiplierFor(bool sprinting)
    {
        float reference = sprinting ? runSyncReferenceSpeed : walkSyncReferenceSpeed;
        if (reference <= 0.01f) return 1f;
        return Mathf.Clamp(smoothedSpeed / reference, minSpeedMultiplier, maxSpeedMultiplier);
    }

    private void UpdateSmoothedSpeed()
    {
        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 10f * Time.deltaTime);
    }
}
