using MultiCamApp.Capture.VideoEngineV2;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// Regression coverage for the v1.2.43 fix: requesting a non-30fps rate at a resolution the
/// camera doesn't support at that exact rate previously fell straight into the resolution
/// ladder, which tries 1080p first — silently "upgrading" any low-resolution + non-30fps
/// request to 1920x1080. Confirmed via real recordings (test25-28, test32-36 in a user test
/// batch): requesting 480p/720p at 24/60 fps consistently landed on 1920x1080 instead.
/// </summary>
public sealed class CameraFormatSelectorV2Tests
{
    // Mimics a cheap UVC webcam (e.g. j5 Webcam JVU250): standard resolutions, 30fps only.
    private static readonly V2CaptureFormat[] Standard30FpsOnlyDevice =
    [
        new() { Width = 1920, Height = 1080, NominalFps = 30, PixelFormat = V2PixelFormat.Mjpeg },
        new() { Width = 1280, Height =  720, NominalFps = 30, PixelFormat = V2PixelFormat.Mjpeg },
        new() { Width =  640, Height =  480, NominalFps = 30, PixelFormat = V2PixelFormat.Mjpeg },
    ];

    [Theory]
    [InlineData(640, 480, 60)]   // 480p @ 60fps not available
    [InlineData(640, 480, 24)]   // 480p @ 24fps not available
    [InlineData(1280, 720, 60)]  // 720p @ 60fps not available
    [InlineData(1280, 720, 24)]  // 720p @ 24fps not available
    public void Select_keeps_requested_resolution_when_only_fps_is_unavailable(
        int width, int height, double fps)
    {
        var selector = new CameraFormatSelectorV2();
        var request = new V2CaptureFormatRequest
        {
            PreferredWidth = width,
            PreferredHeight = height,
            PreferredFps = fps,
            PreferredPixelFormat = V2PixelFormat.Mjpeg,
        };

        var result = selector.Select(Standard30FpsOnlyDevice, request);

        Assert.NotNull(result.SelectedFormat);
        Assert.Equal(width, result.SelectedFormat!.Width);
        Assert.Equal(height, result.SelectedFormat.Height);
        Assert.Equal(30, result.SelectedFormat.NominalFps);
        Assert.Equal(V2FormatSelectionKind.PriorityFallback, result.Kind);
    }

    [Fact]
    public void Select_returns_exact_match_when_available()
    {
        var selector = new CameraFormatSelectorV2();
        var request = new V2CaptureFormatRequest
        {
            PreferredWidth = 1280,
            PreferredHeight = 720,
            PreferredFps = 30,
            PreferredPixelFormat = V2PixelFormat.Mjpeg,
        };

        var result = selector.Select(Standard30FpsOnlyDevice, request);

        Assert.Equal(V2FormatSelectionKind.ExactMatch, result.Kind);
        Assert.Equal(1280, result.SelectedFormat!.Width);
        Assert.Equal(720, result.SelectedFormat.Height);
    }

    [Fact]
    public void Select_falls_through_full_ladder_when_requested_resolution_unavailable_at_any_fps()
    {
        // Device has no 480p mode at all — the "keep requested resolution at 30fps" step
        // must not find anything, so the full ladder (which tries 1080p first) is the only
        // remaining option. This is expected, unavoidable behavior, not a bug.
        V2CaptureFormat[] deviceWithNo480p =
        [
            new() { Width = 1920, Height = 1080, NominalFps = 30, PixelFormat = V2PixelFormat.Mjpeg },
            new() { Width = 1280, Height =  720, NominalFps = 30, PixelFormat = V2PixelFormat.Mjpeg },
        ];

        var selector = new CameraFormatSelectorV2();
        var request = new V2CaptureFormatRequest
        {
            PreferredWidth = 640,
            PreferredHeight = 480,
            PreferredFps = 60,
            PreferredPixelFormat = V2PixelFormat.Mjpeg,
        };

        var result = selector.Select(deviceWithNo480p, request);

        Assert.Equal(V2FormatSelectionKind.PriorityFallback, result.Kind);
        Assert.Equal(1920, result.SelectedFormat!.Width);
        Assert.Equal(1080, result.SelectedFormat.Height);
    }
}
