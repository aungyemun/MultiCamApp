using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MultiCamApp.Core;
using MultiCamApp.Localization;
using MultiCamApp.Utils;
using MultiCamApp.Verification;

namespace MultiCamApp.Ui.Pages;

public partial class VideoVerificationPage : UserControl
{
    public sealed class SessionGroupViewModel
    {
        public SessionGroupViewModel(RecordingSessionAuditResult audit, IEnumerable<VerificationTableRowViewModel> rows)
        {
            Audit = audit;
            foreach (var row in rows)
                CameraRows.Add(row);
        }

        public RecordingSessionAuditResult Audit { get; }
        public ObservableCollection<VerificationTableRowViewModel> CameraRows { get; } = [];
        public bool IsExpanded { get; set; } = true;
        public string HeaderText =>
            $"{Audit.SessionLabel} — {Audit.SessionStatus} ({string.Join(", ", Audit.CamerasFound)})";
        public string ComparisonSummary => Audit.ComparisonSummaryText;
        public string SimpleComparisonSummary => BuildFriendlySessionSummary(Audit);

        public static string BuildFriendlySessionSummary(RecordingSessionAuditResult audit)
        {
            var cameraCount = audit.CamerasFound.Count > 0
                ? audit.CamerasFound.Count
                : audit.CameraVideos.Select(v => v.Entry.CameraSlot).Distinct().Count();
            var mode = DisplayTimingMode(audit.SessionTimingMode);
            var realFramesOnly = string.Equals(audit.SessionTimingMode, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase)
                && audit.TotalDuplicates == 0
                && audit.TotalPlaceholders == 0;
            var timingSource = audit.CameraVideos.Any(v => v.Metadata?.FrameTimestampCsvWritten == true)
                ? "Timestamp CSV"
                : "-";

            return string.Join(Environment.NewLine, new[]
            {
                $"Session: {audit.SessionLabel}",
                $"Result: {NormalizeSessionResult(audit.SessionStatus)}",
                $"Cameras: {cameraCount}",
                $"Recording mode: {mode}",
                $"Frame integrity: {(realFramesOnly ? "Real frames only; no duplicates/placeholders" : "review frame integrity details")}",
                $"Writer drops: {audit.TotalQueueDrops}",
                $"Timing source: {timingSource}",
                $"Note: {FriendlySessionNote(audit)}"
            });
        }

        private static string NormalizeSessionResult(string status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized switch
            {
                "PASS" or "PASS_ORIGINAL_TIMING" or "PASS_ORIGINAL_TIMING_WITH_NOTE" => "PASS",
                "PASS_WITH_WARNING" or "WARNING" => "WARNING",
                "FAIL" => "FAIL",
                _ => string.IsNullOrWhiteSpace(status) ? "-" : status
            };
        }

        private static string FriendlySessionNote(RecordingSessionAuditResult audit)
        {
            if (audit.FrameCountDifferenceAcceptedBecauseOriginalMode)
                return OriginalCaptureVerificationPolicy.FrameCountDifferenceNote;
            if (audit.Failures.Count > 0)
                return "verification failed; inspect details.";
            if (audit.Warnings.Count > 0)
                return "warnings present; inspect details.";
            if (audit.InterpretationNotes.Count > 0)
                return audit.InterpretationNotes[0].TrimEnd('.') + ".";
            return "ready for analysis.";
        }
    }

    public sealed class VerificationTableRowViewModel
    {
        public VerificationTableRowViewModel(VerificationTableRow row)
        {
            Row = row;
            ResultBrush = VerificationUiBrushes.ForStatus(row.AuditStatus, row.Result);
            ResolutionMatchBrush = VerificationUiBrushes.ForMatch(row.ResolutionMatch);
            FpsMatchBrush = VerificationUiBrushes.ForMatch(row.FpsMatch);
            DurationMatchBrush = VerificationUiBrushes.ForMatch(row.DurationMatch);
        }

        public VerificationTableRow Row { get; }
        public Brush ResultBrush { get; }
        public Brush ResolutionMatchBrush { get; }
        public Brush FpsMatchBrush { get; }
        public Brush DurationMatchBrush { get; }

        public string ResultDisplay => Row.ResultDisplay;
        public string Camera => Row.Camera;
        public string Device => Row.Device;
        public string FileName => Row.FileName;
        public string MetadataStatus => Row.MetadataStatus;
        public string ExpectedResolution => Row.ExpectedResolution;
        public string ActualResolution => Row.ActualResolution;
        public string ResolutionMatchDisplay => Row.ResolutionMatchDisplay;
        public string ExpectedFps => Row.ExpectedFps;
        public string ActualFps => Row.ActualFps;
        public string FpsMatchDisplay => Row.FpsMatchDisplay;
        public string ExpectedDuration => Row.ExpectedDuration;
        public string ActualDuration => Row.ActualDuration;
        public string DurationMatchDisplay => Row.DurationMatchDisplay;
        public string FrameCount => Row.FrameCount;
        public string Codec => Row.Codec;
        public string Container => Row.Container;
        public string FileSize => Row.FileSize;
        public string Details => Row.Details;
        public string RequestedFps => Row.RequestedFps;
        public string WriterFps => Row.WriterFps;
        public string ContainerFps => Row.ContainerFps;
        public string MeasuredNativeFps => Row.MeasuredNativeFps;
        public string FpsStabilityGrade => Row.FpsStabilityGrade;
        public string FramesCaptured => Row.FramesCapturedDisplay;
        public string FramesWritten => Row.FramesWrittenDisplay;
        public string TimestampRows => Row.TimestampRowsDisplay;
        public string TimingSource => ShortTimingSource(Row.TimingSourceDisplay);
        public string MeasuredCameraFps => Row.MeasuredCameraFpsDisplay;
        public string WallDuration => Row.WallDurationDisplay;
        public string ContainerDuration => Row.ContainerDurationDisplay;
        public string ContainerVsWallClock => Row.ContainerVsWallClockDisplay;
        public string StartOffset => Row.StartOffsetDisplay;
        public string QueueDrops => Row.QueueDropsDisplay;
        public string CaptureIntervalMeanMinMaxStd => Row.CaptureIntervalMeanMinMaxStdDisplay;
        public string CaptureIntervalP95P99 => Row.CaptureIntervalP95P99Display;
        public string CaptureGapCounts => Row.CaptureGapCountsDisplay;
        public string Duplicates => Row.DuplicatesDisplay;
        public string Placeholders => Row.PlaceholdersDisplay;
        public string AuditStatus => Row.Result == VerificationVerdict.NotChecked
            ? "Ready for verification"
            : Row.AuditStatus;
        public string RecommendedAction => Row.Result == VerificationVerdict.NotChecked
            ? "Click Verify All"
            : Row.RecommendedAction;
        public string SimpleRecommendedAction => Row.Result == VerificationVerdict.NotChecked
            ? "Click Verify All."
            : ToSimpleRecommendedAction(AuditStatus);

