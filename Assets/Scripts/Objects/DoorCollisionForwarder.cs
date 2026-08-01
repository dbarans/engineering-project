using UnityEngine;

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