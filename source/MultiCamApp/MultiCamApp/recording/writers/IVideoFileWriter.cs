// VideoEngineV2 — writer interface.
// Designed so each camera slot can own an independent writer instance.
// Multi-camera coordination (start/stop sync) lives above this interface.

namespace MultiCamApp.Recording.Writers;

/// <summary>
/// Common contract for all V2 video file writers.
/// Each camera slot instantiates its own writer; no shared writer across cameras.
/// </summary>
public interface IVideoFileWriter : IDisposable
{
    /// <summary>Backend identifier for diagnostics and metadata.</summary>
    string WriterDescription { get; }

    /// <summary>True while the writer is accepting frames.</summary>
    bool IsRecording { get; }

    /// <summary>Opens the output file and configures the encoder. No frames are accepted until <see cref="StartAsync"/>.</summary>
    Task OpenAsync(RecordingFileSet files, VideoWriterConfig config, CancellationToken ct = default);

    /// <summary>Starts accepting frames.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stops accepting new frames. Does not finalise the file.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Drains the write queue, finalises the MP4 container, renames temp → final file,
    /// and returns a full result record.
    /// </summary>
    Task<RecordingFinalizeResult> FinalizeAsync(CancellationToken ct = default);

    /// <summary>Returns a live health snapshot.</summary>
    RecordingWriteHealth GetHealth();
}

/// <summary>Configuration passed to <see cref="IVideoFileWriter.OpenAsync"/>.</summary>
public sealed class VideoWriterConfig
{
    public int Width  { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public double TargetFps { get; init; } = 30.0;
    public int TargetBitrateKbps { get; init; } = 8_000;
    public bool PreferHardwareEncoder { get; init; } = true;

    /// <summary>
    /// Optional native capture source, cast to <c>Windows.Media.Capture.MediaCapture</c> by
    /// implementations that share an already-open camera session.
    /// </summary>
    public object? NativeCaptureSource { get; init; }
}
