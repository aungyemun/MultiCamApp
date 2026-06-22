using MultiCamApp.Metadata;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class FrameTimestampTrimmingHelperTests
{
    [Fact]
    public void GetFrameIndexForElapsedTime_UsesCaptureMonotonicSeconds()
    {
        var samples = new[]
        {
            new FrameTimestampSample(0, 1000.000),
            new FrameTimestampSample(1, 1000.034),
            new FrameTimestampSample(2, 1000.067),
            new FrameTimestampSample(3, 1000.101)
        };

        var frameIndex = FrameTimestampTrimmingHelper.GetFrameIndexForElapsedTime(samples, 0.050);

        Assert.Equal(2, frameIndex);
    }

    [Fact]
    public void GetFrameIndexForElapsedTime_ReadsFrameTimestampCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_frame_timestamps.csv");
        try
        {
            File.WriteAllLines(path, new[]
            {
                "frameIndex,captureUtcTime,captureLocalTime,captureMonotonicSec,writeMonotonicSec",
                "0,2026-06-20T00:00:00Z,2026-06-20T09:00:00,50.000,50.001",
                "1,2026-06-20T00:00:00Z,2026-06-20T09:00:00,50.033,50.034",
                "2,2026-06-20T00:00:00Z,2026-06-20T09:00:00,50.066,50.067"
            });

            var frameIndex = FrameTimestampTrimmingHelper.GetFrameIndexForElapsedTime(path, 0.040);

            Assert.Equal(2, frameIndex);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void GetFrameIndexForElapsedTime_ReturnsExpectedFramesForThirtyAndSixHundredThirtySeconds()
    {
        var samples = Enumerable.Range(0, 18_901)
            .Select(i => new FrameTimestampSample(i, 1000.0 + i / 30.0))
            .ToArray();

        Assert.Equal(900, FrameTimestampTrimmingHelper.GetFrameIndexForElapsedTime(samples, 30));
        Assert.Equal(18_900, FrameTimestampTrimmingHelper.GetFrameIndexForElapsedTime(samples, 630));
    }
}
