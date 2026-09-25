using UnityEditor;
using UnityEngine;

/// <summary>
/// Auto-configures import settings for the player animation frames, the counterpart of
/// <see cref="SkullGuyFrameImporter"/>. Runs on (re)import for any texture under
/// <see cref="TargetFolder"/>.
///
/// The important part is <see cref="SpriteImportMode.Single"/> with a centered pivot: the
/// frames arrive sliced into per-frame tight crops, and a pivot centered on each frame's own
/// crop makes the character jitter as the crop changes shape from frame to frame. Keeping the
/// full 1900x1900 canvas registers every frame against the same origin.
///
/// After adding/changing this script, run Tools > Player > Reimport Frames once so the
/// existing frames pick up these settings.
/// </summary>
public class PlayerFrameImporter : AssetPostprocessor
{
    public const string TargetFolder = "Assets/Art/PLAYER";

    // Frames are 1900x1900 with the character occupying roughly the middle 600 px, so this
    // PPU puts the player at about 2 world units — the same on-screen scale as the SkullGuy.
    // This is the knob to turn if the player comes out too big or too small.
    private const float PixelsPerUnit = 300f;

    // The frames are not trimmed, so most of each texture is empty. 512 keeps the memory
    // budget in line with the enemy (~275 frames); raise it once the frames are cropped down
    // to a shared tight canvas the way SKULL-GUY-optimized is.
    private const int MaxTextureSize = 512;

    private void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.StartsWith(TargetFolder))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.maxTextureSize = MaxTextureSize;

        // Pivot in the center of the full frame — all halves share one origin, which is what
        // lets the torso and legs stay attached while they rotate independently.
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
    }
}
