using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps <see cref="ItemDatabase"/> in sync with the project's <see cref="ItemData"/>
/// assets (decision D6 of the save-system plan): scans every ItemData in the project,
/// repairs missing or duplicated ids (a duplicated asset copies its id — OnValidate
/// alone cannot detect that), and rewrites the database asset in
/// <c>Assets/Resources</c>.
///
/// Runs from <b>Tools ▸ Save System ▸ Rebuild Item Database</b> and automatically
/// after item assets are imported, moved, or deleted.
/// </summary>
public static class ItemDatabaseBuilder
{
    private const string DatabaseAssetPath = "Assets/Resources/ItemDatabase.asset";

    [MenuItem("Tools/Save System/Rebuild Item Database")]
    public static void Rebuild()
    {
        List<ItemData> items = LoadAllItems();
        items.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        bool idsChanged = RepairIds(items);

        ItemDatabase db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabaseAssetPath);
        if (db == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabaseAssetPath));
            db = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(db, DatabaseAssetPath);
        }

        bool listChanged = WriteItems(db, items);
        if (idsChanged || listChanged)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[ItemDatabase] Rebuilt with {items.Count} items.", db);
        }
    }

    private static List<ItemData> LoadAllItems()
    {
        var items = new List<ItemData>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) items.Add(item);
        }
        return items;
    }

    /// <summary>Assigns fresh ids to items with an empty or already-taken id. Returns true when anything changed.</summary>
    private static bool RepairIds(List<ItemData> items)
    {
        var seen = new HashSet<string>();
        bool changed = false;

        foreach (ItemData item in items)
        {
            if (!string.IsNullOrEmpty(item.Id) && seen.Add(item.Id)) continue;

            string fresh;
            do { fresh = System.Guid.NewGuid().ToString("N"); } while (!seen.Add(fresh));

            var so = new SerializedObject(item);
            so.FindProperty("id").stringValue = fresh;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            changed = true;
            Debug.Log($"[ItemDatabase] Assigned new id to '{item.name}'.", item);
        }
        return changed;
    }

    /// <summary>Replaces the database's item list. Returns true when the list actually differed.</summary>
    private static bool WriteItems(ItemDatabase db, List<ItemData> items)
    {
        var so = new SerializedObject(db);
        SerializedProperty list = so.FindProperty("items");

        bool same = list.arraySize == items.Count;
        for (int i = 0; same && i < items.Count; i++)
            same = list.GetArrayElementAtIndex(i).objectReferenceValue == items[i];
        if (same) return false;

        list.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(db);
        return true;
    }
}

/// <summary>
/// Triggers a database rebuild when .asset files change. Deleted assets cannot be
/// type-checked anymore, so any .asset deletion counts; the rebuild itself is cheap
/// and skips saving when nothing differs, which also breaks the import loop caused
/// by the rebuild saving the database asset.
/// </summary>
internal sealed class ItemDatabaseAssetWatcher : AssetPostprocessor
{
    private static bool _scheduled;

    private static void OnPostprocessAllAssets(
        string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (_scheduled) return;
        if (!AnyCandidate(importedAssets, checkType: true) &&
            !AnyCandidate(deletedAssets, checkType: false) &&
            !AnyCandidate(movedAssets, checkType: true))
        {
            return;
        }

        _scheduled = true;
        EditorApplication.delayCall += () =>
        {
            _scheduled = false;
            ItemDatabaseBuilder.Rebuild();
        };
    }

    private static bool AnyCandidate(string[] paths, bool checkType)
    {
        foreach (string path in paths)
        {
            if (!path.EndsWith(".asset") || path == "Assets/Resources/ItemDatabase.asset") continue;
            if (!checkType || AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(ItemData))
                return true;
        }
        return false;
    }
}
