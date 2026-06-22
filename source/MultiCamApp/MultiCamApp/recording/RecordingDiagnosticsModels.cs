using System.Text.Json.Serialization;

namespace MultiCamApp.Recording;

public sealed record RecordingDiagnosticsMetadataSummary
{
    public string CsvPath { get; init; } = "";
    public string SummaryJsonPath { get; init; } = "";
    public int SampleCount { get; init; }
    public double SampleIntervalSeconds { get; init; } = 1;
    public double? AverageCpuPercent { get; init; }
    public double? MaxCpuPercent { get; init; }
    public int CpuSamplesOver90Percent { get; init; }
    public double? MaxProcessMemoryMB { get; init; }
    public bool ProcessMemoryContinuouslyIncreases { get; init; }
    public double SystemTotalMemoryMB { get; init; }
    public double MinSystemAvailableMemoryMB { get; init; }
    public double? MinDiskFreeSpaceGB { get; init; }
    public double MaxTotalCurrentFileSizeMB { get; init; }
    public double TotalSessionSizeMB { get; init; }
    public double EstimatedGBPerHourPerCamera { get; init; }
    public double EstimatedGBPerHourAllCameras { get; init; }
    public double? MaxTotalFileSizeGrowthMBps { get; init; }
    public int MaxTotalWriterQueueDepth { get; init; }
    public int MaxTotalWriterQueueCapacity { get; init; }
    public long MaxTotalQueueDrops { get; init; }
    public string SessionVerdictText { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactNote { get; init; }
    public RecordingDiagnosticsCameraSummary? Camera { get; init; }
}

public sealed record RecordingDiagnosticsSummary
{
    public string CsvPath { get; init; } = "";
    public string SummaryJsonPath { get; init; } = "";
    public string OutputFolderPath { get; init; } = "";
    public int ActiveCameraCount { get; init; }
    public IReadOnlyList<string> ActiveCameraSlots { get; init; } = [];
    public DateTime StartedUtc { get; init; }
    public DateTime StoppedUtc { get; init; }
    public int SampleCount { get; init; }
    public double SampleIntervalSeconds { get; init; } = 1;
    public double? AverageCpuPercent { get; init; }
    public double? MaxCpuPercent { get; init; }
    public int CpuSamplesOver90Percent { get; init; }
    public double MaxProcessMemoryMB { get; init; }
    public bool ProcessMemoryContinuouslyIncreases { get; init; }
    public double MaxProcessWorkingSetMB { get; init; }
    public double MaxProcessPrivateMemoryMB { get; init; }
    public double SystemTotalMemoryMB { get; init; }
    public double MinSystemAvailableMemoryMB { get; init; }
    public double MaxSystemMemoryUsedPercent { get; init; }
    public double MinDiskFreeSpaceGB { get; init; }
    public double MaxTotalCurrentFileSizeMB { get; init; }
    public double TotalSessionSizeMB { get; init; }
    public double EstimatedGBPerHourPerCamera { get; init; }
    public double EstimatedGBPerHourAllCameras { get; init; }
    public double? MaxTotalFileSizeGrowthMBps { get; init; }
    public int MaxTotalWriterQueueDepth { get; init; }
    public int MaxTotalWriterQueueCapacity { get; init; }
    public long MaxTotalQueueDrops { get; init; }
    public string SessionVerdictText { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactNote { get; init; }
    public IReadOnlyList<RecordingDiagnosticsCameraSummary> Cameras { get; init; } = [];
}

public sealed record RecordingDiagnosticsCameraSummary
{
    public int CameraIndex { get; init; }
    public string CameraName { get; init; } = "";
    public string TimingVerdict { get; init; } = "";
    public double? RequestedFps { get; init; }
    public double? MeasuredFpsByFrameCount { get; init; }
    public double? MeasuredFpsByIntervals { get; init; }
    public double? CaptureIntervalMeanMs { get; init; }
    public double? CaptureIntervalMedianMs { get; init; }
    public double? CaptureIntervalStdMs { get; init; }
    public double? CaptureIntervalMinMs { get; init; }
    public double? CaptureIntervalMaxMs { get; init; }
    public double? CaptureIntervalP95Ms { get; init; }
    public double? CaptureIntervalP99Ms { get; init; }
    public double? InstantaneousFpsSpikeMaxIgnored { get; init; }
    public int InstantaneousFpsSpikeIgnoredCount { get; init; }
    [JsonIgnore]
    public double? MaxMeasuredFpsRunning { get; init; }
    [JsonIgnore]
    public double? MinMeasuredFpsRunning { get; init; }
    [JsonIgnore]
    public double? MaxCaptureFpsCurrent { get; init; }
    [JsonIgnore]
    public double? MaxCaptureIntervalMeanMs { get; init; }
    [JsonIgnore]
    public double? MaxCaptureIntervalMaxMs { get; init; }
    [JsonIgnore]
    public double? MaxCaptureIntervalStdMs { get; init; }
    public long FramesCaptured { get; init; }
    public long FramesWritten { get; init; }
    public long FramesCapturedMinusWritten { get; init; }
    public long FramesCapturedTotal { get; init; }
    public long FramesAcceptedForRecording { get; init; }
    public long FramesEnqueued { get; init; }
    public long FramesDequeued { get; init; }
    public long FramesCapturedAfterStopRequested { get; init; }
    public long FramesNotRecordedAfterStopRequested { get; init; }
    public long FramesDroppedBeforeEnqueue { get; init; }
    public bool FinalFlushCompleted { get; init; }
    public bool FinalFlushTimedOut { get; init; }
    public bool WriterReleasedSuccessfully { get; init; }
    public long MaxFramesCaptured { get; init; }
    public long MaxFramesEnqueued { get; init; }
    public long MaxFramesDequeued { get; init; }
    public long MaxFramesWritten { get; init; }
    public int MaxWriterQueueDepth { get; init; }
    public int WriterQueueMaxDepth { get; init; }
    public int WriterQueueCapacity { get; init; }
    public int MaxWriterQueueMaxDepth { get; init; }
    public long MaxWriterQueueFullCount { get; init; }
    public long MaxWriterQueueDrops { get; init; }
    public long WriterQueueDrops { get; init; }
    public double? MaxWriterWriteMeanMs { get; init; }
    public double? MaxWriterWriteMaxMs { get; init; }
    public double? WriterWriteMeanMs { get; init; }
    public double? WriterWriteMaxMs { get; init; }
    public double MaxCurrentFileSizeMB { get; init; }
    public double FinalFileSizeMB { get; init; }
    public double EstimatedGBPerHour { get; init; }
    public double? MaxFileSizeGrowthMBps { get; init; }
    public string AutoFocusSupported { get; init; } = "unavailable";
    public string AutoFocusEnabled { get; init; } = "unavailable";
    public string ManualFocusSupported { get; init; } = "unavailable";
    public string ManualFocusValue { get; init; } = "unavailable";
}

internal sealed record RecordingDiagnosticsSample(
    int SampleIndex,
    DateTime SampleLocalTime,
    DateTime SampleUtcTime,
    double ElapsedSec,
    double? CpuPercent,
    double ProcessMemoryMB,
    double? ProcessWorkingSetMB,
    double? ProcessPrivateMemoryMB,
    double SystemTotalMemoryMB,
    double SystemAvailableMemoryMB,
    double SystemMemoryUsedPercent,
    string OutputFolderPath,
    double? DiskFreeSpaceGB,
    double TotalCurrentFileSizeMB,
    double? TotalFileSizeGrowthMBps,
    int TotalWriterQueueDepth,
    int TotalWriterQueueCapacity,
    long TotalQueueDrops,
    IReadOnlyList<RecordingDiagnosticsCameraSample> Cameras);

internal sealed record RecordingDiagnosticsCameraSample
{
    public int CameraIndex { get; init; }
    public string CameraName { get; init; } = "";
    public double? RequestedFps { get; init; }
    public double? MeasuredFpsRunning { get; init; }
    public double? CaptureFpsCurrent { get; init; }
    public double? CaptureIntervalMeanMs { get; init; }
    public double? CaptureIntervalMinMs { get; init; }
    public double? CaptureIntervalMaxMs { get; init; }
    public double? CaptureIntervalStdMs { get; init; }
    public long LongGapCount { get; init; }
    public long SevereGapCount { get; init; }
    public long ShortIntervalCount { get; init; }
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
    public double? WriterWriteP95Ms { get; init; }
    public long WriterBacklogFrames { get; init; }
    public double? WriterBacklogSeconds { get; init; }
    public double CurrentFileSizeMB { get; init; }
    public double? FileSizeGrowthMBps { get; init; }
    public string AutoFocusSupported { get; init; } = "unavailable";
    public string AutoFocusEnabled { get; init; } = "unavailable";
    public string ManualFocusSupported { get; init; } = "unavailable";
    public string ManualFocusValue { get; init; } = "unavailable";
}

public sealed record CameraFocusDiagnostics
{
    public string AutoFocusSupported { get; init; } = "unavailable";
    public string AutoFocusEnabled { get; init; } = "unavailable";
    public string ManualFocusSupported { get; init; } = "unavailable";
    public string ManualFocusValue { get; init; } = "unavailable";
}
