using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// STABLE_CORE_V2 exception #5: a file ffprobe could not read must be distinguishable from
/// "ffprobe not installed", so verdict reconciliation never clears a real unreadable-file FAIL.
/// </summary>
public sealed class ProbeFailureGuardTests
{
    [Fact]
    public void FileUnreadable_True_WhenProbeRanAndFailed()
    {
        var probe = new VideoProbeData { Success = false, Error = "moov atom not found" };
        Assert.True(probe.FileUnreadable);
    }

    [Fact]
    public void FileUnreadable_False_WhenToolMissing()
    {
        var probe = new VideoProbeData { Success = false, ToolUnavailable = true };
        Assert.False(probe.FileUnreadable);
    }

    [Fact]
    public void FileUnreadable_False_WhenProbeSucceeded()
    {
        Assert.False(new VideoProbeData { Success = true }.FileUnreadable);
    }

    [Fact]
    public void ProbeTimeout_IsBounded()
    {
        Assert.InRange(VideoProbeService.ProbeTimeout, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
    }
}
