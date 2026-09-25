/// <summary>
/// Implemented by a component that modulates how brightly a light burns over time (a flickering
/// flame, a failing bulb). Read every frame by <see cref="StationaryLightSource"/>, which scales
/// the light it writes into the vision mask by this value.
///
/// Optional by design — a light with no such component on its GameObject burns at full, steady
/// brightness. Keeping it a separate component means a lamp's behaviour is swapped by swapping
/// the component, not by growing the light source with more modes.
/// </summary>
public interface ILightIntensity
{
    /// <summary>How brightly the light currently burns: 0 fully out, 1 full strength.</summary>
    float Intensity { get; }
}
