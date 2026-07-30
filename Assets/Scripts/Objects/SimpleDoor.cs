using UnityEngine;

/// <summary>
/// Controls interactive door mechanics: entities push doors away from themselves to avoid collision overlap, with two-way sprint breaching.
/// </summary>
public class SimpleDoor : MonoBehaviour
{
    [Header("Rotation Settings")]
    [SerializeField] private Transform doorSystemTransform;
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 5f;

    [Header("Mouse / Interaction Range")]
    [SerializeField] private float interactDistance = 4f;

    [Header("Lock Settings")]
    [SerializeField] private bool isLocked = false;
    [SerializeField] private bool lockOnlyFromOutside = true;

    [Header("Sprint Ramming")]
    [SerializeField] private float staminaCostForRamming = 25f;

    [Header("HP System and Destruction")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private GameObject destroyedDoorVisual;

    private float currentHealth;
    private bool isDestroyed = false;
    private bool isOpen = false;

    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private Camera mainCamera;
    private Collider2D doorCollider;
    private SpriteRenderer doorSpriteRenderer;

    /// <summary>
    /// Gets a value indicating whether the door is currently locked.
    /// </summary>
    public bool IsLocked => isLocked;

    /// <summary>
    /// Gets a value indicating whether the door is currently open.
    /// </summary>
    public bool IsOpen => isOpen;

    private void Awake()
    {
        mainCamera = Camera.main;

        if (doorSystemTransform == null)
            doorSystemTransform = transform;

        closedRotation = doorSystemTransform.localRotation;
        targetRotation = closedRotation;

        currentHealth = maxHealth;
        doorCollider = GetComponent<Collider2D>();
        doorSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (isDestroyed) return;

        if (Input.GetMouseButtonDown(0))
        {
            TryInteract(isLockAction: false);
        }

        if (Input.GetMouseButtonDown(1))
        {
            TryInteract(isLockAction: true);
        }

        doorSystemTransform.localRotation = Quaternion.Slerp(
            doorSystemTransform.localRotation,
            targetRotation,
            Time.deltaTime * openSpeed
        );
    }

    /// <summary>
    /// Performs a raycast from the mouse to trigger door toggling or locking within interaction range.
    /// </summary>
    private void TryInteract(bool isLockAction)
    {
        if (isDestroyed) return;
        if (mainCamera == null) mainCamera = Camera.main;

        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 mousePos2D = new Vector2(mouseWorldPos.x, mouseWorldPos.y);
        RaycastHit2D hit = Physics2D.Raycast(mousePos2D, Vector2.zero);

        if (hit.collider != null && (hit.transform == transform || hit.transform.IsChildOf(transform)))
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                float dist = Vector2.Distance(player.transform.position, doorSystemTransform.position);
                if (dist <= interactDistance)
                {
                    if (isLockAction)
                    {
                        ToggleLock(player);
                    }
                    else
                    {
                        ToggleDoor(player.transform);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Toggles the door state: dynamically pushes the door away from the opener.
    /// </summary>
    /// <param name="opener">The transform of the entity opening the door (Player or Enemy).</param>
    public void ToggleDoor(Transform opener = null)
    {
        if (isDestroyed) return;

        if (isLocked)
        {
            Debug.Log("[Door] Cannot open, door is locked!");
            return;
        }
        isOpen = !isOpen;

        if (isOpen)
        {
            float direction = -1f;

            if (opener == null)
            {
                GameObject player = GameObject.FindWithTag("Player");
                if (player != null) opener = player.transform;
            }

            if (opener != null)
            {
                Vector2 doorToOpener = opener.position - transform.position;
                bool isOpenerInFront = Vector2.Dot(transform.right, doorToOpener) >= 0;
                direction = isOpenerInFront ? -1f : 1f;
            }

            targetRotation = closedRotation * Quaternion.Euler(0, 0, openAngle * direction);
            Debug.Log($"[Door] Opened away from {(opener != null ? opener.name : "Unknown")}!");
        }
        else
        {
            targetRotation = closedRotation;
            Debug.Log("[Door] Closed!");
        }
    }

    /// <summary>
    /// Toggles the locked state of the door if it is closed (can only be locked from the outside).
    /// </summary>
    public void ToggleLock(GameObject interactingPlayer = null)
    {
        if (isDestroyed) return;

        if (isOpen)
        {
            Debug.Log("[Door] Cannot lock an open door! Close it first.");
            return;
        }

        if (lockOnlyFromOutside && interactingPlayer != null)
        {
            Vector2 directionToPlayer = interactingPlayer.transform.position - transform.position;
            bool isOutside = Vector2.Dot(transform.right, directionToPlayer) >= 0;

            if (!isOutside)
            {
                Debug.Log("[Door] You can only lock the door from the outside!");
                return;
            }
        }

        isLocked = !isLocked;
        Debug.Log(isLocked ? "[Door] Locked from the outside!" : "[Door] Unlocked!");
    }

    /// <summary>
    /// Applies damage to the door and triggers destruction if health reaches zero.
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        if (isDestroyed) return;
        currentHealth -= damageAmount;
        Debug.Log($"[Door] Received {damageAmount} damage! Remaining HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
        {
            DestroyDoor();
        }
    }

    /// <summary>
    /// Disables collision/renderers and activates destroyed visuals.
    /// </summary>
    private void DestroyDoor()
    {
        isDestroyed = true;
        Debug.Log("[Door] Fully destroyed!");

        if (doorCollider != null) doorCollider.enabled = false;
        if (doorSpriteRenderer != null) doorSpriteRenderer.enabled = false;

        if (destroyedDoorVisual != null)
        {
            destroyedDoorVisual.SetActive(true);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDestroyed) return;

        CheckRamming(collision);
    }

    /// <summary>
    /// Checks if a sprinting player rams into the door from either side to force it open.
    /// </summary>
    private void CheckRamming(Collision2D collision)
    {
        if (isOpen || isDestroyed) return;

        GameObject hittingObject = collision.gameObject;

        if (hittingObject.CompareTag("Player"))
        {
            var movement = hittingObject.GetComponent<PlayerMovement>();
            var stamina = hittingObject.GetComponent<PlayerStaminaSystem>();

            if (movement != null && stamina != null)
            {
                if (movement.CurrentMode == PlayerMovement.MovementMode.Sprint)
                {
                    if (stamina.CurrentStamina >= staminaCostForRamming)
                    {
                        stamina.UseRamStamina(staminaCostForRamming);
                        TakeDamage(15f);

                        if (!isDestroyed)
                        {
                            isLocked = false;
                            ToggleDoor(hittingObject.transform);
                        }
                        Debug.Log($"[Door] Rammed open from {(Vector2.Dot(transform.right, transform.position - hittingObject.transform.position) >= 0 ? "outside" : "inside")}!");
                    }
                    else
                    {
                        Debug.Log("[Door] Not enough stamina to breach!");
                    }
                }
            }
        }
    }
}