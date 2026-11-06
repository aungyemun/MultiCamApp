using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class DeepVerifyServiceTests
{
    // 2.1.0: without passthrough ffmpeg resamples VFR recordings to CFR before hashing,
    // which drops or repeats frames and makes the duplicate count unreliable.
    [Fact]
    public void FrameMd5_hashes_every_decoded_frame_without_rate_conversion()
    {
        var args = DeepVerifyService.BuildFrameMd5Arguments(@"cam1\cam1.mp4");
        Assert.Contains("-fps_mode passthrough", args);
        Assert.Contains("-f framemd5", args);
        Assert.Contains("\"cam1\\cam1.mp4\"", args);
        Assert.True(args.IndexOf("-fps_mode", System.StringComparison.Ordinal) < args.IndexOf("-f framemd5", System.StringComparison.Ordinal));
    }
}
