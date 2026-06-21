using System;
using UnityEngine;

public class PlayerStaminaSystem : MonoBehaviour
{
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float sprintDrainRate = 20f;
    [SerializeField] private float attackStaminaCost = 15f;
    [SerializeField] private float minStaminaToSprint = 10f;
    [SerializeField] private float minStaminaToAttack = 15f;
    [SerializeField] private float regenDelay = 2f;
    [SerializeField] private float regenRate = 25f;
    [SerializeField] private StaminaBar staminaBar;

    public Action OnStaminaDepletedWhileSprinting;

    private float currentStamina;
    private bool isSprinting;
    private float lastUseTime = -999f;

    private void Start()
    {
        if (staminaBar == null)
            staminaBar = FindFirstObjectByType<StaminaBar>();

        currentStamina = maxStamina;
        staminaBar?.SetMaxStamina(maxStamina);
    }

    private void Update()
    {
        if (isSprinting)
        {
            Drain(sprintDrainRate * Time.deltaTime);
            if (currentStamina <= 0f)
            {
                isSprinting = false;
                OnStaminaDepletedWhileSprinting?.Invoke();
            }
        }
        else if (currentStamina < maxStamina && Time.time - lastUseTime >= regenDelay)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + regenRate * Time.deltaTime);
            staminaBar?.SetStamina(currentStamina);
        }
    }

    public void SetSprinting(bool sprinting)
    {
        isSprinting = sprinting;
    }

    public bool CanSprint() => currentStamina >= minStaminaToSprint;

    public bool TryUseAttackStamina()
    {
        if (currentStamina < minStaminaToAttack)
            return false;
        Drain(attackStaminaCost);
        return true;
    }

    private void Drain(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastUseTime = Time.time;
        staminaBar?.SetStamina(currentStamina);
    }
}
