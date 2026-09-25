using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renderer feature that runs <c>Shaders/HorrorFullScreen.shader</c> over the finished frame:
/// barrel warp, breathing zoom and scanlines. Added to
/// <c>Settings/Renderer2D.asset</c> by <c>Tools ▸ Horror Post FX ▸ Set Up Horror Post-Processing</c>.
///
/// Deliberately hand-rolled instead of URP's built-in <c>FullScreenPassRendererFeature</c>: this
/// owns its own serialized fields, so the setup tool can assign the material from script without
/// reflecting over URP internals whose field names move between package versions.
///
/// Injected at <see cref="RenderPassEvent.AfterRenderingPostProcessing"/> — the warp has to run on
/// top of the Volume's grading and bloom, otherwise the warp and scanlines would be graded and
/// re-bloomed and stop reading as a broken signal.
/// </summary>
public class HorrorFullScreenFeature : ScriptableRendererFeature
{
    [Tooltip("Material using the Custom/HorrorFullScreen shader.")]
    public Material material;

    private HorrorFullScreenPass pass;

    public override void Create()
    {
        pass = new HorrorFullScreenPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null) return;

        // Scene view and preview cameras render the same effect on purpose — authoring lights and
        // props against the un-warped image and then finding the game looks different is worse
        // than a slightly harder-to-read Scene view. Reflection/preview cameras are excluded
        // because they render off-screen targets the blit would have nothing sensible to do with.
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;

        pass.Setup(material);
        renderer.EnqueuePass(pass);
    }

    private class HorrorFullScreenPass : ScriptableRenderPass
    {
        private Material material;

        public void Setup(Material passMaterial)
        {
            material = passMaterial;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();

            // A backbuffer target cannot be read and written in one pass. requiresIntermediateTexture
            // above normally prevents this, but a camera stack or an editor path can still land here.
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "HorrorFullScreen";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;

            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            var blitParameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0);
            renderGraph.AddBlitPass(blitParameters, "Horror Full Screen");

            // Hand the warped copy back as the camera colour, so anything after this (UI overlay,
            // the final blit to the screen) reads the processed image.
            resourceData.cameraColor = destination;
        }
    }
}
