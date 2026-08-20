using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-click setup for the horror post-processing stack (see POSTFX_NOTES.md). Everything the
/// effect needs is generated from script rather than committed as hand-written YAML, because a
/// Volume profile and a renderer feature are both sub-asset-heavy formats where a hand-edited
/// GUID is a silent, hard-to-spot break.
///
/// Six steps, all idempotent — re-running never duplicates anything and never overwrites a profile
/// you have since re-tuned by hand:
///   1. <c>Materials/HorrorFullScreen.mat</c> from the Custom/HorrorFullScreen shader.
///   2. <c>Settings/HorrorVolumeProfile.asset</c> with the colour half of the look.
///   3. <see cref="HorrorFullScreenFeature"/> added to <c>Settings/Renderer2D.asset</c>.
///   4. HDR colour grading on the URP asset, so the bloom and the grading do not band.
///   5. Post-processing switched on for the Player prefab camera and every camera in the open scene.
///   6. A "Global Volume" object carrying the profile and <see cref="HorrorPostProcessing"/>.
///
/// Step 5 is the one that matters most: the project shipped with <c>renderPostProcessing</c> off on
/// the player camera, which makes every Volume in the project a no-op. Nothing else here is visible
/// until that flag is set.
/// </summary>
public static class HorrorPostProcessingSetup
{
    private const string ShaderName = "Custom/HorrorFullScreen";
    private const string MaterialPath = "Assets/Materials/HorrorFullScreen.mat";
    private const string ProfilePath = "Assets/Settings/HorrorVolumeProfile.asset";
    private const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
    private const string UrpAssetPath = "Assets/Settings/UniversalRP.asset";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    private const string VolumeObjectName = "Global Volume";

