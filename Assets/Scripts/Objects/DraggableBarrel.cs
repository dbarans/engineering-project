using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages player interaction for pulling/dragging a barrel.
/// The barrel only follows when the player moves away from it (pull-only),
/// cannot be pushed forward, and detects obstacle collisions via Rigidbody2D casting.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class DraggableBarrel : MonoBehaviour
{
    [Header("Drag Settings")]
    [Tooltip("Maximum interaction distance to start dragging.")]
    [SerializeField] private float grabDistance = 1.6f;

    [Tooltip("Key held down to drag.")]
    [SerializeField] private Key dragKey = Key.F;

    [Tooltip("Movement speed during dragging.")]
    [SerializeField] private float dragSpeed = 2.0f;

    [Tooltip("Layers considered solid obstacles that block the barrel.")]
    [SerializeField] private LayerMask obstacleLayerMask;

    private Rigidbody2D _barrelRb;
    private Collider2D _barrelCol;
    private Transform _playerTransform;
    private PlayerMovement _playerMovement;

    private bool _isDragging = false;
    private float _initialHoldDistance;

    /// <summary>
    /// Where the barrel stood when this drag began. The navigation grid has to hear about both
    /// ends of the move — the cells being freed and the ones being blocked — and by the time
    /// the drag ends the starting footprint is no longer readable from the collider.
    /// </summary>
    private Bounds _dragStartFootprint;

    private readonly RaycastHit2D[] _castHits = new RaycastHit2D[8];
    private ContactFilter2D _castFilter;

    private static DraggableBarrel _currentlyDraggedBarrel;
    public static DraggableBarrel CurrentlyDraggedBarrel => _currentlyDraggedBarrel;

    private void Awake()
    {
        _barrelRb = GetComponent<Rigidbody2D>();
        _barrelCol = GetComponent<Collider2D>();

        _barrelRb.bodyType = RigidbodyType2D.Kinematic;
        _barrelRb.gravityScale = 0f;
        _barrelRb.useFullKinematicContacts = true;

        _castFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstacleLayerMask,
            useTriggers = false
        };
    }

    private void Start()
    {
        EnsurePlayerReference();
    }

    private void EnsurePlayerReference()
    {
        if (_playerTransform != null && _playerMovement != null) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerTransform = player.transform;
            _playerMovement = player.GetComponent<PlayerMovement>();
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (_playerTransform == null) EnsurePlayerReference();
        if (_playerTransform == null) return;

        if (_barrelRb.bodyType == RigidbodyType2D.Static)
        {
            if (_isDragging) StopDragging();
            return;
        }

        float currentDist = Vector2.Distance(transform.position, _playerTransform.position);
        bool isKeyPressed = Keyboard.current[dragKey].isPressed;

        if (_isDragging)
        {
            if (!isKeyPressed || currentDist > grabDistance + 1.2f)
            {
                StopDragging();
            }
        }
        else if (isKeyPressed && currentDist <= grabDistance && _currentlyDraggedBarrel == null)
        {
            if (IsClosestBarrelToPlayer())
            {
                StartDragging(currentDist);
            }
        }
    }

    private bool IsClosestBarrelToPlayer()
    {
        var allBarrels = FindObjectsByType<DraggableBarrel>(FindObjectsSortMode.None);
        float myDist = Vector2.Distance(transform.position, _playerTransform.position);

        foreach (var barrel in allBarrels)
        {
            if (barrel == this || !barrel.enabled) continue;

            float otherDist = Vector2.Distance(barrel.transform.position, _playerTransform.position);
            if (otherDist < myDist)
            {
                return false;
            }
        }

        return true;
    }

    private void FixedUpdate()
    {
        if (!_isDragging || _playerTransform == null) return;

        Vector2 currentBarrelPos = _barrelRb.position;
        Vector2 playerPos = _playerTransform.position;
        Vector2 toPlayer = playerPos - currentBarrelPos;
        float currentDist = toPlayer.magnitude;

        if (currentDist > _initialHoldDistance)
        {
            Vector2 pullDir = toPlayer.normalized;
            float excessDistance = currentDist - _initialHoldDistance;

            _castFilter.layerMask = obstacleLayerMask;
            int hitCount = _barrelRb.Cast(pullDir, _castFilter, _castHits, excessDistance + 0.02f);

            float allowedDistance = excessDistance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _castHits[i];

                // A surface the barrel already rests against reports distance 0 whichever way
                // the cast points, and its normal is not meaningful either — so the raw hit
                // cannot tell "a wall lies ahead" from "we are leaning on one". Taking it at
                // face value pinned the barrel the moment it met a wall, including while the
                // player pulled it away, which is the one direction that frees it. Dragging a
                // barrel into a corridor did it every time: the walls are a barrel-width apart.
                //
                // Physics2D.Distance answers that question even while two colliders overlap:
                // its normal runs from the barrel toward the other collider, so a pull that
                // points against it is a pull away from the contact and nothing to clamp.
                ColliderDistance2D separation = Physics2D.Distance(_barrelCol, hit.collider);
                if (separation.distance <= 0.01f && Vector2.Dot(separation.normal, pullDir) < 0f)
                    continue;

                allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0f, hit.distance - 0.02f));
            }

            if (allowedDistance >= excessDistance)
            {
                Vector2 targetPos = playerPos - (pullDir * _initialHoldDistance);
                _barrelRb.MovePosition(targetPos);
            }
            else
            {
                _barrelRb.MovePosition(currentBarrelPos + pullDir * allowedDistance);
            }
        }
    }

    private void StartDragging(float initialDistance)
    {
        EnsurePlayerReference();
        _isDragging = true;
        _currentlyDraggedBarrel = this;
        _initialHoldDistance = Mathf.Max(initialDistance, 0.8f);
        _dragStartFootprint = _barrelCol != null
            ? _barrelCol.bounds
            : new Bounds(transform.position, Vector3.one);

        if (_playerMovement != null)
        {
            _playerMovement.SetDragging(true, dragSpeed);
        }
    }

    private void StopDragging()
    {
        _isDragging = false;
        if (_currentlyDraggedBarrel == this)
        {
            _currentlyDraggedBarrel = null;
        }

        if (_playerMovement != null)
        {
            _playerMovement.SetDragging(false);
        }

        _barrelRb.linearVelocity = Vector2.zero;

        // A barrel is an obstacle the navigation grid sampled once, when it was built, so a
        // dragged one leaves its old cells blocked and its new ones open until it is told
        // otherwise — which made blocking a corridor with a barrel do nothing to enemies, and
        // clearing one out of the way do nothing either. Both footprints go in one refresh,
        // since the grid relabels its regions per call and the drag is a single move.
        if (PathfindingGrid.Active != null)
        {
            Bounds moved = _dragStartFootprint;
            moved.Encapsulate(_barrelCol != null ? _barrelCol.bounds : new Bounds(transform.position, Vector3.one));
            PathfindingGrid.Active.RefreshArea(moved);
        }
    }

    private void OnDisable()
    {
        if (_isDragging) StopDragging();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, grabDistance);
    }
}