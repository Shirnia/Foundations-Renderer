using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PixelateBenchmarkRunner : MonoBehaviour
{
    private const string BenchmarkSceneName = "PixelateBenchmark";
    private static readonly FrameTiming[] TimingBuffer = new FrameTiming[1];

    [Serializable]
    public struct BenchmarkCase
    {
        public string label;
        public bool effectActive;
        [Min(0)]
        public int paletteCount;

        public BenchmarkCase(string label, bool effectActive, int paletteCount)
        {
            this.label = label;
            this.effectActive = effectActive;
            this.paletteCount = paletteCount;
        }
    }

    private enum CameraPhaseMode
    {
        Static,
        Orbit
    }

    private sealed class SampleAccumulator
    {
        public int frameCount;
        public int cpuSampleCount;
        public int gpuSampleCount;

        private double fpsTotal;
        private double fpsMin = double.MaxValue;
        private double fpsMax = double.MinValue;

        private double cpuTotalMs;
        private double cpuMinMs = double.MaxValue;
        private double cpuMaxMs = double.MinValue;

        private double gpuTotalMs;
        private double gpuMinMs = double.MaxValue;
        private double gpuMaxMs = double.MinValue;

        public void AddFrame(float deltaSeconds, float cpuMs, float gpuMs)
        {
            frameCount++;

            double fps = deltaSeconds > 0f ? 1d / deltaSeconds : 0d;
            fpsTotal += fps;
            fpsMin = Math.Min(fpsMin, fps);
            fpsMax = Math.Max(fpsMax, fps);

            if (cpuMs > 0f)
            {
                cpuSampleCount++;
                cpuTotalMs += cpuMs;
                cpuMinMs = Math.Min(cpuMinMs, cpuMs);
                cpuMaxMs = Math.Max(cpuMaxMs, cpuMs);
            }

            if (gpuMs > 0f)
            {
                gpuSampleCount++;
                gpuTotalMs += gpuMs;
                gpuMinMs = Math.Min(gpuMinMs, gpuMs);
                gpuMaxMs = Math.Max(gpuMaxMs, gpuMs);
            }
        }

        public double AverageFps => frameCount > 0 ? fpsTotal / frameCount : 0d;
        public double MinFps => frameCount > 0 ? fpsMin : 0d;
        public double MaxFps => frameCount > 0 ? fpsMax : 0d;

        public double AverageCpuMs => cpuSampleCount > 0 ? cpuTotalMs / cpuSampleCount : 0d;
        public double MinCpuMs => cpuSampleCount > 0 ? cpuMinMs : 0d;
        public double MaxCpuMs => cpuSampleCount > 0 ? cpuMaxMs : 0d;

        public double AverageGpuMs => gpuSampleCount > 0 ? gpuTotalMs / gpuSampleCount : 0d;
        public double MinGpuMs => gpuSampleCount > 0 ? gpuMinMs : 0d;
        public double MaxGpuMs => gpuSampleCount > 0 ? gpuMaxMs : 0d;
    }

    private struct BenchmarkResult
    {
        public string caseLabel;
        public bool effectActive;
        public int paletteCount;
        public int screenHeight;
        public string cameraMode;
        public int sampleFrames;
        public int cpuSamples;
        public int gpuSamples;
        public double avgFps;
        public double minFps;
        public double maxFps;
        public double avgCpuMs;
        public double minCpuMs;
        public double maxCpuMs;
        public double avgGpuMs;
        public double minGpuMs;
        public double maxGpuMs;
        public bool gpuUnavailable;
    }

    [Header("Benchmark Cases")]
    [SerializeField]
    private List<BenchmarkCase> benchmarkCases = new List<BenchmarkCase>
    {
        new BenchmarkCase("OFF", false, 0),
        new BenchmarkCase("ON_5", true, 5),
        new BenchmarkCase("ON_16", true, 16),
        new BenchmarkCase("ON_128", true, 128),
        new BenchmarkCase("ON_2048", true, 2048)
    };

    [Header("Timing")]
    [SerializeField, Min(0.1f)]
    private float warmupSeconds = 2f;
    [SerializeField, Min(0.1f)]
    private float sampleSeconds = 8f;
    [SerializeField, Min(1)]
    private int featureWaitTimeoutSeconds = 10;

    [Header("Render Settings")]
    [SerializeField, Range(64, 1080)]
    private int fixedScreenHeight = 1080;

    [Header("Grid Workload")]
    [SerializeField, Min(1)]
    private int gridColumns = 20;
    [SerializeField, Min(1)]
    private int gridRows = 12;
    [SerializeField, Min(0.1f)]
    private float gridSpacing = 2.1f;
    [SerializeField, Min(0f)]
    private float verticalJitter = 0.2f;
    [SerializeField, Min(0f)]
    private float scaleJitter = 0.15f;
    [SerializeField]
    private int transformSeed = 1337;

    [Header("Camera")]
    [SerializeField]
    private Vector3 staticCameraOffset = new Vector3(0f, 14f, -30f);
    [SerializeField, Min(0.1f)]
    private float orbitRadius = 30f;
    [SerializeField]
    private float orbitHeight = 14f;
    [SerializeField]
    private float orbitDegreesPerSecond = 18f;
    [SerializeField]
    private float orbitStartAngleDegrees = -90f;
    [SerializeField]
    private float lookAtHeightOffset = 1f;

    [Header("Output")]
    [SerializeField]
    private string outputPrefix = "pixelate_benchmark";
    [SerializeField]
    private bool autoQuitOnComplete = true;

    private Camera benchmarkCamera;
    private Transform gridRoot;
    private Vector3 gridCenter;
    private int previousVSyncCount;
    private int previousTargetFrameRate;
    private bool benchmarkStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapInBenchmarkScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!string.Equals(scene.name, BenchmarkSceneName, StringComparison.Ordinal))
        {
            return;
        }

        if (FindAnyObjectByType<PixelateBenchmarkRunner>() != null)
        {
            return;
        }

        var go = new GameObject(nameof(PixelateBenchmarkRunner));
        go.AddComponent<PixelateBenchmarkRunner>();
    }

    private void Start()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, BenchmarkSceneName, StringComparison.Ordinal))
        {
            return;
        }

        if (benchmarkStarted)
        {
            return;
        }

        benchmarkStarted = true;
        StartCoroutine(RunBenchmark());
    }

    private IEnumerator RunBenchmark()
    {
        if (!TryResolveCamera())
        {
            Debug.LogError("[PixelateBenchmark] No camera found in scene. Aborting benchmark.");
            yield break;
        }

        BuildDeterministicGrid();
        DisablePixelateControllers();

        yield return WaitForPixelateFeature();
        if (PixelateFeature.Instance == null)
        {
            Debug.LogError("[PixelateBenchmark] PixelateFeature.Instance was not found before timeout.");
            yield break;
        }

        previousVSyncCount = QualitySettings.vSyncCount;
        previousTargetFrameRate = Application.targetFrameRate;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        var results = new List<BenchmarkResult>(benchmarkCases.Count * 2);

        for (int i = 0; i < benchmarkCases.Count; i++)
        {
            BenchmarkCase benchmarkCase = benchmarkCases[i];
            ApplyCase(benchmarkCase);
            yield return RunCasePhase(benchmarkCase, CameraPhaseMode.Static, results);
            yield return RunCasePhase(benchmarkCase, CameraPhaseMode.Orbit, results);
        }

        string csvPath = WriteCsv(results);
        LogSummary(results, csvPath);

        QualitySettings.vSyncCount = previousVSyncCount;
        Application.targetFrameRate = previousTargetFrameRate;

        if (autoQuitOnComplete)
        {
            QuitApplication();
        }
    }

    private IEnumerator WaitForPixelateFeature()
    {
        float elapsed = 0f;
        while (PixelateFeature.Instance == null && elapsed < featureWaitTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator RunCasePhase(BenchmarkCase benchmarkCase, CameraPhaseMode phaseMode, List<BenchmarkResult> results)
    {
        yield return SampleWindow(phaseMode, warmupSeconds, collectSamples: false, null);

        var accumulator = new SampleAccumulator();
        yield return SampleWindow(phaseMode, sampleSeconds, collectSamples: true, accumulator);

        var result = new BenchmarkResult
        {
            caseLabel = benchmarkCase.label,
            effectActive = benchmarkCase.effectActive,
            paletteCount = benchmarkCase.paletteCount,
            screenHeight = fixedScreenHeight,
            cameraMode = phaseMode.ToString(),
            sampleFrames = accumulator.frameCount,
            cpuSamples = accumulator.cpuSampleCount,
            gpuSamples = accumulator.gpuSampleCount,
            avgFps = accumulator.AverageFps,
            minFps = accumulator.MinFps,
            maxFps = accumulator.MaxFps,
            avgCpuMs = accumulator.AverageCpuMs,
            minCpuMs = accumulator.MinCpuMs,
            maxCpuMs = accumulator.MaxCpuMs,
            avgGpuMs = accumulator.AverageGpuMs,
            minGpuMs = accumulator.MinGpuMs,
            maxGpuMs = accumulator.MaxGpuMs,
            gpuUnavailable = accumulator.gpuSampleCount == 0
        };

        results.Add(result);
    }

    private IEnumerator SampleWindow(CameraPhaseMode phaseMode, float durationSeconds, bool collectSamples, SampleAccumulator accumulator)
    {
        float elapsed = 0f;
        while (elapsed < durationSeconds)
        {
            UpdateCamera(phaseMode, elapsed);
            FrameTimingManager.CaptureFrameTimings();

            yield return null;

            float delta = Time.unscaledDeltaTime;
            elapsed += delta;

            if (!collectSamples || accumulator == null)
            {
                continue;
            }

            GetLatestFrameTimes(out float cpuMs, out float gpuMs);
            accumulator.AddFrame(delta, cpuMs, gpuMs);
        }
    }

    private void ApplyCase(BenchmarkCase benchmarkCase)
    {
        PixelateFeature feature = PixelateFeature.Instance;
        feature.settings.screenHeight = fixedScreenHeight;

        if (benchmarkCase.effectActive)
        {
            int paletteCount = Mathf.Clamp(
                benchmarkCase.paletteCount,
                PixelateFeature.PixelateSettings.MinPaletteColorCount,
                PixelateFeature.PixelateSettings.MaxRuntimePaletteColorCount);

            feature.settings.defaultPaletteColorCount = paletteCount;
            feature.settings.EnsurePaletteHasUnityDefaults();
            feature.SetActive(true);
        }
        else
        {
            feature.SetActive(false);
        }
    }

    private void GetLatestFrameTimes(out float cpuMs, out float gpuMs)
    {
        cpuMs = 0f;
        gpuMs = 0f;

        uint count = FrameTimingManager.GetLatestTimings(1, TimingBuffer);
        if (count == 0)
        {
            return;
        }

        cpuMs = (float)TimingBuffer[0].cpuFrameTime;
        gpuMs = (float)TimingBuffer[0].gpuFrameTime;
    }

    private bool TryResolveCamera()
    {
        benchmarkCamera = Camera.main;
        if (benchmarkCamera != null)
        {
            return true;
        }

        benchmarkCamera = FindAnyObjectByType<Camera>();
        return benchmarkCamera != null;
    }

    private void DisablePixelateControllers()
    {
        PixelateController[] controllers = FindObjectsByType<PixelateController>(FindObjectsInactive.Include);
        for (int i = 0; i < controllers.Length; i++)
        {
            controllers[i].enabled = false;
        }
    }

    private void BuildDeterministicGrid()
    {
        if (gridRoot != null)
        {
            return;
        }

        gridRoot = new GameObject("BenchmarkGrid").transform;
        var random = new System.Random(transformSeed);

        float width = (gridColumns - 1) * gridSpacing;
        float depth = (gridRows - 1) * gridSpacing;
        float startX = -width * 0.5f;
        float startZ = -depth * 0.5f;

        gridCenter = new Vector3(0f, 0f, 0f);

        for (int row = 0; row < gridRows; row++)
        {
            for (int column = 0; column < gridColumns; column++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"BenchmarkSphere_{row}_{column}";
                sphere.transform.SetParent(gridRoot, false);

                Vector3 position = new Vector3(
                    startX + (column * gridSpacing),
                    RandomRange(random, -verticalJitter, verticalJitter),
                    startZ + (row * gridSpacing));

                float uniformScale = 1f + RandomRange(random, -scaleJitter, scaleJitter);
                sphere.transform.localPosition = position;
                sphere.transform.localRotation = Quaternion.Euler(
                    RandomRange(random, -8f, 8f),
                    RandomRange(random, 0f, 360f),
                    RandomRange(random, -8f, 8f));
                sphere.transform.localScale = Vector3.one * Mathf.Max(0.2f, uniformScale);

                Collider collider = sphere.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
            }
        }
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        double t = random.NextDouble();
        return (float)(min + ((max - min) * t));
    }

    private void UpdateCamera(CameraPhaseMode phaseMode, float elapsedSeconds)
    {
        if (benchmarkCamera == null)
        {
            return;
        }

        Vector3 lookTarget = gridCenter + (Vector3.up * lookAtHeightOffset);
        if (phaseMode == CameraPhaseMode.Static)
        {
            benchmarkCamera.transform.position = gridCenter + staticCameraOffset;
            benchmarkCamera.transform.rotation = Quaternion.LookRotation(lookTarget - benchmarkCamera.transform.position, Vector3.up);
            return;
        }

        float angle = (orbitStartAngleDegrees + (elapsedSeconds * orbitDegreesPerSecond)) * Mathf.Deg2Rad;
        Vector3 orbitOffset = new Vector3(Mathf.Cos(angle) * orbitRadius, orbitHeight, Mathf.Sin(angle) * orbitRadius);
        benchmarkCamera.transform.position = gridCenter + orbitOffset;
        benchmarkCamera.transform.rotation = Quaternion.LookRotation(lookTarget - benchmarkCamera.transform.position, Vector3.up);
    }

    private string WriteCsv(List<BenchmarkResult> results)
    {
        string timestampUtc = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{outputPrefix}_{timestampUtc}.csv";
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);

        var builder = new StringBuilder(4096);
        builder.AppendLine(
            "caseLabel,effectActive,paletteCount,screenHeight,cameraMode,sampleFrames,cpuSamples,gpuSamples,gpuUnavailable," +
            "avgFps,minFps,maxFps,avgCpuMs,minCpuMs,maxCpuMs,avgGpuMs,minGpuMs,maxGpuMs");

        for (int i = 0; i < results.Count; i++)
        {
            BenchmarkResult r = results[i];
            builder
                .Append(EscapeCsv(r.caseLabel)).Append(',')
                .Append(r.effectActive ? "1" : "0").Append(',')
                .Append(r.paletteCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.screenHeight.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.cameraMode).Append(',')
                .Append(r.sampleFrames.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.cpuSamples.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.gpuSamples.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(r.gpuUnavailable ? "1" : "0").Append(',')
                .Append(FormatDouble(r.avgFps)).Append(',')
                .Append(FormatDouble(r.minFps)).Append(',')
                .Append(FormatDouble(r.maxFps)).Append(',')
                .Append(FormatDouble(r.avgCpuMs)).Append(',')
                .Append(FormatDouble(r.minCpuMs)).Append(',')
                .Append(FormatDouble(r.maxCpuMs)).Append(',')
                .Append(FormatDouble(r.avgGpuMs)).Append(',')
                .Append(FormatDouble(r.minGpuMs)).Append(',')
                .Append(FormatDouble(r.maxGpuMs)).AppendLine();
        }

        File.WriteAllText(fullPath, builder.ToString(), Encoding.UTF8);
        return fullPath;
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void LogSummary(List<BenchmarkResult> results, string csvPath)
    {
        Debug.Log($"[PixelateBenchmark] Completed {results.Count} phases. CSV: {csvPath}");

        for (int i = 0; i < results.Count; i++)
        {
            BenchmarkResult r = results[i];
            string gpuText = r.gpuUnavailable ? "GPU N/A" : $"{FormatDouble(r.avgGpuMs)} ms GPU avg";
            Debug.Log(
                $"[PixelateBenchmark] {r.caseLabel} {r.cameraMode}: " +
                $"{FormatDouble(r.avgFps)} FPS avg, {FormatDouble(r.avgCpuMs)} ms CPU avg, {gpuText}");
        }
    }

    private void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
