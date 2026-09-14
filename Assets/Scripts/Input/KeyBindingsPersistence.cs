using UnityEngine;
using UnityEngine.InputSystem;

public static class KeyBindingsPersistence
{
    private const string PrefPrefix = "KeyBindingOverride_";

    public static void ApplyOverrides(KeyBindings bindings)
    {
        bindings.interact = LoadOrDefault(nameof(bindings.interact), bindings.interact);
        bindings.toggleBackpack = LoadOrDefault(nameof(bindings.toggleBackpack), bindings.toggleBackpack);
        bindings.throwItem = LoadOrDefault(nameof(bindings.throwItem), bindings.throwItem);
    }

    public static void SaveOverride(string fieldName, Key value)
    {
        PlayerPrefs.SetInt(PrefPrefix + fieldName, (int)value);
        PlayerPrefs.Save();
    }

    public static void ResetOverride(string fieldName)
    {
        PlayerPrefs.DeleteKey(PrefPrefix + fieldName);
        PlayerPrefs.Save();
    }

    private static Key LoadOrDefault(string fieldName, Key fallback)
    {
        string prefKey = PrefPrefix + fieldName;
        return PlayerPrefs.HasKey(prefKey) ? (Key)PlayerPrefs.GetInt(prefKey) : fallback;
    }
}