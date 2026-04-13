using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Movement strategy using pathfinding algorithm.
/// Uses A* over PathfindingGrid and follows calculated waypoints.
/// </summary>
public class PathfindingMovement : MonoBehaviour, IMovementStrategy, IPathStatusProvider, IMovementArrivalTolerance
{
    [SerializeField] private PathfindingGrid grid;
    [SerializeField] private float repathInterval = 0.25f;
    [SerializeField] private float waypointReachDistance = 0.1f;
    [SerializeField] private float targetRepathDistance = 0.5f;
    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private Color pathColor = Color.yellow;

    private List<Vector2> currentPath = new List<Vector2>();
    private int currentPathIndex;
    private float nextRepathTime;
    private Vector2 lastTarget;
    private bool hasLastTarget;
    private bool hasReachablePath = true;

    /// <inheritdoc />
    public float StopDistanceFromTarget => waypointReachDistance;

    /// <summary>
    /// True when the latest pathfinding attempt produced a reachable route.
    /// </summary>
    public bool HasReachablePath => hasReachablePath;

    /// <summary>
    /// Moves the agent towards the target using pathfinding.
    /// </summary>
    public void Move(Transform agent, Vector3 target, float speed)
    {
        if (agent == null) return;
        if (grid == null)
            grid = FindFirstObjectByType<PathfindingGrid>();
        if (grid == null) return;

        Vector2 target2D = new Vector2(target.x, target.y);

        bool targetChanged = !hasLastTarget || Vector2.Distance(lastTarget, target2D) >= targetRepathDistance;
        bool shouldRepath = Time.time >= nextRepathTime || targetChanged || currentPath.Count == 0 || currentPathIndex >= currentPath.Count;
        if (shouldRepath)
        {
            currentPath = AStarPathfinder.FindPath(grid, agent.position, target2D);
            currentPathIndex = 0;
            lastTarget = target2D;
            hasLastTarget = true;
            nextRepathTime = Time.time + repathInterval;

            bool alreadyAtTarget = Vector2.Distance(agent.position, target2D) <= waypointReachDistance;
            hasReachablePath = alreadyAtTarget || currentPath.Count > 0;
        }

        FollowPath(agent, speed);
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
