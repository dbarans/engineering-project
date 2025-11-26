using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles system-level input actions such as pause and menu navigation.
/// Separates input handling from game logic for better modularity.
/// </summary>
public class GameInputHandler : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private MonoBehaviour gameStateManagerObject;

    private IGameStateManager gameStateManager;
    private PlayerControls controls;

    private void Awake()
    {
        if (gameStateManagerObject == null)
        {
            Debug.LogError($"{nameof(GameInputHandler)}: GameStateManager is not assigned in inspector.");
            return;
        }

        gameStateManager = gameStateManagerObject as IGameStateManager;
        if (gameStateManager == null)
        {
            Debug.LogError($"{nameof(GameInputHandler)}: Assigned object does not implement IGameStateManager.");
        }

        controls = new PlayerControls();
    }

    private void OnEnable()
    {
        controls.UI.Enable();
        controls.UI.Cancel.performed += OnCancelPerformed;
    }

    private void OnDisable()
    {
        controls.UI.Cancel.performed -= OnCancelPerformed;
        controls.UI.Disable();
    }

    private void OnDestroy()
    {
        controls?.Dispose();
    }

    /// <summary>
    /// Handles cancel/pause input action.
    /// </summary>
    private void OnCancelPerformed(InputAction.CallbackContext context)
    {
        if (gameStateManager != null)
        {
            gameStateManager.TogglePause();
        }
    }
}

