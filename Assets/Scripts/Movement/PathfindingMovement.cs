using UnityEngine;

/// <summary>
/// Movement strategy using pathfinding algorithm.
/// Implement pathfinding logic in the Move method.
/// </summary>
public class PathfindingMovement : MonoBehaviour, IMovementStrategy
{
    /// <summary>
    /// Moves the agent towards the target using pathfinding.
    /// </summary>
    public void Move(Transform agent, Vector3 target, float speed)
    {
        // TODO: Implement A* or chosen pathfinding algorithm
    }
}
