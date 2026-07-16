using System.Collections;
using UnityEngine;

/// <summary>
/// Concrete "Skull Guy" enemy. Reuses the <see cref="EnemyBase"/> state machine and movement
/// composition, and owns behaviour specific to this enemy type: melee attacks (range, cooldown,
/// damage) and death handling. The attack animation is played via <see cref="SkullGuyAnimationDriver"/>.
///
/// Required components on the prefab: SpriteRenderer, EnemySpriteAnimator,
/// SkullGuyAnimationDriver, VisionPlayerDetector, an IMovementStrategy
/// (SimpleDirectMovement or PathfindingMovement), and a Rigidbody2D.
/// </summary>
public class SkullGuyEnemy : EnemyBase
{
    [Header("Attack")]
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private int attackDamage = 20;
    [Tooltip("Seconds from the attack animation starting to the hit landing. The hit connects only if the player is still within range at that moment.")]
    [SerializeField] private float attackHitDelay = 0.4f;

    private SkullGuyAnimationDriver animationDriver;
    private PlayerHealthSystem playerHealth;
    private float nextAttackTime;

    protected override void Awake()
    {
        base.Awake();
        animationDriver = GetComponent<SkullGuyAnimationDriver>();
    }

    protected override void Update()
    {
        base.Update();
        if (IsDead) return;

        TryAttack();
    }

    /// <summary>
    /// Attacks when chasing the player, in range, and off cooldown.
    /// </summary>
    private void TryAttack()
    {
        if (CurrentState != EnemyState.FollowPlayer) return;
        if (Time.time < nextAttackTime) return;
        if (Player == null) return;
        if (Vector2.Distance(transform.position, Player.position) > attackRange) return;

        Attack();
    }

    /// <summary>
    /// Starts an attack: plays the animation and lands the hit after attackHitDelay
    /// if the player is still within range.
    /// </summary>
    public void Attack()
    {
        nextAttackTime = Time.time + attackCooldown;
        animationDriver?.TriggerAttack();
        StartCoroutine(LandHitAfterDelay());
    }

    private IEnumerator LandHitAfterDelay()
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (IsDead || Player == null) yield break;
        if (Vector2.Distance(transform.position, Player.position) > attackRange) yield break;

        if (playerHealth == null)
            playerHealth = Player.GetComponent<PlayerHealthSystem>();
        playerHealth?.TakeDamage(attackDamage);
    }

    protected override void OnDeath()
    {
        // Placeholder: disable on death. A death animation can be played here later
        // (e.g. play a clip, then disable on ClipFinished).
        gameObject.SetActive(false);
    }
}
