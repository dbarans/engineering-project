using UnityEngine;

/// <summary>
/// Generic enemy for testing. Replace with specific enemy type when design is finalized.
/// Uses IMovementStrategy (add SimpleDirectMovement or PathfindingMovement component).
/// </summary>
public class TestEnemy : EnemyBase
{
    [Header("Test")]
    [SerializeField] private Transform target;

    protected override Vector3 GetTargetPosition()
    {
        return target != null ? target.position : transform.position;
    }

    protected override void OnDeath()
    {
        Destroy(gameObject);
    }
}
