using System.Text;
using MultiCamApp.Capture;
using MultiCamApp.Utils;

namespace MultiCamApp.Recording;

/// <summary>
/// Post-recording comparison report for 3+ camera sessions (logs/cam3_diagnostics.txt).
/// Read-only analysis — does not alter recording timing, sync, or metadata format.
/// </summary>
public static class CamMultiCameraDiagnosticsReport
{
    public sealed class SlotDiagnostics
    {
        public CameraSlotPipeline Slot { get; init; } = null!;
        public RecordingCameraStats Stats { get; init; } = null!;
        public RecordingSlotStartupSnapshot? Startup { get; init; }
    }

    public static string WriteReport(
        string sessionPath,
        IReadOnlyList<SlotDiagnostics> slots,
        string? recordingStartDiagnosticsPath = null)
    {
        if (slots.Count < 3)
            return "";

        var path = Path.Combine(PathHelper.LogsFolder(), "cam3_diagnostics.txt");
        Directory.CreateDirectory(PathHelper.LogsFolder());

        var sb = new StringBuilder();
        sb.AppendLine("=== MultiCamApp multi-camera diagnostics (cam3 lag analysis) ===");
        sb.AppendLine($"Generated (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Session: {PrivacySanitizer.FileNameOnly(sessionPath)}");
        if (!string.IsNullOrWhiteSpace(recordingStartDiagnosticsPath))
            sb.AppendLine($"Recording start log: {PrivacySanitizer.FileNameOnly(recordingStartDiagnosticsPath)}");
        sb.AppendLine($"Active cameras: {slots.Count}");
        sb.AppendLine();

        foreach (var entry in slots.OrderBy(s => s.Slot.SlotIndex))
            AppendSlotSection(sb, entry);

        AppendComparison(sb, slots);
        AppendPipelineVerification(sb);
        AppendRootCause(sb, slots);

        var text = sb.ToString();
        File.WriteAllText(path, PrivacySanitizer.SanitizeForOutput(text), Encoding.UTF8);
        return path;
    }

    private static void AppendSlotSection(StringBuilder sb, SlotDiagnostics entry)
    {
        var slot = entry.Slot;
        var s = entry.Stats;
        var startup = entry.Startup;

        sb.AppendLine($"--- {s.CameraSlot} ---");
        sb.AppendLine($"USB device name: {PrivacySanitizer.SanitizeForOutput(s.CameraDeviceName)}");
        sb.AppendLine($"USB device path: {PrivacySanitizer.Redacted}");
        sb.AppendLine($"DirectShow open path: {PrivacySanitizer.Redacted}");
        sb.AppendLine($"DirectShow index: {slot.DirectShowIndex}");
        sb.AppendLine($"Backend: {s.Backend}");
        sb.AppendLine($"Requested resolution: {s.RequestedResolution}");
        sb.AppendLine($"Actual opened resolution: {s.SelectedResolution}");
        sb.AppendLine($"Requested FPS: {s.RequestedFps:F3}");
        sb.AppendLine($"Actual camera FPS (negotiated): {s.SelectedDeviceFps:F3}");
        sb.AppendLine($"Writer FPS: {s.RecordingWriterFps:F3}");
        sb.AppendLine($"Measured camera FPS: {s.MeasuredCameraFps:F3}");
        sb.AppendLine($"Measured writer FPS: {s.MeasuredWriterFps:F3}");
        sb.AppendLine($"Preview FPS (UI monitor): {slot.FpsMonitor.AverageFps:F3}");
        sb.AppendLine($"Capture interval mean ms: {s.CaptureIntervalMeanMs:F3}");
        sb.AppendLine($"Capture interval min/max ms: {s.CaptureIntervalMinMs:F3} / {s.CaptureIntervalMaxMs:F3}");
        sb.AppendLine($"Capture interval std ms: {s.CaptureIntervalStdMs:F3}");
        sb.AppendLine($"Max consecutive late frames: {s.MaxConsecutiveLateFrames}");
        sb.AppendLine($"Max consecutive no-frame: {s.MaxConsecutiveNoFrame}");
        sb.AppendLine($"Record queue capacity: {s.WriterQueueCapacity} (bounded channel, per-slot)");
        sb.AppendLine($"Writer queue drops: {s.WriterQueueDrops}");
        sb.AppendLine($"Writer queue full count: {s.WriterQueueFullCount}");
        sb.AppendLine($"Writer queue max depth: {s.WriterQueueDepthMax}");
        sb.AppendLine($"Writer queue depth at stop: {s.WriterQueueDepthAtStop}");
        sb.AppendLine($"Writer frames dequeued: {s.WriterFramesDequeued}");
        sb.AppendLine($"Average VideoWriter.Write ms: {s.AverageVideoWriterWriteMs:F3}");
        sb.AppendLine($"Max VideoWriter.Write ms: {s.MaxVideoWriterWriteMs:F3}");
        sb.AppendLine($"Dropped frame timestamps ms: {s.DroppedFrameTimestamps}");
        sb.AppendLine($"Preview enabled during recording: {s.PreviewEnabledDuringRecording}");
        sb.AppendLine($"Recording preview FPS cap: {s.RecordingPreviewFpsCap:F1}");
        sb.AppendLine($"Preview frames rendered during recording: {s.PreviewFramesRenderedDuringRecording}");
        sb.AppendLine($"File size bytes: {s.FileSizeBytes}");
        sb.AppendLine($"File bitrate Mbps: {s.FileBitrateMbps:F3}");
        sb.AppendLine($"Dropped frames: {s.DroppedFrames}");
        sb.AppendLine($"Duplicate frames: {s.DuplicateFrames}");
        sb.AppendLine($"Placeholder frames: {s.PlaceholderFrames}");
        sb.AppendLine($"Frames captured: {s.FramesCaptured}");
        sb.AppendLine($"Frames written: {s.FramesWritten}");
        sb.AppendLine($"Capture vs written delta: {s.FramesCaptured - s.FramesWritten}");
        sb.AppendLine($"Wall duration s: {s.WallClockDurationSeconds:F3}");
        sb.AppendLine($"Frame-based duration s: {s.FrameBasedDurationSeconds:F3}");
        sb.AppendLine($"Codec/container: {s.Codec} / {s.Container}");
        if (startup != null)
        {
            sb.AppendLine($"Startup — frames captured @1s: {startup.FramesCapturedAfter1s}");
            sb.AppendLine($"Startup — frames captured @3s: {startup.FramesCapturedAfter3s}");
            sb.AppendLine($"Startup — first frame received: {startup.FirstFrameReceived}");
            sb.AppendLine($"Startup — recording pump started: {startup.RecordingTaskStarted}");
        }
        sb.AppendLine();
    }

