////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

using MultiCamApp.Experiment;

namespace MultiCamApp.Verification;

public enum VerificationVerdict
{
    NotChecked,
    Scanning,
    Verifying,
    Pass,
    Warning,
    Fail
}

public sealed class VideoFileEntry
{
    public string CameraSlot { get; set; } = "";
    public string SessionFolder { get; set; } = "";
    public string SessionLabel { get; set; } = "";
    public string CameraFolder { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string? MetadataPath { get; set; }
    public string? MetadataJsonPath { get; set; }
    public bool MetadataJsonFound => !string.IsNullOrEmpty(MetadataJsonPath) && File.Exists(MetadataJsonPath);
    public bool MetadataFound => !string.IsNullOrEmpty(MetadataPath) && File.Exists(MetadataPath);
    public bool IsMissingVideo { get; set; }
    public VerificationVerdict Status { get; set; } = VerificationVerdict.NotChecked;
}

public sealed class VideoProbeData
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ContainerFormat { get; set; }
    public string? VideoCodec { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public string? AvgFrameRateRaw { get; set; }
    public string? RFrameRateRaw { get; set; }
    public bool ConstantFps { get; set; }
    public double DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public long? FrameCount { get; set; }
    public long? BitRate { get; set; }
    public string? PixelFormat { get; set; }
    public bool HasVideoStream { get; set; }
    public bool HasAudioStream { get; set; }
}

public sealed class ExpectedCameraSettings
{
    public string CameraSlot { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Fps { get; set; }
    public string? Codec { get; set; }
    public string? ContainerFormat { get; set; }
    public double? DurationSeconds { get; set; }
    public long? FrameCount { get; set; }
    public string Source { get; set; } = "inferred";
}

public sealed class VideoVerificationResult
{
    public VideoFileEntry Entry { get; set; } = new();
    public VideoProbeData? Probe { get; set; }
    public ExpectedCameraSettings? Expected { get; set; }
    public CameraMetadataRecord? Metadata { get; set; }
    public VerificationVerdict Verdict { get; set; } = VerificationVerdict.NotChecked;
    public string ExpectedResolutionDisplay { get; set; } = "-";
    public string ActualResolutionDisplay { get; set; } = "-";
    public string ExpectedFpsDisplay { get; set; } = "-";
    public string ActualFpsDisplay { get; set; } = "-";
    public string ExpectedDurationDisplay { get; set; } = "-";
    public string DurationDisplay { get; set; } = "-";
    public string ExpectedFrameCountDisplay { get; set; } = "-";
    public string FrameCountDisplay { get; set; } = "-";
    public string NotesDisplay { get; set; } = "";
    public string CodecDisplay { get; set; } = "-";
    public string ContainerDisplay { get; set; } = "-";
    public string FileSizeDisplay { get; set; } = "-";
    public string ScientificTimingStatus { get; set; } = "-";
    public string ScientificTimingMessage { get; set; } = "";
    public double MetadataCompletenessPercent { get; set; }
    public string MissingRequiredMetadataFields { get; set; } = "";
    public bool ScientificMetadataComplete { get; set; }
    public string MissingCriticalMetadataFields { get; set; } = "";
    public string ConstantFpsDisplay { get; set; } = "-";
    public string TimingStatusDisplay { get; set; } = "-";
    public double? TimestampDriftSeconds { get; set; }
    public double? ContainerVsWallClockDifferenceSeconds { get; set; }
    public double? FrameBasedDurationSeconds { get; set; }
    public double? ContainerDurationSeconds { get; set; }
    public double? WallDurationSeconds { get; set; }
    public VerificationMatchStatus ResolutionMatch { get; set; } = VerificationMatchStatus.Na;
    public VerificationMatchStatus FpsMatch { get; set; } = VerificationMatchStatus.Na;
    public VerificationMatchStatus DurationMatch { get; set; } = VerificationMatchStatus.Na;
    public string Recommendation { get; set; } = "";
    public string DetailText { get; set; } = "";
    public List<string> WarningMessages { get; } = [];
    public List<string> ErrorMessages { get; } = [];
    public List<string> Messages { get; } = [];
    public ExperimentVerificationResult? Experiment { get; set; }
}

public sealed class VerificationSummary
{
    public VerificationVerdict OverallVerdict { get; set; } = VerificationVerdict.NotChecked;
    public string SelectedFolder { get; set; } = "";
    public string ExpectedSettingsSource { get; set; } = "-";
    public int TotalVideosFound { get; set; }
    public int VideosVerified { get; set; }
    public int VideosPassed { get; set; }
    public int VideosWarning { get; set; }
    public int VideosFailed { get; set; }
    public string ExpectedFpsSummary { get; set; } = "-";
    public string ExpectedResolutionSummary { get; set; } = "-";
    public string ExpectedCodecSummary { get; set; } = "-";
    public string SessionDurationMatch { get; set; } = "-";
    public string FpsSpreadDisplay { get; set; } = "-";
    public double? DurationSpreadSeconds { get; set; }
    public double? FpsSpread { get; set; }
    public double? MinDurationSeconds { get; set; }
    public double? MaxDurationSeconds { get; set; }
    public double? MinFps { get; set; }
    public double? MaxFps { get; set; }
    public List<string> SessionMessages { get; } = [];
}

public sealed class VerificationReport
{
    public string AppVersion { get; set; } = "";
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;
    public VerificationSummary Summary { get; set; } = new();
    public SessionVerificationResult SessionResult { get; set; } = new();
    public List<VideoVerificationResult> Videos { get; } = [];
    public List<VerificationTableRow> TableRows { get; } = [];
    public List<string> LogLines { get; } = [];
    public BehaviorSessionVerificationResult? Behavior { get; set; }
    public List<BehaviorSessionVerificationResult> BehaviorSessions { get; } = [];
    public List<RecordingSessionAuditResult> SessionAudits { get; } = [];
    public string VerificationProfileUsed { get; set; } = "Standard";
}

public sealed class BehaviorSessionVerificationResult
{
    public string SessionFolder { get; set; } = "";
    public string SessionLabel { get; set; } = "";
    public VerificationVerdict FinalVerdict { get; set; } = VerificationVerdict.NotChecked;
    public string SummaryMessage { get; set; } = "";
    public double RequestedFps { get; set; }
    public double ActualFpsAvg { get; set; }
    public double DurationSecondsAvg { get; set; }
    public long FrameCountAvg { get; set; }
    public double CalculatedFpsAvg { get; set; }
    public double CameraDurationDifferenceSec { get; set; }
    public long CameraFrameCountDifference { get; set; }
    public string ResolutionMatch { get; set; } = "";
    public string RecordingStatus { get; set; } = "";
    public string CropRecommendation { get; set; } = "";
}

public sealed class VerificationProgressUpdate
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string Message { get; init; } = "";
    public VideoVerificationResult? CompletedVideo { get; init; }
    public VerificationTableRow? CompletedTableRow { get; init; }
    public bool IsFinished { get; init; }
    public VerificationReport? Report { get; init; }
}
