using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player nail planks across a closed door, turning it from something an enemy
/// simply walks through into an obstacle that has to be chopped down.
///
/// This closes a hole in the chase loop: <see cref="SimpleDoor.ToggleLock"/> can only be
/// used from the outside, so a player who ran into a room and shut the door behind them
/// had no way to hold it — <see cref="EnemyDoorAttacker"/> found an unlocked door and just
/// opened it, costing the chase nothing. A barricade is the inside-the-room answer:
/// it blocks the door for <em>everyone</em> (the player included, which is the whole
/// tension — you are buying time by walling yourself in) and forces the enemy to spend
/// <see cref="SimpleDoor.TakeDamage"/> hits on it before the door itself is even touched.
///
/// Damage routing is one-way and total: while any stage stands the barricade eats every
/// point of damage aimed at the door, and only the overkill from the blow that breaks the
/// last stage spills through. Stages exist so the destruction reads on screen and through
/// the audible <see cref="NoiseEvents"/> pops rather than being one silent health bar.
///
/// Placement: same GameObject as the <see cref="SimpleDoor"/>. Range uses a plain distance
/// check against the player rather than a trigger collider, matching how the door's own
/// Interact Distance works — so the door prefab needs no extra collider.
///
/// Deliberately item-driven rather than prop-driven. When pushable barrels land they can
/// add a stage through <see cref="AddStage"/> without this class changing.
/// </summary>
[RequireComponent(typeof(SimpleDoor))]
public class DoorBarricade : MonoBehaviour
{
    [Header("Material")]
    [Tooltip("Item spent to build one stage. Wood or Scrap; leaving this empty disables barricading entirely.")]
    [SerializeField] private ItemData plankItem;

    [Tooltip("Units of Plank Item consumed per stage built.")]
    [Min(1)]
    [SerializeField] private int planksPerStage = 2;

    [Tooltip("Units handed back per stage dismantled. Below Planks Per Stage, barricading costs material overall.")]
    [Min(0)]
    [SerializeField] private int planksReturnedPerStage = 1;

    [Header("Strength")]
    [Tooltip("How many stages can be stacked on one door.")]
    [Min(1)]
    [SerializeField] private int maxStages = 3;

    [Tooltip("Damage each stage absorbs. At the enemy's default 25 per strike, 60 is roughly three swings per stage.")]
    [Min(1f)]
    [SerializeField] private float healthPerStage = 60f;

    [Header("Interaction")]
    [SerializeField] private Key buildKey = Key.F;
    [SerializeField] private Key dismantleKey = Key.G;

    [Tooltip("How close the player must stand. Keep at or below the door's own Interact Distance.")]
    [SerializeField] private float interactDistance = 2.5f;

    [Header("Noise")]
    [Tooltip("Audible radius of hammering a stage into place. Barricading is loud on purpose — it is a trade, not a free win.")]
    [SerializeField] private float buildNoiseRadius = 9f;

    [Tooltip("Audible radius of a stage splintering apart.")]
    [SerializeField] private float breakNoiseRadius = 7f;

    [Header("Visuals")]
    [Tooltip("One visual per stage, weakest first: element i is shown once stage i+1 stands. Optional — leave empty for no visual.")]
    [SerializeField] private GameObject[] stageVisuals;

    private SimpleDoor door;
    private Transform player;
    private int stages;
    private float health;

    /// <summary>True while at least one stage stands, i.e. the door cannot be opened by anyone.</summary>
    public bool IsBarricaded => stages > 0;

    /// <summary>Number of stages currently standing.</summary>
    public int Stages => stages;

    private void Awake()
    {
        door = GetComponent<SimpleDoor>();
        RefreshVisuals();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        bool build = Keyboard.current[buildKey].wasPressedThisFrame;
        bool dismantle = Keyboard.current[dismantleKey].wasPressedThisFrame;
        if (!build && !dismantle) return;

        // Resolved only on a key press: every door in a generated dungeon runs this Update.
        GameObject playerObject = ResolvePlayer();
        if (playerObject == null) return;

        float distance = Vector2.Distance(playerObject.transform.position, transform.position);
        if (distance > interactDistance) return;

        if (build) TryBuild(playerObject);
        else TryDismantle(playerObject);
    }

    /// <summary>
    /// Absorbs damage aimed at the door. Returns the amount that should still reach the
    /// door itself: zero while any stage stands, and the overkill from the blow that
    /// destroys the last stage.
    /// </summary>
    public float AbsorbDamage(float damage)
    {
        if (stages <= 0 || damage <= 0f) return damage;

        health -= damage;

        if (health > 0f)
        {
            // Ceil, so a stage is only lost once its whole slice is gone.
            int remaining = Mathf.Max(1, Mathf.CeilToInt(health / healthPerStage));
            if (remaining != stages)
            {
                stages = remaining;
                RefreshVisuals();
                NoiseEvents.Emit(transform.position, breakNoiseRadius);
                Debug.Log($"[Barricade] A stage gave way — {stages} left.", this);
            }
            return 0f;
        }

        float leftover = -health;
        stages = 0;
        health = 0f;
        RefreshVisuals();
        NoiseEvents.Emit(transform.position, breakNoiseRadius);
        Debug.Log("[Barricade] Torn down! The door is exposed.", this);
        return leftover;
    }

