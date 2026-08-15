using UnityEngine;

/// <summary>
/// Enemy type with no vision, relying only on hearing. Heard noises send it to investigate
/// their position (InvestigateNoise); it confirms the player only at point-blank range
/// (alwaysDetectRange). Requires a <see cref="SoundPlayerDetector"/> component; being blind
/// simply means having no <see cref="VisionPlayerDetector"/> attached.
/// </summary>
[RequireComponent(typeof(SoundPlayerDetector))]
public class BlindListenerEnemy : EnemyBase
{
    protected override void OnDeath()
    {
        base.OnDeath();
    }
}
