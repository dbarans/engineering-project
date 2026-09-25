using UnityEngine;
using UnityEngine.InputSystem;

public static class InputService
{
    private const string BindingOverridesPrefKey = "InputBindingOverrides";

    private static PlayerControls _controls;

    public static PlayerControls Controls
    {
        get
        {
            if (_controls == null)
            {
                _controls = new PlayerControls();
                LoadBindingOverrides();
            }
            return _controls;
        }
    }

    public static void SaveBindingOverrides()
    {
        if (_controls == null) return;
        PlayerPrefs.SetString(BindingOverridesPrefKey, _controls.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    private static void LoadBindingOverrides()
    {
        if (!PlayerPrefs.HasKey(BindingOverridesPrefKey)) return;
        string json = PlayerPrefs.GetString(BindingOverridesPrefKey);
        if (!string.IsNullOrEmpty(json))
            _controls.LoadBindingOverridesFromJson(json);
    }

    public static void ResetBindingOverrides()
    {
        Controls.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(BindingOverridesPrefKey);
        PlayerPrefs.Save();
    }
}