        private static string ToSimpleRecommendedAction(string status)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized switch
            {
                "PASS" or "PASS_ORIGINAL_TIMING" => "Ready for analysis",
                "PASS_ORIGINAL_TIMING_WITH_NOTE" => "Ready; use timestamp CSV",
                "PASS_WITH_WARNING" or "WARNING" => "Use timestamp CSV",
                "FAIL" => "Do not use; inspect details",
                _ => "Review verification details."
            };
        }

        private static string ShortTimingSource(string value) =>
            string.Equals(value, "PerFrameCaptureTimestamps", StringComparison.OrdinalIgnoreCase)
                ? "Timestamp CSV"
                : value;
    }

    private readonly ObservableCollection<SessionGroupViewModel> _sessionGroups = [];
    private readonly ObservableCollection<SessionGroupViewModel> _filteredSessionGroups = [];
    private VideoVerificationService? _service;
    private VerificationReport? _lastReport;
    private CancellationTokenSource? _cts;
    private AppConfig _config = new();
    private LanguageManager _language = new();
    private bool _simpleTableView = true;
    private VerificationTableRowViewModel? _selectedTableRow;

    public VideoVerificationPage()
    {
        InitializeComponent();
        SessionResultsList.ItemsSource = _filteredSessionGroups;
        DetailBox.Text = "";
    }

    private void RebuildSessionItemTemplate()
    {
        if (SessionResultsList == null)
            return;

        var expanderStyle = (Style)FindResource("VerifySessionExpander");
        var summaryStyle = (Style)FindResource("VerifySessionSummaryText");
        var gridStyle = (Style)FindResource("VerifySessionDataGrid");

        var factory = new FrameworkElementFactory(typeof(Expander));
        factory.SetValue(FrameworkElement.StyleProperty, expanderStyle);
        factory.SetBinding(Expander.IsExpandedProperty, new Binding(nameof(SessionGroupViewModel.IsExpanded))
        {
            Mode = BindingMode.TwoWay
        });
        factory.SetValue(Expander.HeaderProperty, new Binding(nameof(SessionGroupViewModel.HeaderText)));
        factory.SetValue(Expander.MarginProperty, new Thickness(0, 0, 0, 8));

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var summary = new FrameworkElementFactory(typeof(TextBlock));
        summary.SetValue(FrameworkElement.StyleProperty, summaryStyle);
        summary.SetBinding(
            TextBlock.TextProperty,
            new Binding(_simpleTableView
                ? nameof(SessionGroupViewModel.SimpleComparisonSummary)
                : nameof(SessionGroupViewModel.ComparisonSummary)));
        summary.SetValue(TextBlock.MarginProperty, new Thickness(8, 4, 8, 8));
        stack.AppendChild(summary);

        var grid = new FrameworkElementFactory(typeof(DataGrid));
        grid.SetValue(FrameworkElement.StyleProperty, gridStyle);
        grid.SetBinding(DataGrid.ItemsSourceProperty, new Binding(nameof(SessionGroupViewModel.CameraRows)));
        grid.SetValue(DataGrid.AutoGenerateColumnsProperty, false);
        grid.SetValue(DataGrid.IsReadOnlyProperty, true);
        grid.SetValue(DataGrid.HeadersVisibilityProperty, DataGridHeadersVisibility.Column);
        grid.SetValue(DataGrid.CanUserAddRowsProperty, false);
        grid.SetValue(DataGrid.RowHeightProperty, 30.0);
        grid.SetValue(DataGrid.SelectionModeProperty, DataGridSelectionMode.Single);
        grid.SetValue(DataGrid.SelectionUnitProperty, DataGridSelectionUnit.FullRow);
        grid.SetValue(DataGrid.MinColumnWidthProperty, 88.0);
        grid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        grid.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(SessionCameraGrid_Loaded));
        grid.AddHandler(DataGrid.SelectionChangedEvent, new SelectionChangedEventHandler(SessionCameraGrid_SelectionChanged));
        stack.AppendChild(grid);
        factory.AppendChild(stack);
        SessionResultsList.ItemTemplate = new DataTemplate { VisualTree = factory };
    }

    private void SessionCameraGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.Columns.Count > 0)
            return;
        PopulateCameraGridColumns(grid, _language);
    }

    private void PopulateCameraGridColumns(DataGrid grid, LanguageManager L)
    {
        var cellTextStyle = (Style)FindResource("VerifySessionGridCellText");

        DataTemplate CreateTextCellTemplate(string bindingPath, string? foregroundPath = null)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(FrameworkElement.StyleProperty, cellTextStyle);
            text.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            if (foregroundPath != null)
                text.SetBinding(TextBlock.ForegroundProperty, new Binding(foregroundPath));
            return new DataTemplate { VisualTree = text };
        }

        static TextBlock Header(string text, string? tooltip = null) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = string.IsNullOrWhiteSpace(tooltip) ? text : tooltip,
            MinWidth = 84
        };

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Header("Status", "Verification status. PASS and PASS_ORIGINAL_TIMING are ready; warnings remain visible."),
            Width = new DataGridLength(120),
            CellTemplate = CreateTextCellTemplate(
                nameof(VerificationTableRowViewModel.AuditStatus),
                nameof(VerificationTableRowViewModel.ResultBrush))
        });

        void AddText(string header, string binding, double width, string? tooltip = null)
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = Header(header, tooltip),
                Width = new DataGridLength(width),
                MinWidth = Math.Min(width, 96),
                CellTemplate = CreateTextCellTemplate(binding)
            });
        }

        if (_simpleTableView)
        {
            AddText("Camera", nameof(VerificationTableRowViewModel.Camera), 78);
            AddText("Device", nameof(VerificationTableRowViewModel.Device), 180);
            AddText("Real Capture FPS", nameof(VerificationTableRowViewModel.MeasuredNativeFps), 115, "Measured native camera FPS from capture timestamps.");
            AddText("Playback FPS", nameof(VerificationTableRowViewModel.ContainerFps), 95, "MP4/container playback FPS tag.");
            AddText("FPS Stability", nameof(VerificationTableRowViewModel.FpsStabilityGrade), 105);
            AddText("Frames Written", nameof(VerificationTableRowViewModel.FramesWritten), 100);
            AddText("Writer Drops", nameof(VerificationTableRowViewModel.QueueDrops), 92, "Writer drops.");
            AddText("Timestamp CSV", nameof(VerificationTableRowViewModel.TimestampRows), 105, "Per-frame timestamp CSV row count/status.");
            AddText("Timing Source", nameof(VerificationTableRowViewModel.TimingSource), 155, "Recommended source for timing and trimming.");
            AddText("Recommended Action", nameof(VerificationTableRowViewModel.SimpleRecommendedAction), 310);
            return;
        }

        AddText("Camera", nameof(VerificationTableRowViewModel.Camera), 78);
        AddText("Device", nameof(VerificationTableRowViewModel.Device), 180);
        AddText("Req FPS", nameof(VerificationTableRowViewModel.RequestedFps), 90, "Requested FPS");
        AddText("Writer FPS", nameof(VerificationTableRowViewModel.WriterFps), 90);
        AddText("Playback FPS", nameof(VerificationTableRowViewModel.ContainerFps), 95, "MP4/container playback FPS tag");
        AddText("Real Capture FPS", nameof(VerificationTableRowViewModel.MeasuredNativeFps), 120, "Measured native camera FPS");
        AddText("FPS Stability", nameof(VerificationTableRowViewModel.FpsStabilityGrade), 105, "FPS Stability Grade");
        AddText("Captured", nameof(VerificationTableRowViewModel.FramesCaptured), 95, "Frames Captured");
        AddText("Written", nameof(VerificationTableRowViewModel.FramesWritten), 95, "Frames Written");
        AddText("Timestamp CSV", nameof(VerificationTableRowViewModel.TimestampRows), 105, "Timestamp CSV rows/status");
        AddText("Wall Duration", nameof(VerificationTableRowViewModel.WallDuration), 105, "Wall-clock Duration");
        AddText("MP4 Duration", nameof(VerificationTableRowViewModel.ContainerDuration), 105, "Container Duration");
        AddText("MP4-Wall Diff", nameof(VerificationTableRowViewModel.ContainerVsWallClock), 110, "Container vs Wall-clock Difference");
        AddText("Start Offset", nameof(VerificationTableRowViewModel.StartOffset), 95);
        AddText("Writer Drops", nameof(VerificationTableRowViewModel.QueueDrops), 92, "Writer drops");
        AddText("Dup", nameof(VerificationTableRowViewModel.Duplicates), 74, "Duplicate frames from metadata");
        AddText("Placeholders", nameof(VerificationTableRowViewModel.Placeholders), 100, "Placeholder frames from metadata");
        AddText("Int mean/min/max/std", nameof(VerificationTableRowViewModel.CaptureIntervalMeanMinMaxStd), 165, "Capture interval mean/min/max/std");
        AddText("P95/P99", nameof(VerificationTableRowViewModel.CaptureIntervalP95P99), 110, "Capture interval P95/P99");
        AddText("Long/Short/Severe", nameof(VerificationTableRowViewModel.CaptureGapCounts), 135, "Long/short/severe gap counts");
        AddText("Timing Source", nameof(VerificationTableRowViewModel.TimingSource), 155, "Recommended timing source");
        AddText("Recommended Action", nameof(VerificationTableRowViewModel.RecommendedAction), 320);
    }

    private void SessionCameraGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is VerificationTableRowViewModel vm)
        {
            _selectedTableRow = vm;
            DetailBox.Text = BuildSelectedVideoDetail(vm);
        }
    }

    private void TableViewRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (SimpleViewRadio == null || SessionResultsList == null)
            return;

        _simpleTableView = SimpleViewRadio?.IsChecked != false;
        RebuildSessionItemTemplate();
        if (_selectedTableRow != null)
            DetailBox.Text = BuildSelectedVideoDetail(_selectedTableRow);
    }

    private string BuildSelectedVideoDetail(VerificationTableRowViewModel vm)
    {
        if (!_simpleTableView)
            return string.IsNullOrWhiteSpace(vm.Row.DetailText)
                ? _language["verifyDetailEmpty"]
                : vm.Row.DetailText;

        var row = vm.Row;
        var sb = new System.Text.StringBuilder();
        AppendSection(sb, "Result",
            ("Status", vm.AuditStatus),
            ("Verification result", vm.ResultDisplay),
            ("Recommended action", vm.SimpleRecommendedAction));
        AppendMessages(sb, "Warnings", row.WarningMessages);
        AppendMessages(sb, "Failures", row.ErrorMessages);

        AppendSection(sb, "File",
            ("Camera", vm.Camera),
            ("Device", vm.Device),
            ("File", row.FileName),
            ("Metadata", row.MetadataStatus));

        AppendSection(sb, "Recording & FPS",
            ("Requested FPS", vm.RequestedFps),
            ("Playback FPS", vm.ContainerFps),
            ("Real Capture FPS", vm.MeasuredNativeFps),
            ("FPS stability", vm.FpsStabilityGrade),
            ("Scientific timing source", vm.TimingSource));

        AppendSection(sb, "Timing",
            ("Wall-clock duration", vm.WallDuration),
            ("Container duration", vm.ContainerDuration),
            ("Container vs wall-clock difference", vm.ContainerVsWallClock),
            ("Start offset", vm.StartOffset));

        AppendSection(sb, "Frame Integrity",
            ("Frames captured", vm.FramesCaptured),
            ("Frames written", vm.FramesWritten),
            ("Timestamp CSV", vm.TimestampRows),
            ("Writer drops", vm.QueueDrops),
            ("Duplicate frames", vm.Duplicates),
            ("Placeholder frames", vm.Placeholders));

        AppendSection(sb, "Capture Quality",
            ("Capture interval mean/min/max/std", vm.CaptureIntervalMeanMinMaxStd),
            ("Capture interval P95/P99", vm.CaptureIntervalP95P99),
            ("Long/short/severe gap counts", vm.CaptureGapCounts));

        AppendSection(sb, "Scientific Recommendation",
            ("Scientific timing status", row.ScientificTimingStatusDisplay),
            ("Metadata completeness", row.MetadataCompletenessPercent),
            ("Recommended action", vm.SimpleRecommendedAction));

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(
        System.Text.StringBuilder sb,
        string title,
        params (string Label, string? Value)[] rows)
    {
        if (sb.Length > 0)
            sb.AppendLine();
        sb.AppendLine(title);
        foreach (var (label, value) in rows)
            sb.AppendLine($"  {label}: {DisplayValue(value)}");
    }

    private static void AppendMessages(System.Text.StringBuilder sb, string title, IReadOnlyList<string> messages)
    {
        if (messages.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine(title);
        foreach (var message in messages)
            sb.AppendLine($"  - {message}");
    }

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string DisplayTimingMode(string? value) =>
        string.Equals(value, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase)
            ? "Original Capture Mode"
            : string.IsNullOrWhiteSpace(value) ? "-" : value;

    private string _verificationProfile = "Standard";

    public void Initialize(AppConfig config, LanguageManager language)
    {
        _config = config;
        _language = language;
        _service = new VideoVerificationService(config.Verification);
        PopulateVerificationProfiles();
        SelectVerificationProfile("Standard");
        if (string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            var defaultFolder = PathHelper.DefaultVideosFolder();
            if (Directory.Exists(defaultFolder))
                FolderBox.Text = defaultFolder;
        }

        ApplyLanguage();
        UpdateFfprobeWarning();
    }

    private void PopulateVerificationProfiles()
    {
        VerificationProfileBox.Items.Clear();
        foreach (var name in new[] { "Standard" })
            VerificationProfileBox.Items.Add(new ComboBoxItem
            {
                Content = _language["verifyProfileStandard"],
                Tag = name
            });
    }

    private void SelectVerificationProfile(string profile)
    {
        _verificationProfile = profile;
        for (var i = 0; i < VerificationProfileBox.Items.Count; i++)
        {
            if (VerificationProfileBox.Items[i] is ComboBoxItem ci
                && string.Equals((string?)ci.Tag, profile, StringComparison.OrdinalIgnoreCase))
            {
                VerificationProfileBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void VerificationProfileBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (VerificationProfileBox.SelectedItem is ComboBoxItem ci && ci.Tag is string p)
            _verificationProfile = p;
    }

    public void ApplyLanguage()
    {
        var L = _language;
        PageTitle.Text = L["verifyPageTitle"];
        ProfileLabel.Text = L["verifyProfileLabel"];
        PageSubtitle.Text = L["verifyPageSubtitle"];
        BrowseFolderBtn.Content = L["browse"];
        ScanBtn.Content = L["verifyScan"];
        VerifyBtn.Content = L["verifyRun"];
        VerifyAllBtn.Content = L["verifyAll"];
        ClearBtn.Content = L["verifyClear"];
        AutoScanCheck.Content = L["verifyAutoScan"];
        SearchBox.Tag = L["verifySearchPlaceholder"];
        ExportTxtBtn.Content = L["verifyExportTxt"];
        ExportJsonBtn.Content = L["verifyExportJson"];
        ExportCsvBtn.Content = L["verifyExportCsv"];
        ExportAllBtn.Content = L["verifyExportAll"];
        CancelBtn.Content = L["verifyCancel"];
        CardOverallLabel.Text = L["verifyOverall"];
        CardFoundLabel.Text = L["verifyTotalFound"];
        CardPassLabel.Text = L["verifyPassed"];
        CardWarnLabel.Text = L["verifyWarnings"];
        CardFailLabel.Text = L["verifyFailed"];
        CardSourceLabel.Text = L["verifySettingsSource"];
        CardDurSpreadLabel.Text = L["verifySessionDuration"];
        CardFpsSpreadLabel.Text = L["verifyFpsSpread"];
        DetailTitle.Text = L["verifyDetailTitle"];
        LogTitle.Text = L["verifyLogTitle"];
        ScopeNoteText.Text = L["verifySessionScopeNote"];
        RebuildSessionItemTemplate();
        PopulateVerificationProfiles();
        SelectVerificationProfile(_verificationProfile);
        UpdateFfprobeWarning();
        if (_lastReport == null)
            RefreshSummaryEmpty();
    }

    private void UpdateFfprobeWarning()
    {
        if (_service == null) return;
        FfprobeWarning.Visibility = _service.IsFfprobeAvailable ? Visibility.Collapsed : Visibility.Visible;
        FfprobeWarning.Text = _language["verifyFfprobeMissing"];
    }

    private void BrowseFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            FolderBox.Text = dlg.FolderName;
    }

    private async void ScanBtn_Click(object sender, RoutedEventArgs e) => await RunScanAsync();
    private async void VerifyBtn_Click(object sender, RoutedEventArgs e) => await RunVerifyAsync();
    private async void VerifyAllBtn_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync();
        if (_sessionGroups.Count > 0 || _filteredSessionGroups.Count > 0)
            await RunVerifyAsync();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e) => ClearResults();

    private void ClearResults()
    {
        _sessionGroups.Clear();
        _filteredSessionGroups.Clear();
        _lastReport = null;
        _selectedTableRow = null;
        DetailBox.Text = "";
        SearchBox.Text = "";
        RefreshSummaryEmpty();
        AppendLog(_language["verifyResultsCleared"]);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = SearchBox.Text?.Trim().ToLowerInvariant();
        _filteredSessionGroups.Clear();
        foreach (var session in _sessionGroups)
        {
            if (string.IsNullOrEmpty(filter)
                || session.HeaderText.ToLowerInvariant().Contains(filter)
                || session.CameraRows.Any(r =>
                    r.FileName.ToLowerInvariant().Contains(filter)
                    || r.Camera.ToLowerInvariant().Contains(filter)
                    || r.Details.ToLowerInvariant().Contains(filter)))
            {
                _filteredSessionGroups.Add(session);
            }
        }
    }

    private async void FolderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AutoScanCheck.IsChecked == true && !string.IsNullOrWhiteSpace(FolderBox.Text) && Directory.Exists(FolderBox.Text))
        {
            await RunScanAsync();
        }
    }

    private async void ExportTxtBtn_Click(object sender, RoutedEventArgs e) =>
        await ExportReportAsync(".txt");

    private async void ExportJsonBtn_Click(object sender, RoutedEventArgs e) =>
        await ExportReportAsync(".json");

    private async void ExportCsvBtn_Click(object sender, RoutedEventArgs e) =>
        await ExportReportAsync(".csv");

    private async void ExportAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport == null)
        {
            AppendLog(_language["verifyNothingToExport"]);
            return;
        }

        var exportFolder = GetReportExportFolder();
        if (exportFolder == null)
        {
            AppendLog(_language["verifySelectFolder"]);
            return;
        }

        try
        {
            var writer = new VerificationReportWriter(_language);
            await writer.ExportAllToFolderAsync(_lastReport, exportFolder);
            AppendLog(string.Format(_language["verifyExportedReports"], exportFolder));
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_language["verifyExportError"], ex.Message));
        }
    }

    private async Task ExportReportAsync(string extension)
    {
        if (_lastReport == null)
        {
            AppendLog(_language["verifyNothingToExport"]);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON report|*.json|Text report|*.txt|CSV report|*.csv",
            FileName = $"verification_report{extension}"
        };
        if (dlg.ShowDialog() != true) return;

        var writer = new VerificationReportWriter(_language);
        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        if (ext == ".csv")
            await writer.ExportCsvAsync(_lastReport, dlg.FileName);
        else if (ext == ".txt")
            await writer.ExportTextAsync(_lastReport, dlg.FileName);
        else
            await writer.ExportJsonAsync(_lastReport, dlg.FileName);

        AppendLog(string.Format(_language["verifyExported"], dlg.FileName));
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        AppendLog(_language["verifyCancelled"]);
    }

    private async Task RunScanAsync()
    {
        var folder = FolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            AppendLog(_language["verifySelectFolder"]);
            return;
        }

        if (_service == null || _isBusy) return;
        SetBusy(true, _language["verifyScanning"]);
        _cts = new CancellationTokenSource();
        try
        {
            _sessionGroups.Clear();
            _filteredSessionGroups.Clear();
            _lastReport = null;
            _selectedTableRow = null;
            DetailBox.Text = "";

            var entries = await Task.Run(() => _service.Scan(folder), _cts.Token);
            var sessions = _service.DiscoverSessions(folder).Count;

            foreach (var group in entries.GroupBy(e => e.SessionFolder, StringComparer.OrdinalIgnoreCase))
            {
                var label = Path.GetFileName(group.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var auditPlaceholder = new RecordingSessionAuditResult
                {
                    SessionFolder = group.Key,
                    SessionLabel = label,
                    SessionStatus = "Ready for verification"
                };
                auditPlaceholder.CamerasFound.AddRange(group.Select(g => g.CameraSlot).Distinct());
                var rows = group.Select(entry =>
                {
                    entry.Status = VerificationVerdict.NotChecked;
                    var row = VerificationReportMapper.ScanPlaceholderRow(entry, _language);
                    return new VerificationTableRowViewModel(row);
                });
                _sessionGroups.Add(new SessionGroupViewModel(auditPlaceholder, rows));
            }

            ApplyFilter();

            AppendLog(string.Format(_language["verifyScanComplete"], sessions, entries.Count));
            RefreshSummaryScanned(entries.Count);

            if (entries.Count == 0)
                AppendLog(_language["verifyNoVideos"]);

            if (_config.Verification.AutoVerifyAfterScan && entries.Count > 0)
                await RunVerifyAsync(entries);
        }
        catch (OperationCanceledException)
        {
            AppendLog(_language["verifyCancelled"]);
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_language["verifyScanError"], ex.Message));
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private async Task RunVerifyAsync() => await RunVerifyAsync(null);

    private async Task RunVerifyAsync(IReadOnlyList<VideoFileEntry>? preloaded)
    {
        var folder = FolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            AppendLog(_language["verifySelectFolder"]);
            return;
        }

        if (_service == null || (_isBusy && preloaded == null)) return;
        if (!_service.IsFfprobeAvailable)
        {
            AppendLog(_language["verifyFfprobeMissing"]);
            return;
        }

        SetBusy(true, _language["verifyVerifying"]);
        _cts = new CancellationTokenSource();
        try
        {
            var entries = preloaded ?? await Task.Run(() => _service.Scan(folder), _cts.Token);
            if (entries.Count == 0)
            {
                AppendLog(_language["verifyNoVideos"]);
                RefreshSummaryScanned(0);
                return;
            }

            _sessionGroups.Clear();
            _filteredSessionGroups.Clear();
            var progress = new Progress<VerificationProgressUpdate>(p =>
            {
                Dispatcher.Invoke(() => OnVerifyProgress(p));
            });

            _lastReport = await _service.VerifyAsync(
                folder, entries, _config, progress, _cts.Token, _verificationProfile, _language);

            if (_lastReport != null)
                ApplyFullReport(_lastReport);
        }
        catch (OperationCanceledException)
        {
            AppendLog(_language["verifyCancelled"]);
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_language["verifyVerifyError"], ex.Message));
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private void OnVerifyProgress(VerificationProgressUpdate p)
    {
        ProgressText.Text = p.Message;
        if (p.CompletedTableRow != null)
        {
            UpsertRow(p.CompletedTableRow);
        }

        if (p.IsFinished && p.Report != null)
            ApplyFullReport(p.Report);
    }

    private void UpsertRow(VerificationTableRow row)
    {
        var vm = new VerificationTableRowViewModel(row);
        foreach (var session in _sessionGroups)
        {
            for (var i = 0; i < session.CameraRows.Count; i++)
            {
                if (string.Equals(session.CameraRows[i].Row.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    session.CameraRows[i] = vm;
                    ApplyFilter();
                    return;
                }
            }
        }

        var audit = new RecordingSessionAuditResult
        {
            SessionFolder = row.SessionFolder,
            SessionLabel = row.SessionLabel,
            SessionStatus = row.AuditStatus
        };
        audit.CamerasFound.Add(row.Camera);
        _sessionGroups.Add(new SessionGroupViewModel(audit, [vm]));
        ApplyFilter();
    }

    private void ApplyFullReport(VerificationReport report)
    {
        _lastReport = report;
        _sessionGroups.Clear();

        foreach (var audit in report.SessionAudits)
        {
            var rows = report.TableRows
                .Where(r => string.Equals(r.SessionFolder, audit.SessionFolder, StringComparison.OrdinalIgnoreCase))
                .Select(r => new VerificationTableRowViewModel(r));
            _sessionGroups.Add(new SessionGroupViewModel(audit, rows));
        }

        if (_sessionGroups.Count == 0)
        {
            foreach (var row in report.TableRows)
            {
                var audit = new RecordingSessionAuditResult
                {
                    SessionFolder = row.SessionFolder,
                    SessionLabel = row.SessionLabel,
                    SessionStatus = row.AuditStatus
                };
                audit.CamerasFound.Add(row.Camera);
                _sessionGroups.Add(new SessionGroupViewModel(audit, [new VerificationTableRowViewModel(row)]));
            }
        }

        ApplyFilter();

        foreach (var line in report.LogLines)
        {
            if (_simpleTableView && IsRawAuditDebugLine(line))
                continue;
            AppendLog(line, stamp: false);
        }

        ApplySummary(report);
    }

    private void ApplySummary(VerificationReport report)
    {
        SummaryCardsPanel.Visibility = Visibility.Visible;
        var s = report.Summary;
        CardOverallValue.Text = FriendlyVerdict(s.OverallVerdict);
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(s.OverallVerdict);
        CardFoundValue.Text = s.TotalVideosFound.ToString();
        CardPassValue.Text = s.VideosPassed.ToString();
        CardWarnValue.Text = s.VideosWarning.ToString();
        CardFailValue.Text = s.VideosFailed.ToString();
        CardSourceValue.Text = s.ExpectedSettingsSource;
        var sessionCount = report.SessionAudits.Count > 0
            ? report.SessionAudits.Count
            : report.Videos.Select(v => v.Entry.SessionFolder).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        CardDurSpreadValue.Text = _simpleTableView
            ? ShortSessionStatus(s.SessionDurationMatch)
            : s.SessionDurationMatch;
        CardFpsSpreadValue.Text = _simpleTableView && sessionCount > 1
            ? $"{sessionCount} sessions audited separately"
            : s.FpsSpreadDisplay;
        var sess = report.SessionResult;
        CardTimingConfidenceValue.Text = sess.SessionScientificTimingConfidence;
        CardTimingModeValue.Text = DisplayTimingMode(sess.SessionTimingMode);
        CardOriginalFramesValue.Text = sess.OriginalFramesOnly ? "Real frames only; no duplicates/placeholders" : "Review details";
        CardDuplicateFramesValue.Text = sess.DuplicateFramesTotal.ToString();
        CardPlaceholderFramesValue.Text = sess.PlaceholderFramesTotal.ToString();
        CardQueueDropsValue.Text = sess.WriterQueueDropsTotal.ToString();
        CardTimestampCsvValue.Text = string.IsNullOrWhiteSpace(sess.TimestampCsvStatus) ? "-" : sess.TimestampCsvStatus;
        CardTrimSourceValue.Text = string.IsNullOrWhiteSpace(sess.RecommendedTrimSource) ? "-" : ShortTimingSource(sess.RecommendedTrimSource);
        ContainerDurationWarningBox.Visibility = sess.ShowContainerWallClockWarning
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailBox.Text = BuildSummaryText(report);
    }

    private static string FriendlyVerdict(VerificationVerdict verdict) => verdict switch
    {
        VerificationVerdict.Pass => "PASS",
        VerificationVerdict.Warning => "WARNING",
        VerificationVerdict.Fail => "FAIL",
        VerificationVerdict.Verifying => "VERIFYING",
        VerificationVerdict.Scanning => "SCANNING",
        _ => "Not verified yet"
    };

    private static string ShortTimingSource(string value) =>
        string.Equals(value, "PerFrameCaptureTimestamps", StringComparison.OrdinalIgnoreCase)
            ? "Timestamp CSV"
            : value;

    private static string ShortSessionStatus(string value)
    {
        var normalized = (value ?? "").Trim().ToUpperInvariant();
        return normalized switch
        {
            "PASS" or "PASS_ORIGINAL_TIMING" or "PASS_ORIGINAL_TIMING_WITH_NOTE" => "PASS",
            "PASS_WITH_WARNING" or "WARNING" => "WARNING",
            "FAIL" => "FAIL",
            _ => string.IsNullOrWhiteSpace(value) ? "-" : value
        };
    }

    private string BuildSummaryText(VerificationReport report)
    {
        var s = report.Summary;
        var sess = report.SessionResult;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(_language["verifySessionScopeNote"]);
        sb.AppendLine();

        foreach (var audit in report.SessionAudits)
        {
            sb.AppendLine($"--- Session: {audit.SessionLabel} ---");
            sb.AppendLine(_simpleTableView
                ? SessionGroupViewModel.BuildFriendlySessionSummary(audit)
                : audit.ComparisonSummaryText);
            sb.AppendLine();
        }

        sb.AppendLine($"--- {_language["verifySessionTitle"]} ---");
        sb.AppendLine($"{_language["verifySessionOverall"]}: {sess.OverallResult}");
        sb.AppendLine($"{_language["verifyExpectedCameras"]}: {sess.ExpectedCameras}");
        sb.AppendLine($"{_language["verifyDetectedVideos"]}: {sess.DetectedVideos}");
        sb.AppendLine($"{_language["verifyMissingCameras"]}: {sess.MissingCameraVideos}");
        if (sess.MinDurationSeconds.HasValue)
            sb.AppendLine($"{_language["verifyMinDuration"]}: {sess.MinDurationSeconds:F2}s");
        if (sess.MaxDurationSeconds.HasValue)
            sb.AppendLine($"{_language["verifyMaxDuration"]}: {sess.MaxDurationSeconds:F2}s");
        if (sess.DurationSpreadSeconds.HasValue)
            sb.AppendLine($"{_language["verifyDurationSpread"]}: {sess.DurationSpreadSeconds:F2}s");
        if (sess.InterCameraFrameDifference.HasValue && report.SessionAudits.Count <= 1)
            sb.AppendLine($"{_language["verifyInterCameraFrameDiff"]}: {sess.InterCameraFrameDifference}");
        if (sess.InterCameraDurationDifferenceSeconds.HasValue && report.SessionAudits.Count <= 1)
            sb.AppendLine($"{_language["verifyInterCameraDurationDiff"]}: {sess.InterCameraDurationDifferenceSeconds:F2}s");
        if (sess.MinFps.HasValue)
            sb.AppendLine($"{_language["verifyMinFps"]}: {sess.MinFps:F1}");
        if (sess.MaxFps.HasValue)
            sb.AppendLine($"{_language["verifyMaxFps"]}: {sess.MaxFps:F1}");
        if (sess.FpsSpread.HasValue)
            sb.AppendLine($"{_language["verifyFpsSpreadValue"]}: {sess.FpsSpread:F2}");
        sb.AppendLine($"{_language["verifyScientificTimingStatus"]}: {sess.ScientificTimingStatus}");
        sb.AppendLine($"Scientific Timing Confidence: {sess.SessionScientificTimingConfidence}");
        sb.AppendLine($"Recording mode: {DisplayTimingMode(sess.SessionTimingMode)}");
        sb.AppendLine($"Frame integrity: {(sess.OriginalFramesOnly ? "Real frames only; no duplicates/placeholders" : "Review details")}");
        sb.AppendLine($"Duplicate Frames: {sess.DuplicateFramesTotal}");
        sb.AppendLine($"Placeholder Frames: {sess.PlaceholderFramesTotal}");
        sb.AppendLine($"Writer drops: {sess.WriterQueueDropsTotal}");
        sb.AppendLine($"Timestamp CSV Status: {sess.TimestampCsvStatus}");
        sb.AppendLine($"Scientific timing source: {ShortTimingSource(sess.RecommendedTrimSource)}");
        if (string.Equals(sess.SessionScientificTimingConfidence, ScientificTimingConfidence.High, StringComparison.OrdinalIgnoreCase))
            sb.AppendLine(ScientificTimingConfidence.HighMessage);
        sb.AppendLine("Original Capture Mode preserves real camera frames only. Frame counts may differ because cameras delivered real frames at different measured FPS. Real frames only; no duplicates/placeholders.");
        sb.AppendLine(OriginalCaptureVerificationPolicy.FrameCountDifferenceNote);
        sb.AppendLine("Use timestamp CSV for timing-sensitive analysis.");
        if (sess.ShowContainerWallClockWarning)
            sb.AppendLine(sess.ContainerWallClockWarning);
        var timingMessage = report.Videos
            .Select(v => v.ScientificTimingMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        if (!string.IsNullOrWhiteSpace(timingMessage))
            sb.AppendLine($"{_language["verifyScientificTimingMessage"]}: {timingMessage}");
        foreach (var m in sess.SessionMessages)
            sb.AppendLine($"  • {m}");

        sb.AppendLine();
        sb.AppendLine($"{_language["verifyTimingInterpretationTitle"]}:");
        sb.AppendLine(_language["verifyTimingInterpretation1"]);
        sb.AppendLine(_language["verifyTimingInterpretation2"]);
        sb.AppendLine(_language["verifyTimingInterpretation3"]);
        sb.AppendLine(_language["verifyTimingInterpretation4"]);

        return sb.ToString().TrimEnd();
    }

    private void RefreshSummaryEmpty()
    {
        SummaryCardsPanel.Visibility = Visibility.Collapsed;
        CardOverallValue.Text = "Not verified yet";
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(VerificationVerdict.NotChecked);
        CardFoundValue.Text = "0";
        CardPassValue.Text = "0";
        CardWarnValue.Text = "0";
        CardFailValue.Text = "0";
        CardSourceValue.Text = "—";
        CardDurSpreadValue.Text = "—";
        CardFpsSpreadValue.Text = "—";
        CardTimingConfidenceValue.Text = "Pending";
        CardTimingModeValue.Text = "—";
        CardOriginalFramesValue.Text = "Pending";
        CardDuplicateFramesValue.Text = "—";
        CardPlaceholderFramesValue.Text = "—";
        CardQueueDropsValue.Text = "—";
        CardTimestampCsvValue.Text = "Pending";
        CardTrimSourceValue.Text = "Pending";
        ContainerDurationWarningBox.Visibility = Visibility.Collapsed;
        DetailBox.Text = _language["verifyDetailEmpty"];
    }

    private void RefreshSummaryScanned(int entriesCount)
    {
        RefreshSummaryEmpty();
        SummaryCardsPanel.Visibility = entriesCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CardFoundValue.Text = entriesCount.ToString();
        CardOverallValue.Text = entriesCount > 0 ? "Not verified yet" : NoVideoMessage;
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(VerificationVerdict.NotChecked);
        CardPassValue.Text = "—";
        CardWarnValue.Text = "—";
        CardFailValue.Text = "—";
        CardTimingConfidenceValue.Text = "Pending";
        CardOriginalFramesValue.Text = "Pending";
        CardTimestampCsvValue.Text = "Pending";
        CardTrimSourceValue.Text = "Pending";
        DetailBox.Text = entriesCount > 0
            ? "Scan found videos. Detailed verification has not been run yet. Click Verify All to calculate timing, frame integrity, and metadata results."
            : NoVideoMessage;
    }

    private const string NoVideoMessage =
        "No MP4 videos found in the selected folder.\n\nChoose a recording session folder or click Browse.";

    private bool _isBusy;

    private void SetBusy(bool busy, string message)
    {
        _isBusy = busy;
        ScanBtn.IsEnabled = !busy;
        VerifyBtn.IsEnabled = !busy;
        VerifyAllBtn.IsEnabled = !busy;
        BrowseFolderBtn.IsEnabled = !busy;
        ClearBtn.IsEnabled = !busy;
        ExportTxtBtn.IsEnabled = !busy;
        ExportJsonBtn.IsEnabled = !busy;
        ExportCsvBtn.IsEnabled = !busy;
        ExportAllBtn.IsEnabled = !busy;
        CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = message;
    }

    private void AppendLog(string line, bool stamp = true)
    {
        var text = stamp ? $"[{DateTime.Now:HH:mm:ss}] {line}" : line;
        LogBox.AppendText(text + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private static bool IsRawAuditDebugLine(string line)
    {
        var text = line.Trim();
        return text.Contains("frameCountDifferenceAcceptedBecauseOriginalMode", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("sessionTimingMode:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("sessionAuditSummary", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetReportExportFolder()
    {
        if (_lastReport == null) return null;
        var selected = FolderBox.Text?.Trim();
        if (string.IsNullOrEmpty(selected) || !Directory.Exists(selected))
            return null;

        var sessions = _lastReport.Videos
            .Select(v => v.Entry.SessionFolder)
            .Where(s => !string.IsNullOrEmpty(s) && Directory.Exists(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sessions.Count == 1 &&
            !string.Equals(sessions[0], selected, StringComparison.OrdinalIgnoreCase))
            return sessions[0];

        return selected;
    }
}

internal static class VerificationUiBrushes
{
    private static readonly SolidColorBrush Pass = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush PassNote = new(Color.FromRgb(0x14, 0xB8, 0xA6));
    private static readonly SolidColorBrush Warn = new(Color.FromRgb(0xEA, 0xB3, 0x08));
    private static readonly SolidColorBrush Fail = new(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly SolidColorBrush Neutral = new(Color.FromRgb(0x94, 0xA3, 0xB8));

    static VerificationUiBrushes()
    {
        Pass.Freeze();
        PassNote.Freeze();
        Warn.Freeze();
        Fail.Freeze();
        Neutral.Freeze();
    }

    public static Brush ForVerdict(VerificationVerdict v) => v switch
    {
        VerificationVerdict.Pass => Pass,
        VerificationVerdict.Warning => Warn,
        VerificationVerdict.Fail => Fail,
        _ => Neutral
    };

    public static Brush ForStatus(string status, VerificationVerdict fallback)
    {
        var normalized = (status ?? "").Trim().ToUpperInvariant();
        return normalized switch
        {
            "PASS" or "PASS_ORIGINAL_TIMING" => Pass,
            "PASS_ORIGINAL_TIMING_WITH_NOTE" => PassNote,
            "PASS_WITH_WARNING" or "WARNING" => Warn,
            "FAIL" => Fail,
            _ => ForVerdict(fallback)
        };
    }

    public static Brush ForMatch(VerificationMatchStatus m) => m switch
    {
        VerificationMatchStatus.Yes => Pass,
        VerificationMatchStatus.Warning => Warn,
        VerificationMatchStatus.No => Fail,
        _ => Neutral
    };
}
