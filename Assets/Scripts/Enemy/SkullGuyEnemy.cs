using UnityEngine;

/// <summary>
/// Concrete "Skull Guy" enemy. Reuses the <see cref="EnemyBase"/> state machine and movement
/// composition, and owns behaviour specific to this enemy type (death handling, and a hook for
/// attacks that plays the ATAK animation via <see cref="SkullGuyAnimationDriver"/>).
///
/// Required components on the prefab: SpriteRenderer, EnemySpriteAnimator,
/// SkullGuyAnimationDriver, an IMovementStrategy (SimpleDirectMovement or PathfindingMovement),
/// and a Rigidbody2D.
/// </summary>
public class SkullGuyEnemy : EnemyBase
{
    private SkullGuyAnimationDriver animationDriver;

    protected override void Awake()
    {
        base.Awake();
        animationDriver = GetComponent<SkullGuyAnimationDriver>();
    }

    /// <summary>
    /// Plays the attack animation. Call when the enemy's attack should fire
    /// (attack logic itself is not implemented in the base yet).
    /// </summary>
    public void Attack()
    {
        animationDriver?.TriggerAttack();
    }

    protected override void OnDeath()
    {
        // Placeholder: disable on death. A death animation can be played here later
        // (e.g. play a clip, then disable on ClipFinished).
        gameObject.SetActive(false);
    }
}
