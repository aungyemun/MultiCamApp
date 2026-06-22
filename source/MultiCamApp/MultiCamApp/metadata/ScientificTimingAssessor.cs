////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Verification;

namespace MultiCamApp.Metadata;

public sealed class ScientificTimingInput
{
    public bool VideoReadable { get; init; } = true;
    public bool HasMetadata { get; init; } = true;
    public long FramesWritten { get; init; }
    public long FramesCaptured { get; init; }
    public long QueueDrops { get; init; }
    public long DuplicateFrames { get; init; }
    public long PlaceholderFrames { get; init; }
    public bool ConstantFrameCountMode { get; init; }
    public bool OriginalCaptureMode { get; init; }
    public double RequestedFps { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double WriterFps { get; init; }
    public double ContainerFps { get; init; }
    public double MeasuredCameraFps { get; init; }
    public int ActiveCameraCount { get; init; } = 1;
    public long InterCameraFrameDifference { get; init; }
    public double InterCameraStartOffsetMs { get; init; }
    public long CaptureIntervalCount { get; init; }
    public double CaptureIntervalStdMs { get; init; }
    public double CaptureIntervalP99Ms { get; init; }
    public double ExpectedIntervalMs { get; init; }
    public long LongGapCount { get; init; }
    public long SevereLongGapCount { get; init; }
    public string FpsStabilityGrade { get; init; } = "";
    public bool RequireFrameTimestampCsvValidation { get; init; }
    public bool FrameTimestampCsvWritten { get; init; } = true;
    public long FrameTimestampCsvRowCount { get; init; }
    public long FramesAcceptedForRecording { get; init; }
    public long FramesAcceptedMinusWritten { get; init; }
    public long StopBoundaryCapturedNotRecorded { get; init; }
    public bool FinalFlushTimedOut { get; init; }
    public long MaxConsecutiveNoFrame { get; init; }
}

public static class ScientificTimingAssessor
{
    /// <summary>Inter-camera first-frame offset above this value produces PASS_WITH_WARNING.</summary>
    public const double StartOffsetWarnMs = 50.0;
    /// <summary>Inter-camera first-frame offset above this value produces FAIL.</summary>
    public const double StartOffsetFailMs = 100.0;

    public const string DefaultMessage =
        "Video is valid. Camera delivery FPS may differ from MP4 container FPS. " +
        "For scientific analysis, use frame count and wall-clock/monotonic timing instead of ffprobe duration alone.";

    public const string OriginalCaptureMessage =
        "Original Capture Mode: Real frames only; no duplicates/placeholders. Frame counts may differ because cameras delivered real frames at different measured FPS.";

    public const string OriginalCaptureFpsNoteMessage =
        OriginalCaptureAuditPolicy.StableDifferentFpsNote;

