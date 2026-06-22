using MultiCamApp.Capture;
using MultiCamApp.Utils;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class PreviewSlotFailureHelperTests
{
    private static Dictionary<string, string> Lang => new()
    {
        ["previewSlotFailedAtPreset"] = "{0} could not open at {1} / {2} fps.",
        ["previewSlotTryLowerPreset1080"] = "Try 720p or 360p.",
        ["previewSlotTryLowerPreset720"] = "Try 360p.",
        ["previewSlotTryLowerPreset360"] = "Check camera connection or choose another camera.",
        ["previewSlotTryLowerPresetGeneric"] = "Check camera connection or selected preset.",
        ["previewSlotOpenedInstead"] = "Opened"
    };

    [Fact]
    public void BuildOverlayMessage_1080p_IncludesLowerPresetAdvice()
    {
        string L(string k) => Lang[k];
        var msg = PreviewSlotFailureHelper.BuildOverlayMessage(
            L, "cam2", CaptureResolutionPreset.Width1080, CaptureResolutionPreset.Height1080, 30,
            PreviewSlotFailureCategory.UnsupportedPreset);

        Assert.Contains("cam2 could not open at 1080p / 30 fps.", msg);
        Assert.Contains("Try 720p or 360p.", msg);
    }

    [Fact]
    public void BuildOverlayMessage_720p_Suggests360p()
    {
        string L(string k) => Lang[k];
        var msg = PreviewSlotFailureHelper.BuildOverlayMessage(
            L, "cam2", CaptureResolutionPreset.Width720, CaptureResolutionPreset.Height720, 30,
            PreviewSlotFailureCategory.DeviceOpen);

        Assert.Contains("720p", msg);
        Assert.Contains("Try 360p.", msg);
    }

    [Fact]
    public void ToSlotState_UnsupportedPreset_MapsCorrectly()
    {
        Assert.Equal(PreviewSlotStateKind.FailedUnsupportedPreset,
            PreviewSlotFailureHelper.ToSlotState(PreviewSlotFailureCategory.UnsupportedPreset));
        Assert.Equal(PreviewSlotStateKind.FailedDeviceOpen,
            PreviewSlotFailureHelper.ToSlotState(PreviewSlotFailureCategory.DeviceOpen));
    }
}
