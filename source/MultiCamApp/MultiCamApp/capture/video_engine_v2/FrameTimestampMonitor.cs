////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Monitors per-frame timestamps and writes a per-frame CSV file alongside every recording.
/// Detects timing anomalies (gaps, fast-playback, jitter).
/// </summary>
/// <remarks>
/// CSV fields (v1.1.20):
/// frameIndex, captureTimestampUtc, appTimestampMsFromRecStart,
/// appTimestampSecondsFromRecStart, monotonicTicks,
/// frameIntervalMs, estimatedCaptureFps, droppedFrameWarning
///
/// <para><b>Why CSV rows are queued, not written inline (added v1.2.37):</b> <see cref="RecordFrame"/>
/// runs synchronously on the camera's frame-arrived callback thread, immediately before that same
/// frame is handed to the GPU preview renderer (see <c>CameraPipelineV2.OnFrameArrived</c>). A prior
/// version wrote and periodically flushed the CSV file directly on this thread. Any transient disk
/// I/O latency there (antivirus real-time scanning, search indexer, disk contention — all common on
/// consumer PCs) blocked frame delivery to the renderer for that duration; with 4 cameras each doing
/// this independently, a multi-second stall on any one camera's write was enough to freeze that
/// camera's on-screen preview while the WPF dispatcher (and <c>UiFreezeWatchdog</c>, which only pings
/// the dispatcher) stayed completely unaffected — the freeze was invisible to the app's own freeze
/// detection. Confirmed via a real recording session: the WPF dispatcher watchdog logged a max 1.4s
/// stall, while a synchronized screen recording of the same session showed the live preview frozen
/// for a cumulative 87 of 115 seconds (75%) across 114 separate freeze events, entirely absent from
/// Windows Camera app under the same conditions. Now <see cref="RecordFrame"/> only does the (fast,
/// allocation-light) timing math and enqueues the formatted row; a dedicated background task drains
/// the queue and performs the actual file I/O, so a slow disk can never block frame delivery.
/// </para>
/// </remarks>
public sealed class FrameTimestampMonitor : IDisposable
{
    private readonly Stopwatch _wallClock = new();
    private long _frameCount;
    private TimeSpan _lastPts = TimeSpan.Zero;
    private StreamWriter? _csvWriter;
    private bool _csvOpen;
    private bool _disposed;
    private double _csvStartOffsetMs;   // wallClock elapsed when CSV was opened (recording start)
    private Channel<string>? _csvQueue;
    private Task? _csvWriterTask;

    // Expected inter-frame interval for anomaly detection
    private double _expectedIntervalMs = 1000.0 / 30.0;

    /// <summary>Raised for each frame with its timing analysis.</summary>
    public event EventHandler<CameraFrameTimestampInfo>? TimestampSampled;

    /// <summary>Raised when a timing anomaly is detected (gap ≥ 2× expected interval).</summary>
    public event EventHandler<CameraFrameTimestampInfo>? AnomalyDetected;

    /// <summary>Total frames recorded this session.</summary>
    public long FrameCount => _frameCount;

    // STABLE_CORE_V2 exception #8: CSV rows are written only for frames the encoder accepted, so
    // CSV row k == MP4 frame k. Previously every delivered frame (including the ~1-2 s while the
    // encoder was opening and while it was stopping) got a row — measured on real hardware: +71
    // rows / ~2.4 s of timestamps with no corresponding video frame, misaligning per-frame analysis.
    private long _csvRowIndex;
    private TimeSpan _csvFirstPts;
    private TimeSpan _csvLastPts;
    private double _csvFirstWallMs;

    /// <summary>Rows written to the current/last timestamp CSV (== frames encoded into the MP4).</summary>
    public long CsvRowCount => Interlocked.Read(ref _csvRowIndex);

    /// <summary>Starts the wall-clock reference. Optionally opens a CSV file for this session.</summary>
    public void Start(double expectedFps = 30.0)
    {
        _frameCount       = 0;
        _lastPts          = TimeSpan.Zero;
        _expectedIntervalMs = expectedFps > 0 ? 1000.0 / expectedFps : 33.33;
        _wallClock.Restart();
    }

    /// <summary>Stops monitoring and closes any open CSV writer.</summary>
    public void Stop()
    {
        _wallClock.Stop();
        CloseCsv();
    }

    /// <summary>
    /// Async equivalent of <see cref="Stop"/> — awaits the background CSV writer's drain
    /// instead of blocking the calling thread with <c>Task.Wait</c>. Use this from the
    /// recording-stop path (already inside an async method, called from a UI event handler)
    /// so a slow-to-drain writer never stalls the UI thread. <see cref="Stop"/> remains for
    /// <see cref="Dispose"/>, which cannot await.
    /// </summary>
    public async Task StopAsync()
    {
        _wallClock.Stop();
        await CloseCsvAsync();
    }