    public static (string Status, string Message) Assess(ScientificTimingInput input)
    {
        if (!input.VideoReadable)
            return ("FAIL", "Video file is corrupt or unreadable.");

        if (!input.HasMetadata)
            return ("FAIL", "Recording metadata is missing.");

        if (input.FramesWritten <= 0)
            return ("FAIL", "No frames were written to the output file.");

        if (input.Width <= 0 || input.Height <= 0)
            return ("FAIL", "Resolution metadata is missing or invalid.");

        if (input.QueueDrops > 0)
        {
            return ("FAIL",
                $"Recording integrity issue: queue drops={input.QueueDrops}.");
        }

        if (input.PlaceholderFrames > 0)
        {
            return ("FAIL",
                $"Recording integrity issue: placeholders={input.PlaceholderFrames}.");
        }

        if (input.OriginalCaptureMode && input.DuplicateFrames > 0)
        {
            return ("FAIL",
                $"Original Capture Mode should contain only real frames, but duplicateFrames={input.DuplicateFrames}.");
        }

        if ((input.DuplicateFrames > 0 || input.PlaceholderFrames > 0) && !input.ConstantFrameCountMode)
        {
            return ("FAIL",
                $"Unexpected duplicate/placeholder frames: duplicates={input.DuplicateFrames}, " +
                $"placeholders={input.PlaceholderFrames}.");
        }

        if (input.ActiveCameraCount >= 2)
        {
            if (!input.OriginalCaptureMode && input.InterCameraFrameDifference > 5)
            {
                return ("FAIL",
                    $"Inter-camera frame count mismatch is too large ({input.InterCameraFrameDifference} frames).");
            }

            if (input.InterCameraStartOffsetMs > StartOffsetFailMs)
            {
                return ("FAIL",
                    $"Inter-camera start offset is too large ({input.InterCameraStartOffsetMs:F1} ms). Synchronized recording start is required for scientific multi-camera analysis.");
            }

            if (input.InterCameraStartOffsetMs > StartOffsetWarnMs)
            {
                return ("PASS_WITH_WARNING",
                    $"Inter-camera start offset is elevated ({input.InterCameraStartOffsetMs:F1} ms). Consider reviewing recording synchronization. Timestamp CSV provides per-frame timing for alignment.");
            }
        }

        var writerFps = input.WriterFps > 0 ? input.WriterFps : input.ContainerFps;
        var measured = input.MeasuredCameraFps > 0 ? input.MeasuredCameraFps : input.FramesCaptured > 0 && input.FramesWritten > 0
            ? input.FramesCaptured / Math.Max(0.001, input.FramesWritten / Math.Max(writerFps, 1))
            : 0;

        if (input.OriginalCaptureMode)
        {
            if (input.RequireFrameTimestampCsvValidation
                && (!input.FrameTimestampCsvWritten || input.FrameTimestampCsvRowCount != input.FramesWritten))
                return ("FAIL",
                    $"Frame timestamp CSV missing or row count mismatch: written={input.FrameTimestampCsvWritten}, rows={input.FrameTimestampCsvRowCount}, framesWritten={input.FramesWritten}.");

            if (input.FramesCaptured > 0 && input.FramesWritten != input.FramesCaptured)
            {
                if (IsAcceptedStopBoundaryDifference(input))
                    return ("PASS_ORIGINAL_TIMING_WITH_NOTE",
                        "One final frame occurred at the stop boundary and was not written. This is accepted and does not indicate frame loss during recording.");

                return ("FAIL",
                    $"Original Capture Mode frames are missing without a stop-boundary explanation: framesWritten={input.FramesWritten}, framesCaptured={input.FramesCaptured}.");
            }

            var p99High = input.ExpectedIntervalMs > 0 && input.CaptureIntervalP99Ms > input.ExpectedIntervalMs * 1.75;
            var grade = input.FpsStabilityGrade?.Trim() ?? "";
            if (string.Equals(grade, "Failed", StringComparison.OrdinalIgnoreCase))
                return ("FAIL", "FPS stability grade is Failed.");

            if (input.CaptureIntervalCount > 0
                && (string.Equals(grade, "Borderline", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(grade, "Unstable", StringComparison.OrdinalIgnoreCase)
                    || input.LongGapCount > 0
                    || input.SevereLongGapCount > 0
                    || p99High
                    || input.CaptureIntervalStdMs > OriginalCaptureAuditPolicy.UnstableCaptureIntervalStdMs
                    || input.MaxConsecutiveNoFrame > OriginalCaptureAuditPolicy.UnstableMaxConsecutiveNoFrame))
            {
                return ("PASS_WITH_WARNING",
                    "Real frames were preserved, but capture timing jitter was detected. Use the timestamp CSV for timing-sensitive analysis.");
            }

            var requestedFps = input.RequestedFps > 0 ? input.RequestedFps : writerFps;
            if (requestedFps > 0 && measured > 0 && Math.Abs(requestedFps - measured) > 0.05)
                return ("PASS_ORIGINAL_TIMING_WITH_NOTE", OriginalCaptureFpsNoteMessage);

            return ("PASS_ORIGINAL_TIMING", OriginalCaptureMessage);
        }

        if (writerFps > 0 && measured > 0 && Math.Abs(writerFps - measured) > 0.5)
            return ("PASS_WITH_WARNING", DefaultMessage);

        if (input.ConstantFrameCountMode && (input.DuplicateFrames > 0 || input.PlaceholderFrames > 0))
            return ("PASS_WITH_WARNING",
                "Video is valid and constant-frame-count aligned. Duplicate-frame correction was applied because one or more cameras delivered below the target FPS; duplicate frames are reported in metadata and audit reports.");

        return ("PASS", DefaultMessage);
    }

    private static bool IsAcceptedStopBoundaryDifference(ScientificTimingInput input)
    {
        if (input.FinalFlushTimedOut)
            return false;

        if (input.QueueDrops != 0 || input.DuplicateFrames != 0 || input.PlaceholderFrames != 0 || input.FramesWritten <= 0)
            return false;

        if (input.FrameTimestampCsvRowCount > 0 && input.FrameTimestampCsvRowCount != input.FramesWritten)
            return false;

        if (input.FramesAcceptedForRecording > 0
            && input.FramesAcceptedForRecording == input.FramesWritten
            && input.FramesAcceptedMinusWritten == 0)
            return true;

        var capturedMinusWritten = input.FramesCaptured - input.FramesWritten;
        if (capturedMinusWritten is < 1 or > 2)
            return false;

        return input.StopBoundaryCapturedNotRecorded <= 0 || input.StopBoundaryCapturedNotRecorded >= capturedMinusWritten;
    }
}
