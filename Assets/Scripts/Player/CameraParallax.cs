using UnityEngine;

/// <summary>
/// Controls the camera position, smoothly following the player, adding an offset towards the mouse 
/// based on the attack charge state, and applying the shake offset retrieved from the CameraShake script.
/// </summary>
public class CameraParallax : MonoBehaviour
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private PlayerInputHandler inputHandler;
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0f, -10f);
    [SerializeField] private float maxExplorationOffset = 0.5f;
    [SerializeField] private float explorationInfluence = 0.05f;
    [SerializeField] private float maxAimOffset = 4.5f;
    [SerializeField] private float aimInfluence = 0.4f;

    private CameraShake shaker;

    private void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (inputHandler == null) inputHandler = FindFirstObjectByType<PlayerInputHandler>();
        shaker = GetComponent<CameraShake>();
    }

    private void LateUpdate()
    {
        if (playerTransform == null || mainCamera == null) return;

        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0f;

        bool isAiming = false;
        if (inputHandler != null && inputHandler.GetCurrentAttack() != null)
        {
            isAiming = inputHandler.GetCurrentAttack().GetChargeProgress() > 0f;
        }

        float currentInfluence = isAiming ? aimInfluence : explorationInfluence;
        float currentMaxOffset = isAiming ? maxAimOffset : maxExplorationOffset;

        Vector3 directionToMouse = mouseWorldPos - playerTransform.position;
        Vector3 mouseOffset = directionToMouse * currentInfluence;

        mouseOffset = Vector3.ClampMagnitude(mouseOffset, currentMaxOffset);

        Vector3 targetPosition = playerTransform.position + cameraOffset + mouseOffset;
        Vector3 shakeOffset = (shaker != null) ? shaker.CurrentShakeOffset : Vector3.zero;

        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime) + shakeOffset;
    }
}