using UnityEngine;

public class MeleeAttack : PlayerAttack
{
    [Header("Melee Settings")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float minAttackRadius = 1f;
    [SerializeField] private float maxAttackRadius = 3f;
    [SerializeField] private float minDamage = 10f;
    [SerializeField] private float maxDamage = 40f;
    [SerializeField] private float knockbackForce = 1f;
    [Tooltip("How far the attack area is pushed in front of the attack point, along the aim direction (world units).")]
    [SerializeField] private float forwardOffset = 0.3f;

    [Header("Durability")]
    [Tooltip("Inventory the equipped weapon's durability is read from and spent on. " +
             "Auto-resolved at runtime if left unset.")]
    [SerializeField] private SlotInventory inventory;
    [Tooltip("Hotbar that says which slot holds the active weapon. Auto-resolved at runtime if left unset.")]
    [SerializeField] private HotbarUI hotbar;
    [Tooltip("Damage multiplier once the weapon's durability hits 0. The weapon still swings " +
             "but hits this much softer until repaired.")]
    [SerializeField, Range(0f, 1f)] private float brokenDamageMultiplier = 0.2f;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponentInParent<SlotInventory>();
        if (inventory == null) inventory = FindFirstObjectByType<SlotInventory>();
        if (hotbar == null) hotbar = FindFirstObjectByType<HotbarUI>();
    }

    protected override void ExecuteAttack()
    {
        float progress = GetChargeProgress();
        float radius = Mathf.Lerp(minAttackRadius, maxAttackRadius, progress);
        float damage = Mathf.Lerp(minDamage, maxDamage, progress);
        Vector3 center = AttackCenter();

        // The swing itself, whether or not it connects — a whiff still moves air.
        AudioService.PlayAt(SoundId.PlayerAttackMelee, center);

        // The active weapon lives in the selected hotbar slot (the weapon manager only
        // equips melee while a melee item is selected). A depleted weapon still swings
        // but hits much softer until it is repaired.
        ItemContainer weaponContainer = inventory != null ? inventory.Hotbar : null;
        int weaponSlot = hotbar != null ? hotbar.SelectedIndex : -1;
        ItemStack weapon = weaponContainer != null && weaponSlot >= 0 ? weaponContainer.Get(weaponSlot) : null;
        bool weaponWears = weapon != null && weapon.HasDurability;
        if (weaponWears && weapon.CurrentDurability <= 0)
            damage *= brokenDamageMultiplier;

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(
            center,
            radius,
            enemyLayer
        );

        bool hitAnEnemy = false;
        foreach (Collider2D hit in hitEnemies)
        {
            TryDamageTarget(hit, damage);
            if (hit.TryGetComponent<EnemyBase>(out EnemyBase enemy))
            {
                enemy.TakeDamage(damage);
                Vector2 direction = (enemy.transform.position - center).normalized;
                enemy.Knockback(direction, knockbackForce);
                hitAnEnemy = true;
            }
        }

        // Each swing that lands on an enemy spends one durability point (until depleted).
        if (hitAnEnemy && weaponWears && weapon.CurrentDurability > 0)
            weaponContainer.ReduceDurability(weaponSlot, 1);

        CameraShake shaker = FindFirstObjectByType<CameraShake>();
        if (shaker != null)
        {
            float shakeForceMultiplier = Mathf.Lerp(0.4f, 1.5f, progress);

            shaker.TriggerShake(0.15f, 0.2f, shakeForceMultiplier);
        }
    }

    /// <summary>Attack circle center: the attack point pushed forward along the aim direction.</summary>
    private Vector3 AttackCenter()
    {
        return attackPoint.position + attackPoint.right * forwardOffset;
    }

    /// <summary>
    /// The hit area, drawn only in the editor with the weapon selected — min radius in yellow,
    /// max in red — so the two can still be tuned against the world. Gizmos never render in a
    /// build, so nothing marks the hitbox in-game.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Vector3 center = AttackCenter();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, minAttackRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, maxAttackRadius);
    }
}
