using UnityEngine;

/// <summary>
/// Provides the enemy state machine with the most recent heard noise. Unlike
/// <see cref="IPlayerDetector"/>, a heard noise is only a suspicion — the enemy knows
/// where the sound came from, not where the player is — and drives investigation
/// rather than an immediate chase.
/// </summary>
public interface INoiseSensor
{
    /// <summary>True while a recently heard noise is still fresh enough to react to.</summary>
    bool HasFreshNoise { get; }

    /// <summary>World position of the most recent heard noise. Valid only while HasFreshNoise is true.</summary>
    Vector2 LastNoisePosition { get; }
}
