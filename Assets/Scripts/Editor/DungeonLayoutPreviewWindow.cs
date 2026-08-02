using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor preview for <see cref="RoomCorridorGenerator"/>: generates a layout from a
/// seed and shows it as ASCII plus a few metrics, without touching the scene.
///
/// The point is iteration speed. Judging whether a parameter change improved the
/// dungeons means looking at twenty of them, and painting twenty dungeons into tilemaps
/// takes far longer than reading twenty ASCII dumps. The metrics are also the numbers
/// worth quoting when the generator is written up.
/// </summary>
public class DungeonLayoutPreviewWindow : EditorWindow
{
    private DungeonGenerationSettings _settings;
    private string _seed = "demo";
    private int _batchSize = 8;

    private string _ascii = string.Empty;
    private string _metrics = "Press Generate.";
    private Vector2 _scroll;
    private GUIStyle _monospace;

    [MenuItem("Tools/Dungeon/Layout Preview")]
    public static void Open()
    {
        GetWindow<DungeonLayoutPreviewWindow>("Dungeon Layout").minSize = new Vector2(520, 420);
    }

    private void OnGUI()
    {
        _monospace ??= new GUIStyle(EditorStyles.label)
        {
            font = Font.CreateDynamicFontFromOSFont("Consolas", 12),
            richText = false,
            wordWrap = false
        };

        _settings = (DungeonGenerationSettings)EditorGUILayout.ObjectField(
            "Settings", _settings, typeof(DungeonGenerationSettings), false);

        if (_settings == null)
            EditorGUILayout.HelpBox("No settings asset assigned — using built-in defaults.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            _seed = EditorGUILayout.TextField("Seed", _seed);
            if (GUILayout.Button("Random", GUILayout.Width(70)))
                _seed = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate")) GeneratePreview();

            _batchSize = EditorGUILayout.IntSlider(_batchSize, 2, 200);
            if (GUILayout.Button($"Measure {_batchSize} seeds", GUILayout.Width(140)))
                MeasureBatch();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(_metrics, MessageType.None);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.SelectableLabel(_ascii, _monospace,
            GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        EditorGUILayout.EndScrollView();
    }

    private LayoutParams CurrentParams()
    {
        return _settings != null ? _settings.ToParams() : LayoutParams.Default.Sanitized();
    }

    private void GeneratePreview()
    {
        LayoutParams parameters = CurrentParams();
        var generator = new RoomCorridorGenerator();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        DungeonLayout layout = generator.Generate(_seed, parameters);
        stopwatch.Stop();

        _ascii = layout.ToAscii();
        _metrics = Describe(layout, generator, stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Runs a batch of seeds and reports the aggregate. A single dungeon says nothing
    /// about whether a setting is safe; a failure rate over a batch does.
    /// </summary>
    private void MeasureBatch()
    {
        LayoutParams parameters = CurrentParams();

        int failures = 0, retries = 0, minRooms = int.MaxValue, maxRooms = 0;
        long walkable = 0, links = 0;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < _batchSize; i++)
        {
            var generator = new RoomCorridorGenerator();
            DungeonLayout layout = generator.Generate($"{_seed}-{i}", parameters);

            if (!generator.LastGenerationSucceeded) failures++;
            if (generator.LastAttemptCount > 1) retries++;
            minRooms = Mathf.Min(minRooms, layout.Rooms.Count);
            maxRooms = Mathf.Max(maxRooms, layout.Rooms.Count);
            walkable += layout.CountWalkable();
            links += layout.Links.Count;
        }
        stopwatch.Stop();

        _metrics =
            $"{_batchSize} seeds in {stopwatch.ElapsedMilliseconds} ms " +
            $"({stopwatch.Elapsed.TotalMilliseconds / _batchSize:F2} ms each)\n" +
            $"rooms {minRooms}..{maxRooms}   avg corridors {links / (float)_batchSize:F1}   " +
            $"avg open cells {walkable / _batchSize}\n" +
            $"retried {retries}   failed {failures}";
    }

    private static string Describe(DungeonLayout layout, RoomCorridorGenerator generator, double milliseconds)
    {
        int deadEnds = 0, doors = 0;
        int deepest = 0;
        foreach (var room in layout.Rooms)
        {
            if (room.Degree <= 1) deadEnds++;
            if (room.DepthFromStart != int.MaxValue)
                deepest = Mathf.Max(deepest, room.DepthFromStart);
        }
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
                if (layout[x, y] == CellType.Door) doors++;
        }

        // Links beyond a spanning tree are exactly the cycles.
        int loops = Mathf.Max(0, layout.Links.Count - (layout.Rooms.Count - 1));

        var builder = new StringBuilder();
        builder.AppendLine(
            $"seed '{layout.Seed}'   {layout.Width}x{layout.Height}   {milliseconds:F1} ms   " +
            $"attempt {generator.LastAttemptCount}");
        builder.AppendLine(
            $"rooms {layout.Rooms.Count}   corridors {layout.Links.Count}   loops {loops}   " +
            $"dead ends {deadEnds}   doors {doors}");
        builder.Append(
            $"open cells {layout.CountWalkable()} ({100f * layout.CountWalkable() / (layout.Width * layout.Height):F1}%)   " +
            $"deepest room {deepest} hops from start");

        if (!generator.LastGenerationSucceeded)
            builder.Append($"\nVALIDATION FAILED: {generator.LastFailureReason}");

        return builder.ToString();
    }
}
