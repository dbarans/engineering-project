using UnityEngine;

/// <summary>
/// Forwards the reach trigger's collision events to the parent ChestInteractable.
/// Same relay pattern as <see cref="DoorCollisionForwarder"/> for the door.
/// </summary>
public class ChestReachForwarder : MonoBehaviour
{
    private ChestInteractable _parent;

    private void Awake() => _parent = GetComponentInParent<ChestInteractable>();

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_parent != null)
        {
            _parent.SendMessage("OnTriggerEnter2D", other, SendMessageOptions.DontRequireReceiver);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (_parent != null)
        {
            _parent.SendMessage("OnTriggerExit2D", other, SendMessageOptions.DontRequireReceiver);
        }
    }
}
