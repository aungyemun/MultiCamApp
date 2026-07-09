// VideoEngineV2 — offline runtime capability detector.
// Runs entirely from local system state — no internet access, no downloads.
// Safe to call at startup; all failures are caught and reported in the report.

using System.Management;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// Probes the current machine for all capabilities required by VideoEngineV2 and
/// the legacy OpenCV pipeline.  Returns a <see cref="RuntimeCapabilityReport"/>
/// that <see cref="MultiCamApp.Capture.VideoEngineV2.VideoEngineBackendSelector"/> uses
/// to pick the best available backend tier.
/// </summary>
public static class RuntimeCapabilityDetector
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs all probes asynchronously. Never throws — all errors are recorded in the report.
    /// </summary>
    /// <param name="outputFolderToTest">
    /// Path to test for write permission and free space.
    /// Defaults to <c>%USERPROFILE%\Videos</c>.
    /// </param>
    public static async Task<RuntimeCapabilityReport> DetectAsync(
        string? outputFolderToTest = null, CancellationToken ct = default)
    {
        var report = new RuntimeCapabilityReport
        {
            AppVersion = GetAppVersion(),
            DetectedAt = DateTimeOffset.UtcNow,
        };

        DetectSystem(report);
        DetectDirect3D(report);
        DetectGpus(report);
        await DetectCodecsAsync(report, ct);
        await DetectCamerasAsync(report, ct);
        DetectOpenCv(report);
        DetectRuntimeTools(report);
        DetectStorage(report, outputFolderToTest);
        BuildWarnings(report);

        return report;
    }

    // ── System ────────────────────────────────────────────────────────────────

    private static void DetectSystem(RuntimeCapabilityReport report)
    {
        var os = Environment.OSVersion;
        report.WindowsVersion       = os.VersionString;
        report.WindowsBuildNumber   = os.Version.Build;
        // Minimum supported: Windows 10 1903 (build 18362); TFM minimum is 17763
        report.IsWindowsBuildSupported = os.Version.Major >= 10 && os.Version.Build >= 17763;
        report.ProcessArchitecture  = RuntimeInformation.ProcessArchitecture.ToString();
        report.IsX64Process         = RuntimeInformation.ProcessArchitecture == Architecture.X64;
    }

    // ── Direct3D ──────────────────────────────────────────────────────────────

    private static void DetectDirect3D(RuntimeCapabilityReport report)
    {
        var d3dPath = Path.Combine(Environment.SystemDirectory, "d3d11.dll");
        report.Direct3D11DllPath    = d3dPath;
        report.Direct3D11Available  = File.Exists(d3dPath);

        // Windows 10 build 19041 (our TFM minimum) guarantees D3D11 Feature Level ≥ 11_0
        // on all non-WARP (hardware) adapters. Exact level requires device creation.
        report.EstimatedMinimumD3DFeatureLevel =
            report.IsWindowsBuildSupported
            ? "≥ D3D_FEATURE_LEVEL_11_0  (Windows 10 2004 minimum; exact level requires device probe)"
            : "Unknown (Windows version below supported minimum)";
    }

    // ── GPU / DXGI adapters (via WMI) ─────────────────────────────────────────

    private static void DetectGpus(RuntimeCapabilityReport report)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterCompatibility, DriverVersion FROM Win32_VideoController");
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                var name   = Convert.ToString(obj["Name"])?.Trim()                   ?? "Unknown";
                var vendor = Convert.ToString(obj["AdapterCompatibility"])?.Trim()   ?? "";
                var driver = Convert.ToString(obj["DriverVersion"])?.Trim()          ?? "";
                bool isBasic = name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);
                report.GpuAdapters.Add(new GpuAdapterInfo
                {
                    Name                     = name,
                    Vendor                   = vendor,
                    DriverVersion            = driver,
                    IsMicrosoftBasicRenderDriver = isBasic,
                });
            }

            report.HasMicrosoftBasicRenderDriver =
                report.GpuAdapters.Any(a => a.IsMicrosoftBasicRenderDriver);
            report.PrimaryGpuName = report.GpuAdapters
                .FirstOrDefault(a => !a.IsMicrosoftBasicRenderDriver)?.Name;
        }
        catch (Exception ex) when (IsWmiSafe(ex))
        {
            report.GpuDetectionError = $"WMI GPU query failed: {ex.GetType().Name}: {ex.Message}";
            report.Warnings.Add(report.GpuDetectionError);
        }
    }

    // ── Codecs (WinRT CodecQuery) ─────────────────────────────────────────────

    private static async Task DetectCodecsAsync(RuntimeCapabilityReport report, CancellationToken ct)
    {
        var mfPath = Path.Combine(Environment.SystemDirectory, "mfplat.dll");
        report.MediaFoundationDllPath = mfPath;
        report.MediaFoundationAvailable = File.Exists(mfPath);

        if (!report.MediaFoundationAvailable)
        {
            report.Warnings.Add("mfplat.dll not found. Media Foundation is unavailable.");
            return;
        }

        try
        {
            // CodecQuery requires an instance in the managed WinRT projection
            var query   = new CodecQuery();
            var rawList = await query.FindAllAsync(
                CodecKind.Video,
                CodecCategory.Encoder,
                MediaEncodingSubtypes.H264).AsTask(ct);
            var encoders = rawList.ToList();

            report.H264EncoderCount = encoders.Count;
            report.H264EncoderNames = encoders.Select(e => e.DisplayName).ToList();

            // Heuristic: "Microsoft H264 Video Encoder MFT" is the software encoder.
            // Any other H.264 encoder (NVIDIA, AMD, Intel QSV) is treated as hardware.
            report.H264SoftwareEncoderAvailable = encoders.Any(e =>
                e.DisplayName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
            report.H264HardwareEncoderAvailable = encoders.Any(e =>
                !e.DisplayName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));

            if (!report.H264SoftwareEncoderAvailable && encoders.Count > 0)
            {
                // If there are encoders but none recognised as Microsoft software, treat
                // the first as software-capable (MF may still do SW encoding internally).
                report.H264SoftwareEncoderAvailable = true;
            }
        }
        catch (Exception ex)
        {
            report.CodecDetectionError = $"CodecQuery failed: {ex.GetType().Name}: {ex.Message}";
            report.Warnings.Add(report.CodecDetectionError);
        }
    }

    // ── Camera devices ────────────────────────────────────────────────────────

    private static async Task DetectCamerasAsync(RuntimeCapabilityReport report, CancellationToken ct)
    {
        if (!report.MediaFoundationAvailable) return;

        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync().AsTask(ct);
            report.CameraDeviceCount  = groups.Count;
            report.CameraDeviceNames  = groups.Select(g => g.DisplayName).ToList();

            // Enumerate formats from the first group for diagnostics
            if (groups.Count > 0)
            {
                var firstGroup = groups[0];
                foreach (var src in firstGroup.SourceInfos)
                {
                    if (src.MediaStreamType != Windows.Media.Capture.MediaStreamType.VideoRecord &&
                        src.MediaStreamType != Windows.Media.Capture.MediaStreamType.VideoPreview)
                        continue;

                    var svc = src.VideoProfileMediaDescription;
                    if (svc is null) continue;
                    foreach (var desc in svc)
                    {
                        report.SupportedFormatSummaries.Add(
                            $"{desc.Width}×{desc.Height} @{desc.FrameRate:F0}fps  [{src.SourceKind}]");
                    }
                    break; // first video source only
                }
            }
        }
        catch (Exception ex)
        {
            report.CameraDetectionError = $"Camera enumeration failed: {ex.GetType().Name}: {ex.Message}";
            report.Warnings.Add(report.CameraDetectionError);
        }
    }

    // ── OpenCV native DLL ─────────────────────────────────────────────────────

    private static void DetectOpenCv(RuntimeCapabilityReport report)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dll    = Path.Combine(appDir, "OpenCvSharpExtern.dll");
        report.OpenCvNativeDllPath   = dll;
        report.OpenCvNativeDllExists = File.Exists(dll);

        if (!report.OpenCvNativeDllExists)
        {
            report.Warnings.Add($"OpenCvSharpExtern.dll not found at expected path: {dll}");
            return;
        }

        try
        {
            report.OpenCvNativeDllLoaded = NativeLibrary.TryLoad(dll, out _);
        }
        catch (Exception ex)
        {
            report.OpenCvDetectionError = $"OpenCV DLL load probe failed: {ex.GetType().Name}: {ex.Message}";
            report.Warnings.Add(report.OpenCvDetectionError);
        }
    }

    // ── Runtime tools ─────────────────────────────────────────────────────────

    private static void DetectRuntimeTools(RuntimeCapabilityReport report)
    {
        // VC++ 2015-2022 Redistributable (x64) — required by OpenCV and native WinRT interop
        var vcPath = Path.Combine(Environment.SystemDirectory, "vcruntime140.dll");
        report.VcRuntimeAvailable = File.Exists(vcPath);
        report.VcRuntimePath      = report.VcRuntimeAvailable ? vcPath : null;
        if (!report.VcRuntimeAvailable)
            report.Warnings.Add("vcruntime140.dll not found in System32. Install VC++ 2015-2022 x64 Redistributable.");

        // ffprobe — shipped in runtime\ffmpeg\
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var ffprobe = Path.Combine(appDir, "runtime", "ffmpeg", "ffprobe.exe");
        report.FfprobeAvailable = File.Exists(ffprobe);
        report.FfprobePath      = report.FfprobeAvailable ? ffprobe : null;
        if (!report.FfprobeAvailable)
            report.Warnings.Add($"ffprobe.exe not found at {ffprobe}. Video Verification will be unavailable.");
    }

    // ── Storage ───────────────────────────────────────────────────────────────

    private static void DetectStorage(RuntimeCapabilityReport report, string? outputFolderToTest)
    {
        var folder = outputFolderToTest
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        report.OutputFolderTested = folder;

        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".multicam_write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "write-probe");
            File.Delete(probe);
            report.OutputFolderWritable = true;
        }
        catch (Exception ex)
        {
            report.OutputFolderWritable  = false;
            report.StorageDetectionError = $"Write probe failed: {ex.GetType().Name}: {ex.Message}";
            report.Warnings.Add($"Output folder '{folder}' is not writable: {ex.Message}");
        }

        try
        {
            var root = Path.GetPathRoot(folder);
            if (root is not null && Directory.Exists(root))
            {
                var drive = new DriveInfo(root);
                report.OutputFolderDriveFreeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                if (report.OutputFolderDriveFreeSpaceGB < 5.0)
                    report.Warnings.Add(
                        $"Low disk space on output drive: {report.OutputFolderDriveFreeSpaceGB:F1} GB free. " +
                        "At least 5 GB recommended for uninterrupted recording.");
            }
        }
        catch (Exception ex)
        {
            report.StorageDetectionError ??= $"Disk space check failed: {ex.Message}";
        }
    }

    // ── Aggregate warnings ────────────────────────────────────────────────────

    private static void BuildWarnings(RuntimeCapabilityReport report)
    {
        if (!report.IsX64Process)
            report.Warnings.Add("Process is not x64. VideoEngineV2 requires x64.");

        if (!report.Direct3D11Available)
            report.Warnings.Add("d3d11.dll not found. Direct3D 11 preview path will be unavailable.");

        if (report.HasMicrosoftBasicRenderDriver)
            report.Warnings.Add(
                "Microsoft Basic Render Driver is active. Install official GPU drivers for hardware H.264 encoding and Direct3D 11 preview.");

        if (!report.MediaFoundationAvailable)
            report.Warnings.Add("Media Foundation unavailable. VideoEngineV2 will fall back to legacy OpenCV pipeline.");

        if (!report.H264HardwareEncoderAvailable && report.MediaFoundationAvailable)
            report.Warnings.Add("No hardware H.264 encoder detected. Software encoder will be used.");

        if (report.CameraDeviceCount == 0)
            report.Warnings.Add("No camera devices found via MediaFrameSourceGroup enumeration.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetAppVersion()
    {
        try
        {
            var cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "version.json");
            if (!File.Exists(cfgPath)) return "unknown";
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
        }
        catch { return "unknown"; }
    }

    private static bool IsWmiSafe(Exception ex) =>
        ex is ManagementException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or FileNotFoundException
            or TypeInitializationException;
}
