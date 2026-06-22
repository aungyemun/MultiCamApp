////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Diagnostics;
using MultiCamApp.Metadata;

namespace MultiCamApp.Experiment;

/// <summary>High-resolution per-frame timing for experiment recording (QPC-based).</summary>
public sealed class FrameTimingMonitor
{
    private readonly double _targetFps;
    private readonly double _targetIntervalMs;
    private readonly bool _constantFrameCountMode;
    private readonly List<double> _intervalsMs = [];
    private long _startTick;
    private long _lastWriteTick;
    private long _framesWritten;
    private long _framesCaptured;
    private long _droppedFrames;
    private long _duplicateFrames;
    private long _placeholderFrames;
    private long _maxConsecutiveLateFrames;
    private long _maxConsecutiveNoFrame;
    private long _lastExpectedIndex = -1;
    private DateTime _firstWallUtc;
    private DateTime _lastWallUtc;
    private long _lastCaptureTick;
    private readonly List<double> _captureIntervalsMs = [];

    public FrameTimingMonitor(double targetFps, bool constantFrameCountMode)
    {
        _targetFps = targetFps > 0 ? targetFps : 30;
        _targetIntervalMs = 1000.0 / _targetFps;
        _constantFrameCountMode = constantFrameCountMode;
    }

    public long FramesWritten => _framesWritten;
    public long FramesCaptured => _framesCaptured;
    public long DroppedFrames => _droppedFrames;
    public long DuplicateFrames => _duplicateFrames;
    public long PlaceholderFrames => _placeholderFrames;
    public long MaxConsecutiveLateFrames => _maxConsecutiveLateFrames;
    public long MaxConsecutiveNoFrame => _maxConsecutiveNoFrame;

    public void Begin()
    {
        _startTick = Stopwatch.GetTimestamp();
        _lastWriteTick = 0;
        _framesWritten = 0;
        _framesCaptured = 0;
        _droppedFrames = 0;
        _duplicateFrames = 0;
        _placeholderFrames = 0;
        _maxConsecutiveLateFrames = 0;
        _maxConsecutiveNoFrame = 0;
        _lastExpectedIndex = -1;
        _intervalsMs.Clear();
        _captureIntervalsMs.Clear();
        _firstWallUtc = default;
        _lastWallUtc = default;
        _lastCaptureTick = 0;
    }

    public void NotifyFrameCaptured()
    {
        _framesCaptured++;
        var now = Stopwatch.GetTimestamp();
        if (_lastCaptureTick > 0)
        {
            var captureMs = Stopwatch.GetElapsedTime(_lastCaptureTick, now).TotalMilliseconds;
            // Unit tests and some fast loops can observe identical QPC ticks; clamp to a tiny positive value
            // so MinMs/MeanMs remain > 0 when freezing.
            if (captureMs <= 0)
                captureMs = 0.001;
            _captureIntervalsMs.Add(captureMs);
            var missing = captureMs > _targetIntervalMs
                ? (long)Math.Max(0, Math.Floor(captureMs / _targetIntervalMs) - 1)
                : 0;
            if (missing > _maxConsecutiveNoFrame)
                _maxConsecutiveNoFrame = missing;
            if (missing > _maxConsecutiveLateFrames)
                _maxConsecutiveLateFrames = missing;
        }
        _lastCaptureTick = now;
        var elapsedSec = ElapsedSeconds();
        var expectedIndex = (long)Math.Floor(elapsedSec * _targetFps);
        if (_lastExpectedIndex >= 0 && expectedIndex > _lastExpectedIndex + 1 && !_constantFrameCountMode)
            _droppedFrames += expectedIndex - _lastExpectedIndex - 1;
        _lastExpectedIndex = expectedIndex;
    }

    public void NotifyFrameWritten(bool isDuplicate = false, bool isPlaceholder = false)
    {
        var now = Stopwatch.GetTimestamp();
        if (_framesWritten == 0)
        {
            _firstWallUtc = DateTime.UtcNow;
            _lastWriteTick = now;
        }
        else
        {
            var ms = Stopwatch.GetElapsedTime(_lastWriteTick, now).TotalMilliseconds;
            _intervalsMs.Add(ms);
            _lastWriteTick = now;
        }

        _lastWallUtc = DateTime.UtcNow;
        _framesWritten++;
        if (isDuplicate) _duplicateFrames++;
        if (isPlaceholder) _placeholderFrames++;
        else if (isDuplicate) { }
    }

    public void NotifyDuplicateWrite() => NotifyFrameWritten(isDuplicate: true);

