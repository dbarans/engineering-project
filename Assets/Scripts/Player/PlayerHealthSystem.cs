using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Handles player health status.
/// Respects game state and triggers death screen handling upon fatal damage.
/// </summary>
public class PlayerHealthSystem : MonoBehaviour
{
    [SerializeField]
    private int currentHealth;
    [SerializeField]
    private int maxHealth = 100;
    [SerializeField]
    private HealthBar healthBar;

    [Header("Death Screen Settings")]
    [Tooltip("Delay in seconds before showing the death UI so the death sound can play.")]
    [SerializeField] private float deathScreenDelay = 0.5f;

    private bool _isDead = false;

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
    /// <param name="damage">Value of the damage depending on the attack type</param>
    public void TakeDamage(int damage)
    {
        if (_isDead) return;

        bool wasAlive = currentHealth > 0;

        currentHealth -= damage;
        if (healthBar != null) healthBar.SetHealth(currentHealth);

        if (currentHealth <= 0)
        {
            if (wasAlive)
            {
                _isDead = true;
                AudioService.Play(SoundId.PlayerDeath);
                StartCoroutine(HandleDeathSequence());
            }
        }
        else
        {
            AudioService.Play(SoundId.PlayerHurt);
        }
    }

    /// <summary>
    /// Waits for the death sound and activates the death screen UI.
    /// </summary>
    private IEnumerator HandleDeathSequence()
    {
        var movement = GetComponent<MonoBehaviour>(); 
        yield return new WaitForSecondsRealtime(deathScreenDelay);

        if (DeathScreenUI.Instance != null)
        {
            DeathScreenUI.Instance.ShowDeathScreen();
        }
        else
        {
            var deathUI = FindFirstObjectByType<DeathScreenUI>(FindObjectsInactive.Include);
            if (deathUI != null)
            {
                deathUI.ShowDeathScreen();
            }
            else
            {
                Debug.LogError("[PlayerHealthSystem] Nie znaleziono skryptu DeathScreenUI na scenie!");
            }
        }
    }
    /// <summary>
    /// Method responsible for healing the player.
    /// </summary>
    /// <param name="heal">Value of the healing depending on the healing type</param>
    public void Heal(int heal)
    {
        if (heal <= 0 || _isDead) return;

        SetHealth(currentHealth + heal);
    }

    /// <summary>
    /// Overwrites current health, clamped to [0, maxHealth], and updates the bar.
    /// Used by the save system on restore.
    /// </summary>
    public void SetHealth(int health)
    {
        currentHealth = Mathf.Clamp(health, 0, maxHealth);
        _isDead = currentHealth <= 0;
        
        if (healthBar != null) healthBar.SetHealth(currentHealth);
    }

    /// <summary>
    /// Returning current health status.
    /// </summary>
    public int GetCurrentHealth()
    {
        return currentHealth;
    }

    /// <summary>
    /// The health the player starts a run with, and the ceiling SetHealth clamps to.
    /// </summary>
    public int GetMaxHealth()
    {
        return maxHealth;
    }

    /// <summary>
    /// True when there is nothing left to heal.
    /// </summary>
    public bool IsFull => currentHealth >= maxHealth;
}