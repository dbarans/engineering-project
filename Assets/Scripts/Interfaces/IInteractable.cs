/// <summary>
/// Contract for world objects that can be interacted with by the player.
/// </summary>
public interface IInteractable
{
    void Interact(InteractContext context);
}

/// <summary>
/// Context passed into an interaction call.
/// </summary>
public struct InteractContext
{
    public UnityEngine.GameObject Player;
}
