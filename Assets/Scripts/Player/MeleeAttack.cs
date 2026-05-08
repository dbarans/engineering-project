using System;
using UnityEngine;

public class MeleeAttack : PlayerAttack
{
    [SerializeField] private float attackRadius = 3f;
    [SerializeField] private Transform attackPoint;

    protected override void ExecuteAttack()
    {
        Debug.Log("melee.");

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(
            attackPoint.position,
            attackRadius,
            enemyLayer
        );

        foreach (Collider2D enemy in hitEnemies)
        {
            Debug.Log("hitted: " + enemy.name);
            // enemy.GetComponent<Health>().TakeDamage(100);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
    }
}