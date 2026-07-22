using UnityEngine;

/// <summary>
/// Concrete "Skull Guy" enemy. Reuses the <see cref="EnemyBase"/> state machine and movement
/// composition; melee combat lives in <see cref="EnemyMeleeAttack"/> and visuals in
/// <see cref="SkullGuyAnimationDriver"/>, so this type only owns death handling.
///
/// Required components on the prefab: SpriteRenderer, EnemySpriteAnimator,
/// SkullGuyAnimationDriver, VisionPlayerDetector, EnemyMeleeAttack, an IMovementStrategy
/// (SimpleDirectMovement or PathfindingMovement), and a Rigidbody2D.
/// </summary>
public class SkullGuyEnemy : EnemyBase
{
    protected override void OnDeath()
    {
        // Placeholder: disable on death. A death animation can be played here later
        // (e.g. play a clip, then disable on ClipFinished).
        gameObject.SetActive(false);
    }
}
