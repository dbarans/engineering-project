/// <summary>
/// The one <see cref="PlayerControls"/> instance for the whole game. Before this,
/// <see cref="GameInputHandler"/>, <see cref="PlayerInputHandler"/> and
/// <see cref="PlayerInteractor"/> each called <c>new PlayerControls()</c> independently —
/// three copies of the same bindings, each with its own enabled/disabled state and, more
/// importantly, no shared place a future rebinding screen could apply an override to and
/// have every consumer see it.
///
/// Session-lifetime and never disposed: with three components previously racing to
/// dispose their own copy in <c>OnDestroy</c>, sharing one instance removes that ownership
/// question entirely rather than picking one of the three to be responsible for it. The
/// cost is a handful of native Input System bindings that live as long as the process,
/// same tradeoff <see cref="ItemDatabase"/> and <see cref="PrefabRegistry"/> already make
/// for their own Resources-loaded singletons.
///
/// Sharing one instance means enabling/disabling an action map now affects every
/// consumer of that map, not just the one that called it — so only one place is allowed
/// to own the <c>Player</c> map's enabled state: <see cref="PlayerInputHandler"/>, which
/// already disables it deliberately while the backpack is open.
/// <see cref="PlayerInteractor"/> used to call <c>controls.Player.Enable()/Disable()</c>
/// on its own copy too; on the shared instance that would have re-enabled the map out
/// from under an open backpack (or disabled player movement if <c>PlayerInteractor</c>
/// were ever toggled independently), so it now only subscribes to the specific action it
/// needs and leaves the map's on/off state alone. One visible consequence: interacting
/// with the world is now also blocked while the backpack is open, where previously
/// (separate instances) it was not — that reads as a fix, not a regression.
/// <see cref="GameInputHandler"/> is unaffected — it only ever touches the separate
/// <c>UI</c> map.
/// </summary>
public static class InputService
{
    private static PlayerControls _controls;

    /// <summary>The shared bindings. Created on first access.</summary>
    public static PlayerControls Controls => _controls ??= new PlayerControls();
}
