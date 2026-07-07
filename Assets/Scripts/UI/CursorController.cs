using UnityEngine;
using TMPro;

/// <summary>
/// The player's on-screen cursor. Lives on the same top-most canvas object that
/// follows the mouse and carries the <see cref="HeldItemController"/>'s item, so a
/// single object represents "the cursor": it holds whatever is picked up and shows a
/// small label for whatever is being hovered.
///
/// Today the label is driven by <see cref="WorldItemPickup"/> (the name of a dropped
/// item under the cursor); it is deliberately generic so future systems (interactables,
/// enemies, tooltips) can call <see cref="SetHoverText"/> for anything hovered.
/// </summary>
public class CursorController : MonoBehaviour
{
    [Tooltip("Root of the floating hover label; toggled on only while there is text to show.")]
    [SerializeField] private GameObject labelRoot;
    [SerializeField] private TMP_Text label;

    private void Awake() => ClearHoverText();

    /// <summary>Shows <paramref name="text"/> above the cursor. Empty/null hides the label.</summary>
    public void SetHoverText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            ClearHoverText();
            return;
        }

        if (label != null) label.text = text;
        if (labelRoot != null) labelRoot.SetActive(true);
    }

    /// <summary>Hides the hover label.</summary>
    public void ClearHoverText()
    {
        if (labelRoot != null) labelRoot.SetActive(false);
    }
}
