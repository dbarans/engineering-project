using UnityEngine;

/// <summary>
/// Single shared configuration for how far each kind of gameplay noise carries
/// (the emission radius passed to <see cref="NoiseEvents.Emit"/>). One asset drives every
/// noise source — player footsteps, thrown rocks, gunfire — so all noise ranges can be
/// tuned in one place. Enemy hearing range is a separate, per-enemy value on
/// <see cref="SoundPlayerDetector"/>; a noise is heard only within the smaller of the two.
/// </summary>
[CreateAssetMenu(fileName = "NoiseSettings", menuName = "Audio/Noise Settings")]
public class NoiseSettings : ScriptableObject
{
    [Header("Player movement")]
    [Tooltip("How far walking footsteps carry (world units).")]
    public float walkNoiseRadius = 4f;
    [Tooltip("How far sprinting footsteps carry (world units).")]
    public float sprintNoiseRadius = 8f;

    [Header("Thrown rock")]
    [Tooltip("How far the landing noise of a thrown rock carries (world units).")]
    public float throwLandingRadius = 10f;

    [Header("Ranged weapon")]
    [Tooltip("How far the sound of firing carries (world units).")]
    public float shootNoiseRadius = 12f;
}
