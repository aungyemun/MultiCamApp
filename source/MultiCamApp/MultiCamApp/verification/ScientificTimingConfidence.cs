using MultiCamApp.Metadata;

namespace MultiCamApp.Verification;

public static class ScientificTimingConfidence
{
    public const string High = "HIGH";
    public const string Medium = "MEDIUM";
    public const string Low = "LOW";
    public const string Failed = "FAILED";
    public const string PassOriginalTiming = CameraAuditStatus.PassOriginalTiming;
    public const string PassWithWarning = CameraAuditStatus.PassWithWarning;

    public const string HighMessage =
        "Real frames only; no duplicates/placeholders. Use timestamp CSV for timing-sensitive analysis.";

    public static string FromSessionVideos(IReadOnlyList<VideoVerificationResult> videos)
    {
        if (videos.Count == 0)
            return Failed;

        if (videos.Any(HasTrueFailure))
            return Failed;

        var original = videos.All(v => v.Metadata?.OriginalCaptureMode == true
            && string.Equals(v.Metadata.RecordingTimingMode, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase)
            && !v.Metadata.ConstantFrameCountMode);
        var noFrameLoss = videos.All(v => v.Metadata is { } m
            && (OriginalCaptureVerificationPolicy.ResolveFramesAccepted(m) == OriginalCaptureVerificationPolicy.ResolveFramesWritten(m)
                || OriginalCaptureVerificationPolicy.IsAcceptedStopBoundaryDifference(m))
            && m.DuplicatedFrames + m.DuplicateFrames == 0
            && m.PlaceholderFrames == 0
            && m.WriterQueueDrops == 0);
        var timestampOk = videos.All(v => v.Metadata is { } m
            && m.FrameTimestampCsvWritten
            && m.FrameTimestampCsvRowCount == m.FrameCount);
        var stable = videos.All(v => IsStable(v.Metadata));
        var offsetsOk = videos.All(v => v.Metadata is { } m
            && m.InterCameraStartOffsetMs <= OriginalCaptureAuditPolicy.AcceptableStartEndOffsetMs);
        var durations = videos
            .Select(v => v.Metadata?.WallClockDurationSeconds ?? 0)
            .Where(d => d > 0)
            .ToList();
        var durationsSimilar = durations.Count < 2
            || durations.Max() - durations.Min() <= OriginalCaptureAuditPolicy.AcceptableWallClockDurationDifferenceSeconds;
        var metadataComplete = videos.All(v => MetadataComplete(v.Metadata));
        var borderline = videos.Any(v => IsBorderline(v.Metadata));
        var highJitterOrGaps = videos.Any(v => IsLowConfidence(v.Metadata));

        if (original)
        {
            if (noFrameLoss && timestampOk && stable && offsetsOk && durationsSimilar && metadataComplete && !borderline && !highJitterOrGaps)
                return PassOriginalTiming;

            return PassWithWarning;
        }

        if (noFrameLoss && !highJitterOrGaps && (!timestampOk || borderline))
            return Medium;

        return Low;
    }