    [MenuItem("Tools/Horror Post FX/Set Up Horror Post-Processing")]
    public static void SetUp()
    {
        Material material = EnsureMaterial();
        if (material == null) return;

        VolumeProfile profile = EnsureProfile();
        bool featureAdded = EnsureRendererFeature(material);
        bool gradingChanged = EnsureHdrColorGrading();
        int camerasEnabled = EnablePostProcessingOnCameras();
        bool volumeCreated = EnsureSceneVolume(profile);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Horror Post FX",
            "Setup complete.\n\n" +
            $"Material: {MaterialPath}\n" +
            $"Profile: {ProfilePath}\n" +
            $"Renderer feature: {(featureAdded ? "added to Renderer2D" : "already present")}\n" +
            $"HDR grading: {(gradingChanged ? "enabled" : "already enabled")}\n" +
            $"Cameras with post-processing turned on: {camerasEnabled}\n" +
            $"Global Volume: {(volumeCreated ? "created in the open scene" : "already in the scene")}\n\n" +
            "Tune the look on the Global Volume's profile, and the reactive part on its " +
            "HorrorPostProcessing component.",
            "OK");
    }

    /// <summary>
    /// Removes the effect without deleting the assets: drops the renderer feature and switches the
    /// Global Volume off. Here because the fullscreen pass is the one part of this that cannot be
    /// disabled from the inspector in one place, and hunting it down inside the renderer asset to
    /// A/B the look is tedious.
    /// </summary>
    [MenuItem("Tools/Horror Post FX/Disable Horror Post-Processing")]
    public static void Disable()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer2DPath);
        if (rendererData != null) RemoveRendererFeature(rendererData);

        foreach (var controller in Object.FindObjectsByType<HorrorPostProcessing>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(controller.gameObject, "Disable Horror Post FX");
            controller.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Horror Post FX disabled: fullscreen pass removed, Global Volume switched off. " +
                  "Re-run the setup item to put it back.");
    }

    private static Material EnsureMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "Horror Post FX",
                $"Shader '{ShaderName}' not found.\n\n" +
                "Assets/Shaders/HorrorFullScreen.shader has to compile before the setup can run. " +
                "Check the console for shader errors.",
                "OK");
            return null;
        }

        var material = new Material(shader) { name = "HorrorFullScreen" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    /// <summary>
    /// Creates the Volume profile with the colour half of the look. Leaves an existing profile
    /// completely alone — this tool is expected to be re-run after the profile has been tuned by
    /// hand, and silently resetting that tuning would be the worst thing it could do.
    /// </summary>
    private static VolumeProfile EnsureProfile()
    {
        var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (existing != null) return existing;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);

        // VolumeProfile.Add<T>() only creates the override in memory and lists it in
        // profile.components — it does NOT persist it as a sub-asset. Without an explicit
        // AssetDatabase.AddObjectToAsset() per override, AssetDatabase.SaveAssets() writes the
        // profile with an empty components list, and every value set below is silently thrown
        // away the moment the asset is saved (an active Editor session still shows the live,
        // in-memory values in the meantime — which is why the look could be seen and then vanish).
        VolumeComponent AddPersisted<T>() where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // Neutral, not ACES. ACES lifts and rolls contrast in a way that fights the DarknessOverlay
        // (0.97 alpha over everything unlit, see ENEMY_NOTES.md GU-0036) and crushes the little
        // detail left in the shadows, which is exactly where this game asks the player to look.
        var tonemapping = (Tonemapping)AddPersisted<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.Neutral);

        // Cool, drained and slightly contrastier. The saturation here is the baseline the
        // HorrorPostProcessing dread reaction pulls further down from.
        var colorAdjustments = (ColorAdjustments)AddPersisted<ColorAdjustments>();
        colorAdjustments.postExposure.Override(0f);
        colorAdjustments.contrast.Override(12f);
        colorAdjustments.colorFilter.Override(new Color(0.86f, 0.91f, 1f));
        colorAdjustments.saturation.Override(-28f);

        // Teal shadows against faintly warm highlights — the split that reads as "horror" rather
        // than merely "desaturated", because it keeps lamp light looking like fire.
        var splitToning = (SplitToning)AddPersisted<SplitToning>();
        splitToning.shadows.Override(new Color(0.15f, 0.26f, 0.31f));
        splitToning.highlights.Override(new Color(0.32f, 0.27f, 0.19f));
        splitToning.balance.Override(-15f);

        var vignette = (Vignette)AddPersisted<Vignette>();
        vignette.color.Override(Color.black);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.intensity.Override(0.4f);
        vignette.smoothness.Override(0.55f);
        vignette.rounded.Override(false);

        var filmGrain = (FilmGrain)AddPersisted<FilmGrain>();
        filmGrain.type.Override(FilmGrainLookup.Medium1);
        filmGrain.intensity.Override(0.32f);
        // High response keeps the grain off the bright areas, so lamps stay clean and only the
        // dark two-thirds of the frame get noisy — which is where the tension is anyway.
        filmGrain.response.Override(0.8f);

        var chromaticAberration = (ChromaticAberration)AddPersisted<ChromaticAberration>();
        chromaticAberration.intensity.Override(0.08f);

        // Threshold deliberately just under 1: anything lower and the bloom starts eating the pixel
        // art itself instead of only the light sources, which turns crisp sprites to mush.
        var bloom = (Bloom)AddPersisted<Bloom>();
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.65f);
        bloom.scatter.Override(0.72f);
        bloom.tint.Override(new Color(1f, 0.93f, 0.82f));

        EditorUtility.SetDirty(profile);
        return profile;
    }

    /// <summary>
    /// Appends <see cref="HorrorFullScreenFeature"/> to the 2D renderer as a sub-asset. Done through
    /// SerializedObject because <c>m_RendererFeatures</c> and its parallel <c>m_RendererFeatureMap</c>
    /// have no public API — this mirrors what URP's own renderer inspector does when you press
    /// "Add Renderer Feature".
    /// </summary>
    /// <returns>True if a feature was added, false if one was already there.</returns>
    private static bool EnsureRendererFeature(Material material)
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer2DPath);
        if (rendererData == null)
        {
            Debug.LogError($"Horror Post FX: no renderer data at {Renderer2DPath}.");
            return false;
        }

        foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(Renderer2DPath))
        {
            if (subAsset is not HorrorFullScreenFeature alreadyThere) continue;

            // Re-point the material anyway: the feature surviving a material that was deleted and
            // regenerated is exactly the case where the effect silently stops rendering.
            alreadyThere.material = material;
            EditorUtility.SetDirty(alreadyThere);
            return false;
        }

        var feature = ScriptableObject.CreateInstance<HorrorFullScreenFeature>();
        feature.name = "HorrorFullScreenFeature";
        feature.material = material;
        // Sub-assets of a renderer are hidden the same way URP hides its own, so the Project window
        // shows one renderer asset rather than a renderer plus a loose feature object.
        feature.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(feature, rendererData);

        var serializedRenderer = new SerializedObject(rendererData);
        SerializedProperty features = serializedRenderer.FindProperty("m_RendererFeatures");
        SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");

        int index = features.arraySize;
        features.InsertArrayElementAtIndex(index);
        features.GetArrayElementAtIndex(index).objectReferenceValue = feature;

        featureMap.InsertArrayElementAtIndex(index);
        featureMap.GetArrayElementAtIndex(index).longValue = feature.GetInstanceID();

        serializedRenderer.ApplyModifiedProperties();
        EditorUtility.SetDirty(rendererData);
        return true;
    }

    private static void RemoveRendererFeature(ScriptableRendererData rendererData)
    {
        var serializedRenderer = new SerializedObject(rendererData);
        SerializedProperty features = serializedRenderer.FindProperty("m_RendererFeatures");
        SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");

        for (int i = features.arraySize - 1; i >= 0; i--)
        {
            if (features.GetArrayElementAtIndex(i).objectReferenceValue is not HorrorFullScreenFeature) continue;

            // DeleteArrayElementAtIndex on an object-reference element nulls it first and only
            // removes it on the second call — the documented quirk, not a mistake.
            features.GetArrayElementAtIndex(i).objectReferenceValue = null;
            features.DeleteArrayElementAtIndex(i);
            if (i < featureMap.arraySize) featureMap.DeleteArrayElementAtIndex(i);
        }

        serializedRenderer.ApplyModifiedProperties();

        foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(rendererData)))
        {
            if (subAsset is HorrorFullScreenFeature feature) Object.DestroyImmediate(feature, true);
        }

        EditorUtility.SetDirty(rendererData);
    }

    /// <summary>
    /// Switches the URP asset to HDR colour grading. In LDR mode the grading LUT is built in
    /// display space, and a look this dark and this heavily graded bands visibly in the shadow
    /// falloff of every lamp.
    /// </summary>
    private static bool EnsureHdrColorGrading()
    {
        var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
        if (urpAsset == null) return false;
        if (urpAsset.colorGradingMode == ColorGradingMode.HighDynamicRange) return false;

        var serializedUrp = new SerializedObject(urpAsset);
        serializedUrp.FindProperty("m_ColorGradingMode").enumValueIndex = (int)ColorGradingMode.HighDynamicRange;
        serializedUrp.ApplyModifiedProperties();
        EditorUtility.SetDirty(urpAsset);
        return true;
    }

    /// <summary>
    /// Turns <c>renderPostProcessing</c> on for the Player prefab's camera and for every camera in
    /// the open scene. Without this the whole stack renders nothing at all.
    /// </summary>
    private static int EnablePostProcessingOnCameras()
    {
        int enabled = 0;

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (prefabRoot != null)
        {
            bool prefabChanged = false;
            foreach (var cameraData in prefabRoot.GetComponentsInChildren<UniversalAdditionalCameraData>(true))
            {
                if (cameraData.renderPostProcessing) continue;

                cameraData.renderPostProcessing = true;
                prefabChanged = true;
                enabled++;
            }

            if (prefabChanged) PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            // Skip cameras living inside a prefab instance: the flag is already set on the asset
            // above, and setting it again here would author a pointless instance override.
            if (PrefabUtility.IsPartOfPrefabInstance(camera)) continue;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData == null || cameraData.renderPostProcessing) continue;

            Undo.RecordObject(cameraData, "Enable Post Processing");
            cameraData.renderPostProcessing = true;
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            enabled++;
        }

        return enabled;
    }

    private static bool EnsureSceneVolume(VolumeProfile profile)
    {
        var existing = Object.FindFirstObjectByType<HorrorPostProcessing>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
            return false;
        }

        // Layer 0 (Default) on purpose: the cameras' m_VolumeLayerMask is Default-only, and a
        // volume on any other layer is simply never seen.
        var volumeObject = new GameObject(VolumeObjectName) { layer = 0 };

        var volume = EditorSetupUtility.EnsureComponent<Volume>(volumeObject);
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;

        EditorSetupUtility.EnsureComponent<HorrorPostProcessing>(volumeObject);

        Undo.RegisterCreatedObjectUndo(volumeObject, "Create Global Volume");
        EditorSceneManager.MarkSceneDirty(volumeObject.scene);
        return true;
    }
}
