using UnityEngine;

public class PlayerAim : MonoBehaviour
{
    [SerializeField] private Transform torsoTransform;
    [SerializeField] private Camera mainCamera;

    private Vector2 aimPosition;

    public void SetAimPosition(Vector2 position)
    {
        aimPosition = position;
    }

    private void Update()
    {
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(aimPosition);
        worldPos.z = 0f;

        Vector3 direction = worldPos - torsoTransform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        torsoTransform.rotation = Quaternion.Euler(0f, 0f, angle);
    }
}
