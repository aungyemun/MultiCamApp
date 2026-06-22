using System.Diagnostics;

namespace MultiCamApp.Diagnostics;

/// <summary>Background FPS / drop estimates (not UI-thread timers for recording truth).</summary>
public sealed class FpsMonitor
{
    private CancellationTokenSource? _cts;
    private long _ticks;
    private long _dropped;

    public double AverageFps { get; private set; }
    public long FrameCount => _ticks;
    public long DroppedFrames => _dropped;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void NotifyFrame() => Interlocked.Increment(ref _ticks);

    public void NotifyDropped() => Interlocked.Increment(ref _dropped);

    private async Task RunAsync(CancellationToken token)
    {
        long last = 0;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(1000, token);
            var now = Interlocked.Read(ref _ticks);
            AverageFps = now - last;
            last = now;
        }
    }
}
