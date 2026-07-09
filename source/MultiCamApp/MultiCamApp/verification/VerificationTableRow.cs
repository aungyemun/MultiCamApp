////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

namespace MultiCamApp.Verification;

public sealed class VerificationTableRow
{
    public VerificationVerdict Result { get; set; } = VerificationVerdict.NotChecked;
    public string Camera { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string CameraFolder { get; set; } = "";
    public string MetadataPath { get; set; } = "";
    public string MetadataStatus { get; set; } = "Missing";
    public string ExpectedResolution { get; set; } = "-";
    public string ActualResolution { get; set; } = "-";
    public VerificationMatchStatus ResolutionMatch { get; set; } = VerificationMatchStatus.Na;
    public string ExpectedFps { get; set; } = "-";
    public string ActualFps { get; set; } = "-";
    public VerificationMatchStatus FpsMatch { get; set; } = VerificationMatchStatus.Na;
    public string ExpectedDuration { get; set; } = "-";
    public string ActualDuration { get; set; } = "-";
    public VerificationMatchStatus DurationMatch { get; set; } = VerificationMatchStatus.Na;
    public string ExpectedFrameCount { get; set; } = "-";
    public string FrameCount { get; set; } = "-";
    public string Codec { get; set; } = "-";
    public string Container { get; set; } = "-";
    public string FileSize { get; set; } = "-";
    public string Details { get; set; } = "-";
    public string ExpectedSettingsSource { get; set; } = "-";
    public List<string> WarningMessages { get; } = [];
    public List<string> ErrorMessages { get; } = [];
    public string Recommendation { get; set; } = "";
    public string DetailText { get; set; } = "";
    public bool IsExperiment { get; set; }
    public string ExperimentExpectedFrames { get; set; } = "-";
    public string ExperimentFrameDifference { get; set; } = "-";
    public string ExperimentDroppedFrames { get; set; } = "-";
    public string ExperimentDuplicateFrames { get; set; } = "-";
    public string ExperimentDurationError { get; set; } = "-";
    public string ExperimentMeanInterval { get; set; } = "-";
    public string ExperimentIntervalSd { get; set; } = "-";

    public string AuditStatus { get; set; } = "-";
    public string MeasuredCameraFpsDisplay { get; set; } = "-";
    public string WallDurationDisplay { get; set; } = "-";
    public string QueueDropsDisplay { get; set; } = "-";
    public string DuplicatesDisplay { get; set; } = "-";
    public string PlaceholdersDisplay { get; set; } = "-";
    public string ScientificTimingStatusDisplay { get; set; } = "-";
    public string MetadataCompletenessPercent { get; set; } = "-";
    public string MissingRequiredMetadataFields { get; set; } = "";
    public bool ScientificMetadataComplete { get; set; }
    public string SessionLabel { get; set; } = "";
    public string SessionFolder { get; set; } = "";

    public string ResultDisplay => !string.IsNullOrWhiteSpace(AuditStatus) && AuditStatus != "-"
        ? AuditStatus
        : Result switch
    {
        VerificationVerdict.Pass => "PASS",
        VerificationVerdict.Warning => "WARNING",
        VerificationVerdict.Fail => "FAIL",
        VerificationVerdict.Verifying => "VERIFYING",
        VerificationVerdict.Scanning => "SCANNING",
        _ => "—"
    };

    public string ResolutionMatchDisplay => MatchLabel(ResolutionMatch);
    public string FpsMatchDisplay => MatchLabel(FpsMatch);
    public string DurationMatchDisplay => MatchLabel(DurationMatch);

    public string Device { get; set; } = "-";
    public string RequestedFps { get; set; } = "-";
    public string WriterFps { get; set; } = "-";
    public string ContainerFps { get; set; } = "-";
    public string MeasuredNativeFps { get; set; } = "-";
    public string FpsStabilityGrade { get; set; } = "-";
    public string FramesCapturedDisplay { get; set; } = "-";
    public string FramesWrittenDisplay { get; set; } = "-";
    public string TimestampRowsDisplay { get; set; } = "-";
    public string TimingSourceDisplay { get; set; } = "-";
    public string ContainerDurationDisplay { get; set; } = "-";
    public string ContainerVsWallClockDisplay { get; set; } = "-";
    public string StartOffsetDisplay { get; set; } = "-";
    public string CaptureIntervalMeanMinMaxStdDisplay { get; set; } = "-";
    public string CaptureIntervalP95P99Display { get; set; } = "-";
    public string CaptureGapCountsDisplay { get; set; } = "-";
    public string RecommendedAction { get; set; } = "";

    public static string MatchLabel(VerificationMatchStatus m) => m switch
    {
        VerificationMatchStatus.Yes => "Yes",
        VerificationMatchStatus.Warning => "Warning",
        VerificationMatchStatus.No => "No",
        _ => "—"
    };
}

public sealed class SessionVerificationResult
{
    public VerificationVerdict OverallResult { get; set; } = VerificationVerdict.NotChecked;
    public int ExpectedCameras { get; set; }
    public int DetectedVideos { get; set; }
    public string MissingCameraVideos { get; set; } = "-";
    public double? MinDurationSeconds { get; set; }
    public double? MaxDurationSeconds { get; set; }
    public double? DurationSpreadSeconds { get; set; }
    public double? MinFps { get; set; }
    public double? MaxFps { get; set; }
    public double? FpsSpread { get; set; }
    public string ScientificTimingStatus { get; set; } = "-";
    public string SessionScientificTimingConfidence { get; set; } = ScientificTimingConfidence.Low;
    public string SessionTimingMode { get; set; } = "";
    public bool OriginalFramesOnly { get; set; }
    public long DuplicateFramesTotal { get; set; }
    public long PlaceholderFramesTotal { get; set; }
    public long WriterQueueDropsTotal { get; set; }
    public string TimestampCsvStatus { get; set; } = "-";
    public string RecommendedTrimSource { get; set; } = "-";
    public bool ShowContainerWallClockWarning { get; set; }
    public string ContainerWallClockWarning { get; set; } = "";
    public double? TimestampDriftSeconds { get; set; }
    public double? FrameBasedDurationSeconds { get; set; }
    public double? ContainerDurationSeconds { get; set; }
    public double? WallDurationSeconds { get; set; }
    public double? InterCameraDurationDifferenceSeconds { get; set; }
    public long? InterCameraFrameDifference { get; set; }
    public double? MaxMeasuredFpsDifference { get; set; }
    public double? MaxWallClockDurationDifferenceSec { get; set; }
    public double? MaxStartOffsetSec { get; set; }
    public double? MaxEndOffsetSec { get; set; }
    public long? MaxFrameCountDifference { get; set; }
    public bool FrameCountDifferenceAcceptedBecauseOriginalMode { get; set; }
    public List<string> SessionMessages { get; } = [];
}
