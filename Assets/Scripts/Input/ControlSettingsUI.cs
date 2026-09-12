using UnityEngine;
using UnityEngine.UI;

public class ControlSettingsUI : MonoBehaviour
{
    [Header("Screen")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RebindActionRow[] rows;
    [SerializeField] private KeyBindingRebindRow[] keyBindingRows;
    [SerializeField] private Button closeButton;

    [Header("Dependencies (optional — leave empty in the Main Menu scene)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PauseMenuController pauseMenuController;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
        if (pauseMenuController == null) pauseMenuController = FindFirstObjectByType<PauseMenuController>();
        if (gameManager != null) gameManager.OnGameStateChanged += OnGameStateChanged;

        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Close);

        if (rows != null)
            foreach (var row in rows)
                if (row != null) row.RebindingStateChanged += OnRowRebindingStateChanged;

        if (keyBindingRows != null)
            foreach (var row in keyBindingRows)
                if (row != null) row.RebindingStateChanged += OnRowRebindingStateChanged;
    }

    private void OnDestroy()
    {
        if (gameManager != null) gameManager.OnGameStateChanged -= OnGameStateChanged;

        if (rows != null)
            foreach (var row in rows)
                if (row != null) row.RebindingStateChanged -= OnRowRebindingStateChanged;

        if (keyBindingRows != null)
            foreach (var row in keyBindingRows)
                if (row != null) row.RebindingStateChanged -= OnRowRebindingStateChanged;
    }

    public void Open()
    {
        if (IsOpen || panelRoot == null) return;

        panelRoot.SetActive(true);
        pauseMenuController?.SetSubScreenOpen(true);

        RefreshAllRows();
    }

    public void Close()
    {
        if (!IsOpen) return;
        Hide();
    }

    private void Hide()
    {
        panelRoot.SetActive(false);
        pauseMenuController?.SetSubScreenOpen(false);
    }

    private void RefreshAllRows()
    {
        if (rows != null)
            foreach (var row in rows) row?.RefreshBindingLabel();
        if (keyBindingRows != null)
            foreach (var row in keyBindingRows) row?.RefreshBindingLabel();
    }

    private void OnGameStateChanged(GameState previous, GameState current)
    {
        if (IsOpen && current != GameState.Paused)
            Hide();
    }

    private void OnRowRebindingStateChanged(bool rebinding)
    {
        if (closeButton != null) closeButton.interactable = !rebinding;
    }
}