using UnityEngine;

public class PauseMenuController : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    [SerializeField] private GameObject pausePanel;
    [SerializeField] private string mainMenuSceneName = "Main Menu";
    [SerializeField] private GameManager gameManager;
    [SerializeField] private SaveLoadUI saveLoadUI;
    [SerializeField] private ControlSettingsUI controlSettingsUI; // new

    private bool _subScreenOpen; // new

    private void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        if (saveLoadUI == null)
            saveLoadUI = FindFirstObjectByType<SaveLoadUI>();
        if (controlSettingsUI == null) // new
            controlSettingsUI = FindFirstObjectByType<ControlSettingsUI>();
    }

    private void OnEnable()
    {
        if (gameManager != null)
            gameManager.OnGameStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null)
            gameManager.OnGameStateChanged -= OnGameStateChanged;
    }

    private void OnGameStateChanged(GameState previous, GameState current)
    {
        IsPaused = current == GameState.Paused;
        RefreshPausePanel(); // extracted below
    }

    /// <summary>Called by ControlSettingsUI when it opens/closes on top of the pause menu.</summary>
    public void SetSubScreenOpen(bool open) // new
    {
        _subScreenOpen = open;
        RefreshPausePanel();
    }

    private void RefreshPausePanel() // new — pulled out of OnGameStateChanged
    {
        bool anotherScreenOpen = (saveLoadUI != null && saveLoadUI.IsOpen) || _subScreenOpen;
        bool shouldShow = IsPaused && !anotherScreenOpen;

        if (pausePanel != null)
            pausePanel.SetActive(shouldShow);

        if (shouldShow)
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }

    public void OnResumePressed()
    {
        gameManager?.ResumeGame();
    }

    // Wired to the Settings button's OnClick() — new
    public void OnSettingsPressed()
    {
        controlSettingsUI?.Open();
    }

    public void OnMainMenuPressed()
    {
        gameManager?.ReturnToMenu();
        SceneManager_LoadMainMenu();
    }

    private void SceneManager_LoadMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
    }

    public void OnQuitPressed()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}