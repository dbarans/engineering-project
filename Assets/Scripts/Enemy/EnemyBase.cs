using System.Collections;
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct DropItem
{
    public ItemData itemData;
    public int quantity;
}

/// <summary>
/// Enemy behaviour states used by the base state machine.
/// </summary>
public enum EnemyState
{
    /// <summary>Patrols waypoints, or (transiently) decides what to do when no waypoint is configured.</summary>
    Idle,
    /// <summary>Actively follows the player while in detection range.</summary>
    FollowPlayer,
    /// <summary>Moves to the last known player position after losing sight.</summary>
    InvestigateLastKnown,
    /// <summary>Returns to the patrol route (closest waypoint).</summary>
    ReturnToPatrol,
    /// <summary>Moves toward the position of a heard noise to check it out.</summary>
    InvestigateNoise,
    /// <summary>Wanders between random points near where the player was lost, instead of returning to patrol.</summary>
    WanderNearLastPosition
}

/// <summary>
/// What an enemy does once it gives up investigating (reaches the last-known-position or
/// noise target without re-detecting the player).
/// </summary>
public enum PostInvestigateBehavior
{
    /// <summary>Heads back to the nearest patrol waypoint, as before.</summary>
    ReturnToPatrol,
    /// <summary>Wanders between random points near the spot where the player was lost.</summary>
    WanderNearLastPosition
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
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;
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

    [Header("After losing the player")]
    [Tooltip("What to do once investigation ends without re-detecting the player: return to patrol waypoints, or wander near the spot where the player was lost.")]
    [SerializeField] private PostInvestigateBehavior postInvestigateBehavior = PostInvestigateBehavior.ReturnToPatrol;
    [Tooltip("Radius within which random wander points are picked: around the spot where the player was lost (WanderNearLastPosition), or around the spawn position for enemies with no waypoints configured.")]
    [SerializeField] private float wanderRadius = 4f;

    [Header("Idle audio")]
    [Tooltip("How close the player has to be for this enemy's idle sound to fire at all. Deliberately wider than the player's own view radius (FieldOfView, 15) — hearing what you cannot see is the point. Raising it past the SoundBank entry's maxDistance does nothing: the 3D falloff has already reached silence by then, so both have to move together. 0 keeps this enemy silent while idle.")]
    [SerializeField] private float idleSoundRadius = 26f;
    [Tooltip("Shortest gap between two idle sounds from this enemy, in seconds.")]
    [Min(0f)]
    [SerializeField] private float idleSoundIntervalMin = 4f;
    [Tooltip("Longest gap between two idle sounds from this enemy, in seconds. The actual gap is drawn per sound, so two enemies never settle into the same rhythm.")]
    [Min(0f)]
    [SerializeField] private float idleSoundIntervalMax = 6f;

    [Header("Performance")]
    [Tooltip("Extra distance added to this enemy's own sensor ranges. Inside the result the AI ticks every frame; outside it, only every throttledTickInterval. A dungeon holds dozens of enemies and nearly all of them are far away at any moment.")]
    [SerializeField] private float fullUpdateMargin = 8f;
    [Tooltip("Tick period for enemies the player is far away from. Movement is compensated for the longer step, so patrols still run at normal speed. Set to 0 to disable throttling.")]
    [SerializeField] private float throttledTickInterval = 0.35f;

    [Header("Drop System")]
    [SerializeField] private GameObject corpsePrefab;
    [SerializeField] private List<DropItem> possibleDrops;
    [Header("Random Drop System")]
    [SerializeField] private List<LootDropEntry> possibleRandomDrops = new List<LootDropEntry>();
    [System.Serializable]
    public struct LootDropEntry
        {
            public ItemData itemData;
            [Min(1)] public int minQuantity;
            [Min(1)] public int maxQuantity;
            [Range(0f, 100f)] public float dropChancePercent;
        }

