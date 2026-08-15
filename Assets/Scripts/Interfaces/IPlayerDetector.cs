using UnityEngine;

/// <summary>
/// Decides whether the player is currently "detected" by this enemy (e.g. in sight, in hearing range).
/// Implementations: vision (range + line of sight), sound, etc. Enemy state machine uses this only as yes/no.
/// </summary>
public interface IPlayerDetector
{
    /// <summary>
    /// True if the player should be considered detected (e.g. visible, or heard). Called each frame by the enemy.
    /// </summary>
    /// <param name="player">Player transform. Can be null; implementations should return false then.</param>
    bool IsPlayerDetected(Transform player);

    /// <summary>
    /// Distance beyond which this detector can never report a detection. The enemy uses it to
    /// throttle its whole AI tick while the player is far outside every sensor it owns, so
    /// implementations must not detect anything past this range.
    /// </summary>
    float DetectionRange { get; }
}
