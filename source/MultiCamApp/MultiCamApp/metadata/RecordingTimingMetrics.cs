////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Metadata;

public sealed record CaptureIntervalAnalysis(
    double MeasuredCameraFpsFromFirstLastFrame,
    double MeasuredCameraFpsFromMeanInterval,
    double MeanMs,
    double MedianMs,
    double StdMs,
    double MinMs,
    double MaxMs,
    double P95Ms,
    double P99Ms,
    double ExpectedIntervalMs,
    double RequestedExpectedIntervalMs,
    double MeanIntervalErrorMs,
    double AbsoluteMeanIntervalErrorMs,
    long LongGapCount,
    long ShortGapCount,
    long SevereLongGapCount,
    double JitterScoreMs,
    string FpsStabilityGrade,
    long IntervalCount);

/// <summary>Per-session timing calculations for recording metadata (no cached cross-session values).</summary>
public static class RecordingTimingMetrics
{
    public static double ComputeMeasuredCameraFps(long framesCaptured, double wallClockDurationSeconds)
    {
        if (wallClockDurationSeconds <= 0.05 || framesCaptured <= 0)
            return 0;
        return framesCaptured / wallClockDurationSeconds;
    }

    public static double ComputeEffectivePlaybackFps(long framesWritten, double frameBasedDurationSeconds)
    {
        if (frameBasedDurationSeconds <= 0.05 || framesWritten <= 0)
            return 0;
        return framesWritten / frameBasedDurationSeconds;
    }

    public static double ComputeFrameBasedDurationSeconds(long framesWritten, double writerFps)
    {
        if (writerFps <= 0 || framesWritten <= 0)
            return 0;
        return framesWritten / writerFps;
    }

    public static double ComputeContainerVsWallClockDifference(double containerDurationSeconds, double wallClockDurationSeconds)
        => containerDurationSeconds - wallClockDurationSeconds;

    public static double ComputeInterCameraStartOffsetMs(IReadOnlyList<double> firstFrameMonotonicSeconds)
    {
        if (firstFrameMonotonicSeconds.Count < 2)
            return 0;
        return (firstFrameMonotonicSeconds.Max() - firstFrameMonotonicSeconds.Min()) * 1000.0;
    }

    public static double ComputeInterCameraStopOffsetMs(IReadOnlyList<double> lastFrameMonotonicSeconds)
    {
        if (lastFrameMonotonicSeconds.Count < 2)
            return 0;
        return (lastFrameMonotonicSeconds.Max() - lastFrameMonotonicSeconds.Min()) * 1000.0;
    }

    public static CaptureIntervalAnalysis AnalyzeCaptureMonotonicSeconds(
        IReadOnlyList<double> captureMonotonicSeconds,
        double requestedFps)
    {
        if (captureMonotonicSeconds.Count < 2)
            return EmptyCaptureIntervalAnalysis(requestedFps);

        var ordered = captureMonotonicSeconds.OrderBy(v => v).ToArray();
        var intervals = new List<double>(ordered.Length - 1);
        for (var i = 1; i < ordered.Length; i++)
        {
            var deltaMs = (ordered[i] - ordered[i - 1]) * 1000.0;
            if (deltaMs > 0)
                intervals.Add(deltaMs);
        }

        if (intervals.Count == 0)
            return EmptyCaptureIntervalAnalysis(requestedFps);

        intervals.Sort();
        var mean = intervals.Average();
        var median = PercentileSorted(intervals, 0.50);
        var min = intervals[0];
        var max = intervals[^1];
        var p95 = PercentileSorted(intervals, 0.95);
        var p99 = PercentileSorted(intervals, 0.99);
        var variance = intervals.Sum(v => Math.Pow(v - mean, 2)) / intervals.Count;
        var std = Math.Sqrt(variance);
        var firstToLastSec = ordered[^1] - ordered[0];
        var measuredFirstLast = firstToLastSec > 0 ? (ordered.Length - 1) / firstToLastSec : 0;
        var measuredMean = mean > 0 ? 1000.0 / mean : 0;
        var stableNativeFps = measuredFirstLast > 0 ? measuredFirstLast : measuredMean;
        var expected = stableNativeFps > 0 ? 1000.0 / stableNativeFps : mean;
        var requestedExpected = requestedFps > 0 ? 1000.0 / requestedFps : 0;
        var meanError = expected > 0 ? mean - expected : 0;
        var absMeanError = Math.Abs(meanError);
        var longGaps = intervals.LongCount(v => expected > 0 && v > expected * 1.75);
        var severeLongGaps = intervals.LongCount(v => expected > 0 && v > expected * 2.5);
        var shortGaps = intervals.LongCount(v => expected > 0 && v < expected * 0.50);
        var jitterScore = Math.Sqrt((std * std + Math.Max(0, p99 - expected) * Math.Max(0, p99 - expected)) / 2.0);
        var grade = ClassifyFpsStability(ordered.Length, intervals.Count, std, p99, expected, longGaps, severeLongGaps, shortGaps);

        return new CaptureIntervalAnalysis(
            measuredFirstLast,
            measuredMean,
            mean,
            median,
            std,
            min,
            max,
            p95,
            p99,
            expected,
            requestedExpected,
            meanError,
            absMeanError,
            longGaps,
            shortGaps,
            severeLongGaps,
            jitterScore,
            grade,
            intervals.Count);
    }

    private static CaptureIntervalAnalysis EmptyCaptureIntervalAnalysis(double requestedFps) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            requestedFps > 0 ? 1000.0 / requestedFps : 0,
            0, 0, 0, 0, 0, 0, "Failed", 0);

    private static double PercentileSorted(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0;
        if (sorted.Count == 1)
            return sorted[0];
        var position = Math.Clamp(percentile, 0, 1) * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        var weight = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }

    private static string ClassifyFpsStability(
        int records,
        int intervals,
        double stdMs,
        double p99Ms,
        double expectedMs,
        long longGaps,
        long severeLongGaps,
        long shortGaps)
    {
        if (records < 2 || intervals < 1 || expectedMs <= 0)
            return "Failed";

        var longGapRate = intervals > 0 ? longGaps / (double)intervals : 1;
        var severeRate = intervals > 0 ? severeLongGaps / (double)intervals : 1;
        var shortRate = intervals > 0 ? shortGaps / (double)intervals : 1;
        var p99Ratio = p99Ms / expectedMs;

        if (severeLongGaps > 0 && severeRate >= 0.001 || longGapRate >= 0.02 || shortRate >= 0.02)
            return "Unstable";
        if (severeLongGaps > 0 || longGapRate >= 0.005 || p99Ratio > 1.75 || stdMs > expectedMs * 0.25)
            return "Borderline";
        if (stdMs <= expectedMs * 0.08 && p99Ratio <= 1.25 && severeLongGaps == 0)
            return "Excellent";
        return "Good";
    }
}
