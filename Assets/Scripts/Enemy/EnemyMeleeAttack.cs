using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Melee attack behaviour shared by enemy types (composition, like movement and detection).
/// While the enemy is chasing the player (<see cref="EnemyState.FollowPlayer"/>), in range,
/// and off cooldown, it starts an attack and lands the hit after attackHitDelay if the player
/// is still within range — giving the player a chance to dodge. Raises <see cref="AttackStarted"/>
/// so a visual driver (e.g. SkullGuyAnimationDriver) can play the attack animation.
/// </summary>
[RequireComponent(typeof(EnemyBase))]
public class EnemyMeleeAttack : MonoBehaviour
{
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private int attackDamage = 20;
    [Tooltip("Seconds from the attack starting to the hit landing. The hit connects only if the player is still within range at that moment.")]
    [SerializeField] private float attackHitDelay = 0.4f;

    /// <summary>Raised when an attack starts. Visual drivers subscribe to play the attack animation.</summary>
    public event Action AttackStarted;

    private EnemyBase enemy;
    private PlayerHealthSystem playerHealth;
    private float nextAttackTime;

    private void Awake()
    {
        enemy = GetComponent<EnemyBase>();
    }

    private void Update()
    {
        if (enemy.IsDead) return;
        if (enemy.CurrentState != EnemyState.FollowPlayer) return;
        if (Time.time < nextAttackTime) return;
        if (enemy.Player == null) return;
        if (Vector2.Distance(transform.position, enemy.Player.position) > attackRange) return;

        Attack();
    }

    /// <summary>
    /// Starts an attack: notifies listeners (animation) and lands the hit after attackHitDelay.
    /// </summary>
    public void Attack()
    {
        nextAttackTime = Time.time + attackCooldown;

        // On the wind-up, not on the hit landing: attackHitDelay exists to give the player
        // a window to dodge, and a swing they cannot hear until it connects removes it.
        AudioService.PlayAt(SoundId.EnemyAttack, transform.position);

        AttackStarted?.Invoke();
        StartCoroutine(LandHitAfterDelay());
    }

    private IEnumerator LandHitAfterDelay()
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (enemy.IsDead || enemy.Player == null) yield break;
        if (Vector2.Distance(transform.position, enemy.Player.position) > attackRange) yield break;

        if (playerHealth == null)
            playerHealth = enemy.Player.GetComponent<PlayerHealthSystem>();
        playerHealth?.TakeDamage(attackDamage);
    }
}
