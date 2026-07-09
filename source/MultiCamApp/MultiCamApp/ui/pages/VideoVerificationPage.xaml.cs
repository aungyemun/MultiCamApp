////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MultiCamApp.Core;
using MultiCamApp.Localization;
using MultiCamApp.Utils;
using MultiCamApp.Verification;

namespace MultiCamApp.Ui.Pages;

public partial class VideoVerificationPage : UserControl
{
    // Static so the nested view-model classes below (constructed all over this file
    // without a LanguageManager reference) can localize their display text. There is
    // only ever one VideoVerificationPage instance per running app.
    internal static LanguageManager? CurrentLanguage;

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
            var L = CurrentLanguage;
            string T(string key, string fallback) => L?[key] is { Length: > 0 } v ? v : fallback;

            var cameraCount = audit.CamerasFound.Count > 0
                ? audit.CamerasFound.Count
                : audit.CameraVideos.Select(v => v.Entry.CameraSlot).Distinct().Count();
            var mode = DisplayTimingMode(audit.SessionTimingMode);
            var realFramesOnly = string.Equals(audit.SessionTimingMode, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase)
                && audit.TotalDuplicates == 0
                && audit.TotalPlaceholders == 0;
            var timingSource = audit.CameraVideos.Any(v => v.Metadata?.FrameTimestampCsvWritten == true)
                ? T("verifyTimestampCsvSource", "Timestamp CSV")
                : "-";
            var frameIntegrity = realFramesOnly
                ? T("verifyRealFramesOnlyText", "Real frames only; no duplicates/placeholders")
                : T("verifyReviewFrameIntegrityText", "review frame integrity details");