    public static string FromCameraMetadata(IEnumerable<CameraRecordingMetadata> cameras)
    {
        var videos = cameras.Select(c => new VideoVerificationResult
        {
            Metadata = new CameraMetadataRecord
            {
                RecordingTimingMode = c.RecordingTimingMode,
                OriginalCaptureMode = c.OriginalCaptureMode,
                ConstantFrameCountMode = c.ConstantFrameCountMode,
                FrameCount = c.FrameCount,
                FramesCaptured = c.FramesCaptured,
                DuplicateFrames = c.DuplicateFrames,
                DuplicatedFrames = c.DuplicatedFrames,
                PlaceholderFrames = c.PlaceholderFrames,
                WriterQueueDrops = c.WriterQueueDrops,
                FrameTimestampCsvWritten = c.FrameTimestampCsvWritten,
                FrameTimestampCsvRowCount = c.FrameTimestampCsvRowCount,
                FpsStabilityGrade = c.FpsStabilityGrade,
                CaptureIntervalStdMs = c.CaptureIntervalStdMs,
                CaptureIntervalP99Ms = c.CaptureIntervalP99Ms,
                ExpectedIntervalMs = c.ExpectedIntervalMs,
                LongGapCount = c.LongGapCount,
                SevereLongGapCount = c.SevereLongGapCount,
                InterCameraStartOffsetMs = c.InterCameraStartOffsetMs,
                WallClockDurationSeconds = c.WallClockDurationSeconds,
                FrameTimestampCsvPath = c.FrameTimestampCsvPath,
                FirstFrameUtcTime = c.FirstFrameUtcTime,
                LastFrameUtcTime = c.LastFrameUtcTime,
                FirstFrameCaptureUtcTime = c.FirstFrameCaptureUtcTime,
                LastFrameCaptureUtcTime = c.LastFrameCaptureUtcTime,
                FirstFrameCaptureMonotonicSec = c.FirstFrameCaptureMonotonicSec,
                LastFrameCaptureMonotonicSec = c.LastFrameCaptureMonotonicSec,
                WriterClosedUtcTime = c.WriterClosedUtcTime
            },
            Probe = new VideoProbeData { Success = c.FrameCount > 0, HasVideoStream = c.FrameCount > 0 },
            ScientificTimingStatus = c.ScientificTimingStatus
        }).ToList();

        return FromSessionVideos(videos);
    }

    private static bool HasTrueFailure(VideoVerificationResult video)
    {
        if (video.Probe?.Success != true || video.Probe.HasVideoStream != true)
            return true;
        var m = video.Metadata;
        if (m == null)
            return true;
        return m.WriterQueueDrops > 0
            || (m.OriginalCaptureMode && m.DuplicatedFrames + m.DuplicateFrames > 0)
            || (m.OriginalCaptureMode && m.PlaceholderFrames > 0)
            || (m.FramesCaptured > 0 && m.FrameCount < m.FramesCaptured && !OriginalCaptureVerificationPolicy.IsAcceptedStopBoundaryDifference(m))
            || (string.Equals(video.ScientificTimingStatus, CameraAuditStatus.Fail, StringComparison.OrdinalIgnoreCase)
                && !OriginalCaptureVerificationPolicy.IsAcceptedStopBoundaryDifference(m));
    }

    private static bool IsStable(CameraMetadataRecord? m)
    {
        if (m == null)
            return false;
        var grade = m.FpsStabilityGrade?.Trim();
        if (string.Equals(grade, "Excellent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(grade, "Good", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.IsNullOrWhiteSpace(grade)
            && m.CaptureIntervalStdMs > 0
            && m.CaptureIntervalStdMs <= OriginalCaptureAuditPolicy.UnstableCaptureIntervalStdMs
            && m.SevereLongGapCount == 0;
    }

    private static bool IsBorderline(CameraMetadataRecord? m) =>
        string.Equals(m?.FpsStabilityGrade, "Borderline", StringComparison.OrdinalIgnoreCase)
        || m?.LongGapCount > 0;

    private static bool IsLowConfidence(CameraMetadataRecord? m)
    {
        if (m == null)
            return true;
        var p99High = m.ExpectedIntervalMs > 0 && m.CaptureIntervalP99Ms > m.ExpectedIntervalMs * 1.75;
        return string.Equals(m.FpsStabilityGrade, "Unstable", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.FpsStabilityGrade, "Failed", StringComparison.OrdinalIgnoreCase)
            || m.SevereLongGapCount > 0
            || m.LongGapCount > 2
            || p99High;
    }

    private static bool MetadataComplete(CameraMetadataRecord? m) =>
        m != null
        && !string.IsNullOrWhiteSpace(m.FrameTimestampCsvPath)
        && (m.FirstFrameCaptureUtcTime.HasValue || m.FirstFrameUtcTime.HasValue)
        && (m.LastFrameCaptureUtcTime.HasValue || m.LastFrameUtcTime.HasValue)
        && (m.FirstFrameCaptureMonotonicSec > 0 || m.FirstFrameMonotonicSeconds > 0)
        && (m.LastFrameCaptureMonotonicSec > 0 || m.LastFrameMonotonicSeconds > 0);
}
