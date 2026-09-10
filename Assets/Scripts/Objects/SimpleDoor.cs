using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Controls interactive door mechanics: entities push doors away from themselves, with support for key locks, manual locking, and two-way sprint breaching.
/// </summary>
[RequireComponent(typeof(SaveableEntity))]
public class SimpleDoor : MonoBehaviour, ISaveableComponent
{
    [Header("Hierarchy References")]
    [SerializeField] private Transform doorSystemTransform;

    [Header("Rotation Settings")]
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 5f;

    [Header("Mouse / Interaction Range")]
    [SerializeField] private float interactDistance = 4f;

    [Header("Lock Settings")]
    [SerializeField] private bool isLocked = false;
    [SerializeField] private bool lockOnlyFromOutside = true;

    [Header("Key System")]
    [SerializeField] private bool requiresKeyToOpen = false;
    [SerializeField] private ItemData requiredKeyItem;

    [Tooltip("The door cannot be rammed open, damaged or destroyed — ever, and unlike the " +
             "key lock this does not lapse once the door has been opened. For doors whose " +
             "whole purpose is that there is exactly one way through them: the dungeon's " +
             "exit door is the one today.")]
    [SerializeField] private bool isReinforced = false;

    [Header("Sprint Ramming")]
    [SerializeField] private float staminaCostForRamming = 25f;
    [Tooltip("Damage one sprint ram deals — to the door, or to the barricade when one is up.")]
    [SerializeField] private float ramDamage = 15f;

