/// <summary>
/// Type-specific state provider on a <see cref="SaveableEntity"/> object (plan §5.2).
/// The entity captures the common fields (position, alive) itself and delegates the
/// rest to this component as an opaque JSON payload, dispatched on <see cref="TypeTag"/>
/// at restore. One provider per entity.
/// </summary>
public interface ISaveableComponent
{
    /// <summary>Dispatch tag stored in <see cref="EntityState.typeTag"/>, e.g. "enemy".</summary>
    string TypeTag { get; }

    /// <summary>Serializes this component's state into the JSON payload.</summary>
    string CapturePayload();

    /// <summary>Applies a payload previously produced by <see cref="CapturePayload"/>.</summary>
    void RestorePayload(string payload);
}