    protected float currentHealth;
    private IMovementStrategy movementStrategy;
    private IPlayerDetector[] detectors;
    private INoiseSensor noiseSensor;
    private IPlayerConcealment playerConcealment;
    private Transform concealmentSource;
    private Rigidbody2D rb;
    private float lastDetectionTime = float.NegativeInfinity;
    private Vector2 noiseTargetPosition;
    private Vector2 wanderAnchor;
    private Vector2 wanderTargetPosition;
    private float nextUnreachableWanderRetryTime;
    private Vector2 spawnPosition;
    private IWalkabilityProbe walkabilityProbe;
    private float fullUpdateSqrDistance;
    private float nextThrottledTickTime;
    private float lastTickTime;
    private float lastTickDelta;
    private float tickSpeedScale = 1f;
    private float nextIdleSoundTime;

    /// <summary>
    /// Whether this enemy has already barked for the hunt it is currently on. Latched in
    /// <see cref="UpdateAlertAudio"/>, cleared only when the trail goes cold.
    /// </summary>
    private bool hasAlertedThisHunt;

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
    /// Player transform assigned in the inspector. Used by companion components (e.g. attacks).
    /// </summary>
    public Transform Player => player;

    /// <summary>
    /// Injects the player for enemies spawned at runtime. A prefab asset cannot hold a
    /// reference to a scene object, so a spawned enemy starts with no player and would
    /// never detect anything — the procedural generator calls this right after
    /// Instantiate. Scene-placed enemies keep their inspector reference.
    /// </summary>
    public void SetPlayer(Transform playerTransform)
    {
        if (playerTransform != null) player = playerTransform;
    }

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
        movementStrategy = GetComponent<IMovementStrategy>();
        detectors = GetComponents<IPlayerDetector>();
        noiseSensor = GetComponent<INoiseSensor>();
        walkabilityProbe = movementStrategy as IWalkabilityProbe;
        spawnPosition = transform.position;

        CacheFullUpdateDistance();
        lastTickTime = Time.time;
        // Random phase so a crowd of enemies spawned in the same frame does not land all of its
        // throttled ticks (and their path searches) on the same frame forever after.
        nextThrottledTickTime = Time.time + Random.value * Mathf.Max(0f, throttledTickInterval);
        ScheduleNextIdleSound();

        rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    /// <summary>
    /// Precomputes the radius inside which this enemy must run its AI every frame: the widest
    /// range any of its sensors can reach, plus a margin. Outside it, no detector can possibly
    /// fire, so a full-rate tick would only burn frame time.
    /// </summary>
    private void CacheFullUpdateDistance()
    {
        float sensorRange = alwaysDetectRange;

        if (detectors != null)
        {
            foreach (IPlayerDetector detector in detectors)
                sensorRange = Mathf.Max(sensorRange, detector.DetectionRange);
        }

        float full = sensorRange + Mathf.Max(0f, fullUpdateMargin);
        fullUpdateSqrDistance = full * full;
    }

    /// <summary>
    /// Last-resort player resolution for enemies nobody injected into — currently the
    /// ones the save system respawns from the prefab registry, which has no business
    /// knowing what a player is. Runs in Start so <see cref="GameManager"/> has already
    /// initialised. Enemies placed in the scene or spawned by the dungeon populator
    /// already have their reference and skip this entirely.
    /// </summary>
    protected virtual void Start()
    {
        if (player != null) return;

        var gameManager = FindFirstObjectByType<GameManager>();
        GameObject resolved = gameManager != null ? gameManager.GetPlayer() : null;
        if (resolved != null)
        {
            player = resolved.transform;
            return;
        }

        var movement = FindFirstObjectByType<PlayerMovement>();
        if (movement != null) player = movement.transform;
    }

    protected virtual void Update()
    {
        if (IsDead) return;
        if (!ShouldTickThisFrame()) return;

        lastTickDelta = Time.time - lastTickTime;
        lastTickTime = Time.time;
        // A throttled enemy covers several frames' worth of ground in one step. The movement
        // strategies integrate against Time.deltaTime, so the speed handed to them is scaled by
        // how many frames this tick stands in for — otherwise distant patrols would crawl.
        tickSpeedScale = Time.deltaTime > 0f ? Mathf.Clamp(lastTickDelta / Time.deltaTime, 0f, 60f) : 1f;

        UpdateStateMachine();
        UpdateIdleAudio();
        ResolveUnreachablePatrolWaypoint();
        ResolveUnreachableWanderTarget();
        if (movementStrategy != null && ShouldMove())
            Move();
    }

