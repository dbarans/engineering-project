using System;
using System.Collections.Generic;
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
    /// <summary>One hotbar item wired to the exact attack component that item uses.</summary>
    [Serializable]
    private struct ItemWeapon
    {
        public ItemData item;
        public PlayerAttack weapon;
    }

    [SerializeField] private PlayerInputHandler inputHandler;

    [Tooltip("Default for any Melee item without an entry in Item Weapons.")]
    [SerializeField] private PlayerAttack meleeWeapon;
    [Tooltip("Default for any Ranged item without an entry in Item Weapons.")]
    [SerializeField] private PlayerAttack rangedWeapon;

    [Tooltip("Weapons bound to one specific item, checked before the per-type defaults above. " +
             "This is what keeps two weapons of the same WeaponType apart — the pistol and the " +
             "shotgun are both Ranged, but each needs its own attack component (own ammo, own " +
             "spread, own aim lines). Items with no entry here fall back to the default for their type.")]
    [SerializeField] private List<ItemWeapon> itemWeapons = new List<ItemWeapon>();

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
        Stow(meleeWeapon);
        Stow(rangedWeapon);
        foreach (var entry in itemWeapons) Stow(entry.weapon);
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

        foreach (var entry in itemWeapons)
        {
            if (entry.item == item && entry.weapon != null)
                return entry.weapon;
        }

        switch (item.weaponType)
        {
            case WeaponType.Melee: return meleeWeapon;
            case WeaponType.Ranged: return rangedWeapon;
            default: return null;
        }
    }

    private static void Stow(PlayerAttack weapon)
    {
        if (weapon != null) weapon.gameObject.SetActive(false);
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
