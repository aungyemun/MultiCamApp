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

        // 2.1.0: the controller/hub of each camera is now actually resolved from the Windows device
        // tree (UsbTopologyResolver). Previously every camera stayed "Unknown" and 3–4 camera
        // layouts — the case this check exists for — only got "USB topology unavailable".
        var resolved = new List<(string Camera, UsbTopologyResolver.UsbPath? Path)>();
        foreach (var camera in selectedCameras.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
        {
            var path = UsbTopologyResolver.Resolve(camera.Id);
            var name = PrivacySanitizer.SanitizeForOutput(camera.DisplayName);
            resolved.Add((name, path));
            report.SelectedCameras.Add(new UsbCameraTopologyEntry
            {
                DisplayName = name,
                DeviceId = PrivacySanitizer.Redacted,
                CameraKind = camera.Kind.ToString(),
                UsbControllerOrHub = path is null ? "Unknown" : PrivacySanitizer.SanitizeForOutput(path.Controller),
                UsbHubs = path is null ? [] : path.Hubs.Select(PrivacySanitizer.SanitizeForOutput).ToList()
            });
        }

        if (resolved.Any(r => r.Path is not null))
        {
            report.Status = "OK";
            var (notes, warnings) = UsbTopologyResolver.Summarize(resolved, DiagnosticsLocalization.T);
            report.Notes.AddRange(notes);
            report.Warnings.AddRange(warnings);
            if (warnings.Count > 0) report.Status = "Warning";
        }
        else if (report.SelectedCameras.Count >= 3)
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
