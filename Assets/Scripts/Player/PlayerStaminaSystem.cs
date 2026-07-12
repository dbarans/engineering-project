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
    private bool isMoving;
    private float lastUseTime = -999f;

    /// <summary>Current stamina value, exposed for the save system.</summary>
    public float CurrentStamina => currentStamina;
    
    private void Start()
    {
        if (staminaBar == null)
            staminaBar = FindFirstObjectByType<StaminaBar>();

        currentStamina = maxStamina;
        staminaBar?.SetMaxStamina(maxStamina);
    }

    private void Update()
    {
        if (isSprinting && isMoving)
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

    /// <summary>
    /// Overwrites current stamina, clamped to [0, maxStamina], and updates the bar.
    /// Used by the save system on restore — runs after Start() reset stamina to max.
    /// </summary>
    public void SetStamina(float stamina)
    {
        currentStamina = Mathf.Clamp(stamina, 0f, maxStamina);
        staminaBar?.SetStamina(currentStamina);
    }

    public void SetSprinting(bool sprinting)
    {
        isSprinting = sprinting;
    }

    public void SetMoving(bool moving)
    {
        isMoving = moving;
    }

    public bool CanSprint() => currentStamina >= minStaminaToSprint;

    public bool CanAttack() => currentStamina >= minStaminaToAttack;

    public void UseAttackStamina()
    {
        Drain(attackStaminaCost);
    }

    private void Drain(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastUseTime = Time.time;
        staminaBar?.SetStamina(currentStamina);
    }
}
