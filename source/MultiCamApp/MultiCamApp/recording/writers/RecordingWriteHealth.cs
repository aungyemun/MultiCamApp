// VideoEngineV2 — real-time write health tracking.

using MultiCamApp.Capture.VideoEngineV2;

namespace MultiCamApp.Recording.Writers;

/// <summary>
/// Live health metrics for one V2 recording session.
/// Updated by the writer on each frame submission.
/// Satisfies the scientific requirement to report dropped frames honestly.
/// </summary>
public sealed class RecordingWriteHealth
{
    /// <summary>Frames received from the capture service.</summary>
    public long FramesReceived { get; set; }

    /// <summary>Frames submitted to the encoder / sink writer.</summary>
    public long FramesSubmittedToWriter { get; set; }

    /// <summary>Frames the encoder acknowledged as written or accepted.</summary>
    public long FramesWrittenOrAccepted { get; set; }

    /// <summary>Current depth of the internal write queue (0 = no backlog).</summary>
    public int WriterQueueDepth { get; set; }

    public int MaxWriterQueueDepth { get; set; }

    public double AverageWriterLatencyMs { get; set; }
    public double MaxWriterLatencyMs { get; set; }

    /// <summary>Frames dropped because the writer could not keep up.</summary>
    public long WriterDroppedFrames { get; set; }

    // Encoder identity
    public EncoderBackendType EncoderBackend { get; set; } = EncoderBackendType.NotSelected;
    public string EncoderCodec { get; set; } = "";
    public bool HardwareEncoderUsed { get; set; }
    public bool FallbackUsed { get; set; }
    public string? FallbackReason { get; set; }

    /// <summary>Frames dropped as a fraction of frames received (0–1).</summary>
    public double DroppedFrameRate =>
        FramesReceived > 0 ? (double)WriterDroppedFrames / FramesReceived : 0.0;

    /// <summary>True when the health report has no dropped frames.</summary>
    public bool IsHealthy => WriterDroppedFrames == 0;

    public RecordingWriteHealth Clone() => (RecordingWriteHealth)MemberwiseClone();
}
