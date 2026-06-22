namespace MultiCamApp.Capture;

public sealed record OpenCvRecordingDiagnosticsSnapshot
{
    public bool IsRecording { get; init; }
    public double RequestedFps { get; init; }
    public double WriterFps { get; init; }
    public long FramesCaptured { get; init; }
    public long FramesEnqueued { get; init; }
    public long FramesDequeued { get; init; }
    public long FramesWritten { get; init; }
    public int WriterQueueDepth { get; init; }
    public int WriterQueueCapacity { get; init; }
    public int WriterQueueMaxDepth { get; init; }
    public long WriterQueueFullCount { get; init; }
    public long WriterQueueDrops { get; init; }
    public double? WriterWriteMeanMs { get; init; }
    public double? WriterWriteMaxMs { get; init; }
    public double? CaptureIntervalMeanMs { get; init; }
    public double? CaptureIntervalMinMs { get; init; }
    public double? CaptureIntervalMaxMs { get; init; }
    public double? CaptureIntervalStdMs { get; init; }
    public long LongGapCount { get; init; }
    public long SevereGapCount { get; init; }
    public long ShortIntervalCount { get; init; }
}
