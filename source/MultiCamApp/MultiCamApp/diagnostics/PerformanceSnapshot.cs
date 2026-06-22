namespace MultiCamApp.Diagnostics;

public sealed class PerformanceSnapshot
{
    public DateTime TimestampLocal { get; set; } = DateTime.Now;
    public double ElapsedSeconds { get; set; }
    public string RunState { get; set; } = "";
    public double? ProcessCpuPercent { get; set; }
    public double? TotalCpuPercent { get; set; }
    public double ProcessMemoryMb { get; set; }
    public double WorkingSetMb { get; set; }
    public double? GcMemoryMb { get; set; }
    public int ActiveCameraCount { get; set; }
    public List<CameraPerformanceSnapshot> Cameras { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public sealed class CameraPerformanceSnapshot
{
    public int Slot { get; set; }
    public string Status { get; set; } = "";
    public double? PreviewFps { get; set; }
    public long FramesCapturedTotal { get; set; }
    public long FramesCapturedDelta { get; set; }
    public double FramesCapturedFps { get; set; }
    public long FramesWrittenTotal { get; set; }
    public long FramesWrittenDelta { get; set; }
    public double FramesWrittenFps { get; set; }
    public long WriterQueueDropsTotal { get; set; }
    public long WriterQueueDropsDelta { get; set; }
    public double? PreviewStalenessSeconds { get; set; }
}

public sealed class CameraPerformanceSampleSource
{
    public int Slot { get; init; }
    public string Status { get; init; } = "";
    public bool IsActive { get; init; }
    public double? PreviewFps { get; init; }
    public long FramesCapturedTotal { get; init; }
    public long FramesWrittenTotal { get; init; }
    public long WriterQueueDropsTotal { get; init; }
    public double? PreviewStalenessSeconds { get; init; }
}
