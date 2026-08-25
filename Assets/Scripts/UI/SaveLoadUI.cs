using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen save/load screen (plan Phase 5): five slots shared between Save and
/// Load modes, opened by interacting with a <see cref="SaveStation"/> (the typewriter).
///
/// Rules from the plan:
/// - saving over an occupied slot asks for confirmation, an empty slot saves at once;
/// - Load is clickable only for occupied slots;
/// - opening pauses the game through <see cref="GameManager"/>, closing resumes it.
///
/// Escape is not read here: <see cref="GameInputHandler"/> already maps it to
/// <see cref="GameManager.TogglePause"/>, so the screen watches
/// <see cref="GameManager.OnGameStateChanged"/> and hides itself whenever something
/// else unpauses the game. Everything runs fine at <c>Time.timeScale = 0</c> — the
/// pipeline is synchronous and UI input is unscaled.
/// </summary>
public class SaveLoadUI : MonoBehaviour
{
    public enum Mode { Save, Load }

    [Header("Screen")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private SaveSlotView[] slotViews;
    [SerializeField] private Button saveTabButton;
    [SerializeField] private Button loadTabButton;
    [SerializeField] private Button closeButton;

    [Header("Overwrite confirmation")]
    [SerializeField] private GameObject confirmRoot;
    [SerializeField] private TextMeshProUGUI confirmLabel;
    [SerializeField] private Button confirmAcceptButton;
    [SerializeField] private Button confirmCancelButton;

    [Header("Save cost (ink)")]
    [Tooltip("Ink cost of a save. Auto-resolved from this object when left unset; " +
             "null disables the cost entirely.")]
    [SerializeField] private SaveCost saveCost;
    [Tooltip("Shows how many saves the carried ink allows. Hidden in Load mode.")]
    [SerializeField] private TextMeshProUGUI costLabel;

    [Header("Dependencies")]
    [SerializeField] private GameManager gameManager;
    [Tooltip("HUD toast used for the 'Game saved' confirmation.")]
    [SerializeField] private ToastUI toast;

    private static readonly Color CostOkColor = new Color(0.85f, 0.82f, 0.72f, 1f);
    private static readonly Color CostEmptyColor = new Color(0.92f, 0.55f, 0.42f, 1f);

    private Mode _mode = Mode.Save;
    private int _pendingSlot = -1;

    /// <summary>Whether the screen is currently shown.</summary>
    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        if (saveCost == null) saveCost = GetComponent<SaveCost>();
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null) gameManager.OnGameStateChanged += OnGameStateChanged;

        if (panelRoot != null) panelRoot.SetActive(false);
        if (confirmRoot != null) confirmRoot.SetActive(false);

        if (saveTabButton != null) saveTabButton.onClick.AddListener(() => SetMode(Mode.Save));
        if (loadTabButton != null) loadTabButton.onClick.AddListener(() => SetMode(Mode.Load));
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (confirmAcceptButton != null) confirmAcceptButton.onClick.AddListener(ConfirmOverwrite);
        if (confirmCancelButton != null) confirmCancelButton.onClick.AddListener(HideConfirm);

