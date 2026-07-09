using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class VerificationCaptureProfileTests
{
    [Theory]
    [InlineData("360p", 640, 480)]
    [InlineData("720p", 1280, 720)]
    [InlineData("1080p", 1920, 1080)]
    [InlineData("640x480", 640, 480)]
    [InlineData("1280x720", 1280, 720)]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("1920 × 1080", 1920, 1080)]
    [InlineData("0x0", 0, 0)]
    public void TryParseResolution_parses_ui_presets(string text, int w, int h)
    {
        var ok = VerificationCaptureProfile.TryParseResolution(text, out var width, out var height);
        if (w == 0)
        {
            Assert.False(ok);
            return;
        }

        Assert.True(ok);
        Assert.Equal(w, width);
        Assert.Equal(h, height);
        Assert.True(VerificationCaptureProfile.IsKnownResolution(width, height));
    }

    [Theory]
    [InlineData("1920x1080", "1080p")]
    [InlineData("1280x720", "720p")]
    [InlineData("640x480", "480p")] // relabelled from "360p" in v1.2.40 — 640x480 is VGA/480p (4:3), not true 360p (640x360, 16:9)
    [InlineData("1080p", "1080p")]
    [InlineData(null, "-")]
    public void FormatDisplayLabel_returns_preset_labels(string? text, string expected)
    {
        Assert.Equal(expected, CaptureResolutionPreset.FormatDisplayLabel(text));
    }

    [Theory]
    [InlineData(14.8, 15)]
    [InlineData(23.9, 24)]
    [InlineData(29.7, 30)]
    [InlineData(59.8, 60)]
    public void NormalizeFps_snaps_to_ui_values(double input, double expected) =>
        Assert.Equal(expected, VerificationCaptureProfile.NormalizeFps(input));

    [Theory]
    [InlineData(15, 1.5, 3.0)]
    [InlineData(30, 1.5, 3.0)]
    [InlineData(60, 3.0, 6.0)]
    public void GetFpsTolerances_scales_with_requested_fps(double requested, double warn, double fail)
    {
        var settings = new VerificationSettings();
        var tolerances = VerificationCaptureProfile.GetFpsTolerances(requested, settings);
        Assert.Equal(warn, tolerances.Warning, 2);
        Assert.Equal(fail, tolerances.Fail, 2);
    }
}

public sealed class ExpectedSettingsResolverTests
{
    [Fact]
    public void Resolve_prefers_user_requested_resolution_and_fps_from_metadata_txt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mc-resolver-" + Guid.NewGuid().ToString("N"));
        var camDir = Path.Combine(dir, "cam1");
        Directory.CreateDirectory(camDir);

        File.WriteAllLines(Path.Combine(camDir, "metadata.txt"),
        [
            "Camera slot: cam1",
            "Requested resolution: 1280x720",
            "Selected resolution: 1280x720",
            "Pixels: 1280x720",
            "Requested FPS: 24.00",
            "Selected device FPS: 23.976",
            "Recording writer FPS: 24.000",
            "Frames written: 1200",
            "Duration seconds: 50.0"
        ]);

        var entry = new VideoFileEntry
        {
            CameraSlot = "cam1",
            SessionFolder = dir,
            FullPath = Path.Combine(camDir, "clip.mp4"),
            MetadataPath = Path.Combine(camDir, "metadata.txt"),
            MetadataJsonPath = Path.Combine(camDir, "metadata.json")
        };

        var (bySlot, source) = new ExpectedSettingsResolver().Resolve(dir, [entry], null);

        Assert.Contains("metadata", source);
        Assert.True(bySlot.TryGetValue("cam1", out var expected));
        Assert.Equal(1280, expected.Width);
        Assert.Equal(720, expected.Height);
        Assert.Equal(24, expected.Fps);
    }

    [Fact]
    public void Resolve_prefers_requested_1080p60_from_metadata_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mc-resolver-" + Guid.NewGuid().ToString("N"));
        var camDir = Path.Combine(dir, "cam2");
        Directory.CreateDirectory(camDir);

        var jsonPath = Path.Combine(camDir, "metadata.json");
        File.WriteAllText(jsonPath,
            """
            {
              "CameraSlot": "cam2",
              "RequestedResolution": "1920x1080",
              "SelectedResolution": "1920x1080",
              "Width": 1920,
              "Height": 1080,
              "RequestedFps": 60,
              "SelectedDeviceFps": 59.94,
              "RecordingWriterFps": 60,
              "MeasuredCameraFps": 59.8,
              "FramesWritten": 3600,
              "WallClockDurationSeconds": 60.0
            }
            """);

        var entry = new VideoFileEntry
        {
            CameraSlot = "cam2",
            SessionFolder = dir,
            FullPath = Path.Combine(camDir, "clip.mp4"),
            MetadataJsonPath = jsonPath
        };

        var (bySlot, _) = new ExpectedSettingsResolver().Resolve(dir, [entry], null);
        Assert.True(bySlot.TryGetValue("cam2", out var expected));
        Assert.Equal(1920, expected.Width);
        Assert.Equal(1080, expected.Height);
        Assert.Equal(60, expected.Fps);
    }
}
