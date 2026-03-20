using UnityEngine;

/// <summary>
/// Enemy behaviour states used by the base state machine.
/// </summary>
public enum EnemyState
{
    /// <summary>Patrols waypoints or holds position when no waypoint is available.</summary>
    Idle,
    /// <summary>Actively follows the player while in detection range.</summary>
    FollowPlayer,
    /// <summary>Waits briefly after losing the player before returning to patrol.</summary>
    LostPause,
    /// <summary>Returns to the patrol route (closest waypoint).</summary>
    ReturnToPatrol
}

/// <summary>
/// Abstract base class for all enemy types.
/// Defines common properties and methods that must be implemented by concrete types.
/// Uses IMovementStrategy for movement (composition over inheritance).
/// State machine: Idle &lt;-&gt; FollowPlayer &lt;-&gt; ReturnToPatrol.
/// </summary>
public abstract class EnemyBase : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] protected float maxHealth = 100f;
    [SerializeField] protected float moveSpeed = 3f;

    [Header("State machine")]
    [SerializeField] private Transform player;
    [SerializeField] private float detectionRadius = 5f;
    [SerializeField] private float lostPauseDuration = 1.5f;
    [SerializeField] private Transform[] waypoints;
    [Tooltip("Must be >= movement strategy stopping distance (e.g. SimpleDirectMovement uses 1).")]
    [SerializeField] private float waypointReachedThreshold = 1.2f;
    [SerializeField] private float waypointPauseDuration = 0f;
    [SerializeField] private float unreachableWaypointRetryInterval = 0.6f;

    protected float currentHealth;
    private IMovementStrategy movementStrategy;

    private EnemyState currentState = EnemyState.Idle;
    private int currentWaypointIndex;
    private float lostPauseTimer;
    private bool isWaitingAtWaypoint;
    private float waypointPauseTimer;
    private float nextUnreachableWaypointRetryTime;
    private int unreachableWaypointAttempts;

    /// <summary>
    /// Current AI state (Idle, FollowPlayer, LostPause, ReturnToPatrol).
    /// </summary>
    public EnemyState CurrentState => currentState;

    /// <summary>
    /// Current health of the enemy.
    /// </summary>
    public float CurrentHealth => currentHealth;

    /// <summary>
    /// Maximum health of the enemy.
    /// </summary>
    public float MaxHealth => maxHealth;

    /// <summary>
    /// Whether the enemy is dead.
    /// </summary>
    public bool IsDead => currentHealth <= 0f;

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
        movementStrategy = GetComponent<IMovementStrategy>();

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        UpdateStateMachine();
        ResolveUnreachablePatrolWaypoint();
        if (movementStrategy != null && ShouldMove())
            Move();
    }

    /// <summary>
    /// Switches to another waypoint when the current patrol waypoint is unreachable.
    /// Applies a retry cooldown to avoid constant path recalculation.
    /// </summary>
    private void ResolveUnreachablePatrolWaypoint()
    {
        if (currentState != EnemyState.Idle && currentState != EnemyState.ReturnToPatrol) return;
        if (waypoints == null || waypoints.Length <= 1 || !HasValidWaypoint()) return;
        if (Time.time < nextUnreachableWaypointRetryTime) return;
        if (movementStrategy is not IPathStatusProvider pathStatus) return;
        if (pathStatus.HasReachablePath) return;

        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        isWaitingAtWaypoint = false;
        waypointPauseTimer = 0f;
        nextUnreachableWaypointRetryTime = Time.time + unreachableWaypointRetryInterval;

        unreachableWaypointAttempts++;
        if (unreachableWaypointAttempts >= waypoints.Length)
        {
            // No reachable patrol waypoint right now. Pause retries briefly.
            nextUnreachableWaypointRetryTime = Time.time + Mathf.Max(unreachableWaypointRetryInterval, 1.5f);
            unreachableWaypointAttempts = 0;
        }
    }

    /// <summary>
    /// Handles transitions between Idle, FollowPlayer, LostPause, and ReturnToPatrol.
    /// </summary>
    private void UpdateStateMachine()
    {
        bool playerInRange = player != null && Vector2.Distance(transform.position, player.position) <= detectionRadius;

        switch (currentState)
        {
            case EnemyState.Idle:
                if (playerInRange)
                {
                    isWaitingAtWaypoint = false;
                    currentState = EnemyState.FollowPlayer;
                }
                else
                    AdvanceWaypointIfReached();
                break;
            case EnemyState.FollowPlayer:
                if (!playerInRange)
                {
                    isWaitingAtWaypoint = false;
                    lostPauseTimer = lostPauseDuration;
                    currentState = EnemyState.LostPause;
                }
                break;
            case EnemyState.LostPause:
                if (playerInRange)
                {
                    currentState = EnemyState.FollowPlayer;
                }
                else
                {
                    lostPauseTimer -= Time.deltaTime;
                    if (lostPauseTimer <= 0f)
                    {
                        isWaitingAtWaypoint = false;
                        SelectClosestWaypoint();
                        currentState = EnemyState.ReturnToPatrol;
                    }
                }
                break;
            case EnemyState.ReturnToPatrol:
                if (playerInRange)
                    currentState = EnemyState.FollowPlayer;
                else if (!HasValidWaypoint() || Vector2.Distance(transform.position, waypoints[currentWaypointIndex].position) <= waypointReachedThreshold)
                    currentState = EnemyState.Idle;
                break;
        }
    }

    /// <summary>
    /// Advances patrol waypoint index after reaching the current waypoint.
    /// Supports optional per-waypoint pause.
    /// </summary>
    private void AdvanceWaypointIfReached()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        Transform current = waypoints[currentWaypointIndex];
        if (current == null)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            return;
        }
        if (Vector2.Distance(transform.position, current.position) <= waypointReachedThreshold)
        {
            if (waypoints.Length == 1) return;

            if (waypointPauseDuration <= 0f)
            {
                currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
                return;
            }

            if (!isWaitingAtWaypoint)
            {
                isWaitingAtWaypoint = true;
                waypointPauseTimer = waypointPauseDuration;
                return;
            }

            waypointPauseTimer -= Time.deltaTime;
            if (waypointPauseTimer <= 0f)
            {
                isWaitingAtWaypoint = false;
                currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
                unreachableWaypointAttempts = 0;
            }
        }
        else
        {
            isWaitingAtWaypoint = false;
            unreachableWaypointAttempts = 0;
        }
    }

    /// <summary>
    /// Checks whether the current waypoint index points to a valid transform.
    /// </summary>
    private bool HasValidWaypoint()
    {
        return waypoints != null && waypoints.Length > 0 && waypoints[currentWaypointIndex] != null;
    }

    /// <summary>
    /// Picks the closest valid waypoint to resume patrol from current position.
    /// </summary>
    private void SelectClosestWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        float bestDistance = float.MaxValue;
        int bestIndex = currentWaypointIndex;

        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform waypoint = waypoints[i];
            if (waypoint == null) continue;

            float distance = Vector2.Distance(transform.position, waypoint.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        currentWaypointIndex = bestIndex;
    }

    /// <summary>
    /// Applies damage to the enemy.
    /// </summary>
    /// <param name="damage">Amount of damage.</param>
    public virtual void TakeDamage(float damage)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (IsDead)
        {
            OnDeath();
        }
        else
        {
            OnDamageTaken(damage);
        }
    }

    /// <summary>
    /// Called when enemy takes damage (before death).
    /// Derived types can override for visual/audio effects.
    /// </summary>
    protected virtual void OnDamageTaken(float damage)
    {
        // Optional implementation in derived classes
    }

    /// <summary>
    /// Called when enemy health drops to zero.
    /// Must be implemented by derived classes.
    /// </summary>
    protected abstract void OnDeath();

    /// <summary>
    /// Target position for movement based on current state. Override to customize.
    /// </summary>
    protected virtual Vector3 GetTargetPosition()
    {
        switch (currentState)
        {
            case EnemyState.FollowPlayer:
                return player != null ? player.position : transform.position;
            case EnemyState.ReturnToPatrol:
            case EnemyState.Idle:
            default:
                if (HasValidWaypoint())
                    return waypoints[currentWaypointIndex].position;
                return transform.position;
        }
    }

    /// <summary>
    /// Delegates movement to IMovementStrategy. Override for custom movement.
    /// </summary>
    protected virtual void Move()
    {
        movementStrategy?.Move(transform, GetTargetPosition(), moveSpeed);
    }

    /// <summary>
    /// Decides whether movement should be executed in the current frame/state.
    /// </summary>
    private bool ShouldMove()
    {
        if (currentState == EnemyState.FollowPlayer)
            return true;

        if (currentState == EnemyState.LostPause)
            return false;

        if (!HasValidWaypoint())
            return false;

        // Single waypoint acts as a guard position: move there once, then stay.
        if (waypoints.Length == 1)
            return Vector2.Distance(transform.position, waypoints[0].position) > waypointReachedThreshold;

        return true;
    }
}
