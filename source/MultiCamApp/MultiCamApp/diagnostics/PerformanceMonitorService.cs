using System.Diagnostics;

namespace MultiCamApp.Diagnostics;

public sealed class PerformanceMonitorService : IDisposable
{
    private readonly Func<IReadOnlyList<CameraPerformanceSampleSource>> _cameraSource;
    private readonly Func<string> _runStateSource;
    private readonly object _gate = new();
    private readonly List<PerformanceSnapshot> _snapshots = [];
    private readonly Dictionary<int, CameraCounters> _lastCameraCounters = [];
    private readonly Process _process;
    private PerformanceLogWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private DateTime _startedLocal;
    private DateTime _lastCpuSampleLocal;
    private TimeSpan _lastProcessCpu;

    public PerformanceMonitorService(
        Func<IReadOnlyList<CameraPerformanceSampleSource>> cameraSource,
        Func<string> runStateSource)
    {
        _cameraSource = cameraSource;
        _runStateSource = runStateSource;
        _process = Process.GetCurrentProcess();
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _cts is { IsCancellationRequested: false };
        }
    }

    public void StartIfNeeded()
    {
        lock (_gate)
        {
            if (_cts is { IsCancellationRequested: false })
                return;

            _startedLocal = DateTime.Now;
            _lastCpuSampleLocal = _startedLocal;
            try { _lastProcessCpu = _process.TotalProcessorTime; }
            catch { _lastProcessCpu = TimeSpan.Zero; }
            _snapshots.Clear();
            _lastCameraCounters.Clear();
            _writer = new PerformanceLogWriter(startedLocal: _startedLocal);
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_gate)
        {
            cts = _cts;
            loopTask = _loopTask;
        }

        if (cts == null)
            return;

        try { cts.Cancel(); }
        catch { /* best effort */ }

        try
        {
            if (loopTask != null)
                await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch { /* diagnostic monitor must never affect app behavior */ }
        finally
        {
            cts.Dispose();
        }

        PerformanceLogWriter? writer;
        List<PerformanceSnapshot> snapshots;
        lock (_gate)
        {
            writer = _writer;
            snapshots = _snapshots.ToList();
            _cts = null;
            _loopTask = null;
            _writer = null;
        }

        try
        {
            writer?.WriteSummary(snapshots);
        }
        catch { /* summary write is best-effort diagnostics only */ }
    }

    public PerformanceSnapshot CaptureSnapshotForTest() => CaptureSnapshot(DateTime.Now);

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = CaptureSnapshot(DateTime.Now);
                PerformanceLogWriter? writer;
                lock (_gate)
                {
                    _snapshots.Add(snapshot);
                    writer = _writer;
                }

                writer?.Append(snapshot);
            }
            catch
            {
                // Performance diagnostics are support logs only; never disturb preview or recording.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private PerformanceSnapshot CaptureSnapshot(DateTime now)
    {
        var elapsed = Math.Max(0, (now - _startedLocal).TotalSeconds);
        var processCpu = TryGetProcessCpuPercent(now);
        var cameras = SafeReadCameras();

        _process.Refresh();
        var snapshot = new PerformanceSnapshot
        {
            TimestampLocal = now,
            ElapsedSeconds = elapsed,
            RunState = SafeReadRunState(),
            ProcessCpuPercent = processCpu,
            TotalCpuPercent = null,
            ProcessMemoryMb = BytesToMb(SafeGet(() => _process.PrivateMemorySize64)),
            WorkingSetMb = BytesToMb(SafeGet(() => _process.WorkingSet64)),
            GcMemoryMb = BytesToMb(SafeGet(() => GC.GetTotalMemory(forceFullCollection: false))),
            ActiveCameraCount = cameras.Count(c => c.IsActive)
        };

        foreach (var camera in cameras.OrderBy(c => c.Slot))
        {
            var previous = _lastCameraCounters.TryGetValue(camera.Slot, out var counters)
                ? counters
                : new CameraCounters(camera.FramesCapturedTotal, camera.FramesWrittenTotal, camera.WriterQueueDropsTotal, now);

            var seconds = Math.Max(0.001, (now - previous.TimestampLocal).TotalSeconds);
            var capturedDelta = Math.Max(0, camera.FramesCapturedTotal - previous.FramesCapturedTotal);
            var writtenDelta = Math.Max(0, camera.FramesWrittenTotal - previous.FramesWrittenTotal);
            var dropsDelta = Math.Max(0, camera.WriterQueueDropsTotal - previous.WriterQueueDropsTotal);

            snapshot.Cameras.Add(new CameraPerformanceSnapshot
            {
                Slot = camera.Slot,
                Status = camera.Status,
                PreviewFps = camera.PreviewFps,
                FramesCapturedTotal = camera.FramesCapturedTotal,
                FramesCapturedDelta = capturedDelta,
                FramesCapturedFps = capturedDelta / seconds,
                FramesWrittenTotal = camera.FramesWrittenTotal,
                FramesWrittenDelta = writtenDelta,
                FramesWrittenFps = writtenDelta / seconds,
                WriterQueueDropsTotal = camera.WriterQueueDropsTotal,
                WriterQueueDropsDelta = dropsDelta,
                PreviewStalenessSeconds = camera.PreviewStalenessSeconds
            });

            _lastCameraCounters[camera.Slot] = new CameraCounters(
                camera.FramesCapturedTotal,
                camera.FramesWrittenTotal,
                camera.WriterQueueDropsTotal,
                now);
        }

        if (snapshot.TotalCpuPercent == null)
            snapshot.Notes.Add("Total CPU percent unavailable; process CPU percent was sampled.");

        return snapshot;
    }

    private double? TryGetProcessCpuPercent(DateTime now)
    {
        try
        {
            var cpu = _process.TotalProcessorTime;
            var wallSeconds = Math.Max(0.001, (now - _lastCpuSampleLocal).TotalSeconds);
            var cpuSeconds = Math.Max(0, (cpu - _lastProcessCpu).TotalSeconds);
            _lastCpuSampleLocal = now;
            _lastProcessCpu = cpu;
            return Math.Clamp(cpuSeconds / wallSeconds / Math.Max(1, Environment.ProcessorCount) * 100.0, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<CameraPerformanceSampleSource> SafeReadCameras()
    {
        try { return _cameraSource(); }
        catch { return []; }
    }

    private string SafeReadRunState()
    {
        try { return _runStateSource(); }
        catch { return "Unknown"; }
    }

    private static long SafeGet(Func<long> read)
    {
        try { return read(); }
        catch { return 0; }
    }

    private static double BytesToMb(long bytes) => bytes <= 0 ? 0 : bytes / 1024d / 1024d;

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch { }
    }

    private sealed record CameraCounters(
        long FramesCapturedTotal,
        long FramesWrittenTotal,
        long WriterQueueDropsTotal,
        DateTime TimestampLocal);
}
