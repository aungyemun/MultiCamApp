using MultiCamApp.Capture.VideoEngineV2;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class EncoderCadencePolicyTests
{
    [Fact]
    public void ResolveTargetFps_UsesStableMeasuredCadenceNearNominal()
    {
        var result = EncoderCadencePolicy.ResolveTargetFps(
            30.0, 29.6842, measuredFrames: 300, measuredDuration: TimeSpan.FromSeconds(10));

        Assert.Equal(29.684, result, 3);
    }

    [Fact]
    public void ResolveTargetFps_UsesNominalWhenPreviewSampleIsTooShort()
    {
        var result = EncoderCadencePolicy.ResolveTargetFps(
            30.0, 29.68, measuredFrames: 20, measuredDuration: TimeSpan.FromSeconds(1));

        Assert.Equal(30.0, result);
    }

    [Fact]
    public void ResolveTargetFps_DoesNotConvertToASeparateCameraMode()
    {
        var result = EncoderCadencePolicy.ResolveTargetFps(
            30.0, 20.0, measuredFrames: 300, measuredDuration: TimeSpan.FromSeconds(15));

        Assert.Equal(30.0, result);
    }

    [Fact]
    public void ResearchDefaults_DisableAutoExposure()
    {
        Assert.True(VideoEngineSettings.DisableAutoExposure);
    }
}
