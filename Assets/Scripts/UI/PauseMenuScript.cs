using UnityEngine;

public class PauseMenuController : MonoBehaviour
{
    /// <summary>
    /// Mirrors GameManager's paused state, kept in sync via OnGameStateChanged below.
    /// Exists so other scripts (e.g. HotbarUI's scroll guard) can check pause state
    /// without every script needing its own GameManager reference.
    /// </summary>
    public static bool IsPaused { get; private set; }

    [SerializeField] private GameObject pausePanel;
    [SerializeField] private string mainMenuSceneName = "Main Menu";
    [SerializeField] private GameManager gameManager;
    [SerializeField] private SaveLoadUI saveLoadUI;

    private void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        if (saveLoadUI == null)
            saveLoadUI = FindFirstObjectByType<SaveLoadUI>();
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
        bool anotherScreenOpen = saveLoadUI != null && saveLoadUI.IsOpen;

        IsPaused = current == GameState.Paused;
        bool shouldShow = IsPaused && !anotherScreenOpen;

        if (pausePanel != null)
            pausePanel.SetActive(shouldShow);

        if (shouldShow)
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }

    // Wired to the Resume button's OnClick()
    public void OnResumePressed()
    {
        gameManager?.ResumeGame();
    }

    // Wired to the Main Menu button's OnClick()
    public void OnMainMenuPressed()
    {
        gameManager?.ReturnToMenu();
        SceneManager_LoadMainMenu();
    }

    private void SceneManager_LoadMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
    }

    // Wired to the Quit button's OnClick()
    public void OnQuitPressed()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}