    public double ElapsedSeconds()
    {
        if (_startTick == 0) return 0;
        return Stopwatch.GetElapsedTime(_startTick).TotalSeconds;
    }

    /// <summary>Copy capture interval stats before monitor reset/dispose.</summary>
    public CaptureTimingSnapshot FreezeCaptureTiming() =>
        CaptureTimingSnapshot.FromIntervals(
            _framesCaptured,
            _captureIntervalsMs,
            _maxConsecutiveLateFrames,
            _maxConsecutiveNoFrame);

    public FrameTimingSummary BuildSummary(long expectedFrames, double targetDurationSeconds)
    {
        var duration = ElapsedSeconds();
        var meanFps = duration > 0.05 ? _framesWritten / duration : 0;
        var fpsDrift = Math.Abs(meanFps - _targetFps);

        double minMs = 0, maxMs = 0, avgMs = _targetIntervalMs, stdMs = 0;
        if (_intervalsMs.Count > 0)
        {
            minMs = _intervalsMs.Min();
            maxMs = _intervalsMs.Max();
            avgMs = _intervalsMs.Average();
            var variance = _intervalsMs.Sum(x => (x - avgMs) * (x - avgMs)) / _intervalsMs.Count;
            stdMs = Math.Sqrt(variance);
        }

        double capMinMs = 0, capMaxMs = 0, capAvgMs = _targetIntervalMs, capStdMs = 0;
        if (_captureIntervalsMs.Count > 0)
        {
            capMinMs = _captureIntervalsMs.Min();
            capMaxMs = _captureIntervalsMs.Max();
            capAvgMs = _captureIntervalsMs.Average();
            var capVar = _captureIntervalsMs.Sum(x => (x - capAvgMs) * (x - capAvgMs)) / _captureIntervalsMs.Count;
            capStdMs = Math.Sqrt(capVar);
        }

        return new FrameTimingSummary
        {
            TargetFps = _targetFps,
            TargetDurationSeconds = targetDurationSeconds,
            ExpectedFrames = expectedFrames,
            ActualFramesWritten = _framesWritten,
            FramesCaptured = _framesCaptured,
            DroppedFrames = _droppedFrames,
            DuplicateFrames = _duplicateFrames,
            PlaceholderFrames = _placeholderFrames,
            DurationSeconds = duration,
            MeanFps = meanFps,
            FpsDrift = fpsDrift,
            MinFrameIntervalMs = minMs,
            MaxFrameIntervalMs = maxMs,
            AverageFrameIntervalMs = avgMs,
            FrameIntervalStdDevMs = stdMs,
            MinCaptureIntervalMs = capMinMs,
            MaxCaptureIntervalMs = capMaxMs,
            AverageCaptureIntervalMs = capAvgMs,
            CaptureJitterMs = capStdMs,
            CaptureIntervalCount = _captureIntervalsMs.Count,
            MaxConsecutiveLateFrames = _maxConsecutiveLateFrames,
            MaxConsecutiveNoFrame = _maxConsecutiveNoFrame,
            FirstFrameUtc = _firstWallUtc,
            LastFrameUtc = _lastWallUtc,
            ConstantFrameCountMode = _constantFrameCountMode
        };
    }
}

public sealed class FrameTimingSummary
{
    public double TargetFps { get; init; }
    public double TargetDurationSeconds { get; init; }
    public long ExpectedFrames { get; init; }
    public long ActualFramesWritten { get; init; }
    public long FramesCaptured { get; init; }
    public long DroppedFrames { get; init; }
    public long DuplicateFrames { get; init; }
    public long PlaceholderFrames { get; init; }
    public double DurationSeconds { get; init; }
    public double MeanFps { get; init; }
    public double FpsDrift { get; init; }
    public double MinFrameIntervalMs { get; init; }
    public double MaxFrameIntervalMs { get; init; }
    public double AverageFrameIntervalMs { get; init; }
    public double FrameIntervalStdDevMs { get; init; }
    public double MinCaptureIntervalMs { get; init; }
    public double MaxCaptureIntervalMs { get; init; }
    public double AverageCaptureIntervalMs { get; init; }
    public double CaptureJitterMs { get; init; }
    public long CaptureIntervalCount { get; init; }
    public long MaxConsecutiveLateFrames { get; init; }
    public long MaxConsecutiveNoFrame { get; init; }
    public DateTime FirstFrameUtc { get; init; }
    public DateTime LastFrameUtc { get; init; }
    public bool ConstantFrameCountMode { get; init; }
}
