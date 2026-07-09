using UnityEditor;
using UnityEngine;

/// <summary>
/// Auto-configures import settings for the downscaled SkullGuy animation frames
/// so all 200+ PNGs become game-ready sprites without manual per-file setup.
/// Runs on (re)import for any texture under <see cref="TargetFolder"/>.
///
/// After adding/changing this script, right-click the folder in Unity and choose
/// "Reimport" once so existing frames pick up these settings.
/// </summary>
public class SkullGuyFrameImporter : AssetPostprocessor
{
    private const string TargetFolder = "Assets/Art/SKULL-GUY-optimized";

    // Frames are ~512 px. PPU controls on-screen size; tweak here or scale the
    // enemy transform. Higher PPU = smaller sprite in world units.
    private const float PixelsPerUnit = 256f;
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

        // Pivot in the center — suitable for a top-down character.
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
    }
}
