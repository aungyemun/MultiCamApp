using MultiCamApp.Metadata;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class ScientificTimingAssessorTests
{
    [Fact]
    public void Assess_ReturnsPass_ForValidRecording()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1000,
            FramesCaptured = 1000,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.9,
            InterCameraFrameDifference = 2,
            InterCameraStartOffsetMs = 40
        });

        Assert.Equal("PASS", status);
        Assert.Contains("frame count", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsPassWithWarning_WhenContainerFpsDiffersFromMeasuredCameraFps()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            FramesWritten = 48730,
            FramesCaptured = 48730,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.06
        });

        Assert.Equal("PASS_WITH_WARNING", status);
    }

    [Fact]
    public void Assess_ReturnsPassOriginalTiming_ForStableOriginalCapture()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 18000,
            FramesCaptured = 18000,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30.0,
            DuplicateFrames = 0,
            PlaceholderFrames = 0,
            QueueDrops = 0,
            CaptureIntervalCount = 17999,
            CaptureIntervalStdMs = 3.0
        });

        Assert.Equal("PASS_ORIGINAL_TIMING", status);
        Assert.Contains("Real frames only; no duplicates/placeholders", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsPassOriginalTimingWithNote_ForStableSlightlyLowFps()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 17808,
            FramesCaptured = 17808,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.68,
            DuplicateFrames = 0,
            PlaceholderFrames = 0,
            QueueDrops = 0,
            CaptureIntervalCount = 17807,
            CaptureIntervalStdMs = 5.0
        });

        Assert.Equal("PASS_ORIGINAL_TIMING_WITH_NOTE", status);
        Assert.Contains("Frame counts may differ because cameras delivered real frames at different measured FPS", message);
    }

    [Fact]
    public void Assess_ReturnsFail_WhenOriginalCaptureHasDuplicateFrames()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 18001,
            FramesCaptured = 18000,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            DuplicateFrames = 1
        });

        Assert.Equal("FAIL", status);
        Assert.Contains("duplicateFrames", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_AllowsOriginalCaptureInterCameraFrameDifferences()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            OriginalCaptureMode = true,
            FramesWritten = 17808,
            FramesCaptured = 17808,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.68,
            InterCameraFrameDifference = 200,
            InterCameraStartOffsetMs = 40
        });

        Assert.Equal("PASS_ORIGINAL_TIMING_WITH_NOTE", status);
    }

    [Fact]
    public void Assess_ReturnsFail_WhenQueueDropsPresent()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            FramesWritten = 100,
            FramesCaptured = 100,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            QueueDrops = 1
        });

        Assert.Equal("FAIL", status);
        Assert.Contains("queue drops", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsFail_WhenOriginalCaptureQueueDropsPresent()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 100,
            FramesCaptured = 101,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.68,
            QueueDrops = 1
        });

        Assert.Equal("FAIL", status);
        Assert.Contains("queue drops", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsFail_WhenOriginalCapturePlaceholdersPresent()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 100,
            FramesCaptured = 100,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            PlaceholderFrames = 1
        });

        Assert.Equal("FAIL", status);
        Assert.Contains("placeholders", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsFail_WhenOriginalCaptureTimestampCsvMissing()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 100,
            FramesCaptured = 100,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            RequireFrameTimestampCsvValidation = true,
            FrameTimestampCsvWritten = false,
            FrameTimestampCsvRowCount = 0
        });

        Assert.Equal("FAIL", status);
        Assert.Contains("Frame timestamp CSV", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsWarning_WhenConstantFrameCountDuplicatesPresent()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            FramesWritten = 900,
            FramesCaptured = 895,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.8,
            DuplicateFrames = 5,
            ConstantFrameCountMode = true
        });

        Assert.Equal("PASS_WITH_WARNING", status);
        Assert.Contains("duplicate", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_ReturnsFail_WhenInterCameraFrameDifferenceTooLarge()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1000,
            FramesCaptured = 1000,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraFrameDifference = 8
        });

        Assert.Equal("FAIL", status);
    }

    [Fact]
    public void Assess_IsNeverEmpty_ForTypicalOpenCvRecording()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            FramesWritten = 29984,
            FramesCaptured = 29984,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.06
        });

        Assert.False(string.IsNullOrWhiteSpace(status));
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Assess_OriginalCapturePerfect30Fps_ReturnsPassOriginalTiming()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(
            framesWritten: 18000,
            requestedFps: 30,
            writerFps: 30,
            containerFps: 30,
            measuredFps: 30.000));

        Assert.Equal("PASS_ORIGINAL_TIMING", status);
        Assert.Contains("Real frames only; no duplicates/placeholders", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCaptureStableLowerNativeFps_ReturnsPassOriginalTimingWithNote()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(
            framesWritten: 17810,
            requestedFps: 30,
            writerFps: 30,
            containerFps: 30,
            measuredFps: 29.684));

        Assert.Equal("PASS_ORIGINAL_TIMING_WITH_NOTE", status);
        Assert.Contains("Frame counts may differ because cameras delivered real frames at different measured FPS", message);
    }

    [Fact]
    public void Assess_OriginalCaptureQueueDrops_ReturnsFail()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(queueDrops: 1));

        Assert.Equal("FAIL", status);
        Assert.Contains("queue drops", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCaptureDuplicateFrame_ReturnsFail()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(duplicateFrames: 1));

        Assert.Equal("FAIL", status);
        Assert.Contains("duplicateFrames", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCapturePlaceholderFrame_ReturnsFail()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(placeholderFrames: 1));

        Assert.Equal("FAIL", status);
        Assert.Contains("placeholders", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCaptureTimestampCsvMissing_ReturnsFailInStrictScientificMode()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(
            frameTimestampCsvWritten: false,
            frameTimestampCsvRowCount: 0));

        Assert.Equal("FAIL", status);
        Assert.Contains("Frame timestamp CSV", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCaptureTimestampRowMismatch_ReturnsFail()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(
            framesWritten: 18000,
            frameTimestampCsvRowCount: 17999));

        Assert.Equal("FAIL", status);
        Assert.Contains("row count mismatch", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCaptureHighJitterWithoutDrops_ReturnsPassWithWarning()
    {
        var (status, message) = ScientificTimingAssessor.Assess(OriginalCaptureInput(
            captureIntervalStdMs: 14,
            captureIntervalP99Ms: 95,
            expectedIntervalMs: 33.333,
            longGapCount: 3,
            severeLongGapCount: 1,
            fpsStabilityGrade: "Unstable"));

        Assert.Equal("PASS_WITH_WARNING", status);
        Assert.Contains("capture timing jitter was detected", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_OriginalCaptureAcceptsOneFrameStopBoundaryDifference()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            OriginalCaptureMode = true,
            FramesWritten = 19700,
            FramesCaptured = 19701,
            FramesAcceptedForRecording = 19700,
            FramesAcceptedMinusWritten = 0,
            StopBoundaryCapturedNotRecorded = 1,
            Width = 1920,
            Height = 1080,
            RequestedFps = 30,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.1,
            RequireFrameTimestampCsvValidation = true,
            FrameTimestampCsvWritten = true,
            FrameTimestampCsvRowCount = 19700,
            FpsStabilityGrade = "Excellent"
        });

        Assert.Equal("PASS_ORIGINAL_TIMING_WITH_NOTE", status);
        Assert.Contains("One final frame occurred at the stop boundary", message);
    }

    private static ScientificTimingInput OriginalCaptureInput(
        long framesWritten = 18000,
        double requestedFps = 30,
        double writerFps = 30,
        double containerFps = 30,
        double measuredFps = 30,
        long queueDrops = 0,
        long duplicateFrames = 0,
        long placeholderFrames = 0,
        bool frameTimestampCsvWritten = true,
        long? frameTimestampCsvRowCount = null,
        double captureIntervalStdMs = 2,
        double captureIntervalP99Ms = 36,
        double expectedIntervalMs = 33.333,
        long longGapCount = 0,
        long severeLongGapCount = 0,
        string fpsStabilityGrade = "Excellent") =>
        new()
        {
            OriginalCaptureMode = true,
            FramesWritten = framesWritten,
            FramesCaptured = framesWritten,
            Width = 1920,
            Height = 1080,
            RequestedFps = requestedFps,
            WriterFps = writerFps,
            ContainerFps = containerFps,
            MeasuredCameraFps = measuredFps,
            DuplicateFrames = duplicateFrames,
            PlaceholderFrames = placeholderFrames,
            QueueDrops = queueDrops,
            CaptureIntervalCount = Math.Max(0, framesWritten - 1),
            CaptureIntervalStdMs = captureIntervalStdMs,
            CaptureIntervalP99Ms = captureIntervalP99Ms,
            ExpectedIntervalMs = expectedIntervalMs,
            LongGapCount = longGapCount,
            SevereLongGapCount = severeLongGapCount,
            FpsStabilityGrade = fpsStabilityGrade,
            RequireFrameTimestampCsvValidation = true,
            FrameTimestampCsvWritten = frameTimestampCsvWritten,
            FrameTimestampCsvRowCount = frameTimestampCsvRowCount ?? framesWritten
        };

    // --- inter-camera start offset threshold tests ---

    [Fact]
    public void Assess_SingleCamera_IgnoresInterCameraOffset()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 1,
            FramesWritten = 1800,
            FramesCaptured = 1800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraStartOffsetMs = 0
        });

        Assert.NotEqual("FAIL", status);
    }

    [Fact]
    public void Assess_TwoCameras_OffsetUnderWarnThreshold_DoesNotWarn()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1800,
            FramesCaptured = 1800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraStartOffsetMs = ScientificTimingAssessor.StartOffsetWarnMs - 1
        });

        Assert.NotEqual("PASS_WITH_WARNING", status);
        Assert.NotEqual("FAIL", status);
    }

    [Fact]
    public void Assess_TwoCameras_OffsetAboveWarnBelowFail_ReturnsPassWithWarning()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1800,
            FramesCaptured = 1800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraStartOffsetMs = 75.0
        });

        Assert.Equal("PASS_WITH_WARNING", status);
        Assert.Contains("75.0", message);
    }

    [Fact]
    public void Assess_TwoCameras_OffsetAboveFailThreshold_ReturnsFail()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1800,
            FramesCaptured = 1800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraStartOffsetMs = 190.8
        });

        Assert.Equal("FAIL", status);
        Assert.Contains("190.8", message);
    }

    [Fact]
    public void Assess_TwoCameras_OffsetExactlyAtWarnThreshold_ReturnsPassWithWarning()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1800,
            FramesCaptured = 1800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraStartOffsetMs = ScientificTimingAssessor.StartOffsetWarnMs + 0.001
        });

        Assert.Equal("PASS_WITH_WARNING", status);
    }

    [Fact]
    public void Assess_TwoCameras_OffsetExactlyAtFailThreshold_ReturnsFail()
    {
        var (status, _) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            ActiveCameraCount = 2,
            FramesWritten = 1800,
            FramesCaptured = 1800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 30,
            InterCameraStartOffsetMs = ScientificTimingAssessor.StartOffsetFailMs + 0.001
        });

        Assert.Equal("FAIL", status);
    }
}
