using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-click wiring for the player's sprite animation, the counterpart of
/// <see cref="SkullGuySetup"/>. Creates the art nodes, loads their frames, fixes the draw order
/// and adds + wires <see cref="PlayerAnimationDriver"/> on the player root.
///
/// Usage: select the Player GameObject (a scene instance, or the prefab opened in Prefab Mode),
/// then Tools > Player > Setup Animations.
/// </summary>
public static class PlayerAnimationSetup
{
    /// <summary>
    /// Degrees the art has to be turned to line its own forward up with the player's forward.
    ///
    /// The frames are drawn top-down facing up, +Y — see CHODZENIE_TLOW_CELOWANIE00, where the
    /// revolver points straight up the canvas, and CHODZENIE00, where the boots point up. The
    /// player's forward is +X: the Direction child sits at Torso local (0.548, 0, 0) with no
    /// rotation, and RangedAttack fires along shootPoint.right. Hence -90.
    /// </summary>
    private const float ArtForwardOffsetDeg = -90f;

    /// <summary>Name of the child that carries the torso art, turned by <see cref="ArtForwardOffsetDeg"/>.</summary>
    private const string TorsoArtNode = "TorsoVisual";

    // Draw order inside the player's own SortingGroup: legs at the bottom, torso over them,
    // whatever the torso carries (the Direction/weapon child) on top.
    private const int LegsOrder = 0;
    private const int TorsoOrder = 1;
    private const int CarriedOrder = 2;

    [MenuItem("Tools/Player/Setup Animations")]
    private static void SetupSelected()
    {
        GameObject root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Player Setup", "Select the Player GameObject first (a scene instance or the prefab opened in Prefab Mode).", "OK");
            return;
        }

        // Allow selecting a child (e.g. Torso) and still resolve the whole rig.
        var movement = root.GetComponentInParent<PlayerMovement>();
        if (movement != null) root = movement.gameObject;

        Transform torso = FindChild(root.transform, "Torso");
        Transform legs = FindChild(root.transform, "Legs");
        if (torso == null || legs == null)
        {
            EditorUtility.DisplayDialog("Player Setup",
                $"Could not find the body parts under '{root.name}'.\nExpected children named Torso and Legs.", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root, "Setup Player Animations");

        int clips = 0, frames = 0;
        var report = new StringBuilder();

        // The torso art cannot live on Torso itself — Torso has to keep pointing at the
        // crosshair for Direction's sake — so it gets its own turned child.
        Transform torsoArt = EnsureTorsoArtNode(torso);
        SpriteFrameAnimator torsoAnim = SetupPart(torsoArt.gameObject, false, ref clips, ref frames, report);
        SpriteFrameAnimator legsAnim = SetupPart(legs.gameObject, true, ref clips, ref frames, report);

        SetupFacing(legs, report);
        SetupSorting(torso, torsoArt, legs, report);

        var driver = Ensure<PlayerAnimationDriver>(root);
        var driverSo = new SerializedObject(driver);
        driverSo.FindProperty("torso").objectReferenceValue = torsoAnim;
        driverSo.FindProperty("legs").objectReferenceValue = legsAnim;
        driverSo.FindProperty("movement").objectReferenceValue = root.GetComponent<PlayerMovement>();
        driverSo.FindProperty("weapons").objectReferenceValue = root.GetComponentInChildren<PlayerWeaponManager>(true);
        driverSo.ApplyModifiedProperties();

        EditorUtility.SetDirty(root);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log($"[Player] Animation setup done. {clips} clips, {frames} frames.\n{report}");
        EditorUtility.DisplayDialog("Player Setup",
            $"Animation setup done.\n\nClips: {clips} ({frames} frames)\n\n{report}", "OK");
    }

    /// <summary>
    /// Returns the child that carries the torso art, creating it and moving the SpriteRenderer
    /// down onto it on the first run.
    ///
    /// The art is drawn facing +Y but Torso local +X is the player's forward, so the art needs a
    /// -90° turn. That turn cannot go on Torso: Direction — and the weapon, shoot point and aim
    /// lines under it — are children of Torso, so turning Torso would swing the weapon 90° off
    /// the crosshair. A separate visual child absorbs the art's facing and leaves Direction
    /// exactly on the aim axis. Same shape as SkullGuySetup, where the sprite lives on a Visual
    /// child rather than on the logic root.
    /// </summary>
    private static Transform EnsureTorsoArtNode(Transform torso)
    {
        Transform node = torso.Find(TorsoArtNode);
        if (node == null)
        {
            var go = new GameObject(TorsoArtNode);
            Undo.RegisterCreatedObjectUndo(go, "Create " + TorsoArtNode);
            node = go.transform;
            node.SetParent(torso, false);
            node.SetAsFirstSibling();
        }

        node.localPosition = Vector3.zero;
        node.localScale = Vector3.one;
        node.localRotation = Quaternion.Euler(0f, 0f, ArtForwardOffsetDeg);
        node.gameObject.layer = torso.gameObject.layer;

        // Migrate the renderer off Torso, preserving its material, colour and mask interaction.
        var torsoRenderer = torso.GetComponent<SpriteRenderer>();
        if (torsoRenderer != null)
        {
            if (node.GetComponent<SpriteRenderer>() == null)
            {
                ComponentUtility.CopyComponent(torsoRenderer);
                ComponentUtility.PasteComponentAsNew(node.gameObject);
            }

            // SpriteFrameAnimator requires a SpriteRenderer, so it has to be removed first.
            var strandedAnimator = torso.GetComponent<SpriteFrameAnimator>();
            if (strandedAnimator != null) Undo.DestroyObjectImmediate(strandedAnimator);
            Undo.DestroyObjectImmediate(torsoRenderer);
        }

        if (node.GetComponent<SpriteRenderer>() == null)
            node.gameObject.AddComponent<SpriteRenderer>();

        return node;
    }

