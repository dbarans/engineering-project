using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Class representing health bar in the UI
/// </summary>
/// <param name="maxHealth"> Max health</param>
public class HealthBar : MonoBehaviour
{
    public Slider slider;

    /// <summary>
    /// Sets the max health in health bar.
    /// </summary>
    /// <param name="maxHealth"> Max health</param>
    public void SetMaxHealth(int maxHealth)
    {
        slider.maxValue = maxHealth;
    }

    /// <summary>
    /// Sets the current health of the player.
    /// </summary>
    /// <param name="health"> Current health</param>
    public void SetHealth(int health)
    {
        slider.value = health;
    }
}
