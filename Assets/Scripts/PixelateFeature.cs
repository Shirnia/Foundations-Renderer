
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateFeature : ScriptableRendererFeature
{
    public static PixelateFeature Instance { get; private set; }

    [System.Serializable]
    public class PixelateSettings
    {
        private static readonly Color[] DefaultUnityPalette = BuildDefaultUnityPalette();

        public int screenHeight = 144;
        public Color[] palette = (Color[])DefaultUnityPalette.Clone();

        public void EnsurePaletteHasUnityDefaults()
        {
            if (palette == null || palette.Length < 60)
            {
                palette = (Color[])DefaultUnityPalette.Clone();
            }
        }

        private static Color[] BuildDefaultUnityPalette()
        {
            var colorProperties = new List<PropertyInfo>();
            var properties = typeof(Color).GetProperties(BindingFlags.Public | BindingFlags.Static);

            for (int i = 0; i < properties.Length; i++)
            {
                var property = properties[i];
                if (property.PropertyType == typeof(Color) && property.GetIndexParameters().Length == 0)
                {
                    colorProperties.Add(property);
                }
            }

            colorProperties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var palette = new Color[colorProperties.Count];
            for (int i = 0; i < colorProperties.Count; i++)
            {
                palette[i] = (Color)colorProperties[i].GetValue(null, null);
            }

            return palette;
        }
    }

    public PixelateSettings settings = new PixelateSettings();
    private PixelatePass pixelatePass;
    private Material material;
    private const string SHADER_NAME = "Hidden/URP/Quantize";

    public override void Create()
    {
        Instance = this;
        if (settings == null)
        {
            settings = new PixelateSettings();
        }
        settings.EnsurePaletteHasUnityDefaults();
        material = CoreUtils.CreateEngineMaterial(SHADER_NAME);
        pixelatePass = new PixelatePass(material);
    }

    private void OnValidate()
    {
        if (settings == null)
        {
            settings = new PixelateSettings();
        }

        settings.EnsurePaletteHasUnityDefaults();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // When we are here, the feature is ACTIVE
        Shader.SetGlobalFloat("_PixelateEnabled", 1f);

        if (settings.palette.Length > 0)
        {
            int paletteSize = Mathf.Min(settings.palette.Length, 256);
            var labColors = new Vector4[paletteSize];
            var rgbColors = new Vector4[paletteSize];

            for (int i = 0; i < paletteSize; i++)
            {
                rgbColors[i] = settings.palette[i];
                labColors[i] = RGBToLAB(settings.palette[i]);
            }

            material.SetVectorArray("_PaletteLAB", labColors);
            material.SetVectorArray("_PaletteRGB", rgbColors);
            material.SetInt("_PaletteSize", paletteSize);
        }

        pixelatePass.Setup(settings.screenHeight);
        pixelatePass.ConfigureInput(ScriptableRenderPassInput.Color);
        renderer.EnqueuePass(pixelatePass);
    }

    // Called when the renderer is being set up, including when the feature becomes inactive
    public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
    {
        // This acts as a safety fallback. If AddRenderPasses wasn't called this frame, we know we are inactive.
        // But since URP features are tricky, we'll reset it to 0 only when the "Active" checkbox is manually toggled.
        if (!isActive)
        {
            Shader.SetGlobalFloat("_PixelateEnabled", 0f);
            Shader.SetGlobalInt("_PaletteSize", 0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        Shader.SetGlobalFloat("_PixelateEnabled", 0f);
        CoreUtils.Destroy(material);
    }

    private Vector3 RGBToLAB(Color rgb)
    {
        float r = rgb.r <= 0.04045f ? rgb.r / 12.92f : Mathf.Pow((rgb.r + 0.055f) / 1.055f, 2.4f);
        float g = rgb.g <= 0.04045f ? rgb.g / 12.92f : Mathf.Pow((rgb.g + 0.055f) / 1.055f, 2.4f);
        float b = rgb.b <= 0.04045f ? rgb.b / 12.92f : Mathf.Pow((rgb.b + 0.055f) / 1.055f, 2.4f);

        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;

        x /= 0.95047f;
        y /= 1.00000f;
        z /= 1.08883f;

        x = x > 0.008856f ? Mathf.Pow(x, 1f/3f) : (7.787f * x) + (16f/116f);
        y = y > 0.008856f ? Mathf.Pow(y, 1f/3f) : (7.787f * y) + (16f/116f);
        z = z > 0.008856f ? Mathf.Pow(z, 1f/3f) : (7.787f * z) + (16f/116f);

        float L = (116f * y) - 16f;
        float A = 500f * (x - y);
        float B = 200f * (y - z);

        return new Vector3(L, A, B);
    }
}