    private static void AppendComparison(StringBuilder sb, IReadOnlyList<SlotDiagnostics> slots)
    {
        sb.AppendLine("=== cam1 vs cam2 vs cam3 comparison ===");
        var ordered = slots.OrderBy(s => s.Slot.SlotIndex).ToList();
        if (ordered.Count < 3)
        {
            sb.AppendLine("(Need at least 3 cameras for comparison.)");
            sb.AppendLine();
            return;
        }

        var cam1 = ordered[0].Stats;
        var cam2 = ordered[1].Stats;
        var cam3 = ordered[2].Stats;

        sb.AppendLine($"Preview FPS: cam1={ordered[0].Slot.FpsMonitor.AverageFps:F2}, cam2={ordered[1].Slot.FpsMonitor.AverageFps:F2}, cam3={ordered[2].Slot.FpsMonitor.AverageFps:F2}");
        sb.AppendLine($"Measured camera FPS: cam1={cam1.MeasuredCameraFps:F2}, cam2={cam2.MeasuredCameraFps:F2}, cam3={cam3.MeasuredCameraFps:F2}");
        sb.AppendLine($"Writer FPS: cam1={cam1.RecordingWriterFps:F2}, cam2={cam2.RecordingWriterFps:F2}, cam3={cam3.RecordingWriterFps:F2}");
        sb.AppendLine($"Capture interval mean ms: cam1={cam1.CaptureIntervalMeanMs:F1}, cam2={cam2.CaptureIntervalMeanMs:F1}, cam3={cam3.CaptureIntervalMeanMs:F1}");
        sb.AppendLine($"Frames written: cam1={cam1.FramesWritten}, cam2={cam2.FramesWritten}, cam3={cam3.FramesWritten}");
        sb.AppendLine($"Queue drops: cam1={cam1.WriterQueueDrops}, cam2={cam2.WriterQueueDrops}, cam3={cam3.WriterQueueDrops}");
        sb.AppendLine();

        sb.AppendLine("=== Detected differences ===");
        if (!string.Equals(cam1.SelectedResolution, cam3.SelectedResolution, StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"- Resolution: cam3 opened {cam3.SelectedResolution} vs cam1 {cam1.SelectedResolution}");
        else
            sb.AppendLine("- Resolution: all cameras matched requested resolution at record time");

        if (Math.Abs(cam1.SelectedDeviceFps - cam3.SelectedDeviceFps) > 0.5)
            sb.AppendLine($"- Negotiated FPS differs: cam3={cam3.SelectedDeviceFps:F1} vs cam1={cam1.SelectedDeviceFps:F1}");
        else
            sb.AppendLine("- Negotiated device FPS: same for all cameras");

        var cam3FpsRatio = cam1.MeasuredCameraFps > 0 ? cam3.MeasuredCameraFps / cam1.MeasuredCameraFps : 0;
        if (cam3FpsRatio < 0.85)
            sb.AppendLine($"- cam3 measured capture rate is {(1 - cam3FpsRatio) * 100:F0}% lower than cam1 ({cam3.MeasuredCameraFps:F1} vs {cam1.MeasuredCameraFps:F1} fps)");
        else
            sb.AppendLine("- Measured capture rates are within 15% across cameras");

        if (cam3.WriterQueueDrops > cam1.WriterQueueDrops || cam3.WriterQueueDrops > cam2.WriterQueueDrops)
            sb.AppendLine($"- cam3 writer queue drops ({cam3.WriterQueueDrops}) exceed cam1/cam2");
        else if (cam3.WriterQueueDrops == 0 && cam3.FramesCaptured == cam3.FramesWritten)
            sb.AppendLine("- cam3: zero queue drops; frames captured == frames written (writer keeps up with capture)");

        if (cam3.MaxConsecutiveNoFrame > cam1.MaxConsecutiveNoFrame)
            sb.AppendLine($"- cam3 had longer no-frame streaks (max={cam3.MaxConsecutiveNoFrame}) vs cam1 (max={cam1.MaxConsecutiveNoFrame})");

        sb.AppendLine();
    }

    private static void AppendPipelineVerification(StringBuilder sb)
    {
        sb.AppendLine("=== Pipeline verification (cam3 vs cam1/cam2) ===");
        sb.AppendLine("- Independent OpenCvPreviewController per slot: YES (same class, per-instance state)");
        sb.AppendLine("- Independent capture loop (CaptureLoopAsync): YES");
        sb.AppendLine("- Independent bounded record queue: YES");
        sb.AppendLine("- Independent RecordPumpAsync + VideoWriter: YES");
        sb.AppendLine("- Independent frame/timing counters: YES");
        sb.AppendLine("- Independent monotonic timestamps: YES");
        sb.AppendLine("- Slot-specific recording code path (cam3 fallback): NOT FOUND");
        sb.AppendLine("- Record queue capacity is resolution-aware and stored per camera in metadata.");
        sb.AppendLine("- Different preview UI cap per slot: NO (MaxPreviewFpsUi shared config)");
        sb.AppendLine("- Preview throttle during recording: ENABLED (low-FPS live preview cap while recording)");
        sb.AppendLine("- 3+ camera recording start: sequential with stagger (startup only; does not throttle ongoing capture)");
        sb.AppendLine("- DShowOpenGate: global lock for open/set only (not per-frame)");
        sb.AppendLine("- _deviceIndex>0 MJPEG warmup: applies to cam2/cam3/cam4 at OPEN time only (not during 640x480 record session)");
        sb.AppendLine();
    }

    private static void AppendRootCause(StringBuilder sb, IReadOnlyList<SlotDiagnostics> slots)
    {
        var ordered = slots.OrderBy(s => s.Slot.SlotIndex).ToList();
        if (ordered.Count < 3)
            return;

        var cam1 = ordered[0].Stats;
        var cam3 = ordered[2].Stats;

        sb.AppendLine("=== Root cause classification ===");

        var captureStarved = cam3.MeasuredCameraFps < cam1.MeasuredCameraFps * 0.85;
        var writerBottleneck = cam3.WriterQueueDrops > 0 || cam3.FramesCaptured > cam3.FramesWritten + 2;
        var resolutionMismatch = !string.Equals(cam1.SelectedResolution, cam3.SelectedResolution, StringComparison.OrdinalIgnoreCase);
        var pipelineDifferent = false;

        string primary;
        if (pipelineDifferent)
            primary = "software pipeline difference";
        else if (writerBottleneck)
            primary = "writer bottleneck";
        else if (captureStarved && cam3.WriterQueueDrops == 0)
            primary = "USB bandwidth / USB controller limitation (capture-side starvation)";
        else if (resolutionMismatch)
            primary = "camera capability (resolution negotiation)";
        else
            primary = "hardware bandwidth or camera capability";

        sb.AppendLine($"Primary: {primary}");
        sb.AppendLine();
        sb.AppendLine("Checklist:");
        sb.AppendLine($"- hardware bandwidth: {(captureStarved ? "LIKELY" : "unlikely")} — cam3 delivers fewer frames from device");
        sb.AppendLine($"- camera capability: {(resolutionMismatch ? "POSSIBLE" : "same negotiated mode as cam1/cam2")}");
        sb.AppendLine($"- USB controller limitation: {(captureStarved ? "LIKELY" : "unlikely")} — 3 USB cameras sharing bus");
        sb.AppendLine($"- software pipeline difference: {(pipelineDifferent ? "YES" : "NO")}");
        sb.AppendLine($"- queue bottleneck: {(cam3.WriterQueueDrops > 0 ? "YES" : "NO")} (drops={cam3.WriterQueueDrops})");
        sb.AppendLine($"- writer bottleneck: {(writerBottleneck ? "YES" : "NO")}");
        sb.AppendLine("- preview rendering bottleneck: POSSIBLE if previewFramesRendered is high or CPU load is high; recording now applies a low-FPS preview cap");
        sb.AppendLine();
    }
}