        if (slotViews != null)
        {
            foreach (var view in slotViews)
            {
                if (view != null) view.Clicked += OnSlotClicked;
            }
        }
    }

    private void OnDestroy()
    {
        if (gameManager != null) gameManager.OnGameStateChanged -= OnGameStateChanged;
    }

    /// <summary>
    /// Opens the screen in the given mode. Only possible while the game is playing
    /// (plan Phase 5: saving is allowed from gameplay only); pauses the game.
    /// </summary>
    public void Open(Mode mode)
    {
        if (IsOpen || panelRoot == null) return;
        if (gameManager == null || !gameManager.IsPlaying()) return;

        panelRoot.SetActive(true);    // set first, so IsOpen is true before PauseGame's event fires
        SetMode(mode);
        gameManager.PauseGame();
    }

    /// <summary>Hides the screen and resumes the game.</summary>
    public void Close()
    {
        if (!IsOpen) return;

        Hide();
        if (gameManager != null && gameManager.IsPaused())
            gameManager.ResumeGame();
    }

    private void Hide()
    {
        HideConfirm();
        panelRoot.SetActive(false);
    }

    private void OnGameStateChanged(GameState previous, GameState current)
    {
        // Someone else (Escape via GameInputHandler, a load in progress…) took the
        // game out of Paused while we were showing — follow, don't fight.
        if (IsOpen && current != GameState.Paused)
            Hide();
    }

    private void SetMode(Mode mode)
    {
        _mode = mode;
        HideConfirm();

        if (titleLabel != null)
            titleLabel.text = mode == Mode.Save ? "Save Game" : "Load Game";
        // The active tab is disabled — its disabled tint doubles as the highlight.
        if (saveTabButton != null) saveTabButton.interactable = mode != Mode.Save;
        if (loadTabButton != null) loadTabButton.interactable = mode != Mode.Load;

        Refresh();
    }

    private void Refresh()
    {
        UpdateCostLabel();

        if (slotViews == null) return;

        // In Save mode every slot is a target, but only when the player can afford the
        // ink; out of ink, the whole screen is read-only (like Load on empty slots).
        bool canSaveHere = _mode == Mode.Save && (saveCost == null || saveCost.CanSave);

        var infos = SaveManager.Instance.GetSlotInfos();
        for (int i = 0; i < slotViews.Length; i++)
        {
            if (slotViews[i] == null) continue;
            var meta = i < infos.Length ? infos[i] : null;
            bool interactable = _mode == Mode.Save ? canSaveHere : meta != null;
            slotViews[i].SetInfo(i, meta, interactable);
        }
    }

    /// <summary>
    /// Shows how many saves the carried ink allows (plan §5: ink-gated saving). Hidden
    /// in Load mode — loading is free — and when no ink cost is configured.
    /// </summary>
    private void UpdateCostLabel()
    {
        if (costLabel == null) return;

        if (_mode != Mode.Save || saveCost == null || !saveCost.HasCost)
        {
            costLabel.gameObject.SetActive(false);
            return;
        }

        int saves = saveCost.AvailableSaves;
        costLabel.gameObject.SetActive(true);
        costLabel.text = saves > 0
            ? $"Ink: {saves}   ({saves} save{(saves == 1 ? "" : "s")} left)"
            : "Out of ink — you need ink to save";
        costLabel.color = saves > 0 ? CostOkColor : CostEmptyColor;
    }

    private void OnSlotClicked(int slot)
    {
        if (confirmRoot != null && confirmRoot.activeSelf) return; // dialog owns input

        if (_mode == Mode.Save)
        {
            if (SaveManager.Instance.HasSave(slot))
                ShowConfirm(slot);
            else
                DoSave(slot);
        }
        else
        {
            DoLoad(slot);
        }
    }

    private void ShowConfirm(int slot)
    {
        _pendingSlot = slot;
        if (confirmLabel != null)
            confirmLabel.text = $"Overwrite the save in slot {slot + 1}?";
        if (confirmRoot != null)
            confirmRoot.SetActive(true);
    }

    private void HideConfirm()
    {
        _pendingSlot = -1;
        if (confirmRoot != null)
            confirmRoot.SetActive(false);
    }

    private void ConfirmOverwrite()
    {
        int slot = _pendingSlot;
        HideConfirm();
        if (slot >= 0)
            DoSave(slot);
    }

    private void DoSave(int slot)
    {
        // Gate on ink even here, in case a slot was somehow triggered while greyed out.
        if (saveCost != null && !saveCost.CanSave)
        {
            if (toast != null) toast.Show("You need ink to save");
            Refresh();
            return;
        }

        // Spend the ink before capturing state, so the written save already reflects the
        // ink used — otherwise loading it back would return the ink and give a free save.
        if (saveCost != null && !saveCost.TryConsume())
        {
            if (toast != null) toast.Show("You need ink to save");
            Refresh();
            return;
        }

        if (SaveManager.Instance.Save(slot))
        {
            Close(); // back to the game; the HUD toast is the confirmation
            if (toast != null) toast.Show("Game saved");
        }
        else
        {
            // A failed write must not cost the player their ink — hand it back.
            if (saveCost != null) saveCost.Refund();
            // Stay open so the player can try another slot; details are in the log.
            Refresh();
            if (toast != null) toast.Show("Save failed");
        }
    }

    private void DoLoad(int slot)
    {
        // On success the scene reload tears this screen down and SaveManager's
        // pipeline ends in Playing/timeScale 1 — resuming here would unpause the
        // old scene for its final frames. Just hide.
        if (SaveManager.Instance.Load(slot))
            Hide();
    }
}
