using System.Text.Json;
using MultiCamApp.Capture;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

public sealed class CameraCapabilityScanner
{
    private static readonly (int Width, int Height)[] StandardPresets =
    [
        (640, 480),
        (1280, 720),
        (1920, 1080)
    ];

    public CameraCapabilityReport Scan(IReadOnlyList<CameraDevice> cameras)
    {
        var report = new CameraCapabilityReport
        {
            ScanTimeLocal = DateTime.Now
        };

        report.Notes.Add(DiagnosticsLocalization.T("hwDiagNoteCameraAdvisoryOnly", "Camera capability diagnostics are advisory only and do not block recording."));
        report.Notes.Add(DiagnosticsLocalization.T("hwDiagNoteNoStressTest", "This scanner does not run a long stress test and avoids opening cameras by default."));
        report.Notes.Add(DiagnosticsLocalization.T("hwDiagNoteFocusUnavailable", "Focus control support is reported as unavailable unless the camera/driver exposes it through generic controls without disturbing active camera use."));
        report.Notes.Add(DiagnosticsLocalization.T("hwDiagPrivacyNote", "Hardware diagnostics are privacy-safe and do not store hardware IDs or user/computer identifiers."));

        foreach (var camera in cameras.OrderBy(c => c.EnumerationIndex))
        {
            var entry = new CameraCapabilityEntry
            {
                DisplayName = PrivacySanitizer.SanitizeForOutput(camera.DisplayName),
                DeviceId = PrivacySanitizer.Redacted,
                CameraKind = camera.Kind.ToString()
            };

            foreach (var (width, height) in StandardPresets)
            {
                entry.Presets.Add(new CameraPresetCapability
                {
                    Width = width,
                    Height = height,
                    RequestedFps = 30,
                    BackendUsed = "NotProbed",
                    Result = "Unknown",
                    Warning = DiagnosticsLocalization.T("hwDiagWarnPresetNotProbed", "Not probed to avoid locking cameras or disturbing driver mappings. Unknown does not mean unsupported.")
                });
            }

            if (camera.Kind == CameraKind.Virtual)
                entry.Warnings.Add(DiagnosticsLocalization.T("hwDiagWarnVirtualCamera", "Virtual camera detected. Availability depends on its provider app and whether another app has locked it."));
            if (camera.IsBuiltIn)
                entry.Warnings.Add(DiagnosticsLocalization.T("hwDiagWarnBuiltInCamera", "Built-in camera detected. Use 360p first; use 720p only if stable. Do not use built-in fallback for 1080p stress."));

            report.Cameras.Add(entry);
        }

        if (report.Cameras.Count == 0)
            report.Warnings.Add(DiagnosticsLocalization.T("hwDiagWarnNoCamerasToScan", "No cameras were available to scan."));

        return report;
    }

    public static (string TimestampedPath, string LatestPath) Write(CameraCapabilityReport report)
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        var stamp = report.ScanTimeLocal == default ? DateTime.Now : report.ScanTimeLocal;
        var timestamped = Path.Combine(dir, $"camera_capability_{stamp:yyyyMMdd_HHmmss}.json");
        var latest = Path.Combine(dir, "CameraCapability.latest.json");
        var json = PrivacySanitizer.SanitizeForOutput(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(timestamped, json);
        File.WriteAllText(latest, json);
        return (timestamped, latest);
    }
}
