using MultiCamApp.Experiment;
using MultiCamApp.Metadata;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class CaptureTimingSnapshotTests
{
    [Fact]
    public void FromIntervals_ReturnsUnavailable_WhenFewerThanTwoCaptures()
    {
        var snap = CaptureTimingSnapshot.FromIntervals(1, [], 0, 0);

        Assert.False(snap.IsAvailable);
        Assert.Equal(CaptureTimingSnapshot.UnavailableMessage, snap.AvailabilityMessage);
        Assert.Equal(0, snap.CaptureIntervalCount);
    }

    [Fact]
    public void FreezeCaptureTiming_PreservesIntervals_BeforeMonitorReset()
    {
        var monitor = new FrameTimingMonitor(30, constantFrameCountMode: false);
        monitor.Begin();

        for (var i = 0; i < 50; i++)
            monitor.NotifyFrameCaptured();

        var snap = monitor.FreezeCaptureTiming();

        Assert.True(snap.IsAvailable);
        Assert.Equal(49, snap.CaptureIntervalCount);
        Assert.Equal(50, snap.FramesCaptured);
        Assert.True(snap.MinMs > 0);
        Assert.True(snap.MaxMs >= snap.MinMs);
        Assert.True(snap.MeanMs > 0);
    }

    [Fact]
    public void FromIntervals_ComputesExpectedStats_ForTypical29FpsDelivery()
    {
        var intervals = Enumerable.Repeat(34.4, 100).Select((v, i) => v + (i % 3) - 1).ToList();
        var snap = CaptureTimingSnapshot.FromIntervals(101, intervals, 0, 0);

        Assert.True(snap.IsAvailable);
        Assert.Equal(100, snap.CaptureIntervalCount);
        Assert.InRange(snap.MeanMs, 33, 36);
        Assert.True(snap.MinMs > 0);
        Assert.True(snap.MaxMs > snap.MinMs);
        Assert.True(snap.StdMs > 0);
    }

    [Fact]
    public void Formatter_ShowsUnavailable_WhenIntervalCountIsZero()
    {
        Assert.Equal("Unavailable", CaptureIntervalMetadataFormatter.FormatMs(0, 0, CaptureTimingSnapshot.UnavailableMessage));
        Assert.Equal("34.400", CaptureIntervalMetadataFormatter.FormatMs(34.4, 100, ""));
    }
}
