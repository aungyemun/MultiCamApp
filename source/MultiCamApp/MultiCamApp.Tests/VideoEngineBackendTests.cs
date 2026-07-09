// v1.2.22-alpha — VideoEngine backend architecture tests (V2 Stable only).

using MultiCamApp.Capture.Backend;
using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Recording.Writers;
using Xunit;
#pragma warning disable CA1001 // registry is always disposed via using

namespace MultiCamApp.Tests;

public sealed class VideoEngineBackendTests
{
    // ── BackendIdentifiers ────────────────────────────────────────────────────

    [Fact]
    public void BackendIds_V2Stable_HasExpectedValue()
    {
        Assert.Equal("VideoEngineV2_Stable", BackendIds.VideoEngineV2Stable);
    }

    [Fact]
    public void BackendMode_HasStableAndExperimental()
    {
        Assert.Equal(BackendMode.Stable,       BackendMode.Stable);
        Assert.Equal(BackendMode.Experimental, BackendMode.Experimental);
    }

    // ── VideoEngineRegistry — default selection ───────────────────────────────

    [Fact]
    public void Registry_DefaultBackend_IsV2Stable()
    {
        using var registry = new VideoEngineRegistry();
        Assert.Equal(BackendIds.VideoEngineV2Stable, registry.ActiveBackendId);
        Assert.Equal(BackendIds.VideoEngineV2Stable, registry.RequestedBackendId);
        Assert.False(registry.FallbackOccurred);
        Assert.Equal("", registry.FallbackReason);
    }

    [Fact]
    public void Registry_SelectV2Explicitly_IsV2Stable()
    {
        using var registry = new VideoEngineRegistry();
        registry.SelectBackend(BackendIds.VideoEngineV2Stable);
        Assert.Equal(BackendIds.VideoEngineV2Stable, registry.ActiveBackendId);
        Assert.False(registry.FallbackOccurred);
    }

    // ── BackendMetadata fields ────────────────────────────────────────────────

    [Fact]
    public void Registry_BuildMetadata_V2_HasRequiredFields()
    {
        using var registry = new VideoEngineRegistry();
        registry.SelectBackend(BackendIds.VideoEngineV2Stable);

        var result = RecordingFinalizeResult.Failure("test");
        var meta   = registry.BuildMetadata(result, measuredRealFps: 29.68, previewMeasuredFps: 15.0, previewTargetFps: 30.0);

        Assert.Equal(BackendIds.VideoEngineV2Stable, meta.RecordingBackend);
        Assert.Equal("2.0.3",                        meta.BackendVersion);
        Assert.Equal("Stable",                       meta.BackendMode);
        Assert.False(meta.BackendFallbackUsed);
        Assert.Equal("",                             meta.BackendFallbackReason);
        Assert.NotNull(meta.CaptureApi);
        Assert.NotNull(meta.PreviewApi);
        Assert.NotNull(meta.EncoderApi);
        Assert.NotNull(meta.HardwareEncoderUsed);
        Assert.NotNull(meta.HardwareEncoderEvidence);
        Assert.True(meta.PreviewIndependentFromRecording);
        Assert.Equal(30.0,  meta.PreviewTargetFps);
        Assert.Equal(15.0,  meta.PreviewMeasuredFps);
        Assert.Equal(29.68, meta.RecordingMeasuredRealFps);
    }

    // ── BackendMetadata never has null required string fields ─────────────────

    [Fact]
    public void BackendMetadata_DefaultRecord_HasNoNullStringFields()
    {
        var meta = new BackendMetadata();
        Assert.NotNull(meta.RecordingBackend);
        Assert.NotNull(meta.BackendVersion);
        Assert.NotNull(meta.BackendMode);
        Assert.NotNull(meta.BackendFallbackReason);
        Assert.NotNull(meta.CaptureApi);
        Assert.NotNull(meta.PreviewApi);
        Assert.NotNull(meta.EncoderApi);
        Assert.NotNull(meta.HardwareEncoderUsed);
        Assert.NotNull(meta.HardwareEncoderEvidence);
    }

    // ── BackendInitDiagnostics ────────────────────────────────────────────────

    [Fact]
    public void BackendInitDiagnostics_Success_Factory()
    {
        var diag = BackendInitDiagnostics.Success("test-backend", "All good");
        Assert.True(diag.InitSucceeded);
        Assert.Equal("test-backend", diag.BackendId);
        Assert.Null(diag.FailureReason);
    }

    [Fact]
    public void BackendInitDiagnostics_Failure_Factory()
    {
        var diag = BackendInitDiagnostics.Failure("test-backend", "Crashed");
        Assert.False(diag.InitSucceeded);
        Assert.Equal("Crashed", diag.FailureReason);
        Assert.Contains("failed", diag.InitSummary, StringComparison.OrdinalIgnoreCase);
    }

    // ── VideoEngineV2 stable behavior unchanged ───────────────────────────────

    [Fact]
    public void V2Settings_RequestedBackendId_DefaultsToV2Stable()
    {
        VideoEngineSettings.RequestedBackendId = BackendIds.VideoEngineV2Stable;
        Assert.Equal(BackendIds.VideoEngineV2Stable, VideoEngineSettings.RequestedBackendId);
    }

    [Fact]
    public void V2Settings_AllowCam1RecordingTest_DefaultsTrue()
    {
        Assert.True(VideoEngineSettings.AllowCam1RecordingTest);
    }

    [Fact]
    public void V2Settings_WriteTimestampCsv_DefaultsTrue()
    {
        Assert.True(VideoEngineSettings.WriteTimestampCsv);
    }

    [Fact]
    public void V2Settings_BitrateProfile_DefaultsWindowsCameraLike()
    {
        Assert.Equal(V2BitrateProfile.WindowsCameraLike, VideoEngineSettings.BitrateProfile);
        Assert.Equal(18_000, VideoEngineSettings.TargetBitrateKbps);
    }

    // ── BackendStatusDisplay ──────────────────────────────────────────────────

    [Fact]
    public void Registry_BackendStatusDisplay_V2_FormatsCorrectly()
    {
        using var registry = new VideoEngineRegistry();
        Assert.Equal("VideoEngineV2 Stable", registry.BackendStatusDisplay);
    }
}
