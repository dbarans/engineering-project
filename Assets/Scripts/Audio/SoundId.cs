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

    /// <summary>
    /// The player running out of breath. Raised on the crossing to zero inside
    /// <c>PlayerStaminaSystem.Drain</c>, so it covers every way the bar can be emptied —
    /// a sprint held too long, a door rammed, a swing paid for — and fires once per
    /// emptying rather than every frame the bar sits at zero.
    /// </summary>
    public const string PlayerExhausted = "player.exhausted";

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

    /// <summary>
    /// A lock being turned open by hand — the key being spent on a key-locked door, and the
    /// bolt on a door the player locked themselves coming back off. Deliberately not fired
    /// by the sprint ram, which clears <c>isLocked</c> by breaking the door rather than by
    /// working the lock, and already sounds as a hit and an opening.
    /// </summary>
    public const string DoorUnlock = "door.unlock";

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

    /// <summary>
    /// The save station opening its screen. Fired by <see cref="SaveStation"/> only when the
    /// screen actually opens: <c>SaveLoadUI.Open</c> is a silent no-op unless the game is
    /// Playing, and a click that sounds but shows nothing reads as a bug.
    /// </summary>
    public const string SaveStationOpen = "savestation.open";

    // ---------------------------------------------------------------- ui
    /// <summary>
    /// A press landing on anything clickable — menu buttons, the pause and death screens,
    /// inventory slots in the HUD. Raised centrally by <see cref="UiClickAudio"/>, not by
    /// the individual screens, so there is one id here rather than one per button.
    /// </summary>
    public const string UiClick = "ui.click";

    /// <summary>
    /// The backpack panel opening and closing. On the UI channel with <see cref="UiClick"/>
    /// rather than the world channel: the panel has no place in the dungeon to sound from,
    /// and this is feedback for a screen toggle, not for anything the player character does.
    /// Both are raised from <c>BackpackUI.SetOpen</c>, the one choke point every route into
    /// the panel goes through — the Tab key, and the chest and crafting screens that close
    /// it for themselves.
    /// </summary>
    public const string BackpackOpen = "backpack.open";
    public const string BackpackClose = "backpack.close";

    // ---------------------------------------------------------------- music
    /// <summary>
    /// The main menu's looping track. Started by <c>MainMenuController</c> on scene load
    /// and stopped when the player leaves it — the runtime survives scene loads (D5),
    /// so a track nobody stops follows the player into the game.
    ///
    /// The first id on <see cref="AudioChannel.Music"/>; everything before it is Sfx or Ui.
    /// </summary>
    public const string MusicMenu = "music.menu";

    /// <summary>
    /// The dungeon's looping ambience. Owned by <c>GameManager</c>, which lives only in the
    /// dungeon scene, so it starts on every route in — the Play button, a loaded save and
    /// the death screen's restart — and stops when that scene is torn down.
    /// </summary>
    public const string MusicDungeon = "music.dungeon";

    // ---------------------------------------------------------------- enemies
    /// <summary>
    /// The moan an enemy makes while it has <em>not</em> noticed the player — the ambience
    /// that says something is in the next room before anything is visible. Fired by
    /// <see cref="EnemyBase"/> on a random per-enemy cadence, and only while the player is
    /// close enough for it to carry information.
    /// </summary>
    public const string EnemyIdle = "enemy.idle";

    /// <summary>
    /// Fired once per hunt, when the enemy first finds the player — not on every transition
    /// into the chase. Re-acquiring the player after briefly losing them is the same hunt
    /// and stays silent; the latch clears only once the enemy gives up and the trail goes
    /// cold (<c>EnemyBase.UpdateAlertAudio</c>).
    /// </summary>
    public const string EnemyAlert = "enemy.alert";

    public const string EnemyAttack = "enemy.attack";
    public const string EnemyHurt = "enemy.hurt";
    public const string EnemyDeath = "enemy.death";
}
