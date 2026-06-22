using MultiCamApp.Capture;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class CaptureResolutionHelperTests
{
    [Theory]
    [InlineData(0, 1920, 1080, 0)]
    [InlineData(1, 1920, 1080, 0)]
    [InlineData(2, 1920, 1080, 350)]
    [InlineData(3, 1920, 1080, 500)]
    public void LateSlotExtraOpenDelayMs_Cam3AndCam4_GetExtraDelay(int slotIndex, int w, int h, int expectedMs)
    {
        Assert.Equal(expectedMs, CaptureResolutionHelper.LateSlotExtraOpenDelayMs(slotIndex, w, h));
    }

    [Theory]
    [InlineData(1, 1920, 1080, 0)]
    [InlineData(2, 1920, 1080, 0)]
    [InlineData(3, 1920, 1080, 300)]
    [InlineData(4, 1920, 1080, 400)]
    public void RecordingStartStaggerMs_OnlyAppliesForThreeOrMoreCameras(
        int activeCount, int w, int h, int expectedMs)
    {
        Assert.Equal(expectedMs, CaptureResolutionHelper.RecordingStartStaggerMs(activeCount, w, h));
    }
}
