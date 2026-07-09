using MultiCamApp.Utils;
using System.Diagnostics;
using System.Windows.Threading;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// Lightweight background watchdog that detects UI thread stalls during preview and recording.
/// Posts a periodic heartbeat to the WPF dispatcher and measures response latency.
/// Thresholds: 250 ms minor stall, 1000 ms warning, 3000 ms critical freeze.
/// Thread-safe; does not crash or block the app when a freeze is detected.
/// </summary>
public sealed class UiFreezeWatchdog : IDisposable
{
    private const int HeartbeatIntervalMs = 100;
    private const int MinorStallMs        = 250;
    private const int WarnStallMs         = 1000;
    private const int CriticalFreezeMs    = 3000;

    private readonly Dispatcher _dispatcher;
    private System.Threading.Timer? _timer;
    private volatile bool _disposed;
    private volatile bool _heartbeatPending;
    private volatile bool _pendingCountedAsCritical; // prevents counting same freeze multiple times
    private long _heartbeatSentTick;   // Stopwatch.GetTimestamp() at send time

    // Diagnostics — volatile for safe cross-thread read by metadata writer.
    private volatile int  _freezeCount;
    private long _maxFreezeMs;
    private volatile string _lastFreezeState = "None";

    public int    FreezeCount       => _freezeCount;
    public long   MaxFreezeMs       => Interlocked.Read(ref _maxFreezeMs);
    public string LastFreezeState   => _lastFreezeState;
    public bool   AnyFreezeDetected => _freezeCount > 0;

    /// <summary>Set by the owner to label the current app state in freeze logs.</summary>
    public string CurrentAppState { get; set; } = "Unknown";

    public UiFreezeWatchdog(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_disposed) return;
        _heartbeatPending          = false;
        _pendingCountedAsCritical  = false;
        _timer = new System.Threading.Timer(OnTimerTick, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
        AppDiagnosticLogger.Runtime("UI_FREEZE_WATCHDOG_STARTED interval=100ms");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        AppDiagnosticLogger.Runtime(
            $"UI_FREEZE_WATCHDOG_STOPPED freezeCount={_freezeCount} maxFreezeMs={MaxFreezeMs}");
    }

    /// <summary>
    /// Clears the accumulated freeze counters so the next recording's metadata reflects only
    /// what happens during that recording. Without this, <see cref="FreezeCount"/>/<see cref="MaxFreezeMs"/>/
    /// <see cref="LastFreezeState"/> are lifetime-of-the-app-process totals — a freeze during one
    /// recording session was found bleeding into every subsequent recording's metadata file,
    /// falsely reporting a freeze that never happened during that later recording. Call at the
    /// start of each new recording.
    /// </summary>
    public void Reset()
    {
        _freezeCount              = 0;
        Interlocked.Exchange(ref _maxFreezeMs, 0);
        _lastFreezeState          = "None";
        _pendingCountedAsCritical = false;
    }

    private void OnTimerTick(object? _state)
    {
        if (_disposed) return;

        if (_heartbeatPending)
        {
            // Previous heartbeat still not processed — measure stall duration.
            var stallMs = (long)Stopwatch.GetElapsedTime(Interlocked.Read(ref _heartbeatSentTick)).TotalMilliseconds;

            if (stallMs >= CriticalFreezeMs)
            {
                // Count each critical freeze only once (not every 100 ms tick during the freeze).
                if (!_pendingCountedAsCritical)
                {
                    _pendingCountedAsCritical = true;
                    Interlocked.Increment(ref _freezeCount);
                    if (stallMs > Interlocked.Read(ref _maxFreezeMs))
                        Interlocked.Exchange(ref _maxFreezeMs, stallMs);
                    _lastFreezeState = CurrentAppState;
                    AppDiagnosticLogger.Runtime(
                        $"UI_FREEZE_CRITICAL stallMs={stallMs} state={CurrentAppState} " +
                        $"totalFreezes={_freezeCount}");
                }
                else
                {
                    // Update max even for prolonged freeze
                    if (stallMs > Interlocked.Read(ref _maxFreezeMs))
                        Interlocked.Exchange(ref _maxFreezeMs, stallMs);
                }
            }
            else if (stallMs >= WarnStallMs)
            {
                if (stallMs > Interlocked.Read(ref _maxFreezeMs))
                    Interlocked.Exchange(ref _maxFreezeMs, stallMs);
                AppDiagnosticLogger.Runtime(
                    $"UI_FREEZE_WARNING stallMs={stallMs} state={CurrentAppState}");
            }
            else if (stallMs >= MinorStallMs)
            {
                AppDiagnosticLogger.Runtime(
                    $"UI_FREEZE_MINOR stallMs={stallMs} state={CurrentAppState}");
            }
            // Under minor threshold: normal async scheduling jitter, ignore.
            return;
        }

        // Send a fresh heartbeat.
        _pendingCountedAsCritical = false;
        Interlocked.Exchange(ref _heartbeatSentTick, Stopwatch.GetTimestamp());
        _heartbeatPending = true;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_disposed) return;
            var latencyMs = (long)Stopwatch.GetElapsedTime(Interlocked.Read(ref _heartbeatSentTick)).TotalMilliseconds;
            _heartbeatPending = false;

            if (latencyMs >= WarnStallMs)
                AppDiagnosticLogger.Runtime(
                    $"UI_HEARTBEAT_LATENCY_HIGH latencyMs={latencyMs} state={CurrentAppState}");
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
