/// <summary>
/// Exposes pathfinding status for higher-level AI decisions.
/// </summary>
public interface IPathStatusProvider
{
    /// <summary>
    /// True when the latest pathfinding attempt can currently reach the target.
    /// </summary>
    bool HasReachablePath { get; }

    /// <summary>
    /// Throws away the cached route and everything remembered about the last target, so the next
    /// <see cref="IMovementStrategy.Move"/> starts from scratch. Called when an agent is parked
    /// (culled far from the player): a route it will not walk for minutes is memory held for
    /// nothing, and one enemy is one list per enemy across a whole dungeon.
    /// </summary>
    void ReleaseCachedPath();
}
