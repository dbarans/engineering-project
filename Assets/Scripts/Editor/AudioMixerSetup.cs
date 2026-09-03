using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Creates <c>Assets/Resources/MainMixer.mixer</c> with the group layout
/// <see cref="AudioMixerService"/> expects: a Master group with Music, SFX and UI under it,
/// each group's volume exposed as <c>MasterVolume</c> / <c>MusicVolume</c> / <c>SfxVolume</c>
/// / <c>UiVolume</c>.
///
/// A generator rather than a checked-in asset, and the reason is worth recording: a mixer
/// asset hand-written as YAML <em>hangs Unity's importer</em>. There is no public API for
/// creating one either — <c>AssetDatabase.CreateAsset(new AudioMixer())</c> is not a
/// supported call — so this goes through the same internal controller the
/// <c>Assets ▸ Create ▸ Audio Mixer</c> menu item uses, by reflection. Ugly, but it is
/// Unity's own code path, which is exactly the property that matters here.
///
/// Non-destructive like <see cref="SoundBankSetup"/>: an existing mixer is inspected and
/// reported on, never rebuilt. Re-running it is how you check the asset is still wired the
/// way the code expects.
/// </summary>
public static class AudioMixerSetup
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string AssetPath = ResourcesFolder + "/" + AudioMixerService.ResourcesPath + ".mixer";

    /// <summary>Groups created under Master, in the order they appear in the mixer window.</summary>
    private static readonly AudioChannel[] ChildChannels =
    {
        AudioChannel.Music,
        AudioChannel.Sfx,
        AudioChannel.Ui,
    };

    [MenuItem("Tools/Audio/Build Audio Mixer")]
    public static void Build()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
        if (existing != null)
        {
            Report("Mixer already exists — checked, not rebuilt.", existing);
            return;
        }

        Directory.CreateDirectory(ResourcesFolder);
        AssetDatabase.Refresh();

        AudioMixer mixer;
        try
        {
            mixer = CreateMixer();
        }
        catch (Exception e)
        {
            // Reflection into editor internals is the one thing here that can rot across
            // Unity versions, so it fails loudly with a way out rather than a stack trace.
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Audio Mixer",
                "Could not create the mixer through Unity's internal API — see the console.\n\n" +
                "Build it by hand instead:\n" +
                $"1. Assets ▸ Create ▸ Audio Mixer, named '{AudioMixerService.ResourcesPath}', in {ResourcesFolder}.\n" +
                "2. Add three groups under Master: Music, SFX, UI.\n" +
                "3. Right-click each group's Volume ▸ Expose, then rename the parameters to " +
                "MasterVolume, MusicVolume, SfxVolume, UiVolume.\n\n" +
                "Until then every sound still plays, just unrouted.", "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Report($"Created '{AssetPath}'.", mixer);
    }

    /// <summary>
    /// Builds the mixer through <c>UnityEditor.Audio.AudioMixerController</c>. Every member
    /// touched here is internal, hence the reflection; the names are checked one at a time
    /// so a failure says which one moved.
    /// </summary>
    private static AudioMixer CreateMixer()
    {
        Assembly editorAssembly = typeof(EditorApplication).Assembly;
        Type controllerType = RequireType(editorAssembly, "UnityEditor.Audio.AudioMixerController");
        Type groupType = RequireType(editorAssembly, "UnityEditor.Audio.AudioMixerGroupController");
        Type parameterPathType = RequireType(editorAssembly, "UnityEditor.Audio.AudioGroupParameterPath");

        object controller = RequireMethod(controllerType, "CreateMixerControllerAtPath")
            .Invoke(null, new object[] { AssetPath });
        if (controller == null)
            throw new InvalidOperationException($"CreateMixerControllerAtPath returned null for '{AssetPath}'.");

        object master = RequireProperty(controllerType, "masterGroup").GetValue(controller);
        if (master == null)
            throw new InvalidOperationException("The new mixer has no master group.");

        MethodInfo createGroup = RequireMethod(controllerType, "CreateNewGroup");
        MethodInfo addChild = RequireMethod(controllerType, "AddChildToParent");

        var groups = new List<object> { master };

        foreach (AudioChannel channel in ChildChannels)
        {
            object group = createGroup.Invoke(controller, new object[] { GroupName(channel), false });
            if (group == null)
                throw new InvalidOperationException($"CreateNewGroup returned null for '{GroupName(channel)}'.");

            addChild.Invoke(controller, new[] { group, master });
            groups.Add(group);
        }

        ExposeVolume(controller, master, AudioChannel.Master, controllerType, groupType, parameterPathType);
        for (int i = 0; i < ChildChannels.Length; i++)
            ExposeVolume(controller, groups[i + 1], ChildChannels[i], controllerType, groupType, parameterPathType);

        BuildDefaultView(controller, groups, controllerType, groupType, editorAssembly);

        var mixer = (AudioMixer)controller;
        EditorUtility.SetDirty(mixer);
        return mixer;
    }

    /// <summary>
    /// Gives the mixer the one view that lists all four groups.
    ///
    /// A freshly created controller has an <em>empty</em> <c>views</c> array, which is why
    /// the obvious call — <c>AddGroupToCurrentView</c> — throws
    /// <c>IndexOutOfRangeException</c> here: it indexes the current view before one exists.
    /// Writing the view directly sidesteps that. Without it the mixer is wired correctly
    /// and routes sound correctly, but the Audio Mixer window shows only Master.
    /// </summary>
    private static void BuildDefaultView(
        object controller, List<object> groups, Type controllerType, Type groupType, Assembly editorAssembly)
    {
        Type viewType = RequireType(editorAssembly, "UnityEditor.Audio.MixerGroupView");
        PropertyInfo groupIdProperty = RequireProperty(groupType, "groupID");
        Type guidType = groupIdProperty.PropertyType;

        Array guids = Array.CreateInstance(guidType, groups.Count);
        for (int i = 0; i < groups.Count; i++)
            guids.SetValue(groupIdProperty.GetValue(groups[i]), i);

        object view = Activator.CreateInstance(viewType);
        RequireField(viewType, "guids").SetValue(view, guids);
        RequireField(viewType, "name").SetValue(view, "View");

        Array views = Array.CreateInstance(viewType, 1);
        views.SetValue(view, 0);

        RequireProperty(controllerType, "views").SetValue(controller, views);
        RequireProperty(controllerType, "currentViewIndex").SetValue(controller, 0);
    }

    /// <summary>
    /// Exposes a group's volume and renames the exposed parameter.
    ///
    /// Two steps, because <c>AddExposedParameter</c> names the parameter after the group
    /// path it was exposed from, and the names in <see cref="AudioMixerService"/> have to
    /// survive somebody renaming a group.
    /// </summary>
    private static void ExposeVolume(
        object controller, object group, AudioChannel channel,
        Type controllerType, Type groupType, Type parameterPathType)
    {
        object volumeGuid = RequireMethod(groupType, "GetGUIDForVolume").Invoke(group, null);
        object path = Activator.CreateInstance(parameterPathType, group, volumeGuid);

        RequireMethod(controllerType, "AddExposedParameter").Invoke(controller, new[] { path });

        PropertyInfo exposedProperty = RequireProperty(controllerType, "exposedParameters");
        var exposed = (Array)exposedProperty.GetValue(controller);
        if (exposed == null) throw new InvalidOperationException("exposedParameters is null after exposing a volume.");

        string wanted = AudioMixerService.VolumeParameter(channel);
        bool renamed = false;

        for (int i = 0; i < exposed.Length; i++)
        {
            // Boxed struct: mutate the copy, then write it back into the array.
            object parameter = exposed.GetValue(i);
            Type parameterType = parameter.GetType();

            if (!Equals(RequireField(parameterType, "guid").GetValue(parameter), volumeGuid)) continue;

            RequireField(parameterType, "name").SetValue(parameter, wanted);
            exposed.SetValue(parameter, i);
            renamed = true;
            break;
        }

        if (!renamed)
            throw new InvalidOperationException($"Exposed parameter for the {channel} volume was not found to rename.");

        exposedProperty.SetValue(controller, exposed);
    }

    private static string GroupName(AudioChannel channel)
    {
        switch (channel)
        {
            case AudioChannel.Music: return "Music";
            case AudioChannel.Ui: return "UI";
            case AudioChannel.Master: return "Master";
            default: return "SFX";
        }
    }

    /// <summary>
    /// Logs and shows what the mixer actually contains, resolved through
    /// <see cref="AudioMixerService"/> — so this reports on the lookup the game does at
    /// runtime, not on a second description of it that could agree with the asset while
    /// the game still fails.
    /// </summary>
    private static void Report(string headline, AudioMixer mixer)
    {
        var summary = new System.Text.StringBuilder(headline).AppendLine().AppendLine();

        foreach (AudioChannel channel in new[] { AudioChannel.Master, AudioChannel.Music, AudioChannel.Sfx, AudioChannel.Ui })
        {
            string parameter = AudioMixerService.VolumeParameter(channel);
            bool hasGroup = mixer.FindMatchingGroups(GroupName(channel)).Length > 0;
            bool hasParameter = mixer.GetFloat(parameter, out _);

            summary.AppendLine(
                $"{GroupName(channel),-7} group: {(hasGroup ? "yes" : "MISSING")}   " +
                $"{parameter}: {(hasParameter ? "exposed" : "NOT EXPOSED")}");
        }

        Debug.Log($"[Audio Mixer] {summary}", mixer);
        EditorUtility.DisplayDialog("Audio Mixer", summary.ToString(), "OK");
        Selection.activeObject = mixer;
    }

    private static Type RequireType(Assembly assembly, string name)
    {
        return assembly.GetType(name)
            ?? throw new MissingMemberException($"'{name}' is gone from {assembly.GetName().Name}.");
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static PropertyInfo RequireProperty(Type type, string name)
    {
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMemberException(type.FullName, name);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(type.FullName, name);
    }
}
