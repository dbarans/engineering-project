using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages the victory screen overlay displayed upon successfully finishing a run.
/// Orchestrates a timed sequence: fades the screen to black, reveals the victory title,
/// fades in contextual flavor text, and finally displays the interactive return button.
/// Maintains canvas sorting, unscaled animation updates, cursor unlocking, and EventSystem selection.
/// </summary>
public class VictoryScreenUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject victoryPanel;
    [SerializeField] private Button mainMenuButton;

    [Header("Sequence Canvas Groups")]
    [SerializeField] private CanvasGroup blackOverlayGroup;
    [SerializeField] private CanvasGroup titleGroup;
    [SerializeField] private CanvasGroup textGroup;
    [SerializeField] private CanvasGroup buttonGroup;

    [Header("Scene Transition")]
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    [Header("Sequence Timings")]
    [SerializeField] private float fadeToBlackDuration = 1.2f;
    [SerializeField] private float titleFadeDuration = 0.8f;
    [SerializeField] private float textFadeDuration = 0.8f;
    [SerializeField] private float buttonFadeDuration = 0.6f;
    [SerializeField] private float delayBetweenElements = 0.35f;

    private Coroutine sequenceCoroutine;

    private void Awake()
    {
        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);

        GameObject panelToToggle = victoryPanel != null ? victoryPanel : gameObject;
        panelToToggle.SetActive(false);
    }

    /// <summary>
    /// Displays the victory screen, brings the canvas to the front, freezes game time,
    /// and starts the sequential fade-in coroutine.
    /// </summary>
    [ContextMenu("Test Show Victory")]
    public void Show()
    {
        GameObject target = victoryPanel != null ? victoryPanel : gameObject;

        target.SetActive(true);
        target.transform.SetAsLastSibling();

        var canvas = target.GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            canvas.enabled = true;
            canvas.gameObject.SetActive(true);
            canvas.sortingOrder = 999;
        }

        target.transform.localScale = Vector3.one;

        Time.timeScale = 0f;

        InitCanvasGroup(blackOverlayGroup, false);
        InitCanvasGroup(titleGroup, false);
        InitCanvasGroup(textGroup, false);
        InitCanvasGroup(buttonGroup, false);

        if (sequenceCoroutine != null)
            StopCoroutine(sequenceCoroutine);

        sequenceCoroutine = StartCoroutine(PlayVictorySequence());
    }

    private IEnumerator PlayVictorySequence()
    {
        if (blackOverlayGroup != null)
            yield return StartCoroutine(FadeGroup(blackOverlayGroup, 0f, 1f, fadeToBlackDuration));

        yield return new WaitForSecondsRealtime(delayBetweenElements);

        if (titleGroup != null)
            yield return StartCoroutine(FadeGroup(titleGroup, 0f, 1f, titleFadeDuration));

        yield return new WaitForSecondsRealtime(delayBetweenElements);

        if (textGroup != null)
            yield return StartCoroutine(FadeGroup(textGroup, 0f, 1f, textFadeDuration));

        yield return new WaitForSecondsRealtime(delayBetweenElements);

        if (buttonGroup != null)
        {
            yield return StartCoroutine(FadeGroup(buttonGroup, 0f, 1f, buttonFadeDuration));
            buttonGroup.interactable = true;
            buttonGroup.blocksRaycasts = true;
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.interactable = true;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (mainMenuButton != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(mainMenuButton.gameObject);
        }
    }

    private IEnumerator FadeGroup(CanvasGroup group, float startAlpha, float targetAlpha, float duration)
    {
        float elapsed = 0f;
        group.alpha = startAlpha;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }

        group.alpha = targetAlpha;
    }

    private void InitCanvasGroup(CanvasGroup group, bool interactive)
    {
        if (group == null) return;
        group.alpha = 0f;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }

    /// <summary>
    /// Unfreezes time and loads the configured main menu scene.
    /// </summary>
    private void OnMainMenuClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void OnDestroy()
    {
        if (mainMenuButton != null)
            mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
    }
}