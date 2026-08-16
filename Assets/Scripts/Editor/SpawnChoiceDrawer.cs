using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws one entry of a <see cref="RoomContentSettings"/> spawn table as a weight slider
/// with the share it actually works out to.
///
/// The weights were always there, but as a bare float field they said nothing about how
/// often the thing appears: whether <c>0.3</c> is rare or common depends entirely on what
/// the other entries in the same list are set to, which the inspector never showed. Tuning
/// a table therefore meant summing the column by hand. The slider gives a direction to
/// drag, and the percentage next to it answers the question the number is really being
/// used to ask — how much of this table is this entry.
///
/// <para>
/// The share is computed over the whole list with no depth gating, deliberately: gating is
/// per-room and depth-dependent, so any single figure for it would be wrong nearly
/// everywhere. This is the share among entries that are eligible at all, and
/// <c>Min Depth</c> sits directly under it to say when that is.
/// </para>
/// </summary>
public abstract class SpawnChoiceDrawer : PropertyDrawer
{
    /// <summary>
    /// Lowest top end the weight slider is drawn with. The shipped tables sit inside
    /// 0..1.5, so a fixed 0..1 would leave half of them pinned at the maximum, and a much
    /// wider range would spend most of its travel on values nothing uses. Raised to fit
    /// any entry already authored above it, so opening the inspector can never clamp a
    /// weight the slider simply cannot reach.
    /// </summary>
    private const float MinSliderMax = 3f;

    private const float SharePixels = 46f;

    /// <summary>The id field's label — what this table's entries are identified by.</summary>
    protected abstract string IdLabel { get; }

    /// <summary>The property holding the id, so the base can draw the shared first row.</summary>
    protected abstract string IdProperty { get; }

    /// <summary>Rows drawn under the weight, specific to the kind of table.</summary>
    protected abstract string[] TrailingProperties { get; }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        int rows = 2 + TrailingProperties.Length;
        return rows * EditorGUIUtility.singleLineHeight +
               (rows - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect row = position;
        row.height = EditorGUIUtility.singleLineHeight;

        EditorGUI.PropertyField(row, property.FindPropertyRelative(IdProperty), new GUIContent(IdLabel));
        row = NextRow(row);

        DrawWeight(row, property);
        row = NextRow(row);

        foreach (string trailing in TrailingProperties)
        {
            EditorGUI.PropertyField(row, property.FindPropertyRelative(trailing), true);
            row = NextRow(row);
        }

        EditorGUI.EndProperty();
    }

    private static void DrawWeight(Rect row, SerializedProperty property)
    {
        SerializedProperty weight = property.FindPropertyRelative("weight");

        Rect sliderRect = row;
        sliderRect.width -= SharePixels;

        Rect shareRect = row;
        shareRect.xMin = sliderRect.xMax;

        float max = Mathf.Max(MinSliderMax, weight.floatValue);
        EditorGUI.Slider(sliderRect, weight, 0f, max, new GUIContent("Weight",
            "How likely this entry is relative to the others in the same list. The share " +
            "to the right is what it currently works out to."));

        var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        EditorGUI.LabelField(shareRect, ShareText(property, weight.floatValue), style);
    }

    /// <summary>
    /// This entry's weight as a percentage of its list's total, or "—" when the list is
    /// unreadable or every weight in it is zero (in which case nothing spawns from it at
    /// all, and no share is meaningful).
    /// </summary>
    private static string ShareText(SerializedProperty element, float weight)
    {
        SerializedProperty list = ParentList(element);
        if (list == null || !list.isArray) return "—";

        float total = 0f;
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(i).FindPropertyRelative("weight");
            if (entry != null) total += entry.floatValue;
        }

        if (total <= 0f) return "—";
        return $"{weight / total * 100f:0.#}%";
    }

    /// <summary>
    /// The list an element belongs to, found by trimming the <c>.Array.data[i]</c> tail off
    /// its own path. There is no direct "my parent" on <see cref="SerializedProperty"/>, and
    /// the drawer is handed the element rather than the list it sits in.
    /// </summary>
    private static SerializedProperty ParentList(SerializedProperty element)
    {
        string path = element.propertyPath;
        int tail = path.LastIndexOf(".Array.data[", System.StringComparison.Ordinal);
        if (tail < 0) return null;

        return element.serializedObject.FindProperty(path.Substring(0, tail));
    }

    private static Rect NextRow(Rect row)
    {
        row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        return row;
    }
}

/// <summary>Weight slider for the prefab tables: enemies and props.</summary>
[CustomPropertyDrawer(typeof(RoomContentSettings.PrefabChoice))]
public class PrefabChoiceDrawer : SpawnChoiceDrawer
{
    protected override string IdLabel => "Prefab Id";
    protected override string IdProperty => "prefabId";
    protected override string[] TrailingProperties { get; } = { "minDepth" };
}

/// <summary>Weight slider for the item tables: floor loot, chest contents, treasure.</summary>
[CustomPropertyDrawer(typeof(RoomContentSettings.ItemChoice))]
public class ItemChoiceDrawer : SpawnChoiceDrawer
{
    protected override string IdLabel => "Item Id";
    protected override string IdProperty => "itemId";
    protected override string[] TrailingProperties { get; } = { "minCount", "maxCount" };
}
