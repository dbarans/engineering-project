using System.Collections;
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
    /// <summary>Moves to the last known player position after losing sight.</summary>
    InvestigateLastKnown,
    /// <summary>Returns to the patrol route (closest waypoint).</summary>
    ReturnToPatrol,
    /// <summary>Moves toward the position of a heard noise to check it out.</summary>
    InvestigateNoise
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
    [SerializeField] private float chaseSpeedMultiplier = 1.5f;

    [Header("State machine")]
    [SerializeField] private Transform player;
    [Tooltip("Player is always detected within this distance, regardless of vision/hearing checks. Guards against line-of-sight raycasts producing false negatives when the player is right next to the enemy.")]
    [SerializeField] private float alwaysDetectRange = 0.5f;
    [Tooltip("How long (seconds) the enemy keeps treating the player as detected after all detectors lose them. Prevents instantly dropping the chase when the player stops making noise or breaks line of sight for a moment.")]
    [SerializeField] private float detectionMemoryDuration = 1.5f;
    [SerializeField] private float investigateOvershootDistance = 1f;
    [SerializeField] private float investigateArrivalThreshold = 0.35f;
    [SerializeField] private Transform[] waypoints;
    [Tooltip("Must be >= movement strategy stopping distance (e.g. SimpleDirectMovement uses 1).")]
    [SerializeField] private float waypointReachedThreshold = 1.2f;
    [SerializeField] private float waypointPauseDuration = 0f;
    [SerializeField] private float unreachableWaypointRetryInterval = 0.6f;

    [Tooltip("If true, losing sight of the player sends the enemy straight to the nearest patrol waypoint. If false, it visits the last known position first.")]
    [SerializeField] private bool skipInvestigateWhenLostPlayer = true;

    protected float currentHealth;
    private IMovementStrategy movementStrategy;
    private IPlayerDetector[] detectors;
    private INoiseSensor noiseSensor;
    private Rigidbody2D rb;
    private float lastDetectionTime = float.NegativeInfinity;
    private Vector2 noiseTargetPosition;

    private EnemyState currentState = EnemyState.Idle;
    private int currentWaypointIndex;
    private bool isWaitingAtWaypoint;
    private float waypointPauseTimer;
    private float nextUnreachableWaypointRetryTime;
    private Vector2 lastKnownPlayerPosition;
    private Vector2 investigateTargetPosition;
    private bool hasLastKnownPlayerPosition;

    /// <summary>
    /// Current AI state (Idle, FollowPlayer, InvestigateLastKnown, ReturnToPatrol).
    /// </summary>
    public EnemyState CurrentState => currentState;

    /// <summary>
    /// World position the enemy is currently heading toward for the active state
    /// (player, last-known position, or patrol waypoint). Used by visuals for facing.
    /// </summary>
    public Vector3 CurrentTargetPosition => GetTargetPosition();

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

    /// <summary>
    /// Player transform assigned in the inspector. Available to derived types (e.g. for attacks).
    /// </summary>
    protected Transform Player => player;

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
        movementStrategy = GetComponent<IMovementStrategy>();
        detectors = GetComponents<IPlayerDetector>();
        noiseSensor = GetComponent<INoiseSensor>();

        rb = GetComponent<Rigidbody2D>();
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
    /// Uses a short cooldown between switches to limit path recalculation cost.
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
    }

    /// <summary>
    /// Handles transitions between Idle, FollowPlayer, InvestigateLastKnown, InvestigateNoise,
    /// and ReturnToPatrol. A detection lingers for detectionMemoryDuration after all detectors
    /// lose the player, so the chase is not dropped the moment the player goes quiet or breaks
    /// line of sight. A heard noise is weaker than a detection: it sends the enemy to
    /// investigate the noise position instead of straight into a chase.
    /// </summary>
    private void UpdateStateMachine()
    {
        if (IsPlayerDetected())
            lastDetectionTime = Time.time;

        bool playerInRange = Time.time - lastDetectionTime <= detectionMemoryDuration;
        if (playerInRange && player != null)
        {
            lastKnownPlayerPosition = player.position;
            hasLastKnownPlayerPosition = true;
        }

        bool heardNoise = noiseSensor != null && noiseSensor.HasFreshNoise;

        switch (currentState)
        {
            case EnemyState.Idle:
                if (playerInRange)
                {
                    isWaitingAtWaypoint = false;
                    currentState = EnemyState.FollowPlayer;
                }
                else if (heardNoise)
                {
                    isWaitingAtWaypoint = false;
                    noiseTargetPosition = noiseSensor.LastNoisePosition;
                    currentState = EnemyState.InvestigateNoise;
                }
                else
                    AdvanceWaypointIfReached();
                break;
            case EnemyState.FollowPlayer:
                if (!playerInRange)
                {
                    isWaitingAtWaypoint = false;
                    if (!skipInvestigateWhenLostPlayer && hasLastKnownPlayerPosition)
                    {
                        investigateTargetPosition = GetInvestigateTargetPosition();
                        currentState = EnemyState.InvestigateLastKnown;
                    }
                    else
                    {
                        SelectClosestWaypoint();
                        currentState = EnemyState.ReturnToPatrol;
                    }
                }
                break;
            case EnemyState.InvestigateLastKnown:
                if (playerInRange)
                {
                    currentState = EnemyState.FollowPlayer;
                }
                else if (heardNoise)
                {
                    noiseTargetPosition = noiseSensor.LastNoisePosition;
                    currentState = EnemyState.InvestigateNoise;
                }
                else if (Vector2.Distance(transform.position, investigateTargetPosition) <= investigateArrivalThreshold)
                {
                    hasLastKnownPlayerPosition = false;
                    SelectClosestWaypoint();
                    currentState = EnemyState.ReturnToPatrol;
                }
                break;
            case EnemyState.InvestigateNoise:
                if (playerInRange)
                {
                    currentState = EnemyState.FollowPlayer;
                }
                else
                {
                    // Keep following the trail: each fresh noise moves the target.
                    if (heardNoise)
                        noiseTargetPosition = noiseSensor.LastNoisePosition;

                    if (Vector2.Distance(transform.position, noiseTargetPosition) <= investigateArrivalThreshold)
                    {
                        SelectClosestWaypoint();
                        currentState = EnemyState.ReturnToPatrol;
                    }
                }
                break;
            case EnemyState.ReturnToPatrol:
                if (playerInRange)
                    currentState = EnemyState.FollowPlayer;
                else if (heardNoise)
                {
                    noiseTargetPosition = noiseSensor.LastNoisePosition;
                    currentState = EnemyState.InvestigateNoise;
                }
                else if (!HasValidWaypoint() || Vector2.Distance(transform.position, waypoints[currentWaypointIndex].position) <= EffectiveWaypointReachedThreshold())
                    currentState = EnemyState.Idle;
                break;
        }
    }

    /// <summary>
    /// Builds an investigate target slightly beyond the last seen player position.
    /// Helps avoid stopping exactly at doors/corners.
    /// </summary>
    private Vector2 GetInvestigateTargetPosition()
    {
        Vector2 fromEnemyToLastSeen = (lastKnownPlayerPosition - (Vector2)transform.position).normalized;
        if (fromEnemyToLastSeen == Vector2.zero)
            return lastKnownPlayerPosition;
        return lastKnownPlayerPosition + fromEnemyToLastSeen * Mathf.Max(0f, investigateOvershootDistance);
    }

    /// <summary>
    /// Detects the player by querying every <see cref="IPlayerDetector"/> component on the enemy
    /// (e.g. <see cref="VisionPlayerDetector"/>, <see cref="SoundPlayerDetector"/>). Player is
    /// detected if any detector succeeds, or unconditionally within alwaysDetectRange.
    /// </summary>
    private bool IsPlayerDetected()
    {
        if (player == null) return false;

        if (Vector2.Distance(transform.position, player.position) <= alwaysDetectRange) return true;

        if (detectors != null)
        {
            foreach (IPlayerDetector detector in detectors)
            {
                if (detector.IsPlayerDetected(player)) return true;
            }
        }

        return false;
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
        if (Vector2.Distance(transform.position, current.position) <= EffectiveWaypointReachedThreshold())
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
            }
        }
        else
        {
            isWaitingAtWaypoint = false;
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
    /// Patrol arrival distance: at least inspector threshold and at least the movement strategy's stop distance.
    /// </summary>
    private float EffectiveWaypointReachedThreshold()
    {
        if (movementStrategy is IMovementArrivalTolerance tol)
            return Mathf.Max(waypointReachedThreshold, tol.StopDistanceFromTarget);
        return waypointReachedThreshold;
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
    /// Applies an instant knockback impulse to the enemy.
    /// </summary>
    /// <param name="direction">Normalized direction of the push.</param>
    /// <param name="force">Strength of the impulse.</param>
    public virtual void Knockback(Vector2 direction, float force)
    {
        if (IsDead) return;

        if (rb != null)
            StartCoroutine(ApplyKnockback(direction.normalized * force));
    }

    private IEnumerator ApplyKnockback(Vector2 impulse)
    {
        rb.AddForce(impulse, ForceMode2D.Impulse);
        yield return new WaitForSeconds(0.12f);
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
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
            case EnemyState.InvestigateLastKnown:
                return hasLastKnownPlayerPosition ? (Vector3)investigateTargetPosition : transform.position;
            case EnemyState.InvestigateNoise:
                return noiseTargetPosition;
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
        movementStrategy?.Move(transform, GetTargetPosition(), GetCurrentMoveSpeed());
    }

    /// <summary>
    /// Returns movement speed for current AI state.
    /// </summary>
    private float GetCurrentMoveSpeed()
    {
        if (currentState == EnemyState.FollowPlayer
            || currentState == EnemyState.InvestigateLastKnown
            || currentState == EnemyState.InvestigateNoise)
            return moveSpeed * Mathf.Max(0f, chaseSpeedMultiplier);

        return moveSpeed;
    }

    /// <summary>
    /// Decides whether movement should be executed in the current frame/state.
    /// </summary>
    private bool ShouldMove()
    {
        if (currentState == EnemyState.FollowPlayer
            || currentState == EnemyState.InvestigateLastKnown
            || currentState == EnemyState.InvestigateNoise)
            return true;

        if (!HasValidWaypoint())
            return false;

        // Single waypoint acts as a guard position: move there once, then stay.
        if (waypoints.Length == 1)
            return Vector2.Distance(transform.position, waypoints[0].position) > EffectiveWaypointReachedThreshold();

        return true;
    }

    /// <summary>
    /// Save hook (plan §5.4): snapshots the mutable AI state for <see cref="EnemySaveable"/>.
    /// Waypoints travel as an index, never as Transform references.
    /// </summary>
    public EnemySaveState CaptureSaveState()
    {
        return new EnemySaveState
        {
            health = currentHealth,
            aiState = (int)currentState,
            waypointIndex = currentWaypointIndex,
            lastKnownPlayerPos = hasLastKnownPlayerPosition
                ? new[] { lastKnownPlayerPosition.x, lastKnownPlayerPosition.y }
                : null
        };
    }

    /// <summary>
    /// Save hook (plan §5.4): applies a snapshot from <see cref="CaptureSaveState"/>.
    /// InvestigateLastKnown maps to ReturnToPatrol — its private target fields are not
    /// saved and restoring the raw state would leave the enemy stuck at a stale target;
    /// heading for the nearest waypoint instead is indistinguishable to the player.
    /// </summary>
    public void RestoreSaveState(EnemySaveState state)
    {
        currentHealth = Mathf.Clamp(state.health, 0f, maxHealth);

        if (waypoints != null && waypoints.Length > 0)
            currentWaypointIndex = Mathf.Clamp(state.waypointIndex, 0, waypoints.Length - 1);

        hasLastKnownPlayerPosition =
            state.lastKnownPlayerPos != null && state.lastKnownPlayerPos.Length >= 2;
        lastKnownPlayerPosition = hasLastKnownPlayerPosition
            ? new Vector2(state.lastKnownPlayerPos[0], state.lastKnownPlayerPos[1])
            : Vector2.zero;

        var restoredState = (EnemyState)state.aiState;
        if (restoredState < EnemyState.Idle || restoredState > EnemyState.InvestigateNoise)
            restoredState = EnemyState.Idle; // unknown value from a foreign/edited save
        if (restoredState == EnemyState.InvestigateLastKnown || restoredState == EnemyState.InvestigateNoise)
        {
            SelectClosestWaypoint();
            restoredState = EnemyState.ReturnToPatrol;
        }
        currentState = restoredState;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, alwaysDetectRange);
    }
}
