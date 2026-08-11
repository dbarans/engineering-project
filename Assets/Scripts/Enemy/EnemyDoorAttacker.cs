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
            // The barricade came down: the door behind it is just a door again, so walk
            // through instead of carrying on chopping. Without this the enemy would keep
            // swinging until the door itself was destroyed, handing the player several
            // free seconds that the barricade was never meant to buy. Locked doors are
            // unaffected — their lock never clears, so they are still breached outright.
            if (!currentDoor.IsLocked && !currentDoor.IsBarricaded)
            {
                currentDoor.ToggleDoor(transform);
                ResetAttack();
                return;
            }

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
                // A barricade blocks the enemy exactly like a lock does — it is the only
                // way a player shut inside a room can stop a chase, since ToggleLock
                // refuses to work from the inside.
                if (door.IsLocked || door.IsBarricaded)
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
            else if (!door.IsBarricaded)
            {
                // A patrolling enemy leaves a barricaded door alone rather than shouldering
                // it every frame — OnCollisionStay2D would otherwise retry the open call
                // continuously and flood the log with refusals.
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