using UnityEngine;

/// <summary>
/// Generic enemy for testing. Uses state machine (Idle / FollowPlayer / LostPause / ReturnToPatrol).
/// Add IMovementStrategy (SimpleDirectMovement or PathfindingMovement). Assign Player in Inspector and optional waypoints.
/// </summary>
public class TestEnemy : EnemyBase
{
    protected override void OnDeath()
    {
        gameObject.SetActive(false);
    }
}
