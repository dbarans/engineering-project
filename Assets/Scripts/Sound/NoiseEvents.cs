using System;
using UnityEngine;

/// <summary>
/// Global event bus for gameplay noises. Anything that makes noise (player footsteps,
/// thrown objects, doors, gunfire) calls <see cref="Emit"/>; listeners such as
/// <see cref="SoundPlayerDetector"/> subscribe to <see cref="NoiseEmitted"/> and decide
/// on their own whether the noise was close enough to hear.
/// </summary>
public static class NoiseEvents
{
    /// <summary>
    /// Raised for every emitted noise. Arguments: world position of the noise and its
    /// radius — the maximum distance at which the noise is audible.
    /// </summary>
    public static event Action<Vector2, float> NoiseEmitted;

    /// <summary>
    /// Broadcasts a noise to all listeners. Noises with a non-positive radius are ignored.
    /// </summary>
    /// <param name="position">World position where the noise originated.</param>
    /// <param name="radius">Maximum distance at which the noise is audible.</param>
    public static void Emit(Vector2 position, float radius)
    {
        if (radius <= 0f) return;
        NoiseEmitted?.Invoke(position, radius);
    }

    /// <summary>
    /// Clears stale subscribers on play mode start (needed when domain reload is disabled,
    /// as static events would otherwise keep references from the previous session).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSubscribers()
    {
        NoiseEmitted = null;
    }
}
