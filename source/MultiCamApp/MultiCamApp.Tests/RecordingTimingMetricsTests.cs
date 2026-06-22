using MultiCamApp.Metadata;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class RecordingTimingMetricsTests
{
    [Fact]
    public void ComputeMeasuredCameraFps_UsesCurrentSessionValues()
    {
        var sessionOne = RecordingTimingMetrics.ComputeMeasuredCameraFps(48730, 1676.46);
        var sessionTwo = RecordingTimingMetrics.ComputeMeasuredCameraFps(29984, 1031.60);

        Assert.True(sessionOne > 29 && sessionOne < 30);
        Assert.True(sessionTwo > 29 && sessionTwo < 30);
        Assert.NotEqual(sessionOne, sessionTwo);
    }

    [Fact]
    public void ComputeContainerVsWallDifference_IsInformational_NotIntegrityFailure()
    {
        var diff = RecordingTimingMetrics.ComputeContainerVsWallClockDifference(1624.33, 1676.46);
        Assert.True(diff < -50);
        Assert.True(diff > -60);
    }

    [Fact]
    public void ComputeEffectivePlaybackFps_MatchesWriterTagForConstantContainer()
    {
        var fps = RecordingTimingMetrics.ComputeEffectivePlaybackFps(48730, 48730 / 30.0);
        Assert.InRange(fps, 29.99, 30.01);
    }

    [Fact]
    public void InterCameraOffsets_AreComputedFromMonotonicTimestamps()
    {
        var starts = new[] { 1000.0, 1000.05 };
        var stops = new[] { 2676.0, 2676.04 };
        var startMs = RecordingTimingMetrics.ComputeInterCameraStartOffsetMs(starts);
        var stopMs = RecordingTimingMetrics.ComputeInterCameraStopOffsetMs(stops);

        Assert.Equal(50, startMs, 1);
        Assert.Equal(40, stopMs, 1);
        Assert.True(startMs <= 100);
        Assert.True(stopMs <= 100);
    }

    [Fact]
    public void AnalyzeCaptureMonotonicSeconds_ComputesThirtyFpsIntervals()
    {
        var timestamps = BuildConstantFpsTimestamps(30.0, frameCount: 301);

        var analysis = RecordingTimingMetrics.AnalyzeCaptureMonotonicSeconds(timestamps, requestedFps: 30);

        Assert.Equal(300, analysis.IntervalCount);
        Assert.Equal(33.333, analysis.MeanMs, 3);
        Assert.Equal(33.333, analysis.MedianMs, 3);
        Assert.Equal(33.333, analysis.ExpectedIntervalMs, 3);
        Assert.Equal(30.0, analysis.MeasuredCameraFpsFromMeanInterval, 3);
    }

    [Fact]
    public void AnalyzeCaptureMonotonicSeconds_ComputesStableLowerNativeFpsIntervals()
    {
        var timestamps = BuildConstantFpsTimestamps(29.684, frameCount: 301);

        var analysis = RecordingTimingMetrics.AnalyzeCaptureMonotonicSeconds(timestamps, requestedFps: 30);

        Assert.Equal(33.688, analysis.MeanMs, 3);
        Assert.Equal(33.688, analysis.MedianMs, 3);
        Assert.Equal(29.684, analysis.MeasuredCameraFpsFromMeanInterval, 3);
        Assert.Equal(33.333, analysis.RequestedExpectedIntervalMs, 3);
    }

    [Fact]
    public void AnalyzeCaptureMonotonicSeconds_CountsLongAndSevereGaps()
    {
        var intervalsMs = Enumerable.Repeat(33.333, 120)
            .Concat([100.0])
            .ToArray();
        var timestamps = BuildTimestampsFromIntervalsMs(intervalsMs);

        var analysis = RecordingTimingMetrics.AnalyzeCaptureMonotonicSeconds(timestamps, requestedFps: 30);

        Assert.Equal(1, analysis.LongGapCount);
        Assert.Equal(1, analysis.SevereLongGapCount);
        Assert.Equal("Unstable", analysis.FpsStabilityGrade);
    }

    [Fact]
    public void AnalyzeCaptureMonotonicSeconds_CalculatesP95AndP99()
    {
        var timestamps = BuildTimestampsFromIntervalsMs([10, 20, 30, 40, 50]);

        var analysis = RecordingTimingMetrics.AnalyzeCaptureMonotonicSeconds(timestamps, requestedFps: 30);

        Assert.Equal(48.0, analysis.P95Ms, 3);
        Assert.Equal(49.6, analysis.P99Ms, 3);
    }

    private static IReadOnlyList<double> BuildConstantFpsTimestamps(double fps, int frameCount)
    {
        var intervalSec = 1.0 / fps;
        return Enumerable.Range(0, frameCount)
            .Select(i => 1000.0 + i * intervalSec)
            .ToArray();
    }

    private static IReadOnlyList<double> BuildTimestampsFromIntervalsMs(IReadOnlyList<double> intervalsMs)
    {
        var timestamps = new List<double> { 1000.0 };
        foreach (var intervalMs in intervalsMs)
            timestamps.Add(timestamps[^1] + intervalMs / 1000.0);
        return timestamps;
    }
}
