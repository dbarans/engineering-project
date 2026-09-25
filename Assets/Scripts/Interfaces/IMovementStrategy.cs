using UnityEngine;

/// <summary>
/// Defines the contract for movement strategies (pathfinding, direct movement, etc.).
/// Enables composition and exchange of movement algorithms without modifying enemy logic.
/// </summary>
public interface IMovementStrategy
{
    /// <summary>
    /// Moves the agent towards the target position.
    /// Called each frame by the enemy.
    /// </summary>
    /// <param name="agent">Transform of the moving entity.</param>
    /// <param name="target">Target position to move towards.</param>
    /// <param name="speed">Movement speed.</param>
    void Move(Transform agent, Vector3 target, float speed);
}
