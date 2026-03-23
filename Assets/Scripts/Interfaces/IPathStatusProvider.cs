/// <summary>
/// Exposes pathfinding status for higher-level AI decisions.
/// </summary>
public interface IPathStatusProvider
{
    /// <summary>
    /// True when the latest pathfinding attempt can currently reach the target.
    /// </summary>
    bool HasReachablePath { get; }
}
