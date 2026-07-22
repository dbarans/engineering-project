using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row on the save/load screen: a clickable slot showing either "Empty" or the
/// save's metadata (scene, local save time, player HP). <see cref="SaveLoadUI"/> binds
/// the rows and decides whether a row is clickable in the current mode.
/// </summary>
public class SaveSlotView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI detailLabel;

    private int _slot;

    /// <summary>Raised with the slot index when the row is clicked.</summary>
    public event Action<int> Clicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(() => Clicked?.Invoke(_slot));
    }

    /// <summary>
    /// Shows the slot's content. <paramref name="meta"/> null = empty (or unreadable)
    /// slot; <paramref name="interactable"/> false greys the row out (Load mode on an
    /// empty slot).
    /// </summary>
    public void SetInfo(int slot, SaveMetadata meta, bool interactable)
    {
        _slot = slot;

        if (titleLabel != null)
            titleLabel.text = $"Slot {slot + 1}";
        if (detailLabel != null)
            detailLabel.text = meta != null ? Describe(meta) : "Empty";
        if (button != null)
            button.interactable = interactable;
    }

    private static string Describe(SaveMetadata meta)
    {
        string when = meta.savedAtUtc;
        if (DateTime.TryParse(meta.savedAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var utc))
            when = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        return $"{meta.sceneDisplayName}   {when}   HP {meta.playerHealth}";
    }
}