    [Header("HP System and Destruction")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private GameObject destroyedDoorVisual;

    private float currentHealth;
    private bool isDestroyed = false;
    private bool isOpen = false;

    // Which way the leaf last swung, -1 or +1. Picked in ToggleDoor from the side the
    // opener stood on, and carried into the save because restore has to put the door back
    // on the side it actually opened towards. Restoring on a fixed side swung half the
    // open doors into the wall they were hinged against.
    private float openDirection = -1f;

    // Where the leaf sits relative to its hinge, which never changes — opening turns the
    // hinge, and the leaf rides along. The save records the leaf's world position, and on
    // an open door that is the swung-out one; writing it back before the hinge has been
    // turned, and then turning the hinge, displaced the leaf twice. Restoring this puts it
    // back on the hinge first.
    private Vector3 hingedLocalPosition;

    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private Camera mainCamera;
    private Collider2D doorCollider;
    private SpriteRenderer doorSpriteRenderer;
    private DoorBarricade barricade;

    /// <summary>
    /// Gets a value indicating whether the door is currently locked.
    /// </summary>
    public bool IsLocked => isLocked;

    /// <summary>
    /// Gets a value indicating whether planks are nailed across this door. A barricaded
    /// door cannot be opened from either side and soaks damage before the door itself
    /// takes any — see <see cref="DoorBarricade"/>. False when the door has no barricade
    /// component at all, which is the case for every door that was never set up for it.
    /// </summary>
    public bool IsBarricaded => barricade != null && barricade.IsBarricaded;

    /// <summary>
    /// Gets a value indicating whether the door is currently open.
    /// </summary>
    public bool IsOpen => isOpen;

    /// <summary>
    /// Locks the door behind a key, as the generator does for the exit room's doorways.
    ///
    /// Exists because the two fields it writes are serialized and private — the sane
    /// default, since a door's lock is a level-design decision — while a procedural
    /// dungeon has no inspector to set them in. Writing them through a method rather than
    /// widening the fields keeps the rest of the door's state machine the only thing that
    /// can clear the lock afterwards, which is exactly once, when the key is spent.
    ///
    /// A null <paramref name="key"/> is refused rather than silently accepted: a door that
    /// requires a key nobody can hold is not locked, it is broken, and
    /// <see cref="ToggleDoor"/> would only be able to log about it once per attempt.
    /// </summary>
    /// <param name="key">The item consumed to open this door. Must not be null.</param>
    /// <param name="reinforced">
    /// Also make the door permanently immune to ramming and damage. The key lock alone
    /// grants that immunity only while the door is still locked, which is not enough for a
    /// door that must never be passable by force: spending the key clears the lock, and
    /// with it the protection.
    /// </param>
    public void RequireKey(ItemData key, bool reinforced = false)
    {
        if (key == null)
        {
            Debug.LogWarning("[Door] RequireKey was given no key item; the door stays unlocked.", this);
            return;
        }

        requiresKeyToOpen = true;
        requiredKeyItem = key;
        if (reinforced) isReinforced = true;

        PersistInEditor();
    }

    /// <summary>
    /// Makes the door permanently immune to ramming and to damage, with no key involved.
    /// </summary>
    public void Reinforce()
    {
        isReinforced = true;
        PersistInEditor();
    }

    /// <summary>Logs the message and puts it on the player's HUD, as the barricade does.</summary>
    private void Notify(string message)
    {
        Debug.Log($"[Door] {message}", this);
        FindFirstObjectByType<ToastUI>()?.Show(message);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Writes edit-mode changes into the scene file.
    ///
    /// The Dungeon scene is authored by baking — generate once in the editor, then save —
    /// and the generator configures doors through the methods above. Without this the lock
    /// exists in memory only: the door looks locked until the next domain reload, then
    /// comes back openable by anyone, which is exactly how the exit door ended up opening
    /// without the key.
    ///
    /// Both calls are needed and they do different jobs. <c>SetDirty</c> marks the
    /// component as changed; <c>RecordPrefabInstancePropertyModifications</c> is what turns
    /// the changed fields into prefab-instance overrides. Generated doors are prefab
    /// instances on purpose (see <c>PrefabRegistry.InstantiateFor</c>), and an override that
    /// was never recorded is discarded when the instance next reconciles with its prefab.
    /// </summary>
    private void PersistInEditor()
    {
        if (Application.isPlaying) return;

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
    }
#else
    private void PersistInEditor() { }
#endif

    private void Awake()
    {
        mainCamera = Camera.main;

        if (doorSystemTransform == null)
            doorSystemTransform = transform;

        closedRotation = doorSystemTransform.localRotation;
        targetRotation = closedRotation;
        hingedLocalPosition = transform.localPosition;

        currentHealth = maxHealth;
        doorCollider = GetComponent<Collider2D>();
        doorSpriteRenderer = GetComponent<SpriteRenderer>();
        barricade = GetComponent<DoorBarricade>();
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
    /// Toggles the door state: consumes a key if required, checks for locks, and pushes the door away from the opener.
    /// </summary>
    /// <param name="opener">The transform of the entity opening the door (Player or Enemy).</param>
    public void ToggleDoor(Transform opener = null)
    {
        if (isDestroyed) return;

        // Checked before the key and lock branches: planks are nailed across the frame,
        // so they stop the owner of the key just as surely as they stop everything else.
        // Deliberately symmetric — walling yourself in is the cost of the time it buys.
        if (IsBarricaded && !isOpen)
        {
            Debug.Log("[Door] Barricaded shut — the planks have to come off first.");
            return;
        }

        if (requiresKeyToOpen && !isOpen)
        {
            if (opener != null && requiredKeyItem != null)
            {
                SlotInventory inventory = opener.GetComponent<SlotInventory>();
                if (inventory != null)
                {
                    bool keyUsed = false;

                    if (inventory.Backpack.TryRemove(requiredKeyItem, 1) > 0)
                    {
                        keyUsed = true;
                    }
                    else if (inventory.Hotbar.TryRemove(requiredKeyItem, 1) > 0)
                    {
                        keyUsed = true;
                    }

                    if (keyUsed)
                    {
                        requiresKeyToOpen = false;
                        Notify($"Unlocked with the {requiredKeyItem.itemName}.");
                        AudioService.PlayAt(SoundId.DoorUnlock, transform.position);
                    }
                    else
                    {
                        // On the HUD rather than only in the console: a generated dungeon
                        // locks doors the player is meant to *decide* about, and a decision
                        // needs the door to say which key it wants and that it is locked at
                        // all, rather than reading as a door that just does not open.
                        Notify($"Locked. Needs the {requiredKeyItem.itemName}.");
                        return;
                    }
                }
                else
                {
                    Debug.Log("[Door] Opener has no SlotInventory component!");
                    return;
                }
            }
            else
            {
                Debug.LogWarning("[Door] Requires key, but no key item is assigned in Inspector or opener is null!");
                return;
            }
        }

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

            openDirection = direction;
            targetRotation = closedRotation * Quaternion.Euler(0, 0, openAngle * direction);
            AudioService.PlayAt(SoundId.DoorOpen, transform.position);
            Debug.Log($"[Door] Opened away from {(opener != null ? opener.name : "Unknown")}!");
        }
        else
        {
            targetRotation = closedRotation;
            AudioService.PlayAt(SoundId.DoorClose, transform.position);
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

        // Only the unlocking half sounds. Locking has no id of its own yet, and reusing the
        // unlock clip for it would make the two states indistinguishable by ear — the one
        // thing the sound is there to tell the player.
        if (!isLocked) AudioService.PlayAt(SoundId.DoorUnlock, transform.position);
    }

    /// <summary>
    /// Applies damage to the door and triggers destruction if health reaches zero.
    /// Key-locked doors are immune to physical damage.
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        if (isDestroyed) return;

        // Ahead of the key-locked immunity below: a reinforced door the player has also
        // barricaded should still lose its planks, otherwise barricading the sturdiest
        // door in the dungeon would make it permanently unopenable by anyone.
        if (barricade != null)
        {
            damageAmount = barricade.AbsorbDamage(damageAmount);
            if (damageAmount <= 0f) return;
        }

        // Reinforcement outlasts the lock. A key-locked door stops taking damage only while
        // it is still locked, and spending the key clears that — which would leave the exit
        // door breakable the moment it had been opened once.
        if (isReinforced)
        {
            Debug.Log("[Door] This door is reinforced. It cannot be damaged.");
            return;
        }

        if (requiresKeyToOpen)
        {
            Debug.Log("[Door] This door is too sturdy! Melee attacks deal no damage.");
            return;
        }

        currentHealth -= damageAmount;
        AudioService.PlayAt(SoundId.DoorHit, transform.position);
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
        currentHealth = 0f;
        AudioService.PlayAt(SoundId.DoorDestroy, transform.position);
        Debug.Log("[Door] Fully destroyed!");

        ApplyDestroyedState();
    }

    private void ApplyDestroyedState()
    {
        if (doorCollider != null) doorCollider.enabled = false;
        if (doorSpriteRenderer != null) doorSpriteRenderer.enabled = false;

        foreach (var col in GetComponentsInChildren<Collider2D>(true))
            col.enabled = false;

        foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (destroyedDoorVisual != null && sr.transform.IsChildOf(destroyedDoorVisual.transform))
                continue;
            sr.enabled = false;
        }

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

        // A barricade is rammed apart plank by plank rather than opened, and that holds
        // even on a key-locked door — so this runs before the reinforcement check below.
        // Without it the ram would clear isLocked on a door it never actually opened.
        if (IsBarricaded)
        {
            RamBarricade(collision.gameObject);
            return;
        }

        if (isReinforced)
        {
            Debug.Log("[Door] This door is reinforced. Ramming it does nothing.");
            return;
        }

        if (requiresKeyToOpen)
        {
            Notify(requiredKeyItem != null
                ? $"The lock holds. Only the {requiredKeyItem.itemName} opens this."
                : "The lock holds — this door does not give to force.");
            return;
        }

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
                        TakeDamage(ramDamage);

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

    /// <summary>
    /// A sprinting player throwing their weight against a barricade — their own, usually.
    /// Costs the same stamina as a normal ram, but the force goes into the planks instead
    /// of the hinges, and it never opens the door: the barricade has to fall first.
    /// </summary>
    private void RamBarricade(GameObject hittingObject)
    {
        if (!hittingObject.CompareTag("Player")) return;

        var movement = hittingObject.GetComponent<PlayerMovement>();
        var stamina = hittingObject.GetComponent<PlayerStaminaSystem>();
        if (movement == null || stamina == null) return;

        if (movement.CurrentMode != PlayerMovement.MovementMode.Sprint) return;

        if (stamina.CurrentStamina < staminaCostForRamming)
        {
            Debug.Log("[Door] Not enough stamina to break through the barricade!");
            return;
        }

        stamina.UseRamStamina(staminaCostForRamming);
        barricade.AbsorbDamage(ramDamage);
        Debug.Log("[Door] Slammed into the barricade!");
    }

    #region ISaveableComponent Implementation

    public string TypeTag => "door";

    [System.Serializable]
    private class DoorSaveData
    {
        public bool isDestroyed;
        public bool isOpen;
        public bool isLocked;
        public bool requiresKey;
        public float health;

        // Defaults to the side ToggleDoor picks when it has no opener, so a save written
        // before this field existed restores exactly as it used to.
        public float openDirection = -1f;
    }

    public string CapturePayload()
    {
        var data = new DoorSaveData
        {
            isDestroyed = isDestroyed,
            isOpen = isOpen,
            isLocked = isLocked,
            requiresKey = requiresKeyToOpen,
            health = currentHealth,
            openDirection = openDirection
        };
        return JsonConvert.SerializeObject(data);
    }

    public void RestorePayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        var data = JsonConvert.DeserializeObject<DoorSaveData>(payload);
        if (data == null) return;

        isDestroyed = data.isDestroyed;
        isOpen = data.isOpen;
        isLocked = data.isLocked;
        requiresKeyToOpen = data.requiresKey;
        currentHealth = data.health;
        openDirection = data.openDirection;

        // Undo the world position the entity restore wrote over us: the leaf's place is
        // decided by its hinge, not by where it happened to be swung to when saving.
        transform.localPosition = hingedLocalPosition;

        if (isDestroyed)
        {
            ApplyDestroyedState();
        }
        else
        {
            if (isOpen)
            {
                targetRotation = closedRotation * Quaternion.Euler(0, 0, openAngle * openDirection);
                doorSystemTransform.localRotation = targetRotation;
            }
            else
            {
                targetRotation = closedRotation;
                doorSystemTransform.localRotation = closedRotation;
            }
        }
    }

    #endregion
}