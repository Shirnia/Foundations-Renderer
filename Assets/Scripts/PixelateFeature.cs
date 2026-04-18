
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateFeature : ScriptableRendererFeature
{
    private const int PaletteTextureHeight = 1;
    private const string ShaderName = "Hidden/URP/Quantize";
    private static readonly int PaletteLabTexId = Shader.PropertyToID("_PaletteLABTex");
    private static readonly int PaletteRgbTexId = Shader.PropertyToID("_PaletteRGBTex");
    private static readonly int PaletteSizeId = Shader.PropertyToID("_PaletteSize");

    public static PixelateFeature Instance { get; private set; }

    [System.Serializable]
    public class PixelateSettings
    {
        private const int DefaultPaletteColorCount = 128;
        public const int MinPaletteColorCount = 5;
        public const int MaxRuntimePaletteColorCount = 2048;

        private static readonly Color[] AnchorColors =
        {
            Color.black,
            Color.white,
            Color.red,
            Color.green,
            Color.blue
        };

        [Min(MinPaletteColorCount)]
        public int defaultPaletteColorCount = DefaultPaletteColorCount;

        [SerializeField, HideInInspector]
        private int lastGeneratedPaletteColorCount = DefaultPaletteColorCount;

        public int screenHeight = 144;
        public Color[] palette = BuildDefaultUnityPalette(DefaultPaletteColorCount);

        public void EnsurePaletteHasUnityDefaults()
        {
            int sanitizedCount = SanitizePaletteCount(defaultPaletteColorCount, true);
            if (defaultPaletteColorCount != sanitizedCount)
            {
                defaultPaletteColorCount = sanitizedCount;
            }

            bool hasValidPalette = palette != null && palette.Length >= MinPaletteColorCount;

            // Migration path for pre-count assets: preserve existing palette until count changes.
            if (lastGeneratedPaletteColorCount <= 0)
            {
                if (hasValidPalette)
                {
                    lastGeneratedPaletteColorCount = sanitizedCount;
                    return;
                }
            }

            if (!hasValidPalette || sanitizedCount != lastGeneratedPaletteColorCount)
            {
                palette = BuildDefaultUnityPalette(sanitizedCount);
                lastGeneratedPaletteColorCount = sanitizedCount;
            }
        }

        private static int SanitizePaletteCount(int requestedCount, bool logClampWarning)
        {
            int sanitizedCount = Mathf.Max(requestedCount, MinPaletteColorCount);

            if (sanitizedCount > MaxRuntimePaletteColorCount)
            {
                if (logClampWarning)
                {
                    Debug.LogWarning(
                        $"PixelateFeature palette count {requestedCount} exceeds the max {MaxRuntimePaletteColorCount}. Clamping.");
                }

                sanitizedCount = MaxRuntimePaletteColorCount;
            }

            return sanitizedCount;
        }

        public static Color[] BuildDefaultUnityPalette(int colorCount)
        {
            int targetCount = SanitizePaletteCount(colorCount, false);
            var paletteBuffer = new List<Color>(targetCount);

            int anchorCount = Mathf.Min(targetCount, AnchorColors.Length);
            for (int i = 0; i < anchorCount; i++)
            {
                paletteBuffer.Add(AnchorColors[i]);
            }

            int remainingCount = targetCount - paletteBuffer.Count;
            if (remainingCount <= 0)
            {
                return paletteBuffer.ToArray();
            }

            int levelsPerChannel = 2;
            while ((levelsPerChannel * levelsPerChannel * levelsPerChannel) - AnchorColors.Length < remainingCount)
            {
                levelsPerChannel++;
            }

            float channelStep = levelsPerChannel > 1 ? 1f / (levelsPerChannel - 1) : 0f;
            var candidateColors = new List<Color>(levelsPerChannel * levelsPerChannel * levelsPerChannel);
            for (int r = 0; r < levelsPerChannel; r++)
            {
                for (int g = 0; g < levelsPerChannel; g++)
                {
                    for (int b = 0; b < levelsPerChannel; b++)
                    {
                        var candidate = new Color(r * channelStep, g * channelStep, b * channelStep, 1f);
                        if (IsAnchorColor(candidate))
                        {
                            continue;
                        }

                        candidateColors.Add(candidate);
                    }
                }
            }

            var anchorLabs = new Vector3[anchorCount];
            for (int i = 0; i < anchorCount; i++)
            {
                anchorLabs[i] = RGBToLAB(AnchorColors[i]);
            }

            var candidateLabs = new Vector3[candidateColors.Count];
            var nearestDistanceSq = new float[candidateColors.Count];
            var selectedMask = new bool[candidateColors.Count];

            for (int i = 0; i < candidateColors.Count; i++)
            {
                candidateLabs[i] = RGBToLAB(candidateColors[i]);
                float minDistanceSq = float.MaxValue;

                for (int j = 0; j < anchorLabs.Length; j++)
                {
                    float distanceSq = (candidateLabs[i] - anchorLabs[j]).sqrMagnitude;
                    if (distanceSq < minDistanceSq)
                    {
                        minDistanceSq = distanceSq;
                    }
                }

                nearestDistanceSq[i] = minDistanceSq;
            }

            int colorsToPick = Mathf.Min(remainingCount, candidateColors.Count);
            for (int pick = 0; pick < colorsToPick; pick++)
            {
                int bestIndex = -1;
                float bestDistanceSq = -1f;

                for (int i = 0; i < candidateColors.Count; i++)
                {
                    if (selectedMask[i])
                    {
                        continue;
                    }

                    float distanceSq = nearestDistanceSq[i];
                    if (distanceSq > bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                selectedMask[bestIndex] = true;
                paletteBuffer.Add(candidateColors[bestIndex]);

                Vector3 selectedLab = candidateLabs[bestIndex];
                for (int i = 0; i < candidateColors.Count; i++)
                {
                    if (selectedMask[i])
                    {
                        continue;
                    }

                    float distanceSq = (candidateLabs[i] - selectedLab).sqrMagnitude;
                    if (distanceSq < nearestDistanceSq[i])
                    {
                        nearestDistanceSq[i] = distanceSq;
                    }
                }
            }

            return paletteBuffer.ToArray();
        }

        private static bool IsAnchorColor(Color candidate)
        {
            for (int i = 0; i < AnchorColors.Length; i++)
            {
                if (ApproximatelySameColor(candidate, AnchorColors[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ApproximatelySameColor(Color lhs, Color rhs)
        {
            const float tolerance = 0.0001f;
            return Mathf.Abs(lhs.r - rhs.r) < tolerance &&
                   Mathf.Abs(lhs.g - rhs.g) < tolerance &&
                   Mathf.Abs(lhs.b - rhs.b) < tolerance;
        }
    }

    public PixelateSettings settings = new PixelateSettings();
    private PixelatePass pixelatePass;
    private Material material;
    private Texture2D paletteLabTexture;
    private Texture2D paletteRgbTexture;
    private int lastPaletteHash = int.MinValue;
    private int lastUploadedPaletteSize = -1;
    private int lastRuntimeClampWarningSourceLength = -1;

    public override void Create()
    {
        Instance = this;
        if (settings == null)
        {
            settings = new PixelateSettings();
        }

        settings.EnsurePaletteHasUnityDefaults();
        material = CoreUtils.CreateEngineMaterial(ShaderName);
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
        if (settings == null)
        {
            settings = new PixelateSettings();
        }

        settings.EnsurePaletteHasUnityDefaults();

        // When we are here, the feature is ACTIVE.
        Shader.SetGlobalFloat("_PixelateEnabled", 1f);

        int sourceLength = settings.palette == null ? 0 : settings.palette.Length;
        if (sourceLength > PixelateSettings.MaxRuntimePaletteColorCount &&
            sourceLength != lastRuntimeClampWarningSourceLength)
        {
            Debug.LogWarning(
                $"PixelateFeature palette has {sourceLength} colors; runtime supports up to " +
                $"{PixelateSettings.MaxRuntimePaletteColorCount}. Truncating at runtime.");
            lastRuntimeClampWarningSourceLength = sourceLength;
        }

        int paletteSize = Mathf.Min(sourceLength, PixelateSettings.MaxRuntimePaletteColorCount);
        if (paletteSize > 0)
        {
            UploadPaletteIfDirty(settings.palette, paletteSize);
            material.SetTexture(PaletteLabTexId, paletteLabTexture);
            material.SetTexture(PaletteRgbTexId, paletteRgbTexture);
        }

        material.SetInt(PaletteSizeId, paletteSize);
        Shader.SetGlobalTexture(PaletteLabTexId, paletteLabTexture);
        Shader.SetGlobalTexture(PaletteRgbTexId, paletteRgbTexture);
        Shader.SetGlobalInt(PaletteSizeId, paletteSize);

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
            Shader.SetGlobalInt(PaletteSizeId, 0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        Shader.SetGlobalFloat("_PixelateEnabled", 0f);
        Shader.SetGlobalInt(PaletteSizeId, 0);
        Shader.SetGlobalTexture(PaletteLabTexId, null);
        Shader.SetGlobalTexture(PaletteRgbTexId, null);
        DestroyPaletteTextures();
        CoreUtils.Destroy(material);
    }

    private void UploadPaletteIfDirty(Color[] palette, int paletteSize)
    {
        int paletteHash = ComputePaletteHash(palette, paletteSize);
        if (paletteSize == lastUploadedPaletteSize &&
            paletteHash == lastPaletteHash &&
            paletteLabTexture != null &&
            paletteRgbTexture != null)
        {
            return;
        }

        EnsurePaletteTextures(paletteSize);

        var rgbPixels = new Color[paletteSize];
        var labPixels = new Color[paletteSize];

        for (int i = 0; i < paletteSize; i++)
        {
            Color rgb = palette[i];
            Vector3 lab = RGBToLAB(rgb);

            rgbPixels[i] = new Color(rgb.r, rgb.g, rgb.b, 1f);
            labPixels[i] = new Color(lab.x, lab.y, lab.z, 1f);
        }

        paletteRgbTexture.SetPixels(rgbPixels);
        paletteRgbTexture.Apply(false, false);
        paletteLabTexture.SetPixels(labPixels);
        paletteLabTexture.Apply(false, false);

        lastUploadedPaletteSize = paletteSize;
        lastPaletteHash = paletteHash;
    }

    private void EnsurePaletteTextures(int paletteSize)
    {
        if (paletteSize <= 0)
        {
            return;
        }

        if (paletteRgbTexture == null || paletteRgbTexture.width != paletteSize || paletteRgbTexture.height != PaletteTextureHeight)
        {
            CoreUtils.Destroy(paletteRgbTexture);
            paletteRgbTexture = CreatePaletteTexture(paletteSize, TextureFormat.RGBA32, "Pixelate Palette RGB");
        }

        if (paletteLabTexture == null || paletteLabTexture.width != paletteSize || paletteLabTexture.height != PaletteTextureHeight)
        {
            CoreUtils.Destroy(paletteLabTexture);
            paletteLabTexture = CreatePaletteTexture(paletteSize, TextureFormat.RGBAHalf, "Pixelate Palette LAB");
        }
    }

    private static Texture2D CreatePaletteTexture(int width, TextureFormat format, string textureName)
    {
        var texture = new Texture2D(width, PaletteTextureHeight, format, false, true);
        texture.name = textureName;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    private void DestroyPaletteTextures()
    {
        CoreUtils.Destroy(paletteRgbTexture);
        CoreUtils.Destroy(paletteLabTexture);
        paletteRgbTexture = null;
        paletteLabTexture = null;
        lastUploadedPaletteSize = -1;
        lastPaletteHash = int.MinValue;
    }

    private static int ComputePaletteHash(Color[] palette, int paletteSize)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < paletteSize; i++)
            {
                hash = (hash * 31) + palette[i].GetHashCode();
            }

            return hash;
        }
    }

    private static Vector3 RGBToLAB(Color rgb)
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

        x = x > 0.008856f ? Mathf.Pow(x, 1f / 3f) : (7.787f * x) + (16f / 116f);
        y = y > 0.008856f ? Mathf.Pow(y, 1f / 3f) : (7.787f * y) + (16f / 116f);
        z = z > 0.008856f ? Mathf.Pow(z, 1f / 3f) : (7.787f * z) + (16f / 116f);

        float l = (116f * y) - 16f;
        float a = 500f * (x - y);
        float bValue = 200f * (y - z);

        return new Vector3(l, a, bValue);
    }
}
