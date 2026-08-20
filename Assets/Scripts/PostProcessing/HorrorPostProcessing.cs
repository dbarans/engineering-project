using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives the global Volume so the post-processing reacts to the game instead of sitting at fixed
/// values. A static profile makes every room look the same; the whole point of the effect is that
/// the image gets worse as the player's situation does.
///
/// Four inputs, each mapped to its own visual language so they stay tellable apart on screen:
///   • <b>Dread</b> — how far the player's health has fallen. Slow, sustained: the vignette closes
///     in and pulses like a heartbeat, colour drains, grain rises.
///   • <b>Alert</b> — at least one enemy is actively chasing. Tightens the vignette further and
///     pushes chromatic aberration, so "something is after you" reads even off-screen.
///   • <b>Glitch</b> — short bursts on taking damage or on a nearby lamp blacking out. Tearing and
///     colour split, via the <c>_HorrorGlitch</c> global that HorrorFullScreen.shader reads.
///   • <b>Master</b> — <see cref="intensity"/>, one dial to pull the whole thing back or off.
///
/// Sits on the "Global Volume" object built by <c>Tools ▸ Horror Post FX ▸ Set Up Horror
/// Post-Processing</c>. Reads <see cref="Volume.profile"/>, not <c>sharedProfile</c>, so a play
/// session never writes its runtime values back into the profile asset on disk.
///
/// Enemies and lamps are found by scan rather than injected, because the dungeon generator spawns
/// both at runtime (see GENERATION_NOTES.md) and there is nothing to wire in the scene. The scan
/// is throttled to <see cref="trackingRefreshInterval"/>; only the cached arrays are read per frame.
/// </summary>
[RequireComponent(typeof(Volume))]
public class HorrorPostProcessing : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Scales the whole effect. 0 leaves the frame untouched, 1 is the tuned look.")]
    [Range(0f, 1f)]
    [SerializeField] private float intensity = 1f;

    [Header("Dread — driven by missing health")]
    [Tooltip("Health fraction at which dread starts building. Above this the player is 'fine' and the screen stays calm.")]
    [Range(0f, 1f)]
    [SerializeField] private float dreadHealthThreshold = 0.6f;
    [Tooltip("How much extra vignette full dread adds on top of the profile's own value.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float dreadVignette = 0.18f;
    [Tooltip("How much saturation full dread removes, in Color Adjustments units.")]
    [Range(0f, 100f)]
    [SerializeField] private float dreadDesaturation = 35f;
    [Tooltip("How much film grain full dread adds.")]
    [Range(0f, 1f)]
    [SerializeField] private float dreadGrain = 0.25f;

    [Header("Dread — heartbeat")]
    [Tooltip("Heartbeat rate at the moment dread starts, in beats per minute.")]
    [SerializeField] private float heartbeatMinBpm = 60f;
    [Tooltip("Heartbeat rate at zero health, in beats per minute.")]
    [SerializeField] private float heartbeatMaxBpm = 150f;
    [Tooltip("How much vignette a single beat adds at full dread.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float heartbeatVignette = 0.12f;

    [Header("Alert — driven by enemies chasing")]
    [Tooltip("How fast the alert look fades in once a chase starts, and back out once every chase ends. Seconds to travel the full range.")]
    [SerializeField] private float alertBlendDuration = 0.6f;
    [Tooltip("How much extra vignette a chase adds.")]
    [Range(0f, 0.6f)]
    [SerializeField] private float alertVignette = 0.12f;
    [Tooltip("How much chromatic aberration a chase adds.")]
    [Range(0f, 1f)]
    [SerializeField] private float alertChromaticAberration = 0.35f;

    [Header("Glitch bursts")]
    [Tooltip("Glitch strength of a single burst, before it starts decaying.")]
    [Range(0f, 1f)]
    [SerializeField] private float glitchBurstStrength = 1f;
    [Tooltip("How long a burst takes to decay back to nothing, in seconds.")]
    [SerializeField] private float glitchDecayDuration = 0.45f;
    [Tooltip("How much chromatic aberration a full-strength burst adds.")]
    [Range(0f, 1f)]
    [SerializeField] private float glitchChromaticAberration = 0.4f;
    [Tooltip("How many stops a full-strength burst darkens the frame by. The dip is what sells a burst as a power problem rather than a camera effect.")]
    [Range(0f, 3f)]
    [SerializeField] private float glitchExposureDrop = 0.7f;
    [Tooltip("How close a lamp has to be, in world units, for its blackout to glitch the screen. A lamp dying three rooms away is not the player's problem.")]
    [SerializeField] private float blackoutGlitchRadius = 12f;

    [Header("Tracking")]
    [Tooltip("How often the scene is rescanned for enemies, lamps and the player, in seconds. Only the scan is throttled; the cached results are read every frame.")]
    [SerializeField] private float trackingRefreshInterval = 2f;

    // Volume overrides, resolved once in Awake. Any of these may legitimately be absent if the
    // profile does not carry that override — every use is guarded rather than assumed.
    private Vignette vignette;
    private ChromaticAberration chromaticAberration;
    private ColorAdjustments colorAdjustments;
    private FilmGrain filmGrain;

    // The profile's authored values, captured before the first frame writes over them. Every
    // per-frame value is baseline + reaction, so re-tuning the profile asset keeps working.
    private float baseVignette;
    private float baseChromaticAberration;
    private float baseSaturation;
    private float basePostExposure;
    private float baseGrain;

    private PlayerHealthSystem playerHealth;
    private Transform playerTransform;
    private EnemyBase[] trackedEnemies = new EnemyBase[0];
    private BrokenLightFlicker[] trackedLights = new BrokenLightFlicker[0];
    private bool[] lightWasLit = new bool[0];
    private float refreshTimer;

    private float dread;
    private float alert;
    private float glitch;
    private float heartbeatPhase;
    private int lastSeenHealth = int.MinValue;

    private static readonly int HorrorIntensityId = Shader.PropertyToID("_HorrorIntensity");
    private static readonly int HorrorGlitchId = Shader.PropertyToID("_HorrorGlitch");

    /// <summary>
    /// Kicks off a glitch burst — tearing, colour split and a brief exposure dip. Public so
    /// anything with a reason to rattle the image (a scripted scare, a door being broken down)
    /// can trigger one without this class having to know about it.
    /// </summary>
    /// <param name="strength">0..1, scaled against the tuned burst strength.</param>
    public void TriggerGlitch(float strength = 1f)
    {
        glitch = Mathf.Max(glitch, glitchBurstStrength * Mathf.Clamp01(strength));
    }

    private void Awake()
    {
        var volume = GetComponent<Volume>();

        // profile, not sharedProfile: the getter hands back a runtime copy, so the values written
        // below are thrown away on exit instead of being saved into the asset.
        VolumeProfile profile = volume.profile;
        if (profile == null) return;

        profile.TryGet(out vignette);
        profile.TryGet(out chromaticAberration);
        profile.TryGet(out colorAdjustments);
        profile.TryGet(out filmGrain);

        if (vignette != null) baseVignette = vignette.intensity.value;
        if (chromaticAberration != null) baseChromaticAberration = chromaticAberration.intensity.value;
        if (colorAdjustments != null)
        {
            baseSaturation = colorAdjustments.saturation.value;
            basePostExposure = colorAdjustments.postExposure.value;
        }
        if (filmGrain != null) baseGrain = filmGrain.intensity.value;
    }

    private void OnEnable()
    {
        RefreshTracking();
    }

    private void OnDisable()
    {
        // Leave the shader globals clean, so a disabled controller cannot strand the fullscreen
        // pass mid-glitch for the rest of the session.
        Shader.SetGlobalFloat(HorrorIntensityId, 0f);
        Shader.SetGlobalFloat(HorrorGlitchId, 0f);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        refreshTimer -= deltaTime;
        if (refreshTimer <= 0f) RefreshTracking();

        UpdateDread(deltaTime);
        UpdateAlert(deltaTime);
        UpdateGlitch(deltaTime);

        ApplyToVolume();

        Shader.SetGlobalFloat(HorrorIntensityId, intensity);
        Shader.SetGlobalFloat(HorrorGlitchId, glitch * intensity);
    }

    /// <summary>
    /// Maps missing health onto <see cref="dread"/>, and advances the heartbeat phase at a rate
    /// that rises with it. Also watches for the health going down at all, which is the damage
    /// signal — <see cref="PlayerHealthSystem"/> raises no event, so this is polled.
    /// </summary>
    private void UpdateDread(float deltaTime)
    {
        if (playerHealth == null)
        {
            dread = 0f;
            return;
        }

        int currentHealth = playerHealth.GetCurrentHealth();
        int maxHealth = Mathf.Max(1, playerHealth.GetMaxHealth());

        // int.MinValue means "first frame": there is no previous value to have dropped from, and
        // treating it as one would glitch the screen the moment the game starts.
        if (lastSeenHealth != int.MinValue && currentHealth < lastSeenHealth) TriggerGlitch();
        lastSeenHealth = currentHealth;

        float healthFraction = Mathf.Clamp01(currentHealth / (float)maxHealth);
        dread = dreadHealthThreshold <= 0f
            ? 0f
            : Mathf.Clamp01((dreadHealthThreshold - healthFraction) / dreadHealthThreshold);

        float beatsPerSecond = Mathf.Lerp(heartbeatMinBpm, heartbeatMaxBpm, dread) / 60f;
        heartbeatPhase = Mathf.Repeat(heartbeatPhase + deltaTime * beatsPerSecond, 1f);
    }

    /// <summary>
    /// Blends <see cref="alert"/> toward 1 while any tracked enemy is chasing, and back to 0 once
    /// none is. Blended rather than switched: a hard cut on the vignette reads as a rendering bug.
    /// </summary>
    private void UpdateAlert(float deltaTime)
    {
        bool anyChasing = false;
        for (int i = 0; i < trackedEnemies.Length; i++)
        {
            EnemyBase enemy = trackedEnemies[i];
            if (enemy == null || enemy.IsDead) continue;
            if (enemy.CurrentState != EnemyState.FollowPlayer) continue;

            anyChasing = true;
            break;
        }

        float step = alertBlendDuration > 0f ? deltaTime / alertBlendDuration : 1f;
        alert = Mathf.MoveTowards(alert, anyChasing ? 1f : 0f, step);
    }

    /// <summary>
    /// Decays any running burst, and fires a new one when a lamp near the player drops to full
    /// dark. The lit/unlit edge is what matters, not the level — <see cref="BrokenLightFlicker"/>
    /// steps its intensity every few frames, so reacting to the value itself would glitch the
    /// screen continuously under a flickering lamp.
    /// </summary>
    private void UpdateGlitch(float deltaTime)
    {
        for (int i = 0; i < trackedLights.Length; i++)
        {
            BrokenLightFlicker light = trackedLights[i];
            if (light == null)
            {
                lightWasLit[i] = false;
                continue;
            }

            bool isLit = light.Intensity > 0f;
            bool wentDark = lightWasLit[i] && !isLit;
            lightWasLit[i] = isLit;

            if (!wentDark || playerTransform == null) continue;

            float sqrDistance = ((Vector2)(light.transform.position - playerTransform.position)).sqrMagnitude;
            if (sqrDistance <= blackoutGlitchRadius * blackoutGlitchRadius) TriggerGlitch();
        }

        float decayStep = glitchDecayDuration > 0f ? deltaTime / glitchDecayDuration : 1f;
        glitch = Mathf.MoveTowards(glitch, 0f, decayStep);
    }

    /// <summary>
    /// Writes the combined state onto the Volume overrides. Every write is baseline + reaction,
    /// scaled by the master <see cref="intensity"/>, and clamped to the range each override
    /// actually accepts.
    /// </summary>
    private void ApplyToVolume()
    {
        // A short, sharp thump rather than a sine: raising a clipped sine to a high power leaves a
        // beat that is mostly silence, which is how a heartbeat is felt.
        float heartbeat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(heartbeatPhase * Mathf.PI * 2f)), 6f);

        if (vignette != null)
        {
            float added = dread * dreadVignette
                        + alert * alertVignette
                        + dread * heartbeat * heartbeatVignette;
            vignette.intensity.value = Mathf.Clamp01(baseVignette + added * intensity);
        }

        if (chromaticAberration != null)
        {
            float added = alert * alertChromaticAberration + glitch * glitchChromaticAberration;
            chromaticAberration.intensity.value = Mathf.Clamp01(baseChromaticAberration + added * intensity);
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.saturation.value =
                Mathf.Clamp(baseSaturation - dread * dreadDesaturation * intensity, -100f, 100f);
            colorAdjustments.postExposure.value =
                basePostExposure - glitch * glitchExposureDrop * intensity;
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = Mathf.Clamp01(baseGrain + dread * dreadGrain * intensity);
        }
    }

    /// <summary>
    /// Rescans the scene for the player, the enemies and the breakable lamps. Runs on an interval
    /// because the dungeon generator spawns all three after this component already exists, and a
    /// scene loaded from a save replaces them wholesale.
    /// </summary>
    private void RefreshTracking()
    {
        refreshTimer = trackingRefreshInterval;

        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealthSystem>();
            // lastSeenHealth is deliberately not reset when the player is re-found: carrying the
            // last known value across a respawn reads as health going up, never down, so a
            // respawn cannot fire a false damage burst.
            if (playerHealth != null) playerTransform = playerHealth.transform;
        }

        trackedEnemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);

        var lights = FindObjectsByType<BrokenLightFlicker>(FindObjectsSortMode.None);
        if (lights.Length != trackedLights.Length)
        {
            // The lit-state array is rebuilt alongside the lamp array, seeded from the current
            // state: a lamp that appears mid-blackout must not count as having just gone dark.
            var rebuiltLitState = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++) rebuiltLitState[i] = lights[i].Intensity > 0f;
            lightWasLit = rebuiltLitState;
        }
        trackedLights = lights;
    }
}
