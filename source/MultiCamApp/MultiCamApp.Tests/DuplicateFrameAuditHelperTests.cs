using MultiCamApp.Metadata;
using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class DuplicateFrameAuditHelperTests
{
    [Fact]
    public void Assess_ReturnsWarning_NotCleanPass_WhenDuplicateCorrectionIsPresent()
    {
        var (status, message) = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            FramesWritten = 18000,
            FramesCaptured = 17800,
            Width = 1920,
            Height = 1080,
            WriterFps = 30,
            ContainerFps = 30,
            MeasuredCameraFps = 29.7,
            DuplicateFrames = 200,
            QueueDrops = 0,
            PlaceholderFrames = 0,
            ConstantFrameCountMode = true
        });

        Assert.Equal(CameraAuditStatus.PassWithWarning, status);
        Assert.NotEqual(CameraAuditStatus.Pass, status);
        Assert.Contains("duplicate", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateFramesPerMinute_CalculatesRate()
    {
        var rate = DuplicateFrameAuditHelper.DuplicateFramesPerMinute(200, 600);

        Assert.Equal(20.0, rate, 1);
    }

    [Fact]
    public void DuplicatePercentage_CalculatesPercentOfWrittenFrames()
    {
        var percentage = DuplicateFrameAuditHelper.DuplicatePercentage(200, 18000);

        Assert.Equal(1.1, percentage, 1);
    }

    [Fact]
    public void ClassifyCameraStability_UsesScientificDuplicateThresholds()
    {
        var cam2 = new DuplicateFrameCameraAudit("cam2", 18000, 1, 0, 0, 600, 30, 30.001, "1920x1080");
        var cam1 = new DuplicateFrameCameraAudit("cam1", 18000, 200, 0, 0, 600, 30, 29.68, "1920x1080");

        Assert.Equal("Excellent", DuplicateFrameAuditHelper.ClassifyCameraStability(cam2));
        Assert.Equal("Warning", DuplicateFrameAuditHelper.ClassifyCameraStability(cam1));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void ClassifyCameraStability_ReturnsPoor_ForQueueDropsOrPlaceholders(long queueDrops, long placeholders)
    {
        var camera = new DuplicateFrameCameraAudit("cam1", 18000, 0, queueDrops, placeholders, 600, 30, 30, "1920x1080");

        Assert.Equal("Poor", DuplicateFrameAuditHelper.ClassifyCameraStability(camera));
    }

    [Fact]
    public void BuildSessionSummary_ReturnsWarningMessage_ForDuplicateCorrectedAlignedSession()
    {
        var summary = DuplicateFrameAuditHelper.BuildSessionSummary("test-session",
        [
            new DuplicateFrameCameraAudit("cam1", 18000, 200, 0, 0, 600, 30, 29.68, "1920x1080"),
            new DuplicateFrameCameraAudit("cam2", 18000, 1, 0, 0, 600, 30, 30.001, "1920x1080")
        ]);

        Assert.Equal(CameraAuditStatus.PassWithWarning, summary.ScientificStatus);
        Assert.Equal(200, summary.MaxDuplicateFrames);
        Assert.Equal(201, summary.TotalDuplicateFrames);
        Assert.Equal("cam2", summary.BestCamera);
        Assert.Equal("cam1, cam2", summary.CamerasNeedingDuplicateFrameCorrection);
        Assert.Equal(DuplicateFrameAuditHelper.DuplicateCorrectedSessionMessage, summary.Message);
    }

    [Fact]
    public void BuildRecommendedPreset_ExplainsWarningWithoutHidingCorrection()
    {
        var recommendation = DuplicateFrameAuditHelper.BuildRecommendedPreset(
            new DuplicateFrameCameraAudit("cam1", 18000, 200, 0, 0, 600, 30, 29.68, "1920x1080"));

        Assert.Contains("Valid and aligned", recommendation);
        Assert.Contains("duplicate-frame reporting", recommendation);
        Assert.Contains("720p", recommendation);
    }
}
