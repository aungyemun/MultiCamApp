using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Ui;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// Regressions found by real two-camera recordings (v2.1.0): CSV/frame alignment (#8),
/// UVC exposure units (#9), per-resolution bitrate (#10), two-camera layout.
/// </summary>
public sealed class RecordingAccuracyRegressionTests
{
    [Fact]
    public async Task TimestampCsv_HasOneRowPerEncodedFrame_IndexedFromZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "multicam_csv_" + Guid.NewGuid().ToString("N"));
        var csv = Path.Combine(dir, "cam1_timestamps.csv");
        using var monitor = new FrameTimestampMonitor();
        monitor.Start(30);
        try
        {
            var t0 = TimeSpan.FromSeconds(100);
            // 10 preview frames before recording (must not appear in the CSV)
            for (var i = 0; i < 10; i++)
                monitor.RecordFrame(Frame(t0 + TimeSpan.FromMilliseconds(33.3 * i)));
            monitor.OpenCsv(csv);
            // 5 frames while the encoder is still opening (delivered, not encoded) — no CSV rows
            for (var i = 10; i < 15; i++)
                monitor.RecordFrame(Frame(t0 + TimeSpan.FromMilliseconds(33.3 * i)));
            // 30 encoded frames
            for (var i = 15; i < 45; i++)
            {
                var info = monitor.RecordFrame(Frame(t0 + TimeSpan.FromMilliseconds(33.3 * i)));
                monitor.AppendRecordedFrame(info);
            }
            Assert.Equal(30, monitor.CsvRowCount);
            await monitor.StopAsync();

            var lines = File.ReadAllLines(csv);
            Assert.Equal(31, lines.Length); // header + 30
            Assert.EndsWith("capturePtsMsFromFirstFrame,capturePtsMsSystemClock", lines[0]);
            var first = lines[1].Split(',');
            var last = lines[^1].Split(',');
            Assert.Equal("0", first[0]);
            Assert.Equal("29", last[0]);
            Assert.Equal("0.000", first[5]);                         // no interval for frame 0
            Assert.Equal("0.000", first[8]);                         // PTS relative to first recorded frame
            Assert.Equal(29 * 33.3, double.Parse(last[8], System.Globalization.CultureInfo.InvariantCulture), 1);
            // 2.1.0: absolute capture time (shared system clock) for cross-camera alignment.
            var sys0 = double.Parse(first[9], System.Globalization.CultureInfo.InvariantCulture);
            var sysN = double.Parse(last[9], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(29 * 33.3, sysN - sys0, 1);
            Assert.Equal(double.Parse(last[8], System.Globalization.CultureInfo.InvariantCulture), sysN - sys0, 3);
            Assert.All(lines.Skip(1), l => Assert.Equal(10, l.Split(',').Length));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static V2FrameArrivedEventArgs Frame(TimeSpan pts) => new()
    {
        PresentationTimestamp = pts,
        WallClockTimestamp = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData(-6, 1.0 / 64)]
    [InlineData(-5, 1.0 / 32)]
    [InlineData(-1, 0.5)]
    public void UvcExposure_IsLog2Seconds(double raw, double seconds)
    {
        Assert.Equal(seconds, CameraControlManagerV2.UvcExposureToSeconds(raw), 9);
        Assert.Equal(raw, CameraControlManagerV2.SecondsToUvcExposure(seconds));
    }

    // Manual focus: UVC controls reject values off the step grid (C922 focus: 0..250 step 5).
    [Theory]
    [InlineData(122, 0, 250, 5, 120)]
    [InlineData(123, 0, 250, 5, 125)]
    [InlineData(400, 0, 250, 5, 250)]
    [InlineData(-3, 0, 250, 5, 0)]
    [InlineData(37.4, 0, 1023, 1, 37)]
    public void FocusValue_SnapsToControlStep(double v, double min, double max, double step, double expected)
        => Assert.Equal(expected, CameraControlManagerV2.SnapToStep(v, min, max, step));

    // 2.1.0 video quality selector: size estimate per camera-minute (720p30 measured: 7.9 Mbit/s ≈ 59 MB/min).
    [Theory]
    [InlineData(V2BitrateProfile.WindowsCameraLike, 1280, 720, 30, 60.0)]
    [InlineData(V2BitrateProfile.Standard,          1280, 720, 30, 25.0)]
    [InlineData(V2BitrateProfile.WindowsCameraLike, 1920, 1080, 60, 202.5)]  // capped at 1.5x base
    [InlineData(V2BitrateProfile.Standard,          640, 480, 15, 15.0)]     // floored at 2 Mbit/s
    public void QualityPreset_FileSizeEstimate(V2BitrateProfile p, int w, int h, double fps, double mbPerMin)
        => Assert.Equal(mbPerMin, VideoEngineSettings.MegabytesPerMinute(p, w, h, fps), 1);

    // Exception #13: UI exposure slider (0-255) -> UVC log2-second step, on the real C922 range (-11..-2).
    [Theory]
    [InlineData(0, -11)]
    [InlineData(142, -6)]   // 1/64 s: short enough for 60 fps (16.7 ms frame interval)
    [InlineData(255, -2)]   // 1/4 s
    [InlineData(-10, -11)]  // clamped
    [InlineData(400, -2)]   // clamped
    public void ExposureSlider_MapsOntoUvcRange(double slider, double expectedLog2)
    {
        Assert.Equal(expectedLog2, CameraControlManagerV2.SliderToUvcExposureLog2(slider, -11, -2));
    }

    [Theory]
    [InlineData(1920, 1080, 30, 18000)]
    [InlineData(1280, 720, 30, 8000)]
    [InlineData(640, 480, 30, 2667)]
    [InlineData(640, 360, 30, 2000)]
    [InlineData(1920, 1080, 60, 27000)]
    public void Bitrate_ScalesWithPixelRate(int w, int h, double fps, int expectedKbps)
    {
        var saved = VideoEngineSettings.TargetBitrateKbps;
        try
        {
            VideoEngineSettings.TargetBitrateKbps = 18000;
            Assert.Equal(expectedKbps, VideoEngineSettings.ResolveTargetBitrateKbps(w, h, fps));
        }
        finally { VideoEngineSettings.TargetBitrateKbps = saved; }
    }

    [Theory]
    [InlineData(1060, 720, true)]   // default window preview area: stacking shows more video
    [InlineData(1900, 500, false)]  // very wide: side by side
    [InlineData(0, 0, false)]
    public void TwoCameras_StackWhenItShowsMoreVideo(double w, double h, bool stack)
    {
        Assert.Equal(stack, ResponsiveLayoutManager.ShouldStackTwo(w, h, 16.0 / 9.0));
    }
}
