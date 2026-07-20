using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Short-lived notification in the corner of the player's HUD ("Game saved").
/// <see cref="Show"/> displays the message for a few seconds, then fades it out;
/// a new message restarts the timer. Runs on unscaled time so it behaves the same
/// whether the game is playing or paused, and never intercepts clicks (the setup
/// script disables raycasts on the whole toast).
/// </summary>
public class ToastUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TextMeshProUGUI label;
    [Tooltip("How long the message stays fully visible before fading.")]
    [SerializeField] private float visibleSeconds = 2f;
    [SerializeField] private float fadeSeconds = 0.5f;

    private Coroutine _routine;

    private void Awake()
    {
        if (group != null) group.alpha = 0f;
    }

    /// <summary>Shows the message, replacing whatever is currently displayed.</summary>
    public void Show(string message)
    {
        if (group == null) return;

        if (label != null) label.text = message;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeRoutine());
    }

    private IEnumerator FadeRoutine()
    {
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(visibleSeconds);

        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / fadeSeconds;
            yield return null;
        }
        group.alpha = 0f;
        _routine = null;
    }
}
