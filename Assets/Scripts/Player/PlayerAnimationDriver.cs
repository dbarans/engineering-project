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
///   attacking                 -> the weapon's attack clip (one-shot, plays to the end)
///   sprinting                 -> BIEG_TLOW, weapon or not
///   weapon selected, charging -> the weapon's aim clip
///   weapon selected, carrying -> the weapon's carry clip
///   otherwise                 -> CHODZENIE_TLOW
///
/// Each weapon brings its own carry/aim/attack triple (see <see cref="WeaponAnimationSet"/>),
/// because how the player holds a pistol, a shotgun and an axe are three different poses.
/// The weapon component itself says which triple it wants, so two weapons of the same
/// <see cref="WeaponType"/> — pistol and shotgun — still animate apart.
///
/// The axe's aim clip is a wind-up rather than a cycle, so it is scrubbed by charge progress
/// instead of played: it advances only while the prepare button is held, holds its last frame
/// once the swing is fully charged, and is dropped the moment the player swings or lets go.
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

    /// <summary>The three torso clips one weapon needs: carried, brought up to aim, and used.</summary>
    private readonly struct WeaponClips
    {
        public readonly string carry;
        public readonly string aim;
        public readonly string attack;

        /// <summary>
        /// Whether the aim clip is a wind-up that tracks the charge instead of a walk cycle.
        /// The guns' CELOWANIE clips are walk cycles with the weapon raised, so they loop with
        /// the stride; the axe's NAPIECIE is a single pull-back, scrubbed by charge progress so
        /// it lands on its last frame exactly when the swing is fully charged and holds there.
        /// </summary>
        public readonly bool aimFollowsCharge;

        public WeaponClips(string carry, string aim, string attack, bool aimFollowsCharge = false)
        {
            this.carry = carry;
            this.aim = aim;
            this.attack = attack;
            this.aimFollowsCharge = aimFollowsCharge;
        }
    }

    /// <summary>
    /// Clips per <see cref="WeaponAnimationSet"/>, indexed by the enum value. The pistol's
    /// folders were exported before the other two and do not follow their
    /// CHODZENIE_TLOW_&lt;weapon&gt;_&lt;state&gt; naming — its shot clip is CHODZENIE_TLOW_STRZAL
    /// with no BRON in the middle — so the names are listed rather than built from a prefix.
    /// </summary>
    private static readonly WeaponClips[] ClipSets =
    {
        // Pistol
        new WeaponClips("CHODZENIE_TLOW_BRON", "CHODZENIE_TLOW_BRON_CELOWANIE", "CHODZENIE_TLOW_STRZAL"),
        // Shotgun
        new WeaponClips("CHODZENIE_TLOW_STRZELBA", "CHODZENIE_TLOW_STRZELBA_CELOWANIE", "CHODZENIE_TLOW_STRZELBA_STRZAL"),
        // Axe — NAPIECIE (wind-up) stands in for the aim pose, STRZAL for the swing.
        new WeaponClips("CHODZENIE_TLOW_AXE", "CHODZENIE_TLOW_AXE_NAPIECIE", "CHODZENIE_TLOW_AXE_STRZAL",
                        aimFollowsCharge: true),
    };

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

    /// <summary>
    /// Attack clip currently owning the torso, or null. Remembered rather than re-derived from
    /// the equipped weapon so a swap mid-swing lets the swing finish instead of cutting to the
    /// new weapon's carry pose halfway through.
    /// </summary>
    private string playingAttackClip;

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
    /// Plays the attack animation of whatever is equipped — a shot for the guns, a swing for
    /// the axe. Subscribed to <see cref="PlayerWeaponManager.WeaponFired"/>.
    /// </summary>
    public void TriggerShot()
    {
        PlayerAttack weapon = weapons != null ? weapons.ActiveWeapon : null;
        if (torso == null || weapon == null) return;

        playingAttackClip = ClipsFor(weapon).attack;
        torso.Play(playingAttackClip, true);
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

        // An attack in progress owns the torso until it reaches its last frame.
        if (playingAttackClip != null && torso.IsPlaying(playingAttackClip) && !torso.IsFinished)
        {
            torso.SpeedMultiplier = 1f;
            if (animateShotWhileStanding)
                torso.Paused = false;
            return;
        }
        playingAttackClip = null;

        // A wind-up is a readout of the charge, not something playing on its own clock: it runs
        // while the prepare button is held whether or not the player is walking, and stops dead
        // the moment they let go or swing.
        PlayerAttack winding = sprinting ? null : ChargingWeaponWithWindUp();
        if (winding != null)
        {
            torso.Play(ClipsFor(winding).aim);
            torso.Scrub(winding.GetChargeProgress());
            return;
        }

        torso.Play(TorsoClip(sprinting));
        torso.SpeedMultiplier = tempo;
        SetAnimating(torso, moving);
    }

    /// <summary>
    /// The equipped weapon if it is mid-charge and its aim clip is a wind-up, otherwise null.
    /// </summary>
    private PlayerAttack ChargingWeaponWithWindUp()
    {
        PlayerAttack weapon = weapons != null ? weapons.ActiveWeapon : null;
        if (weapon == null || !weapon.IsCharging) return null;
        return ClipsFor(weapon).aimFollowsCharge ? weapon : null;
    }

    /// <summary>
    /// Which torso clip the current state calls for.
    ///
    /// Sprinting outranks the loadout: the run cycle plays whether or not a weapon is selected.
    /// There is no armed run art, so the player is drawn empty-handed while sprinting — the two
    /// never overlap in practice anyway, since <see cref="PlayerInputHandler"/> refuses to start
    /// a charge while sprinting and refuses to start a sprint while aiming.
    ///
    /// Anything that is not a weapon (a torch, a bandage) equips no attack component and so
    /// falls through to the empty-handed walk.
    /// </summary>
    private string TorsoClip(bool sprinting)
    {
        if (sprinting)
            return TorsoRun;

        PlayerAttack weapon = weapons != null ? weapons.ActiveWeapon : null;
        if (weapon == null)
            return TorsoWalk;

        WeaponClips clips = ClipsFor(weapon);
        return weapon.IsCharging ? clips.aim : clips.carry;
    }

    /// <summary>
    /// Clips for a weapon's animation set. Falls back to the pistol's set for an out-of-range
    /// value, so a set added to the enum without art here still animates instead of freezing.
    /// </summary>
    private static WeaponClips ClipsFor(PlayerAttack weapon)
    {
        int index = (int)weapon.AnimationSet;
        return index >= 0 && index < ClipSets.Length ? ClipSets[index] : ClipSets[0];
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
