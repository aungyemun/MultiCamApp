////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MultiCamApp.Capture;
using MultiCamApp.Experiment;
using MultiCamApp.Metadata;
using MultiCamApp.Recording;

namespace MultiCamApp.Verification;

public sealed class CameraMetadataRecord
{
    public HashSet<string> PresentMetadataFields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasField(params string[] names) => names.Any(PresentMetadataFields.Contains);
    public string AppVersion { get; set; } = "";
    public int BuildNumber { get; set; }
    public string ReleaseStage { get; set; } = "";
    public string SessionName { get; set; } = "";
    public string CameraSlot { get; set; } = "";
    public string CameraDeviceName { get; set; } = "";
    public string? Resolution { get; set; }
    public string? RequestedResolution { get; set; }
    public string? SelectedResolution { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public double RequestedFps { get; set; }
    public double SelectedDeviceFps { get; set; }
    public double RecordingWriterFps { get; set; }
    public double WriterFps { get; set; }
    public double ContainerFps { get; set; }
    public double MeasuredCameraFps { get; set; }
    public double EffectivePlaybackFps { get; set; }
    public double WallClockDurationSeconds { get; set; }
    public double ContainerVsWallClockDifferenceSeconds { get; set; }
    public double InterCameraStartOffsetMs { get; set; }
    public double InterCameraStopOffsetMs { get; set; }
    public double ActualFps { get; set; }
    public string? Codec { get; set; }
    public string? ContainerFormat { get; set; }
    public string? VideoSubtype { get; set; }
    public string? FilePath { get; set; }
    public double? DurationSeconds { get; set; }
    public long FrameCount { get; set; }
    public long FramesCaptured { get; set; }
    public long DuplicatedFrames { get; set; }
    public long PlaceholderFrames { get; set; }
    public long WriterQueueDrops { get; set; }
    public double WallDurationSeconds { get; set; }
    public double FrameBasedDurationSeconds { get; set; }
    public double ContainerDurationSeconds { get; set; }
    public double TimestampDriftSeconds { get; set; }
    public double AverageCaptureIntervalMs { get; set; }
    public double MinCaptureIntervalMs { get; set; }
    public double MaxCaptureIntervalMs { get; set; }
    public double CaptureJitterMs { get; set; }
    public double CaptureIntervalMeanMs { get; set; }
    public double CaptureIntervalMedianMs { get; set; }
    public double CaptureIntervalMinMs { get; set; }
    public double CaptureIntervalMaxMs { get; set; }
    public double CaptureIntervalP95Ms { get; set; }
    public double CaptureIntervalP99Ms { get; set; }
    public double CaptureIntervalStdMs { get; set; }
    public long CaptureIntervalCount { get; set; }
    public double MeasuredCameraFpsFromFirstLastFrame { get; set; }
    public double MeasuredCameraFpsFromMeanInterval { get; set; }
    public double ExpectedIntervalMs { get; set; }
    public double RequestedExpectedIntervalMs { get; set; }
    public double MeanIntervalErrorMs { get; set; }
    public double AbsoluteMeanIntervalErrorMs { get; set; }
    public long LongGapCount { get; set; }
    public long ShortGapCount { get; set; }
    public long SevereLongGapCount { get; set; }
    public double JitterScoreMs { get; set; }
    public string FpsStabilityGrade { get; set; } = "";
    public string? CaptureIntervalStatsMessage { get; set; }
    public string? ScientificTimingMessage { get; set; }
    public long MaxConsecutiveLateFrames { get; set; }
    public long MaxConsecutiveNoFrame { get; set; }
    public long InterCameraFrameDiff { get; set; }
    public double InterCameraDurationDiffSec { get; set; }
    public string? RecordingApi { get; set; }
    public string? Backend { get; set; }
    public string? DeviceId { get; set; }
    public int DeviceIndex { get; set; } = -1;
    public string SessionTitleOriginal { get; set; } = "";
    public string SessionFolderName { get; set; } = "";
    public bool ExperimentMode { get; set; }
    public double TargetDurationSeconds { get; set; }
    public double TargetFps { get; set; }
    public long ExpectedFrames { get; set; }
    public long DroppedFrames { get; set; }
    public long DuplicateFrames { get; set; }
    public bool ConstantFrameCountMode { get; set; }
    public double AverageFrameIntervalMs { get; set; }
    public double FrameIntervalStdDevMs { get; set; }
    public string ExperimentResult { get; set; } = "";
    public string RecordingMode { get; set; } = "";
    public double MinimumAnalysisDurationSeconds { get; set; }
    public double PlannedRecordingDurationSeconds { get; set; }
    public bool UsableFor10MinAnalysis { get; set; }
    public DateTime? RecordingStartLocal { get; set; }
    public DateTime? RecordingStopLocal { get; set; }
    public string? CameraHardwareId { get; set; }
    public string? ScientificTimingStatus { get; set; }
    public string RecommendedAction { get; set; } = "";
    public bool AutoFocusRequested { get; set; }
    public bool AutoFocusApplyAttempted { get; set; }
    public bool? AutoFocusApplySucceeded { get; set; }
    public string AutoFocusReadbackValue { get; set; } = "unavailable";
    public bool? ManualFocusSupported { get; set; }
    public double? ManualFocusRequestedValue { get; set; }
    public string ManualFocusReadbackValue { get; set; } = "unavailable";
    public string FocusControlMode { get; set; } = "unavailable";
    public string FocusWarning { get; set; } = "";
    public string RecordingTimingMode { get; set; } = "";
    public bool OriginalCaptureMode { get; set; }
    public double SessionStartMonotonicSeconds { get; set; }
    public double CameraStartMonotonicSeconds { get; set; }
    public DateTime? FirstFrameUtcTime { get; set; }
    public DateTime? LastFrameUtcTime { get; set; }
    public DateTime? WriterClosedUtcTime { get; set; }
    public double FirstFrameMonotonicSeconds { get; set; }
    public double LastFrameMonotonicSeconds { get; set; }
    public double StopRequestedMonotonicSeconds { get; set; }
    public double WriterClosedMonotonicSeconds { get; set; }
    public string FrameTimestampCsvPath { get; set; } = "";
    public bool FrameTimestampCsvWritten { get; set; }
    public long FrameTimestampCsvRowCount { get; set; }
    public DateTime? FirstFrameCaptureUtcTime { get; set; }
    public DateTime? LastFrameCaptureUtcTime { get; set; }
    public double FirstFrameCaptureMonotonicSec { get; set; }
    public double LastFrameCaptureMonotonicSec { get; set; }
    public double FirstToLastFrameDurationSec { get; set; }
    public string TrimRecommendedTimeSource { get; set; } = "";
    public string TrimWarning { get; set; } = "";
    public string ScientificTrimStartReference { get; set; } = "";
    public string ScientificTrimEndReference { get; set; } = "";
    public bool SupportsTimestampBasedTrimming { get; set; }
    public string? ComputerName { get; set; }
    public string? CpuName { get; set; }
    public double RamGb { get; set; }
    public RecordingDiagnosticsMetadataSummary? RecordingDiagnostics { get; set; }
}

public sealed class SessionSummaryRecord
{
    public string SessionName { get; set; } = "";
    public string SessionTitleOriginal { get; set; } = "";
    public string SessionFolderName { get; set; } = "";
    public string Folder { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public List<CameraMetadataRecord> Cameras { get; } = [];
}

public static partial class MetadataParser
{
    public static CameraMetadataRecord? ParseCameraMetadataFile(string path)
    {
        if (!File.Exists(path)) return null;
        var dict = ParseKeyValueLines(File.ReadAllLines(path));
        var record = new CameraMetadataRecord
        {
            AppVersion = Get(dict, "App Version") ?? Get(dict, "appVersion") ?? "",
            ReleaseStage = Get(dict, "Release Stage") ?? Get(dict, "releaseStage") ?? "",
            SessionName = Get(dict, "Session name") ?? Get(dict, "sessionName") ?? "",
            CameraSlot = Get(dict, "Camera slot") ?? "",
            CameraDeviceName = Get(dict, "Camera device name") ?? "",
            Resolution = Get(dict, "Resolution"),
            RequestedResolution = Get(dict, "Requested resolution"),
            SelectedResolution = Get(dict, "Selected resolution"),
            Codec = Get(dict, "Codec"),
            ContainerFormat = Get(dict, "Container"),
            VideoSubtype = Get(dict, "Video subtype"),
            FilePath = Get(dict, "File path"),
            RecordingApi = Get(dict, "Recording API"),
            CameraHardwareId = Get(dict, "Camera hardware ID"),
            Backend = Get(dict, "Backend"),
            DeviceId = Get(dict, "Device ID"),
            ScientificTimingStatus = Get(dict, "Scientific timing status"),
            ScientificTimingMessage = Get(dict, "Scientific timing message"),
            AutoFocusRequested = ReadTextNullableBool(dict, "AutoFocusRequested") ?? false,
            AutoFocusApplyAttempted = ReadTextNullableBool(dict, "AutoFocusApplyAttempted") ?? false,
            AutoFocusApplySucceeded = ReadTextNullableBool(dict, "AutoFocusApplySucceeded"),
            AutoFocusReadbackValue = Get(dict, "AutoFocusReadbackValue") ?? "unavailable",
            ManualFocusSupported = ReadTextNullableBool(dict, "ManualFocusSupported"),
            ManualFocusRequestedValue = ReadTextNullableDouble(dict, "ManualFocusRequestedValue"),
            ManualFocusReadbackValue = Get(dict, "ManualFocusReadbackValue") ?? "unavailable",
            FocusControlMode = Get(dict, "FocusControlMode") ?? "unavailable",
            FocusWarning = Get(dict, "FocusWarning") ?? "",
            RecordingTimingMode = Get(dict, "recordingTimingMode") ?? Get(dict, "Recording timing mode") ?? "",
            CaptureIntervalStatsMessage = Get(dict, "Capture interval stats note")
        };
        record.PresentMetadataFields.UnionWith(dict.Keys);
        if (int.TryParse(Get(dict, "Build Number") ?? Get(dict, "buildNumber"), out var buildNumber))
            record.BuildNumber = buildNumber;
        if (int.TryParse(Get(dict, "Device index") ?? Get(dict, "DirectShow index") ?? Get(dict, "deviceIndex"), out var deviceIndex))
            record.DeviceIndex = deviceIndex;

        if (TryParsePixels(Get(dict, "Pixels"), out var w, out var h))
        {
            record.PixelWidth = w;
            record.PixelHeight = h;
        }
        else if (!string.IsNullOrEmpty(record.Resolution))
        {
            TryParsePixels(record.Resolution.Replace(" ", ""), out w, out h);
            record.PixelWidth = w;
            record.PixelHeight = h;
        }

        if (double.TryParse(Get(dict, "Requested FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rfps))
            record.RequestedFps = rfps;
        if (double.TryParse(Get(dict, "Selected device FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var sdfps))
            record.SelectedDeviceFps = sdfps;
        if (double.TryParse(Get(dict, "Recording writer FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rwfps))
            record.RecordingWriterFps = rwfps;
        if (double.TryParse(Get(dict, "Writer FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wfps))
            record.WriterFps = wfps;
        if (double.TryParse(Get(dict, "Container FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cfps))
            record.ContainerFps = cfps;
        if (double.TryParse(Get(dict, "Measured camera FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var mcfps))
            record.MeasuredCameraFps = mcfps;
        if (double.TryParse(Get(dict, "Measured writer FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var mwfps))
            record.ActualFps = mwfps;
        else if (double.TryParse(Get(dict, "Actual FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var afps))
            record.ActualFps = afps;
        if (record.MeasuredCameraFps <= 0 && record.ActualFps > 0)
            record.MeasuredCameraFps = record.ActualFps;
        if (record.WriterFps <= 0)
            record.WriterFps = record.RecordingWriterFps;
        if (record.ContainerFps <= 0)
            record.ContainerFps = record.RecordingWriterFps;
        if (double.TryParse(Get(dict, "effectiveRecordedFps") ?? Get(dict, "Effective playback FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var effectiveFps))
            record.EffectivePlaybackFps = effectiveFps;

        if (long.TryParse(Get(dict, "Frames written"), out var fw))
            record.FrameCount = fw;
        else if (long.TryParse(Get(dict, "Frame count"), out var fc))
            record.FrameCount = fc;
        if (long.TryParse(Get(dict, "Frames captured"), out var fcap))
            record.FramesCaptured = fcap;
        if (long.TryParse(Get(dict, "Duplicated frames"), out var dups))
            record.DuplicatedFrames = dups;
        if (long.TryParse(Get(dict, "Placeholder frames"), out var ph))
            record.PlaceholderFrames = ph;
        if (long.TryParse(Get(dict, "Writer queue drops"), out var qd)
            || long.TryParse(Get(dict, "Writer drops"), out qd))
            record.WriterQueueDrops = qd;

        if (double.TryParse(Get(dict, "Duration seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var durSec))
            record.DurationSeconds = durSec;
        else
        {
            var mono = Get(dict, "Duration (monotonic)") ?? Get(dict, "Total duration (monotonic)");
            if (!string.IsNullOrEmpty(mono))
                record.DurationSeconds = ParseDurationSpan(mono);
        }
        if (double.TryParse(Get(dict, "Wall clock duration seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wallClock))
            record.WallClockDurationSeconds = wallClock;
        if (double.TryParse(Get(dict, "Wall duration seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wall))
            record.WallDurationSeconds = wall;
        if (record.WallClockDurationSeconds <= 0)
            record.WallClockDurationSeconds = record.WallDurationSeconds;
        if (double.TryParse(Get(dict, "Frame-based duration seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var fbd))
            record.FrameBasedDurationSeconds = fbd;
        if (double.TryParse(Get(dict, "Container duration seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cd))
            record.ContainerDurationSeconds = cd;
        if (double.TryParse(Get(dict, "Container vs wall-clock difference seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cvw))
            record.ContainerVsWallClockDifferenceSeconds = cvw;
        var driftText = Get(dict, "Timestamp drift seconds") ?? Get(dict, "Timestamp drift seconds (legacy)");
        if (double.TryParse(driftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var drift))
            record.TimestampDriftSeconds = drift;
        if (record.ContainerVsWallClockDifferenceSeconds == 0 && record.TimestampDriftSeconds != 0)
            record.ContainerVsWallClockDifferenceSeconds = record.TimestampDriftSeconds;
        if (TryParseMetadataMetric(Get(dict, "Capture interval mean ms"), out var capMean)
            || TryParseMetadataMetric(Get(dict, "Average capture interval ms"), out capMean))
        {
            record.CaptureIntervalMeanMs = capMean;
            record.AverageCaptureIntervalMs = capMean;
        }
        if (TryParseMetadataMetric(Get(dict, "captureIntervalMedianMs"), out var capMedian))
            record.CaptureIntervalMedianMs = capMedian;
        if (TryParseMetadataMetric(Get(dict, "Capture interval min ms"), out var capMin)
            || TryParseMetadataMetric(Get(dict, "Min capture interval ms"), out capMin))
        {
            record.CaptureIntervalMinMs = capMin;
            record.MinCaptureIntervalMs = capMin;
        }
        if (TryParseMetadataMetric(Get(dict, "Capture interval max ms"), out var capMax)
            || TryParseMetadataMetric(Get(dict, "Max capture interval ms"), out capMax))
        {
            record.CaptureIntervalMaxMs = capMax;
            record.MaxCaptureIntervalMs = capMax;
        }
        if (TryParseMetadataMetric(Get(dict, "captureIntervalP95Ms"), out var capP95))
            record.CaptureIntervalP95Ms = capP95;
        if (TryParseMetadataMetric(Get(dict, "captureIntervalP99Ms"), out var capP99))
            record.CaptureIntervalP99Ms = capP99;
        if (TryParseMetadataMetric(Get(dict, "Capture interval std ms"), out var capStd)
            || TryParseMetadataMetric(Get(dict, "Capture jitter ms"), out capStd))
        {
            record.CaptureIntervalStdMs = capStd;
            record.CaptureJitterMs = capStd;
        }
        if (TryParseMetadataCount(Get(dict, "Capture interval count"), out var capCount))
            record.CaptureIntervalCount = capCount;
        if (TryParseMetadataMetric(Get(dict, "measuredCameraFpsFromFirstLastFrame"), out var fpsFirstLast))
            record.MeasuredCameraFpsFromFirstLastFrame = fpsFirstLast;
        if (TryParseMetadataMetric(Get(dict, "measuredCameraFpsFromMeanInterval"), out var fpsMeanInterval))
            record.MeasuredCameraFpsFromMeanInterval = fpsMeanInterval;
        if (TryParseMetadataMetric(Get(dict, "expectedIntervalMs"), out var expectedInterval))
            record.ExpectedIntervalMs = expectedInterval;
        if (TryParseMetadataMetric(Get(dict, "requestedExpectedIntervalMs"), out var requestedExpectedInterval))
            record.RequestedExpectedIntervalMs = requestedExpectedInterval;
        if (TryParseMetadataMetric(Get(dict, "meanIntervalErrorMs"), out var meanIntervalError))
            record.MeanIntervalErrorMs = meanIntervalError;
        if (TryParseMetadataMetric(Get(dict, "absoluteMeanIntervalErrorMs"), out var absMeanIntervalError))
            record.AbsoluteMeanIntervalErrorMs = absMeanIntervalError;
        if (long.TryParse(Get(dict, "longGapCount"), out var longGapCount))
            record.LongGapCount = longGapCount;
        if (long.TryParse(Get(dict, "shortGapCount"), out var shortGapCount))
            record.ShortGapCount = shortGapCount;
        if (long.TryParse(Get(dict, "severeLongGapCount"), out var severeLongGapCount))
            record.SevereLongGapCount = severeLongGapCount;
        if (TryParseMetadataMetric(Get(dict, "jitterScoreMs"), out var jitterScore))
            record.JitterScoreMs = jitterScore;
        record.FpsStabilityGrade = Get(dict, "fpsStabilityGrade") ?? "";
        if (long.TryParse(Get(dict, "Max consecutive late frames"), out var lateFrames))
            record.MaxConsecutiveLateFrames = lateFrames;
        if (long.TryParse(Get(dict, "Max consecutive no frame"), out var noFrame))
            record.MaxConsecutiveNoFrame = noFrame;
        if (long.TryParse(Get(dict, "Inter-camera frame diff"), out var icf))
            record.InterCameraFrameDiff = icf;
        else if (long.TryParse(Get(dict, "Inter-camera frame difference"), out var icfAlt))
            record.InterCameraFrameDiff = icfAlt;
        if (double.TryParse(Get(dict, "Inter-camera start offset ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var icStart))
            record.InterCameraStartOffsetMs = icStart;
        if (double.TryParse(Get(dict, "Inter-camera stop offset ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var icStop))
            record.InterCameraStopOffsetMs = icStop;
        if (double.TryParse(Get(dict, "Inter-camera duration diff sec"), NumberStyles.Float, CultureInfo.InvariantCulture, out var icd))
            record.InterCameraDurationDiffSec = icd;

        if (bool.TryParse(Get(dict, "Experiment Mode"), out var expMode))
            record.ExperimentMode = expMode;
        if (double.TryParse(Get(dict, "Target duration seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tDur))
            record.TargetDurationSeconds = tDur;
        if (double.TryParse(Get(dict, "Target FPS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tFps))
            record.TargetFps = tFps;
        if (long.TryParse(Get(dict, "Expected frames"), out var expFrames))
            record.ExpectedFrames = expFrames;
        if (long.TryParse(Get(dict, "Dropped frames"), out var dropped))
            record.DroppedFrames = dropped;
        if (long.TryParse(Get(dict, "Duplicate frames"), out var dup))
            record.DuplicateFrames = dup;
        if (bool.TryParse(Get(dict, "Constant frame count mode"), out var cfc))
            record.ConstantFrameCountMode = cfc;
        if (bool.TryParse(Get(dict, "originalCaptureMode") ?? Get(dict, "Original capture mode"), out var originalCapture))
            record.OriginalCaptureMode = originalCapture;
        record.FrameTimestampCsvPath = Get(dict, "frameTimestampCsvPath") ?? "";
        if (bool.TryParse(Get(dict, "frameTimestampCsvWritten"), out var timestampCsvWritten))
            record.FrameTimestampCsvWritten = timestampCsvWritten;
        if (long.TryParse(Get(dict, "frameTimestampCsvRowCount"), out var timestampCsvRows))
            record.FrameTimestampCsvRowCount = timestampCsvRows;
        if (DateTime.TryParse(Get(dict, "firstFrameCaptureUtcTime"), null, DateTimeStyles.RoundtripKind, out var firstCaptureUtc))
            record.FirstFrameCaptureUtcTime = firstCaptureUtc;
        if (DateTime.TryParse(Get(dict, "lastFrameCaptureUtcTime"), null, DateTimeStyles.RoundtripKind, out var lastCaptureUtc))
            record.LastFrameCaptureUtcTime = lastCaptureUtc;
        if (DateTime.TryParse(Get(dict, "firstFrameUtcTime"), null, DateTimeStyles.RoundtripKind, out var firstFrameUtc))
            record.FirstFrameUtcTime = firstFrameUtc;
        if (DateTime.TryParse(Get(dict, "lastFrameUtcTime"), null, DateTimeStyles.RoundtripKind, out var lastFrameUtc))
            record.LastFrameUtcTime = lastFrameUtc;
        if (DateTime.TryParse(Get(dict, "writerClosedUtcTime"), null, DateTimeStyles.RoundtripKind, out var writerClosedUtc))
            record.WriterClosedUtcTime = writerClosedUtc;
        if (double.TryParse(Get(dict, "firstFrameCaptureMonotonicSec"), NumberStyles.Float, CultureInfo.InvariantCulture, out var firstCaptureMono))
            record.FirstFrameCaptureMonotonicSec = firstCaptureMono;
        if (double.TryParse(Get(dict, "lastFrameCaptureMonotonicSec"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lastCaptureMono))
            record.LastFrameCaptureMonotonicSec = lastCaptureMono;
        if (double.TryParse(Get(dict, "firstToLastFrameDurationSec"), NumberStyles.Float, CultureInfo.InvariantCulture, out var firstToLastSec))
            record.FirstToLastFrameDurationSec = firstToLastSec;
        record.TrimRecommendedTimeSource = Get(dict, "trimRecommendedTimeSource") ?? "";
        record.TrimWarning = Get(dict, "trimWarning") ?? "";
        record.ScientificTrimStartReference = Get(dict, "scientificTrimStartReference") ?? "";
        record.ScientificTrimEndReference = Get(dict, "scientificTrimEndReference") ?? "";
        if (bool.TryParse(Get(dict, "supportsTimestampBasedTrimming"), out var supportsTimestampTrimming))
            record.SupportsTimestampBasedTrimming = supportsTimestampTrimming;
        record.RecommendedAction = Get(dict, "recommendedAction") ?? Get(dict, "Recommended action") ?? "";
        if (double.TryParse(Get(dict, "Average frame interval ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var avgFrameInt))
            record.AverageFrameIntervalMs = avgFrameInt;
        if (double.TryParse(Get(dict, "Frame interval std dev ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var stdInt))
            record.FrameIntervalStdDevMs = stdInt;
        record.ExperimentResult = Get(dict, "Experiment result") ?? "";
        record.SessionTitleOriginal = Get(dict, "Session title original") ?? "";
        record.SessionFolderName = Get(dict, "Session folder name") ?? "";
        record.RecordingMode = Get(dict, "RecordingMode") ?? "";

        if (double.TryParse(Get(dict, "MinimumAnalysisDurationSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var minDur))
            record.MinimumAnalysisDurationSeconds = minDur;
        if (double.TryParse(Get(dict, "PlannedRecordingDurationSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var planDur))
            record.PlannedRecordingDurationSeconds = planDur;
        if (bool.TryParse(Get(dict, "UsableFor10MinAnalysis"), out var usable))
            record.UsableFor10MinAnalysis = usable;

        if (DateTime.TryParse(Get(dict, "Recording start (local)"), null, DateTimeStyles.RoundtripKind, out var startLocal))
            record.RecordingStartLocal = startLocal;
        if (DateTime.TryParse(Get(dict, "Recording stop (local)"), null, DateTimeStyles.RoundtripKind, out var stopLocal))
            record.RecordingStopLocal = stopLocal;
        if (double.TryParse(Get(dict, "Session start monotonic seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ssm))
            record.SessionStartMonotonicSeconds = ssm;
        if (double.TryParse(Get(dict, "Camera start monotonic seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var csm))
            record.CameraStartMonotonicSeconds = csm;
        if (double.TryParse(Get(dict, "First frame monotonic seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ffm))
            record.FirstFrameMonotonicSeconds = ffm;
        if (double.TryParse(Get(dict, "Last frame monotonic seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lfm))
            record.LastFrameMonotonicSeconds = lfm;
        if (double.TryParse(Get(dict, "Stop requested monotonic seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var srm))
            record.StopRequestedMonotonicSeconds = srm;
        if (double.TryParse(Get(dict, "Writer closed monotonic seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var wcm))
            record.WriterClosedMonotonicSeconds = wcm;
        record.ComputerName = Get(dict, "Computer name") ?? Get(dict, "PC name");
        record.CpuName = Get(dict, "CPU name");
        if (double.TryParse(Get(dict, "RAM GB"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ramGb))
            record.RamGb = ramGb;
        record.RecordingDiagnostics = ParseTextRecordingDiagnostics(dict);

        if (string.Equals(record.RecordingMode, LocomotorRecordingController.RecordingModeName, StringComparison.OrdinalIgnoreCase))
            record.ExperimentMode = true;

        return record;
    }

    public static CameraMetadataRecord? ParseCameraMetadataJson(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var record = new CameraMetadataRecord
            {
                AppVersion = ReadString(root, "AppVersion") ?? "",
                BuildNumber = ReadInt(root, "BuildNumber"),
                ReleaseStage = ReadString(root, "ReleaseStage") ?? "",
                SessionName = ReadString(root, "SessionName") ?? "",
                CameraSlot = ReadString(root, "CameraSlot") ?? "",
                CameraDeviceName = ReadString(root, "CameraDeviceName") ?? "",
                Codec = ReadString(root, "Codec"),
                ContainerFormat = ReadString(root, "ContainerFormat"),
                FilePath = ReadString(root, "OutputFilePath"),
                RecordingApi = ReadString(root, "RecordingApi"),
                Backend = ReadString(root, "Backend"),
                DeviceId = ReadString(root, "DeviceId"),
                ScientificTimingStatus = ReadString(root, "ScientificTimingStatus"),
                ScientificTimingMessage = ReadString(root, "ScientificTimingMessage"),
                AutoFocusRequested = ReadBool(root, "AutoFocusRequested"),
                AutoFocusApplyAttempted = ReadBool(root, "AutoFocusApplyAttempted"),
                AutoFocusApplySucceeded = ReadNullableBool(root, "AutoFocusApplySucceeded"),
                AutoFocusReadbackValue = ReadString(root, "AutoFocusReadbackValue") ?? "unavailable",
                ManualFocusSupported = ReadNullableBool(root, "ManualFocusSupported"),
                ManualFocusRequestedValue = ReadNullableDouble(root, "ManualFocusRequestedValue"),
                ManualFocusReadbackValue = ReadString(root, "ManualFocusReadbackValue") ?? "unavailable",
                FocusControlMode = ReadString(root, "FocusControlMode") ?? "unavailable",
                FocusWarning = ReadString(root, "FocusWarning") ?? "",
                CaptureIntervalStatsMessage = ReadString(root, "CaptureIntervalStatsMessage"),
                SessionTitleOriginal = ReadString(root, "SessionTitleOriginal") ?? "",
                SessionFolderName = ReadString(root, "SessionFolderName") ?? "",
                ExperimentResult = ReadString(root, "ExperimentResult") ?? "",
                RecordingMode = ReadString(root, "RecordingMode") ?? ""
            };
            foreach (var property in root.EnumerateObject())
                record.PresentMetadataFields.Add(property.Name);

            record.RequestedResolution = ReadString(root, "RequestedResolution");
            record.SelectedResolution = ReadString(root, "SelectedResolution");
            record.Resolution = ReadString(root, "Resolution");
            record.PixelWidth = ReadInt(root, "Width");
            record.PixelHeight = ReadInt(root, "Height");
            if (record.PixelWidth <= 0 || record.PixelHeight <= 0)
            {
                var resolutionText = record.Resolution
                    ?? record.SelectedResolution
                    ?? record.RequestedResolution;
                if (TryParsePixels(resolutionText, out var rw, out var rh))
                {
                    record.PixelWidth = rw;
                    record.PixelHeight = rh;
                }
            }

            record.RequestedFps = ReadDouble(root, "RequestedFps");
            record.SelectedDeviceFps = ReadDouble(root, "SelectedDeviceFps");
            record.RecordingWriterFps = ReadDouble(root, "RecordingWriterFps");
            record.WriterFps = ReadDouble(root, "WriterFps");
            record.ContainerFps = ReadDouble(root, "ContainerFps");
            record.MeasuredCameraFps = ReadDouble(root, "MeasuredCameraFps");
            record.EffectivePlaybackFps = ReadDouble(root, "EffectivePlaybackFps");
            if (record.MeasuredCameraFps <= 0)
                record.MeasuredCameraFps = ReadDouble(root, "MeasuredWriterFps");
            record.ActualFps = record.MeasuredCameraFps;
            record.FrameCount = ReadLong(root, "FramesWritten");
            if (record.FrameCount <= 0)
                record.FrameCount = ReadLong(root, "FrameCount");
            record.FramesCaptured = ReadLong(root, "FramesCaptured");
            record.DuplicatedFrames = ReadLong(root, "DuplicatedFrames");
            record.PlaceholderFrames = ReadLong(root, "PlaceholderFrames");
            record.WriterQueueDrops = ReadLong(root, "WriterQueueDrops");
            record.WallClockDurationSeconds = ReadDouble(root, "WallClockDurationSeconds");
            if (record.WallClockDurationSeconds <= 0)
                record.WallClockDurationSeconds = ReadDouble(root, "WallDurationSeconds");
            record.WallDurationSeconds = record.WallClockDurationSeconds;
            record.FrameBasedDurationSeconds = ReadDouble(root, "FrameBasedDurationSeconds");
            record.ContainerDurationSeconds = ReadDouble(root, "ContainerDurationSeconds");
            record.ContainerVsWallClockDifferenceSeconds = ReadDouble(root, "ContainerVsWallClockDifferenceSeconds");
            record.TimestampDriftSeconds = ReadDouble(root, "TimestampDriftSeconds");
            record.CaptureIntervalMeanMs = ReadDouble(root, "CaptureIntervalMeanMs");
            record.CaptureIntervalMedianMs = ReadDouble(root, "CaptureIntervalMedianMs");
            record.CaptureIntervalMinMs = ReadDouble(root, "CaptureIntervalMinMs");
            record.CaptureIntervalMaxMs = ReadDouble(root, "CaptureIntervalMaxMs");
            record.CaptureIntervalP95Ms = ReadDouble(root, "CaptureIntervalP95Ms");
            record.CaptureIntervalP99Ms = ReadDouble(root, "CaptureIntervalP99Ms");
            record.CaptureIntervalStdMs = ReadDouble(root, "CaptureIntervalStdMs");
            record.CaptureIntervalCount = ReadLong(root, "CaptureIntervalCount");
            record.MeasuredCameraFpsFromFirstLastFrame = ReadDouble(root, "MeasuredCameraFpsFromFirstLastFrame");
            record.MeasuredCameraFpsFromMeanInterval = ReadDouble(root, "MeasuredCameraFpsFromMeanInterval");
            record.ExpectedIntervalMs = ReadDouble(root, "ExpectedIntervalMs");
            record.RequestedExpectedIntervalMs = ReadDouble(root, "RequestedExpectedIntervalMs");
            record.MeanIntervalErrorMs = ReadDouble(root, "MeanIntervalErrorMs");
            record.AbsoluteMeanIntervalErrorMs = ReadDouble(root, "AbsoluteMeanIntervalErrorMs");
            record.LongGapCount = ReadLong(root, "LongGapCount");
            record.ShortGapCount = ReadLong(root, "ShortGapCount");
            record.SevereLongGapCount = ReadLong(root, "SevereLongGapCount");
            record.JitterScoreMs = ReadDouble(root, "JitterScoreMs");
            record.FpsStabilityGrade = ReadString(root, "FpsStabilityGrade") ?? "";
            record.InterCameraFrameDiff = ReadLong(root, "InterCameraFrameDiff");
            record.InterCameraStartOffsetMs = ReadDouble(root, "InterCameraStartOffsetMs");
            record.InterCameraStopOffsetMs = ReadDouble(root, "InterCameraStopOffsetMs");
            record.InterCameraDurationDiffSec = ReadDouble(root, "InterCameraDurationDiffSec");
            record.DeviceIndex = ReadInt(root, "DeviceIndex", -1);
            record.DuplicateFrames = ReadLong(root, "DuplicateFrames");
            record.DroppedFrames = ReadLong(root, "DroppedFrames");
            record.ExpectedFrames = ReadLong(root, "ExpectedFrames");
            record.ExperimentMode = ReadBool(root, "ExperimentMode");
            record.TargetDurationSeconds = ReadDouble(root, "TargetDurationSeconds");
            record.TargetFps = ReadDouble(root, "TargetFps");
            record.ConstantFrameCountMode = ReadBool(root, "ConstantFrameCountMode");
            record.RecordingTimingMode = ReadString(root, "RecordingTimingMode") ?? "";
            record.OriginalCaptureMode = ReadBool(root, "OriginalCaptureMode");
            record.FrameTimestampCsvPath = ReadString(root, "FrameTimestampCsvPath") ?? "";
            record.FrameTimestampCsvWritten = ReadBool(root, "FrameTimestampCsvWritten");
            record.FrameTimestampCsvRowCount = ReadLong(root, "FrameTimestampCsvRowCount");
            record.FirstFrameCaptureUtcTime = ReadDateTime(root, "FirstFrameCaptureUtcTime");
            record.LastFrameCaptureUtcTime = ReadDateTime(root, "LastFrameCaptureUtcTime");
            record.FirstFrameUtcTime = ReadDateTime(root, "FirstFrameUtcTime");
            record.LastFrameUtcTime = ReadDateTime(root, "LastFrameUtcTime");
            record.WriterClosedUtcTime = ReadDateTime(root, "WriterClosedUtcTime");
            record.FirstFrameCaptureMonotonicSec = ReadDouble(root, "FirstFrameCaptureMonotonicSec");
            record.LastFrameCaptureMonotonicSec = ReadDouble(root, "LastFrameCaptureMonotonicSec");
            record.FirstToLastFrameDurationSec = ReadDouble(root, "FirstToLastFrameDurationSec");
            record.TrimRecommendedTimeSource = ReadString(root, "TrimRecommendedTimeSource") ?? "";
            record.TrimWarning = ReadString(root, "TrimWarning") ?? "";
            record.ScientificTrimStartReference = ReadString(root, "ScientificTrimStartReference") ?? "";
            record.ScientificTrimEndReference = ReadString(root, "ScientificTrimEndReference") ?? "";
            record.SupportsTimestampBasedTrimming = ReadBool(root, "SupportsTimestampBasedTrimming");
            record.RecommendedAction = ReadString(root, "RecommendedAction") ?? "";
            record.AverageFrameIntervalMs = ReadDouble(root, "AverageFrameIntervalMs");
            record.FrameIntervalStdDevMs = ReadDouble(root, "FrameIntervalStdDevMs");
            record.MinimumAnalysisDurationSeconds = ReadDouble(root, "MinimumAnalysisDurationSeconds");
            record.PlannedRecordingDurationSeconds = ReadDouble(root, "PlannedRecordingDurationSeconds");
            record.UsableFor10MinAnalysis = ReadBool(root, "UsableFor10MinAnalysis");
            record.RecordingDiagnostics = ReadRecordingDiagnostics(root);
            record.DurationSeconds = record.WallClockDurationSeconds;

            if (string.IsNullOrWhiteSpace(record.Resolution) && record.PixelWidth > 0 && record.PixelHeight > 0)
                record.Resolution = CaptureResolutionPreset.ToLabel(record.PixelWidth, record.PixelHeight);

            return record;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    private static long ReadLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? (long)value.GetDouble()
            : 0;

    private static int ReadInt(JsonElement root, string name, int fallback = 0) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();

    private static bool? ReadNullableBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTime? ReadDateTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return DateTime.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static RecordingDiagnosticsMetadataSummary? ParseTextRecordingDiagnostics(Dictionary<string, string> dict)
    {
        var csvPath = Get(dict, "recordingDiagnosticsCsvPath") ?? "";
        var summaryPath = Get(dict, "recordingDiagnosticsSummaryJsonPath") ?? "";
        if (string.IsNullOrWhiteSpace(csvPath) && string.IsNullOrWhiteSpace(summaryPath))
            return null;

        var camera = new RecordingDiagnosticsCameraSummary
        {
            TimingVerdict = Get(dict, "recordingDiagnosticsTimingVerdict") ?? "",
            MeasuredFpsByFrameCount = ReadTextNullableDouble(dict, "recordingDiagnosticsMeasuredFpsByFrameCount"),
            MeasuredFpsByIntervals = ReadTextNullableDouble(dict, "recordingDiagnosticsMeasuredFpsByIntervals"),
            CaptureIntervalMeanMs = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalMeanMs"),
            CaptureIntervalMedianMs = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalMedianMs"),
            CaptureIntervalStdMs = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalStdMs"),
            CaptureIntervalMinMs = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalMinMs"),
            CaptureIntervalMaxMs = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalMaxMs"),
            CaptureIntervalP95Ms = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalP95Ms"),
            CaptureIntervalP99Ms = ReadTextNullableDouble(dict, "recordingDiagnosticsCaptureIntervalP99Ms"),
            InstantaneousFpsSpikeMaxIgnored = ReadTextNullableDouble(dict, "recordingDiagnosticsInstantaneousFpsSpikeMaxIgnored"),
            InstantaneousFpsSpikeIgnoredCount = (int)ReadTextLong(dict, "recordingDiagnosticsInstantaneousFpsSpikeIgnoredCount"),
            FramesCaptured = ReadTextLong(dict, "recordingDiagnosticsFramesCaptured"),
            FramesWritten = ReadTextLong(dict, "recordingDiagnosticsFramesWritten"),
            FramesCapturedMinusWritten = ReadTextLong(dict, "recordingDiagnosticsFramesCapturedMinusWritten"),
            FramesCapturedTotal = ReadTextLong(dict, "recordingDiagnosticsFramesCapturedTotal"),
            FramesAcceptedForRecording = ReadTextLong(dict, "recordingDiagnosticsFramesAcceptedForRecording"),
            FramesEnqueued = ReadTextLong(dict, "recordingDiagnosticsFramesEnqueued"),
            FramesDequeued = ReadTextLong(dict, "recordingDiagnosticsFramesDequeued"),
            FramesCapturedAfterStopRequested = ReadTextLong(dict, "recordingDiagnosticsFramesCapturedAfterStopRequested"),
            FramesNotRecordedAfterStopRequested = ReadTextLong(dict, "recordingDiagnosticsFramesNotRecordedAfterStopRequested"),
            FramesDroppedBeforeEnqueue = ReadTextLong(dict, "recordingDiagnosticsFramesDroppedBeforeEnqueue"),
            FinalFlushCompleted = ReadTextBool(dict, "recordingDiagnosticsFinalFlushCompleted"),
            FinalFlushTimedOut = ReadTextBool(dict, "recordingDiagnosticsFinalFlushTimedOut"),
            WriterReleasedSuccessfully = ReadTextBool(dict, "recordingDiagnosticsWriterReleasedSuccessfully"),
            FinalFileSizeMB = ReadTextDouble(dict, "finalFileSizeMB"),
            EstimatedGBPerHour = ReadTextDouble(dict, "estimatedGBPerHourPerCamera"),
            MaxWriterQueueDepth = (int)ReadTextLong(dict, "recordingDiagnosticsMaxTotalWriterQueueDepth"),
            MaxWriterQueueDrops = ReadTextLong(dict, "recordingDiagnosticsMaxTotalQueueDrops")
        };

        return new RecordingDiagnosticsMetadataSummary
        {
            CsvPath = csvPath,
            SummaryJsonPath = summaryPath,
            SampleCount = (int)ReadTextLong(dict, "recordingDiagnosticsSampleCount"),
            AverageCpuPercent = ReadTextNullableDouble(dict, "recordingDiagnosticsAverageCpuPercent"),
            MaxCpuPercent = ReadTextNullableDouble(dict, "recordingDiagnosticsMaxCpuPercent"),
            CpuSamplesOver90Percent = (int)ReadTextLong(dict, "recordingDiagnosticsCpuSamplesOver90Percent"),
            MaxProcessMemoryMB = ReadTextNullableDouble(dict, "recordingDiagnosticsMaxProcessMemoryMB"),
            ProcessMemoryContinuouslyIncreases = ReadTextBool(dict, "recordingDiagnosticsProcessMemoryContinuouslyIncreases"),
            SystemTotalMemoryMB = ReadTextDouble(dict, "recordingDiagnosticsSystemTotalMemoryMB"),
            MinSystemAvailableMemoryMB = ReadTextDouble(dict, "recordingDiagnosticsMinSystemAvailableMemoryMB"),
            MinDiskFreeSpaceGB = ReadTextNullableDouble(dict, "recordingDiagnosticsMinDiskFreeSpaceGB"),
            MaxTotalCurrentFileSizeMB = ReadTextDouble(dict, "recordingDiagnosticsMaxTotalCurrentFileSizeMB"),
            TotalSessionSizeMB = ReadTextDouble(dict, "totalSessionSizeMB"),
            EstimatedGBPerHourPerCamera = ReadTextDouble(dict, "estimatedGBPerHourPerCamera"),
            EstimatedGBPerHourAllCameras = ReadTextDouble(dict, "estimatedGBPerHourAllCameras"),
            MaxTotalWriterQueueDepth = (int)ReadTextLong(dict, "recordingDiagnosticsMaxTotalWriterQueueDepth"),
            MaxTotalQueueDrops = ReadTextLong(dict, "recordingDiagnosticsMaxTotalQueueDrops"),
            SessionVerdictText = Get(dict, "recordingDiagnosticsSessionVerdictText") ?? "",
            ArtifactNote = Get(dict, "recordingDiagnosticsArtifactNote") ?? "",
            Camera = camera
        };
    }

    private static RecordingDiagnosticsMetadataSummary? ReadRecordingDiagnostics(JsonElement root)
    {
        if (!root.TryGetProperty("RecordingDiagnostics", out var diag) || diag.ValueKind != JsonValueKind.Object)
            return null;

        RecordingDiagnosticsCameraSummary? camera = null;
        if (diag.TryGetProperty("Camera", out var cameraElement) && cameraElement.ValueKind == JsonValueKind.Object)
        {
            camera = new RecordingDiagnosticsCameraSummary
            {
                CameraIndex = ReadInt(cameraElement, "CameraIndex"),
                CameraName = ReadString(cameraElement, "CameraName") ?? "",
                TimingVerdict = ReadString(cameraElement, "TimingVerdict") ?? "",
                RequestedFps = ReadNullableDouble(cameraElement, "RequestedFps"),
                MeasuredFpsByFrameCount = ReadNullableDouble(cameraElement, "MeasuredFpsByFrameCount"),
                MeasuredFpsByIntervals = ReadNullableDouble(cameraElement, "MeasuredFpsByIntervals"),
                CaptureIntervalMeanMs = ReadNullableDouble(cameraElement, "CaptureIntervalMeanMs"),
                CaptureIntervalMedianMs = ReadNullableDouble(cameraElement, "CaptureIntervalMedianMs"),
                CaptureIntervalStdMs = ReadNullableDouble(cameraElement, "CaptureIntervalStdMs"),
                CaptureIntervalMinMs = ReadNullableDouble(cameraElement, "CaptureIntervalMinMs"),
                CaptureIntervalMaxMs = ReadNullableDouble(cameraElement, "CaptureIntervalMaxMs"),
                CaptureIntervalP95Ms = ReadNullableDouble(cameraElement, "CaptureIntervalP95Ms"),
                CaptureIntervalP99Ms = ReadNullableDouble(cameraElement, "CaptureIntervalP99Ms"),
                InstantaneousFpsSpikeMaxIgnored = ReadNullableDouble(cameraElement, "InstantaneousFpsSpikeMaxIgnored"),
                InstantaneousFpsSpikeIgnoredCount = ReadInt(cameraElement, "InstantaneousFpsSpikeIgnoredCount"),
                FramesCaptured = ReadLong(cameraElement, "FramesCaptured"),
                FramesWritten = ReadLong(cameraElement, "FramesWritten"),
                FramesCapturedMinusWritten = ReadLong(cameraElement, "FramesCapturedMinusWritten"),
                FramesCapturedTotal = ReadLong(cameraElement, "FramesCapturedTotal"),
                FramesAcceptedForRecording = ReadLong(cameraElement, "FramesAcceptedForRecording"),
                FramesEnqueued = ReadLong(cameraElement, "FramesEnqueued"),
                FramesDequeued = ReadLong(cameraElement, "FramesDequeued"),
                FramesCapturedAfterStopRequested = ReadLong(cameraElement, "FramesCapturedAfterStopRequested"),
                FramesNotRecordedAfterStopRequested = ReadLong(cameraElement, "FramesNotRecordedAfterStopRequested"),
                FramesDroppedBeforeEnqueue = ReadLong(cameraElement, "FramesDroppedBeforeEnqueue"),
                FinalFlushCompleted = ReadBool(cameraElement, "FinalFlushCompleted"),
                FinalFlushTimedOut = ReadBool(cameraElement, "FinalFlushTimedOut"),
                WriterReleasedSuccessfully = ReadBool(cameraElement, "WriterReleasedSuccessfully"),
                MaxMeasuredFpsRunning = ReadNullableDouble(cameraElement, "MaxMeasuredFpsRunning"),
                MinMeasuredFpsRunning = ReadNullableDouble(cameraElement, "MinMeasuredFpsRunning"),
                MaxCaptureFpsCurrent = ReadNullableDouble(cameraElement, "MaxCaptureFpsCurrent"),
                MaxCaptureIntervalStdMs = ReadNullableDouble(cameraElement, "MaxCaptureIntervalStdMs"),
                MaxWriterQueueDepth = ReadInt(cameraElement, "MaxWriterQueueDepth"),
                MaxWriterQueueDrops = ReadLong(cameraElement, "MaxWriterQueueDrops"),
                MaxWriterWriteMeanMs = ReadNullableDouble(cameraElement, "MaxWriterWriteMeanMs"),
                MaxWriterWriteMaxMs = ReadNullableDouble(cameraElement, "MaxWriterWriteMaxMs"),
                MaxCurrentFileSizeMB = ReadDouble(cameraElement, "MaxCurrentFileSizeMB"),
                FinalFileSizeMB = ReadDouble(cameraElement, "FinalFileSizeMB"),
                EstimatedGBPerHour = ReadDouble(cameraElement, "EstimatedGBPerHour"),
                MaxFileSizeGrowthMBps = ReadNullableDouble(cameraElement, "MaxFileSizeGrowthMBps"),
                AutoFocusSupported = ReadString(cameraElement, "AutoFocusSupported") ?? "unavailable",
                AutoFocusEnabled = ReadString(cameraElement, "AutoFocusEnabled") ?? "unavailable",
                ManualFocusSupported = ReadString(cameraElement, "ManualFocusSupported") ?? "unavailable",
                ManualFocusValue = ReadString(cameraElement, "ManualFocusValue") ?? "unavailable"
            };
        }

        return new RecordingDiagnosticsMetadataSummary
        {
            CsvPath = ReadString(diag, "CsvPath") ?? "",
            SummaryJsonPath = ReadString(diag, "SummaryJsonPath") ?? "",
            SampleCount = ReadInt(diag, "SampleCount"),
            SampleIntervalSeconds = ReadDouble(diag, "SampleIntervalSeconds"),
            AverageCpuPercent = ReadNullableDouble(diag, "AverageCpuPercent"),
            MaxCpuPercent = ReadNullableDouble(diag, "MaxCpuPercent"),
            CpuSamplesOver90Percent = ReadInt(diag, "CpuSamplesOver90Percent"),
            MaxProcessMemoryMB = ReadNullableDouble(diag, "MaxProcessMemoryMB"),
            ProcessMemoryContinuouslyIncreases = ReadBool(diag, "ProcessMemoryContinuouslyIncreases"),
            SystemTotalMemoryMB = ReadDouble(diag, "SystemTotalMemoryMB"),
            MinSystemAvailableMemoryMB = ReadDouble(diag, "MinSystemAvailableMemoryMB"),
            MinDiskFreeSpaceGB = ReadNullableDouble(diag, "MinDiskFreeSpaceGB"),
            MaxTotalCurrentFileSizeMB = ReadDouble(diag, "MaxTotalCurrentFileSizeMB"),
            TotalSessionSizeMB = ReadDouble(diag, "TotalSessionSizeMB"),
            EstimatedGBPerHourPerCamera = ReadDouble(diag, "EstimatedGBPerHourPerCamera"),
            EstimatedGBPerHourAllCameras = ReadDouble(diag, "EstimatedGBPerHourAllCameras"),
            MaxTotalFileSizeGrowthMBps = ReadNullableDouble(diag, "MaxTotalFileSizeGrowthMBps"),
            MaxTotalWriterQueueDepth = ReadInt(diag, "MaxTotalWriterQueueDepth"),
            MaxTotalWriterQueueCapacity = ReadInt(diag, "MaxTotalWriterQueueCapacity"),
            MaxTotalQueueDrops = ReadLong(diag, "MaxTotalQueueDrops"),
            SessionVerdictText = ReadString(diag, "SessionVerdictText") ?? "",
            ArtifactNote = ReadString(diag, "ArtifactNote") ?? "",
            Camera = camera
        };
    }

    private static double? ReadNullableDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static double ReadTextDouble(Dictionary<string, string> dict, string name) =>
        double.TryParse(Get(dict, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static double? ReadTextNullableDouble(Dictionary<string, string> dict, string name) =>
        double.TryParse(Get(dict, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static long ReadTextLong(Dictionary<string, string> dict, string name) =>
        long.TryParse(Get(dict, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static bool ReadTextBool(Dictionary<string, string> dict, string name) =>
        bool.TryParse(Get(dict, name), out var value) && value;

    private static bool? ReadTextNullableBool(Dictionary<string, string> dict, string name) =>
        bool.TryParse(Get(dict, name), out var value) ? value : null;

    public static CameraMetadataRecord? LoadCameraMetadata(string? metadataJsonPath, string? metadataTxtPath)
    {
        if (!string.IsNullOrWhiteSpace(metadataJsonPath))
        {
            var json = ParseCameraMetadataJson(metadataJsonPath);
            if (json != null)
                return json;
        }

        if (!string.IsNullOrWhiteSpace(metadataTxtPath))
            return ParseCameraMetadataFile(metadataTxtPath);

        return null;
    }

    public static SessionSummaryRecord? ParseSessionSummary(string sessionFolder)
    {
        var txt = Path.Combine(sessionFolder, "session_summary.txt");
        if (!File.Exists(txt)) return null;

        var lines = File.ReadAllLines(txt);
        var record = new SessionSummaryRecord { Folder = sessionFolder };
        CameraMetadataRecord? current = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("Session:", StringComparison.OrdinalIgnoreCase))
                record.SessionName = line["Session:".Length..].Trim();
            else if (line.StartsWith("Session title original:", StringComparison.OrdinalIgnoreCase))
                record.SessionTitleOriginal = line["Session title original:".Length..].Trim();
            else if (line.StartsWith("Session folder name:", StringComparison.OrdinalIgnoreCase))
                record.SessionFolderName = line["Session folder name:".Length..].Trim();
            else if (line.StartsWith("Folder:", StringComparison.OrdinalIgnoreCase))
                record.Folder = line["Folder:".Length..].Trim();
            else if (line.StartsWith("App Version:", StringComparison.OrdinalIgnoreCase))
                record.AppVersion = line["App Version:".Length..].Trim();
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                if (current != null) record.Cameras.Add(current);
                var slot = line.Trim('-', ' ').Trim();
                current = new CameraMetadataRecord { CameraSlot = slot };
            }
            else if (current != null)
            {
                if (line.StartsWith("Device:", StringComparison.OrdinalIgnoreCase)) { }
                else if (line.StartsWith("Resolution:", StringComparison.OrdinalIgnoreCase))
                {
                    current.Resolution = line["Resolution:".Length..].Trim();
                    var paren = current.Resolution.IndexOf('(');
                    if (paren >= 0 && TryParsePixels(current.Resolution[(paren + 1)..].TrimEnd(')'), out var w, out var h))
                    {
                        current.PixelWidth = w;
                        current.PixelHeight = h;
                    }
                }
                else if (line.StartsWith("FPS:", StringComparison.OrdinalIgnoreCase))
                {
                    var fpsPart = line["FPS:".Length..].Trim();
                    var req = Regex.Match(fpsPart, @"requested\s+([\d.]+)", RegexOptions.IgnoreCase);
                    var sel = Regex.Match(fpsPart, @"selected\s+([\d.]+)", RegexOptions.IgnoreCase);
                    var wr = Regex.Match(fpsPart, @"writer\s+([\d.]+)", RegexOptions.IgnoreCase);
                    var act = Regex.Match(fpsPart, @"actual\s+([\d.]+)", RegexOptions.IgnoreCase);
                    if (req.Success) current.RequestedFps = double.Parse(req.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (sel.Success) current.SelectedDeviceFps = double.Parse(sel.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (wr.Success) current.RecordingWriterFps = double.Parse(wr.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (act.Success) current.ActualFps = double.Parse(act.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                else if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                {
                    var fmt = line["Format:".Length..].Trim().Split('/');
                    if (fmt.Length >= 1) current.ContainerFormat = fmt[0];
                    if (fmt.Length >= 2) current.VideoSubtype = fmt[1];
                    current.Codec = fmt.Length >= 2 ? fmt[1] : fmt[0];
                }
                else if (line.StartsWith("Duration seconds:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(line["Duration seconds:".Length..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                        current.DurationSeconds = ds;
                }
                else if (line.StartsWith("Frames written:", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(line["Frames written:".Length..].Trim(), out var fw))
                        current.FrameCount = fw;
                }
                else if (line.StartsWith("Duration (monotonic):", StringComparison.OrdinalIgnoreCase))
                {
                    if (current.DurationSeconds is null or 0)
                        current.DurationSeconds = ParseDurationSpan(line["Duration (monotonic):".Length..].Trim());
                }
                else if (line.StartsWith("Timestamp drift seconds:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(line["Timestamp drift seconds:".Length..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var drift))
                        current.TimestampDriftSeconds = drift;
                }
                else if (line.StartsWith("Scientific timing status:", StringComparison.OrdinalIgnoreCase))
                    current.ScientificTimingStatus = line["Scientific timing status:".Length..].Trim();
                else if (line.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
                    current.FilePath = line["File:".Length..].Trim();
            }
        }

        if (current != null) record.Cameras.Add(current);
        return record.Cameras.Count > 0 ? record : null;
    }

    private static Dictionary<string, string> ParseKeyValueLines(string[] lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return dict;
    }

    private static string? Get(Dictionary<string, string> dict, string key) =>
        dict.TryGetValue(key, out var v) ? v : null;

    private static bool TryParseMetadataMetric(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("Unavailable", StringComparison.OrdinalIgnoreCase)) return false;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryParseMetadataCount(string? value, out long count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("Unavailable", StringComparison.OrdinalIgnoreCase)) return false;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
    }

    private static bool TryParsePixels(string? text, out int w, out int h)
    {
        w = h = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Regex.Match(text, @"(\d+)\s*[xX×]\s*(\d+)");
        if (!m.Success) return false;
        w = int.Parse(m.Groups[1].Value);
        h = int.Parse(m.Groups[2].Value);
        return true;
    }

    private static double? ParseDurationSpan(string text)
    {
        var m = Regex.Match(text, @"(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?");
        if (!m.Success) return null;
        var hours = int.Parse(m.Groups[2].Value);
        var mins = int.Parse(m.Groups[3].Value);
        var secs = int.Parse(m.Groups[4].Value);
        var frac = m.Groups[5].Success ? double.Parse("0." + m.Groups[5].Value, CultureInfo.InvariantCulture) : 0;
        return hours * 3600 + mins * 60 + secs + frac;
    }
}
