////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Verification;

public sealed record MetadataCompletenessResult(
    double Percent,
    IReadOnlyList<string> MissingRequiredFields,
    bool ScientificMetadataComplete,
    IReadOnlyList<string> MissingCriticalFields);

public static class MetadataCompletenessPolicy
{
    public static readonly string[] RequiredOriginalCaptureFields =
    [
        "appVersion",
        "buildNumber",
        "releaseStage",
        "sessionName",
        "sessionFolder",
        "recordingTimingMode",
        "originalCaptureMode",
        "constantFrameCountMode",
        "deviceName",
        "deviceIndex",
        "resolution",
        "requestedFps",
        "selectedFps",
        "writerFps",
        "containerFps",
        "measuredCameraFps",
        "effectiveRecordedFps",
        "framesCaptured",
        "framesWritten",
        "duplicateFrames",
        "placeholderFrames",
        "writerQueueDrops",
        "wallClockDurationSec",
        "frameBasedDurationSec",
        "containerDurationSec",
        "containerVsWallClockDifferenceSec",
        "firstFrameCaptureUtcTime",
        "lastFrameCaptureUtcTime",
        "firstFrameCaptureMonotonicSec",
        "lastFrameCaptureMonotonicSec",
        "frameTimestampCsvPath",
        "frameTimestampCsvWritten",
        "frameTimestampCsvRowCount",
        "captureIntervalMeanMs",
        "captureIntervalMedianMs",
        "captureIntervalStdMs",
        "captureIntervalMinMs",
        "captureIntervalMaxMs",
        "captureIntervalP95Ms",
        "captureIntervalP99Ms",
        "fpsStabilityGrade",
        "scientificTimingStatus",
        "scientificTimingMessage",
        "recommendedAction"
    ];

    public static readonly string[] CriticalOriginalCaptureFields =
    [
        "recordingTimingMode",
        "framesCaptured",
        "framesWritten",
        "duplicateFrames",
        "placeholderFrames",
        "writerQueueDrops",
        "measuredCameraFps",
        "firstFrameCaptureMonotonicSec",
        "lastFrameCaptureMonotonicSec",
        "frameTimestampCsvWritten",
        "frameTimestampCsvRowCount"
    ];

    public static MetadataCompletenessResult Assess(CameraMetadataRecord? metadata)
    {
        if (metadata == null)
            return new MetadataCompletenessResult(0, RequiredOriginalCaptureFields, false, CriticalOriginalCaptureFields);

        var missing = RequiredOriginalCaptureFields
            .Where(field => !HasRequiredField(metadata, field))
            .ToArray();
        var missingCritical = CriticalOriginalCaptureFields
            .Where(field => !HasRequiredField(metadata, field))
            .ToArray();
        var present = RequiredOriginalCaptureFields.Length - missing.Length;
        var percent = RequiredOriginalCaptureFields.Length == 0
            ? 100.0
            : present * 100.0 / RequiredOriginalCaptureFields.Length;

        return new MetadataCompletenessResult(
            percent,
            missing,
            missing.Length == 0,
            missingCritical);
    }

    private static bool HasRequiredField(CameraMetadataRecord m, string field) => field switch
    {
        "appVersion" => HasText(m.AppVersion),
        "buildNumber" => m.BuildNumber > 0,
        "releaseStage" => HasText(m.ReleaseStage),
        "sessionName" => HasText(m.SessionName),
        "sessionFolder" => HasText(m.SessionFolderName),
        "recordingTimingMode" => HasText(m.RecordingTimingMode),
        "originalCaptureMode" => m.HasField("OriginalCaptureMode", "originalCaptureMode", "Original capture mode"),
        "constantFrameCountMode" => m.HasField("ConstantFrameCountMode", "constantFrameCountMode", "Constant frame count mode"),
        "deviceName" => HasText(m.CameraDeviceName),
        "deviceIndex" => m.HasField("DeviceIndex", "deviceIndex", "DirectShow index"),
        "resolution" => HasText(m.Resolution) || (m.PixelWidth > 0 && m.PixelHeight > 0),
        "requestedFps" => m.RequestedFps > 0,
        "selectedFps" => m.SelectedDeviceFps > 0,
        "writerFps" => m.WriterFps > 0 || m.RecordingWriterFps > 0,
        "containerFps" => m.ContainerFps > 0,
        "measuredCameraFps" => m.MeasuredCameraFps > 0,
        "effectiveRecordedFps" => m.EffectivePlaybackFps > 0,
        "framesCaptured" => m.FramesCaptured > 0,
        "framesWritten" => m.FrameCount > 0,
        "duplicateFrames" => m.HasField("DuplicateFrames", "duplicateFrames", "Duplicate frames", "Duplicated frames"),
        "placeholderFrames" => m.HasField("PlaceholderFrames", "placeholderFrames", "Placeholder frames"),
        "writerQueueDrops" => m.HasField("WriterQueueDrops", "writerQueueDrops", "Writer drops"),
        "wallClockDurationSec" => m.WallClockDurationSeconds > 0,
        "frameBasedDurationSec" => m.FrameBasedDurationSeconds > 0,
        "containerDurationSec" => m.ContainerDurationSeconds > 0,
        "containerVsWallClockDifferenceSec" => m.HasField("ContainerVsWallClockDifferenceSeconds", "containerVsWallClockDifferenceSec", "Container vs wall-clock difference seconds"),
        "firstFrameCaptureUtcTime" => m.FirstFrameCaptureUtcTime.HasValue,
        "lastFrameCaptureUtcTime" => m.LastFrameCaptureUtcTime.HasValue,
        "firstFrameCaptureMonotonicSec" => m.FirstFrameCaptureMonotonicSec > 0,
        "lastFrameCaptureMonotonicSec" => m.LastFrameCaptureMonotonicSec > 0,
        "frameTimestampCsvPath" => HasText(m.FrameTimestampCsvPath),
        "frameTimestampCsvWritten" => m.HasField("FrameTimestampCsvWritten", "frameTimestampCsvWritten"),
        "frameTimestampCsvRowCount" => m.FrameTimestampCsvRowCount > 0,
        "captureIntervalMeanMs" => m.CaptureIntervalMeanMs > 0,
        "captureIntervalMedianMs" => m.CaptureIntervalMedianMs > 0,
        "captureIntervalStdMs" => m.HasField("CaptureIntervalStdMs", "Capture interval std ms", "captureIntervalStdMs"),
        "captureIntervalMinMs" => m.CaptureIntervalMinMs > 0,
        "captureIntervalMaxMs" => m.CaptureIntervalMaxMs > 0,
        "captureIntervalP95Ms" => m.CaptureIntervalP95Ms > 0,
        "captureIntervalP99Ms" => m.CaptureIntervalP99Ms > 0,
        "fpsStabilityGrade" => HasText(m.FpsStabilityGrade),
        "scientificTimingStatus" => HasText(m.ScientificTimingStatus),
        "scientificTimingMessage" => HasText(m.ScientificTimingMessage),
        "recommendedAction" => HasText(m.RecommendedAction),
        _ => false
    };

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
