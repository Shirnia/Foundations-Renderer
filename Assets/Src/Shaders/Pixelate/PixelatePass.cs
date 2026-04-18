
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class PixelatePass : ScriptableRenderPass
{
    private Material material;
    private int screenHeight;

    public PixelatePass(Material material)
    {
        this.material = material;
        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        // This ensures URP creates an intermediate texture so we can read from it
        requiresIntermediateTexture = true;
    }

    public void Setup(int screenHeight)
    {
        this.screenHeight = screenHeight;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        
        if (resourceData.isActiveTargetBackBuffer)
        {
            // We cannot read from the backbuffer, so we skip if no intermediate texture is available
            return;
        }

        TextureHandle source = resourceData.activeColorTexture;
        TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);

        // Calculate downsampled dimensions
        float aspectRatio = (float)sourceDesc.width / sourceDesc.height;
        int width = Mathf.CeilToInt(screenHeight * aspectRatio);

        // Create temporary texture for downsampling
        TextureDesc downsampleDesc = sourceDesc;
        downsampleDesc.width = width;
        downsampleDesc.height = screenHeight;
        downsampleDesc.name = "_PixelateTemporaryRT";
        downsampleDesc.filterMode = FilterMode.Point;
        downsampleDesc.depthBufferBits = 0;

        TextureHandle temporaryRT = renderGraph.CreateTexture(downsampleDesc);

        // 1. Downsample + Quantize
        // This blits from screen to the small texture using our quantization material
        RenderGraphUtils.BlitMaterialParameters downsamplePara = new(source, temporaryRT, material, 0);
        renderGraph.AddBlitPass(downsamplePara, passName: "Pixelate Downsample");

        // 2. Upsample back to original
        // This blits from the small texture back to the screen
        // We use the same material; since it's already quantized, it will just scale up with Point filtering
        RenderGraphUtils.BlitMaterialParameters upsamplePara = new(temporaryRT, source, material, 0);
        renderGraph.AddBlitPass(upsamplePara, passName: "Pixelate Upsample");
    }
}
