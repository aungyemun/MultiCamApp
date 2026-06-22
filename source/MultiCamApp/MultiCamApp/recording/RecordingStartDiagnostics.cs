using System.Text;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Recording;

/// <summary>Detailed per-slot recording startup trace (logs/recording_start_diagnostics_YYYYMMDD_HHMMSS.log).</summary>
public sealed class RecordingStartDiagnostics : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private readonly string _path;

    private RecordingStartDiagnostics(string path, StreamWriter? writer)
    {
        _path = path;
        _writer = writer;
    }

    public string Path => _path;

    public static RecordingStartDiagnostics Create()
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"recording_start_diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        StreamWriter? writer = null;
        try { writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true }; }
        catch { /* logs folder read-only or locked — diagnostics silently disabled for this recording */ }
        return new RecordingStartDiagnostics(path, writer);
    }

    public void WriteHeader(AppConfig config, int layoutCount, int activeSlots, string? sessionPath,
        bool sequentialOpenCv = false, int staggerMs = 0, int openCvSlots = 0, int winRtSlots = 0)
    {
        WriteLine("=== MultiCamApp recording start diagnostics ===");
        WriteLine($"Timestamp (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        WriteLine($"App version: {VersionService.Load().Display}");
        WriteLine($"Session path: {PrivacySanitizer.FileNameOnly(sessionPath)}");
        WriteLine($"Layout cameras: {layoutCount}");
        WriteLine($"Active recording slots: {activeSlots}");
        WriteLine($"OpenCV slots: {openCvSlots}");
        WriteLine($"WinRT slots: {winRtSlots}");
        WriteLine($"Sequential OpenCV startup: {sequentialOpenCv}");
        WriteLine($"Recording stagger ms: {staggerMs}");
        WriteLine($"Preview engine: {config.PreviewEngine}");
        WriteLine($"Recording engine: {config.RecordingEngine}");
        WriteLine($"Requested resolution: {CaptureResolutionPreset.ToLabel(config.PreferredCaptureWidth, config.PreferredCaptureHeight)}");
        WriteLine($"Requested FPS: {config.PreferFps:F3}");
        WriteLine("");
    }

    public void WriteSlotSection(string phase, CameraSlotPipeline slot, RecordingSlotStartupSnapshot snap)
    {
        WriteLine($"--- {phase}: {snap.SlotName} ---");
        WriteLine($"Device name: {PrivacySanitizer.SanitizeForOutput(snap.DeviceName)}");
        WriteLine($"Capture open path: {PrivacySanitizer.Redacted}");
        WriteLine($"DirectShow index: {snap.DirectShowIndex}");
        WriteLine($"Record queue capacity: {snap.RecordQueueCapacity}");
        WriteLine($"Preview active: {snap.PreviewActive}");
        WriteLine($"Selected resolution: {snap.SelectedResolution}");
        WriteLine($"Selected FPS: {snap.SelectedFps:F3}");
        WriteLine($"Requested writer FPS: {snap.RequestedWriterFps:F3}");
        WriteLine($"Output folder: {PrivacySanitizer.FileNameOnly(snap.OutputFolder)}");
        WriteLine($"MP4 path: {PrivacySanitizer.FileNameOnly(snap.Mp4Path)}");
        WriteLine($"VideoWriter created: {snap.VideoWriterCreated}");
        WriteLine($"VideoWriter opened: {snap.VideoWriterOpened}");
        WriteLine($"Recording flag set: {snap.RecordingFlagSet}");
        WriteLine($"Recording task started: {snap.RecordingTaskStarted}");
        WriteLine($"Frame queue connected: {snap.FrameQueueConnected}");
        WriteLine($"First frame received after start: {snap.FirstFrameReceived}");
        WriteLine($"First frame written: {snap.FirstFrameWritten}");
        WriteLine($"Frames captured after 1 second: {snap.FramesCapturedAfter1s}");
        WriteLine($"Frames written after 1 second: {snap.FramesWrittenAfter1s}");
        WriteLine($"Frames captured after 3 seconds: {snap.FramesCapturedAfter3s}");
        WriteLine($"Frames written after 3 seconds: {snap.FramesWrittenAfter3s}");
        WriteLine($"Backend: {snap.Backend}");
        WriteLine($"Pipeline status: {slot.Status}");
        if (!string.IsNullOrWhiteSpace(snap.ExceptionMessage))
            WriteLine($"Exception: {snap.ExceptionMessage}");
        WriteLine("");
    }

    public void WriteFooter(bool success, string? message = null)
    {
        WriteLine("=== Summary ===");
        WriteLine($"Result: {(success ? "SUCCESS" : "FAILED")}");
        if (!string.IsNullOrWhiteSpace(message))
            WriteLine($"Message: {message}");
        WriteLine($"Log file: {PrivacySanitizer.FileNameOnly(_path)}");
    }

    private void WriteLine(string line)
    {
        if (_writer == null) return;
        lock (_lock)
            _writer.WriteLine(PrivacySanitizer.SanitizeForLog(line));
    }

    public void Dispose()
    {
        lock (_lock)
            _writer?.Dispose();
    }
}

public sealed class RecordingSlotStartupSnapshot
{
    public string SlotName { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string DevicePath { get; init; } = "";
    public int DirectShowIndex { get; init; } = -1;
    public bool PreviewActive { get; init; }
    public string SelectedResolution { get; init; } = "";
    public double SelectedFps { get; init; }
    public double RequestedWriterFps { get; init; }
    public string OutputFolder { get; init; } = "";
    public string Mp4Path { get; init; } = "";
    public bool VideoWriterCreated { get; init; }
    public bool VideoWriterOpened { get; init; }
    public bool RecordingFlagSet { get; init; }
    public bool RecordingTaskStarted { get; init; }
    public bool FrameQueueConnected { get; init; }
    public bool FirstFrameReceived { get; init; }
    public bool FirstFrameWritten { get; init; }
    public long FramesCapturedAfter1s { get; init; }
    public long FramesWrittenAfter1s { get; init; }
    public long FramesCapturedAfter3s { get; init; }
    public long FramesWrittenAfter3s { get; init; }
    public int RecordQueueCapacity { get; init; } = OpenCvPreviewController.DefaultRecordQueueCapacity;
    public string Backend { get; init; } = "";
    public string? ExceptionMessage { get; init; }
}
