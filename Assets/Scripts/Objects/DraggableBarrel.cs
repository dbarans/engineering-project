using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages player interaction for dragging a barrel with obstacle collision checks.
/// Restricts dragging to the closest barrel, synchronizes movement using Rigidbody2D casting
/// to prevent clipping through walls, and caps the player's dragging speed.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class DraggableBarrel : MonoBehaviour
{
    [Header("Drag Settings")]
    [Tooltip("Maximum interaction distance from the player to initiate dragging.")]
    [SerializeField] private float grabDistance = 1.6f;

    [Tooltip("Key required to be held down to drag the barrel.")]
    [SerializeField] private Key dragKey = Key.F;

    [Tooltip("Movement speed cap applied to both the barrel and the player during dragging.")]
    [SerializeField] private float dragSpeed = 2.2f;

    [Tooltip("Layers considered solid obstacles that block the barrel from moving (e.g. Walls, Environment).")]
    [SerializeField] private LayerMask obstacleLayerMask;

    private Rigidbody2D _barrelRb;
    private Collider2D _barrelCol;
    private Transform _playerTransform;
    private Rigidbody2D _playerRb;

    private bool _isDragging = false;
    private Vector2 _grabOffset;
    private readonly RaycastHit2D[] _castHits = new RaycastHit2D[8];
    private ContactFilter2D _castFilter;

    private static DraggableBarrel _currentlyDraggedBarrel;

    /// <summary>
    /// Currently dragged barrel instance across the scene.
    /// </summary>
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
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerTransform = player.transform;
            _playerRb = player.GetComponent<Rigidbody2D>();
        }
    }

    private void Update()
    {
        if (_playerTransform == null || Keyboard.current == null) return;

        if (_barrelRb.bodyType == RigidbodyType2D.Static)
        {
            if (_isDragging) StopDragging();
            return;
        }

        float distance = Vector2.Distance(transform.position, _playerTransform.position);
        bool isKeyPressed = Keyboard.current[dragKey].isPressed;

        if (_isDragging)
        {
            // Release drag if key is released or if the barrel was blocked by a wall and player walked away
            if (!isKeyPressed || distance > grabDistance + 0.8f)
            {
                StopDragging();
            }
        }
        else if (isKeyPressed && distance <= grabDistance && _currentlyDraggedBarrel == null)
        {
            if (IsClosestBarrelToPlayer())
            {
                StartDragging();
            }
        }
    }

    /// <summary>
    /// Checks if this barrel instance is the closest one to the player among all active draggable barrels.
    /// </summary>
    /// <returns>True if closest, false otherwise.</returns>
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

        // Cap player movement velocity so they cannot outrun the dragging speed
        if (_playerRb != null && _playerRb.linearVelocity.magnitude > dragSpeed)
        {
            _playerRb.linearVelocity = _playerRb.linearVelocity.normalized * dragSpeed;
        }

        Vector2 currentPosition = _barrelRb.position;
        Vector2 targetPosition = (Vector2)_playerTransform.position + _grabOffset;
        Vector2 movementDelta = targetPosition - currentPosition;
        float distanceToTarget = movementDelta.magnitude;

        if (distanceToTarget > 0.001f)
        {
            Vector2 moveDirection = movementDelta / distanceToTarget;
            float maxStep = dragSpeed * Time.fixedDeltaTime;
            float stepDistance = Mathf.Min(distanceToTarget, maxStep);

            // Cast the collider forward to detect solid walls before moving
            _castFilter.layerMask = obstacleLayerMask;
            int hitCount = _barrelRb.Cast(moveDirection, _castFilter, _castHits, stepDistance + 0.02f);

            if (hitCount > 0)
            {
                // Wall hit: adjust travel distance so the barrel stops right at the surface
                float allowedDistance = Mathf.Max(0f, _castHits[0].distance - 0.02f);
                Vector2 newPosition = currentPosition + moveDirection * allowedDistance;
                _barrelRb.MovePosition(newPosition);
            }
            else
            {
                // Free path: move towards the target
                Vector2 newPosition = currentPosition + moveDirection * stepDistance;
                _barrelRb.MovePosition(newPosition);
            }
        }
    }

    /// <summary>
    /// Locks the barrel into dragging mode and calculates initial grab offset.
    /// </summary>
    private void StartDragging()
    {
        _isDragging = true;
        _currentlyDraggedBarrel = this;
        _grabOffset = (Vector2)transform.position - (Vector2)_playerTransform.position;
    }

    /// <summary>
    /// Releases the barrel from dragging mode and resets velocity.
    /// </summary>
    private void StopDragging()
    {
        _isDragging = false;
        if (_currentlyDraggedBarrel == this)
        {
            _currentlyDraggedBarrel = null;
        }

        _barrelRb.linearVelocity = Vector2.zero;
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