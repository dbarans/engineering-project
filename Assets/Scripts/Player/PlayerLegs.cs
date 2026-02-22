using UnityEngine;

/// <summary>
/// Handles player aiming by rotating the torso towards the target position.
/// Respects game state and does not update during pause.
/// </summary>
public class PlayerLegs : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform legsTransform;

    private Vector2 legsPosition;

    /// <summary>
    /// Sets the target aim position in screen coordinates.
    /// </summary>
    /// <param name="position">Screen position to aim at.</param>
    public void SetLegsPosition(Vector2 position)
    {
        legsPosition = position;
    }

    private void Update()
    {
        if (legsPosition.sqrMagnitude < 0.01f)
            return;

        float angle = Mathf.Atan2(legsPosition.y, legsPosition.x) * Mathf.Rad2Deg;

        legsTransform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }
}
