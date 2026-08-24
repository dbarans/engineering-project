using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the game over death screen, hides active gameplay HUD elements,
/// fades in the UI smoothly, and handles reloading from the latest save slot.
/// </summary>
public class DeathScreenUI : MonoBehaviour
{
    public static DeathScreenUI Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject deathPanel;
    [SerializeField] private CanvasGroup deathCanvasGroup;
    [SerializeField] private Button loadLastSaveButton;
    [SerializeField] private Button mainMenuButton;

    [Header("HUD Settings")]
    [Tooltip("Gameplay HUD elements to deactivate on death (e.g. HealthBar, StaminaBar, Hotbar).")]
    [SerializeField] private GameObject[] hudElementsToHide;

    [Header("Animation Settings")]
    [Tooltip("Duration of the fade-in animation in seconds.")]
    [SerializeField] private float fadeDuration = 1.5f;

    [Header("Scene Settings")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (deathCanvasGroup == null && deathPanel != null)
        {
            deathCanvasGroup = deathPanel.GetComponent<CanvasGroup>();
        }

        if (deathPanel != null)
        {
            deathPanel.SetActive(false);
        }

        if (loadLastSaveButton != null)
            loadLastSaveButton.onClick.AddListener(OnLoadLastSaveClicked);

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
    }

    /// <summary>
    /// Displays the death screen, hides HUD elements, unlocks cursor, freezes gameplay, and starts the fade-in.
    /// </summary>
    public void ShowDeathScreen()
    {
        if (hudElementsToHide != null)
        {
            foreach (var hud in hudElementsToHide)
            {
                if (hud != null)
                    hud.SetActive(false);
            }
        }
        if (deathPanel != null)
            deathPanel.SetActive(true);

        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (deathCanvasGroup != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeInRoutine());
        }
    }

    /// <summary>
    /// Smoothly transitions the canvas alpha from 0 to 1 using unscaled time.
    /// </summary>
    private IEnumerator FadeInRoutine()
    {
        deathCanvasGroup.alpha = 0f;
        deathCanvasGroup.blocksRaycasts = false;

        float elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            deathCanvasGroup.alpha = Mathf.Clamp01(elapsedTime / fadeDuration);
            yield return null;
        }

        deathCanvasGroup.alpha = 1f;
        deathCanvasGroup.blocksRaycasts = true;
    }

    /// <summary>
    /// Finds the most recent save slot and delegates loading to SaveManager.
    /// </summary>
    public void OnLoadLastSaveClicked()
    {
        Time.timeScale = 1f;

        if (deathPanel != null)
            deathPanel.SetActive(false);

        int latestSlot = GetLatestSaveSlot();

        if (latestSlot != -1)
        {
            SaveManager.Instance.Load(latestSlot);
        }
        else
        {
            Debug.LogWarning("[DeathScreenUI] No saves found. Restarting current level as fallback.");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    /// <summary>
    /// Restores time scale and returns the player to the main menu scene.
    /// </summary>
    public void OnMainMenuClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// Iterates over all save slots to find the one with the most recent savedAtUtc timestamp.
    /// </summary>
    /// <returns>Index of the newest save slot, or -1 if no saves exist.</returns>
    private int GetLatestSaveSlot()
    {
        if (SaveManager.Instance == null) return -1;

        var infos = SaveManager.Instance.GetSlotInfos();
        int latestSlot = -1;
        DateTime latestTime = DateTime.MinValue;

        for (int i = 0; i < infos.Length; i++)
        {
            if (infos[i] != null && DateTime.TryParse(infos[i].savedAtUtc, out DateTime saveTime))
            {
                if (saveTime > latestTime)
                {
                    latestTime = saveTime;
                    latestSlot = i;
                }
            }
        }

        return latestSlot;
    }
}