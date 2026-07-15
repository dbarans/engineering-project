using UnityEngine;

/// <summary>
/// Enemy type with no vision, relying only on hearing to detect the player.
/// Requires a <see cref="SoundPlayerDetector"/> component and visionDistance set to 0
/// in the inspector (inherited from <see cref="EnemyBase"/>) so the built-in vision check is disabled.
/// </summary>
[RequireComponent(typeof(SoundPlayerDetector))]
public class BlindListenerEnemy : EnemyBase
{
    protected override void OnDeath()
    {
        gameObject.SetActive(false);
    }
}
