using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives the global Volume so the post-processing reacts to the game instead of sitting at fixed
/// values. A static profile makes every room look the same; the whole point of the effect is that
/// the image gets worse as the player's situation does.
///
/// Three inputs, each mapped to its own visual language so they stay tellable apart on screen:
///   • <b>Dread</b> — how far the player's health has fallen. Slow, sustained: the vignette closes
///     in and pulses like a heartbeat, colour drains, grain rises.
///   • <b>Alert</b> — at least one enemy is actively chasing. Tightens the vignette further and
///     pushes chromatic aberration, so "something is after you" reads even off-screen.
///   • <b>Master</b> — <see cref="intensity"/>, one dial to pull the whole thing back or off.
///
/// Sits on the "Global Volume" object built by <c>Tools ▸ Horror Post FX ▸ Set Up Horror
/// Post-Processing</c>. Reads <see cref="Volume.profile"/>, not <c>sharedProfile</c>, so a play
/// session never writes its runtime values back into the profile asset on disk.
///
/// Enemies are found by scan rather than injected, because the dungeon generator spawns them at
/// runtime (see GENERATION_NOTES.md) and there is nothing to wire in the scene. The scan
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

    [Header("Tracking")]
    [Tooltip("How often the scene is rescanned for enemies and the player, in seconds. Only the scan is throttled; the cached results are read every frame.")]
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
    private float baseGrain;

    private PlayerHealthSystem playerHealth;
    private EnemyBase[] trackedEnemies = new EnemyBase[0];
    private float refreshTimer;

    private float dread;
    private float alert;
    private float heartbeatPhase;

    private static readonly int HorrorIntensityId = Shader.PropertyToID("_HorrorIntensity");

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
        if (colorAdjustments != null) baseSaturation = colorAdjustments.saturation.value;
        if (filmGrain != null) baseGrain = filmGrain.intensity.value;
    }

    private void OnEnable()
    {
        RefreshTracking();
    }

    private void OnDisable()
    {
        // Leave the shader global clean, so a disabled controller cannot strand the fullscreen
        // pass at a stale intensity for the rest of the session.
        Shader.SetGlobalFloat(HorrorIntensityId, 0f);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        refreshTimer -= deltaTime;
        if (refreshTimer <= 0f) RefreshTracking();

        UpdateDread(deltaTime);
        UpdateAlert(deltaTime);

        ApplyToVolume();

        Shader.SetGlobalFloat(HorrorIntensityId, intensity);
    }

    /// <summary>
    /// Maps missing health onto <see cref="dread"/>, and advances the heartbeat phase at a rate
    /// that rises with it.
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
            float added = alert * alertChromaticAberration;
            chromaticAberration.intensity.value = Mathf.Clamp01(baseChromaticAberration + added * intensity);
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.saturation.value =
                Mathf.Clamp(baseSaturation - dread * dreadDesaturation * intensity, -100f, 100f);
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = Mathf.Clamp01(baseGrain + dread * dreadGrain * intensity);
        }
    }

    /// <summary>
    /// Rescans the scene for the player and the enemies. Runs on an interval because the dungeon
    /// generator spawns both after this component already exists, and a scene loaded from a save
    /// replaces them wholesale.
    /// </summary>
    private void RefreshTracking()
    {
        refreshTimer = trackingRefreshInterval;

        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealthSystem>();
        }

        trackedEnemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
    }
}
