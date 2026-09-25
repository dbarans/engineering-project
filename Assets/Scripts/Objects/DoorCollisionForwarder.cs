using UnityEngine;

/// <summary>
/// Forwards child collider collision events to the parent SimpleDoor component.
/// </summary>
public class DoorCollisionForwarder : MonoBehaviour
{
    private SimpleDoor parentDoor;

    private void Awake()
    {
        parentDoor = GetComponentInParent<SimpleDoor>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (parentDoor != null)
        {
            parentDoor.SendMessage("OnCollisionEnter2D", collision, SendMessageOptions.DontRequireReceiver);
        }
    }
}