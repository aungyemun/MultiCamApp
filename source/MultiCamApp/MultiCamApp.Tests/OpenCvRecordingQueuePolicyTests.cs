using MultiCamApp.Capture;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class OpenCvRecordingQueuePolicyTests
{
    [Theory]
    [InlineData(640, 480, 30)]
    [InlineData(1280, 720, 60)]
    [InlineData(1920, 1080, 90)]
    public void ResolveRecordQueueCapacity_ScalesWithResolution(int width, int height, int expected)
    {
        Assert.Equal(expected, OpenCvPreviewController.ResolveRecordQueueCapacity(width, height));
    }

    [Theory]
    [InlineData(1280, 720, 20)]
    [InlineData(1920, 1080, 15)]
    public void ResolveRecordingPreviewFpsCap_ReducesPreviewLoadFor1080p(int width, int height, int expected)
    {
        Assert.Equal(expected, OpenCvPreviewController.ResolveRecordingPreviewFpsCap(width, height));
    }

    [Fact]
    public void ResolveRecordingPreviewFpsCap_BelowFullHd_Returns20()
    {
        // 640×480 is below 1280×720 threshold — should use DefaultRecordingPreviewFpsCap (20).
        Assert.Equal(20, OpenCvPreviewController.ResolveRecordingPreviewFpsCap(640, 480));
    }
}
