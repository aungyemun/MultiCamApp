using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Recording;

public sealed class RecordingDiagnosticsMonitor
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private readonly AppConfig _config;
    private readonly IReadOnlyList<CameraSlotPipeline> _slots;
    private readonly string _sessionPath;
    private readonly string _csvPath;
    private readonly string _summaryPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<RecordingDiagnosticsSample> _samples = [];
    private readonly Dictionary<int, CameraSampleState> _cameraStates = new();
    private readonly object _lock = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();
    private StreamWriter? _writer;
    private Task? _task;
    private TimeSpan _lastCpuTotal;
    private long _lastCpuTicks;
    private double _lastTotalFileSizeMB;
    private double _lastTotalFileSizeElapsedSec;

    public RecordingDiagnosticsMonitor(string sessionPath, IReadOnlyList<CameraSlotPipeline> slots, AppConfig config)
    {
        _sessionPath = sessionPath;
        _slots = slots;
        _config = config;
        _csvPath = Path.Combine(sessionPath, "recording_diagnostics.csv");
        _summaryPath = Path.Combine(sessionPath, "recording_diagnostics_summary.json");
    }

    public string CsvPath => _csvPath;
    public string SummaryPath => _summaryPath;
    public RecordingDiagnosticsSummary? Summary { get; private set; }

    public void Start()
    {
        try
        {
            Directory.CreateDirectory(_sessionPath);
            _writer = new StreamWriter(_csvPath, append: false, Encoding.UTF8) { AutoFlush = true };
            _writer.WriteLine(BuildHeader());
            _lastCpuTotal = _process.TotalProcessorTime;
            _lastCpuTicks = Stopwatch.GetTimestamp();
            _task = Task.Run(RunAsync);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_START_FAILED error={ex.Message}");
            SafeDisposeWriter();
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _cts.Cancel();
            if (_task != null)
                await _task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_STOP_FAILED error={ex.Message}");
        }
        finally
        {
            SafeDisposeWriter();
            try
            {
                Summary = BuildSummary();
                await File.WriteAllTextAsync(
                    _summaryPath,
                    PrivacySanitizer.SanitizeForOutput(JsonSerializer.Serialize(Summary, new JsonSerializerOptions { WriteIndented = true })))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_SUMMARY_FAILED error={ex.Message}");
            }

            _cts.Dispose();
        }
    }

    public RecordingDiagnosticsMetadataSummary? BuildMetadataSummaryForCamera(int cameraIndex)
    {
        var summary = Summary;
        if (summary == null)
            return null;

        var camera = summary.Cameras.FirstOrDefault(c => c.CameraIndex == cameraIndex);
        return new RecordingDiagnosticsMetadataSummary
        {
            CsvPath = PrivacySanitizer.FileNameOnly(summary.CsvPath),
            SummaryJsonPath = PrivacySanitizer.FileNameOnly(summary.SummaryJsonPath),
            SampleCount = summary.SampleCount,
            SampleIntervalSeconds = summary.SampleIntervalSeconds,
            AverageCpuPercent = summary.AverageCpuPercent,
            MaxCpuPercent = summary.MaxCpuPercent,
            CpuSamplesOver90Percent = summary.CpuSamplesOver90Percent,
            MaxProcessMemoryMB = Math.Max(summary.MaxProcessMemoryMB, summary.MaxProcessPrivateMemoryMB),
            ProcessMemoryContinuouslyIncreases = summary.ProcessMemoryContinuouslyIncreases,
            SystemTotalMemoryMB = summary.SystemTotalMemoryMB,
            MinSystemAvailableMemoryMB = summary.MinSystemAvailableMemoryMB,
            MinDiskFreeSpaceGB = summary.MinDiskFreeSpaceGB,
            MaxTotalCurrentFileSizeMB = summary.MaxTotalCurrentFileSizeMB,
            TotalSessionSizeMB = summary.TotalSessionSizeMB,
            EstimatedGBPerHourPerCamera = summary.EstimatedGBPerHourPerCamera,
            EstimatedGBPerHourAllCameras = summary.EstimatedGBPerHourAllCameras,
            MaxTotalFileSizeGrowthMBps = summary.MaxTotalFileSizeGrowthMBps,
            MaxTotalWriterQueueDepth = summary.MaxTotalWriterQueueDepth,
            MaxTotalWriterQueueCapacity = summary.MaxTotalWriterQueueCapacity,
            MaxTotalQueueDrops = summary.MaxTotalQueueDrops,
            SessionVerdictText = summary.SessionVerdictText,
            ArtifactNote = summary.ArtifactNote,
            Camera = camera
        };
    }

    private async Task RunAsync()
    {
        var sampleIndex = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var sample = CaptureSample(sampleIndex++);
                lock (_lock)
                {
                    _samples.Add(sample);
                    WriteSampleRows(sample);
                }
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_SAMPLE_FAILED error={ex.Message}");
            }

            try
            {
                await Task.Delay(SampleInterval, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private RecordingDiagnosticsSample CaptureSample(int sampleIndex)
    {
        _process.Refresh();
        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var elapsedSec = _elapsed.Elapsed.TotalSeconds;
        var memory = TryGetMemoryStatus();
        var diskFree = TryGetDiskFreeSpaceGb(_sessionPath);
        var cameras = _slots
            .Where(s => s.SelectedMode != null)
            .OrderBy(s => s.SlotIndex)
            .Select(slot => CaptureCameraSample(slot, elapsedSec))
            .ToList();

        var totalFileSize = cameras.Sum(c => c.CurrentFileSizeMB);
        double? totalGrowth = null;
        if (_lastTotalFileSizeElapsedSec > 0 && elapsedSec > _lastTotalFileSizeElapsedSec)
            totalGrowth = (totalFileSize - _lastTotalFileSizeMB) / (elapsedSec - _lastTotalFileSizeElapsedSec);
        _lastTotalFileSizeMB = totalFileSize;
        _lastTotalFileSizeElapsedSec = elapsedSec;

        return new RecordingDiagnosticsSample(
            sampleIndex,
            nowLocal,
            nowUtc,
            elapsedSec,
            TryGetCpuPercent(),
            BytesToMb(_process.PrivateMemorySize64),
            BytesToMb(_process.WorkingSet64),
            BytesToMb(_process.PrivateMemorySize64),
            memory.TotalMb,
            memory.AvailableMb,
            memory.UsedPercent,
            PrivacySanitizer.FileNameOnly(_sessionPath),
            diskFree,
            totalFileSize,
            totalGrowth,
            cameras.Sum(c => c.WriterQueueDepth),
            cameras.Sum(c => c.WriterQueueCapacity),
            cameras.Sum(c => c.WriterQueueDrops),
            cameras);
    }

    private RecordingDiagnosticsCameraSample CaptureCameraSample(CameraSlotPipeline slot, double elapsedSec)
    {
        var snapshot = slot.GetOpenCvRecordingDiagnosticsSnapshot();
        var focus = slot.GetFocusDiagnosticsSnapshot(_config);
        var path = slot.CurrentRecordingFilePath;
        var fileSizeMb = TryGetFileSizeMb(path);
        var state = GetCameraState(slot.SlotIndex);
        var dt = elapsedSec - state.LastElapsedSec;
        var captured = snapshot?.FramesCaptured ?? slot.CaptureFrameCount;
        var fileGrowth = dt > 0 ? (double?)((fileSizeMb - state.LastFileSizeMB) / dt) : null;
        var captureFps = dt > 0 ? (double?)((captured - state.LastFramesCaptured) / dt) : null;
        state.LastElapsedSec = elapsedSec;
        state.LastFramesCaptured = captured;
        state.LastFileSizeMB = fileSizeMb;

        double? requestedFps = snapshot?.RequestedFps > 0
            ? snapshot.RequestedFps
            : slot.RequestedFps > 0 ? slot.RequestedFps : null;
        var measuredRunning = elapsedSec > 0.25 ? captured / Math.Max(0.001, elapsedSec) : (double?)null;
        var writerFps = snapshot?.WriterFps > 0 ? snapshot.WriterFps : requestedFps;
        var backlogFrames = Math.Max(0, (snapshot?.FramesEnqueued ?? 0) - (snapshot?.FramesDequeued ?? 0));

        return new RecordingDiagnosticsCameraSample
        {
            CameraIndex = slot.SlotIndex + 1,
            CameraName = PrivacySanitizer.SanitizeForOutput(slot.DeviceName),
            RequestedFps = requestedFps,
            MeasuredFpsRunning = measuredRunning,
            CaptureFpsCurrent = captureFps,
            CaptureIntervalMeanMs = snapshot?.CaptureIntervalMeanMs,
            CaptureIntervalMinMs = snapshot?.CaptureIntervalMinMs,
            CaptureIntervalMaxMs = snapshot?.CaptureIntervalMaxMs,
            CaptureIntervalStdMs = snapshot?.CaptureIntervalStdMs,
            LongGapCount = snapshot?.LongGapCount ?? 0,
            SevereGapCount = snapshot?.SevereGapCount ?? 0,
            ShortIntervalCount = snapshot?.ShortIntervalCount ?? 0,
            FramesCaptured = captured,
            FramesEnqueued = snapshot?.FramesEnqueued ?? 0,
            FramesDequeued = snapshot?.FramesDequeued ?? 0,
            FramesWritten = snapshot?.FramesWritten ?? slot.CurrentOpenCvFramesWritten,
            WriterQueueDepth = snapshot?.WriterQueueDepth ?? 0,
            WriterQueueCapacity = snapshot?.WriterQueueCapacity ?? 0,
            WriterQueueMaxDepth = snapshot?.WriterQueueMaxDepth ?? 0,
            WriterQueueFullCount = snapshot?.WriterQueueFullCount ?? 0,
            WriterQueueDrops = snapshot?.WriterQueueDrops ?? slot.CurrentOpenCvWriterQueueDrops,
            WriterWriteMeanMs = snapshot?.WriterWriteMeanMs,
            WriterWriteMaxMs = snapshot?.WriterWriteMaxMs,
            WriterWriteP95Ms = null,
            WriterBacklogFrames = backlogFrames,
            WriterBacklogSeconds = writerFps > 0 ? backlogFrames / writerFps : null,
            CurrentFileSizeMB = fileSizeMb,
            FileSizeGrowthMBps = fileGrowth,
            AutoFocusSupported = focus.AutoFocusSupported,
            AutoFocusEnabled = focus.AutoFocusEnabled,
            ManualFocusSupported = focus.ManualFocusSupported,
            ManualFocusValue = focus.ManualFocusValue
        };
    }

    private CameraSampleState GetCameraState(int slotIndex)
    {
        if (!_cameraStates.TryGetValue(slotIndex, out var state))
        {
            state = new CameraSampleState();
            _cameraStates[slotIndex] = state;
        }

        return state;
    }

    private double? TryGetCpuPercent()
    {
        try
        {
            var nowTicks = Stopwatch.GetTimestamp();
            var nowCpu = _process.TotalProcessorTime;
            var elapsedSec = (nowTicks - _lastCpuTicks) / (double)Stopwatch.Frequency;
            var cpuDeltaMs = (nowCpu - _lastCpuTotal).TotalMilliseconds;
            _lastCpuTicks = nowTicks;
            _lastCpuTotal = nowCpu;
            if (elapsedSec <= 0)
                return null;

            return Math.Max(0, Math.Min(100, cpuDeltaMs / (elapsedSec * 1000.0 * Environment.ProcessorCount) * 100.0));
        }
        catch
        {
            return null;
        }
    }

    private RecordingDiagnosticsSummary BuildSummary()
    {
        List<RecordingDiagnosticsSample> samples;
        lock (_lock)
            samples = _samples.ToList();

        var recordingHours = samples.Count >= 2
            ? Math.Max(1.0 / 3600.0, (samples[^1].ElapsedSec - samples[0].ElapsedSec) / 3600.0)
            : 1.0 / 3600.0;
        var cameraSummaries = samples
            .SelectMany(s => s.Cameras)
            .GroupBy(c => c.CameraIndex)
            .Select(g =>
            {
                var cameraSamples = g.ToList();
                var requestedFps = MaxNullable(cameraSamples.Select(c => c.RequestedFps));
                var stableTiming = BuildStableCameraTimingSummary(g.Key, requestedFps);
                var validInstantaneousFps = FilterValidInstantaneousFps(cameraSamples, requestedFps).ToList();
                var ignoredInstantaneousFps = FilterIgnoredInstantaneousFps(cameraSamples, requestedFps).ToList();
                var finalStats = GetFinalRecordingStats(g.Key);
                var sampledFramesCaptured = cameraSamples.Max(c => c.FramesCaptured);
                var sampledFramesEnqueued = cameraSamples.Max(c => c.FramesEnqueued);
                var sampledFramesDequeued = cameraSamples.Max(c => c.FramesDequeued);
                var sampledFramesWritten = cameraSamples.Max(c => c.FramesWritten);
                var framesAccepted = finalStats?.FrameTimestampCsvRowCount > 0
                    ? finalStats.FrameTimestampCsvRowCount
                    : finalStats?.FramesWritten ?? sampledFramesEnqueued;
                var framesWritten = finalStats?.FramesWritten > 0 ? finalStats.FramesWritten : sampledFramesWritten;
                var framesDequeued = finalStats?.WriterFramesDequeued > 0 ? finalStats.WriterFramesDequeued : sampledFramesDequeued;
                var framesCapturedTotal = Math.Max(sampledFramesCaptured, finalStats?.FramesCaptured ?? 0);
                var stopBoundaryFrames = Math.Max(0, framesCapturedTotal - framesAccepted);
                var framesDroppedBeforeEnqueue = Math.Max(0, framesAccepted - Math.Max(sampledFramesEnqueued, framesAccepted));
                var queueDrops = finalStats?.WriterQueueDrops ?? cameraSamples.Max(c => c.WriterQueueDrops);
                var writerQueueMaxDepth = Math.Max(
                    finalStats?.WriterQueueDepthMax ?? 0,
                    cameraSamples.Max(c => c.WriterQueueMaxDepth));
                var writerQueueCapacity = finalStats?.WriterQueueCapacity > 0
                    ? finalStats.WriterQueueCapacity
                    : cameraSamples.Max(c => c.WriterQueueCapacity);
                var writerQueueDepthAtStop = finalStats?.WriterQueueDepthAtStop ?? cameraSamples.Last().WriterQueueDepth;
                var writerWriteMeanMs = finalStats?.AverageVideoWriterWriteMs > 0
                    ? finalStats.AverageVideoWriterWriteMs
                    : MaxNullable(cameraSamples.Select(c => c.WriterWriteMeanMs));
                var writerWriteMaxMs = finalStats?.MaxVideoWriterWriteMs > 0
                    ? finalStats.MaxVideoWriterWriteMs
                    : MaxNullable(cameraSamples.Select(c => c.WriterWriteMaxMs));
                var writerReleased = finalStats?.WriterClosedMonotonicSec > 0;
                var finalFlushCompleted = framesAccepted == framesWritten
                                          && framesDequeued >= framesAccepted
                                          && writerQueueDepthAtStop == 0
                                          && writerReleased;
                var finalFlushTimedOut = !finalFlushCompleted && framesAccepted != framesWritten;

                return new RecordingDiagnosticsCameraSummary
                {
                    CameraIndex = g.Key,
                    CameraName = cameraSamples.Select(c => c.CameraName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                    TimingVerdict = BuildCameraTimingVerdict(
                        requestedFps,
                        stableTiming,
                        queueDrops,
                        framesAccepted,
                        framesWritten,
                        stopBoundaryFrames,
                        finalFlushCompleted,
                        finalFlushTimedOut),
                    RequestedFps = requestedFps,
                    MeasuredFpsByFrameCount = stableTiming.MeasuredFpsByFrameCount,
                    MeasuredFpsByIntervals = stableTiming.MeasuredFpsByIntervals,
                    CaptureIntervalMeanMs = stableTiming.CaptureIntervalMeanMs,
                    CaptureIntervalMedianMs = stableTiming.CaptureIntervalMedianMs,
                    CaptureIntervalStdMs = stableTiming.CaptureIntervalStdMs,
                    CaptureIntervalMinMs = stableTiming.CaptureIntervalMinMs,
                    CaptureIntervalMaxMs = stableTiming.CaptureIntervalMaxMs,
                    CaptureIntervalP95Ms = stableTiming.CaptureIntervalP95Ms,
                    CaptureIntervalP99Ms = stableTiming.CaptureIntervalP99Ms,
                    InstantaneousFpsSpikeMaxIgnored = ignoredInstantaneousFps.Count > 0 ? ignoredInstantaneousFps.Max() : null,
                    InstantaneousFpsSpikeIgnoredCount = ignoredInstantaneousFps.Count,
                    MaxMeasuredFpsRunning = MaxNullable(cameraSamples.Select(c => c.MeasuredFpsRunning)),
                    MinMeasuredFpsRunning = MinNullable(cameraSamples.Select(c => c.MeasuredFpsRunning)),
                    MaxCaptureFpsCurrent = validInstantaneousFps.Count > 0 ? validInstantaneousFps.Max() : null,
                    MaxCaptureIntervalMeanMs = MaxNullable(cameraSamples.Select(c => c.CaptureIntervalMeanMs)),
                    MaxCaptureIntervalMaxMs = MaxNullable(cameraSamples.Select(c => c.CaptureIntervalMaxMs)),
                    MaxCaptureIntervalStdMs = MaxNullable(cameraSamples.Select(c => c.CaptureIntervalStdMs)),
                    FramesCaptured = framesCapturedTotal,
                    FramesWritten = framesWritten,
                    FramesCapturedMinusWritten = framesCapturedTotal - framesWritten,
                    FramesCapturedTotal = framesCapturedTotal,
                    FramesAcceptedForRecording = framesAccepted,
                    FramesEnqueued = Math.Max(sampledFramesEnqueued, framesAccepted),
                    FramesDequeued = framesDequeued,
                    FramesCapturedAfterStopRequested = stopBoundaryFrames,
                    FramesNotRecordedAfterStopRequested = stopBoundaryFrames,
                    FramesDroppedBeforeEnqueue = framesDroppedBeforeEnqueue,
                    FinalFlushCompleted = finalFlushCompleted,
                    FinalFlushTimedOut = finalFlushTimedOut,
                    WriterReleasedSuccessfully = writerReleased,
                    MaxFramesCaptured = framesCapturedTotal,
                    MaxFramesEnqueued = Math.Max(sampledFramesEnqueued, framesAccepted),
                    MaxFramesDequeued = framesDequeued,
                    MaxFramesWritten = framesWritten,
                    MaxWriterQueueDepth = cameraSamples.Max(c => c.WriterQueueDepth),
                    WriterQueueMaxDepth = writerQueueMaxDepth,
                    WriterQueueCapacity = writerQueueCapacity,
                    MaxWriterQueueMaxDepth = writerQueueMaxDepth,
                    MaxWriterQueueFullCount = cameraSamples.Max(c => c.WriterQueueFullCount),
                    MaxWriterQueueDrops = queueDrops,
                    WriterQueueDrops = queueDrops,
                    MaxWriterWriteMeanMs = writerWriteMeanMs,
                    MaxWriterWriteMaxMs = writerWriteMaxMs,
                    WriterWriteMeanMs = writerWriteMeanMs,
                    WriterWriteMaxMs = writerWriteMaxMs,
                    MaxCurrentFileSizeMB = cameraSamples.Max(c => c.CurrentFileSizeMB),
                    FinalFileSizeMB = cameraSamples.Last().CurrentFileSizeMB,
                    EstimatedGBPerHour = EstimateGbPerHour(cameraSamples.Last().CurrentFileSizeMB, recordingHours),
                    MaxFileSizeGrowthMBps = MaxNullable(cameraSamples.Select(c => c.FileSizeGrowthMBps)),
                    AutoFocusSupported = LastNonUnavailable(cameraSamples.Select(c => c.AutoFocusSupported)),
                    AutoFocusEnabled = LastNonUnavailable(cameraSamples.Select(c => c.AutoFocusEnabled)),
                    ManualFocusSupported = LastNonUnavailable(cameraSamples.Select(c => c.ManualFocusSupported)),
                    ManualFocusValue = LastNonUnavailable(cameraSamples.Select(c => c.ManualFocusValue))
                };
            })
            .OrderBy(c => c.CameraIndex)
            .ToList();

        var memory = TryGetMemoryStatus();
        var totalSessionSizeMb = samples.LastOrDefault()?.TotalCurrentFileSizeMB ?? 0;
        var estimatedAllGbPerHour = EstimateGbPerHour(totalSessionSizeMb, recordingHours);
        var estimatedPerCameraGbPerHour = cameraSummaries.Count > 0
            ? cameraSummaries.Average(c => c.EstimatedGBPerHour)
            : 0;
        string? artifactNote = cameraSummaries.Any(c => c.InstantaneousFpsSpikeIgnoredCount > 0)
            ? "Instantaneous FPS spikes were ignored because they exceeded the realistic range for the requested FPS."
            : null;
        var sessionVerdictText = BuildSessionVerdictText(cameraSummaries, samples);

        var activeSlots = _slots
            .Where(s => s.AssignedDeviceId != null)
            .OrderBy(s => s.SlotIndex)
            .Select(s => s.SlotName)
            .ToArray();

        return new RecordingDiagnosticsSummary
        {
            CsvPath = PrivacySanitizer.FileNameOnly(_csvPath),
            SummaryJsonPath = PrivacySanitizer.FileNameOnly(_summaryPath),
            OutputFolderPath = PrivacySanitizer.FileNameOnly(_sessionPath),
            ActiveCameraCount = activeSlots.Length,
            ActiveCameraSlots = activeSlots,
            StartedUtc = samples.FirstOrDefault()?.SampleUtcTime ?? DateTime.UtcNow,
            StoppedUtc = samples.LastOrDefault()?.SampleUtcTime ?? DateTime.UtcNow,
            SampleCount = samples.Count,
            SampleIntervalSeconds = SampleInterval.TotalSeconds,
            AverageCpuPercent = AverageNullable(samples.Select(s => s.CpuPercent)),
            MaxCpuPercent = MaxNullable(samples.Select(s => s.CpuPercent)),
            CpuSamplesOver90Percent = samples.Count(s => s.CpuPercent is > 90),
            MaxProcessMemoryMB = samples.Count > 0 ? samples.Max(s => s.ProcessMemoryMB) : 0,
            ProcessMemoryContinuouslyIncreases = DetectProcessMemoryGrowth(samples),
            MaxProcessWorkingSetMB = samples.Count > 0 ? samples.Max(s => s.ProcessWorkingSetMB ?? 0) : 0,
            MaxProcessPrivateMemoryMB = samples.Count > 0 ? samples.Max(s => s.ProcessPrivateMemoryMB ?? 0) : 0,
            SystemTotalMemoryMB = samples.FirstOrDefault()?.SystemTotalMemoryMB ?? memory.TotalMb,
            MinSystemAvailableMemoryMB = samples.Count > 0 ? samples.Min(s => s.SystemAvailableMemoryMB) : memory.AvailableMb,
            MaxSystemMemoryUsedPercent = samples.Count > 0 ? samples.Max(s => s.SystemMemoryUsedPercent) : memory.UsedPercent,
            MinDiskFreeSpaceGB = samples.Count > 0 ? samples.Min(s => s.DiskFreeSpaceGB ?? 0) : 0,
            MaxTotalCurrentFileSizeMB = samples.Count > 0 ? samples.Max(s => s.TotalCurrentFileSizeMB) : 0,
            TotalSessionSizeMB = totalSessionSizeMb,
            EstimatedGBPerHourPerCamera = estimatedPerCameraGbPerHour,
            EstimatedGBPerHourAllCameras = estimatedAllGbPerHour,
            MaxTotalFileSizeGrowthMBps = MaxNullable(samples.Select(s => s.TotalFileSizeGrowthMBps)),
            MaxTotalWriterQueueDepth = samples.Count > 0 ? samples.Max(s => s.TotalWriterQueueDepth) : 0,
            MaxTotalWriterQueueCapacity = samples.Count > 0 ? samples.Max(s => s.TotalWriterQueueCapacity) : 0,
            // Use the max of sample-based totals and the sum of camera final stats to catch drops that
            // occurred at or after stop (after the last diagnostics sample was taken).
            MaxTotalQueueDrops = Math.Max(
                samples.Count > 0 ? samples.Max(s => s.TotalQueueDrops) : 0,
                cameraSummaries.Sum(c => c.WriterQueueDrops)),
            SessionVerdictText = sessionVerdictText,
            ArtifactNote = artifactNote,
            Cameras = cameraSummaries
        };
    }

    private void WriteSampleRows(RecordingDiagnosticsSample sample)
    {
        if (_writer == null)
            return;

        foreach (var camera in sample.Cameras)
            _writer.WriteLine(BuildRow(sample, camera));
    }

    private static string BuildHeader() => string.Join(",",
    [
        "sampleIndex","sampleLocalTime","sampleUtcTime","elapsedSec","cpuPercent",
        "processMemoryMB","processWorkingSetMB","processPrivateMemoryMB",
        "systemTotalMemoryMB","systemAvailableMemoryMB","systemMemoryUsedPercent",
        "outputFolderPath","diskFreeSpaceGB","totalCurrentFileSizeMB","totalFileSizeGrowthMBps",
        "totalWriterQueueDepth","totalWriterQueueCapacity","totalQueueDrops",
        "cameraIndex","cameraName","requestedFPS","measuredFPSRunning","captureFPSCurrent",
        "captureIntervalMeanMs","captureIntervalMinMs","captureIntervalMaxMs","captureIntervalStdMs",
        "longGapCount","severeGapCount","shortIntervalCount",
        "framesCaptured","framesEnqueued","framesDequeued","framesWritten",
        "writerQueueDepth","writerQueueCapacity","writerQueueMaxDepth","writerQueueFullCount","writerQueueDrops",
        "writerWriteMeanMs","writerWriteMaxMs","writerWriteP95Ms","writerBacklogFrames","writerBacklogSeconds",
        "currentFileSizeMB","fileSizeGrowthMBps",
        "autoFocusSupported","autoFocusEnabled","manualFocusSupported","manualFocusValue"
    ]);

    private static string BuildRow(RecordingDiagnosticsSample sample, RecordingDiagnosticsCameraSample camera)
    {
        var values = new string?[]
        {
            sample.SampleIndex.ToString(CultureInfo.InvariantCulture),
            sample.SampleLocalTime.ToString("O", CultureInfo.InvariantCulture),
            sample.SampleUtcTime.ToString("O", CultureInfo.InvariantCulture),
            Format(sample.ElapsedSec),
            Format(sample.CpuPercent),
            Format(sample.ProcessMemoryMB),
            Format(sample.ProcessWorkingSetMB),
            Format(sample.ProcessPrivateMemoryMB),
            Format(sample.SystemTotalMemoryMB),
            Format(sample.SystemAvailableMemoryMB),
            Format(sample.SystemMemoryUsedPercent),
            PrivacySanitizer.SanitizeForOutput(sample.OutputFolderPath),
            Format(sample.DiskFreeSpaceGB),
            Format(sample.TotalCurrentFileSizeMB),
            Format(sample.TotalFileSizeGrowthMBps),
            sample.TotalWriterQueueDepth.ToString(CultureInfo.InvariantCulture),
            sample.TotalWriterQueueCapacity.ToString(CultureInfo.InvariantCulture),
            sample.TotalQueueDrops.ToString(CultureInfo.InvariantCulture),
            camera.CameraIndex.ToString(CultureInfo.InvariantCulture),
            PrivacySanitizer.SanitizeForOutput(camera.CameraName),
            Format(camera.RequestedFps),
            Format(camera.MeasuredFpsRunning),
            Format(camera.CaptureFpsCurrent),
            Format(camera.CaptureIntervalMeanMs),
            Format(camera.CaptureIntervalMinMs),
            Format(camera.CaptureIntervalMaxMs),
            Format(camera.CaptureIntervalStdMs),
            camera.LongGapCount.ToString(CultureInfo.InvariantCulture),
            camera.SevereGapCount.ToString(CultureInfo.InvariantCulture),
            camera.ShortIntervalCount.ToString(CultureInfo.InvariantCulture),
            camera.FramesCaptured.ToString(CultureInfo.InvariantCulture),
            camera.FramesEnqueued.ToString(CultureInfo.InvariantCulture),
            camera.FramesDequeued.ToString(CultureInfo.InvariantCulture),
            camera.FramesWritten.ToString(CultureInfo.InvariantCulture),
            camera.WriterQueueDepth.ToString(CultureInfo.InvariantCulture),
            camera.WriterQueueCapacity.ToString(CultureInfo.InvariantCulture),
            camera.WriterQueueMaxDepth.ToString(CultureInfo.InvariantCulture),
            camera.WriterQueueFullCount.ToString(CultureInfo.InvariantCulture),
            camera.WriterQueueDrops.ToString(CultureInfo.InvariantCulture),
            Format(camera.WriterWriteMeanMs),
            Format(camera.WriterWriteMaxMs),
            Format(camera.WriterWriteP95Ms),
            camera.WriterBacklogFrames.ToString(CultureInfo.InvariantCulture),
            Format(camera.WriterBacklogSeconds),
            Format(camera.CurrentFileSizeMB),
            Format(camera.FileSizeGrowthMBps),
            camera.AutoFocusSupported,
            camera.AutoFocusEnabled,
            camera.ManualFocusSupported,
            camera.ManualFocusValue
        };
        return string.Join(",", values.Select(EscapeCsv));
    }

    private static string Format(double? value) =>
        value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value.Value.ToString("F6", CultureInfo.InvariantCulture)
            : "";

    private static string EscapeCsv(string? value)
    {
        value ??= "";
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static double? MaxNullable(IEnumerable<double?> values)
    {
        var present = values.Where(v => v.HasValue && !double.IsNaN(v.Value)).Select(v => v!.Value).ToList();
        return present.Count > 0 ? present.Max() : null;
    }

    private StableCameraTimingSummary BuildStableCameraTimingSummary(int cameraIndex, double? requestedFps)
    {
        try
        {
            var csvPath = ResolveFrameTimestampCsvPath(cameraIndex);
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
                return StableCameraTimingSummary.Empty;

            var captureTimes = new List<double>();
            var intervals = new List<double>();
            long duplicateFrames = 0;
            long placeholderFrames = 0;
            foreach (var line in File.ReadLines(csvPath).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 11)
                    continue;

                if (string.Equals(parts[9].Trim(), "true", StringComparison.OrdinalIgnoreCase))
                    duplicateFrames++;
                if (string.Equals(parts[10].Trim(), "true", StringComparison.OrdinalIgnoreCase))
                    placeholderFrames++;

                var original = parts[8].Trim();
                if (!string.Equals(original, "true", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var captureMono)
                    && captureMono > 0)
                {
                    captureTimes.Add(captureMono);
                }

                if (double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var intervalMs)
                    && IsValidIntervalMs(intervalMs, requestedFps))
                {
                    intervals.Add(intervalMs);
                }
            }

            var durationSec = captureTimes.Count >= 2 ? captureTimes[^1] - captureTimes[0] : 0;
            var measuredByFrameCount = durationSec > 0
                ? captureTimes.Count / durationSec
                : (double?)null;
            var measuredByIntervals = intervals.Count > 0
                ? intervals.Count / (intervals.Sum() / 1000.0)
                : (double?)null;

            return new StableCameraTimingSummary(
                measuredByFrameCount,
                measuredByIntervals,
                AverageNullable(intervals.Select(v => (double?)v)),
                Percentile(intervals, 0.50),
                StdDev(intervals),
                intervals.Count > 0 ? intervals.Min() : null,
                intervals.Count > 0 ? intervals.Max() : null,
                Percentile(intervals, 0.95),
                Percentile(intervals, 0.99),
                duplicateFrames,
                placeholderFrames);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_STABLE_TIMING_FAILED cam{cameraIndex} error={ex.Message}");
            return StableCameraTimingSummary.Empty;
        }
    }

    private RecordingCameraStats? GetFinalRecordingStats(int cameraIndex)
    {
        var slot = _slots.FirstOrDefault(s => s.SlotIndex + 1 == cameraIndex);
        return slot?.LastOpenCvRecordingStats;
    }

    private string? ResolveFrameTimestampCsvPath(int cameraIndex)
    {
        var preferred = Path.Combine(_sessionPath, $"cam{cameraIndex}", $"cam{cameraIndex}_frame_timestamps.csv");
        if (File.Exists(preferred))
            return preferred;

        try
        {
            return Directory
                .EnumerateFiles(_sessionPath, $"cam{cameraIndex}_frame_timestamps.csv", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidIntervalMs(double intervalMs, double? requestedFps)
    {
        if (double.IsNaN(intervalMs) || double.IsInfinity(intervalMs) || intervalMs <= 0)
            return false;
        if (requestedFps is > 0 && 1000.0 / intervalMs > requestedFps.Value * 2.0)
            return false;
        return true;
    }

    private static IEnumerable<double> FilterValidInstantaneousFps(
        IEnumerable<RecordingDiagnosticsCameraSample> samples,
        double? requestedFps) =>
        samples
            .Select(s => s.CaptureFpsCurrent)
            .Where(v => v.HasValue && IsValidInstantaneousFps(v.Value, requestedFps))
            .Select(v => v!.Value);

    private static IEnumerable<double> FilterIgnoredInstantaneousFps(
        IEnumerable<RecordingDiagnosticsCameraSample> samples,
        double? requestedFps) =>
        samples
            .Select(s => s.CaptureFpsCurrent)
            .Where(v => v.HasValue && IsIgnoredInstantaneousFps(v.Value, requestedFps))
            .Select(v => v!.Value);

    private static bool IsValidInstantaneousFps(double fps, double? requestedFps)
    {
        if (double.IsNaN(fps) || double.IsInfinity(fps) || fps < 0)
            return false;
        return requestedFps is not > 0 || fps <= requestedFps.Value * 2.0;
    }

    private static bool IsIgnoredInstantaneousFps(double fps, double? requestedFps) =>
        requestedFps is > 0 && !double.IsNaN(fps) && !double.IsInfinity(fps) && fps > requestedFps.Value * 2.0;

    private static double? Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        if (values.Count == 0)
            return null;

        var ordered = values.OrderBy(v => v).ToArray();
        if (ordered.Length == 1)
            return ordered[0];

        var position = (ordered.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return ordered[lower];
        var weight = position - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * weight;
    }

    private static double? StdDev(IReadOnlyCollection<double> values)
    {
        if (values.Count <= 1)
            return null;

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    private static string BuildCameraTimingVerdict(
        double? requestedFps,
        StableCameraTimingSummary timing,
        long writerQueueDrops,
        long framesAcceptedForRecording,
        long framesWritten,
        long stopBoundaryFrames,
        bool finalFlushCompleted,
        bool finalFlushTimedOut)
    {
        if (timing.PlaceholderFrames > 0
            || timing.DuplicateFrames > 0
            || IsSevereFrameLoss(framesAcceptedForRecording - framesWritten, requestedFps))
        {
            return "FAIL";
        }

        if (writerQueueDrops > 0)
            return "SERIOUS_WARNING";

        if (framesAcceptedForRecording != framesWritten || !finalFlushCompleted)
            return "SERIOUS_WARNING";

        var measured = timing.MeasuredFpsByFrameCount ?? timing.MeasuredFpsByIntervals;
        if (measured is not > 0 || requestedFps is not > 0)
            return "PASS_WITH_WARNING";

        var requestedIntervalMs = 1000.0 / requestedFps.Value;
        var stableIntervals = IsStableIntervals(timing, requestedIntervalMs);
        if (!stableIntervals)
            return "PASS_WITH_WARNING";

        if (stopBoundaryFrames > 0)
            return "PASS_ORIGINAL_TIMING_WITH_STOP_BOUNDARY_NOTE";

        var fpsDelta = requestedFps.Value - measured.Value;
        if (Math.Abs(fpsDelta) <= 0.1)
            return "PASS_ORIGINAL_TIMING";

        if (fpsDelta > 0 && measured.Value >= requestedFps.Value * 0.93)
            return "PASS_ORIGINAL_TIMING_WITH_NOTE";

        return "PASS_WITH_WARNING";
    }

    private static bool IsSevereFrameLoss(long framesCapturedMinusWritten, double? requestedFps)
    {
        var loss = Math.Abs(framesCapturedMinusWritten);
        var threshold = requestedFps is > 0 ? Math.Max(10, (long)Math.Ceiling(requestedFps.Value * 2)) : 10;
        return loss > threshold;
    }

    private static bool IsStableIntervals(StableCameraTimingSummary timing, double requestedIntervalMs)
    {
        var stdOk = timing.CaptureIntervalStdMs is > 0
            ? timing.CaptureIntervalStdMs <= requestedIntervalMs * 0.25
            : true;
        var p99Ok = timing.CaptureIntervalP99Ms is > 0
            ? timing.CaptureIntervalP99Ms <= requestedIntervalMs * 1.75
            : true;
        return stdOk && p99Ok;
    }

    private static string BuildSessionVerdictText(
        IReadOnlyList<RecordingDiagnosticsCameraSummary> cameras,
        IReadOnlyList<RecordingDiagnosticsSample> samples)
    {
        var cameraCount = cameras.Count;
        var queueDrops = cameras.Sum(c => c.WriterQueueDrops);
        var resourcesSafe = ResourcesWithinSafeRange(samples);
        var hasSlightlyBelow = cameras.Any(c =>
            c.TimingVerdict == "PASS_ORIGINAL_TIMING_WITH_NOTE");
        var hasStopBoundary = cameras.Any(c =>
            c.TimingVerdict == "PASS_ORIGINAL_TIMING_WITH_STOP_BOUNDARY_NOTE");
        var hasWarnings = cameras.Any(c =>
            c.TimingVerdict is "PASS_WITH_WARNING" or "SERIOUS_WARNING" or "FAIL");

        var cpuOk = samples.Count == 0 || samples.Count(s => s.CpuPercent is > 90) == 0;
        var ramOk = samples.Count == 0 || samples.Min(s => s.SystemAvailableMemoryMB) >= 1000;
        var diskOk = samples.Count == 0 || samples.Min(s => s.DiskFreeSpaceGB ?? double.MaxValue) >= 1;

        if (queueDrops == 0)
        {
            if (cpuOk && ramOk && diskOk)
            {
                return "Recording completed successfully. No writer queue drops were detected. CPU, RAM, and disk space were within safe range.";
            }
            if (!cpuOk && ramOk && diskOk)
            {
                return "Recording completed successfully. CPU briefly reached a high value, but no writer queue drops were detected. Review diagnostics if recording problems appear.";
            }
        }

        var text = new StringBuilder();
        text.Append($"{cameraCount}-camera recording completed successfully. ");
        text.Append(queueDrops == 0
            ? "No writer queue drops were detected. "
            : $"{queueDrops} writer queue drop(s) were detected. ");
        text.Append(resourcesSafe
            ? "CPU, RAM, and disk space were within safe range. "
            : "Review CPU, RAM, or disk diagnostics before scientific use. ");
        if (hasStopBoundary)
            text.Append("Total captured-vs-written differences were explained by stop-boundary frames after accepted frames were written. ");
        if (hasSlightlyBelow)
            text.Append("Some cameras delivered stable real-frame capture slightly below requested FPS. ");
        else if (!hasWarnings)
            text.Append("All cameras delivered stable real-frame capture near requested FPS. ");
        else
            text.Append("One or more cameras had timing warnings. ");
        text.Append("Use metadata timestamps for timing-sensitive analysis.");
        return text.ToString();
    }

    private static bool ResourcesWithinSafeRange(IReadOnlyList<RecordingDiagnosticsSample> samples)
    {
        if (samples.Count == 0)
            return true;

        var cpuOk = samples.Count(s => s.CpuPercent is > 90) == 0;
        var ramOk = samples.Min(s => s.SystemAvailableMemoryMB) >= 1000;
        var diskOk = samples.Min(s => s.DiskFreeSpaceGB ?? double.MaxValue) >= 1;
        return cpuOk && ramOk && diskOk;
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var present = values.Where(v => v.HasValue && !double.IsNaN(v.Value)).Select(v => v!.Value).ToList();
        return present.Count > 0 ? present.Average() : null;
    }

    private static bool DetectProcessMemoryGrowth(IReadOnlyList<RecordingDiagnosticsSample> samples)
    {
        if (samples.Count < 5)
            return false;

        var first = samples.First().ProcessMemoryMB;
        var last = samples.Last().ProcessMemoryMB;
        if (last - first < 100)
            return false;

        var increases = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i].ProcessMemoryMB >= samples[i - 1].ProcessMemoryMB)
                increases++;
        }

        return increases >= (samples.Count - 1) * 0.8;
    }

    private static double EstimateGbPerHour(double sizeMb, double recordingHours) =>
        recordingHours > 0 ? sizeMb / 1024.0 / recordingHours : 0;

    private static double? MinNullable(IEnumerable<double?> values)
    {
        var present = values.Where(v => v.HasValue && !double.IsNaN(v.Value)).Select(v => v!.Value).ToList();
        return present.Count > 0 ? present.Min() : null;
    }

    private static string LastNonUnavailable(IEnumerable<string> values) =>
        values.LastOrDefault(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "unavailable", StringComparison.OrdinalIgnoreCase))
        ?? "unavailable";

    private static double TryGetFileSizeMb(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? BytesToMb(new FileInfo(path).Length)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double? TryGetDiskFreeSpaceGb(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
                return null;
            return new DriveInfo(root).AvailableFreeSpace / 1024d / 1024d / 1024d;
        }
        catch
        {
            return null;
        }
    }

    private static double BytesToMb(long bytes) => bytes / 1024d / 1024d;

    private static (double TotalMb, double AvailableMb, double UsedPercent) TryGetMemoryStatus()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                var total = BytesToMb((long)status.TotalPhysicalMemory);
                var available = BytesToMb((long)status.AvailablePhysicalMemory);
                var used = total > 0 ? Math.Max(0, Math.Min(100, (total - available) / total * 100.0)) : 0;
                return (total, available, used);
            }
        }
        catch
        {
            // Best-effort diagnostics only.
        }

        return (0, 0, 0);
    }

    private void SafeDisposeWriter()
    {
        try { _writer?.Dispose(); }
        catch { }
        finally { _writer = null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed class CameraSampleState
    {
        public double LastElapsedSec { get; set; }
        public long LastFramesCaptured { get; set; }
        public double LastFileSizeMB { get; set; }
    }

    private sealed record StableCameraTimingSummary(
        double? MeasuredFpsByFrameCount,
        double? MeasuredFpsByIntervals,
        double? CaptureIntervalMeanMs,
        double? CaptureIntervalMedianMs,
        double? CaptureIntervalStdMs,
        double? CaptureIntervalMinMs,
        double? CaptureIntervalMaxMs,
        double? CaptureIntervalP95Ms,
        double? CaptureIntervalP99Ms,
        long DuplicateFrames,
        long PlaceholderFrames)
    {
        public static StableCameraTimingSummary Empty { get; } = new(null, null, null, null, null, null, null, null, null, 0, 0);
    }
}
