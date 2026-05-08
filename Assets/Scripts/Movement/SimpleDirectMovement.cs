using UnityEngine;

/// <summary>
/// Simple direct movement towards target (no pathfinding).
/// Use for testing or enemies that ignore obstacles.
/// </summary>
public class SimpleDirectMovement : MonoBehaviour, IMovementStrategy
{
    [SerializeField] private float stoppingDistance = 1f;

    /// <summary>
    /// Moves the agent directly towards the target.
    /// </summary>
    public void Move(Transform agent, Vector3 target, float speed)
    {
        Vector3 direction = (target - agent.position).normalized;
        float distance = Vector3.Distance(agent.position, target);

        if (distance > stoppingDistance)
        {
            agent.position += direction * speed * Time.deltaTime;
        }
    }
}
