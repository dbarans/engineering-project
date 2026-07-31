using System;
using UnityEngine;

public class RangedAttack : PlayerAttack
{
    [Header("Ranged Settings")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform shootPoint;

    [Header("Ammo")]
    [Tooltip("Item consumed by each shot. One unit is spent per shot; with none in the " +
             "inventory the weapon cannot fire. Leave empty to disable the ammo requirement.")]
    [SerializeField] private ItemData ammoItem;

    [Tooltip("Inventory the ammo is drawn from. Auto-resolved at runtime if left unset.")]
    [SerializeField] private SlotInventory inventory;

    [Header("Accuracy Settings")]
    [SerializeField] private float maxSpreadAngle = 40f;
    [SerializeField] private float minSpreadAngle = 2f;
    [SerializeField] private float lineLength = 5f;

    [Header("Visuals")]
    [SerializeField] private LineRenderer leftLine;
    [SerializeField] private LineRenderer rightLine;

    [Header("Aim FOV Narrowing")]
    [Tooltip("Field of view never narrows below this angle while aiming, even at full charge (currentSpreadAngle can get much smaller than this).")]
    [SerializeField] private float minAimViewAngle = 25f;

    [Header("Noise")]
    [Tooltip("Shared noise ranges asset — the firing noise radius is read from here.")]
    [SerializeField] private NoiseSettings noiseSettings;

    private float currentSpreadAngle;
    private FieldOfView playerFov;

    private void Awake()
    {
        playerFov = GetComponentInParent<FieldOfView>();
        if (inventory == null) inventory = GetComponentInParent<SlotInventory>();
        if (inventory == null) inventory = FindFirstObjectByType<SlotInventory>();
    }

    public override void StartCharging()
    {
        currentSpreadAngle = maxSpreadAngle;
        base.StartCharging();
    }

    protected override void Update()
    {
        base.Update();

        if (isCharging)
        {
            ToggleLines(true);

            float progress = GetChargeProgress();
            currentSpreadAngle = Mathf.Lerp(maxSpreadAngle, minSpreadAngle, progress);

            UpdateAimLines();
            // Field of view eases toward minAimViewAngle while aiming (focused aim, less
            // peripheral vision). The target is fixed, not the ever-narrowing spread cone —
            // FieldOfView handles its own gradual transition (aimTransitionSpeed) independently
            // of how fast the shot is charging.
            playerFov?.SetAimNarrowing(true, minAimViewAngle);
        }
        else
        {
            ToggleLines(false);
        }
    }

    public override void StopCharging()
    {
        base.StopCharging();
        ToggleLines(false);
        playerFov?.SetAimNarrowing(false, 0f);
    }

    private void OnDisable()
    {
        playerFov?.SetAimNarrowing(false, 0f);
    }

    /// <summary>
    /// Gates the shot on ammo: with an <see cref="ammoItem"/> configured, at least one
    /// unit must be in the inventory or the trigger does nothing.
    /// </summary>
    protected override bool CanFire() => HasAmmo();

    /// <summary>True when firing is allowed: no ammo item is required, or one is available.</summary>
    private bool HasAmmo()
    {
        if (ammoItem == null) return true;
        return AmmoCount() > 0;
    }

    /// <summary>Total rounds of <see cref="ammoItem"/> held across the hotbar and backpack.</summary>
    private int AmmoCount()
    {
        if (inventory == null || ammoItem == null) return 0;
        return inventory.Hotbar.Count(ammoItem) + inventory.Backpack.Count(ammoItem);
    }

    /// <summary>
    /// Spends a single round, draining the hotbar first then the backpack. Returns false
    /// when nothing could be removed (also true when no ammo item is required).
    /// </summary>
    private bool ConsumeAmmo()
    {
        if (ammoItem == null) return true;
        if (inventory == null) return false;
        if (inventory.Hotbar.TryRemove(ammoItem, 1) > 0) return true;
        return inventory.Backpack.TryRemove(ammoItem, 1) > 0;
    }

    protected override void ExecuteAttack()
    {
        // CanFire already confirmed a round is available; consuming here keeps the spend
        // and the projectile spawn atomic so a shot is never fired without paying for it.
        if (!ConsumeAmmo())
            return;

        float randomOffset = UnityEngine.Random.Range(-currentSpreadAngle / 2f, currentSpreadAngle / 2f);
        Quaternion shootRotation = shootPoint.rotation * Quaternion.Euler(0, 0, randomOffset);
        Instantiate(projectilePrefab, shootPoint.position, shootRotation);

        CameraShake shaker = FindFirstObjectByType<CameraShake>();
        if (shaker != null)
        {
            shaker.TriggerShake(0.08f, 0.4f);
        }
        if (noiseSettings != null)
            NoiseEvents.Emit(shootPoint.position, noiseSettings.shootNoiseRadius);
    }

    private void UpdateAimLines()
    {
        if (leftLine == null || rightLine == null || shootPoint == null) return;

        leftLine.SetPosition(0, shootPoint.position);
        rightLine.SetPosition(0, shootPoint.position);

        Vector3 leftDir = Quaternion.Euler(0, 0, currentSpreadAngle / 2f) * shootPoint.right;
        Vector3 rightDir = Quaternion.Euler(0, 0, -currentSpreadAngle / 2f) * shootPoint.right;

        leftLine.SetPosition(1, shootPoint.position + leftDir * lineLength);
        rightLine.SetPosition(1, shootPoint.position + rightDir * lineLength);
    }

    private void ToggleLines(bool state)
    {
        if (leftLine != null) leftLine.enabled = state;
        if (rightLine != null) rightLine.enabled = state;
    }
}
