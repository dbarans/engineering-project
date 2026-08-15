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
}
