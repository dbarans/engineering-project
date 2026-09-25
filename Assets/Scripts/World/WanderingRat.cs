using UnityEngine;

/// <summary>
/// A live rat that scurries about the floor it was spawned on: short dashes between long
/// pauses, and a bolt for it whenever the player gets close.
///
/// Scenery that moves, not an enemy. It has no collider, no health and nothing to interact
/// with — under this project's convention only walls block movement and sight, and a rat is
/// not a wall. What it is for is what the floor decals are for, except that a decal stops
/// being noticed the second time a corridor is walked and something moving at the edge of
/// the vision cone never does.
///
/// Where it may walk is asked of <see cref="PathfindingGrid"/> rather than of physics,
/// because the rat carries nothing to collide with. That gets props for free: the grid
/// samples obstacle colliders, so a rat will not scurry through a barrel even though
/// nothing would physically stop it.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class WanderingRat : MonoBehaviour
{
    [Header("Scurrying")]
    [Tooltip("How far from where it was spawned the rat will stray, in world units. Its " +
             "home only moves when the player chases it off.")]
    [Min(0f)] [SerializeField] private float wanderRadius = 4f;

    [SerializeField] private float speed = 2.5f;

    [Tooltip("Length of one dash, in world units. A rat crosses a short distance and stops; " +
             "one that glides the whole way over a room reads as a bug, not an animal.")]
    [Min(0.1f)] [SerializeField] private float minDash = 1f;
    [Min(0.1f)] [SerializeField] private float maxDash = 3f;

    [Tooltip("How long it sits still between dashes.")]
    [Min(0f)] [SerializeField] private float minPause = 0.4f;
    [Min(0f)] [SerializeField] private float maxPause = 2.5f;

    [Header("The player")]
    [Tooltip("Inside this distance the rat bolts away instead of wandering, and keeps " +
             "bolting until it is clear. Zero ignores the player entirely.")]
    [Min(0f)] [SerializeField] private float fleeRadius = 3f;

    [SerializeField] private float fleeSpeed = 6f;

    [Header("Facing")]
    [Tooltip("Degrees added to the direction of travel. The generated sprite is drawn " +
             "nose-first along +X, so it needs none; other art may.")]
    [SerializeField] private float spriteFacing;

    [Tooltip("Turn rate in degrees per second. Finite, so a change of direction is a turn " +
             "rather than a teleport.")]
    [Min(1f)] [SerializeField] private float turnSpeed = 900f;

    /// <summary>How many directions are tried before a dash is given up on.</summary>
    private const int TargetAttempts = 8;

    /// <summary>Distance to a target at which it counts as reached.</summary>
    private const float ArrivalEpsilon = 0.05f;

    private PathfindingGrid _grid;
    private Transform _player;

    private Vector2 _home;
    private Vector2 _target;
    private float _pauseLeft;

    private void Start()
    {
        _grid = FindFirstObjectByType<PathfindingGrid>();

        var movement = FindFirstObjectByType<PlayerMovement>();
        if (movement != null) _player = movement.transform;

        _home = transform.position;
        _target = _home;

        // Staggered, so a room's worth of rats spawned in one frame does not move as one
        // body. Plain randomness rather than the dungeon seed on purpose: nothing about a
        // rat is saved, so nothing about it has to come back the same way twice.
        _pauseLeft = Random.Range(0f, maxPause);
    }

    private void Update()
    {
        bool fleeing = IsPlayerClose(out Vector2 away);

        if (fleeing)
        {
            // Re-aimed every frame the player is near rather than once on being startled:
            // a rat walked towards keeps giving ground, which is the whole behaviour. Its
            // home travels with it, so being chased across a room does not leave it
            // tethered to a corner it can no longer reach.
            _pauseLeft = 0f;
            _target = PickDash(away, maxDash, respectHome: false);
            _home = transform.position;
        }
        else if (_pauseLeft > 0f)
        {
            _pauseLeft -= Time.deltaTime;
            return;
        }

        Vector2 position = transform.position;
        Vector2 step = _target - position;

        if (step.sqrMagnitude <= ArrivalEpsilon * ArrivalEpsilon)
        {
            _pauseLeft = Random.Range(minPause, maxPause);
            _target = PickDash(Random.insideUnitCircle.normalized,
                Random.Range(minDash, maxDash), respectHome: true);
            return;
        }

        Vector2 direction = step.normalized;
        float distance = (fleeing ? fleeSpeed : speed) * Time.deltaTime;

        transform.position = position + direction * Mathf.Min(distance, step.magnitude);
        Face(direction);
    }

    /// <summary>
    /// Whether the player is inside <see cref="fleeRadius"/>, and if so which way is away
    /// from them.
    /// </summary>
    private bool IsPlayerClose(out Vector2 away)
    {
        away = Vector2.zero;
        if (_player == null || fleeRadius <= 0f) return false;

        Vector2 offset = (Vector2)transform.position - (Vector2)_player.position;
        if (offset.sqrMagnitude > fleeRadius * fleeRadius) return false;

        // Standing directly on the rat: every direction is away, and normalising a zero
        // vector would leave it sitting there being trodden on.
        away = offset.sqrMagnitude > 0.0001f
            ? offset.normalized
            : Random.insideUnitCircle.normalized;
        return true;
    }

    /// <summary>
    /// A reachable point roughly <paramref name="distance"/> away in roughly
    /// <paramref name="preferred"/>'s direction, or the rat's own position when the floor
    /// around it offers nothing — standing still beats walking into a wall.
    ///
    /// The direction is a preference rather than an instruction: each attempt widens the
    /// spread around it, so a rat cornered against a wall works its way round to a
    /// direction that is actually open instead of pressing into the stone.
    /// </summary>
    private Vector2 PickDash(Vector2 preferred, float distance, bool respectHome)
    {
        Vector2 position = transform.position;

        for (int attempt = 0; attempt < TargetAttempts; attempt++)
        {
            float spread = 180f * attempt / (TargetAttempts - 1f);
            Vector2 direction = Rotate(preferred, Random.Range(-spread, spread));
            Vector2 candidate = position + direction * distance;

            if (respectHome && (candidate - _home).sqrMagnitude > wanderRadius * wanderRadius)
                continue;

            if (!IsPathClear(position, candidate)) continue;

            return candidate;
        }

        return position;
    }

    /// <summary>
    /// Whether the rat may stand at the given point. True when there is no grid to ask:
    /// a hand-built scene has none, and a rat that refuses to move at all there is worse
    /// than one that occasionally clips a wall.
    /// </summary>
    private bool IsFloor(Vector2 world)
    {
        return _grid == null || _grid.IsWalkableWorld(world);
    }

    /// <summary>
    /// Whether every point along the straight line from <paramref name="from"/> to
    /// <paramref name="to"/> is floor, not only the two ends.
    ///
    /// <see cref="PickDash"/> used to check the destination alone, which is enough for a
    /// convex room but not for one with a corner in it, or for a dash aimed across a wall
    /// that happens to have floor on both sides — the straight line between two walkable
    /// points can still cross a cell that is not. Sampled at a fraction of a grid cell
    /// rather than once per cell, so a dash roughly along a wall cannot skip the one cell
    /// that would have stopped it.
    /// </summary>
    private bool IsPathClear(Vector2 from, Vector2 to)
    {
        if (_grid == null) return true;

        float step = Mathf.Max(_grid.CellSize * 0.25f, 0.01f);
        float length = Vector2.Distance(from, to);
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / step));

        for (int i = 1; i <= samples; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)samples);
            if (!_grid.IsWalkableWorld(point)) return false;
        }

        return true;
    }

    private void Face(Vector2 direction)
    {
        float wanted = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + spriteFacing;
        float turned = Mathf.MoveTowardsAngle(
            transform.eulerAngles.z, wanted, turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, turned);
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.7f, 0.4f, 0.5f);
        Vector3 centre = Application.isPlaying ? (Vector3)_home : transform.position;
        Gizmos.DrawWireSphere(centre, wanderRadius);
    }
}