    /// <summary>
    /// Adds one stage without charging the player any material. The seam for pushable
    /// props (a barrel shoved against the door) to reinforce it once that lands.
    /// Returns false when the door is already at <see cref="maxStages"/>.
    /// </summary>
    public bool AddStage()
    {
        if (stages >= maxStages) return false;

        stages++;
        health = stages * healthPerStage;
        RefreshVisuals();
        return true;
    }

    /// <summary>
    /// Forces the barricade to an exact stage count, recomputing health and refreshing
    /// visuals to match. The restore-from-save entry point — unlike <see cref="AddStage"/>
    /// it does not check whether the door is open or gate the change on a build cost,
    /// since a save is trusted to already describe a valid state.
    /// </summary>
    public void SetStages(int count)
    {
        stages = Mathf.Clamp(count, 0, maxStages);
        health = stages * healthPerStage;
        RefreshVisuals();
    }

    private void TryBuild(GameObject playerObject)
    {
        if (plankItem == null)
        {
            Debug.LogWarning("[Barricade] No Plank Item assigned — this door cannot be barricaded.", this);
            return;
        }

        // An open door has nothing to nail planks to, and letting it be barricaded ajar
        // would freeze it open — the opposite of the point.
        if (door.IsOpen)
        {
            Notify("Close the door before barricading it.");
            return;
        }

        if (stages >= maxStages)
        {
            Notify("This door is barricaded as far as it will go.");
            return;
        }

        SlotInventory inventory = playerObject.GetComponent<SlotInventory>();
        if (inventory == null) return;

        if (!ConsumePlanks(inventory, planksPerStage))
        {
            Notify($"Not enough {plankItem.itemName} — {planksPerStage} needed.");
            return;
        }

        AddStage();
        NoiseEvents.Emit(transform.position, buildNoiseRadius);
        Debug.Log($"[Barricade] Stage {stages}/{maxStages} nailed on.", this);
    }

    private void TryDismantle(GameObject playerObject)
    {
        if (stages <= 0) return;

        SlotInventory inventory = playerObject.GetComponent<SlotInventory>();
        if (inventory == null) return;

        // Checked up front so a full inventory cancels the dismantle instead of
        // silently destroying the planks it cannot hand back.
        if (planksReturnedPerStage > 0 && !HasRoomForPlanks(inventory, planksReturnedPerStage))
        {
            Notify("No room to carry the planks.");
            return;
        }

        stages--;
        health = stages * healthPerStage;
        RefreshVisuals();

        if (planksReturnedPerStage > 0)
        {
            ReturnPlanks(inventory, planksReturnedPerStage);
        }

        Debug.Log($"[Barricade] Stage pried off — {stages}/{maxStages} left.", this);
    }

    private bool ConsumePlanks(SlotInventory inventory, int amount)
    {
        int available = inventory.Backpack.Count(plankItem) + inventory.Hotbar.Count(plankItem);
        if (available < amount) return false;

        // Backpack first, then hotbar — the same order SimpleDoor spends a key in,
        // so the hotbar keeps whatever the player deliberately put there for as long
        // as possible.
        int taken = inventory.Backpack.TryRemove(plankItem, amount);
        if (taken < amount)
        {
            taken += inventory.Hotbar.TryRemove(plankItem, amount - taken);
        }

        return taken >= amount;
    }

    private bool HasRoomForPlanks(SlotInventory inventory, int amount)
    {
        return inventory.Backpack.CanAdd(plankItem, amount) || inventory.Hotbar.CanAdd(plankItem, amount);
    }

    private void ReturnPlanks(SlotInventory inventory, int amount)
    {
        int leftover = inventory.Backpack.TryAddItem(plankItem, amount);
        if (leftover > 0)
        {
            leftover = inventory.Hotbar.TryAddItem(plankItem, leftover);
        }

        if (leftover > 0)
        {
            Debug.LogWarning($"[Barricade] {leftover} {plankItem.itemName} did not fit and was lost.", this);
        }
    }

    private void RefreshVisuals()
    {
        if (stageVisuals == null) return;

        for (int i = 0; i < stageVisuals.Length; i++)
        {
            if (stageVisuals[i] != null)
            {
                stageVisuals[i].SetActive(i < stages);
            }
        }
    }

    private GameObject ResolvePlayer()
    {
        if (player != null) return player.gameObject;

        GameObject found = GameObject.FindWithTag("Player");
        player = found != null ? found.transform : null;
        return found;
    }

    private void Notify(string message)
    {
        Debug.Log($"[Barricade] {message}", this);
        FindFirstObjectByType<ToastUI>()?.Show(message);
    }
}
