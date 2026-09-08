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
    WanderNearLastPosition,
    /// <summary>Sweeps onward the way the player fled — down the corridor, picking a branch at each junction. Appended last: saved enum ints stay valid.</summary>
    SearchAhead
}

/// <summary>
/// What an enemy does once it gives up investigating (reaches the last-known-position or
/// noise target without re-detecting the player).
/// </summary>
public enum PostInvestigateBehavior
{
    /// <summary>Heads back to the nearest patrol waypoint, or to its post when it has no route.</summary>
    ReturnToPatrol,
    /// <summary>Wanders between random points near the spot where the player was lost.</summary>
    WanderNearLastPosition,
    /// <summary>Rolled per enemy, so a group of identical prefabs does not all react the same way. Appended last: saved enum ints stay valid.</summary>
    Randomized
}

/// <summary>
/// What an enemy does when it has nothing to chase and no patrol route configured.
/// Enemies with waypoints always patrol them; this decides the rest.
/// </summary>
public enum IdleBehavior
{
    /// <summary>Roams random points around its post.</summary>
    Wander,
    /// <summary>Holds its post, and walks back to it after a chase drags it away.</summary>
    Guard,
    /// <summary>Rolled per enemy, so a room full of the same prefab is not all roamers or all statues.</summary>
    Randomized
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

    [Header("Idle behaviour")]
    [Tooltip("What this enemy does with no player to chase and no waypoints: roam around its post, or stand guard on it. Randomized rolls one per enemy from guardChance, so a crowd of identical prefabs is a mix. Enemies that do have waypoints patrol them either way.")]
    [SerializeField] private IdleBehavior idleBehavior = IdleBehavior.Randomized;
    [Tooltip("Chance a Randomized enemy comes out a standing guard rather than a roamer.")]
    [Range(0f, 1f)]
    [SerializeField] private float guardChance = 0.4f;

    [Header("After losing the player")]
    [Tooltip("What to do once investigation ends without re-detecting the player: return to its patrol route (or post), or wander near the spot where the player was lost. Randomized rolls one per enemy from wanderAfterLosingChance. Guards always go back to their post regardless.")]
    [SerializeField] private PostInvestigateBehavior postInvestigateBehavior = PostInvestigateBehavior.Randomized;
    [Tooltip("Chance a Randomized enemy settles where the trail went cold instead of walking back to its post.")]
    [Range(0f, 1f)]
    [SerializeField] private float wanderAfterLosingChance = 0.5f;
    [Tooltip("Longest the whole sweep after a lost chase can last, in seconds. Guards included — they only head back to their post once this runs out. 0 skips searching entirely.")]
    [Min(0f)]
    [SerializeField] private float searchDuration = 8f;
    [Tooltip("How far ahead the enemy aims for each step of the sweep. Roughly the length of one 'stride' down a corridor.")]
    [Min(0.1f)]
    [SerializeField] private float searchStepDistance = 1.5f;
    [Tooltip("How much clear space a direction needs before it counts as a way on. Too small and every doorway reads as a junction; too large and real side passages are missed.")]
    [Min(0.1f)]
    [SerializeField] private float searchProbeDistance = 1.6f;
    [Tooltip("How many junctions the enemy picks its way through before giving up the sweep. 0 = it gives up at the first fork.")]
    [Min(0)]
    [SerializeField] private int maxSearchJunctions = 3;
    [Tooltip("Safety net: give up on an investigation target not reached within this many seconds — an unreachable spot (behind a locked door, inside a wall) would otherwise leave the enemy standing still forever. 0 disables the timeout.")]
    [Min(0f)]
    [SerializeField] private float investigateTimeout = 8f;
    [Tooltip("Give up on a wander point not reached within this many seconds and pick another. A point a couple of steps past a wall is a walk around half the dungeon, and nothing else notices - the pathfinder does have a route, it is just an absurd one. 0 disables the timeout.")]
    [Min(0f)]
    [SerializeField] private float wanderTargetTimeout = 6f;
    [Tooltip("Radius within which random wander points are picked: around the spot where the player was lost (WanderNearLastPosition), or around the post for roamers with no waypoints configured.")]
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
    [Tooltip("Past this distance from the player the enemy is parked: no state machine, no path searches, no movement, and its cached route is handed back. Throttling still runs a full A* search several times a second for an enemy on the far side of the map, which nobody can see and nothing can reach. Floored at the enemy's own sensor range plus margin, so it can never cull an enemy that could still detect the player. 0 disables parking.")]
    [Min(0f)]
    [SerializeField] private float cullDistance = 60f;

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
    private float wanderTargetDeadline = float.PositiveInfinity;
    private float nextUnreachableInvestigateRetryTime;
    private float investigateStartTime;
    private Vector2 searchDirection = Vector2.right;
    private Vector2 searchStepTarget;
    private int searchJunctionsTaken;
    /// <summary>True once the sweep has taken a fork: the enemy has lost the thread and slows from a run to a walk.</summary>
    private bool searchIsWalking;
    private Vector2 travelDirection = Vector2.right;
    private Vector2 lastTravelSamplePosition;
    /// <summary>When the current search around the lost trail ends. Infinity = search forever (a roamer that adopted the spot).</summary>
    private float searchEndTime = float.PositiveInfinity;
    private Vector2 homePosition;
    private IWalkabilityProbe walkabilityProbe;
    private float fullUpdateSqrDistance;
    private float cullSqrDistance;
    private float uncullSqrDistance;
    private bool isCulled;
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
    /// True while the enemy is parked for being too far from the player to matter: no state
    /// machine, no path searches, no movement. Visuals read it so they can stand down too.
    /// </summary>
    public bool IsCulled => isCulled;

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
        homePosition = transform.position;
        lastTravelSamplePosition = transform.position;

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

