////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Recording;
using System.Text.Json.Serialization;

namespace MultiCamApp.Metadata;

public sealed class CameraRecordingMetadata
{
    [JsonPropertyName("metadataSchemaVersion")]
    public string MetadataSchemaVersion { get; set; } = "2.0";
    [JsonPropertyName("txtMetadataPrivacySafe")]
    public bool TxtMetadataPrivacySafe { get; set; } = true;
    [JsonPropertyName("privacySafe")]
    public bool PrivacySafe { get; set; } = true;
    [JsonPropertyName("absolutePathsPersisted")]
    public bool AbsolutePathsPersisted { get; set; }
    [JsonPropertyName("hardwareIdentifiersPersisted")]
    public bool HardwareIdentifiersPersisted { get; set; }
    public string RecordingTimingMode { get; set; } = RecordingTimingModes.OriginalCapture;
    public bool OriginalCaptureMode { get; set; } = true;
    public string AppName { get; set; } = "MultiCamApp";
    public string AppVersion { get; set; } = "0.0.1";
    public int BuildNumber { get; set; } = 1;
    public string ReleaseStage { get; set; } = "experimental";
    public string SessionName { get; set; } = "";
    public string SessionTitleOriginal { get; set; } = "";
    public string SessionFolderName { get; set; } = "";
    public DateTime? RecordingDateTimeLocal { get; set; }
    public string CameraSlot { get; set; } = "";
    public string CameraDeviceName { get; set; } = "";
    public string? CameraHardwareId { get; set; }
    public string Backend { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public int DeviceIndex { get; set; } = -1;
    public string Resolution { get; set; } = "";
    public string RequestedResolution { get; set; } = "";
    public string SelectedResolution { get; set; } = "";
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public double RequestedFps { get; set; }
    public double SelectedDeviceFps { get; set; }
    public double RecordingWriterFps { get; set; }
    public uint FrameRateNumerator { get; set; }
    public uint FrameRateDenominator { get; set; } = 1;
    public double ActualFps { get; set; }
    public string Codec { get; set; } = "H264";
    public string ContainerFormat { get; set; } = "MP4";
    public string VideoSubtype { get; set; } = "H264";
    public string FilePath { get; set; } = "";
    public DateTime RecordingStartTimeLocal { get; set; }
    public string ModeSelectionReason { get; set; } = "";
    public bool IsNativeRecommended { get; set; }
    public DateTime RecordingStartTime { get; set; }
    public DateTime RecordingStopTime { get; set; }
    public DateTime? SessionStartLocalTime { get; set; }
    public DateTime? SessionStartUtcTime { get; set; }
    public long SessionStartMonotonicTicks { get; set; }
    public double SessionStartMonotonicSec { get; set; }
    public DateTime? SessionStopLocalTime { get; set; }
    public DateTime? SessionStopUtcTime { get; set; }
    public long SessionStopMonotonicTicks { get; set; }
    public double SessionStopMonotonicSec { get; set; }
    public double SessionWallClockDurationSec { get; set; }
    public double SessionMonotonicDurationSec { get; set; }
    public DateTime? RecordingRequestedStartLocalTime { get; set; }
    public DateTime? RecordingRequestedStartUtcTime { get; set; }
    public double RecordingRequestedStartMonotonicSec { get; set; }
    public DateTime? CameraRecordingStartLocalTime { get; set; }
    public DateTime? CameraRecordingStartUtcTime { get; set; }
    public double CameraRecordingStartMonotonicSec { get; set; }
    public DateTime? FirstFrameLocalTime { get; set; }
    public DateTime? FirstFrameUtcTime { get; set; }
    public double FirstFrameMonotonicSec { get; set; }
    public DateTime? LastFrameLocalTime { get; set; }
    public DateTime? LastFrameUtcTime { get; set; }
    public double LastFrameMonotonicSec { get; set; }
    public DateTime? CameraRecordingStopLocalTime { get; set; }
    public DateTime? CameraRecordingStopUtcTime { get; set; }
    public double CameraRecordingStopMonotonicSec { get; set; }
    public DateTime? WriterClosedLocalTime { get; set; }
    public DateTime? WriterClosedUtcTime { get; set; }
    public double WriterClosedMonotonicSec { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan MonotonicDuration { get; set; }
    public double SessionStartMonotonicSeconds { get; set; }
    public double CameraStartMonotonicSeconds { get; set; }
    public double FirstFrameMonotonicSeconds { get; set; }
    public double LastFrameMonotonicSeconds { get; set; }
    public double StopRequestedMonotonicSeconds { get; set; }
    public double WriterClosedMonotonicSeconds { get; set; }
    public double WallDurationSeconds { get; set; }
    public double FrameBasedDurationSeconds { get; set; }
    public double ContainerDurationSeconds { get; set; }
    public double TimestampDriftSeconds { get; set; }
    public double WallClockDurationSeconds { get; set; }
    public double ContainerVsWallClockDifferenceSeconds { get; set; }
    public double SelectedFps { get; set; }
    public double WriterFps { get; set; }
    public double ContainerFps { get; set; }
    public double MeasuredCameraFps { get; set; }
    public double EffectivePlaybackFps { get; set; }
    public double CaptureIntervalMeanMs { get; set; }
    public double CaptureIntervalMedianMs { get; set; }
    public double CaptureIntervalMinMs { get; set; }
    public double CaptureIntervalMaxMs { get; set; }
    public double CaptureIntervalP95Ms { get; set; }
    public double CaptureIntervalP99Ms { get; set; }
    public double CaptureIntervalStdMs { get; set; }
    public long CaptureIntervalCount { get; set; }
    public string CaptureIntervalStatsMessage { get; set; } = "";
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
    public int ActiveCameraCount { get; set; }
    public string[] ActiveCameraSlots { get; set; } = [];
    public double InterCameraStartOffsetMs { get; set; }
    public double InterCameraStopOffsetMs { get; set; }
    public string ScientificTimingMessage { get; set; } = "";
    public long FrameCount { get; set; }
    public long FramesCaptured { get; set; }
    public long DroppedFrames { get; set; }
    public long DuplicatedFrames { get; set; }
    public long DuplicateFrames { get; set; }
    public long PlaceholderFrames { get; set; }
    public bool ConstantFrameCountMode { get; set; }
    public long WriterQueueDrops { get; set; }
    public long MaxConsecutiveLateFrames { get; set; }
    public long MaxConsecutiveNoFrame { get; set; }
    public double AverageCaptureIntervalMs { get; set; }
    public double MinCaptureIntervalMs { get; set; }
    public double MaxCaptureIntervalMs { get; set; }
    public double CaptureJitterMs { get; set; }
    public long InterCameraFrameDiff { get; set; }
    public double InterCameraDurationDiffSec { get; set; }
    public string TimestampPrecision { get; set; } =
        "UTC wall clock (start/stop) + local time (filename) + monotonic QPC (duration)";
    public string RecordingApi { get; set; } = "LowLagMediaRecording";
    public string TimingAccuracy { get; set; } = "";
    public string OsVersion { get; set; } = Environment.OSVersion.VersionString;
    public string PcName { get; set; } = Environment.MachineName;
    public string ComputerName { get; set; } = Environment.MachineName;
    public string CpuName { get; set; } = "";
    public double RamGb { get; set; }
    public string ScientificTimingStatus { get; set; } = "";
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
    public bool FocusAppliedByUser { get; set; }
    public string FocusModeSummary { get; set; } = "";
    public bool AutoExposureRequested { get; set; }
    public bool AutoExposureApplyAttempted { get; set; }
    public bool? AutoExposureApplySucceeded { get; set; }
    public string AutoExposureReadbackValue { get; set; } = "unavailable";
    public bool? ManualExposureSupported { get; set; }
    public double? ManualExposureRequestedValue { get; set; }
    public string ManualExposureReadbackValue { get; set; } = "unavailable";
    public bool LowLightCompensationOffRequested { get; set; }
    public bool? LowLightCompensationOffConfirmed { get; set; }
    public string ExposureWarning { get; set; } = "";
    public string AutoWhiteBalanceStatus { get; set; } = "Unavailable";
    public string WhiteBalanceReadbackValue { get; set; } = "Unavailable";
    // Environmental lock — set at recording start when user has locked camera hardware parameters.
    public bool EnvironmentalLockActive { get; set; }
    public bool FocusHardwareLocked { get; set; }
    public uint FocusLockedAtSteps { get; set; }
    public bool ExposureHardwareLocked { get; set; }
    public double ExposureLockedAtSeconds { get; set; }
    public bool WhiteBalanceHardwareLocked { get; set; }
    public uint WhiteBalanceLockedAtK { get; set; }
    public bool IsoHardwareLocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool PrivacyMode { get; set; } = true;
    public bool HiddenRecordingAllowed { get; set; }
    public bool CameraReleasedOnStop { get; set; } = true;
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
    public RecordingDiagnosticsMetadataSummary? RecordingDiagnostics { get; set; }
}