    /// <summary>
    /// Distance-based AI throttling. Enemies within sensor range of the player (or reacting to a
    /// noise they just heard) tick every frame as before; the rest — the overwhelming majority in
    /// a large dungeon — tick at throttledTickInterval, which is what keeps dozens of path
    /// searches from piling into a single frame.
    /// </summary>
    private bool ShouldTickThisFrame()
    {
        if (throttledTickInterval <= 0f) return true;

        // A fresh noise is the one thing that can reach an enemy the player is nowhere near, and
        // it stays fresh only briefly — checking it here costs a float compare and keeps hearing
        // exactly as responsive as it was.
        bool nearPlayer = noiseSensor != null && noiseSensor.HasFreshNoise;

        if (!nearPlayer && player != null)
        {
            Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
            nearPlayer = toPlayer.sqrMagnitude <= fullUpdateSqrDistance;
        }

        if (!nearPlayer && Time.time < nextThrottledTickTime) return false;

        // Jitter keeps the throttled ticks of a spawned group from re-synchronising over time.
        nextThrottledTickTime = Time.time + throttledTickInterval * Random.Range(0.85f, 1.15f);
        return true;
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
    /// Picks a new random wander point when the current one turns out unreachable.
    /// Uses the same throttled-retry pattern as ResolveUnreachablePatrolWaypoint.
    /// </summary>
    private void ResolveUnreachableWanderTarget()
    {
        if (currentState != EnemyState.WanderNearLastPosition) return;
        if (Time.time < nextUnreachableWanderRetryTime) return;
        if (movementStrategy is not IPathStatusProvider pathStatus) return;
        if (pathStatus.HasReachablePath) return;

        PickRandomWanderTarget();
        nextUnreachableWanderRetryTime = Time.time + unreachableWaypointRetryInterval;
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
                else if (!HasValidWaypoint())
                {
                    // No patrol route configured: wander near the spawn position instead of
                    // standing still forever.
                    wanderAnchor = spawnPosition;
                    PickRandomWanderTarget();
                    currentState = EnemyState.WanderNearLastPosition;
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
                    currentState = EndInvestigation();
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
                        currentState = EndInvestigation();
                }
                break;
            case EnemyState.WanderNearLastPosition:
                if (playerInRange)
                {
                    currentState = EnemyState.FollowPlayer;
                }
                else if (heardNoise)
                {
                    noiseTargetPosition = noiseSensor.LastNoisePosition;
                    currentState = EnemyState.InvestigateNoise;
                }
                else if (Vector2.Distance(transform.position, wanderTargetPosition) <= investigateArrivalThreshold)
                {
                    PickRandomWanderTarget();
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

        UpdateAlertAudio();
    }

    /// <summary>
    /// The bark on spotting the player: <b>once per hunt</b>, not once per time the enemy
    /// re-acquires them.
    ///
    /// The transition into <see cref="EnemyState.FollowPlayer"/> alone is not enough. Losing
    /// the player drops the chase into an investigate state, and re-finding them from there
    /// is a *second* transition in — so a player ducking in and out of cover, which is the
    /// core stealth loop, got barked at every 1.5 s
    /// (<see cref="detectionMemoryDuration"/>). The alert has to mean "it has found you",
    /// and something that fires that often means nothing.
    ///
    /// The latch clears only once the enemy has genuinely given up: back to patrolling,
    /// idling, or wandering where it lost the trail. Investigating deliberately does
    /// <em>not</em> clear it — the enemy is still hunting, and picking the trail back up is
    /// the same hunt continuing. The next real sighting after it gives up barks again.
    /// </summary>
    private void UpdateAlertAudio()
    {
        if (currentState == EnemyState.FollowPlayer)
        {
            if (hasAlertedThisHunt) return;

            AudioService.PlayAt(SoundId.EnemyAlert, transform.position);
            hasAlertedThisHunt = true;
            return;
        }

        // Every state that is not the chase and not an investigation: the trail is cold.
        if (currentState == EnemyState.Idle
            || currentState == EnemyState.ReturnToPatrol
            || currentState == EnemyState.WanderNearLastPosition)
        {
            hasAlertedThisHunt = false;
        }
    }

    /// <summary>
    /// The moan an enemy makes while it has not noticed the player, on its own random
    /// cadence and only while the player is near enough to hear it mean something.
    ///
    /// Everything except <see cref="EnemyState.FollowPlayer"/> counts as idle. That one state
    /// already has a voice — <c>enemy.alert</c> on the way in, <c>enemy.attack</c> while it
    /// lands blows — and those two are what tell the player they have been seen. Layering a
    /// moan over them would blur the one cue that has to stay unambiguous. Investigating
    /// still moans: the enemy is looking, not looking <em>at you</em>, and hearing it search
    /// nearby is the point.
    ///
    /// The radius is a gate, not a volume curve. The clip's 3D falloff (SoundBank
    /// minDistance/maxDistance) already decides loudness; without the gate every enemy in the
    /// dungeon would still be *playing*, burning the 24-source pool on sounds attenuated to
    /// nothing and stealing voices from the ones the player can actually hear.
    ///
    /// While the gate is shut the timer is pushed forward rather than left running down, so a
    /// moan lands 4-6 s <em>after</em> the player arrives instead of the instant they cross
    /// the line — and a room full of enemies that all idled through the same long silence does
    /// not greet them in unison.
    /// </summary>
    private void UpdateIdleAudio()
    {
        if (idleSoundRadius <= 0f) return;

        if (currentState == EnemyState.FollowPlayer || !IsPlayerWithinIdleSoundRange())
        {
            ScheduleNextIdleSound();
            return;
        }

        if (Time.time < nextIdleSoundTime) return;

        // PlayAt, not PlayOn: the moan outlives the enemy that started it (these clips run
        // several seconds and the enemy can be killed mid-sound), and a pooled source
        // parented to an object that is then destroyed goes down with it. The same reason
        // every other enemy sound here is fixed to a point — see AUDIO_NOTES.md D5.
        AudioService.PlayAt(SoundId.EnemyIdle, transform.position);
        ScheduleNextIdleSound();
    }

    /// <summary>Whether the player is inside <c>idleSoundRadius</c> of this enemy.</summary>
    private bool IsPlayerWithinIdleSoundRange()
    {
        if (player == null) return false;

        Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
        return toPlayer.sqrMagnitude <= idleSoundRadius * idleSoundRadius;
    }

    /// <summary>
    /// Arms the idle-sound timer with a freshly drawn interval. Mathf.Max guards an inspector
    /// where max was left below min, which Random.Range would otherwise silently invert.
    /// </summary>
    private void ScheduleNextIdleSound()
    {
        nextIdleSoundTime = Time.time +
            Random.Range(idleSoundIntervalMin, Mathf.Max(idleSoundIntervalMin, idleSoundIntervalMax));
    }

    /// <summary>
    /// Called when an investigation (last-known-position or noise) ends without re-detecting
    /// the player. Returns the next state per postInvestigateBehavior: either heads back to the
    /// nearest patrol waypoint, or starts wandering near the current position (where the
    /// investigation trail ran out).
    /// </summary>
    private EnemyState EndInvestigation()
    {
        if (postInvestigateBehavior == PostInvestigateBehavior.WanderNearLastPosition)
        {
            wanderAnchor = transform.position;
            PickRandomWanderTarget();
            return EnemyState.WanderNearLastPosition;
        }

        SelectClosestWaypoint();
        return EnemyState.ReturnToPatrol;
    }

    /// <summary>
    /// Picks a new random point within wanderRadius of wanderAnchor, preferring one the movement
    /// strategy can actually stand on. An unvetted point lands inside a wall often enough that it
    /// used to cost a failed full-map search every time.
    /// </summary>
    private void PickRandomWanderTarget()
    {
        const int attempts = 6;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 candidate = wanderAnchor + Random.insideUnitCircle * wanderRadius;
            if (walkabilityProbe == null || walkabilityProbe.IsWalkable(candidate))
            {
                wanderTargetPosition = candidate;
                return;
            }
        }

        // Every candidate was blocked (enemy boxed in): stay put rather than commit to a target
        // that is known to be unreachable.
        wanderTargetPosition = transform.position;
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
    /// A concealed player (crouched under a table) is never detected — that check comes first,
    /// so hiding beats even alwaysDetectRange.
    /// </summary>
    private bool IsPlayerDetected()
    {
        if (player == null) return false;

        if (IsPlayerConcealed()) return false;

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
    /// True while the player is hidden by the world itself (see <see cref="IPlayerConcealment"/>).
    /// Resolved lazily and re-resolved whenever the player reference changes, since it is a
    /// serialized field that can be reassigned at runtime.
    /// </summary>
    private bool IsPlayerConcealed()
    {
        if (concealmentSource != player)
        {
            concealmentSource = player;
            playerConcealment = player.GetComponent<IPlayerConcealment>();
        }

        return playerConcealment != null && playerConcealment.IsConcealed;
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

            waypointPauseTimer -= lastTickDelta;
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

        // Positional and fired before the hook below: OnDeath is where subclasses
        // deactivate or destroy the enemy, and PlayAt outlives the emitter either way.
        AudioService.PlayAt(IsDead ? SoundId.EnemyDeath : SoundId.EnemyHurt, transform.position);

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
    /// Called when enemy health drops to zero. Spawns a corpse and passes item drops.
    /// </summary>
    protected virtual void OnDeath()
    {
        if (corpsePrefab != null)
        {
            List<DropItem> generatedDrops = GenerateLoot();

            GameObject corpseInstance = Instantiate(corpsePrefab, transform.position, Quaternion.identity);
            EnemyCorpse corpseComponent = corpseInstance.GetComponent<EnemyCorpse>();
            if (corpseComponent != null)
            {
                corpseComponent.InitializeDrop(generatedDrops);
            }
        }
        Destroy(gameObject);
    }

    private List<DropItem> GenerateLoot()
    {
        List<DropItem> droppedItems = new List<DropItem>();

        if (possibleRandomDrops == null || possibleRandomDrops.Count == 0)
            return droppedItems;

        List<LootDropEntry> availableDrops = new List<LootDropEntry>(possibleRandomDrops);

        int itemsToPick = Mathf.Min(2, availableDrops.Count);

        for (int i = 0; i < itemsToPick; i++)
        {
            int randomIndex = Random.Range(0, availableDrops.Count);
            var entry = availableDrops[randomIndex];
            availableDrops.RemoveAt(randomIndex);
            float roll = Random.Range(0f, 100f);
            if (roll <= entry.dropChancePercent)
            {
                int qty = Random.Range(entry.minQuantity, entry.maxQuantity + 1);
                droppedItems.Add(new DropItem { itemData = entry.itemData, quantity = qty });
            }
        }

        return droppedItems;
    }

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
            case EnemyState.WanderNearLastPosition:
                return wanderTargetPosition;
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
        movementStrategy?.Move(transform, GetTargetPosition(), GetCurrentMoveSpeed() * tickSpeedScale);
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
            || currentState == EnemyState.InvestigateNoise
            || currentState == EnemyState.WanderNearLastPosition)
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
        if (restoredState < EnemyState.Idle || restoredState > EnemyState.WanderNearLastPosition)
            restoredState = EnemyState.Idle; // unknown value from a foreign/edited save
        if (restoredState == EnemyState.InvestigateLastKnown
            || restoredState == EnemyState.InvestigateNoise
            || restoredState == EnemyState.WanderNearLastPosition)
        {
            SelectClosestWaypoint();
            restoredState = EnemyState.ReturnToPatrol;
        }
        currentState = restoredState;
    }

    private void OnDrawGizmos()
    {
        if (gizmoDebugSettings == null || gizmoDebugSettings.IsVisible(GizmoRanges.EnemyAlwaysDetect))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, alwaysDetectRange);
        }

        if (postInvestigateBehavior == PostInvestigateBehavior.WanderNearLastPosition)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, wanderRadius);
        }

        if (idleSoundRadius > 0f &&
            (gizmoDebugSettings == null || gizmoDebugSettings.IsVisible(GizmoRanges.EnemyIdleSound)))
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, idleSoundRadius);
        }
    }
}
