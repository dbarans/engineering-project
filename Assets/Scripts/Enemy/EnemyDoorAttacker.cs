using UnityEngine;

/// <summary>
/// Handles enemy door interactions (opening during patrol or if unlocked, breaching if locked).
/// </summary>
public class EnemyDoorAttacker : MonoBehaviour
{
    [Header("Attack Settings")]
    [Tooltip("Amount of damage dealt to the door per individual strike.")]
    [SerializeField] private float damagePerHit = 25f;

    [Tooltip("Time delay in seconds between consecutive door strikes.")]
    [SerializeField] private float attackInterval = 1.2f;

    private SimpleDoor currentDoor;
    private float nextAttackTime;
    private bool isAttackingDoor = false;

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
        if (currentDoor == null)
        {
            ResetAttack();
            return;
        }

        if (isAttackingDoor && currentDoor != null)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            if (Time.time >= nextAttackTime)
            {
                AttackCurrentDoor();
                nextAttackTime = Time.time + attackInterval;
            }
        }
    }

    /// <summary>
    /// Triggers the attack animation and deals damage to the targeted door.
    /// </summary>
    private void AttackCurrentDoor()
    {
        if (currentDoor != null)
        {
            Debug.Log($"[AI {gameObject.name}] Breaching door!");

            if (animationDriver != null)
            {
                animationDriver.TriggerAttack();
            }

            currentDoor.TakeDamage(damagePerHit);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        CheckForDoor(collision.gameObject);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!isAttackingDoor)
        {
            CheckForDoor(collision.gameObject);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (currentDoor != null && collision.gameObject == currentDoor.gameObject)
        {
            ResetAttack();
        }
    }

    /// <summary>
    /// Evaluates door collision: opens if unlocked, breaches if locked or aggressive.
    /// </summary>
    private void CheckForDoor(GameObject obj)
    {
        SimpleDoor door = obj.GetComponent<SimpleDoor>();

        if (door != null)
        {
            bool isChasingOrSearching = enemyBase != null &&
                (enemyBase.CurrentState == EnemyState.FollowPlayer ||
                 enemyBase.CurrentState == EnemyState.InvestigateLastKnown ||
                 enemyBase.CurrentState == EnemyState.InvestigateNoise);

            if (isChasingOrSearching)
            {
                if (door.IsLocked)
                {
                    currentDoor = door;
                    isAttackingDoor = true;
                    nextAttackTime = Time.time + 0.1f;
                }
                else
                {
                    door.ToggleDoor(transform);
                }
            }
            else
            {
                door.ToggleDoor(transform);
            }
        }
    }

    private void ResetAttack()
    {
        currentDoor = null;
        isAttackingDoor = false;
    }
}