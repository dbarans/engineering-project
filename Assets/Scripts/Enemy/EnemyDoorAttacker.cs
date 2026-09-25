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

    [Tooltip("Minimum delay in seconds before the same door may be pushed open again.")]
    [SerializeField] private float toggleCooldown = 1f;

    private SimpleDoor currentDoor;
    private float nextAttackTime;
    private bool isAttackingDoor = false;

    // Collision callbacks fire for every contact of every frame, so the door lookup is cached per
    // GameObject: a walled-in enemy would otherwise resolve the same non-door collider hundreds of
    // times a second, which in the editor costs far more than the lookup itself.
    private GameObject lastCheckedObject;
    private SimpleDoor lastCheckedDoor;

    private SimpleDoor lastToggledDoor;
    private float nextToggleTime;

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
        SimpleDoor door = ResolveDoor(obj);

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
                    TryToggle(door);
                }
            }
            else if (!door.IsBarricaded)
            {
                // A patrolling enemy leaves a barricaded door alone rather than shouldering
                // it every frame — OnCollisionStay2D would otherwise retry the open call
                // continuously and flood the log with refusals.
                TryToggle(door);
            }
        }
    }

    /// <summary>
    /// Returns the door on <paramref name="obj"/>, reusing the previous result while the enemy keeps
    /// touching the same object. Contact callbacks repeat every physics step, so an uncached lookup
    /// runs constantly for walls and props that will never carry a door.
    /// </summary>
    private SimpleDoor ResolveDoor(GameObject obj)
    {
        if (ReferenceEquals(obj, lastCheckedObject))
            return lastCheckedDoor;

        lastCheckedObject = obj;
        lastCheckedDoor = obj.TryGetComponent(out SimpleDoor door) ? door : null;
        return lastCheckedDoor;
    }

    /// <summary>
    /// Opens or closes a door, but at most once per <see cref="toggleCooldown"/> for the same door.
    /// Standing in a doorway keeps the contact alive, and without the cooldown the door would be
    /// toggled on every physics step — flapping open and shut and flooding the log.
    /// </summary>
    private void TryToggle(SimpleDoor door)
    {
        if (door == lastToggledDoor && Time.time < nextToggleTime)
            return;

        lastToggledDoor = door;
        nextToggleTime = Time.time + toggleCooldown;
        door.ToggleDoor(transform);
    }

    private void ResetAttack()
    {
        currentDoor = null;
        isAttackingDoor = false;
    }
}