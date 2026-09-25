/// <summary>
/// Enemy-facing alias of <see cref="SpriteFrameAnimator"/>. The playback logic is shared with
/// the player (see <see cref="PlayerAnimationDriver"/>); this type only exists so the enemy
/// prefabs and <see cref="SkullGuyAnimationDriver"/> keep referencing a component whose name
/// says what it is on. All behaviour and serialized clip data live in the base class.
/// </summary>
public class EnemySpriteAnimator : SpriteFrameAnimator
{
}
