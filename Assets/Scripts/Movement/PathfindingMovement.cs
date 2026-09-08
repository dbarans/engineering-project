using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Movement strategy using pathfinding algorithm.
/// Uses A* over PathfindingGrid and follows calculated waypoints.
/// </summary>
public class PathfindingMovement : MonoBehaviour, IMovementStrategy, IPathStatusProvider, IMovementArrivalTolerance, IWalkabilityProbe
{
    [SerializeField] private PathfindingGrid grid;
    [SerializeField] private float repathInterval = 0.25f;
    [SerializeField] private float waypointReachDistance = 0.1f;
    [SerializeField] private float targetRepathDistance = 0.5f;
    [Tooltip("Cap on cells one search may expand before giving up and following the closest partial route. Bounds the worst case on a large grid.")]
    [SerializeField] private int maxExploredNodes = AStarPathfinder.DefaultMaxExploredNodes;
    [Tooltip("Cooldown after a search that found no route to the target. Without it an unreachable target re-runs a failed search every single frame.")]
    [SerializeField] private float failedPathRetryInterval = 0.75f;
    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private Color pathColor = Color.yellow;

    private readonly List<Vector2> currentPath = new List<Vector2>();
    private int currentPathIndex;
    private float nextRepathTime;
    private float nextFailedRetryTime;
    private Vector2 lastTarget;
    private bool hasLastTarget;
    private bool hasReachablePath = true;

    /// <inheritdoc />
    public float StopDistanceFromTarget => waypointReachDistance;

    /// <summary>
    /// True when the latest pathfinding attempt produced a reachable route.
    /// </summary>
    public bool HasReachablePath => hasReachablePath;

    /// <inheritdoc />
    public void ReleaseCachedPath()
    {
        if (currentPath.Count == 0 && currentPath.Capacity == 0) return;

        currentPath.Clear();
        // Clear() keeps the capacity - the whole point here is to hand the memory back, and a
        // route across a 200x200 dungeon is not a small list.
        currentPath.TrimExcess();
        currentPathIndex = 0;

        // The next Move must re-path rather than compare against a target it no longer has a
        // route to.
        hasLastTarget = false;
        hasReachablePath = true;
    }

    /// <inheritdoc />
    public float WalkableSampleSize
    {
        get
        {
            PathfindingGrid resolved = ResolveGrid();
            return resolved != null ? resolved.CellSize : 0f;
        }
    }

    /// <inheritdoc />
    public bool IsWalkable(Vector2 worldPosition)
    {
        PathfindingGrid resolved = ResolveGrid();
        return resolved == null || resolved.IsWalkableWorld(worldPosition);
    }

    /// <summary>
    /// Moves the agent towards the target using pathfinding.
    /// </summary>
    public void Move(Transform agent, Vector3 target, float speed)
    {
        if (agent == null) return;
        PathfindingGrid resolved = ResolveGrid();
        if (resolved == null) return;

        Vector2 target2D = new Vector2(target.x, target.y);

        bool targetChanged = !hasLastTarget || Vector2.Distance(lastTarget, target2D) >= targetRepathDistance;
        bool pathExhausted = currentPath.Count == 0 || currentPathIndex >= currentPath.Count;
        bool shouldRepath = Time.time >= nextRepathTime || targetChanged || pathExhausted;

        // A target that just failed keeps failing for a while (a sealed-off room, a spot inside a
        // wall). Retrying it every frame is what turns one bad target into a frame-rate collapse,
        // so the failure gets its own, longer cooldown.
        if (shouldRepath && Time.time < nextFailedRetryTime) shouldRepath = false;

        if (shouldRepath)
        {
            bool reachedTarget = AStarPathfinder.FindPath(resolved, agent.position, target2D, currentPath, maxExploredNodes);
            currentPathIndex = 0;
            lastTarget = target2D;
            hasLastTarget = true;
            nextRepathTime = Time.time + repathInterval;

            bool alreadyAtTarget = Vector2.Distance(agent.position, target2D) <= waypointReachDistance;
            hasReachablePath = alreadyAtTarget || reachedTarget;

            if (!hasReachablePath)
                nextFailedRetryTime = Time.time + failedPathRetryInterval;
        }

        FollowPath(agent, speed);
    }

    /// <summary>
    /// Resolves and caches the grid reference, falling back to the one in the scene when the
    /// prefab could not carry it (spawned enemies).
    /// </summary>
    private PathfindingGrid ResolveGrid()
    {
        if (grid == null)
            grid = FindFirstObjectByType<PathfindingGrid>();
        return grid;
    }

    /// <summary>
    /// Moves the agent along the currently cached path.
    /// </summary>
    private void FollowPath(Transform agent, float speed)
    {
        while (currentPathIndex < currentPath.Count)
        {
            Vector2 waypoint = currentPath[currentPathIndex];
            if (Vector2.Distance(agent.position, waypoint) <= waypointReachDistance)
            {
                currentPathIndex++;
                continue;
            }

            Vector2 nextPos = Vector2.MoveTowards(agent.position, waypoint, speed * Time.deltaTime);
            agent.position = new Vector3(nextPos.x, nextPos.y, agent.position.z);
            break;
        }
    }

    /// <summary>
    /// Draws the currently cached path in Scene view for debugging.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!drawPathGizmos || currentPath == null || currentPath.Count == 0) return;

        Gizmos.color = pathColor;

        Vector3 previous = transform.position;
        for (int i = 0; i < currentPath.Count; i++)
        {
            Vector3 current = new Vector3(currentPath[i].x, currentPath[i].y, transform.position.z);
            Gizmos.DrawLine(previous, current);
            Gizmos.DrawSphere(current, 0.06f);
            previous = current;
        }
    }
}
