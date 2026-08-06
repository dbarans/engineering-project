using System;
using UnityEngine;

/// <summary>
/// Keeps the active combat method in sync with the hotbar: whichever weapon item
/// is under the selection highlight decides whether melee, ranged, or no attack
/// is available.
///
/// Also the single source of truth for "what is the player holding right now", which
/// <see cref="PlayerAnimationDriver"/> reads to pick the torso animation.
/// </summary>
public class PlayerWeaponManager : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler inputHandler;

    [SerializeField] private PlayerAttack meleeWeapon;
    [SerializeField] private PlayerAttack rangedWeapon;

    [Tooltip("Auto-resolved at runtime if left unset (the hotbar lives in the UI canvas, not the player prefab).")]
    [SerializeField] private HotbarUI hotbar;

    private PlayerAttack activeWeapon;

    /// <summary>The attack component currently equipped, or <c>null</c> when nothing is selected.</summary>
    public PlayerAttack ActiveWeapon => activeWeapon;

    /// <summary>
    /// Combat style the player is currently holding. Derived from the equipped component rather
    /// than the hotbar item so it stays correct on the no-hotbar fallback path too.
    /// </summary>
    public WeaponType ActiveWeaponType
    {
        get
        {
            if (activeWeapon == null) return WeaponType.None;
            return activeWeapon is RangedAttack ? WeaponType.Ranged : WeaponType.Melee;
        }
    }

    /// <summary>Raised after the equipped weapon changed (including to none).</summary>
    public event Action WeaponChanged;

    /// <summary>
    /// Raised when the equipped weapon fires. Re-published here so listeners survive weapon
    /// swaps without having to resubscribe themselves.
    /// </summary>
    public event Action WeaponFired;

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
        if (activeWeapon != null)
            activeWeapon.Fired -= OnWeaponFired;
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
            activeWeapon.Fired -= OnWeaponFired;
            activeWeapon.StopCharging();
            activeWeapon.gameObject.SetActive(false);
        }

        activeWeapon = newWeapon;
        inputHandler.SetCurrentAttack(activeWeapon);

        if (activeWeapon != null)
        {
            activeWeapon.Fired += OnWeaponFired;
            activeWeapon.gameObject.SetActive(true);
            Debug.Log($"equiped: {activeWeapon.gameObject.name}");
        }
        else
        {
            Debug.Log("unequiped: no weapon selected");
        }

        WeaponChanged?.Invoke();
    }

    private void OnWeaponFired() => WeaponFired?.Invoke();
}
