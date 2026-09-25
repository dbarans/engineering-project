using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [Tooltip("Exact name of your gameplay scene, must be in Build Settings")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private ControlSettingsUI controlSettingsUI; // new

    private void Awake() // new
    {
        if (controlSettingsUI == null)
            controlSettingsUI = FindFirstObjectByType<ControlSettingsUI>();
    }

    /// <summary>
    /// Starts the menu track. Runs on every load of this scene, including a return from
    /// the game, and PlayMusic ignores a request for the track already playing.
    /// </summary>
    private void Start()
    {
        AudioService.PlayMusic(SoundId.MusicMenu);
    }

    /// <summary>
    /// Stops the track on the way out, wherever the exit is. Not in OnPlayPressed: the
    /// Load button leaves the menu through SaveManager.Load, which loads the saved scene
    /// itself, and AudioRuntime is DontDestroyOnLoad — a track nobody stops would play on
    /// under the dungeon.
    /// </summary>
    private void OnDestroy()
    {
        AudioService.StopMusic();
    }

    public void OnPlayPressed()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    // Wired to the Settings button's OnClick() — new
    public void OnSettingsPressed()
    {
        controlSettingsUI?.Open();
    }

    public void OnQuitPressed()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}