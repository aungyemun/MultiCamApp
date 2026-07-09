using System.Text.Json;
using MultiCamApp.Capture;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

public sealed class UsbTopologyScanner
{
    public UsbTopologyReport Scan(IReadOnlyList<CameraDevice> selectedCameras)
    {
        var report = new UsbTopologyReport
        {
            ScanTimeLocal = DateTime.Now,
            Status = "Unknown"
        };

        foreach (var camera in selectedCameras.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
        {
            report.SelectedCameras.Add(new UsbCameraTopologyEntry
            {
                DisplayName = PrivacySanitizer.SanitizeForOutput(camera.DisplayName),
                DeviceId = PrivacySanitizer.Redacted,
                CameraKind = camera.Kind.ToString(),
                UsbControllerOrHub = "Unknown"
            });
        }

        if (report.SelectedCameras.Count >= 3)
        {
            report.Notes.Add(DiagnosticsLocalization.T("hwDiagUsbUnavailable", "USB topology unavailable."));
        }
        else
        {
            report.Notes.Add(DiagnosticsLocalization.T("hwDiagUsbAdvisoryFor34Cam", "USB topology advisory is most useful for 3/4-camera layouts."));
        }

        report.Notes.Add(DiagnosticsLocalization.T("hwDiagUsbAdvisoryOnlyNote", "USB topology diagnostics are advisory only and do not change selected devices, opening order, or recording behavior."));
        report.Notes.Add(DiagnosticsLocalization.T("hwDiagPrivacyNote", "Hardware diagnostics are privacy-safe and do not store hardware IDs or user/computer identifiers."));
        return report;
    }

    public static (string TimestampedPath, string LatestPath) Write(UsbTopologyReport report)
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        var stamp = report.ScanTimeLocal == default ? DateTime.Now : report.ScanTimeLocal;
        var timestamped = Path.Combine(dir, $"usb_topology_{stamp:yyyyMMdd_HHmmss}.json");
        var latest = Path.Combine(dir, "UsbTopology.latest.json");
        var json = PrivacySanitizer.SanitizeForOutput(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(timestamped, json);
        File.WriteAllText(latest, json);
        return (timestamped, latest);
    }
}
