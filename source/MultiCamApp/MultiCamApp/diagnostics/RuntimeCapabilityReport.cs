// VideoEngineV2 — offline runtime capability snapshot.
// Populated by RuntimeCapabilityDetector; consumed by VideoEngineBackendSelector.
// No internet access required or performed.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// Full snapshot of what the current machine can support at runtime.
/// Serialisable to JSON and plaintext for diagnostics output.
/// </summary>
public sealed class RuntimeCapabilityReport
{
    // ── Detection metadata ────────────────────────────────────────────────────

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; set; } = "";

    // ── Windows / process ─────────────────────────────────────────────────────

    public string WindowsVersion { get; set; } = "";
    public int WindowsBuildNumber { get; set; }
    public bool IsWindowsBuildSupported { get; set; }
    public string ProcessArchitecture { get; set; } = "";
    public bool IsX64Process { get; set; }

    // ── Direct3D / DXGI ───────────────────────────────────────────────────────

    public bool Direct3D11Available { get; set; }
    public string Direct3D11DllPath { get; set; } = "";
    /// <summary>
    /// Minimum guaranteed by OS version (Win10 2004+ TFM = "≥ D3D_FEATURE_LEVEL_11_0").
    /// Exact level requires device creation; not performed here.
    /// </summary>
    public string EstimatedMinimumD3DFeatureLevel { get; set; } = "";
    public List<GpuAdapterInfo> GpuAdapters { get; set; } = [];
    public bool HasMicrosoftBasicRenderDriver { get; set; }
    public string? PrimaryGpuName { get; set; }
    public string? GpuDetectionError { get; set; }

    // ── Media Foundation ──────────────────────────────────────────────────────

    public bool MediaFoundationAvailable { get; set; }
    public string MediaFoundationDllPath { get; set; } = "";
    public int H264EncoderCount { get; set; }
    public bool H264HardwareEncoderAvailable { get; set; }
    public bool H264SoftwareEncoderAvailable { get; set; }
    public List<string> H264EncoderNames { get; set; } = [];
    public string? CodecDetectionError { get; set; }

    // ── Cameras ───────────────────────────────────────────────────────────────

    public int CameraDeviceCount { get; set; }
    public List<string> CameraDeviceNames { get; set; } = [];
    /// <summary>Brief format descriptions for diagnostics (e.g. "1920×1080 MJPEG @30fps").</summary>
    public List<string> SupportedFormatSummaries { get; set; } = [];
    public string? CameraDetectionError { get; set; }

    // ── OpenCV ────────────────────────────────────────────────────────────────

    public string? OpenCvNativeDllPath { get; set; }
    public bool OpenCvNativeDllExists { get; set; }
    public bool OpenCvNativeDllLoaded { get; set; }
    public string? OpenCvDetectionError { get; set; }

    // ── Runtime tools ─────────────────────────────────────────────────────────

    public bool VcRuntimeAvailable { get; set; }
    public string? VcRuntimePath { get; set; }
    public bool FfprobeAvailable { get; set; }
    public string? FfprobePath { get; set; }

    // ── Storage ───────────────────────────────────────────────────────────────

    public string? OutputFolderTested { get; set; }
    public bool OutputFolderWritable { get; set; }
    public double OutputFolderDriveFreeSpaceGB { get; set; }
    public string? StorageDetectionError { get; set; }

    // ── Aggregated issues ─────────────────────────────────────────────────────

    /// <summary>Human-readable warnings collected during detection.</summary>
    public List<string> Warnings { get; set; } = [];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented  = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, _jsonOptions);

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MultiCamApp Runtime Capability Report  —  {DetectedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"App version : {AppVersion}");
        sb.AppendLine();
        sb.AppendLine("[System]");
        sb.AppendLine($"  Windows     : {WindowsVersion}  (build {WindowsBuildNumber}, supported={IsWindowsBuildSupported})");
        sb.AppendLine($"  Architecture: {ProcessArchitecture}  (x64={IsX64Process})");
        sb.AppendLine();
        sb.AppendLine("[Direct3D / DXGI]");
        sb.AppendLine($"  D3D11 DLL   : {Direct3D11Available}  ({Direct3D11DllPath})");
        sb.AppendLine($"  Feature level: {EstimatedMinimumD3DFeatureLevel}");
        sb.AppendLine($"  GPU adapters : {GpuAdapters.Count}");
        foreach (var a in GpuAdapters)
            sb.AppendLine($"    - {a.Name}  [{a.Vendor}]  driver {a.DriverVersion}  basic={a.IsMicrosoftBasicRenderDriver}");
        sb.AppendLine($"  Basic render driver: {HasMicrosoftBasicRenderDriver}");
        if (GpuDetectionError is not null) sb.AppendLine($"  GPU detection error: {GpuDetectionError}");
        sb.AppendLine();
        sb.AppendLine("[Media Foundation]");
        sb.AppendLine($"  MF available  : {MediaFoundationAvailable}  ({MediaFoundationDllPath})");
        sb.AppendLine($"  H.264 encoders: {H264EncoderCount}  (HW={H264HardwareEncoderAvailable}, SW={H264SoftwareEncoderAvailable})");
        foreach (var n in H264EncoderNames)
            sb.AppendLine($"    - {n}");
        if (CodecDetectionError is not null) sb.AppendLine($"  Codec detection error: {CodecDetectionError}");
        sb.AppendLine();
        sb.AppendLine("[Cameras]");
        sb.AppendLine($"  Devices: {CameraDeviceCount}");
        foreach (var n in CameraDeviceNames)
            sb.AppendLine($"    - {n}");
        if (SupportedFormatSummaries.Count > 0)
        {
            sb.AppendLine($"  Formats (first camera): {SupportedFormatSummaries.Count}");
            foreach (var f in SupportedFormatSummaries.Take(10))
                sb.AppendLine($"    {f}");
            if (SupportedFormatSummaries.Count > 10)
                sb.AppendLine($"    ... ({SupportedFormatSummaries.Count - 10} more)");
        }
        if (CameraDetectionError is not null) sb.AppendLine($"  Camera detection error: {CameraDetectionError}");
        sb.AppendLine();
        sb.AppendLine("[OpenCV]");
        sb.AppendLine($"  DLL path   : {OpenCvNativeDllPath}");
        sb.AppendLine($"  DLL exists : {OpenCvNativeDllExists}");
        sb.AppendLine($"  DLL loaded : {OpenCvNativeDllLoaded}");
        if (OpenCvDetectionError is not null) sb.AppendLine($"  OpenCV error: {OpenCvDetectionError}");
        sb.AppendLine();
        sb.AppendLine("[Runtime tools]");
        sb.AppendLine($"  VC++ runtime: {VcRuntimeAvailable}  ({VcRuntimePath})");
        sb.AppendLine($"  ffprobe     : {FfprobeAvailable}  ({FfprobePath})");
        sb.AppendLine();
        sb.AppendLine("[Storage]");
        sb.AppendLine($"  Output folder  : {OutputFolderTested}");
        sb.AppendLine($"  Writable       : {OutputFolderWritable}");
        sb.AppendLine($"  Drive free (GB): {OutputFolderDriveFreeSpaceGB:F1}");
        if (StorageDetectionError is not null) sb.AppendLine($"  Storage error: {StorageDetectionError}");
        if (Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Warnings]");
            foreach (var w in Warnings) sb.AppendLine($"  ! {w}");
        }
        return sb.ToString();
    }

    public async Task SaveAsync(string folder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var jsonPath = Path.Combine(folder, "runtime_capability_report.json");
        var txtPath  = Path.Combine(folder, "runtime_capability_report.txt");
        await File.WriteAllTextAsync(jsonPath, ToJson(), System.Text.Encoding.UTF8, ct);
        await File.WriteAllTextAsync(txtPath,  ToText(), System.Text.Encoding.UTF8, ct);
    }
}

/// <summary>Information about one DXGI / WMI display adapter.</summary>
public sealed class GpuAdapterInfo
{
    public string Name { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public bool IsMicrosoftBasicRenderDriver { get; init; }
}
