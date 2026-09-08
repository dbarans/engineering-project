using UnityEngine;

/// <summary>
/// Optional companion to <see cref="IMovementStrategy"/> for strategies backed by a navigation
/// grid. Lets AI code check a candidate destination before committing to it, instead of finding
/// out it was inside a wall by running a search that fails.
/// </summary>
public interface IWalkabilityProbe
{
    /// <summary>
    /// True when an agent can stand at this world position. Implementations without navigation
    /// data should return true rather than block callers.
    /// </summary>
    bool IsWalkable(Vector2 worldPosition);

    /// <summary>
    /// Size of one navigation sample, in world units - the grain of everything this probe can
    /// tell an AI. Callers need it for two things: how far apart two probes have to be to land
    /// on different cells, and how close to a requested point the strategy can actually stop,
    /// since a grid route ends on a cell centre rather than on the point that was asked for.
    /// 0 when the implementation has no grid behind it.
    /// </summary>
    float WalkableSampleSize { get; }
}
