using UnityEngine;

/// <summary>
/// Drives a light like a flame: a constant, gentle wobble with no hard cuts. Put one next to a
/// <see cref="StationaryLightSource"/> (an oil lamp) or a <see cref="HeldTorch"/> (the player's
/// torch), never two, since the light reads a single <see cref="ILightIntensity"/>.
///
/// This is the only way a light in this game wavers: nothing here runs on electricity, so every
/// light on screen is something burning, and every one of them wobbles rather than flickers or
/// cuts out. A torch takes a stronger, faster wobble than a lamp sheltered behind glass — that
/// difference is all in <see cref="strength"/> and <see cref="speed"/>, not in the code.
///
/// Uses Perlin noise rather than a sine so the wobble wanders instead of pulsing on a beat.
/// </summary>
public class FlameFlicker : MonoBehaviour, ILightIntensity
{
    [Tooltip("How much the light dims at the low point of the wobble. 0 = steady, 1 = can dim to fully off.")]
    [Range(0f, 1f)]
    [SerializeField] private float strength = 0.1f;
    [Tooltip("How fast the wobble wanders, in cycles/second.")]
    [SerializeField] private float speed = 1.5f;

    // Random per lamp, so several flames in view never wobble in lockstep.
    private float noiseSeed;

    /// <summary>How brightly the light currently burns: 0 fully out, 1 full strength.</summary>
    public float Intensity => 1f - strength * Mathf.PerlinNoise(noiseSeed, Time.time * speed);

    private void Awake()
    {
        noiseSeed = Random.value * 1000f;
    }
}
