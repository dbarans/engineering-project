using System;
using UnityEngine;

/// <summary>
/// Handles player health status
/// Respects game state and does not update during pause.
/// </summary>
public class PlayerHealthSystem : MonoBehaviour
{
    [SerializeField]
    private int currentHealth;
    [SerializeField]
    private int maxHealth = 100;
    [SerializeField]
    private HealthBar healthBar;
    
    void Start()
    {
        if (healthBar == null)
            healthBar = FindFirstObjectByType<HealthBar>();

        currentHealth = maxHealth;
        if (healthBar != null)
            healthBar.SetMaxHealth(maxHealth);
    }

    /// <summary>
    /// Method responsible for taking damage by the player.
    /// </summary>
    /// <param name="damage"> Value of the damage depending of the attack type</param>
    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        if (healthBar != null) healthBar.SetHealth(currentHealth);
    }
    
    /// <summary>
    /// Method responsible for healing the player.
    /// </summary>
    /// <param name="heal"> Value of the healing depending of the healing type</param>
    public void Heal(int heal)
    {
        currentHealth += heal;
        if (healthBar != null) healthBar.SetHealth(currentHealth);
    }
    
    /// <summary>
    /// Overwrites current health, clamped to [0, maxHealth], and updates the bar.
    /// Used by the save system on restore — runs after Start() reset health to max.
    /// </summary>
    public void SetHealth(int health)
    {
        currentHealth = Mathf.Clamp(health, 0, maxHealth);
        if (healthBar != null) healthBar.SetHealth(currentHealth);
    }

    /// <summary>
    /// Returning current health status.
    /// </summary>
    public int GetCurrentHealth()
    {
        return currentHealth;
    }
}
