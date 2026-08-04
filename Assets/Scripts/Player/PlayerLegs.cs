using UnityEngine;

/// <summary>
/// Handles player aiming by rotating the torso towards the target position.
/// Respects game state and does not update during pause.
/// </summary>
public class PlayerLegs : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform legsTransform;

    [Tooltip("Degrees added so the legs art's forward matches +X (right). Frames drawn facing " +
             "up need -90. If the legs point 90° off after swapping the art, try 90 / -90 / 180.")]
    [SerializeField] private float spriteForwardOffsetDeg = -90f;

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

        legsTransform.rotation = Quaternion.Euler(0f, 0f, angle + spriteForwardOffsetDeg);
    }
}
