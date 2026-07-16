using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup for a SkullGuy enemy GameObject. Ensures every required component is present
/// and wired, assigns the player and vision mask, and loads all animation frames.
///
/// Usage: select the SkullGuy GameObject (a scene instance, or open its prefab in Prefab Mode),
/// then Tools > SkullGuy > Setup Selected.
/// </summary>
public static class SkullGuySetup
{
    [MenuItem("Tools/SkullGuy/Setup Selected")]
    private static void SetupSelected()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("SkullGuy Setup", "Select the SkullGuy GameObject first (a scene instance or the prefab opened in Prefab Mode).", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(go, "Setup SkullGuy");

        // --- Visual child (sprite lives on a child, not the logic root) ---
        // The TestEnemy prefab uses a 3D capsule mesh on a child (e.g. "EnemyModel").
        // Reuse that child as the sprite visual: strip its 3D mesh and put the sprite there.
        Transform visual = ResolveVisualChild(go);
        StripMesh(visual.gameObject);

        // Remove any sprite components accidentally placed on the root (e.g. earlier run).
        RemoveFromRoot<EnemySpriteAnimator>(go);
        RemoveFromRoot<SpriteRenderer>(go);

        var sr = Ensure<SpriteRenderer>(visual.gameObject);

        // --- Physics ---
        var rb = Ensure<Rigidbody2D>(go);
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        if (go.GetComponent<Collider2D>() == null)
            go.AddComponent<CircleCollider2D>();

        // --- Movement strategy (only if none present) ---
        if (go.GetComponent<IMovementStrategy>() == null)
            go.AddComponent<SimpleDirectMovement>();

        // --- Enemy behaviour ---
        if (go.GetComponent<TestEnemy>() != null && go.GetComponent<SkullGuyEnemy>() == null)
            Debug.LogWarning("[SkullGuy] Object has TestEnemy. Swap it to SkullGuyEnemy (Inspector Debug mode -> Script) to keep field values, then rerun.");
        var enemy = Ensure<SkullGuyEnemy>(go);

        // --- Animation ---
        var animator = Ensure<EnemySpriteAnimator>(visual.gameObject);
        var driver = Ensure<SkullGuyAnimationDriver>(go);

        // Wire driver.animator (private serialized field).
        var driverSo = new SerializedObject(driver);
        driverSo.FindProperty("animator").objectReferenceValue = animator;
        driverSo.ApplyModifiedProperties();

        // --- Hidden outside the player's field of view (Darkwood vision) ---
        Ensure<HideableObject>(go);

        // --- Assign player on EnemyBase ---
        var enemySo = new SerializedObject(enemy);
        var playerProp = enemySo.FindProperty("player");
        if (playerProp.objectReferenceValue == null)
        {
            var playerMovement = Object.FindFirstObjectByType<PlayerMovement>();
            if (playerMovement != null)
                playerProp.objectReferenceValue = playerMovement.transform;
        }
        enemySo.ApplyModifiedProperties();

        // --- Vision detector (sight range + line-of-sight blockers) ---
        var vision = Ensure<VisionPlayerDetector>(go);
        int mask = LayerMask.GetMask("ObstacleStatic", "ObstacleDynamic");
        if (mask != 0)
        {
            var visionSo = new SerializedObject(vision);
            visionSo.FindProperty("obstacleLayers").intValue = mask;
            visionSo.ApplyModifiedProperties();
        }

        // --- Load frames + preview idle sprite ---
        int clips = SkullGuyFrameLoader.LoadInto(animator, out string report, out int totalFrames);
        Sprite idle = SkullGuyFrameLoader.GetIdleFirstFrame();
        if (idle != null)
            sr.sprite = idle;

        EditorUtility.SetDirty(go);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

        string playerInfo = enemySo.FindProperty("player").objectReferenceValue != null
            ? "Player assigned."
            : "Player NOT found — assign it manually on SkullGuyEnemy.";
        Debug.Log($"[SkullGuy] Setup done. {clips} clips, {totalFrames} frames. {playerInfo}\n{report}");
        EditorUtility.DisplayDialog("SkullGuy Setup",
            $"Setup done.\n\nClips: {clips} ({totalFrames} frames)\n{playerInfo}", "OK");
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    /// <summary>
    /// Finds the child used as the sprite visual: an existing sprite/animator child, else a child
    /// named "EnemyModel"/"Visual", else a freshly created "Visual" child.
    /// </summary>
    private static Transform ResolveVisualChild(GameObject root)
    {
        var existing = root.GetComponentInChildren<EnemySpriteAnimator>(true);
        if (existing != null && existing.gameObject != root)
            return existing.transform;

        foreach (Transform child in root.transform)
        {
            if (child.name == "EnemyModel" || child.name == "Visual")
                return child;
        }

        var visual = new GameObject("Visual");
        Undo.RegisterCreatedObjectUndo(visual, "Create Visual");
        visual.transform.SetParent(root.transform, false);
        return visual.transform;
    }

    /// <summary>Removes 3D mesh components and resets scale so the sprite isn't distorted.</summary>
    private static void StripMesh(GameObject go)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) Undo.DestroyObjectImmediate(mr);
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null) Undo.DestroyObjectImmediate(mf);
        go.transform.localScale = Vector3.one;
        go.transform.localPosition = Vector3.zero;
    }

    /// <summary>Removes a component from the root if present (respects RequireComponent order).</summary>
    private static void RemoveFromRoot<T>(GameObject root) where T : Component
    {
        var c = root.GetComponent<T>();
        if (c != null) Undo.DestroyObjectImmediate(c);
    }
}
