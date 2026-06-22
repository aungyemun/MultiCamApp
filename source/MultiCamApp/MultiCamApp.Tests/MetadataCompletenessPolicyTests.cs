using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class MetadataCompletenessPolicyTests
{
    [Fact]
    public void Assess_ReturnsComplete_ForScientificOriginalCaptureMetadata()
    {
        var metadata = CreateCompleteOriginalCaptureMetadata();

        var result = MetadataCompletenessPolicy.Assess(metadata);

        Assert.True(result.ScientificMetadataComplete);
        Assert.Equal(100.0, result.Percent, precision: 3);
        Assert.Empty(result.MissingRequiredFields);
        Assert.Empty(result.MissingCriticalFields);
    }

    [Fact]
    public void Assess_ReturnsIncompleteWithoutCriticalMissing_WhenNonCriticalFieldMissing()
    {
        var metadata = CreateCompleteOriginalCaptureMetadata();
        metadata.AppVersion = "";

        var result = MetadataCompletenessPolicy.Assess(metadata);

        Assert.False(result.ScientificMetadataComplete);
        Assert.Contains("appVersion", result.MissingRequiredFields);
        Assert.Empty(result.MissingCriticalFields);
    }

    [Fact]
    public void Assess_ReturnsCriticalMissing_WhenScientificTimingFieldMissing()
    {
        var metadata = CreateCompleteOriginalCaptureMetadata();
        metadata.MeasuredCameraFps = 0;

        var result = MetadataCompletenessPolicy.Assess(metadata);

        Assert.False(result.ScientificMetadataComplete);
        Assert.Contains("measuredCameraFps", result.MissingRequiredFields);
        Assert.Contains("measuredCameraFps", result.MissingCriticalFields);
    }

    private static CameraMetadataRecord CreateCompleteOriginalCaptureMetadata()
    {
        var metadata = new CameraMetadataRecord
        {
            AppVersion = "1.0.79",
            BuildNumber = 180,
            ReleaseStage = "experimental",
            SessionName = "test",
            SessionFolderName = "Session_test",
            RecordingTimingMode = OriginalCaptureAuditPolicy.Mode,
            OriginalCaptureMode = true,
            ConstantFrameCountMode = false,
            CameraDeviceName = "USB Camera",
            DeviceIndex = 2,
            Resolution = "1920x1080",
            RequestedFps = 30,
            SelectedDeviceFps = 30,
            WriterFps = 29.68,
            ContainerFps = 29.68,
            MeasuredCameraFps = 29.68,
            EffectivePlaybackFps = 29.68,
            FramesCaptured = 17808,
            FrameCount = 17808,
            DuplicateFrames = 0,
            PlaceholderFrames = 0,
            WriterQueueDrops = 0,
            WallClockDurationSeconds = 600,
            FrameBasedDurationSeconds = 600,
            ContainerDurationSeconds = 600,
            ContainerVsWallClockDifferenceSeconds = 0,
            FirstFrameCaptureUtcTime = DateTime.UtcNow.AddMinutes(-10),
            LastFrameCaptureUtcTime = DateTime.UtcNow,
            FirstFrameCaptureMonotonicSec = 10,
            LastFrameCaptureMonotonicSec = 610,
            FrameTimestampCsvPath = @"C:\tmp\frame_timestamps.csv",
            FrameTimestampCsvWritten = true,
            FrameTimestampCsvRowCount = 17808,
            CaptureIntervalMeanMs = 33.693,
            CaptureIntervalMedianMs = 33.690,
            CaptureIntervalStdMs = 2.0,
            CaptureIntervalMinMs = 31.0,
            CaptureIntervalMaxMs = 38.0,
            CaptureIntervalP95Ms = 35.0,
            CaptureIntervalP99Ms = 37.0,
            FpsStabilityGrade = "STABLE",
            ScientificTimingStatus = CameraAuditStatus.PassOriginalTimingWithNote,
            ScientificTimingMessage = OriginalCaptureAuditPolicy.StableDifferentFpsNote,
            RecommendedAction = OriginalCaptureAuditPolicy.SessionInterpretation
        };

        metadata.PresentMetadataFields.UnionWith([
            "OriginalCaptureMode",
            "ConstantFrameCountMode",
            "DeviceIndex",
            "DuplicateFrames",
            "PlaceholderFrames",
            "WriterQueueDrops",
            "ContainerVsWallClockDifferenceSeconds",
            "FrameTimestampCsvWritten",
            "CaptureIntervalStdMs"
        ]);
        return metadata;
    }
}