            return string.Join(Environment.NewLine, new[]
            {
                string.Format(T("verifySessionLabel", "Session: {0}"), audit.SessionLabel),
                string.Format(T("verifyResultLabel", "Result: {0}"), NormalizeSessionResult(audit.SessionStatus)),
                string.Format(T("verifyCamerasLabel", "Cameras: {0}"), cameraCount),
                string.Format(T("verifyRecordingModeLabelColon", "Recording mode: {0}"), mode),
                string.Format(T("verifyFrameIntegrityLabelColon", "Frame integrity: {0}"), frameIntegrity),
                string.Format(T("verifyWriterDropsLabelColon", "Writer drops: {0}"), audit.TotalQueueDrops),
                string.Format(T("verifyTimingSourceLabelColon", "Timing source: {0}"), timingSource),
                string.Format(T("verifyNoteLabel", "Note: {0}"), FriendlySessionNote(audit))
            });
        }

        internal static string NormalizeSessionResult(string status)
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
            var L = CurrentLanguage;
            string T(string key, string fallback) => L?[key] is { Length: > 0 } v ? v : fallback;

            if (audit.FrameCountDifferenceAcceptedBecauseOriginalMode)
                return T("verifyNoteFrameCountDifference", OriginalCaptureVerificationPolicy.FrameCountDifferenceNote);
            if (audit.Failures.Count > 0)
                return T("verifyNoteFailedInspect", "verification failed; inspect details.");
            if (audit.Warnings.Count > 0)
                return T("verifyNoteWarningsPresent", "warnings present; inspect details.");
            if (audit.InterpretationNotes.Count > 0)
                return audit.InterpretationNotes[0].TrimEnd('.') + ".";
            return T("verifyNoteReadyForAnalysis", "ready for analysis.");
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
            ? T("verifyReadyForVerification", "Ready for verification")
            : Row.AuditStatus;
        public string RecommendedAction => Row.Result == VerificationVerdict.NotChecked
            ? T("verifyClickVerifyAll", "Click Verify All")
            : Row.RecommendedAction;
        public string SimpleRecommendedAction => Row.Result == VerificationVerdict.NotChecked
            ? T("verifyClickVerifyAllPeriod", "Click Verify All.")
            : ToSimpleRecommendedAction(Row.AuditStatus, Row.TimestampRowsDisplay);

        private static string T(string key, string fallback) =>
            CurrentLanguage?[key] is { Length: > 0 } v ? v : fallback;

        private static string ToSimpleRecommendedAction(string status, string timestampCsvDisplay)
        {
            // Match on the RAW (untranslated) status code from Row.AuditStatus — PASS/WARNING/FAIL
            // are kept as fixed English status codes (like metadata field values), not translated,
            // so this switch and any downstream status coloring/matching stays reliable.
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            // "Use timestamp CSV" used to be shown unconditionally for any WARNING/PASS_WITH_WARNING
            // row, even when the Timestamp CSV column itself reads "-"/"MISSING" (no CSV was ever
            // written) — telling the user to use a file that doesn't exist. Only recommend it when
            // TimestampRowsDisplay actually contains a row count (e.g. "17960 rows" or a bare number).
            var hasCsv = (timestampCsvDisplay ?? "").Any(char.IsDigit);
            return normalized switch
            {
                "PASS" or "PASS_ORIGINAL_TIMING" => T("verifyRecActionReadyForAnalysis", "Ready for analysis"),
                "PASS_ORIGINAL_TIMING_WITH_NOTE" => hasCsv
                    ? T("verifyRecActionReadyUseTimestamp", "Ready; use timestamp CSV")
                    : T("verifyRecActionReadyNoTimestamp", "Ready for analysis; no timestamp CSV was written"),
                "PASS_WITH_WARNING" or "WARNING" => hasCsv
                    ? T("verifyRecActionUseTimestamp", "Use timestamp CSV")
                    : T("verifyRecActionNoTimestampCsv", "No timestamp CSV available; inspect frame data manually"),
                "FAIL" => T("verifyRecActionDoNotUse", "Do not use; inspect details"),
                _ => T("verifyRecActionReview", "Review verification details.")
            };
        }

        private static string ShortTimingSource(string value) =>
            string.Equals(value, "PerFrameCaptureTimestamps", StringComparison.OrdinalIgnoreCase)
                ? T("verifyTimestampCsvSource", "Timestamp CSV")
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

    // V2 verification — runs independently of VideoVerificationService (STABLE_CORE_V1)
    private readonly V2VerificationRunner _v2Runner = new();
    private IReadOnlyList<V2SessionVerificationGroup> _lastV2Groups = [];
    // Map: final MP4 absolute path → V2 metadata (populated during scan/verify)
    private readonly Dictionary<string, V2RecordingMetadata> _v2MetadataByPath =
        new(StringComparer.OrdinalIgnoreCase);

    // Deep Verify — independent, on-demand per-frame MD5 duplicate-frame check (not STABLE_CORE_V1,
    // not part of the Scan/Verify flow above; see DeepVerifyService for why this needs full ffmpeg.exe).
    private DeepVerifyService? _deepVerifyService;
    private readonly Dictionary<string, DeepVerifyFileResult> _deepVerifyResults =
        new(StringComparer.OrdinalIgnoreCase);

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
        grid.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ForwardMouseWheelToAncestorScrollViewer));
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
            Header = Header(L["verifyColStatus"], L["verifyColStatusTooltip"]),
            Width = new DataGridLength(120),
            CellTemplate = CreateTextCellTemplate(
                nameof(VerificationTableRowViewModel.AuditStatus),
                nameof(VerificationTableRowViewModel.ResultBrush))
        });

        void AddText(string headerKey, string binding, double width, string? tooltipKey = null)
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = Header(L[headerKey], tooltipKey != null ? L[tooltipKey] : null),
                Width = new DataGridLength(width),
                MinWidth = Math.Min(width, 96),
                CellTemplate = CreateTextCellTemplate(binding)
            });
        }

        if (_simpleTableView)
        {
            AddText("verifyColCamera", nameof(VerificationTableRowViewModel.Camera), 78);
            AddText("verifyColDevice", nameof(VerificationTableRowViewModel.Device), 180);
            AddText("verifyColRealCaptureFps", nameof(VerificationTableRowViewModel.MeasuredNativeFps), 115, "verifyColRealCaptureFpsTooltip");
            AddText("verifyColPlaybackFps", nameof(VerificationTableRowViewModel.ContainerFps), 95, "verifyColPlaybackFpsTooltip");
            AddText("verifyColFpsStability", nameof(VerificationTableRowViewModel.FpsStabilityGrade), 105);
            AddText("verifyColFramesWritten", nameof(VerificationTableRowViewModel.FramesWritten), 100);
            AddText("verifyColWriterDrops", nameof(VerificationTableRowViewModel.QueueDrops), 92, "verifyColWriterDropsTooltip");
            AddText("verifyColTimestampCsv", nameof(VerificationTableRowViewModel.TimestampRows), 105, "verifyColTimestampCsvTooltip");
            AddText("verifyColTimingSource", nameof(VerificationTableRowViewModel.TimingSource), 155, "verifyColTimingSourceTooltip");
            AddText("verifyColRecommendedAction", nameof(VerificationTableRowViewModel.SimpleRecommendedAction), 310);
            return;
        }

        AddText("verifyColCamera", nameof(VerificationTableRowViewModel.Camera), 78);
        AddText("verifyColDevice", nameof(VerificationTableRowViewModel.Device), 180);
        AddText("verifyColReqFps", nameof(VerificationTableRowViewModel.RequestedFps), 90, "verifyColReqFpsTooltip");
        AddText("verifyColWriterFps", nameof(VerificationTableRowViewModel.WriterFps), 90);
        AddText("verifyColPlaybackFps", nameof(VerificationTableRowViewModel.ContainerFps), 95, "verifyColPlaybackFpsTooltip");
        AddText("verifyColRealCaptureFps", nameof(VerificationTableRowViewModel.MeasuredNativeFps), 120, "verifyColRealCaptureFpsTooltip");
        AddText("verifyColFpsStability", nameof(VerificationTableRowViewModel.FpsStabilityGrade), 105, "verifyColFpsStabilityTooltip");
        AddText("verifyColCaptured", nameof(VerificationTableRowViewModel.FramesCaptured), 95, "verifyColCapturedTooltip");
        AddText("verifyColWritten", nameof(VerificationTableRowViewModel.FramesWritten), 95, "verifyColWrittenTooltip");
        AddText("verifyColTimestampCsv", nameof(VerificationTableRowViewModel.TimestampRows), 105, "verifyColTimestampCsvRowsTooltip");
        AddText("verifyColWallDuration", nameof(VerificationTableRowViewModel.WallDuration), 105, "verifyColWallDurationTooltip");
        AddText("verifyColMp4Duration", nameof(VerificationTableRowViewModel.ContainerDuration), 105, "verifyColMp4DurationTooltip");
        AddText("verifyColMp4WallDiff", nameof(VerificationTableRowViewModel.ContainerVsWallClock), 110, "verifyColMp4WallDiffTooltip");
        AddText("verifyColStartOffset", nameof(VerificationTableRowViewModel.StartOffset), 95);
        AddText("verifyColWriterDrops", nameof(VerificationTableRowViewModel.QueueDrops), 92, "verifyColWriterDropsTooltip");
        AddText("verifyColDup", nameof(VerificationTableRowViewModel.Duplicates), 74, "verifyColDupTooltip");
        AddText("verifyColPlaceholders", nameof(VerificationTableRowViewModel.Placeholders), 100, "verifyColPlaceholdersTooltip");
        AddText("verifyColIntMeanMinMaxStd", nameof(VerificationTableRowViewModel.CaptureIntervalMeanMinMaxStd), 165, "verifyColIntMeanMinMaxStdTooltip");
        AddText("verifyColP95P99", nameof(VerificationTableRowViewModel.CaptureIntervalP95P99), 110, "verifyColP95P99Tooltip");
        AddText("verifyColLongShortSevere", nameof(VerificationTableRowViewModel.CaptureGapCounts), 135, "verifyColLongShortSevereTooltip");
        AddText("verifyColTimingSource", nameof(VerificationTableRowViewModel.TimingSource), 155, "verifyColTimingSourceDetailedTooltip");
        AddText("verifyColRecommendedAction", nameof(VerificationTableRowViewModel.RecommendedAction), 320);
    }

    /// <summary>
    /// Each per-camera DataGrid inside the session list sets its own
    /// ScrollViewer.VerticalScrollBarVisibility="Auto" (see <see cref="RebuildSessionItemTemplate"/>),
    /// and WPF's DataGrid always marks MouseWheel as handled once it processes it — even when the
    /// grid has nothing left to scroll (it's usually only 1-4 rows). Without this, hovering over a
    /// table's rows and scrolling does nothing: the DataGrid eats the event instead of it reaching
    /// the SessionResultsList's own scrollable region above it (<see cref="SessionResultsScrollViewer"/>),
    /// which is what actually needs to scroll when there are more sessions than fit in the visible
    /// area. Unconditional: a 1-4 row table never has a legitimate reason to trap wheel scroll.
    /// </summary>
    private static void ForwardMouseWheelToAncestorScrollViewer(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d) return;

        var parent = FindAncestorScrollViewer(d);
        if (parent is ScrollViewer ancestor)
        {
            e.Handled = true;
            ancestor.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent
            });
        }
    }

    /// <summary>
    /// <see cref="SessionResultsScrollViewer"/> wraps the session list in its own confined,
    /// independently-scrollable region (so the page doesn't grow unbounded with many sessions) —
    /// unlike the per-camera DataGrids, this one's own scrolling is legitimate and should work
    /// normally. But once it's scrolled all the way to the boundary in the wheel's direction (or
    /// there's nothing to scroll at all), further wheel input should fall through to the outer
    /// page ScrollViewer instead of being silently swallowed — this is what "scroll around the
    /// verification table part" means in practice: scroll the session list first, then the page.
    /// </summary>
    private void SessionResultsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        bool wantsUp = e.Delta > 0;
        bool wantsDown = e.Delta < 0;
        bool atTop = sv.VerticalOffset <= 0.5;
        bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5;
        if (!((wantsUp && atTop) || (wantsDown && atBottom)))
            return; // sv still has room to scroll itself — let its own default handling run

        var outer = FindAncestorScrollViewer(sv);
        if (outer is null) return;
        e.Handled = true;
        outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent
        });
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject d)
    {
        var parent = VisualTreeHelper.GetParent(d);
        while (parent != null && parent is not ScrollViewer)
            parent = VisualTreeHelper.GetParent(parent);
        return parent as ScrollViewer;
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
        {
            var detail = string.IsNullOrWhiteSpace(vm.Row.DetailText)
                ? _language["verifyDetailEmpty"]
                : vm.Row.DetailText;
            // Append V2 engine section if not already included
            if (!detail.Contains("VideoEngineV2 Recording Engine", StringComparison.Ordinal))
            {
                var v2Meta = GetV2MetadataForRow(vm.Row);
                if (v2Meta is not null)
                    detail += Environment.NewLine + Environment.NewLine +
                              V2VerificationRunner.BuildV2EngineDetailSection(v2Meta, null);
            }
            return detail;
        }

        var row = vm.Row;
        var L = _language;
        var sb = new System.Text.StringBuilder();
        AppendSection(sb, L["verifyDetailResultSection"],
            (L["verifyDetailStatus"], vm.AuditStatus),
            (L["verifyDetailVerificationResult"], vm.ResultDisplay),
            (L["verifyDetailRecommendedAction"], vm.SimpleRecommendedAction));
        AppendMessages(sb, L["verifyDetailWarnings"], row.WarningMessages);
        AppendMessages(sb, L["verifyDetailFailures"], row.ErrorMessages);

        AppendSection(sb, L["verifyDetailFileSection"],
            (L["verifyDetailCamera"], vm.Camera),
            (L["verifyDetailDevice"], vm.Device),
            (L["verifyDetailFile"], row.FileName),
            (L["verifyDetailMetadata"], row.MetadataStatus));

        AppendSection(sb, L["verifyDetailRecordingFpsSection"],
            (L["verifyDetailRequestedFps"], vm.RequestedFps),
            (L["verifyDetailPlaybackFps"], vm.ContainerFps),
            (L["verifyDetailRealCaptureFps"], vm.MeasuredNativeFps),
            (L["verifyDetailFpsStability"], vm.FpsStabilityGrade),
            (L["verifyDetailScientificTimingSource"], vm.TimingSource));

        AppendSection(sb, L["verifyDetailTimingSection"],
            (L["verifyDetailWallClockDuration"], vm.WallDuration),
            (L["verifyDetailContainerDuration"], vm.ContainerDuration),
            (L["verifyDetailContainerVsWallClockDiff"], vm.ContainerVsWallClock),
            (L["verifyDetailStartOffset"], vm.StartOffset));

        AppendSection(sb, L["verifyDetailFrameIntegritySection"],
            (L["verifyDetailFramesCaptured"], vm.FramesCaptured),
            (L["verifyDetailFramesWritten"], vm.FramesWritten),
            (L["verifyDetailTimestampCsv"], vm.TimestampRows),
            (L["verifyDetailWriterDrops"], vm.QueueDrops),
            (L["verifyDetailDuplicateFrames"], vm.Duplicates),
            (L["verifyDetailPlaceholderFrames"], vm.Placeholders));

        AppendSection(sb, L["verifyDetailCaptureQualitySection"],
            (L["verifyDetailCaptureIntervalMeanMinMaxStd"], vm.CaptureIntervalMeanMinMaxStd),
            (L["verifyDetailCaptureIntervalP95P99"], vm.CaptureIntervalP95P99),
            (L["verifyDetailLongShortSevereGapCounts"], vm.CaptureGapCounts));

        AppendSection(sb, L["verifyDetailScientificRecommendationSection"],
            (L["verifyDetailScientificTimingStatus"], row.ScientificTimingStatusDisplay),
            (L["verifyDetailMetadataCompleteness"], row.MetadataCompletenessPercent),
            (L["verifyDetailRecommendedAction"], vm.SimpleRecommendedAction));

        // Append V2 engine section when the selected recording is a V2 recording
        var v2MetaSimple = GetV2MetadataForRow(row);
        if (v2MetaSimple is not null)
        {
            sb.AppendLine();
            sb.AppendLine(V2VerificationRunner.BuildV2EngineDetailSection(v2MetaSimple, null));
        }

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
            ? (CurrentLanguage?["verifyOriginalCaptureModeLabel"] is { Length: > 0 } v ? v : "Original Capture Mode")
            : string.IsNullOrWhiteSpace(value) ? "-" : value;

    private string _verificationProfile = "Standard";

    public void Initialize(AppConfig config, LanguageManager language)
    {
        _config = config;
        _language = language;
        _service = new VideoVerificationService(config.Verification);
        _deepVerifyService = new DeepVerifyService(config.Verification);
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
        UpdateFfmpegWarning();
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
        CurrentLanguage = _language;
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

        StableTimingNoteText.Text = L["verifyStableTimingNote"];
        StableTimingNoteText.ToolTip = L["verifyStableTimingTooltip"];
        PageSubtitle.ToolTip = L["verifyStableTimingTooltip"];
        OriginalCaptureExplanationText.Text = L["verifyOriginalCaptureExplanation"];
        ContainerDurationWarningText.Text = L["verifyContainerDurationWarning"];
        VerificationSummaryGroupTitle.Text = L["verifySummaryGroupTitle"];
        TimingQualityGroupTitle.Text = L["verifyTimingQualityGroupTitle"];
        ScientificTimingConfidenceLabel.Text = L["verifyScientificTimingConfidence"];
        ScientificTimingConfidenceLabel.ToolTip = L["verifyStableTimingTooltip"];
        CardTimingConfidenceValue.ToolTip = L["verifyStableTimingTooltip"];
        RecordingModeLabel.Text = L["verifyRecordingModeLabel"];
        ScientificTimingSourceLabel.Text = L["verifyScientificTimingSource"];
        FrameIntegrityGroupTitle.Text = L["verifyFrameIntegrityGroupTitle"];
        FrameIntegrityLabel.Text = L["verifyFrameIntegrityGroupTitle"];
        FrameIntegrityLabel.ToolTip = L["verifyFrameIntegrityTooltip"];
        CardOriginalFramesValue.ToolTip = L["verifyFrameIntegrityTooltip"];
        DuplicateFramesLabel.Text = L["verifyDuplicateFramesLabel"];
        PlaceholderFramesLabel.Text = L["verifyPlaceholderFramesLabel"];
        WriterDropsLabel.Text = L["verifyWriterDropsLabel"];
        WriterDropsLabel.ToolTip = L["verifyWriterDropsTooltip"];
        CardQueueDropsValue.ToolTip = L["verifyWriterDropsTooltip"];
        TimestampCsvStatusLabel.Text = L["verifyTimestampCsvStatusLabel"];
        HardwareCalibrationLockLabel.Text = L["verifyHardwareCalibrationLockLabel"];
        HardwareCalibrationLockLabel.ToolTip = L["verifyHardwareCalibrationLockTooltip"];
        CardEnvLockValue.ToolTip = L["verifyHardwareCalibrationLockTooltip"];
        VerificationTableLabel.Text = L["verifyTableLabel"];
        SimpleViewRadio.Content = L["verifySimpleView"];
        DetailedViewRadio.Content = L["verifyDetailedView"];

        DeepVerifyBtn.Content = L["verifyDeepVerify"];
        DeepVerifyBtn.ToolTip = L["verifyDeepVerifyTooltip"];
        DeepVerifyGroupTitle.Text = L["verifyDeepVerifyGroupTitle"];
        DeepVerifyExplanationText.Text = L["verifyDeepVerifyExplanation"];
        DeepVerifyStatusLabel.Text = L["verifyDeepVerifyStatusLabel"];
        DeepVerifyFilesLabel.Text = L["verifyDeepVerifyFilesLabel"];
        DeepVerifyDuplicatesLabel.Text = L["verifyDeepVerifyDuplicatesLabel"];
        DeepVerifyWorstFileLabel.Text = L["verifyDeepVerifyWorstFileLabel"];

        RebuildSessionItemTemplate();
        PopulateVerificationProfiles();
        SelectVerificationProfile(_verificationProfile);
        UpdateFfprobeWarning();
        UpdateFfmpegWarning();
        if (_lastReport == null)
            RefreshSummaryEmpty();

        // RebuildSessionItemTemplate() re-reads bound properties (e.g. SimpleComparisonSummary),
        // so the session table refreshes to the new language automatically. DetailBox.Text is a
        // plain assigned string, not a binding, so without this it keeps showing whatever was last
        // built — in the language active at that time — until the next Verify/row-selection.
        if (_selectedTableRow != null)
            DetailBox.Text = BuildSelectedVideoDetail(_selectedTableRow);
        else if (_lastReport != null)
            DetailBox.Text = BuildSummaryText(_lastReport);
        else if (_lastV2Groups.Count > 0)
            DetailBox.Text = BuildV2StandaloneSummaryText(_lastV2Groups);
    }

    private void UpdateFfprobeWarning()
    {
        if (_service == null) return;
        FfprobeWarning.Visibility = _service.IsFfprobeAvailable ? Visibility.Collapsed : Visibility.Visible;
        FfprobeWarning.Text = _language["verifyFfprobeMissing"];
    }

    private void UpdateFfmpegWarning()
    {
        if (_deepVerifyService == null) return;
        FfmpegWarning.Visibility = _deepVerifyService.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        FfmpegWarning.Text = _language["verifyFfmpegMissing"];
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
        _lastV2Groups = [];
        _v2MetadataByPath.Clear();
        _deepVerifyResults.Clear();
        _selectedTableRow = null;
        DetailBox.Text = "";
        SearchBox.Text = "";
        // The Verification Log is an intentionally cumulative running history across repeated
        // Scan/Verify/Verify All actions (by design — see verifyV2PreScanDisclaimer's surrounding
        // logic), but that left "Clear results" as the one button with no way to actually reset it:
        // every prior action's lines, including exact repeats from clicking Scan/Verify/Verify All
        // back-to-back, stayed on screen forever. Clearing it here is the one place a user can
        // deliberately ask for a clean slate.
        LogBox.Clear();
        RefreshSummaryEmpty();
        AppendLog(_language["verifyResultsCleared"]);
    }

    // ── V2 helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Populates <see cref="_v2MetadataByPath"/> by reading sibling V2 metadata JSON
    /// for every MP4 entry found by the legacy scanner.
    /// </summary>
    private void PreloadV2Metadata(IReadOnlyList<VideoFileEntry> entries)
    {
        foreach (var entry in entries)
        {
            var meta = V2MetadataReader.TryReadForVideo(entry.FullPath);
            if (meta is not null)
                _v2MetadataByPath[entry.FullPath] = meta;
        }
    }

    /// <summary>
    /// Returns cached V2 metadata for the given table row, checking both the
    /// pre-loaded cache and the V2 group results.
    /// </summary>
    private V2RecordingMetadata? GetV2MetadataForRow(VerificationTableRow row)
    {
        if (string.IsNullOrEmpty(row.FilePath)) return null;
        if (_v2MetadataByPath.TryGetValue(row.FilePath, out var cached)) return cached;
        // Also check live groups (populated during verify)
        foreach (var g in _lastV2Groups)
            foreach (var s in g.Slots)
                if (string.Equals(s.FileSet.FinalVideoPath, row.FilePath, StringComparison.OrdinalIgnoreCase))
                    return s.Metadata;
        return null;
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

            // Pre-scan V2 metadata to cache for later enrichment and detail display
            _v2MetadataByPath.Clear();
            await Task.Run(() => PreloadV2Metadata(entries), _cts.Token);

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

            var v2Count = _v2MetadataByPath.Count;
            AppendLog(string.Format(_language["verifyScanComplete"], sessions, entries.Count)
                + (v2Count > 0 ? $"  ({v2Count} VideoEngineV2 slot(s) detected)" : ""));
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

        SetBusy(true, _language["verifyVerifying"]);
        _cts = new CancellationTokenSource();
        try
        {
            // Always run V2 standalone verification (no ffprobe required)
            _lastV2Groups = await Task.Run(() => _v2Runner.Run(folder), _cts.Token);
            if (_lastV2Groups.Count > 0)
            {
                _v2MetadataByPath.Clear();
                foreach (var g in _lastV2Groups)
                    foreach (var s in g.Slots)
                        if (s.Metadata is not null)
                            _v2MetadataByPath[s.FileSet.FinalVideoPath] = s.Metadata;
                AppendLog(string.Format(_language["verifyV2FoundSessions"],
                    _lastV2Groups.Count, _lastV2Groups.Sum(g => g.Slots.Count)));
                // The scan about to run (VideoVerificationService.VerifyAsync, STABLE_CORE_V1) is a
                // live streaming log — every line appears the instant it's computed, well before this
                // page's own V2 enrichment/correction step (which only runs after VerifyAsync returns).
                // For any V2 session, that raw scan reliably logs "Session status: FAIL" and a wall of
                // "FAIL: camN: individual camera audit failed" lines, because it doesn't understand
                // V2's metadata schema — followed later by a "[V2] Corrected..." line once this page
                // re-evaluates it. Both halves are individually true at the moment they're logged, but
                // reading top-to-bottom without reaching the correction at the end looks like a real
                // failure. Warn up front so nobody has to guess why FAIL text appears before a PASS.
                AppendLog(_language["verifyV2PreScanDisclaimer"]);
            }

            if (!_service.IsFfprobeAvailable)
            {
                // ffprobe absent — show V2 standalone results only
                AppendLog(_language["verifyFfprobeMissing"]);
                if (_lastV2Groups.Count > 0)
                    ApplyV2StandaloneResults(_lastV2Groups);
                else
                    AppendLog(_language["verifyNoVideos"]);
                return;
            }

            var entries = preloaded ?? await Task.Run(() => _service.Scan(folder), _cts.Token);
            if (entries.Count == 0 && _lastV2Groups.Count == 0)
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

            if (entries.Count > 0)
            {
                _lastReport = await _service.VerifyAsync(
                    folder, entries, _config, progress, _cts.Token, _verificationProfile, _language);

                if (_lastReport != null)
                {
                    ApplyFullReport(_lastReport);
                    // Enrich rows for V2 sessions detected alongside ffprobe results
                    if (_lastV2Groups.Count > 0)
                    {
                        EnrichV2SessionGroups();
                        EnrichV2VideoMetadata();
                        ReconcileV2Summary(entries.Count);
                        await ResaveAuditReportAfterEnrichment(folder);
                    }
                }
            }
            else if (_lastV2Groups.Count > 0)
            {
                ApplyV2StandaloneResults(_lastV2Groups);
            }
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

    /// <summary>
    /// Shows V2 results in the UI when ffprobe is absent.
    /// Each V2 session group becomes a <see cref="SessionGroupViewModel"/> with V2 table rows.
    /// </summary>
    private void ApplyV2StandaloneResults(IReadOnlyList<V2SessionVerificationGroup> groups)
    {
        _sessionGroups.Clear();
        _filteredSessionGroups.Clear();
        int totalPassed = 0, totalFailed = 0, totalWarn = 0;

        foreach (var group in groups)
        {
            var rows = V2VerificationRunner.ToTableRows(group);
            var audit = new RecordingSessionAuditResult
            {
                SessionFolder = group.SessionFolder,
                SessionLabel  = group.SessionLabel,
                SessionStatus = group.OverallStatus,
            };
            audit.CamerasFound.AddRange(group.Slots.Select(s => s.SlotName));
            var vms = rows.Select(r => new VerificationTableRowViewModel(r));
            _sessionGroups.Add(new SessionGroupViewModel(audit, vms));

            foreach (var r in rows)
            {
                if (r.Result == VerificationVerdict.Pass)    totalPassed++;
                else if (r.Result == VerificationVerdict.Fail) totalFailed++;
                else totalWarn++;
            }
        }

        ApplyFilter();

        // Populate summary cards manually (no VerificationReport)
        SummaryCardsPanel.Visibility = Visibility.Visible;
        int total = totalPassed + totalFailed + totalWarn;
        CardFoundValue.Text = total.ToString();
        CardPassValue.Text  = totalPassed.ToString();
        CardWarnValue.Text  = totalWarn.ToString();
        CardFailValue.Text  = totalFailed.ToString();
        CardSourceValue.Text = _language["verifySourceV2Metadata"];
        var overall = totalFailed > 0 ? VerificationVerdict.Fail
                    : totalWarn   > 0 ? VerificationVerdict.Warning
                    : VerificationVerdict.Pass;
        CardOverallValue.Text       = FriendlyVerdict(overall);
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(overall);
        CardDurSpreadValue.Text    = _language["verifyFfprobeUnavailableValue"];
        CardFpsSpreadValue.Text    = _language["verifyFfprobeUnavailableValue"];
        CardTimingConfidenceValue.Text = "—";
        CardTimingModeValue.Text   = "VideoEngineV2";
        CardOriginalFramesValue.Text   = _language["verifyV2NativeCapture"];
        CardTimestampCsvValue.Text     = groups.Any(g => g.Slots.Any(s => File.Exists(s.FileSet.TimestampCsvPath)))
                                             ? _language["verifyPresent"] : _language["verifyNotFound"];
        CardTrimSourceValue.Text   = _language["verifyTimestampCsvShort"];
        // Environmental lock card — read from V2 metadata JSON (parsed via V2MetadataReader)
        var envLockSlots = groups.SelectMany(g => g.Slots)
            .Where(s => s.Metadata?.EnvironmentalLockActive == true).ToList();
        CardEnvLockValue.Text = envLockSlots.Count == 0
            ? _language["verifyNotActive"]
            : string.Format(_language["verifyActiveSlotsTemplate"], envLockSlots.Count, envLockSlots.Count == 1 ? "" : "s");
        CardEnvLockValue.Foreground = envLockSlots.Count > 0
            ? System.Windows.Media.Brushes.LightGreen
            : (System.Windows.Media.Brush)FindResource("AppSecondaryTextBrush");
        ContainerDurationWarningBox.Visibility = Visibility.Collapsed;

        AppendLog(string.Format(_language["verifyV2StandaloneComplete"], totalPassed, totalWarn, totalFailed));
        DetailBox.Text = BuildV2StandaloneSummaryText(groups);
    }

    private static string BuildV2StandaloneSummaryText(IReadOnlyList<V2SessionVerificationGroup> groups)
    {
        var L = CurrentLanguage;
        string T(string key, string fallback) => L?[key] is { Length: > 0 } v ? v : fallback;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(T("verifyV2StandaloneHeader", "VideoEngineV2 Verification (ffprobe not available — V2 metadata only)"));
        sb.AppendLine();
        foreach (var g in groups)
        {
            sb.AppendLine(string.Format(T("verifySessionBracketStatus", "Session: {0}  [{1}]"), g.SessionLabel, g.OverallStatus));
            foreach (var s in g.Slots)
            {
                sb.AppendLine($"  {s.SlotName}: {(s.VerificationResult.Passed ? "PASS" : "FAIL")}");
                foreach (var issue in s.VerificationResult.Issues)
                    sb.AppendLine($"    {issue}");
            }
            sb.AppendLine();
        }
        sb.AppendLine(T("verifyV2StandaloneFfprobeNote",
            "Note: Duration, FPS, and codec info require ffprobe. Install ffprobe alongside the app for full verification."));
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// After legacy ffprobe verification completes, updates existing table rows for V2 slots
    /// with V2 metadata (device name, target FPS, frames written, engine detail).
    /// </summary>
    private void EnrichV2SessionGroups()
    {
        if (_v2MetadataByPath.Count == 0) return;
        bool anyEnriched = false;

        foreach (var sessionGroup in _sessionGroups)
        {
            for (var i = 0; i < sessionGroup.CameraRows.Count; i++)
            {
                var vm  = sessionGroup.CameraRows[i];
                var row = vm.Row;

                if (!_v2MetadataByPath.TryGetValue(row.FilePath, out var meta)) continue;

                // Enrich mutable fields on the STABLE_CORE_V1 row with V2 metadata
                if (string.IsNullOrEmpty(row.Device) || row.Device == "-")
                    row.Device = meta.Device;
                if (row.RequestedFps == "-" && meta.TargetFps > 0)
                    row.RequestedFps = $"{meta.TargetFps:F0}";
                if (row.FramesWrittenDisplay == "-" && meta.FramesWritten > 0)
                    row.FramesWrittenDisplay = meta.FramesWritten.ToString();
                if (row.MeasuredNativeFps == "-" && meta.MeasuredFpsFromTimestamps > 0)
                    row.MeasuredNativeFps = $"{meta.MeasuredFpsFromTimestamps:F3}";
                if (string.IsNullOrEmpty(row.MetadataStatus) || row.MetadataStatus == "Missing")
                    row.MetadataStatus = "V2 JSON";
                // Same schema-blindness as everything else here: the legacy mapper looks for a
                // flat "frameTimestampCsvWritten" field V2 JSON doesn't have, so this column shows
                // "MISSING" even though the CSV genuinely exists with real rows (already true of
                // every V2 recording — VideoEngineSettings.WriteTimestampCsv defaults true).
                if (meta.TimestampCsvWritten && meta.TimestampCsvRows > 0)
                    row.TimestampRowsDisplay = string.Format(_language["verifyTimestampCsvRowsCount"], meta.TimestampCsvRows);

                // Per-video detail panel's "Scientific Recommendation" section — same schema-blind
                // FAIL as everywhere else, plus a "Metadata completeness: 0.0%" that's technically
                // accurate against the 44 legacy flat-field names (V2 JSON has none of them under
                // those names) but reads as an alarming data-loss signal when the V2 JSON is
                // actually fully populated, just under a different, richer nested schema.
                if (row.ScientificTimingStatusDisplay == CameraAuditStatus.Fail)
                {
                    var tcSource = meta.InterCameraTimingConfidence is { Length: > 0 } ic ? ic : meta.TimingConfidence;
                    if (tcSource is { Length: > 0 })
                        row.ScientificTimingStatusDisplay = SessionGroupViewModel.NormalizeSessionResult(tcSource);
                }
                if (row.MetadataCompletenessPercent is "0.0%" or "0%")
                    row.MetadataCompletenessPercent = _language["verifyMetadataCompletenessV2Note"];

                // Cross-check the app's own frame counter against ffprobe's real container frame
                // count (row.FrameCount, populated from the video file itself when ffprobe is
                // available — V2 sessions have no legacy-format metadata for the mapper to prefer
                // instead, so this is the real decoded frame count, not another self-report).
                // Found via real-hardware audit (2026-07-06): under severe camera stall/recovery,
                // the app's own framesWritten/CSV counters can undercount the real encoded frame
                // count by hundreds of frames while every other session matches exactly — nothing
                // in the pipeline previously compared these two numbers, so this class of issue was
                // invisible even when ffprobe data was available.
                if (long.TryParse(row.FrameCount, out var realFrameCount) && realFrameCount > 0
                    && meta.FramesWritten > 0)
                {
                    var frameDiff = Math.Abs(realFrameCount - meta.FramesWritten);
                    if (frameDiff > 5)
                    {
                        row.WarningMessages.Add(string.Format(
                            _language["verifyV2FrameCountMismatchWarning"], realFrameCount, meta.FramesWritten, frameDiff));
                        if (row.Result == VerificationVerdict.Pass)
                            row.Result = VerificationVerdict.Warning;
                    }
                }

                // Append V2 engine detail to DetailText
                var v2Section = V2VerificationRunner.BuildV2EngineDetailSection(meta, null);
                if (!row.DetailText.Contains("VideoEngineV2", StringComparison.Ordinal))
                    row.DetailText = string.IsNullOrWhiteSpace(row.DetailText)
                        ? v2Section
                        : row.DetailText + Environment.NewLine + Environment.NewLine + v2Section;

                // Replace viewmodel so the grid reflects updated values
                sessionGroup.CameraRows[i] = new VerificationTableRowViewModel(row);
                anyEnriched = true;
            }

            // SessionComparisonService (STABLE_CORE_V1) only sets audit.SessionTimingMode to
            // "OriginalCapture" when every video's *legacy* CameraMetadataRecord.OriginalCaptureMode
            // already reads true — but that legacy record is schema-blind for V2 (MetadataParser
            // explicitly writes false/"" for fields the V2 JSON doesn't have under those names), so
            // this never fires for a V2 session, leaving SessionTimingMode blank and "Recording
            // mode: -" showing in both the per-session summary and (for a single-session scan) the
            // top card, even though every camera's own V2 metadata already confirms Original Capture
            // Mode. The per-row EnrichLegacyMetadataRecord above fixes the per-row legacy record, but
            // that doesn't retroactively fix SessionTimingMode, which was already computed earlier in
            // VerifyAsync before this method ever runs — so it needs its own correction here.
            if (string.IsNullOrWhiteSpace(sessionGroup.Audit.SessionTimingMode)
                && sessionGroup.CameraRows.Count > 0
                && sessionGroup.CameraRows.All(r => _v2MetadataByPath.TryGetValue(r.Row.FilePath, out var m) && m.IsOriginalCaptureMode))
            {
                sessionGroup.Audit.SessionTimingMode = OriginalCaptureAuditPolicy.Mode;
                sessionGroup.Audit.FrameCountDifferenceAcceptedBecauseOriginalMode = true;
                anyEnriched = true;
            }

            if (ReconcileV2SessionVerdict(sessionGroup))
                anyEnriched = true;
        }

        if (anyEnriched)
        {
            ApplyFilter();
            AppendLog(string.Format(_language["verifyV2Enriched"], _v2MetadataByPath.Count));
        }
    }

    /// <summary>
    /// <see cref="EnrichV2SessionGroups"/> patches the on-screen <see cref="VerificationTableRow"/>
    /// objects with V2 metadata, but "Export TXT/JSON/CSV" (<see cref="VerificationReportWriter"/>)
    /// and the auto-saved video_audit_report.txt read a completely separate object graph —
    /// <c>report.Videos[i].Metadata</c> (<see cref="CameraMetadataRecord"/>), populated by the
    /// legacy, schema-blind <c>MetadataParser</c> (STABLE_CORE_V1). For a VideoEngineV2 recording
    /// that parser "succeeds" (the file exists and is valid JSON) but extracts nothing meaningful,
    /// since none of the legacy flat field names it looks for exist in V2's nested schema — so every
    /// numeric field silently defaults to 0/false and every exported video shows
    /// "Scientific timing status: FAIL" regardless of the real, complete data sitting in the V2 JSON.
    /// This patches that second object graph the same authoritative-already-computed-value way, so
    /// exports reflect the same corrected picture the interactive page already shows. Only ever
    /// fills in real values already read from the recording's own V2 metadata; never invents data.
    /// </summary>
    private void EnrichV2VideoMetadata()
    {
        if (_lastReport == null || _v2MetadataByPath.Count == 0) return;

        foreach (var video in _lastReport.Videos)
        {
            if (_v2MetadataByPath.TryGetValue(video.Entry.FullPath, out var meta))
                V2VerificationRunner.EnrichLegacyMetadataRecord(video, meta);
        }
    }

    /// <summary>
    /// Corrects a false-positive session FAIL caused by the legacy ffprobe-based verification
    /// stack (VideoVerificationService/SessionComparisonService, STABLE_CORE_V1) not understanding
    /// V2's nested metadata schema — see the doc comment on
    /// <see cref="V2RecordingMetadata.VerificationGlobalSessionResult"/> for the full mechanism.
    /// Only ever moves a verdict FAIL → PASS/WARNING using a value the app itself already computed
    /// and wrote to disk at recording time; never invents a more lenient result and never touches
    /// a session where the legacy path and the V2-native audit already agree.
    /// Returns true if anything was changed (so the caller knows to refresh the grid/log).
    /// </summary>
    private bool ReconcileV2SessionVerdict(SessionGroupViewModel sessionGroup)
    {
        var audit = sessionGroup.Audit;
        if (audit.SessionStatus != CameraAuditStatus.Fail) return false;
        if (sessionGroup.CameraRows.Count == 0) return false;

        var v2Results = new List<string>();
        foreach (var vm in sessionGroup.CameraRows)
        {
            if (!_v2MetadataByPath.TryGetValue(vm.Row.FilePath, out var meta)) return false; // not a fully-V2 session
            var raw = meta.VerificationGlobalSessionResult is { Length: > 0 } g
                ? g
                : meta.VerificationSessionResult;
            if (string.IsNullOrWhiteSpace(raw)) return false; // one or more cameras lack this field — don't guess
            v2Results.Add(raw);
        }

        // All cameras in this session carry the same session-wide value (it's written identically
        // into every camera's own metadata.json), so any one of them is authoritative.
        var normalized = SessionGroupViewModel.NormalizeSessionResult(v2Results[0]);
        if (normalized == CameraAuditStatus.Fail) return false; // V2's own audit agrees it's a real fail

        // NormalizeSessionResult deliberately collapses to a short internal token ("WARNING", not
        // "PASS_WITH_WARNING" — see NormalizedResultToVerdict's doc comment). audit.SessionStatus and
        // row.AuditStatus are both display/export fields that use the full CameraAuditStatus
        // vocabulary everywhere else (SessionComparisonService.DetermineSessionStatus, per-row
        // metadata's own VerificationSessionResult, etc.) — writing the short token directly into
        // them here caused a real, confirmed vocabulary mismatch: a session with an untouched PASS
        // camera and a reconciled-from-FAIL camera showed "PASS_WITH_WARNING" on one row and bare
        // "WARNING" on the other, the exact same severity displayed as two different strings.
        var fullStatus = normalized == "WARNING" ? CameraAuditStatus.PassWithWarning : normalized;

        var oldStatus = audit.SessionStatus;
        audit.SessionStatus = fullStatus;
        audit.SessionVerdict = NormalizedResultToVerdict(normalized);
        // audit.SessionScientificTimingConfidence is a separate field (computed once by
        // SessionComparisonService, exported directly in report.SessionAudits) that this method
        // otherwise never touches — left at its stale FAILED even after SessionStatus/SessionVerdict
        // above are corrected, so JSON export would show a corrected session status sitting right
        // next to a contradicting "SessionScientificTimingConfidence": "FAILED".
        if (string.Equals(audit.SessionScientificTimingConfidence, ScientificTimingConfidence.Failed, StringComparison.OrdinalIgnoreCase))
            audit.SessionScientificTimingConfidence = normalized == "WARNING" ? ScientificTimingConfidence.Medium : ScientificTimingConfidence.High;
        // audit.Failures still holds the original false-positive entries (e.g. "Inter-camera frame
        // difference too large", "individual camera audit failed") — SimpleComparisonSummary's
        // "Note:" line reads Failures.Count directly and would keep saying "verification failed;
        // inspect details." even after the status above is corrected, contradicting it. Safe to
        // clear entirely: we only reach here because V2's own full session audit already
        // independently re-validated everything and confirmed this isn't a real fail.
        audit.Failures.Clear();

        var note = string.Format(_language["verifyV2CorrectedNote"], oldStatus, fullStatus);
        audit.InterpretationNotes.Insert(0, note);
        audit.ComparisonSummaryText = note + Environment.NewLine + Environment.NewLine + audit.ComparisonSummaryText;

        foreach (var vm in sessionGroup.CameraRows)
        {
            if (vm.Row.AuditStatus == CameraAuditStatus.Fail)
            {
                vm.Row.AuditStatus = fullStatus;
                // Same reasoning as audit.Failures.Clear() above — these are the same false-positive
                // entries (e.g. "Scientific timing status: FAIL"), just duplicated onto the per-row
                // detail panel's "Failures" section instead of the session-level summary.
                vm.Row.ErrorMessages.Clear();
                // vm.Row.Details/Recommendation are baked into strings once at initial scan time
                // (VerificationReportMapper.ToTableRow, before this correction ever runs) from the
                // same stale FAIL verdict — e.g. Details "Scientific timing status: FAIL" and
                // Recommendation "Review failure messages and re-record if the file is unusable."
                // Correcting only AuditStatus/ErrorMessages left these two CSV/JSON export columns
                // still contradicting the now-correct WARNING/PASS status.
                vm.Row.Details = normalized == "WARNING" ? _language["verifyDetailsSeePanel"] : _language["verifyDetailsOk"];
                vm.Row.Recommendation = normalized == "WARNING"
                    ? _language["verifyRecReviewWarnings"]
                    : _language["verifyRecNoAction"];
                vm.Row.RecommendedAction = vm.Row.Recommendation;
                // vm.Row.DetailText (the big multi-section report shown in "Selected Video Detail"
                // when a row is clicked) is also baked in at that same initial scan time — its
                // "Result: FAIL", "Failures:", "Reason:", and "Recommendation:" sections still read
                // the stale pre-correction verdict verbatim even though the table row above now
                // shows the corrected status. Rewriting those embedded sections in place would be
                // fragile; instead, prepend the same kind of corrective note already used for
                // audit.ComparisonSummaryText above, so anyone reading the raw detail immediately
                // sees why it still says FAIL further down.
                if (!string.IsNullOrWhiteSpace(vm.Row.DetailText))
                {
                    var rowNote = string.Format(_language["verifyV2RowCorrectedNote"], CameraAuditStatus.Fail, fullStatus);
                    vm.Row.DetailText = rowNote + Environment.NewLine + Environment.NewLine + vm.Row.DetailText;
                }
            }
        }
        // Rebuild viewmodels so their cached brushes reflect the corrected status.
        for (var i = 0; i < sessionGroup.CameraRows.Count; i++)
            sessionGroup.CameraRows[i] = new VerificationTableRowViewModel(sessionGroup.CameraRows[i].Row);

        AppendLog(string.Format(_language["verifyV2CorrectedFalsePositive"], audit.SessionLabel, fullStatus));
        return true;
    }

    /// <summary>
    /// Maps <see cref="SessionGroupViewModel.NormalizeSessionResult"/>'s output ("PASS"/"WARNING"/
    /// "FAIL") to a <see cref="VerificationVerdict"/>. Deliberately NOT the same vocabulary as
    /// <see cref="CameraAuditStatus"/>'s own constants (its warning constant is the string
    /// "PASS_WITH_WARNING", not "WARNING") — code comparing a normalized result against
    /// <c>CameraAuditStatus.PassWithWarning</c> directly will never match and silently miscounts
    /// every warning as a pass. Route all normalized-result comparisons through this helper instead
    /// of re-deriving the mapping ad hoc.
    /// </summary>
    private static VerificationVerdict NormalizedResultToVerdict(string normalized) => normalized switch
    {
        CameraAuditStatus.Fail => VerificationVerdict.Fail,
        "WARNING" => VerificationVerdict.Warning,
        _ => VerificationVerdict.Pass
    };

    /// <summary>
    /// Corrects the top-level summary cards and detail text the same way
    /// <see cref="ReconcileV2SessionVerdict"/> corrects per-session/per-row status — those cards
    /// are populated by <see cref="ApplySummary"/> from <c>report.Summary</c>/<c>report.SessionResult</c>
    /// (legacy, schema-blind aggregates computed BEFORE any V2 enrichment runs), so fixing only the
    /// session list left the top cards showing the original wrong FAIL. Only activates when every
    /// scanned video in this folder is confirmed VideoEngineV2 — a mixed legacy/V2 folder is left
    /// alone entirely, since the legacy aggregate might be genuinely correct for the legacy videos.
    /// </summary>
    /// <summary>Tallies corrected per-row verdicts across all current session groups. Shared by
    /// <see cref="ReconcileV2Summary"/> (top cards) and <see cref="BuildSummaryText"/> (detail text)
    /// so both agree with each other and with the per-row table.</summary>
    private (int found, int passed, int warned, int failed) CountV2RowVerdicts()
    {
        int found = 0, passed = 0, warned = 0, failed = 0;
        foreach (var sessionGroup in _sessionGroups)
            foreach (var vm in sessionGroup.CameraRows)
            {
                found++;
                var normalized = SessionGroupViewModel.NormalizeSessionResult(vm.Row.AuditStatus);
                if (normalized == CameraAuditStatus.Fail) failed++;
                else if (normalized == "WARNING") warned++;
                else passed++;
            }
        return (found, passed, warned, failed);
    }

    private void ReconcileV2Summary(int totalEntriesScanned)
    {
        if (_lastReport == null) return;
        if (_v2MetadataByPath.Count < totalEntriesScanned) return; // mixed/partial V2 — don't guess

        // DetailBox's per-session text is built from the same RecordingSessionAuditResult objects
        // ReconcileV2SessionVerdict already mutated in place (report.SessionAudits and
        // _sessionGroups[*].Audit are the same references) — re-running ApplySummary picks that up
        // for free. This also resets the cards below to their still-stale values, which is fine:
        // they're immediately overridden afterward.
        ApplySummary(_lastReport);

        var (found, passed, warned, failed) = CountV2RowVerdicts();
        if (found == 0) return;

        CardFoundValue.Text = found.ToString();
        CardPassValue.Text  = passed.ToString();
        CardWarnValue.Text  = warned.ToString();
        CardFailValue.Text  = failed.ToString();
        var overallVerdict = failed > 0 ? VerificationVerdict.Fail : warned > 0 ? VerificationVerdict.Warning : VerificationVerdict.Pass;
        CardOverallValue.Text = FriendlyVerdict(overallVerdict);
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(overallVerdict);

        // Everything above only ever corrected the on-screen Card *Text* — never
        // _lastReport.Summary itself. Export TXT/JSON/CSV (VerificationReportWriter) and
        // "Export All to Folder" all serialize report.Summary directly, so without this, every
        // export from a V2 session would show the original schema-blind FAIL/wrong pass-warn-fail
        // counts even after the screen was showing the corrected WARNING — the exact same
        // screen-vs-export contradiction previously fixed for individual rows (ReconcileV2SessionVerdict
        // already mutates the real VerificationTableRow.AuditStatus field, not just a Text property;
        // the summary cards were the one place that correction never made it past the widget).
        _lastReport.Summary.VideosPassed = passed;
        _lastReport.Summary.VideosWarning = warned;
        _lastReport.Summary.VideosFailed = failed;
        _lastReport.Summary.OverallVerdict = overallVerdict;
        _lastReport.SessionResult.OverallResult = overallVerdict;

        // report.Summary.SessionMessages / report.SessionResult.SessionMessages are baked-in text
        // snapshotted by VideoVerificationService at initial-scan time (STABLE_CORE_V1, before this
        // V2 correction runs) — e.g. "test1_...: FAIL" plus stale failure bullets ("individual camera
        // audit failed", "Inter-camera frame difference too large"). BuildSummaryText already hides
        // these on-screen for a fully-V2 session (see its `fullyV2` branch below), but the raw lists
        // themselves were never corrected, so JSON/CSV export kept serializing the stale FAIL text
        // right next to the now-correct OverallVerdict/counts above — the same screen-vs-export gap
        // this method exists to close for everything else.
        var correctedRollup = string.Format(_language["verifyV2SummaryCorrected"], found, passed, warned, failed);
        _lastReport.Summary.SessionMessages.Clear();
        _lastReport.Summary.SessionMessages.Add(correctedRollup);
        _lastReport.SessionResult.SessionMessages.Clear();
        _lastReport.SessionResult.SessionMessages.Add(correctedRollup);

        // Session Duration / FPS Spread cards can independently show a raw legacy FAIL from the
        // same schema-blindness, regardless of how many sessions were scanned (a single-session
        // false positive is just as wrong as a multi-session one) — downgrade to the worst
        // *corrected* per-session status whenever that happens. Left untouched when it already
        // shows something else (e.g. a genuinely-computed "OK"), since that value isn't known to
        // be wrong.
        // Full CameraAuditStatus vocabulary throughout (matches ApplySummary's non-corrected values:
        // s.SessionDurationMatch/report.Summary.SessionDurationMatch always hold "PASS"/
        // "PASS_WITH_WARNING"/"FAIL", never a bare "WARNING" token) — the card text is shortened via
        // ShortSessionStatus below only when _simpleTableView requests it, same as ApplySummary does.
        var worstSession = CameraAuditStatus.Pass;
        foreach (var g in _sessionGroups)
        {
            var n = SessionGroupViewModel.NormalizeSessionResult(g.Audit.SessionStatus);
            if (n == CameraAuditStatus.Fail) { worstSession = CameraAuditStatus.Fail; break; }
            if (n == "WARNING") worstSession = CameraAuditStatus.PassWithWarning;
        }
        var worstSessionCardText = _simpleTableView ? ShortSessionStatus(worstSession) : worstSession;
        if (CardDurSpreadValue.Text == CameraAuditStatus.Fail) CardDurSpreadValue.Text = worstSessionCardText;
        if (CardFpsSpreadValue.Text == CameraAuditStatus.Fail) CardFpsSpreadValue.Text = worstSessionCardText;
        if (_lastReport.Summary.SessionDurationMatch == CameraAuditStatus.Fail)
            _lastReport.Summary.SessionDurationMatch = worstSession;

        // Scientific Timing Confidence — worst-case across all V2 metadata's own already-computed
        // timing.timingConfidence/verification.interCameraTimingConfidence (see V2RecordingMetadata
        // doc comments). Only overridden when every value is non-empty and none is a real FAIL.
        var timingValues = _v2MetadataByPath.Values
            .Select(m => m.InterCameraTimingConfidence is { Length: > 0 } ic ? ic : m.TimingConfidence)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        if (timingValues.Count == _v2MetadataByPath.Count)
        {
            var normalizedTimingValues = timingValues.Select(SessionGroupViewModel.NormalizeSessionResult).ToList();
            if (!normalizedTimingValues.Contains(CameraAuditStatus.Fail))
            {
                var isWarn = normalizedTimingValues.Contains("WARNING");
                // ScientificTimingStatus (card text and SessionResult field) uses the full
                // CameraAuditStatus vocabulary ("PASS"/"PASS_WITH_WARNING"/"FAIL") everywhere else —
                // see VideoVerificationService.cs's own assignments to the same field. Writing the
                // short NormalizeSessionResult token ("WARNING") directly into it was the same
                // vocabulary-mismatch bug fixed in ReconcileV2SessionVerdict above.
                var correctedStatus = isWarn ? CameraAuditStatus.PassWithWarning : CameraAuditStatus.Pass;
                CardTimingConfidenceValue.Text = correctedStatus;
                // Same card-only gap as VideosPassed/Warning/Failed above: report.SessionResult's own
                // ScientificTimingStatus/SessionScientificTimingConfidence are separate data fields
                // (STABLE_CORE_V1, computed once at initial scan) that TXT/JSON/CSV export reads
                // directly — correcting only the Card.Text left every export still showing the raw
                // FAIL/FAILED values this section just proved wrong.
                if (_lastReport.SessionResult.ScientificTimingStatus == CameraAuditStatus.Fail)
                    _lastReport.SessionResult.ScientificTimingStatus = correctedStatus;
                if (string.Equals(_lastReport.SessionResult.SessionScientificTimingConfidence, ScientificTimingConfidence.Failed, StringComparison.OrdinalIgnoreCase))
                    _lastReport.SessionResult.SessionScientificTimingConfidence =
                        isWarn ? ScientificTimingConfidence.Medium : ScientificTimingConfidence.High;
            }
        }

        // Timestamp CSV Status — every V2 recording writes this CSV (VideoEngineSettings.WriteTimestampCsv
        // defaults true); the legacy card shows "MISSING/INCOMPLETE" only because it's reading a
        // top-level legacy field V2 JSON doesn't have, not because the CSV is actually missing.
        var csvCount = _v2MetadataByPath.Values.Count(m => m.TimestampCsvWritten && m.TimestampCsvRows > 0);
        if (csvCount == _v2MetadataByPath.Count)
            CardTimestampCsvValue.Text = string.Format(_language["verifyTimestampCsvPresentCount"], csvCount);

        // Recording Mode / Scientific Timing Source — VideoVerificationService only ever populates
        // report.SessionResult.SessionTimingMode/RecommendedTrimSource for a single-session scan
        // (report.SessionAudits.Count == 1); a multi-session batch like this one leaves both blank,
        // showing "-" even when every scanned video agrees on the same real values. Same
        // corrected-when-unanimous approach as the cards above.
        if (_v2MetadataByPath.Values.All(m => m.IsOriginalCaptureMode))
            CardTimingModeValue.Text = DisplayTimingMode(OriginalCaptureAuditPolicy.Mode);
        if (csvCount == _v2MetadataByPath.Count)
            CardTrimSourceValue.Text = _language["verifyTimestampCsvShort"];

        // Frame Integrity — session.OriginalFramesOnly (ApplyOriginalCaptureSessionCardFields,
        // STABLE_CORE_V1) is computed from the legacy CameraMetadataRecord's FrameCount/
        // DuplicatedFrames/PlaceholderFrames, which the schema-blind parser never populates for a
        // V2 recording — so this card shows the vague "Review details" fallback even when every
        // camera confirms Original Capture Mode, which is V2's own architectural guarantee of real
        // frames only (no duplicate/placeholder padding by design, not merely unmeasured). Same
        // corrected-when-unanimous approach as Recording Mode above.
        if (_v2MetadataByPath.Values.All(m => m.IsOriginalCaptureMode))
        {
            CardOriginalFramesValue.Text = _language["verifyRealFramesOnly"];
            _lastReport.SessionResult.OriginalFramesOnly = true;
        }

        // Hardware Calibration Lock — ApplySummary (called at the top of this method) always sets
        // this to the "(legacy pipeline)" placeholder unconditionally, because the legacy report
        // format has no concept of V2's per-camera environmentalLock JSON. Unlike every other card
        // above, nothing ever corrected this one for a V2 session, so it kept showing "(legacy
        // pipeline)" even for a 100%-V2 recording — same real data ApplyV2StandaloneResults already
        // reads correctly in the no-ffprobe fallback path, just never applied here.
        var envLockSlots = _v2MetadataByPath.Values.Where(m => m.EnvironmentalLockActive).ToList();
        CardEnvLockValue.Text = envLockSlots.Count == 0
            ? _language["verifyNotActive"]
            : string.Format(_language["verifyActiveSlotsTemplate"], envLockSlots.Count, envLockSlots.Count == 1 ? "" : "s");
        CardEnvLockValue.Foreground = envLockSlots.Count > 0
            ? System.Windows.Media.Brushes.LightGreen
            : (System.Windows.Media.Brush)FindResource("AppSecondaryTextBrush");

        AppendLog(string.Format(_language["verifyV2SummaryCorrected"], found, passed, warned, failed));
    }

    /// <summary>
    /// video_audit_report.txt is auto-saved by <c>VideoVerificationService.VerifyAsync</c> itself
    /// (STABLE_CORE_V1), before <see cref="EnrichV2SessionGroups"/>/<see cref="EnrichV2VideoMetadata"/>/
    /// <see cref="ReconcileV2Summary"/> ever run — so the copy already sitting in the session folder
    /// is always the stale, schema-blind FAIL version the moment Verify All finishes, until the user
    /// separately clicks an Export button. Re-saves it here, immediately after those corrections,
    /// using the now-corrected <see cref="_lastReport"/>, so the file on disk always matches what
    /// the page just displayed. Best-effort: a failure here must not interrupt verification, matching
    /// the try/catch already around the original auto-save in VideoVerificationService.
    /// </summary>
    private async Task ResaveAuditReportAfterEnrichment(string folder)
    {
        if (_lastReport == null) return;
        try
        {
            var auditReportPath = Path.Combine(folder, "video_audit_report.txt");
            await new VerificationReportWriter(_language).ExportVideoAuditReportAsync(_lastReport, auditReportPath);
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_language["verifyLogCouldNotSaveAuditReport"], ex.Message));
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
            ? string.Format(_language["verifySessionsAuditedSeparately"], sessionCount)
            : s.FpsSpreadDisplay;
        var sess = report.SessionResult;
        CardTimingConfidenceValue.Text = sess.SessionScientificTimingConfidence;
        CardTimingModeValue.Text = DisplayTimingMode(sess.SessionTimingMode);
        CardOriginalFramesValue.Text = sess.OriginalFramesOnly ? _language["verifyRealFramesOnly"] : _language["verifyReviewDetails"];
        CardDuplicateFramesValue.Text = sess.DuplicateFramesTotal.ToString();
        CardPlaceholderFramesValue.Text = sess.PlaceholderFramesTotal.ToString();
        CardQueueDropsValue.Text = sess.WriterQueueDropsTotal.ToString();
        CardTimestampCsvValue.Text = string.IsNullOrWhiteSpace(sess.TimestampCsvStatus) ? "-" : sess.TimestampCsvStatus;
        CardTrimSourceValue.Text = string.IsNullOrWhiteSpace(sess.RecommendedTrimSource) ? "-" : ShortTimingSource(sess.RecommendedTrimSource);
        CardEnvLockValue.Text = _language["verifyLegacyPipelineValue"];
        CardEnvLockValue.Foreground = (System.Windows.Media.Brush)FindResource("AppSecondaryTextBrush");
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
        _ => CurrentLanguage?["verifyNotVerifiedYet"] is { Length: > 0 } v ? v : "Not verified yet"
    };

    private static string ShortTimingSource(string value) =>
        string.Equals(value, "PerFrameCaptureTimestamps", StringComparison.OrdinalIgnoreCase)
            ? (CurrentLanguage?["verifyTimestampCsvShort"] is { Length: > 0 } v ? v : "Timestamp CSV")
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
            sb.AppendLine($"--- {string.Format(_language["verifySessionLabel"], audit.SessionLabel)} ---");
            sb.AppendLine(_simpleTableView
                ? SessionGroupViewModel.BuildFriendlySessionSummary(audit)
                : audit.ComparisonSummaryText);
            sb.AppendLine();
        }

        // report.SessionResult ("sess") is a SEPARATE whole-scan legacy aggregate, independently
        // re-derived from the same schema-blind logic as report.SessionAudits — including a raw
        // sess.SessionMessages bullet list (e.g. "cam1: individual camera audit failed") that is
        // NOT covered by ReconcileV2SessionVerdict's per-session correction above, and would
        // contradict it word-for-word. When every scanned video is confirmed VideoEngineV2, the
        // per-session breakdown above is already authoritative and correct — show a short rollup
        // instead of this redundant, misleading legacy block. Untouched for legacy/mixed folders.
        bool fullyV2 = report.Videos.Count > 0 && _v2MetadataByPath.Count >= report.Videos.Count;
        sb.AppendLine($"--- {_language["verifySessionTitle"]} ---");
        if (fullyV2)
        {
            var (found, passed, warned, failed) = CountV2RowVerdicts();
            var overallVerdict = failed > 0 ? VerificationVerdict.Fail : warned > 0 ? VerificationVerdict.Warning : VerificationVerdict.Pass;
            sb.AppendLine($"{_language["verifySessionOverall"]}: {FriendlyVerdict(overallVerdict)}");
            sb.AppendLine(string.Format(_language["verifyV2SummaryCorrected"], found, passed, warned, failed));
        }
        else
        {
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
            sb.AppendLine($"{_language["verifyScientificTimingConfidence"]}: {sess.SessionScientificTimingConfidence}");
            sb.AppendLine($"{_language["verifyRecordingModeLabel"]}: {DisplayTimingMode(sess.SessionTimingMode)}");
            sb.AppendLine($"{_language["verifyFrameIntegrityGroupTitle"]}: {(sess.OriginalFramesOnly ? _language["verifyRealFramesOnly"] : _language["verifyReviewDetails"])}");
            sb.AppendLine($"{_language["verifyDuplicateFramesLabel"]}: {sess.DuplicateFramesTotal}");
            sb.AppendLine($"{_language["verifyPlaceholderFramesLabel"]}: {sess.PlaceholderFramesTotal}");
            sb.AppendLine($"{_language["verifyWriterDropsLabel"]}: {sess.WriterQueueDropsTotal}");
            sb.AppendLine($"{_language["verifyTimestampCsvStatusLabel"]}: {sess.TimestampCsvStatus}");
            sb.AppendLine($"{_language["verifyScientificTimingSource"]}: {ShortTimingSource(sess.RecommendedTrimSource)}");
            if (string.Equals(sess.SessionScientificTimingConfidence, ScientificTimingConfidence.High, StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(_language["verifyHighConfidenceMessage"]);
            sb.AppendLine(_language["verifyOriginalCaptureExplanation"]);
            sb.AppendLine(_language["verifyNoteFrameCountDifference"]);
            sb.AppendLine(_language["verifyUseTimestampCsvForTimingNote"]);
            if (sess.ShowContainerWallClockWarning)
                sb.AppendLine(sess.ContainerWallClockWarning);
            var timingMessage = report.Videos
                .Select(v => v.ScientificTimingMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            if (!string.IsNullOrWhiteSpace(timingMessage))
                sb.AppendLine($"{_language["verifyScientificTimingMessage"]}: {timingMessage}");
            foreach (var m in sess.SessionMessages)
                sb.AppendLine($"  • {m}");
        }

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
        CardOverallValue.Text = _language["verifyNotVerifiedYet"];
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(VerificationVerdict.NotChecked);
        CardFoundValue.Text = "0";
        CardPassValue.Text = "0";
        CardWarnValue.Text = "0";
        CardFailValue.Text = "0";
        CardSourceValue.Text = "—";
        CardDurSpreadValue.Text = "—";
        CardFpsSpreadValue.Text = "—";
        CardTimingConfidenceValue.Text = _language["verifyPending"];
        CardTimingModeValue.Text = "—";
        CardOriginalFramesValue.Text = _language["verifyPending"];
        CardDuplicateFramesValue.Text = "—";
        CardPlaceholderFramesValue.Text = "—";
        CardQueueDropsValue.Text = "—";
        CardTimestampCsvValue.Text = _language["verifyPending"];
        CardTrimSourceValue.Text = _language["verifyPending"];
        CardEnvLockValue.Text = "—";
        ContainerDurationWarningBox.Visibility = Visibility.Collapsed;
        RefreshDeepVerifySummary();
        DetailBox.Text = _language["verifyDetailEmpty"];
    }

    private void RefreshSummaryScanned(int entriesCount)
    {
        RefreshSummaryEmpty();
        SummaryCardsPanel.Visibility = entriesCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CardFoundValue.Text = entriesCount.ToString();
        CardOverallValue.Text = entriesCount > 0 ? _language["verifyNotVerifiedYet"] : NoVideoMessage;
        CardOverallValue.Foreground = VerificationUiBrushes.ForVerdict(VerificationVerdict.NotChecked);
        CardPassValue.Text = "—";
        CardWarnValue.Text = "—";
        CardFailValue.Text = "—";
        CardTimingConfidenceValue.Text = _language["verifyPending"];
        CardOriginalFramesValue.Text = _language["verifyPending"];
        CardTimestampCsvValue.Text = _language["verifyPending"];
        CardTrimSourceValue.Text = _language["verifyPending"];
        CardEnvLockValue.Text = "—";
        DetailBox.Text = entriesCount > 0
            ? _language["verifyScanFoundVideosPending"]
            : NoVideoMessage;
    }

    // ── Deep Verify summary card ────────────────────────────────────────────────

    private void RefreshDeepVerifySummary()
    {
        if (_deepVerifyResults.Count == 0)
        {
            CardDeepVerifyStatusValue.Text = _language["verifyDeepVerifyNotRun"];
            CardDeepVerifyStatusValue.Foreground = VerificationUiBrushes.ForVerdict(VerificationVerdict.NotChecked);
            CardDeepVerifyFilesValue.Text = "—";
            CardDeepVerifyDuplicatesValue.Text = "—";
            CardDeepVerifyWorstFileValue.Text = "—";
            return;
        }

        var errorCount = _deepVerifyResults.Values.Count(r => !r.Success);
        var totalDuplicates = _deepVerifyResults.Values.Sum(r => r.DuplicateFrameCount);
        var worst = _deepVerifyResults.Values
            .Where(r => r.Success)
            .OrderByDescending(r => r.DuplicateFrameCount)
            .FirstOrDefault();

        CardDeepVerifyStatusValue.Text = errorCount > 0
            ? string.Format(_language["verifyDeepVerifyStatusErrors"], errorCount)
            : totalDuplicates > 0
                ? _language["verifyDeepVerifyStatusWarn"]
                : _language["verifyDeepVerifyStatusPass"];
        CardDeepVerifyStatusValue.Foreground = errorCount > 0 || totalDuplicates > 0
            ? VerificationUiBrushes.ForVerdict(VerificationVerdict.Warning)
            : VerificationUiBrushes.ForVerdict(VerificationVerdict.Pass);
        CardDeepVerifyFilesValue.Text = _deepVerifyResults.Count.ToString();
        CardDeepVerifyDuplicatesValue.Text = totalDuplicates.ToString();
        CardDeepVerifyWorstFileValue.Text = worst is { DuplicateFrameCount: > 0 }
            ? $"{Path.GetFileName(worst.FilePath)} ({worst.DuplicateFrameCount}/{worst.TotalFramesHashed})"
            : "—";
    }

    private async void DeepVerifyBtn_Click(object sender, RoutedEventArgs e) => await RunDeepVerifyAsync();

    private async Task RunDeepVerifyAsync()
    {
        var folder = FolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            AppendLog(_language["verifySelectFolder"]);
            return;
        }

        if (_service == null || _deepVerifyService == null || _isBusy) return;

        if (!_deepVerifyService.IsAvailable)
        {
            UpdateFfmpegWarning();
            AppendLog(_language["verifyFfmpegMissing"]);
            return;
        }

        SetBusy(true, _language["verifyDeepVerifying"]);
        _cts = new CancellationTokenSource();
        try
        {
            var entries = await Task.Run(() => _service.Scan(folder), _cts.Token);
            if (entries.Count == 0)
            {
                AppendLog(_language["verifyNoVideos"]);
                return;
            }

            AppendLog(string.Format(_language["verifyDeepVerifyStarted"], entries.Count));

            var index = 0;
            foreach (var entry in entries)
            {
                index++;
                ProgressText.Text = string.Format(_language["verifyDeepVerifyProgress"], index, entries.Count, entry.FileName);
                AppendLog(string.Format(_language["verifyDeepVerifyProgress"], index, entries.Count, entry.FileName));

                var result = await _deepVerifyService.VerifyFileAsync(entry.FullPath, _cts.Token);
                _deepVerifyResults[entry.FullPath] = result;

                AppendLog(!result.Success
                    ? string.Format(_language["verifyDeepVerifyFileError"], entry.FileName, result.Error)
                    : string.Format(_language["verifyDeepVerifyFileDone"],
                        entry.FileName, result.TotalFramesHashed, result.DuplicateFrameCount, result.Elapsed.TotalSeconds));

                RefreshDeepVerifySummary();
            }

            AppendLog(_language["verifyDeepVerifyComplete"]);
        }
        catch (OperationCanceledException)
        {
            AppendLog(_language["verifyCancelled"]);
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_language["verifyDeepVerifyError"], ex.Message));
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private string NoVideoMessage => _language["verifyNoVideoMessage"];

    private bool _isBusy;

    private void SetBusy(bool busy, string message)
    {
        _isBusy = busy;
        ScanBtn.IsEnabled = !busy;
        VerifyBtn.IsEnabled = !busy;
        VerifyAllBtn.IsEnabled = !busy;
        DeepVerifyBtn.IsEnabled = !busy;
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
