using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Diagnostics;
using MultiCamApp.Utils;

namespace MultiCamApp.Ui.Pages;

public partial class HardwareDiagnosticsPage : UserControl
{
    private Func<IReadOnlyList<CameraDevice>> _getAllCameras = () => [];
    private Func<IReadOnlyList<CameraDevice>> _getSelectedCameras = () => [];
    private Func<int> _getLayoutCount = () => 1;
    private VersionInfo _version = new();
    private string _lastSummary = "";
    private CancellationTokenSource? _scanCts;

    public HardwareDiagnosticsPage()
    {
        InitializeComponent();
    }

    public void Initialize(
        VersionInfo version,
        Func<IReadOnlyList<CameraDevice>> getAllCameras,
        Func<IReadOnlyList<CameraDevice>> getSelectedCameras,
        Func<int> getLayoutCount)
    {
        _version = version;
        _getAllCameras = getAllCameras;
        _getSelectedCameras = getSelectedCameras;
        _getLayoutCount = getLayoutCount;
    }

    private async void RunScanButton_Click(object sender, RoutedEventArgs e)
    {
        RunScanButton.IsEnabled = false;
        StatusText.Text = "Running hardware scan...";
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        try
        {
            var allCameras = _getAllCameras();
            var selectedCameras = _getSelectedCameras();
            var layoutCount = Math.Max(1, _getLayoutCount());

            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var system = new SystemCapabilityScanner().Scan(_version);
                var systemPaths = SystemProfileWriter.Write(system);
                token.ThrowIfCancellationRequested();
                var camera = new CameraCapabilityScanner().Scan(allCameras);
                var cameraPaths = CameraCapabilityScanner.Write(camera);
                token.ThrowIfCancellationRequested();
                var usb = new UsbTopologyScanner().Scan(selectedCameras);
                var usbPaths = UsbTopologyScanner.Write(usb);
                return (system, systemPaths, camera, cameraPaths, usb, usbPaths);
            }, token).ConfigureAwait(true);

            token.ThrowIfCancellationRequested();

            LastScanText.Text = $"Last scan: {result.system.ScanTimeLocal:yyyy-MM-dd HH:mm:ss}";
            RecommendedPresetText.Text = BuildPresetAdvisory(layoutCount, selectedCameras);
            SystemCardText.Text = BuildSystemCard(result.system);
            CameraDevicesCardText.Text = BuildCameraDevicesCard(result.camera);
            UsbStatusCardText.Text = BuildUsbStatusCard(result.usb, PrivacySanitizer.FileNameOnly(result.usbPaths.LatestPath));
            SystemSummaryBox.Text = BuildSystemSummary(result.system, PrivacySanitizer.FileNameOnly(result.systemPaths.LatestPath));
            CameraSummaryBox.Text = BuildCameraSummary(result.camera, PrivacySanitizer.FileNameOnly(result.cameraPaths.LatestPath));
            UsbSummaryBox.Text = BuildUsbSummary(result.usb, PrivacySanitizer.FileNameOnly(result.usbPaths.LatestPath));
            _lastSummary = BuildFullSummary(result.system, result.camera, result.usb, layoutCount, selectedCameras);
            StatusText.Text = "Hardware scan complete. Results are advisory only.";

            if (result.system.HasMicrosoftBasicDisplayAdapter)
            {
                MessageBox.Show(
                    "Microsoft Basic Display Adapter was detected. Install the official Intel, NVIDIA, or AMD graphics driver for best preview and recording reliability.",
                    "Hardware Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Hardware scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Hardware scan failed safely: {PrivacySanitizer.SanitizeForOutput(ex.Message)}";
        }
        finally
        {
            RunScanButton.IsEnabled = true;
        }
    }

