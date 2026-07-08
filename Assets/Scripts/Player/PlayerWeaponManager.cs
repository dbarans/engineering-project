using UnityEngine;

/// <summary>
/// Keeps the active combat method in sync with the hotbar: whichever weapon item
/// is under the selection highlight decides whether melee, ranged, or no attack
/// is available.
/// </summary>
public class PlayerWeaponManager : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler inputHandler;

    [SerializeField] private PlayerAttack meleeWeapon;
    [SerializeField] private PlayerAttack rangedWeapon;

    [Tooltip("Auto-resolved at runtime if left unset (the hotbar lives in the UI canvas, not the player prefab).")]
    [SerializeField] private HotbarUI hotbar;

    private PlayerAttack activeWeapon;

    private void Start()
    {
        if (hotbar == null) hotbar = FindFirstObjectByType<HotbarUI>();

        // Start from a clean state; the hotbar selection decides what becomes active.
        PlayerAttack fallback = inputHandler.GetCurrentAttack();
        if (meleeWeapon != null) meleeWeapon.gameObject.SetActive(false);
        if (rangedWeapon != null) rangedWeapon.gameObject.SetActive(false);
        inputHandler.SetCurrentAttack(null);

        if (hotbar != null)
        {
            hotbar.SelectedItemChanged += OnSelectedItemChanged;
            OnSelectedItemChanged();
        }
        else
        {
            // No hotbar in this scene — fall back to whatever the input handler was wired with.
            EquipWeapon(fallback);
        }
    }

    private void OnDestroy()
    {
        if (hotbar != null)
            hotbar.SelectedItemChanged -= OnSelectedItemChanged;
    }

    private void OnSelectedItemChanged()
    {
        EquipWeapon(WeaponFor(hotbar.SelectedItem));
    }

    private PlayerAttack WeaponFor(ItemData item)
    {
        if (item == null) return null;
        switch (item.weaponType)
        {
            case WeaponType.Melee: return meleeWeapon;
            case WeaponType.Ranged: return rangedWeapon;
            default: return null;
        }
    }

    private void EquipWeapon(PlayerAttack newWeapon)
    {
        if (activeWeapon == newWeapon) return;

        if (activeWeapon != null)
        {
            activeWeapon.StopCharging();
            activeWeapon.gameObject.SetActive(false);
        }

        activeWeapon = newWeapon;
        inputHandler.SetCurrentAttack(activeWeapon);

        if (activeWeapon != null)
        {
            activeWeapon.gameObject.SetActive(true);
            Debug.Log($"equiped: {activeWeapon.gameObject.name}");
        }
        else
        {
            Debug.Log("unequiped: no weapon selected");
        }
    }
}
