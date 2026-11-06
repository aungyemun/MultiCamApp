using MultiCamApp.Capture.VideoEngineV2;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// STABLE_CORE_V2 exception #14: camera-mode ranking must work for cameras other than the ones
/// tested on hardware. Format lists below mimic real driver patterns (subtype order varies by vendor).
/// </summary>
public class CameraModeRankingTests
{
    private static (int, int, double, string?) F(int w, int h, double fps, string sub) => (w, h, fps, sub);

    private static (int, int, double, string?) First(IReadOnlyList<(int, int, double, string?)> list, int w, int h, double fps)
        => list[MediaFoundationCaptureService.RankFormats(list, w, h, fps)[0]];

    [Fact]
    public void MjpegListedFirst_UncompressedMirrorIsChosen()
    {
        // Many webcams list MJPG before the frame-server NV12 mirror; the CPU reader got no frames from MJPG.
        var list = new[] { F(1280, 720, 30, "MJPG"), F(1280, 720, 30, "NV12"), F(1920, 1080, 30, "MJPG"), F(1920, 1080, 30, "NV12") };
        Assert.Equal(F(1280, 720, 30, "NV12"), First(list, 1280, 720, 30));
    }

    [Fact]
    public void C922Pattern_Nv12BeforeMjpgAtSameMode()
    {
        var list = new[] { F(1280, 720, 30, "NV12"), F(1280, 720, 30, "MJPG"), F(1280, 720, 60, "NV12"), F(1280, 720, 60, "MJPG"), F(1920, 1080, 5, "YUY2") };
        Assert.Equal(F(1280, 720, 60, "NV12"), First(list, 1280, 720, 60));
    }

    [Fact]
    public void Yuy2OnlyCamera_IsUsed()
    {
        var list = new[] { F(640, 480, 30, "YUY2"), F(1280, 720, 10, "YUY2") };
        Assert.Equal(F(1280, 720, 10, "YUY2"), First(list, 1280, 720, 30));   // keeps resolution, lower fps
    }

    [Fact]
    public void NtscRate_29_97_MatchesThirtyRequest()
    {
        var list = new[] { F(1280, 720, 29.97, "NV12"), F(1280, 720, 15, "NV12") };
        Assert.Equal(F(1280, 720, 29.97, "NV12"), First(list, 1280, 720, 30));
    }

    [Fact]
    public void ExactMjpgOnlyMode_TriedFirst_ThenUncompressedFallbacksInOrder()
    {
        // 1080p30 only as MJPG; uncompressed 1080p only at 5 fps. The watchdog falls back in this order.
        var list = new[] { F(1920, 1080, 30, "MJPG"), F(1920, 1080, 5, "YUY2"), F(1280, 720, 30, "YUY2"), F(640, 480, 30, "NV12") };
        var order = MediaFoundationCaptureService.RankFormats(list, 1920, 1080, 30).Select(i => list[i]).ToList();
        Assert.Equal(F(1920, 1080, 30, "MJPG"), order[0]);
        Assert.Equal(F(1920, 1080, 5, "YUY2"), order[1]);
        Assert.Equal(F(1280, 720, 30, "YUY2"), order[2]);
    }

    [Fact]
    public void UnusualResolutionCamera_ClosestModeChosen()
    {
        var list = new[] { F(800, 600, 30, "YUY2"), F(320, 240, 30, "YUY2") };
        Assert.Equal(F(800, 600, 30, "YUY2"), First(list, 1280, 720, 30));
    }

    [Theory]
    [InlineData("NV12", 0)]
    [InlineData("yuy2", 1)]
    [InlineData("RGB32", 2)]
    [InlineData("MJPG", 8)]
    [InlineData("H264", 9)]
    [InlineData("Y800", 5)]
    [InlineData(null, 5)]
    public void SubtypeRank_PrefersUncompressed(string? subtype, int rank)
        => Assert.Equal(rank, MediaFoundationCaptureService.SubtypeRank(subtype));
}