        // Never park an enemy that could still sense the player: the floor is its own full-update
        // radius plus a little, whatever the inspector says.
        float cull = Mathf.Max(cullDistance, full + 4f);
        cullSqrDistance = cull * cull;

        // Hysteresis, so an enemy sitting exactly on the boundary does not park and wake every
        // few frames, dropping and rebuilding its route each time.
        float uncull = cull * 0.9f;
        uncullSqrDistance = uncull * uncull;
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
        if (UpdateCulling()) return;
        if (!ShouldTickThisFrame()) return;

        lastTickDelta = Time.time - lastTickTime;
        lastTickTime = Time.time;
        // A throttled enemy covers several frames' worth of ground in one step. The movement
        // strategies integrate against Time.deltaTime, so the speed handed to them is scaled by
        // how many frames this tick stands in for — otherwise distant patrols would crawl.
        tickSpeedScale = Time.deltaTime > 0f ? Mathf.Clamp(lastTickDelta / Time.deltaTime, 0f, 60f) : 1f;

        UpdateTravelDirection();
        UpdateStateMachine();
        UpdateIdleAudio();
        ResolveUnreachablePatrolWaypoint();
        ResolveUnreachableWanderTarget();
        ResolveUnreachableInvestigateTarget();
        if (movementStrategy != null && ShouldMove())
            Move();
    }

    /// <summary>
    /// Keeps a running note of which way the enemy is actually travelling. That is the heading a
    /// sweep starts on: when the player breaks away, the enemy carries on the way the chase was
    /// going rather than guessing from a position that is by then several steps stale.
    /// </summary>
    private void UpdateTravelDirection()
    {
        Vector2 position = transform.position;
        Vector2 delta = position - lastTravelSamplePosition;

        // Sampled over distance, not per tick: a slow walk covers almost nothing in one frame,
        // and normalising that noise would swing the heading around at random.
        if (delta.sqrMagnitude < 0.04f) return;

        travelDirection = delta.normalized;
        lastTravelSamplePosition = position;
    }

    /// <summary>
    /// Parks enemies too far from the player to matter, and wakes them when the player comes
    /// back. Returns true while parked, meaning "skip this enemy entirely this frame".
    ///
    /// This is the step beyond throttling. A throttled enemy still runs the whole state machine
    /// and a full A* search a few times a second; multiply that by every enemy in a 200x200
    /// dungeon and most of the AI budget goes on patrols nobody is in the same room as - or even
    /// the same wing of the map as. Parked, an enemy costs one distance comparison per frame and
    /// hands its cached route back to the heap.
    /// </summary>
    private bool UpdateCulling()
    {
        if (cullDistance <= 0f || player == null)
        {
            isCulled = false;
            return false;
        }

        float sqrToPlayer = ((Vector2)player.position - (Vector2)transform.position).sqrMagnitude;

        if (!isCulled)
        {
            if (sqrToPlayer <= cullSqrDistance) return false;

            isCulled = true;
            (movementStrategy as IPathStatusProvider)?.ReleaseCachedPath();
            return true;
        }

        if (sqrToPlayer > uncullSqrDistance) return true;

        isCulled = false;

        // Wake up as if no time had passed. lastTickTime drives tickSpeedScale, which compensates
        // movement for a longer step - left at the value from before the park, the first tick
        // after waking would be scaled by minutes of standing still and teleport the enemy.
        lastTickTime = Time.time;
        nextThrottledTickTime = Time.time;
        lastTravelSamplePosition = transform.position;
        return false;
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
    /// Gives up on an investigation target the pathfinder cannot reach — a spot behind a closed
    /// door, inside a wall, or across a gap. Without this the enemy keeps a target it can never
    /// arrive at, and since arrival is the only way out of an investigate state, it stands still
    /// until the player walks back into its senses.
    /// </summary>
    private void ResolveUnreachableInvestigateTarget()
    {
        if (currentState != EnemyState.InvestigateLastKnown
            && currentState != EnemyState.InvestigateNoise
            && currentState != EnemyState.SearchAhead) return;
        if (Time.time < nextUnreachableInvestigateRetryTime) return;
        if (movementStrategy is not IPathStatusProvider pathStatus) return;

        nextUnreachableInvestigateRetryTime = Time.time + unreachableWaypointRetryInterval;
        if (pathStatus.HasReachablePath) return;

        // A sweep step the pathfinder cannot reach means the probe was optimistic about the
        // passage: re-decide from here, and end the sweep if there is nowhere left to go.
        if (currentState == EnemyState.SearchAhead)
        {
            if (!AdvanceSearchStep()) currentState = FinishSearch();
            return;
        }

        // Search from where it stands instead: the player went somewhere around here.
        currentState = EndInvestigation();
    }

    /// <summary>Whether the enemy has been stuck on the same investigation for longer than investigateTimeout.</summary>
    private bool InvestigationTimedOut()
    {
        return investigateTimeout > 0f && Time.time - investigateStartTime > investigateTimeout;
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
        EnemyState stateOnEntry = currentState;

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
                    if (ResolvedIdleBehavior == IdleBehavior.Guard)
                    {
                        // A guard holds its post. It only moves if something (a chase it gave
                        // up on, a knockback) left it standing somewhere else.
                        if (!IsAtPost())
                            currentState = EnemyState.ReturnToPatrol;
                    }
                    else
                    {
                        // No patrol route configured: roam around the post instead of standing
                        // still forever. This is home, so there is nothing to stop searching for.
                        wanderAnchor = homePosition;
                        PickRandomWanderTarget();
                        searchEndTime = float.PositiveInfinity;
                        currentState = EnemyState.WanderNearLastPosition;
                    }
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
                        // Nothing left to investigate — the same choice as the end of an
                        // investigation: back to the post, or settle where the chase died.
                        currentState = EndInvestigation();
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
                else if (Vector2.Distance(transform.position, investigateTargetPosition) <= EffectiveArrivalThreshold
                    || InvestigationTimedOut())
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
                    // Keep following the trail: each fresh noise moves the target, and a live
                    // trail deserves a fresh timeout — the enemy is making progress, not stuck.
                    if (heardNoise)
                    {
                        noiseTargetPosition = noiseSensor.LastNoisePosition;
                        investigateStartTime = Time.time;
                    }

                    if (Vector2.Distance(transform.position, noiseTargetPosition) <= EffectiveArrivalThreshold
                        || InvestigationTimedOut())
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
                else if (Time.time >= searchEndTime)
                {
                    // Done poking around this patch: adopt it, or head home.
                    currentState = FinishSearch();
                }
                else if (Vector2.Distance(transform.position, wanderTargetPosition) <= EffectiveArrivalThreshold
                    || Time.time >= wanderTargetDeadline)
                {
                    PickRandomWanderTarget();
                }
                break;
            case EnemyState.SearchAhead:
                if (playerInRange)
                {
                    currentState = EnemyState.FollowPlayer;
                }
                else if (heardNoise)
                {
                    noiseTargetPosition = noiseSensor.LastNoisePosition;
                    currentState = EnemyState.InvestigateNoise;
                }
                else if (Time.time >= searchEndTime)
                {
                    currentState = FinishSearch();
                }
                else if (Vector2.Distance(transform.position, searchStepTarget) <= EffectiveArrivalThreshold
                    && !AdvanceSearchStep())
                {
                    // Dead end, or one fork too many: stop sweeping.
                    currentState = FinishSearch();
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
                else if (HasValidWaypoint()
                    ? Vector2.Distance(transform.position, waypoints[currentWaypointIndex].position) <= EffectiveWaypointReachedThreshold()
                    : IsAtPost())
                {
                    // Routeless enemies walk back to the post they started on rather than
                    // dropping straight into Idle wherever the chase happened to end.
                    currentState = EnemyState.Idle;
                }
                break;
        }

        // Arms the investigation timeout on the tick the enemy commits to an investigate state,
        // whichever of the several transitions got it there.
        if (currentState != stateOnEntry
            && (currentState == EnemyState.InvestigateLastKnown || currentState == EnemyState.InvestigateNoise))
            investigateStartTime = Time.time;

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
    /// The spot this enemy calls its own: where it stood when it spawned (or where it was
    /// posted in the save). Guards hold it, roamers circle it, and everyone walks back to it
    /// once a hunt is over.
    /// </summary>
    private Vector2 PostPosition => Application.isPlaying ? homePosition : (Vector2)transform.position;

    /// <summary>Whether the enemy is standing close enough to its post to count as being on it.</summary>
    private bool IsAtPost()
    {
        return Vector2.Distance(transform.position, homePosition) <= EffectiveWaypointReachedThreshold();
    }

    /// <summary>
    /// Resolves <see cref="idleBehavior"/>, rolling per enemy when it is set to Randomized.
    /// </summary>
    private IdleBehavior ResolvedIdleBehavior
    {
        get
        {
            if (idleBehavior != IdleBehavior.Randomized) return idleBehavior;
            return PersonalityRoll(GuardRollSalt) < guardChance ? IdleBehavior.Guard : IdleBehavior.Wander;
        }
    }

    /// <summary>
    /// Resolves <see cref="postInvestigateBehavior"/>. A guard is a guard: whatever the field
    /// says, it walks back to its post rather than settling down wherever the trail died —
    /// otherwise one chase would permanently move the guard off the spot it was placed on.
    /// </summary>
    private PostInvestigateBehavior ResolvedPostInvestigateBehavior
    {
        get
        {
            if (ResolvedIdleBehavior == IdleBehavior.Guard) return PostInvestigateBehavior.ReturnToPatrol;
            if (postInvestigateBehavior != PostInvestigateBehavior.Randomized) return postInvestigateBehavior;
            return PersonalityRoll(WanderRollSalt) < wanderAfterLosingChance
                ? PostInvestigateBehavior.WanderNearLastPosition
                : PostInvestigateBehavior.ReturnToPatrol;
        }
    }

    private const int GuardRollSalt = 1;
    private const int WanderRollSalt = 2;

    /// <summary>
    /// A stable pseudo-random value in [0,1) for this enemy, hashed from its post position.
    ///
    /// Deliberately not <see cref="Random"/>: the roll has to give the same answer every time
    /// it is asked, or an enemy would change its mind between frames. Hashing the post also
    /// survives a save/load and a scene reload — the enemy that guarded a doorway before the
    /// save still guards it after — while every enemy in the dungeon gets its own value.
    /// </summary>
    private float PersonalityRoll(int salt)
    {
        Vector2 seed = PostPosition;
        int x = Mathf.RoundToInt(seed.x * 16f);
        int y = Mathf.RoundToInt(seed.y * 16f);
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);

        // Bit-mixer (Murmur-style finalizer): neighbouring posts must not land on neighbouring
        // rolls, or a whole corridor of enemies comes out the same.
        h ^= h >> 16;
        h *= 0x7feb352du;
        h ^= h >> 15;
        h *= 0x846ca68bu;
        h ^= h >> 16;

        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>
    /// Called when an investigation (last-known-position or noise) ends without re-detecting
    /// the player. Returns the next state per postInvestigateBehavior: either heads back to the
    /// nearest patrol waypoint, or starts wandering near the current position (where the
    /// investigation trail ran out).
    /// </summary>
    private EnemyState EndInvestigation()
    {
        // Everyone searches first. Turning back the instant the trail runs out reads as the enemy
        // losing interest mid-stride. The sweep is the interesting version of that search, so it
        // gets first refusal; random pottering is only for enemies with no grid to read.
        if (BeginSearchAhead())
            return EnemyState.SearchAhead;

        wanderAnchor = transform.position;
        PickRandomWanderTarget();
        searchEndTime = Time.time + searchDuration;
        return EnemyState.WanderNearLastPosition;
    }

    /// <summary>
    /// The four directions a sweep can take. Corridors in this dungeon are axis-aligned, so
    /// diagonals would only ever cut a corner into a wall.
    /// </summary>
    private static readonly Vector2[] SearchDirections =
    {
        Vector2.right, Vector2.left, Vector2.up, Vector2.down
    };

    /// <summary>
    /// Starts the sweep: carry on the way the chase was going, and keep going until the passage
    /// gives the enemy a real choice. Returns false when there is no grid to read (no
    /// <see cref="IWalkabilityProbe"/>) or nowhere to go, so the caller can fall back.
    /// </summary>
    private bool BeginSearchAhead()
    {
        if (walkabilityProbe == null || searchDuration <= 0f) return false;

        searchDirection = SnapToSearchDirection(travelDirection);
        searchJunctionsTaken = 0;
        searchIsWalking = false;
        searchEndTime = Time.time + searchDuration;
        searchStepTarget = (Vector2)transform.position + searchDirection * EffectiveSearchStepDistance;

        // Chases end face-first into walls often enough to check: if straight on is blocked, let
        // the normal step logic pick a way out instead of walking into it.
        if (!IsPassageOpen(transform.position, searchDirection))
            return AdvanceSearchStep();

        return true;
    }

    /// <summary>
    /// One step of the sweep, re-decided every time the enemy reaches its step target.
    ///
    /// Straight on, or the single way round a bend, is not a decision - the enemy keeps running.
    /// Two or more ways on is a junction: it picks one, and from there it is searching rather
    /// than chasing, so it drops to a walk. Returns false when the sweep is over: a dead end, or
    /// one junction past <see cref="maxSearchJunctions"/>.
    /// </summary>
    private bool AdvanceSearchStep()
    {
        Vector2 origin = transform.position;
        Vector2 back = -searchDirection;

        Vector2 onlyOption = Vector2.zero;
        int openCount = 0;
        foreach (Vector2 direction in SearchDirections)
        {
            // Never count the way it came from: backtracking would turn every corridor into a
            // junction and every sweep into pacing on the spot.
            if (Vector2.Dot(direction, back) > 0.9f) continue;
            if (!IsPassageOpen(origin, direction)) continue;

            openCount++;
            if (openCount == 1) onlyOption = direction;
        }

        if (openCount == 0) return false;

        if (openCount == 1)
        {
            searchDirection = onlyOption;
        }
        else
        {
            if (searchJunctionsTaken >= maxSearchJunctions) return false;

            searchJunctionsTaken++;
            searchIsWalking = true;
            searchDirection = PickSearchBranch(origin, back, openCount);
        }

        searchStepTarget = origin + searchDirection * EffectiveSearchStepDistance;
        return true;
    }

    /// <summary>Picks one of the open ways on at a junction, uniformly at random.</summary>
    private Vector2 PickSearchBranch(Vector2 origin, Vector2 back, int openCount)
    {
        int chosen = Random.Range(0, openCount);

        foreach (Vector2 direction in SearchDirections)
        {
            if (Vector2.Dot(direction, back) > 0.9f) continue;
            if (!IsPassageOpen(origin, direction)) continue;
            if (chosen-- == 0) return direction;
        }

        return searchDirection;
    }

    /// <summary>
    /// Whether the enemy could walk <see cref="searchProbeDistance"/> in this direction. Sampled
    /// at several points rather than just the far end, so a pillar or a door frame partway along
    /// does not read as open ground.
    /// </summary>
    private bool IsPassageOpen(Vector2 origin, Vector2 direction)
    {
        if (walkabilityProbe == null) return false;

        const int samples = 3;
        float reach = EffectiveSearchProbeDistance;
        for (int i = 1; i <= samples; i++)
        {
            if (!walkabilityProbe.IsWalkable(origin + direction * (reach * i / samples)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Grain of the navigation data behind this enemy, in world units, or 0 with no grid.
    /// </summary>
    private float NavigationSampleSize => walkabilityProbe != null ? walkabilityProbe.WalkableSampleSize : 0f;

    /// <summary>
    /// How close counts as "reached" for an investigation, a wander point or a sweep step.
    /// </summary>
    /// <remarks>
    /// Never tighter than the navigation grid can deliver. An A* route is a list of <em>cell
    /// centres</em>; the exact point that was asked for is never appended, so on a 2-unit grid the
    /// enemy can legitimately stop 1.4 units from its target. The authored 0.35 was unsatisfiable
    /// there, and since arrival is the only way out of an investigate state, the enemy stood on
    /// the spot until the player walked back into its senses.
    /// </remarks>
    private float EffectiveArrivalThreshold =>
        Mathf.Max(investigateArrivalThreshold, NavigationSampleSize * 0.75f);

    /// <summary>Sweep step length, never shorter than one navigation cell.</summary>
    private float EffectiveSearchStepDistance => Mathf.Max(searchStepDistance, NavigationSampleSize);

    /// <summary>
    /// Sweep probe reach, never shorter than 1.5 cells - a probe that lands inside the enemy's own
    /// cell reports every direction open, and every corridor would read as a crossroads.
    /// </summary>
    private float EffectiveSearchProbeDistance => Mathf.Max(searchProbeDistance, NavigationSampleSize * 1.5f);

    /// <summary>Snaps a heading to the nearest of the four sweep directions.</summary>
    private static Vector2 SnapToSearchDirection(Vector2 heading)
    {
        if (heading.sqrMagnitude < 0.0001f) return Vector2.right;

        return Mathf.Abs(heading.x) >= Mathf.Abs(heading.y)
            ? (heading.x >= 0f ? Vector2.right : Vector2.left)
            : (heading.y >= 0f ? Vector2.up : Vector2.down);
    }

    /// <summary>
    /// The sweep is over. Now the per-enemy choice applies: adopt this patch of the dungeon and
    /// roam it, or head back to the post.
    /// </summary>
    private EnemyState FinishSearch()
    {
        if (ResolvedPostInvestigateBehavior == PostInvestigateBehavior.WanderNearLastPosition)
        {
            wanderAnchor = transform.position;
            PickRandomWanderTarget();
            searchEndTime = float.PositiveInfinity;
            return EnemyState.WanderNearLastPosition;
        }

        SelectClosestWaypoint();
        return EnemyState.ReturnToPatrol;
    }

    private static readonly float[] RadiusShrinkStages = { 1f, 0.5f, 0.25f };

    /// <summary>
    /// Picks a new random point within wanderRadius of wanderAnchor, preferring one the movement
    /// strategy can actually stand on. An unvetted point lands inside a wall often enough that it
    /// used to cost a failed full-map search every time.
    ///
    /// Tries the full radius first, then shrinks it in stages — a tight corridor or alcove can
    /// fail every full-radius candidate while still having walkable space closer to the anchor,
    /// and picking a nearer point beats standing still, which is what the old single-radius
    /// attempt collapsed to.
    /// </summary>
    private void PickRandomWanderTarget()
    {
        const int attemptsPerRadius = 6;

        foreach (float scale in RadiusShrinkStages)
        {
            float radius = wanderRadius * scale;
            for (int i = 0; i < attemptsPerRadius; i++)
            {
                Vector2 candidate = wanderAnchor + Random.insideUnitCircle * radius;
                if (!IsWanderCandidateUsable(candidate)) continue;

                wanderTargetPosition = candidate;
                wanderTargetDeadline = WanderDeadline();
                return;
            }
        }

        // Every candidate at every radius was blocked (enemy boxed in on all sides): stay put
        // rather than commit to a target that is known to be unreachable.
        wanderTargetPosition = transform.position;
        wanderTargetDeadline = WanderDeadline();
    }

    private float WanderDeadline()
    {
        return wanderTargetTimeout > 0f ? Time.time + wanderTargetTimeout : float.PositiveInfinity;
    }

    /// <summary>
    /// Whether a wander point is somewhere this enemy should actually stroll to: standable, and
    /// with open ground the whole way there in a straight line.
    ///
    /// Walkable alone is not enough. A point two steps past a wall is walkable, and the
    /// pathfinder will dutifully find the route to it - around the room, down the corridor, back
    /// up the other side. Nothing downstream flags that as wrong, because it is not unreachable,
    /// merely absurd, and the enemy ends up touring the dungeon on an idle stroll. Wandering is
    /// meant to stay in the room it started in, so the straight line has to be clear.
    /// </summary>
    private bool IsWanderCandidateUsable(Vector2 candidate)
    {
        if (walkabilityProbe == null) return true;

        return walkabilityProbe.IsWalkable(candidate)
            && IsRouteLocallyClear(transform.position, candidate);
    }

    /// <summary>
    /// Whether the straight line between two points stays on walkable ground, sampled at half a
    /// navigation cell so nothing thinner than a wall slips between two samples.
    /// </summary>
    private bool IsRouteLocallyClear(Vector2 from, Vector2 to)
    {
        if (walkabilityProbe == null) return true;

        Vector2 delta = to - from;
        float step = Mathf.Max(0.25f, NavigationSampleSize * 0.5f);
        int steps = Mathf.CeilToInt(delta.magnitude / step);

        for (int i = 1; i <= steps; i++)
        {
            if (!walkabilityProbe.IsWalkable(from + delta * (i / (float)steps)))
                return false;
        }

        return true;
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

        Vector2 overshot = lastKnownPlayerPosition + fromEnemyToLastSeen * Mathf.Max(0f, investigateOvershootDistance);

        // The overshoot exists to clear doorways and corners - which is exactly where it can land
        // inside a wall, or on open ground on the far side of one. Either way the enemy should go
        // to the spot it actually saw the player at rather than take the long way round to a point
        // it invented.
        if (walkabilityProbe != null
            && (!walkabilityProbe.IsWalkable(overshot)
                || !IsRouteLocallyClear(lastKnownPlayerPosition, overshot)))
            return lastKnownPlayerPosition;

        return overshot;
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
    /// Patrol (and post) arrival distance: at least the inspector threshold, at least the movement
    /// strategy's stop distance, and at least what the navigation grid can resolve - a route ends
    /// on a cell centre, so on a coarse grid the enemy cannot get any closer than that however
    /// long it walks.
    /// </summary>
    private float EffectiveWaypointReachedThreshold()
    {
        float threshold = Mathf.Max(waypointReachedThreshold, NavigationSampleSize * 0.75f);

        if (movementStrategy is IMovementArrivalTolerance tol)
            return Mathf.Max(threshold, tol.StopDistanceFromTarget);

        return threshold;
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
            case EnemyState.SearchAhead:
                return searchStepTarget;
            case EnemyState.ReturnToPatrol:
                if (HasValidWaypoint())
                    return waypoints[currentWaypointIndex].position;
                return homePosition;
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

        // A sweep down an unbranching corridor is still the chase: the enemy has one guess left
        // about where the player went and it runs it down. Past the first fork it is guessing,
        // and a guess is walked.
        if (currentState == EnemyState.SearchAhead && !searchIsWalking)
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
            || currentState == EnemyState.WanderNearLastPosition
            || currentState == EnemyState.SearchAhead)
            return true;

        // No route: only the walk back to the post is worth moving for — Idle means standing
        // there (guard) or roaming, which runs in WanderNearLastPosition.
        if (!HasValidWaypoint())
            return currentState == EnemyState.ReturnToPatrol && !IsAtPost();

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
                : null,
            homePos = new[] { homePosition.x, homePosition.y }
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

        if (state.homePos != null && state.homePos.Length >= 2)
            homePosition = new Vector2(state.homePos[0], state.homePos[1]);

        hasLastKnownPlayerPosition =
            state.lastKnownPlayerPos != null && state.lastKnownPlayerPos.Length >= 2;
        lastKnownPlayerPosition = hasLastKnownPlayerPosition
            ? new Vector2(state.lastKnownPlayerPos[0], state.lastKnownPlayerPos[1])
            : Vector2.zero;

        var restoredState = (EnemyState)state.aiState;
        if (restoredState < EnemyState.Idle || restoredState > EnemyState.SearchAhead)
            restoredState = EnemyState.Idle; // unknown value from a foreign/edited save
        if (restoredState == EnemyState.InvestigateLastKnown
            || restoredState == EnemyState.InvestigateNoise
            || restoredState == EnemyState.WanderNearLastPosition
            || restoredState == EnemyState.SearchAhead)
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

        if (ResolvedIdleBehavior == IdleBehavior.Guard)
        {
            // Where this enemy stands guard, and how far off it the enemy currently is.
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(PostPosition, Vector3.one * 0.4f);
            Gizmos.DrawLine(transform.position, PostPosition);
        }
        else if (ResolvedPostInvestigateBehavior == PostInvestigateBehavior.WanderNearLastPosition
            || !HasValidWaypoint())
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(
                Application.isPlaying && currentState == EnemyState.WanderNearLastPosition
                    ? (Vector3)wanderAnchor
                    : (Vector3)PostPosition,
                wanderRadius);
        }

        if (Application.isPlaying && currentState == EnemyState.SearchAhead)
        {
            // Where the sweep is headed next, and how far the probe reaches.
            Gizmos.color = searchIsWalking ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, searchStepTarget);
            Gizmos.DrawWireSphere(searchStepTarget, 0.15f);
        }

        if (idleSoundRadius > 0f &&
            (gizmoDebugSettings == null || gizmoDebugSettings.IsVisible(GizmoRanges.EnemyIdleSound)))
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, idleSoundRadius);
        }
    }
}
