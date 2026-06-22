////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Capture;
using MultiCamApp.Experiment;
using MultiCamApp.Utils;
using MultiCamApp.Verification;

namespace MultiCamApp.Metadata;

public sealed class SessionSummaryWriter
{
    public async Task WriteAsync(
        string sessionPath,
        string sessionName,
        string sessionTitleOriginal,
        string sessionFolderName,
        DateTime recordingDateTimeLocal,
        IEnumerable<CameraRecordingMetadata> cameras,
        LocomotorSessionVerificationResult? locomotor = null)
    {
        var path = Path.Combine(sessionPath, "session_summary.txt");
        var cameraList = cameras.OrderBy(c => c.CameraSlot, StringComparer.OrdinalIgnoreCase).ToList();
        var first = cameraList.FirstOrDefault();
        var confidence = ScientificTimingConfidence.FromCameraMetadata(cameraList);
        var activeCameraCount = cameraList.Count;
        var activeCameraSlots = string.Join(", ", cameraList.Select(c => c.CameraSlot));
        var lines = new List<string>
        {
            $"Session: {sessionName}",
            $"Session title original: {sessionTitleOriginal}",
            $"Session folder name: {sessionFolderName}",
            $"Recording date time: {recordingDateTimeLocal:yyyy-MM-dd HH:mm:ss}",
            $"Active camera count: {activeCameraCount}",
            $"Active camera slots: {activeCameraSlots}",
            $"Folder: {PrivacySanitizer.FileNameOnly(sessionPath)}",
            $"Generated: {DateTime.Now:O}",
            $"App Version: {first?.AppVersion ?? "unknown"}",
            $"Build Number: {first?.BuildNumber ?? 0}",
            $"Release Stage: {first?.ReleaseStage ?? "unknown"}",
            $"sessionScientificTimingConfidence: {confidence}",
            $"Scientific Timing Confidence: {confidence}",
            confidence == ScientificTimingConfidence.High ? ScientificTimingConfidence.HighMessage : "",
            ""
        };

        foreach (var c in cameraList)
        {
            lines.Add($"--- {c.CameraSlot} ---");
            lines.Add($"Device: {PrivacySanitizer.SanitizeForOutput(c.CameraDeviceName)}");
            lines.Add($"Resolution: {c.Resolution}");
            lines.Add($"FPS: requested {c.RequestedFps:F3}, selected {(c.SelectedFps > 0 ? c.SelectedFps : c.SelectedDeviceFps):F3}, writer {(c.WriterFps > 0 ? c.WriterFps : c.RecordingWriterFps):F3}, camera {c.MeasuredCameraFps:F3}, container {c.ContainerFps:F3}");
            lines.Add($"Format: {c.ContainerFormat}/{c.VideoSubtype}");
            lines.Add($"Wall clock duration seconds: {c.WallClockDurationSeconds:F6}");
            lines.Add($"Frame-based duration seconds: {c.FrameBasedDurationSeconds:F6}");
            lines.Add($"Container duration seconds: {c.ContainerDurationSeconds:F6}");
            lines.Add($"Container vs wall-clock difference seconds: {c.ContainerVsWallClockDifferenceSeconds:F6}");
            lines.Add($"Duration (monotonic): {c.MonotonicDuration}");
            lines.Add($"Frames written: {c.FrameCount}");
            lines.Add($"Frames captured: {c.FramesCaptured}");
            lines.Add($"Capture interval count: {CaptureIntervalMetadataFormatter.FormatCount(c.CaptureIntervalCount, c.CaptureIntervalStatsMessage)}");
            lines.Add($"Capture interval mean ms: {CaptureIntervalMetadataFormatter.FormatMs(c.CaptureIntervalMeanMs, c.CaptureIntervalCount, c.CaptureIntervalStatsMessage)}");
            lines.Add($"Capture interval min ms: {CaptureIntervalMetadataFormatter.FormatMs(c.CaptureIntervalMinMs, c.CaptureIntervalCount, c.CaptureIntervalStatsMessage)}");
            lines.Add($"Capture interval max ms: {CaptureIntervalMetadataFormatter.FormatMs(c.CaptureIntervalMaxMs, c.CaptureIntervalCount, c.CaptureIntervalStatsMessage)}");
            lines.Add($"Capture interval std ms: {CaptureIntervalMetadataFormatter.FormatMs(c.CaptureIntervalStdMs, c.CaptureIntervalCount, c.CaptureIntervalStatsMessage)}");
            var captureIntervalNote = CaptureIntervalMetadataFormatter.DescribeAvailability(
                c.CaptureIntervalCount, c.CaptureIntervalStatsMessage);
            if (!string.IsNullOrWhiteSpace(captureIntervalNote))
                lines.Add($"Capture interval stats note: {captureIntervalNote}");
            if (activeCameraCount >= 2)
            {
                lines.Add($"Inter-camera frame difference: {c.InterCameraFrameDiff}");
                lines.Add($"Inter-camera start offset ms: {c.InterCameraStartOffsetMs:F3}");
            }
            else
            {
                lines.Add("Inter-camera frame difference: Not applicable, single-camera recording");
                lines.Add("Inter-camera start offset ms: Not applicable, single-camera recording");
            }
            lines.Add($"Scientific timing status: {c.ScientificTimingStatus}");
            lines.Add($"Scientific timing message: {c.ScientificTimingMessage}");
            lines.Add($"Start (local): {c.RecordingStartTimeLocal:O}");
            lines.Add($"File: {PrivacySanitizer.RelativeOrFileName(sessionPath, c.FilePath)}");
            lines.Add("");
        }

        lines.AddRange(LocomotorMetadataWriter.BuildSessionSummaryLines(locomotor));

        await File.WriteAllLinesAsync(path, lines.Select(PrivacySanitizer.SanitizeForOutput));
    }
}
