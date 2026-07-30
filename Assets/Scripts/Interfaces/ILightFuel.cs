/// <summary>
/// Implemented by a component that limits how long a light source can burn (oil, candle wax,
/// batteries). Checked by <see cref="StationaryLightSource"/> before it lights anything: a light
/// with no fuel emits nothing.
///
/// Optional by design — a light source with no such component on its GameObject burns forever.
/// This keeps the current stationary lamps maintenance-free while leaving one seam for the
/// planned consumable-fuel resource, which can be added without touching the light itself.
/// </summary>
public interface ILightFuel
{
    /// <summary>True while fuel remains and the light may burn.</summary>
    bool HasFuel { get; }
}
