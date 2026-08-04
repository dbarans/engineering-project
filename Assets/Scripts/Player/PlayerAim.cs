using UnityEngine;

/// <summary>
/// Handles player aiming by rotating the torso towards the target position.
/// Respects game state and does not update during pause.
///
/// The torso's local +X is the player's forward: that is where the Direction child sits, and
/// the weapon, shoot point and aim lines hang off it. So this rotation is deliberately
/// offset-free — the art's own facing is corrected on the TorsoVisual child instead
/// (see PlayerAnimationSetup), never here, or the weapon would stop following the crosshair.
/// </summary>
public class PlayerAim : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform torsoTransform;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float rotationSpeed = 450f;

    private IGameStateManager gameStateManager;
    private Vector2 aimPosition;

    /// <summary>
    /// Sets the game state manager dependency. Called by GameManager during initialization.
    /// </summary>
    /// <param name="manager">The game state manager instance.</param>
    public void SetGameStateManager(IGameStateManager manager)
    {
        gameStateManager = manager;
    }

    /// <summary>
    /// Sets the target aim position in screen coordinates.
    /// </summary>
    /// <param name="position">Screen position to aim at.</param>
    public void SetAimPosition(Vector2 position)
    {
        aimPosition = position;
    }

    private void Update()
    {
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            return;
        }

        Vector3 worldPos = mainCamera.ScreenToWorldPoint(aimPosition);
        worldPos.z = 0f;

        Vector3 direction = worldPos - torsoTransform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
        
        torsoTransform.rotation = Quaternion.RotateTowards(
            torsoTransform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }
}