    /// <summary>
    /// Lines the legs art up with the same forward. Legs carry no children, so unlike the torso
    /// the turn can go straight on the transform via <see cref="PlayerLegs"/>'s offset.
    /// </summary>
    private static void SetupFacing(Transform legs, StringBuilder report)
    {
        PlayerLegs[] legsScripts = legs.GetComponents<PlayerLegs>();
        foreach (PlayerLegs pl in legsScripts)
        {
            var so = new SerializedObject(pl);
            so.FindProperty("spriteForwardOffsetDeg").floatValue = ArtForwardOffsetDeg;
            so.ApplyModifiedProperties();
        }

        report.AppendLine("-- facing --")
              .AppendLine($"art forward {ArtForwardOffsetDeg}° (drawn facing +Y, player forward is +X / Direction)")
              .AppendLine($"torso: absorbed by the {TorsoArtNode} child, Torso itself stays on the aim axis")
              .AppendLine($"legs: PlayerLegs.spriteForwardOffsetDeg on {legsScripts.Length} component(s)");

        if (legsScripts.Length > 1)
            report.AppendLine($"WARNING: {legs.name} has {legsScripts.Length} PlayerLegs components — delete the duplicates.");
    }

    /// <summary>
    /// Guarantees the legs always draw under the torso. Both halves sit at Order in Layer 0 on
    /// the Default layer with the same z, so nothing decides the tie and the legs can pop over
    /// the torso from frame to frame.
    ///
    /// The fix is a <see cref="SortingGroup"/> rather than plain per-renderer orders: order 1 in
    /// the Default layer is already taken by Table / Door_System / CraftingTable, which are
    /// deliberately drawn over the player so it can hide underneath them. The group keeps the
    /// whole player at order 0 against the world and sorts the halves only against each other.
    ///
    /// The group goes on the visual parent (PlayerModel), never on the player root: FieldOfView
    /// parents its stencil prepass (order -10) and mask mesh (order 5) to the root, and pulling
    /// those into a sorting group would break the vision masking.
    /// </summary>
    private static void SetupSorting(Transform torso, Transform torsoArt, Transform legs, StringBuilder report)
    {
        Transform model = torso.parent != null ? torso.parent : torso;
        var group = Ensure<SortingGroup>(model.gameObject);
        group.sortingOrder = 0;

        SetOrder(legs.GetComponent<SpriteRenderer>(), LegsOrder);
        SetOrder(torsoArt.GetComponent<SpriteRenderer>(), TorsoOrder);

        // Anything else parented under the torso (the Direction object holding the weapon
        // sprite) rides above it.
        foreach (SpriteRenderer sr in torso.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.transform != torsoArt && sr.transform != torso)
                SetOrder(sr, CarriedOrder);

        report.AppendLine("-- sorting --")
              .AppendLine($"SortingGroup on {model.name} (order {group.sortingOrder}); legs {LegsOrder} < torso {TorsoOrder} < carried {CarriedOrder}");
    }

    private static void SetOrder(SpriteRenderer sr, int order)
    {
        if (sr != null) sr.sortingOrder = order;
    }

    private static SpriteFrameAnimator SetupPart(GameObject part, bool isLegs, ref int clips, ref int frames, StringBuilder report)
    {
        var animator = Ensure<SpriteFrameAnimator>(part);
        clips += PlayerFrameLoader.LoadInto(animator, isLegs, out string partReport, out int partFrames);
        frames += partFrames;
        report.AppendLine($"-- {part.name} --").AppendLine(partReport);

        // Preview frame so the rig is visible in edit mode.
        var sr = part.GetComponent<SpriteRenderer>();
        Sprite preview = PlayerFrameLoader.GetPreviewFrame(isLegs);
        if (sr != null && preview != null)
            sr.sprite = preview;

        return animator;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
