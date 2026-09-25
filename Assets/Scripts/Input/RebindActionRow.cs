using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RebindActionRow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private string actionPath;
    [SerializeField] private string compositePartName;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI actionLabel;
    [SerializeField] private TextMeshProUGUI bindingLabel;
    [SerializeField] private Button rebindButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private GameObject waitingForInputRoot;

    private InputAction _action;
    private int _bindingIndex;
    private InputActionRebindingExtensions.RebindingOperation _rebindOperation;

    public event Action<bool> RebindingStateChanged;

    private void Awake()
    {
        _action = InputService.Controls.asset.FindAction(actionPath);
        _bindingIndex = FindBindingIndex();

        if (rebindButton != null) rebindButton.onClick.AddListener(StartRebind);
        if (resetButton != null) resetButton.onClick.AddListener(ResetToDefault);
        if (waitingForInputRoot != null) waitingForInputRoot.SetActive(false);
    }

    private void OnEnable() => RefreshBindingLabel();

    private void OnDestroy() => _rebindOperation?.Dispose();

    private int FindBindingIndex()
    {
        if (_action == null) return -1;
        var bindings = _action.bindings;

        if (string.IsNullOrEmpty(compositePartName))
        {
            int fallback = -1;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite) continue;
                if (fallback < 0) fallback = i;

                string path = bindings[i].effectivePath;
                if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>"))
                    return i;
            }
            return fallback;
        }

        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].isPartOfComposite &&
                string.Equals(bindings[i].name, compositePartName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public void RefreshBindingLabel()
    {
        if (bindingLabel == null || _action == null || _bindingIndex < 0) return;
        bindingLabel.text = _action.GetBindingDisplayString(_bindingIndex);
    }

    private void StartRebind()
    {
        if (_action == null || _bindingIndex < 0 || _rebindOperation != null) return;

        RebindingStateChanged?.Invoke(true);
        if (waitingForInputRoot != null) waitingForInputRoot.SetActive(true);
        if (bindingLabel != null) bindingLabel.text = "…";

        bool wasEnabled = _action.enabled;
        _action.Disable();

        _rebindOperation = _action.PerformInteractiveRebinding(_bindingIndex)
            .WithControlsExcluding("Mouse/position")
            .WithControlsExcluding("Mouse/delta")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(op => FinishRebind(op, wasEnabled))
            .OnCancel(op => FinishRebind(op, wasEnabled))
            .Start();
    }

    private void FinishRebind(InputActionRebindingExtensions.RebindingOperation op, bool wasEnabled)
    {
        op.Dispose();
        _rebindOperation = null;

        if (wasEnabled) _action.Enable();
        if (waitingForInputRoot != null) waitingForInputRoot.SetActive(false);

        RefreshBindingLabel();
        InputService.SaveBindingOverrides();
        RebindingStateChanged?.Invoke(false);
    }

    private void ResetToDefault()
    {
        if (_action == null || _bindingIndex < 0) return;
        _action.RemoveBindingOverride(_bindingIndex);
        RefreshBindingLabel();
        InputService.SaveBindingOverrides();
    }
}