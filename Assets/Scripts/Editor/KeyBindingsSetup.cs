using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click creation of the <see cref="KeyBindings"/> asset in Resources. Without it every
/// consumer falls back to <see cref="KeyBindings.Instance"/>'s built-in defaults, which work
/// but cannot be tuned from the Inspector or (later) a settings screen.
///
/// Only ever creates the asset — never touches one that already exists, so re-running does
/// not clobber keys someone already rebound by hand.
/// </summary>
public static class KeyBindingsSetup
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = ResourcesFolder + "/" + KeyBindings.ResourcesPath + ".asset";

    [MenuItem("Tools/Input/Build Key Bindings")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<KeyBindings>(AssetPath) != null)
        {
            EditorUtility.DisplayDialog("Key Bindings",
                $"'{AssetPath}' already exists — left untouched.\n\n" +
                "Edit its fields directly in the Inspector to change keys.", "OK");
            return;
        }

        Directory.CreateDirectory(ResourcesFolder);

        var bindings = ScriptableObject.CreateInstance<KeyBindings>();
        AssetDatabase.CreateAsset(bindings, AssetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Key Bindings] Created '{AssetPath}' with default keys.");
        EditorUtility.DisplayDialog("Key Bindings",
            $"Created '{AssetPath}'.\n\n" +
            "Edit its fields in the Inspector to change interact, backpack toggle or " +
            "throw keys — every script that uses them reads this one asset.", "OK");
    }
}
