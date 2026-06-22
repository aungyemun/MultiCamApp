using MultiCamApp.Capture;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class DuplicateUsbCapturePolicyTests
{
    private static readonly CameraDevice J5A = new()
    {
        Id = "j5-a",
        Name = "j5 Webcam JVU250",
        DisplayName = "j5 Webcam JVU250",
        Kind = CameraKind.ExternalUsb,
        EnumerationIndex = 1
    };

    private static readonly CameraDevice J5B = new()
    {
        Id = "j5-b",
        Name = "j5 Webcam JVU250",
        DisplayName = "j5 Webcam JVU250",
        Kind = CameraKind.ExternalUsb,
        EnumerationIndex = 2
    };

    private static readonly CameraDevice UsbLive = new()
    {
        Id = "usb-live",
        Name = "USB Live camera",
        DisplayName = "USB Live camera",
        Kind = CameraKind.ExternalUsb,
        EnumerationIndex = 3
    };

    [Fact]
    public void ShouldFallbackToWinRtAfterOpenCvProbe_ThreeCameraLayout_StaysOnOpenCv()
    {
        var devices = new[] { J5A, J5B, UsbLive };
        var layout = new string?[] { "j5-a", "j5-b", "usb-live" };

        Assert.False(DuplicateUsbCapturePolicy.ShouldFallbackToWinRtAfterOpenCvProbe(
            devices, layout, UsbLive, openCvIndex: 3, probedWidth: 1920, probedHeight: 1080, preferredCaptureWidth: 1920));

        Assert.False(DuplicateUsbCapturePolicy.ShouldFallbackToWinRtAfterOpenCvProbe(
            devices, layout, UsbLive, openCvIndex: 2, probedWidth: 1920, probedHeight: 1080, preferredCaptureWidth: 1920));
    }

    [Fact]
    public void ShouldFallbackToWinRtAfterOpenCvProbe_TwoDuplicateJ5_StaysOnSuccessfulOpenCv()
    {
        var devices = new[] { J5A, J5B };
        var layout = new string?[] { "j5-a", "j5-b", null };

        Assert.False(DuplicateUsbCapturePolicy.ShouldFallbackToWinRtAfterOpenCvProbe(
            devices, layout, J5B, openCvIndex: 3, probedWidth: 1920, probedHeight: 1080, preferredCaptureWidth: 1920));
    }

    [Fact]
    public void ShouldFallbackToWinRtAfterOpenCvProbe_SingleCamera_NeverFallsBack()
    {
        var devices = new[] { J5A };
        var layout = new string?[] { "j5-a", null, null };

        Assert.False(DuplicateUsbCapturePolicy.ShouldFallbackToWinRtAfterOpenCvProbe(
            devices, layout, J5A, openCvIndex: 3, probedWidth: 1920, probedHeight: 1080, preferredCaptureWidth: 1920));
    }
}
