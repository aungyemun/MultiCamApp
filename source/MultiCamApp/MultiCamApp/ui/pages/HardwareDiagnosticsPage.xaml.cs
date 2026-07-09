using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Diagnostics;
using MultiCamApp.Localization;
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
    private LanguageManager? _lang;
    private DateTime? _lastScanTimeLocal;
    private bool _scanHasRun;
    // Cached raw scan results so ApplyLanguage can rebuild the result cards/summary boxes in the
    // newly-selected language after a scan has already run — without these, switching language only
    // updated titles/buttons/placeholders, leaving actual scan results frozen in whatever language
    // was active when Run Scan was clicked (the same "language switch doesn't refresh already-
    // computed content" gap fixed once before on the Video Verification page's DetailBox).
    private SystemProfile? _lastSystemProfile;
    private CameraCapabilityReport? _lastCameraReport;
    private UsbTopologyReport? _lastUsbReport;
    private int _lastLayoutCount = 1;
    private string _lastSystemLatestPath = "";
    private string _lastCameraLatestPath = "";
    private string _lastUsbLatestPath = "";

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

    /// <summary>Called on startup and whenever the user switches UI language.</summary>
    public void ApplyLanguage(LanguageManager lang)
    {
        _lang = lang;
        Diagnostics.DiagnosticsLocalization.Current = lang;
        var L = lang;
        PageTitleText.Text = L["hwDiagPageTitle"];
        PageDescriptionText.Text = L["hwDiagPageDescription"];
        RunScanButton.Content = L["hwDiagRunScanButton"];
        OpenLogsButton.Content = L["hwDiagOpenLogsButton"];
        CopySummaryButton.Content = L["hwDiagCopySummaryButton"];
        SummaryTitleText.Text = L["hwDiagSummaryTitle"];
        LastScanText.Text = _lastScanTimeLocal.HasValue
            ? string.Format(L["hwDiagLastScanTemplate"], _lastScanTimeLocal.Value.ToString("yyyy-MM-dd HH:mm:ss"))
            : L["hwDiagLastScanNotRun"];
        RecommendedPresetText.Text = BuildPresetAdvisory(Math.Max(1, _getLayoutCount()));
        StatusText.Text = L["hwDiagReady"];
        SystemCardTitleText.Text = L["hwDiagSystemCardTitle"];
        CameraDevicesCardTitleText.Text = L["hwDiagCameraDevicesCardTitle"];
        UsbStatusCardTitleText.Text = L["hwDiagUsbCardTitle"];
        SystemReportExpander.Header = L["hwDiagSystemReportHeader"];
        CameraReportExpander.Header = L["hwDiagCameraReportHeader"];
        UsbReportExpander.Header = L["hwDiagUsbReportHeader"];

        if (!_scanHasRun)
        {
            SystemCardText.Text = L["hwDiagSystemCardPlaceholder"];
            CameraDevicesCardText.Text = L["hwDiagCameraDevicesCardPlaceholder"];
            UsbStatusCardText.Text = L["hwDiagUsbCardPlaceholder"];
        }
        else if (_lastSystemProfile != null && _lastCameraReport != null && _lastUsbReport != null)
        {
            // Rebuild the previous scan's results in the newly-selected language instead of leaving
            // them frozen in whatever language was active when Run Scan was last clicked.
            RecommendedPresetText.Text = BuildPresetAdvisory(_lastLayoutCount);
            SystemCardText.Text = BuildSystemCard(_lastSystemProfile);
            CameraDevicesCardText.Text = BuildCameraDevicesCard(_lastCameraReport);
            UsbStatusCardText.Text = BuildUsbStatusCard(_lastUsbReport);
            SystemSummaryBox.Text = BuildSystemSummary(_lastSystemProfile, _lastSystemLatestPath);
            CameraSummaryBox.Text = BuildCameraSummary(_lastCameraReport, _lastCameraLatestPath);
            UsbSummaryBox.Text = BuildUsbSummary(_lastUsbReport, _lastUsbLatestPath);
            _lastSummary = BuildFullSummary(_lastSystemProfile, _lastCameraReport, _lastUsbReport, _lastLayoutCount);
        }
    }

    private async void RunScanButton_Click(object sender, RoutedEventArgs e)
    {
        RunScanButton.IsEnabled = false;
        StatusText.Text = _lang?["hwDiagRunningScan"] ?? "Running hardware scan...";
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

            _lastScanTimeLocal = result.system.ScanTimeLocal;
            _scanHasRun = true;
            _lastSystemProfile = result.system;
            _lastCameraReport = result.camera;
            _lastUsbReport = result.usb;
            _lastLayoutCount = layoutCount;
            _lastSystemLatestPath = PrivacySanitizer.FileNameOnly(result.systemPaths.LatestPath);
            _lastCameraLatestPath = PrivacySanitizer.FileNameOnly(result.cameraPaths.LatestPath);
            _lastUsbLatestPath = PrivacySanitizer.FileNameOnly(result.usbPaths.LatestPath);
            LastScanText.Text = string.Format(_lang?["hwDiagLastScanTemplate"] ?? "Last scan: {0}", result.system.ScanTimeLocal.ToString("yyyy-MM-dd HH:mm:ss"));
            RecommendedPresetText.Text = BuildPresetAdvisory(layoutCount);
            SystemCardText.Text = BuildSystemCard(result.system);
            CameraDevicesCardText.Text = BuildCameraDevicesCard(result.camera);
            UsbStatusCardText.Text = BuildUsbStatusCard(result.usb);
            SystemSummaryBox.Text = BuildSystemSummary(result.system, PrivacySanitizer.FileNameOnly(result.systemPaths.LatestPath));
            CameraSummaryBox.Text = BuildCameraSummary(result.camera, PrivacySanitizer.FileNameOnly(result.cameraPaths.LatestPath));
            UsbSummaryBox.Text = BuildUsbSummary(result.usb, PrivacySanitizer.FileNameOnly(result.usbPaths.LatestPath));
            _lastSummary = BuildFullSummary(result.system, result.camera, result.usb, layoutCount);
            StatusText.Text = _lang?["hwDiagScanComplete"] ?? "Hardware scan complete. Results are advisory only.";

            if (result.system.HasMicrosoftBasicDisplayAdapter)
            {
                MessageBox.Show(
                    _lang?["hwDiagBasicAdapterBody"] ?? "Microsoft Basic Display Adapter was detected. Install the official Intel, NVIDIA, or AMD graphics driver for best preview and recording reliability.",
                    _lang?["hwDiagBasicAdapterTitle"] ?? "Hardware Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = _lang?["hwDiagScanCancelled"] ?? "Hardware scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(_lang?["hwDiagScanFailed"] ?? "Hardware scan failed safely: {0}", PrivacySanitizer.SanitizeForOutput(ex.Message));
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
            StatusText.Text = string.Format(_lang?["hwDiagLogsFolderError"] ?? "Could not open logs folder: {0}", PrivacySanitizer.SanitizeForOutput(ex.Message));
        }
    }

    private void CopySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        var text = string.IsNullOrWhiteSpace(_lastSummary)
            ? (_lang?["hwDiagNoScanYet"] ?? "No hardware scan has been run yet.")
            : _lastSummary;
        Clipboard.SetText(text);
        StatusText.Text = _lang?["hwDiagSummaryCopied"] ?? "Diagnostic summary copied.";
    }

    private string T(string key, string fallback) => _lang?[key] is { Length: > 0 } v ? v : fallback;

    private string BuildSystemSummary(SystemProfile profile, string latestPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(T("hwDiagAppLine", "App: {0} build {1}"), profile.AppVersion, profile.BuildNumber));
        sb.AppendLine(string.Format(T("hwDiagOsLine", "OS: {0}"), profile.OsVersion));
        sb.AppendLine(string.Format(T("hwDiagCpuLine", "CPU: {0}"), profile.CpuName));
        sb.AppendLine(string.Format(T("hwDiagRamLine", "RAM: {0}"), FormatBytes(profile.TotalPhysicalMemoryBytes)));
        sb.AppendLine(T("hwDiagDisplayAdaptersLabel", "Display adapters:"));
        foreach (var gpu in profile.DisplayAdapters)
            sb.AppendLine($"- {gpu.Name} | provider={gpu.DriverProvider} | version={gpu.DriverVersion} | date={gpu.DriverDate}");
        AppendList(sb, T("hwDiagWarningsLabel", "Warnings"), profile.Warnings);
        AppendList(sb, T("hwDiagEncoderHintsLabel", "Encoder hints"), profile.EncoderHints);
        sb.AppendLine(T("hwDiagPrivacyNote", "Note: Hardware diagnostics are privacy-safe and do not store hardware IDs or user/computer identifiers."));
        sb.AppendLine(string.Format(T("hwDiagLatestProfileLine", "Latest profile: {0}"), PrivacySanitizer.FileNameOnly(latestPath)));
        return sb.ToString();
    }

    private string BuildSystemCard(SystemProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(T("hwDiagCpuLine", "CPU: {0}"), ShortValue(profile.CpuName)));
        sb.AppendLine(string.Format(T("hwDiagRamLine", "RAM: {0}"), FormatBytes(profile.TotalPhysicalMemoryBytes)));
        sb.AppendLine(string.Format(T("hwDiagOsLine", "OS: {0}"), ShortValue(profile.OsVersion)));
        sb.AppendLine(string.Format(T("hwDiagGraphicsLine", "Graphics: {0}"), BuildGraphicsDisplay(profile)));
        return sb.ToString().TrimEnd();
    }

    private string BuildGraphicsDisplay(SystemProfile profile)
    {
        if (profile.DisplayAdapters.Count == 0)
            return T("hwDiagGraphicsNotDetected", "not detected by diagnostics");

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
            return T("hwDiagBuiltInGraphics", "built-in graphics");
        }

        return ShortValue(PrivacySanitizer.SanitizeForOutput(profile.DisplayAdapters[0].Name));
    }

    private string BuildCameraSummary(CameraCapabilityReport report, string latestPath)
    {
        var sb = new StringBuilder();
        if (report.Cameras.Count == 0)
            sb.AppendLine(T("hwDiagNoCamerasDetected", "No cameras detected."));
        foreach (var camera in report.Cameras)
        {
            sb.AppendLine(string.Format(T("hwDiagCameraLine", "Camera: {0}"), PrivacySanitizer.SanitizeForOutput(camera.DisplayName)));
            sb.AppendLine(string.Format(T("hwDiagCameraTypeLine", "Type: {0}"), FormatCameraType(camera.CameraKind)));
            sb.AppendLine(string.Format(T("hwDiagFocusCapabilityLine", "Focus capability: {0}"), FormatFocusCapability(camera)));
            sb.AppendLine(T("hwDiagPresetNotChecked", "Preset support: not checked by diagnostics"));
            AppendList(sb, $"  {T("hwDiagWarningsLabel", "Warnings")}", camera.Warnings);
            sb.AppendLine();
        }
        AppendList(sb, T("hwDiagWarningsLabel", "Warnings"), report.Warnings);
        foreach (var note in report.Notes)
            sb.AppendLine(note);
        sb.AppendLine(string.Format(T("hwDiagLatestReportLine", "Latest report: {0}"), PrivacySanitizer.FileNameOnly(latestPath)));
        return sb.ToString();
    }

    private string FormatCameraType(string cameraKind) => cameraKind switch
    {
        "ExternalUsb" => T("hwDiagCameraTypeExternalUsb", "External USB"),
        "BuiltIn" or "BuiltInOther" => T("hwDiagCameraTypeBuiltIn", "Built-in camera"),
        "Virtual" => T("hwDiagCameraTypeVirtual", "Virtual camera"),
        _ => T("hwDiagCameraTypeGeneric", "Camera")
    };

    private string FormatFocusCapability(CameraCapabilityEntry camera)
    {
        if (string.Equals(camera.CameraKind, "Virtual", StringComparison.OrdinalIgnoreCase))
            return T("hwDiagFocusControlledByProvider", "controlled by provider app or driver");
        if (IsAffirmative(camera.AutoFocusSupported) || IsAffirmative(camera.ManualFocusSupported))
            return T("hwDiagFocusReportedByDriver", "reported by camera driver");
        return T("hwDiagFocusNotReportedByDriver", "not reported by camera driver");
    }

    private static bool IsAffirmative(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("available", StringComparison.OrdinalIgnoreCase)
        || value.Equals("supported", StringComparison.OrdinalIgnoreCase);

    private string BuildCameraDevicesCard(CameraCapabilityReport report)
    {
        if (report.Cameras.Count == 0)
            return T("hwDiagNoCamerasDetected", "No cameras detected.");

        var lines = new List<string> { string.Format(T("hwDiagCamerasDetectedCount", "{0} cameras detected"), report.Cameras.Count) };
        lines.AddRange(report.Cameras
            .Take(6)
            .Select(c => $"- {ShortValue(PrivacySanitizer.SanitizeForOutput(c.DisplayName))}"));
        if (report.Cameras.Count > 6)
            lines.Add(string.Format(T("hwDiagMoreCamerasLine", "- +{0} more"), report.Cameras.Count - 6));
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildUsbSummary(UsbTopologyReport report, string latestPath)
    {
        var sb = new StringBuilder();
        if (report.SelectedCameras.Count == 0)
        {
            sb.AppendLine(T("hwDiagUsbNoCamerasSelected", "No cameras selected to check."));
        }
        else
        {
            foreach (var cam in report.SelectedCameras)
            {
                sb.AppendLine(string.Format(T("hwDiagCameraLine", "Camera: {0}"), cam.DisplayName));
                sb.AppendLine(string.Format(T("hwDiagCameraTypeLine", "Type: {0}"), FormatCameraType(cam.CameraKind)));
                sb.AppendLine(string.Format(T("hwDiagUsbControllerLine", "USB controller/hub: {0}"),
                    string.Equals(cam.UsbControllerOrHub, "Unknown", StringComparison.OrdinalIgnoreCase)
                        ? T("hwDiagNotDeterminedByDiagnostics", "not determined by diagnostics")
                        : cam.UsbControllerOrHub));
                sb.AppendLine();
            }
        }
        AppendList(sb, T("hwDiagWarningsLabel", "Warnings"), report.Warnings);
        foreach (var note in report.Notes)
            sb.AppendLine(note);
        sb.AppendLine(string.Format(T("hwDiagLatestReportLine", "Latest report: {0}"), PrivacySanitizer.FileNameOnly(latestPath)));
        return sb.ToString();
    }

    private string BuildUsbStatusCard(UsbTopologyReport report)
    {
        if (report.SelectedCameras.Count == 0)
            return T("hwDiagUsbNoCamerasSelected", "No cameras selected to check.");
        var lines = new List<string>
        {
            string.Format(T("hwDiagUsbCamerasCheckedCount", "{0} selected camera(s) checked"), report.SelectedCameras.Count)
        };
        if (report.Notes.Count > 0)
            lines.Add(report.Notes[0]);
        return string.Join(Environment.NewLine, lines);
    }

    // Advisory text tiered by the user's actual selected layout — previously this always
    // recommended "4-camera 1080p" settings even for a 1-camera layout, which was misleading.
    private string BuildPresetAdvisory(int layoutCount) => layoutCount switch
    {
        1 => T("hwDiagPresetAdvisory1Cam", "1-camera layout: USB bandwidth is rarely a concern. Bright, even room lighting still helps focus and exposure stability."),
        2 => T("hwDiagPresetAdvisory2Cam", "2-camera layout: prefer separate USB ports for each camera if available. Bright room lighting and preview FPS Balanced are recommended."),
        _ => T("hwDiagPresetAdvisory", "Recommended for 3-4 camera 1080p: use separate USB ports or powered USB hub, bright room lighting, preview FPS Balanced, and High-Stability Recording Mode ON.")
    };

    private string BuildFullSummary(SystemProfile system, CameraCapabilityReport camera, UsbTopologyReport usb, int layoutCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine(T("hwDiagFullSummaryTitle", "MultiCamApp Hardware Diagnostic Summary"));
        sb.AppendLine(BuildPresetAdvisory(layoutCount));
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

    private string FormatBytes(ulong? bytes)
    {
        if (bytes == null) return T("hwDiagUnknown", "Unknown");
        var gb = bytes.Value / 1024d / 1024d / 1024d;
        return $"{gb:F1} GB";
    }

    private string ShortValue(string? value, int maxLength = 90)
    {
        if (string.IsNullOrWhiteSpace(value))
            return T("hwDiagUnknown", "Unknown");
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 1)] + "…";
    }
}
