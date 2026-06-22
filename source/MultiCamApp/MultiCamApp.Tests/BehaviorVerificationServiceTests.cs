using MultiCamApp.Core;
using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class BehaviorVerificationServiceTests
{
    private static readonly AppConfig Config = new();

    [Fact]
    public void VerifySession_PassesForTypicalTwoCameraSession()
    {
        var svc = new BehaviorVerificationService();
        var videos = new[]
        {
            BuildVideo("cam1", frameCount: 20930, durationSec: 697.67, wallSec: 720.08, interCameraFrameDiff: 1),
            BuildVideo("cam2", frameCount: 20931, durationSec: 697.70, wallSec: 720.14, interCameraFrameDiff: 1),
        };

        var result = svc.VerifySession(@"C:\sessions\test1", videos, Config);

        Assert.Equal(VerificationVerdict.Pass, result.FinalVerdict);
        Assert.Equal(1, result.CameraFrameCountDifference);
        Assert.True(result.CameraDurationDifferenceSec < 1.0);
    }

    [Fact]
    public void VerifySession_FailsWhenUnrelatedSessionsAreCombined()
    {
        var svc = new BehaviorVerificationService();
        var videos = new[]
        {
            BuildVideo("cam1", frameCount: 19319, durationSec: 643.97, wallSec: 664.62, interCameraFrameDiff: 1, includeMetadata: false),
            BuildVideo("cam2", frameCount: 21750, durationSec: 725.00, wallSec: 748.28, interCameraFrameDiff: 0, includeMetadata: false),
        };

        var result = svc.VerifySession(@"C:\sessions\mixed", videos, Config);

        Assert.Equal(VerificationVerdict.Fail, result.FinalVerdict);
        Assert.True(result.CameraFrameCountDifference > 100);
        Assert.True(result.CameraDurationDifferenceSec > 50);
    }

    private static VideoVerificationResult BuildVideo(
        string slot,
        long frameCount,
        double durationSec,
        double wallSec,
        long interCameraFrameDiff,
        bool includeMetadata = true)
    {
        return new VideoVerificationResult
        {
            Entry = new VideoFileEntry { CameraSlot = slot },
            Probe = new VideoProbeData
            {
                Success = true,
                Fps = 30,
                DurationSeconds = durationSec,
                FrameCount = frameCount,
                Width = 1920,
                Height = 1080,
                HasVideoStream = true,
            },
            Expected = new ExpectedCameraSettings { Fps = 30, Width = 1920, Height = 1080 },
            ResolutionMatch = VerificationMatchStatus.Yes,
            WallDurationSeconds = wallSec,
            Metadata = includeMetadata
                ? new CameraMetadataRecord
                {
                    InterCameraFrameDiff = interCameraFrameDiff,
                    WallClockDurationSeconds = wallSec,
                }
                : null,
        };
    }
}
