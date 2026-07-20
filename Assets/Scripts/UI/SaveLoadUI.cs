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

    [Header("Dependencies")]
    [SerializeField] private GameManager gameManager;
    [Tooltip("HUD toast used for the 'Game saved' confirmation.")]
    [SerializeField] private ToastUI toast;

    private Mode _mode = Mode.Save;
    private int _pendingSlot = -1;

    /// <summary>Whether the screen is currently shown.</summary>
    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
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

        gameManager.PauseGame();
        SetMode(mode);
        panelRoot.SetActive(true);
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
        if (slotViews == null) return;

        var infos = SaveManager.Instance.GetSlotInfos();
        for (int i = 0; i < slotViews.Length; i++)
        {
            if (slotViews[i] == null) continue;
            var meta = i < infos.Length ? infos[i] : null;
            // Save mode: every slot is a target. Load mode: only readable saves.
            slotViews[i].SetInfo(i, meta, _mode == Mode.Save || meta != null);
        }
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
        if (SaveManager.Instance.Save(slot))
        {
            Close(); // back to the game; the HUD toast is the confirmation
            if (toast != null) toast.Show("Game saved");
        }
        else
        {
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
