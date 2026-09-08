using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Plays <see cref="SoundId.UiClick"/> whenever a press lands on something the UI would
/// treat as a click — every button in the main menu, the pause menu, the death screen, the
/// save/load screen, and every inventory slot in the HUD.
///
/// One global listener rather than a call per button, for two reasons. Most of this
/// project's buttons are wired to their handler in the Inspector, not in code, so there is
/// no single method to hook; and some clickable things are not buttons at all
/// (<see cref="SlotView"/> handles pointer clicks directly). A listener that asks the
/// <c>EventSystem</c> the same question it asks itself covers both, covers screens nobody
/// has built yet, and needs no prefab or scene re-authoring (AUDIO_NOTES.md D10).
///
/// Bootstrapped into a <c>DontDestroyOnLoad</c> object like <see cref="AudioRuntime"/>, so
/// it survives the menu → game scene change that the UI it listens to does not.
/// </summary>
[DisallowMultipleComponent]
public class UiClickAudio : MonoBehaviour
{
    private static UiClickAudio _instance;

    /// <summary>Reused across frames; <c>RaycastAll</c> appends, so it is cleared per press.</summary>
    private readonly List<RaycastResult> _hits = new List<RaycastResult>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        var go = new GameObject(nameof(UiClickAudio));
        _instance = go.AddComponent<UiClickAudio>();
        DontDestroyOnLoad(go);
    }

    /// <summary>
    /// Runs unscaled: the pause menu and the death screen both sit at
    /// <c>Time.timeScale == 0</c>, which is exactly when most of this project's clicking
    /// happens. <c>Update</c> is not affected by timescale, and neither is audio playback.
    /// </summary>
    private void Update()
    {
        if (!TryReadPress(out Vector2 screenPosition)) return;

        EventSystem events = EventSystem.current;
        if (events == null) return;

        var pointer = new PointerEventData(events) { position = screenPosition };
        _hits.Clear();
        events.RaycastAll(pointer, _hits);

        GameObject target = ClickTarget();
        if (target == null) return;
        if (!IsInteractable(target)) return;

        AudioService.Play(SoundId.UiClick);
    }

    /// <summary>
    /// What the <c>EventSystem</c> would deliver this press to, or null when the press
    /// lands on nothing clickable.
    ///
    /// Deliberately only considers the <em>topmost</em> hit, then walks up its parents for
    /// a click handler — that is what the input module itself does. Falling through to the
    /// next raycast hit instead would make a panel background that merely covers a button
    /// still click it, which is the opposite of what blocking means.
    /// </summary>
    private GameObject ClickTarget()
    {
        foreach (RaycastResult hit in _hits)
        {
            if (hit.gameObject == null) continue;
            return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
        }

        return null;
    }

    /// <summary>
    /// Whether the target would actually respond. A greyed-out button (or one under a
    /// faded <c>CanvasGroup</c>, which <c>IsInteractable</c> accounts for) has to stay
    /// silent — a click sound on a button that does nothing reads as the game having
    /// missed the input, not as the button being disabled.
    ///
    /// Things that handle clicks without being <c>Selectable</c> — inventory slots — have
    /// no interactable state to check, so they always sound.
    /// </summary>
    private static bool IsInteractable(GameObject target)
    {
        var selectable = target.GetComponent<Selectable>();
        return selectable == null || selectable.IsInteractable();
    }

    /// <summary>
    /// The screen position of a press that started this frame, if there was one. Presses
    /// rather than releases: a click sound that waits for the button to come back up feels
    /// like input lag even though nothing is actually slower.
    /// </summary>
    private static bool TryReadPress(out Vector2 position)
    {
        position = default;

#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            position = mouse.position.ReadValue();
            return true;
        }

        // Android is a build target, and there is no mouse there.
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            position = touchscreen.primaryTouch.position.ReadValue();
            return true;
        }

        return false;
#else
        if (Input.GetMouseButtonDown(0))
        {
            position = Input.mousePosition;
            return true;
        }

        return false;
#endif
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Drops the reference on play-mode start when domain reload is disabled.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }
}
