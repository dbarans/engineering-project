using System.Collections;
using UnityEngine;

/// <summary>
/// Drives a light like a failing, badly wired lamp: sudden flutter bursts and full blackouts
/// preceded by a browning-out dim, the way a bulb sags before it drops. Put it next to a
/// <see cref="StationaryLightSource"/>, which reads the intensity through <see cref="ILightIntensity"/>
/// and scales its lit area by it.
///
/// Every transition is a hard step, never an interpolation — a dying light snaps between states,
/// and smooth fades read as "atmospheric" rather than broken. The only gradual-looking stage is
/// the brownout, and even that is a short staircase of discrete steps.
///
/// One cycle picks its next stage at random (weighted by the chance fields), so the lamp never
/// falls into a rhythm the player can predict.
/// </summary>
public class BrokenLightFlicker : MonoBehaviour, ILightIntensity
{
    [Header("Steady Hold")]
    [Tooltip("How long the light stays at full strength between blackouts/flutters before rolling again, in seconds. The wide default range is what keeps the rhythm unpredictable: sometimes a long uneasy hold, sometimes another stage right away.")]
    [SerializeField] private Vector2 holdDurationRange = new Vector2(0.05f, 2f);

    [Header("Flutter")]
    [Tooltip("Chance per cycle that the light breaks into a rapid on/off flutter instead of a plain steady hold.")]
    [Range(0f, 1f)]
    [SerializeField] private float flutterChance = 0.25f;
    [Tooltip("How many on/off pulses one flutter burst plays.")]
    [SerializeField] private Vector2Int flutterPulseCountRange = new Vector2Int(3, 5);
    [Tooltip("How long a single on or off step of a flutter lasts, in seconds. Kept short and independent of the pulse count, so more pulses make a longer burst rather than a slower one.")]
    [SerializeField] private Vector2 flutterStepDurationRange = new Vector2(0.03f, 0.07f);

    [Header("Blackout")]
    [Tooltip("Chance per cycle that the light browns out and dies completely for a moment.")]
    [Range(0f, 1f)]
    [SerializeField] private float blackoutChance = 0.15f;
    [Tooltip("How long the light stays fully dark, in seconds.")]
    [SerializeField] private Vector2 blackoutDurationRange = new Vector2(0.1f, 0.8f);
    [Tooltip("How long the sagging dim before a blackout lasts, in seconds. Set to 0 to have the light cut out with no warning.")]
    [SerializeField] private float brownoutDuration = 0.25f;
    [Tooltip("How many discrete steps the brownout sags through on its way down. More steps read as a slower, sicklier death.")]
    [Range(1, 10)]
    [SerializeField] private int brownoutSteps = 4;

    private float intensity = 1f;

    /// <summary>How brightly the light currently burns: 0 fully out, 1 full strength.</summary>
    public float Intensity => intensity;

    private void OnEnable()
    {
        StartCoroutine(FlickerRoutine());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        // Leave the lamp at full strength, so a disabled flicker never strands it mid-blackout.
        intensity = 1f;
    }

    /// <summary>
    /// The main loop: each pass rolls for one stage — blackout, flutter, or a plain steady hold —
    /// and plays it to the end before rolling again. Rolling per cycle rather than on a fixed
    /// schedule is what makes the lamp's timing irregular.
    /// </summary>
    private IEnumerator FlickerRoutine()
    {
        while (true)
        {
            float roll = Random.value;

            if (roll < blackoutChance)
                yield return Blackout();
            else if (roll < blackoutChance + flutterChance)
                yield return Flutter();
            else
                yield return SteadyHold();
        }
    }

    /// <summary>
    /// Stage 1 — the light's resting state. Holds at full strength for a random stretch before
    /// the next stage is rolled, so the lamp doesn't lurch from one broken stage straight into
    /// another with no breathing room.
    /// </summary>
    private IEnumerator SteadyHold()
    {
        intensity = 1f;
        yield return new WaitForSeconds(Random.Range(holdDurationRange.x, holdDurationRange.y));
    }

    /// <summary>
    /// Stage 2 — a loose connection sparking back to life: several rapid full-off/full-on pulses
    /// of equal length, then back to full strength.
    /// </summary>
    private IEnumerator Flutter()
    {
        int pulses = Random.Range(flutterPulseCountRange.x, flutterPulseCountRange.y + 1);
        float stepDuration = Random.Range(flutterStepDurationRange.x, flutterStepDurationRange.y);

        for (int i = 0; i < pulses; i++)
        {
            intensity = 0f;
            yield return new WaitForSeconds(stepDuration);

            intensity = 1f;
            yield return new WaitForSeconds(stepDuration);
        }
    }

    /// <summary>
    /// Stage 3 — the light loses power. First it sags through a short staircase of ever-dimmer
    /// steps (the brownout, like a bulb browning out under a failing supply), then cuts to full
    /// darkness for a beat, then snaps straight back to full strength — recovery is always
    /// instant, which is what makes the return feel like a jolt.
    /// </summary>
    private IEnumerator Blackout()
    {
        if (brownoutDuration > 0f)
        {
            float stepDuration = brownoutDuration / brownoutSteps;
            for (int step = 0; step < brownoutSteps; step++)
            {
                // Each step drops a fixed fraction lower, reaching near-zero on the last one.
                intensity = 1f - (step + 1f) / brownoutSteps;
                yield return new WaitForSeconds(stepDuration);
            }
        }

        intensity = 0f;
        yield return new WaitForSeconds(Random.Range(blackoutDurationRange.x, blackoutDurationRange.y));

        intensity = 1f;
    }
}
