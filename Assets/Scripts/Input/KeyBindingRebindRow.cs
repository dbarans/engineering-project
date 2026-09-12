using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class KeyBindingRebindRow : MonoBehaviour
{
    public enum BindingField { ToggleBackpack, ThrowItem }

    [Header("Target")]
    [SerializeField] private BindingField field;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI actionLabel;
    [SerializeField] private TextMeshProUGUI bindingLabel;
    [SerializeField] private Button rebindButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private GameObject waitingForInputRoot;

    public event Action<bool> RebindingStateChanged;

    private bool _listening;
    private KeyBindings Bindings => KeyBindings.Instance;

    private void Awake()
    {
        if (rebindButton != null) rebindButton.onClick.AddListener(StartRebind);
        if (resetButton != null) resetButton.onClick.AddListener(ResetToDefault);
        if (waitingForInputRoot != null) waitingForInputRoot.SetActive(false);
    }

    private void OnEnable() => RefreshBindingLabel();

    public void RefreshBindingLabel()
    {
        if (bindingLabel != null) bindingLabel.text = GetCurrentKey().ToString();
    }

    private Key GetCurrentKey() => field switch
    {
        BindingField.ToggleBackpack => Bindings.toggleBackpack,
        BindingField.ThrowItem => Bindings.throwItem,
        _ => Key.None
    };

    private void SetKey(Key key)
    {
        switch (field)
        {
            case BindingField.ToggleBackpack: Bindings.toggleBackpack = key; break;
            case BindingField.ThrowItem: Bindings.throwItem = key; break;
        }
    }

    private string FieldName() => field switch
    {
        BindingField.ToggleBackpack => nameof(KeyBindings.toggleBackpack),
        BindingField.ThrowItem => nameof(KeyBindings.throwItem),
        _ => field.ToString()
    };

    private void StartRebind()
    {
        if (_listening) return;
        _listening = true;
        if (waitingForInputRoot != null) waitingForInputRoot.SetActive(true);
        if (bindingLabel != null) bindingLabel.text = "…";
        RebindingStateChanged?.Invoke(true);
    }

    private void Update()
    {
        if (!_listening) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            FinishListening();
            return;
        }

        foreach (var control in keyboard.allKeys)
        {
            if (control.wasPressedThisFrame)
            {
                SetKey(control.keyCode);
                KeyBindingsPersistence.SaveOverride(FieldName(), control.keyCode);
                FinishListening();
                break;
            }
        }
    }

    private void FinishListening()
    {
        _listening = false;
        if (waitingForInputRoot != null) waitingForInputRoot.SetActive(false);
        RefreshBindingLabel();
        RebindingStateChanged?.Invoke(false);
    }

    private void ResetToDefault()
    {
        var defaults = ScriptableObject.CreateInstance<KeyBindings>();
        Key defaultKey = field switch
        {
            BindingField.ToggleBackpack => defaults.toggleBackpack,
            BindingField.ThrowItem => defaults.throwItem,
            _ => Key.None
        };
        Destroy(defaults);

        SetKey(defaultKey);
        KeyBindingsPersistence.ResetOverride(FieldName());
        RefreshBindingLabel();
    }
}