    public void CancelActiveScan()
    {
        try { _scanCts?.Cancel(); } catch { }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = PathHelper.LogsFolder();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open logs folder: {PrivacySanitizer.SanitizeForOutput(ex.Message)}";
        }
    }

    private void CopySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        var text = string.IsNullOrWhiteSpace(_lastSummary)
            ? "No hardware scan has been run yet."
            : _lastSummary;
        Clipboard.SetText(text);
        StatusText.Text = "Diagnostic summary copied.";
    }

    private static string BuildSystemSummary(SystemProfile profile, string latestPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"App: {profile.AppVersion} build {profile.BuildNumber}");
        sb.AppendLine($"OS: {profile.OsVersion}");
        sb.AppendLine($"CPU: {profile.CpuName}");
        sb.AppendLine($"RAM: {FormatBytes(profile.TotalPhysicalMemoryBytes)}");
        sb.AppendLine("Display adapters:");
        foreach (var gpu in profile.DisplayAdapters)
            sb.AppendLine($"- {gpu.Name} | provider={gpu.DriverProvider} | version={gpu.DriverVersion} | date={gpu.DriverDate}");
        AppendList(sb, "Warnings", profile.Warnings);
        AppendList(sb, "Encoder hints", profile.EncoderHints);
        sb.AppendLine("Note: Hardware diagnostics are privacy-safe and do not store hardware IDs or user/computer identifiers.");
        sb.AppendLine($"Latest profile: {PrivacySanitizer.FileNameOnly(latestPath)}");
        return sb.ToString();
    }

    private static string BuildSystemCard(SystemProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CPU: {ShortValue(profile.CpuName)}");
        sb.AppendLine($"RAM: {FormatBytes(profile.TotalPhysicalMemoryBytes)}");
        sb.AppendLine($"OS: {ShortValue(profile.OsVersion)}");
        sb.AppendLine($"Graphics: {BuildGraphicsDisplay(profile)}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildGraphicsDisplay(SystemProfile profile)
    {
        if (profile.DisplayAdapters.Count == 0)
            return "not detected by diagnostics";

        var external = profile.DisplayAdapters
            .Select(a => PrivacySanitizer.SanitizeForOutput(a.Name))
            .FirstOrDefault(name =>
                !string.IsNullOrWhiteSpace(name)
                && (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(external))
            return ShortValue(external);

        if (profile.DisplayAdapters.Any(a =>
                a.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)))
        {
            return "built-in graphics";
        }

        return ShortValue(PrivacySanitizer.SanitizeForOutput(profile.DisplayAdapters[0].Name));
    }

    private static string BuildCameraSummary(CameraCapabilityReport report, string latestPath)
    {
        var sb = new StringBuilder();
        if (report.Cameras.Count == 0)
            sb.AppendLine("No cameras detected.");
        foreach (var camera in report.Cameras)
        {
            sb.AppendLine($"Camera: {PrivacySanitizer.SanitizeForOutput(camera.DisplayName)}");
            sb.AppendLine($"Type: {FormatCameraType(camera.CameraKind)}");
            sb.AppendLine($"Focus capability: {FormatFocusCapability(camera)}");
            sb.AppendLine("Preset support: not checked by diagnostics");
            AppendList(sb, "  Warnings", camera.Warnings);
            sb.AppendLine();
        }
        AppendList(sb, "Warnings", report.Warnings);
        sb.AppendLine("Camera capability reporting is advisory and depends on the camera driver.");
        sb.AppendLine("Note: Hardware diagnostics are privacy-safe and do not store hardware IDs or user/computer identifiers.");
        sb.AppendLine($"Latest report: {PrivacySanitizer.FileNameOnly(latestPath)}");
        return sb.ToString();
    }

    private static string FormatCameraType(string cameraKind) => cameraKind switch
    {
        "ExternalUsb" => "External USB",
        "BuiltIn" or "BuiltInOther" => "Built-in camera",
        "Virtual" => "Virtual camera",
        _ => "Camera"
    };

    private static string FormatFocusCapability(CameraCapabilityEntry camera)
    {
        if (string.Equals(camera.CameraKind, "Virtual", StringComparison.OrdinalIgnoreCase))
            return "controlled by provider app or driver";
        if (IsAffirmative(camera.AutoFocusSupported) || IsAffirmative(camera.ManualFocusSupported))
            return "reported by camera driver";
        return "not reported by camera driver";
    }

    private static bool IsAffirmative(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("available", StringComparison.OrdinalIgnoreCase)
        || value.Equals("supported", StringComparison.OrdinalIgnoreCase);

    private static string BuildCameraDevicesCard(CameraCapabilityReport report)
    {
        if (report.Cameras.Count == 0)
            return "No cameras detected.";

        var lines = new List<string> { $"{report.Cameras.Count} cameras detected" };
        lines.AddRange(report.Cameras
            .Take(6)
            .Select(c => $"- {ShortValue(PrivacySanitizer.SanitizeForOutput(c.DisplayName))}"));
        if (report.Cameras.Count > 6)
            lines.Add($"- +{report.Cameras.Count - 6} more");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildUsbSummary(UsbTopologyReport report, string latestPath)
    {
        return string.Join(Environment.NewLine,
        [
            "USB topology unavailable.",
            "",
            "Note: Hardware diagnostics are privacy-safe and do not store hardware IDs or user/computer identifiers.",
            $"Latest report: {PrivacySanitizer.FileNameOnly(latestPath)}"
        ]);
    }

    private static string BuildUsbStatusCard(UsbTopologyReport report, string latestPath)
    {
        return "USB topology unavailable.";
    }

    private static string BuildPresetAdvisory(int layoutCount, IReadOnlyList<CameraDevice> selectedCameras)
    {
        return "Recommended for 4-camera 1080p: use separate USB ports or powered USB hub, bright room lighting, preview FPS Balanced, and High-Stability Recording Mode ON.";
    }

    private static string BuildFullSummary(
        SystemProfile system,
        CameraCapabilityReport camera,
        UsbTopologyReport usb,
        int layoutCount,
        IReadOnlyList<CameraDevice> selectedCameras)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MultiCamApp Hardware Diagnostic Summary");
        sb.AppendLine(BuildPresetAdvisory(layoutCount, selectedCameras));
        sb.AppendLine();
        sb.AppendLine(BuildSystemSummary(system, "SystemProfile.latest.json"));
        sb.AppendLine(BuildCameraSummary(camera, "CameraCapability.latest.json"));
        sb.AppendLine(BuildUsbSummary(usb, "UsbTopology.latest.json"));
        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;
        sb.AppendLine($"{title}:");
        foreach (var value in values)
            sb.AppendLine($"- {PrivacySanitizer.SanitizeForOutput(value)}");
    }

    private static string FormatBytes(ulong? bytes)
    {
        if (bytes == null) return "Unknown";
        var gb = bytes.Value / 1024d / 1024d / 1024d;
        return $"{gb:F1} GB";
    }

    private static string ShortValue(string? value, int maxLength = 90)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 1)] + "…";
    }
}
