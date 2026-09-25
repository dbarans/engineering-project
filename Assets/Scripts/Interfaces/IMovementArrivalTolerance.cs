/// <summary>
/// Optional capability of a movement strategy: how far from the target it stops.
/// Patrol logic must treat the waypoint as "reached" at least at this distance,
/// otherwise the agent can stop short of the configured patrol threshold and never resume Idle patrol advances.
/// </summary>
public interface IMovementArrivalTolerance
{
    /// <summary>
    /// World-space distance from target at which movement effectively stops (or closest achievable).
    /// </summary>
    float StopDistanceFromTarget { get; }
}
