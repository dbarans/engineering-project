using UnityEngine;

public class PlayerWeaponManager : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler inputHandler;

    [SerializeField] private PlayerAttack meleeWeapon;
    [SerializeField] private PlayerAttack rangedWeapon;

    private PlayerAttack activeWeapon;

    private void Start()
    {
        EquipWeapon(meleeWeapon);
    }

    private void EquipWeapon(PlayerAttack newWeapon)
    {
        if (activeWeapon != null)
        {
            activeWeapon.StopCharging();
            activeWeapon.gameObject.SetActive(false);
        }

        activeWeapon = newWeapon;

        activeWeapon.gameObject.SetActive(true);
        inputHandler.SetCurrentAttack(activeWeapon);

        Debug.Log($"equiped: {activeWeapon.gameObject.name}");
    }
}