/// <summary>
/// Every sound the game can play, as stable string ids — the audio counterpart of
/// <see cref="PrefabRegistry"/>'s prefab ids.
///
/// Constants rather than raw strings at the call site: a typo in a literal is a silent
/// no-op that no compiler catches and nobody notices until someone asks why the door is
/// quiet. These also give <c>SoundBankSetup</c> something to enumerate by reflection, so
/// the bank asset can be generated already listing every id.
///
/// Ids are referenced from the <see cref="SoundBank"/> asset, so renaming one orphans the
/// entry that used to carry its clips — rename in both places, or leave the id alone and
/// change only the clips behind it.
/// </summary>
public static class SoundId
{
    // ---------------------------------------------------------------- player
    public const string PlayerFootstepWalk = "player.footstep.walk";
    public const string PlayerFootstepSprint = "player.footstep.sprint";

    /// <summary>
    /// Sneaking emits <em>no</em> gameplay noise (see <see cref="PlayerNoiseEmitter"/>) but
    /// still plays audio: the player should hear their own careful footsteps even when
    /// nothing else can. That split is the whole reason audio and NoiseEvents are separate
    /// systems — see AUDIO_NOTES.md D1.
    /// </summary>
    public const string PlayerFootstepSneak = "player.footstep.sneak";

    public const string PlayerHurt = "player.hurt";
    public const string PlayerDeath = "player.death";
    public const string PlayerAttackMelee = "player.attack.melee";
    public const string PlayerAttackRanged = "player.attack.ranged";

    /// <summary>Trigger pulled with an empty magazine.</summary>
    public const string PlayerAttackDryFire = "player.attack.dryfire";

    public const string PlayerThrow = "player.throw";

    // ---------------------------------------------------------------- surfaces
    /// <summary>
    /// A step taken on broken glass (<see cref="NoisySurface"/>). Replaces the ordinary
    /// footstep for that step, in every movement mode — including sneaking, which is the
    /// point of the surface: it is the one floor the player cannot cross quietly.
    /// </summary>
    public const string SurfaceGlassStep = "surface.glass.step";

    // ---------------------------------------------------------------- doors
    public const string DoorOpen = "door.open";
    public const string DoorClose = "door.close";

    /// <summary>A blow landing on the door itself (enemy breaching, or a sprint ram).</summary>
    public const string DoorHit = "door.hit";
    public const string DoorDestroy = "door.destroy";

    // ---------------------------------------------------------------- barricade
    public const string BarricadeBuild = "barricade.build";

    /// <summary>One stage of a multi-stage barricade giving way.</summary>
    public const string BarricadeStageBreak = "barricade.stagebreak";

    /// <summary>The last stage falling — the door behind it is now exposed.</summary>
    public const string BarricadeDestroy = "barricade.destroy";

    // ---------------------------------------------------------------- world / items
    public const string ChestOpen = "chest.open";
    public const string ChestClose = "chest.close";
    public const string ItemPickup = "item.pickup";
    public const string ItemDrop = "item.drop";

    // ---------------------------------------------------------------- enemies
    /// <summary>Fired once on the transition into chasing, not repeatedly while chasing.</summary>
    public const string EnemyAlert = "enemy.alert";

    public const string EnemyAttack = "enemy.attack";
    public const string EnemyHurt = "enemy.hurt";
    public const string EnemyDeath = "enemy.death";
}
