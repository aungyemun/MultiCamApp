////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using System.Diagnostics;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Tracks live frame delivery rate and drop counts for the V2 preview session.
/// Preview and recording are independent: this monitor tracks preview frames only.
/// The legacy recording pipeline runs its own diagnostics and is not affected.
/// </summary>
public sealed class RecordingHealthMonitor : IDisposable
{
    private readonly Stopwatch _sessionClock = new();
    private long _framesDelivered;
    // Rolling window for live FPS computation (last N timestamps)
    private readonly long[] _recentFrameTicks = new long[64];
    private int _recentHead;
    private bool _disposed;

    /// <summary>True while the session clock is running.</summary>
    public bool IsMonitoring => _sessionClock.IsRunning;

    public void StartSession()
    {
        _framesDelivered = 0;
        _recentHead      = 0;
        Array.Clear(_recentFrameTicks);
        _sessionClock.Restart();
    }

    public void StopSession() => _sessionClock.Stop();

    /// <summary>Called for each frame successfully delivered to the preview renderer.</summary>
    public void NotifyFrameDelivered()
    {
        Interlocked.Increment(ref _framesDelivered);
        // Record tick for rolling FPS
        int slot = (int)(Interlocked.Increment(ref _recentHead) % _recentFrameTicks.Length);
        _recentFrameTicks[slot] = Stopwatch.GetTimestamp();
    }

    /// <summary>Returns a point-in-time health snapshot with live rolling FPS.</summary>
    public CameraHealthSnapshot GetSnapshot()
    {
        var elapsed = _sessionClock.Elapsed;
        return new CameraHealthSnapshot
        {
            SessionElapsed  = elapsed,
            FramesDelivered = _framesDelivered,
            // FramesDropped always 0: MediaFrameReader.Realtime drops frames silently with no callback.
            AverageFps      = elapsed.TotalSeconds > 0
                                  ? _framesDelivered / elapsed.TotalSeconds
                                  : 0.0,
            LiveFps         = ComputeRollingFps(),
        };
    }

    // Average FPS over the most recent ~2 seconds using the circular tick buffer.
    private double ComputeRollingFps()
    {
        long now    = Stopwatch.GetTimestamp();
        long window = (long)(Stopwatch.Frequency * 2.0); // 2-second window
        int  count  = 0;
        foreach (var t in _recentFrameTicks)
        {
            if (t > 0 && now - t <= window) count++;
        }
        return count / 2.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionClock.Stop();
    }
}

/// <summary>Point-in-time recording/preview health snapshot.</summary>
public sealed class CameraHealthSnapshot
{
    public TimeSpan SessionElapsed { get; init; }
    /// <summary>
    /// Frames delivered since preview started (preview-inclusive: includes frames before recording began).
    /// Use <see cref="FramesSubmittedSincePreviewStart"/> as an alias with a clearer name.
    /// </summary>
    public long FramesDelivered { get; init; }
    /// <summary>
    /// Always 0 for V2 pipeline — <see cref="MediaFrameReaderAcquisitionMode.Realtime"/> drops
    /// frames silently with no drop-count callback. Do not interpret 0 as "no drops occurred."
    /// </summary>
    public long FramesDropped { get; init; }
    /// <summary>Average FPS over the entire session (wall-clock / total frames).</summary>
    public double AverageFps { get; init; }
    /// <summary>Rolling FPS over approximately the last 2 seconds.</summary>
    public double LiveFps { get; init; }
    /// <summary>Always true — drop count is undetectable via this API; see <see cref="FramesDropped"/>.</summary>
    public bool IsHealthy => true;

    /// <summary>
    /// Frames delivered since preview started (preview-inclusive).
    /// Alias for <see cref="FramesDelivered"/> with explicit scope naming (v1.2.14-alpha).
    /// Resets when <see cref="RecordingHealthMonitor.StartSession"/> is called (at preview start).
    /// Includes frames delivered both during preview and during recording.
    /// </summary>
    public long FramesSubmittedSincePreviewStart => FramesDelivered;
}
