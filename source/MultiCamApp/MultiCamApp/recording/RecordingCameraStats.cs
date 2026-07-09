////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Capture;
using MultiCamApp.Metadata;
using System.Text.Json.Serialization;

namespace MultiCamApp.Recording;

/// <summary>Final per-camera recording metrics from the writer (OpenCV path).</summary>
public sealed record RecordingCameraStats
{
    [JsonPropertyName("metadataSchemaVersion")]
    public string MetadataSchemaVersion { get; init; } = "2.0";
    [JsonPropertyName("txtMetadataPrivacySafe")]
    public bool TxtMetadataPrivacySafe { get; init; } = true;
    [JsonPropertyName("privacySafe")]
    public bool PrivacySafe { get; init; } = true;
    [JsonPropertyName("absolutePathsPersisted")]
    public bool AbsolutePathsPersisted { get; init; }
    [JsonPropertyName("hardwareIdentifiersPersisted")]
    public bool HardwareIdentifiersPersisted { get; init; }
    public string RecordingTimingMode { get; init; } = RecordingTimingModes.OriginalCapture;
    public bool OriginalCaptureMode { get; init; } = true;
    public string CameraSlot { get; init; } = "";
    public string CameraDeviceName { get; init; } = "";
    public string Backend { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public int DeviceIndex { get; init; } = -1;
    public string RequestedResolution { get; init; } = "";
    public string SelectedResolution { get; init; } = "";
    public string OutputFilePath { get; init; } = "";
    public double RequestedFps { get; init; }
    public double SelectedDeviceFps { get; init; }
    public double SelectedFps { get; init; }
    public double RecordingWriterFps { get; init; }
    public double WriterFps { get; init; }
    public double ContainerFps { get; init; }
    public double MeasuredWriterFps { get; init; }
    public double MeasuredCameraFps { get; init; }
    public double EffectivePlaybackFps { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FramesWritten { get; init; }
    public long FramesCaptured { get; init; }
    public long WriterQueueDrops { get; init; }
    public long WriterFramesDequeued { get; init; }
    public int WriterQueueCapacity { get; init; }
    public int WriterQueueDepthMax { get; init; }
    public int WriterQueueDepthAtStop { get; init; }
    public long WriterQueueFullCount { get; init; }
    public double WriterLoopStartMonotonicSeconds { get; init; }
    public double WriterLoopStopMonotonicSeconds { get; init; }
    public double AverageVideoWriterWriteMs { get; init; }
    public double MaxVideoWriterWriteMs { get; init; }
    public string DroppedFrameTimestamps { get; init; } = "";
    public bool PreviewEnabledDuringRecording { get; init; }
    public double RecordingPreviewFpsCap { get; init; }
    public long PreviewFramesRenderedDuringRecording { get; init; }
    public long FileSizeBytes { get; init; }
    public double FileBitrateMbps { get; init; }
    public DateTime StartWallClockUtc { get; init; }
    public DateTime StopWallClockUtc { get; init; }
    public DateTime StartWallClockLocal { get; init; }
    public DateTime StopWallClockLocal { get; init; }
    public DateTime? SessionStartLocalTime { get; init; }
    public DateTime? SessionStartUtcTime { get; init; }
    public long SessionStartMonotonicTicks { get; init; }
    public double SessionStartMonotonicSec { get; init; }
    public DateTime? SessionStopLocalTime { get; init; }
    public DateTime? SessionStopUtcTime { get; init; }
    public long SessionStopMonotonicTicks { get; init; }
    public double SessionStopMonotonicSec { get; init; }
    public double SessionWallClockDurationSec { get; init; }
    public double SessionMonotonicDurationSec { get; init; }
    public DateTime? RecordingRequestedStartLocalTime { get; init; }
    public DateTime? RecordingRequestedStartUtcTime { get; init; }
    public double RecordingRequestedStartMonotonicSec { get; init; }
    public DateTime? CameraRecordingStartLocalTime { get; init; }
    public DateTime? CameraRecordingStartUtcTime { get; init; }
    public double CameraRecordingStartMonotonicSec { get; init; }
    public DateTime? FirstFrameLocalTime { get; init; }
    public DateTime? FirstFrameUtcTime { get; init; }
    public double FirstFrameMonotonicSec { get; init; }
    public DateTime? LastFrameLocalTime { get; init; }
    public DateTime? LastFrameUtcTime { get; init; }
    public double LastFrameMonotonicSec { get; init; }
    public DateTime? CameraRecordingStopLocalTime { get; init; }
    public DateTime? CameraRecordingStopUtcTime { get; init; }
    public double CameraRecordingStopMonotonicSec { get; init; }
    public DateTime? WriterClosedLocalTime { get; init; }
    public DateTime? WriterClosedUtcTime { get; init; }
    public double WriterClosedMonotonicSec { get; init; }
    public double StartMonotonicSeconds { get; init; }
    public double StopMonotonicSeconds { get; init; }
    public double FirstFrameMonotonicSeconds { get; init; }
    public double LastFrameMonotonicSeconds { get; init; }
    public double StopRequestedMonotonicSeconds { get; init; }
    public double WriterClosedMonotonicSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public double WallDurationSeconds { get; init; }
    public double WallClockDurationSeconds { get; init; }
    public double FrameBasedDurationSeconds { get; init; }
    public double ContainerDurationSeconds { get; init; }
    /// <summary>Legacy alias. Prefer ContainerVsWallClockDifferenceSeconds.</summary>
    public double TimestampDriftSeconds { get; init; }
    public double ContainerVsWallClockDifferenceSeconds { get; init; }
    public string Codec { get; init; } = "";
    public string Container { get; init; } = "";
    public string Status { get; init; } = "completed";
    public string FrameTimestampCsvPath { get; init; } = "";
    public bool FrameTimestampCsvWritten { get; init; }
    public long FrameTimestampCsvRowCount { get; init; }
    public DateTime? FirstFrameCaptureUtcTime { get; init; }
    public DateTime? LastFrameCaptureUtcTime { get; init; }
    public double FirstFrameCaptureMonotonicSec { get; init; }
    public double LastFrameCaptureMonotonicSec { get; init; }
    public double FirstToLastFrameDurationSec { get; init; }
    public string TrimRecommendedTimeSource { get; init; } = "";
    public string TrimWarning { get; init; } = "";
    public string ScientificTrimStartReference { get; init; } = "";
    public string ScientificTrimEndReference { get; init; } = "";
    public bool SupportsTimestampBasedTrimming { get; init; }

    public string AppName { get; init; } = "MultiCamApp";
    public string AppVersion { get; init; } = "";
    public int BuildNumber { get; init; }
    public string ReleaseStage { get; init; } = "experimental";
    public string SessionName { get; init; } = "";
    public string SessionTitleOriginal { get; init; } = "";
    public string SessionFolderName { get; init; } = "";
    public DateTime? RecordingDateTimeLocal { get; init; }
    public string? CameraHardwareId { get; init; }
    public string RecordingApi { get; init; } = "OpenCV-VideoWriter";
    public string ComputerName { get; init; } = Environment.MachineName;
    public string CpuName { get; init; } = "";
    public double RamGb { get; init; }
    public string ScientificTimingStatus { get; init; } = "";
    public string ScientificTimingMessage { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
    public int ActiveCameraCount { get; init; }
    public string[] ActiveCameraSlots { get; init; } = [];
    public double InterCameraStartOffsetMs { get; init; }
    public double InterCameraStopOffsetMs { get; init; }
    public long InterCameraFrameDifference { get; init; }
    public bool AutoFocusRequested { get; init; }
    public bool AutoFocusApplyAttempted { get; init; }
    public bool? AutoFocusApplySucceeded { get; init; }
    public string AutoFocusReadbackValue { get; init; } = "unavailable";
    public bool? ManualFocusSupported { get; init; }
    public double? ManualFocusRequestedValue { get; init; }
    public string ManualFocusReadbackValue { get; init; } = "unavailable";
    public string FocusControlMode { get; init; } = "unavailable";
    public string FocusWarning { get; init; } = "";
    public bool FocusAppliedByUser { get; init; }
    public string FocusModeSummary { get; init; } = "";
    public bool AutoExposureRequested { get; init; }
    public bool AutoExposureApplyAttempted { get; init; }
    public bool? AutoExposureApplySucceeded { get; init; }
    public string AutoExposureReadbackValue { get; init; } = "unavailable";
    public bool? ManualExposureSupported { get; init; }
    public double? ManualExposureRequestedValue { get; init; }
    public string ManualExposureReadbackValue { get; init; } = "unavailable";
    public bool LowLightCompensationOffRequested { get; init; }
    public bool? LowLightCompensationOffConfirmed { get; init; }
    public string ExposureWarning { get; init; } = "";
    public string AutoWhiteBalanceStatus { get; init; } = "Unavailable";
    public string WhiteBalanceReadbackValue { get; init; } = "Unavailable";
    public bool EnvironmentalLockActive { get; init; }
    public bool FocusHardwareLocked { get; init; }
    public uint FocusLockedAtSteps { get; init; }
    public bool ExposureHardwareLocked { get; init; }
    public double ExposureLockedAtSeconds { get; init; }
    public bool WhiteBalanceHardwareLocked { get; init; }
    public uint WhiteBalanceLockedAtK { get; init; }
    public bool IsoHardwareLocked { get; init; }

    public TimeSpan MonotonicDuration => TimeSpan.FromSeconds(DurationSeconds);

    public string DurationHms => MonotonicDuration.ToString(@"hh\:mm\:ss\.fff");

    public string Resolution => CaptureResolutionPreset.ToLabel(Width, Height);

    public bool ExperimentMode { get; init; }
    public double TargetDurationSeconds { get; init; }
    public double TargetFps { get; init; }
    public long ExpectedFrames { get; init; }
    public long DroppedFrames { get; init; }
    public long DuplicateFrames { get; init; }
    public long PlaceholderFrames { get; init; }
    public bool ConstantFrameCountMode { get; init; }
    public bool StrictFrameValidation { get; init; } = true;
    public double MinFrameIntervalMs { get; init; }
    public double MaxFrameIntervalMs { get; init; }
    public double AverageFrameIntervalMs { get; init; }
    public double FrameIntervalStdDevMs { get; init; }
    public double MinCaptureIntervalMs { get; init; }
    public double MaxCaptureIntervalMs { get; init; }
    public double AverageCaptureIntervalMs { get; init; }
    public double CaptureJitterMs { get; init; }
    public double CaptureIntervalMeanMs { get; init; }
    public double CaptureIntervalMedianMs { get; init; }
    public double CaptureIntervalMinMs { get; init; }
    public double CaptureIntervalMaxMs { get; init; }
    public double CaptureIntervalP95Ms { get; init; }
    public double CaptureIntervalP99Ms { get; init; }
    public double CaptureIntervalStdMs { get; init; }
    public long CaptureIntervalCount { get; init; }
    public string CaptureIntervalStatsMessage { get; init; } = "";
    public double MeasuredCameraFpsFromFirstLastFrame { get; init; }
    public double MeasuredCameraFpsFromMeanInterval { get; init; }
    public double ExpectedIntervalMs { get; init; }
    public double RequestedExpectedIntervalMs { get; init; }
    public double MeanIntervalErrorMs { get; init; }
    public double AbsoluteMeanIntervalErrorMs { get; init; }
    public long LongGapCount { get; init; }
    public long ShortGapCount { get; init; }
    public long SevereLongGapCount { get; init; }
    public double JitterScoreMs { get; init; }
    public string FpsStabilityGrade { get; init; } = "";
    public long MaxConsecutiveLateFrames { get; init; }
    public long MaxConsecutiveNoFrame { get; init; }
    public double FpsDrift { get; init; }
    public DateTime FirstFrameUtc { get; init; }
    public DateTime LastFrameUtc { get; init; }
    public string ExperimentResult { get; init; } = "";
    public bool LocomotorMode { get; init; }
    public double MinimumAnalysisDurationSeconds { get; init; }
    public double PlannedRecordingDurationSeconds { get; init; }
    public bool UsableFor10MinAnalysis { get; init; }
    public RecordingDiagnosticsMetadataSummary? RecordingDiagnostics { get; init; }
}
