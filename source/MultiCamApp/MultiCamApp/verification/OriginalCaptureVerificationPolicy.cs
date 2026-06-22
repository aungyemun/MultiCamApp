namespace MultiCamApp.Verification;

public static class OriginalCaptureVerificationPolicy
{
    public const string PassWithWarning = "PASS_WITH_WARNING";
    public const string StopBoundaryAcceptedMessage =
        "One final frame occurred at the stop boundary and was not written. This is accepted and does not indicate frame loss during recording.";
    public const string FrameCountDifferenceNote =
        "Frame counts may differ because cameras delivered real frames at different measured FPS.";
    public const string ContainerWallClockNote =
        "Container duration differs from wall-clock time. Use timestamp CSV for scientific trimming and analysis.";

    public static bool IsOriginalCapture(CameraMetadataRecord? metadata) =>
        metadata != null
        && (metadata.OriginalCaptureMode
            || string.Equals(metadata.RecordingTimingMode, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase))
        && !metadata.ConstantFrameCountMode;

    public static bool IsAcceptedStopBoundaryDifference(CameraMetadataRecord? metadata)
    {
        if (metadata == null)
            return false;
        if (!IsOriginalCapture(metadata))
            return false;

        var written = ResolveFramesWritten(metadata);
        var captured = ResolveFramesCaptured(metadata);
        if (written <= 0 || captured <= written)
            return false;

        var capturedMinusWritten = captured - written;
        if (capturedMinusWritten is < 1 or > 2)
            return false;

        if (metadata.WriterQueueDrops != 0
            || metadata.DuplicatedFrames + metadata.DuplicateFrames != 0
            || metadata.PlaceholderFrames != 0)
            return false;

        if (metadata.FrameTimestampCsvRowCount > 0 && metadata.FrameTimestampCsvRowCount != written)
            return false;

        var camera = metadata.RecordingDiagnostics?.Camera;
        if (camera?.FinalFlushTimedOut == true)
            return false;

        if (camera?.FramesAcceptedForRecording > 0)
            return camera.FramesAcceptedForRecording == written;

        return camera == null
            || camera.FramesNotRecordedAfterStopRequested <= 0
            || camera.FramesNotRecordedAfterStopRequested >= capturedMinusWritten;
    }

    public static long ResolveFramesWritten(CameraMetadataRecord metadata) =>
        metadata.RecordingDiagnostics?.Camera?.FramesWritten > 0
            ? metadata.RecordingDiagnostics.Camera.FramesWritten
            : metadata.FrameCount;

    public static long ResolveFramesAccepted(CameraMetadataRecord metadata) =>
        metadata.RecordingDiagnostics?.Camera?.FramesAcceptedForRecording > 0
            ? metadata.RecordingDiagnostics.Camera.FramesAcceptedForRecording
            : metadata.FrameTimestampCsvRowCount > 0
                ? metadata.FrameTimestampCsvRowCount
                : metadata.FrameCount;

    public static long ResolveFramesCaptured(CameraMetadataRecord metadata)
    {
        var camera = metadata.RecordingDiagnostics?.Camera;
        if (camera?.FramesCapturedTotal > 0)
            return camera.FramesCapturedTotal;
        if (camera?.FramesCaptured > 0)
            return camera.FramesCaptured;
        return metadata.FramesCaptured > 0 ? metadata.FramesCaptured : metadata.FrameCount;
    }
}
