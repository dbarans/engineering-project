using UnityEngine;

/// <summary>
/// Handles enemy interaction with destructible barrels, smashing them when blocked during a chase or search.
/// </summary>
public class EnemyBarrelAttacker : MonoBehaviour
{
    [Header("Attack Settings")]
    [Tooltip("Amount of damage dealt to the barrel per strike.")]
    [SerializeField] private int damagePerHit = 15;

    [Tooltip("Time delay in seconds between consecutive strikes.")]
    [SerializeField] private float attackInterval = 1.2f;

    private Barrel currentBarrel;
    private float nextAttackTime;
    private bool isAttackingBarrel = false;

    private GameObject lastCheckedObject;
    private Barrel lastCheckedBarrel;

    private EnemyBase enemyBase;
    private SkullGuyAnimationDriver animationDriver;
    private Rigidbody2D rb;

    private void Awake()
    {
        enemyBase = GetComponent<EnemyBase>();
        animationDriver = GetComponent<SkullGuyAnimationDriver>();
        if (animationDriver == null)
            animationDriver = GetComponentInChildren<SkullGuyAnimationDriver>();

        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (currentBarrel == null)
        {
            ResetAttack();
            return;
        }

        if (isAttackingBarrel)
        {
            if (!currentBarrel.isActiveAndEnabled || !currentBarrel.GetComponent<Collider2D>().enabled)
            {
                ResetAttack();
                return;
            }

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            if (Time.time >= nextAttackTime)
            {
                AttackCurrentBarrel();
                nextAttackTime = Time.time + attackInterval;
            }
        }
    }

    /// <summary>
    /// Triggers the attack animation and deals damage to the targeted barrel.
    /// </summary>
    private void AttackCurrentBarrel()
    {
        if (currentBarrel != null)
        {
            if (animationDriver != null)
            {
                animationDriver.TriggerAttack();
            }

            currentBarrel.TakeDamage(damagePerHit);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        CheckForBarrel(collision.gameObject);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!isAttackingBarrel)
        {
            CheckForBarrel(collision.gameObject);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (currentBarrel != null && collision.gameObject == currentBarrel.gameObject)
        {
            ResetAttack();
        }
    }

    /// <summary>
    /// Evaluates collision with a destructible barrel during chase and investigation states.
    /// </summary>
    private void CheckForBarrel(GameObject obj)
    {
        bool isChasingOrSearching = enemyBase != null &&
            (enemyBase.CurrentState == EnemyState.FollowPlayer ||
             enemyBase.CurrentState == EnemyState.InvestigateLastKnown ||
             enemyBase.CurrentState == EnemyState.InvestigateNoise);

        if (isChasingOrSearching)
        {
            Barrel barrel = ResolveBarrel(obj);
            if (barrel != null)
            {
                currentBarrel = barrel;
                isAttackingBarrel = true;
                nextAttackTime = Time.time + 0.1f;
            }
        }
    }

    private Barrel ResolveBarrel(GameObject obj)
    {
        if (ReferenceEquals(obj, lastCheckedObject))
            return lastCheckedBarrel;

        lastCheckedObject = obj;
        lastCheckedBarrel = obj.TryGetComponent(out Barrel barrel) ? barrel : null;
        return lastCheckedBarrel;
    }

    private void ResetAttack()
    {
        currentBarrel = null;
        isAttackingBarrel = false;
    }
}