    /// <summary>
    /// Opens a CSV file at <paramref name="csvPath"/> and begins writing per-frame rows.
    /// Call after <see cref="Start"/> and before the first <see cref="RecordFrame"/> call.
    /// </summary>
    public void OpenCsv(string csvPath)
    {
        CloseCsv();
        // Resume the clock if Stop() paused it between recordings on the same pipeline instance.
        if (!_wallClock.IsRunning) _wallClock.Start();
        _csvStartOffsetMs = _wallClock.Elapsed.TotalMilliseconds;
        Interlocked.Exchange(ref _csvRowIndex, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath) ?? ".");
        _csvWriter = new StreamWriter(csvPath, append: false, encoding: Encoding.UTF8);
        _csvWriter.WriteLine(
            "frameIndex,captureTimestampUtc,appTimestampMsFromRecStart," +
            "appTimestampSecondsFromRecStart,monotonicTicks," +
            "frameIntervalMs,estimatedCaptureFps,droppedFrameWarning,capturePtsMsFromFirstFrame,capturePtsMsSystemClock");
        _csvOpen = true;

        // Unbounded: the writer task always keeps up in practice (row formatting + a file write
        // per frame is far cheaper than the ~33ms frame interval it's draining), so back-pressure
        // isn't a concern — an unbounded channel avoids ever blocking the producer (frame-arrived
        // thread) on a full queue, which would reintroduce the exact stall this design avoids.
        var queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _csvQueue = queue;
        _csvWriterTask = Task.Run(() => DrainCsvQueueAsync(queue.Reader, _csvWriter));
    }

    /// <summary>
    /// Records one frame arrival and performs anomaly detection. Runs on the camera's
    /// frame-arrived callback thread — must stay allocation-light and I/O-free (see remarks
    /// on the class). The formatted CSV row is handed off to a background writer task.
    /// </summary>
    public CameraFrameTimestampInfo RecordFrame(V2FrameArrivedEventArgs frame)
    {
        var wallElapsed = _wallClock.Elapsed;
        var ptsDelta    = _frameCount > 0 ? (TimeSpan?)(frame.PresentationTimestamp - _lastPts) : null;
        _lastPts        = frame.PresentationTimestamp;
        var index       = Interlocked.Increment(ref _frameCount) - 1;

        double intervalMs   = ptsDelta?.TotalMilliseconds ?? 0;
        bool hasAnomaly     = ptsDelta.HasValue && ptsDelta.Value.TotalMilliseconds > _expectedIntervalMs * 2.0;
        double estFps       = wallElapsed.TotalSeconds > 0 ? index / wallElapsed.TotalSeconds : 0;

        var info = new CameraFrameTimestampInfo
        {
            FrameIndex            = index,
            PresentationTimestamp = frame.PresentationTimestamp,
            WallClockTimestamp    = frame.WallClockTimestamp,
            WallClockElapsed      = wallElapsed,
            PtsDelta              = ptsDelta,
            HasTimingAnomaly      = hasAnomaly,
            AnomalyDescription    = hasAnomaly
                ? $"Frame gap {intervalMs:F1} ms (expected ≤{_expectedIntervalMs * 2:F1} ms)"
                : null,
        };

        // CSV rows are no longer written here (preview-inclusive) — see AppendRecordedFrame.
        TimestampSampled?.Invoke(this, info);
        if (hasAnomaly) AnomalyDetected?.Invoke(this, info);

        return info;
    }

    /// <summary>
    /// Appends one CSV row for a frame that was actually written to the MP4. Call from the
    /// frame-arrived thread only, after the encoder accepted the frame, in delivery order.
    /// Columns: frameIndex = 0-based MP4 frame number; appTimestampMs/SecondsFromRecStart = app
    /// arrival time relative to the first recorded frame; frameIntervalMs = capture-timestamp
    /// delta to the previous recorded frame; estimatedCaptureFps = rows / recording time so far;
    /// capturePtsMsFromFirstFrame = the exact capture timestamp used as the MP4 sample time.
    /// </summary>
    public void AppendRecordedFrame(CameraFrameTimestampInfo info)
    {
        var queue = _csvQueue;
        if (!_csvOpen || queue is null) return;

        var index = Interlocked.Increment(ref _csvRowIndex) - 1;
        var wallMs = info.WallClockElapsed.TotalMilliseconds;
        if (index == 0)
        {
            _csvFirstPts = info.PresentationTimestamp;
            _csvFirstWallMs = wallMs;
            _csvLastPts = info.PresentationTimestamp;
        }

        double recRelMs   = wallMs - _csvFirstWallMs;
        double recRelSec  = recRelMs / 1000.0;
        double intervalMs = index == 0 ? 0 : (info.PresentationTimestamp - _csvLastPts).TotalMilliseconds;
        _csvLastPts = info.PresentationTimestamp;
        bool anomaly      = index > 0 && intervalMs > _expectedIntervalMs * 2.0;
        double estFps     = recRelSec > 0 ? index / recRelSec : 0;
        double ptsRelMs   = (info.PresentationTimestamp - _csvFirstPts).TotalMilliseconds;
        long   ticks      = Stopwatch.GetTimestamp();

        // Formatting is pure string work (no I/O); TryWrite on an unbounded channel never blocks.
        queue.Writer.TryWrite(string.Join(",",
            index,
            info.WallClockTimestamp.ToString("O", CultureInfo.InvariantCulture),
            recRelMs.ToString("F3", CultureInfo.InvariantCulture),
            recRelSec.ToString("F6", CultureInfo.InvariantCulture),
            ticks,
            intervalMs.ToString("F3", CultureInfo.InvariantCulture),
            estFps.ToString("F2", CultureInfo.InvariantCulture),
            anomaly ? "1" : "0",
            ptsRelMs.ToString("F3", CultureInfo.InvariantCulture),
            // 2.1.0: absolute capture time on the system (QPC) clock that Windows uses for every camera
            // (MediaFrameReference.SystemRelativeTime) — subtract between cameras for exact alignment.
            info.PresentationTimestamp.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Runs on a dedicated background task — the only place that touches <paramref name="writer"/>
    /// after <see cref="OpenCsv"/>. Drains rows as they arrive and flushes periodically so a crash
    /// still loses at most a small window of rows, matching the previous inline-flush guarantee
    /// without the frame-arrived thread ever waiting on disk I/O itself.
    /// </summary>
    private static async Task DrainCsvQueueAsync(ChannelReader<string> reader, StreamWriter writer)
    {
        var sinceFlush = 0;
        try
        {
            await foreach (var line in reader.ReadAllAsync().ConfigureAwait(false))
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                if (++sinceFlush >= 10) // ~333 ms at 30 fps — same cadence as the previous inline flush
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    sinceFlush = 0;
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort: a CSV write failure must never take down frame capture/recording.
            AppDiagnosticLogger.Runtime($"V2_TIMESTAMP_CSV_WRITER_ERROR {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { await writer.FlushAsync().ConfigureAwait(false); } catch { }
        }
    }

    private void CloseCsv()
    {
        if (!_csvOpen) return;
        _csvOpen = false;

        // Stop accepting new rows, then give the background writer a bounded window to drain
        // whatever's left (frame delivery has already stopped by the time Stop()/CloseCsv() is
        // called, so the queue is typically near-empty — this is not on the frame hot path).
        _csvQueue?.Writer.TryComplete();
        try { _csvWriterTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        _csvQueue = null;
        _csvWriterTask = null;

        try { _csvWriter?.Flush(); _csvWriter?.Dispose(); } catch { /* best effort */ }
        _csvWriter = null;
    }

    /// <summary>Async equivalent of <see cref="CloseCsv"/> — see <see cref="StopAsync"/>.</summary>
    private async Task CloseCsvAsync()
    {
        if (!_csvOpen) return;
        _csvOpen = false;

        _csvQueue?.Writer.TryComplete();
        if (_csvWriterTask is not null)
        {
            try { await _csvWriterTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        }
        _csvQueue = null;
        _csvWriterTask = null;

        try { _csvWriter?.Flush(); _csvWriter?.Dispose(); } catch { /* best effort */ }
        _csvWriter = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wallClock.Stop();
        CloseCsv();
    }
}

/// <summary>Per-frame timestamp analysis record.</summary>
public sealed class CameraFrameTimestampInfo
{
    public long FrameIndex { get; init; }
    /// <summary>Media Foundation presentation timestamp (QPC-based monotonic).</summary>
    public TimeSpan PresentationTimestamp { get; init; }
    /// <summary>UTC wall-clock time at which the frame event was raised.</summary>
    public DateTimeOffset WallClockTimestamp { get; init; }
    /// <summary>Elapsed time since session start at frame arrival.</summary>
    public TimeSpan WallClockElapsed { get; init; }
    /// <summary>Delta between this frame's PTS and the previous frame's PTS. Null for frame 0.</summary>
    public TimeSpan? PtsDelta { get; init; }
    /// <summary>True if the PTS delta indicates a timing gap or fast-playback risk.</summary>
    public bool HasTimingAnomaly { get; init; }
    public string? AnomalyDescription { get; init; }
}
