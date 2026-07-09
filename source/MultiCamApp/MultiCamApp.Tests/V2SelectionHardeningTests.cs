// v1.2.19-alpha — unit tests for resolution/FPS/backend/GPU/autofocus selection hardening.
//
// All tests cover pure-logic models in V2SelectionHardeningModels.cs and the
// corresponding BackendMetadata default field values.
// No WPF, camera device, or WinRT dependency.

using MultiCamApp.Capture.Backend;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class V2SelectionHardeningTests
{
    // ── ResolutionPresets ─────────────────────────────────────────────────────

    [Fact]
    public void ResolutionPreset_1080p_MapsTo1920x1080()
    {
        var p = ResolutionPresets.ForLabel("1080p");
        Assert.NotNull(p);
        Assert.Equal(1920, p!.Width);
        Assert.Equal(1080, p.Height);
    }

    [Fact]
    public void ResolutionPreset_720p_MapsTo1280x720()
    {
        var p = ResolutionPresets.ForLabel("720p");
        Assert.NotNull(p);
        Assert.Equal(1280, p!.Width);
        Assert.Equal(720, p.Height);
    }

    [Fact]
    public void ResolutionPreset_360p_MapsTo640x360()
    {
        var p = ResolutionPresets.ForLabel("360p");
        Assert.NotNull(p);
        Assert.Equal(640, p!.Width);
        Assert.Equal(360, p.Height);
    }

    [Fact]
    public void ResolutionPreset_UnknownLabel_ReturnsNull()
    {
        Assert.Null(ResolutionPresets.ForLabel("4K"));
        Assert.Null(ResolutionPresets.ForLabel(""));
        Assert.Null(ResolutionPresets.ForLabel("480p"));
    }

    [Fact]
    public void ResolutionPreset_IsKnownPreset_KnownLabels()
    {
        Assert.True(ResolutionPresets.IsKnownPreset("1080p"));
        Assert.True(ResolutionPresets.IsKnownPreset("720p"));
        Assert.True(ResolutionPresets.IsKnownPreset("360p"));
    }

    [Fact]
    public void ResolutionPreset_IsKnownPreset_UnknownLabel_ReturnsFalse()
    {
        Assert.False(ResolutionPresets.IsKnownPreset("4K"));
        Assert.False(ResolutionPresets.IsKnownPreset(""));
    }

    [Fact]
    public void ResolutionPreset_ForDimensions_1920x1080_Returns1080p()
    {
        var p = ResolutionPresets.ForDimensions(1920, 1080);
        Assert.NotNull(p);
        Assert.Equal("1080p", p!.Label);
    }

    [Fact]
    public void ResolutionPreset_ForDimensions_UnknownDims_ReturnsNull()
    {
        Assert.Null(ResolutionPresets.ForDimensions(800, 600));
        Assert.Null(ResolutionPresets.ForDimensions(0, 0));
    }

    // ── ResolutionSelectionPolicy ─────────────────────────────────────────────

    [Fact]
    public void ResolutionSelection_ExactMatch_StatusIsExact()
    {
        var r = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1920, 1080);
        Assert.Equal("Exact", r.ResolutionSelectionStatus);
        Assert.False(r.ResolutionFallbackUsed);
        Assert.Empty(r.ResolutionFallbackReason);
    }

    [Fact]
    public void ResolutionSelection_Mismatch_StatusIsFallback()
    {
        var r = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1280, 720);
        Assert.Equal("Fallback", r.ResolutionSelectionStatus);
        Assert.True(r.ResolutionFallbackUsed);
        Assert.NotEmpty(r.ResolutionFallbackReason);
    }

    [Fact]
    public void ResolutionSelection_Mismatch_FallbackReasonContainsDimensions()
    {
        var r = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1280, 720);
        Assert.Contains("1280", r.ResolutionFallbackReason);
        Assert.Contains("720", r.ResolutionFallbackReason);
    }

    [Fact]
    public void ResolutionSelection_ExplicitSelectionReason_IsPreserved()
    {
        var r = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1280, 720,
            "DeviceDoesNotSupport1080p");
        Assert.Equal("DeviceDoesNotSupport1080p", r.ResolutionFallbackReason);
    }

    [Fact]
    public void ResolutionSelection_ZeroSelectedDims_StatusIsUnavailable()
    {
        var r = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 0, 0);
        Assert.Equal("Unavailable", r.ResolutionSelectionStatus);
        Assert.False(r.ResolutionFallbackUsed);
    }

    [Fact]
    public void ResolutionSelection_PreservesDimensions()
    {
        var r = ResolutionSelectionPolicy.Evaluate("720p", 1280, 720, 1920, 1080);
        Assert.Equal(1280, r.RequestedWidth);
        Assert.Equal(720, r.RequestedHeight);
        Assert.Equal(1920, r.SelectedWidth);
        Assert.Equal(1080, r.SelectedHeight);
        Assert.Equal("720p", r.RequestedResolutionPreset);
    }

    // ── FpsSelectionPolicy ────────────────────────────────────────────────────

    [Fact]
    public void FpsSelection_ExactMatch_StatusIsExact()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 30.0);
        Assert.Equal("Exact", r.FpsSelectionStatus);
        Assert.False(r.FpsFallbackUsed);
        Assert.Empty(r.FpsFallbackReason);
    }

    [Fact]
    public void FpsSelection_WithinTolerance_IsExact()
    {
        // 30.000 vs 30.010 is within ±0.02 fps tolerance
        var r = FpsSelectionPolicy.Evaluate(30.0, 30.01);
        Assert.Equal("Exact", r.FpsSelectionStatus);
        Assert.False(r.FpsFallbackUsed);
    }

    [Fact]
    public void FpsSelection_OutsideTolerance_IsFallback()
    {
        // 30 requested, 29.97 selected (NTSC driver) — outside 0.02 tolerance
        var r = FpsSelectionPolicy.Evaluate(30.0, 29.97);
        Assert.Equal("Fallback", r.FpsSelectionStatus);
        Assert.True(r.FpsFallbackUsed);
        Assert.NotEmpty(r.FpsFallbackReason);
    }

    [Fact]
    public void FpsSelection_FallbackReason_ContainsSelectedFps()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 25.0);
        Assert.Contains("25", r.FpsFallbackReason);
    }

    [Fact]
    public void FpsSelection_ExplicitSelectionReason_IsPreserved()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 25.0, selectionReason: "DeviceMaxFps25");
        Assert.Equal("DeviceMaxFps25", r.FpsFallbackReason);
    }

    [Fact]
    public void FpsSelection_DriverVfrDetected_IsPropagated()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 60.0, driverVfrDetected: true);
        Assert.True(r.DriverVfrDetected);
    }

    [Fact]
    public void FpsSelection_DriverVfrNotDetected_DefaultFalse()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 30.0);
        Assert.False(r.DriverVfrDetected);
    }

    [Fact]
    public void FpsSelection_ZeroFps_StatusIsUnavailable()
    {
        var r = FpsSelectionPolicy.Evaluate(0, 0);
        Assert.Equal("Unavailable", r.FpsSelectionStatus);
    }

    [Fact]
    public void FpsSelection_WriterFps_IsPropagated()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 30.0, writerFps: 30.0);
        Assert.Equal(30.0, r.WriterFps);
    }

    [Fact]
    public void FpsSelection_MeasuredCameraFps_IsPropagated()
    {
        var r = FpsSelectionPolicy.Evaluate(30.0, 30.0, measuredCameraFps: 29.68);
        Assert.Equal(29.68, r.MeasuredCameraFps, 2);
    }

    // ── AutofocusPolicyReport ─────────────────────────────────────────────────

    [Fact]
    public void Autofocus_NotSupported_PolicyResultIsNotSupported()
    {
        var r = AutofocusPolicyReport.NotSupported();
        Assert.Equal("NotSupported", r.AutofocusPolicyResult);
        Assert.Equal("NotSupported", r.AutofocusControlSupported);
        Assert.False(r.AutofocusOffAttempted);
        Assert.False(r.AutofocusOffSucceeded);
    }

    [Fact]
    public void Autofocus_ManualExposureUiAvailable_AlwaysFalse()
    {
        // ManualExposureUiAvailable must always be false (no slider in UI).
        Assert.False(AutofocusPolicyReport.NotSupported().ManualExposureUiAvailable);
        Assert.False(AutofocusPolicyReport.Unknown().ManualExposureUiAvailable);
        Assert.False(AutofocusPolicyReport.FromAttempt(true, true).ManualExposureUiAvailable);
    }

    [Fact]
    public void Autofocus_ManualFocusUiAvailable_AlwaysFalse()
    {
        // ManualFocusUiAvailable must always be false (no slider in UI).
        Assert.False(AutofocusPolicyReport.NotSupported().ManualFocusUiAvailable);
        Assert.False(AutofocusPolicyReport.Unknown().ManualFocusUiAvailable);
        Assert.False(AutofocusPolicyReport.FromAttempt(false, false).ManualFocusUiAvailable);
    }

    [Fact]
    public void Autofocus_FromAttempt_Succeeded_PolicyIsOffConfirmed()
    {
        var r = AutofocusPolicyReport.FromAttempt(attempted: true, succeeded: true);
        Assert.Equal("OffConfirmed", r.AutofocusPolicyResult);
        Assert.True(r.AutofocusOffAttempted);
        Assert.True(r.AutofocusOffSucceeded);
    }

    [Fact]
    public void Autofocus_FromAttempt_Failed_PolicyIsOffFailed()
    {
        var r = AutofocusPolicyReport.FromAttempt(attempted: true, succeeded: false);
        Assert.Equal("OffFailed", r.AutofocusPolicyResult);
        Assert.True(r.AutofocusOffAttempted);
        Assert.False(r.AutofocusOffSucceeded);
    }

    [Fact]
    public void Autofocus_FromAttempt_NotAttempted_PolicyIsNotAttempted()
    {
        var r = AutofocusPolicyReport.FromAttempt(attempted: false, succeeded: false);
        Assert.Equal("NotAttempted", r.AutofocusPolicyResult);
    }

    [Fact]
    public void Autofocus_ExposureControlSupported_IsPropagated()
    {
        var r = AutofocusPolicyReport.NotSupported(exposureControlSupported: "Supported");
        Assert.Equal("Supported", r.ExposureControlSupported);
    }

    // ── BackendMetadata new field defaults ────────────────────────────────────

    [Fact]
    public void BackendMetadata_RequestedBackend_DefaultIsUnknown()
    {
        var m = new BackendMetadata();
        Assert.Equal("Unknown", m.RequestedBackend);
    }

    [Fact]
    public void BackendMetadata_ResolutionSelectionStatus_DefaultIsUnavailable()
    {
        var m = new BackendMetadata();
        Assert.Equal("Unavailable", m.ResolutionSelectionStatus);
    }

    [Fact]
    public void BackendMetadata_FpsSelectionStatus_DefaultIsUnavailable()
    {
        var m = new BackendMetadata();
        Assert.Equal("Unavailable", m.FpsSelectionStatus);
    }

    [Fact]
    public void BackendMetadata_GpuAccelerationAvailable_DefaultIsUnknown()
    {
        var m = new BackendMetadata();
        Assert.Equal("Unknown", m.GpuAccelerationAvailable);
    }

    [Fact]
    public void BackendMetadata_ManualExposureUiAvailable_DefaultFalse()
    {
        var m = new BackendMetadata();
        Assert.False(m.ManualExposureUiAvailable);
    }

    [Fact]
    public void BackendMetadata_ManualFocusUiAvailable_DefaultFalse()
    {
        var m = new BackendMetadata();
        Assert.False(m.ManualFocusUiAvailable);
    }

    [Fact]
    public void BackendMetadata_AutofocusPolicyResult_DefaultIsUnknown()
    {
        var m = new BackendMetadata();
        Assert.Equal("Unknown", m.AutofocusPolicyResult);
    }

    // ── RecordingSelectionContext ─────────────────────────────────────────────

    [Fact]
    public void RecordingSelectionContext_From_PropagatesResolution()
    {
        var res = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1920, 1080);
        var fps = FpsSelectionPolicy.Evaluate(30.0, 30.0);
        var af  = AutofocusPolicyReport.NotSupported();
        var ctx = RecordingSelectionContext.From(res, fps, af,
            requestedBackend: "VideoEngineV2_Stable");

        Assert.Equal("1080p", ctx.RequestedResolutionPreset);
        Assert.Equal(1920, ctx.RequestedWidth);
        Assert.Equal(1080, ctx.RequestedHeight);
        Assert.Equal("Exact", ctx.ResolutionSelectionStatus);
        Assert.False(ctx.ResolutionFallbackUsed);
    }

    [Fact]
    public void RecordingSelectionContext_From_PropagatesFps()
    {
        var res = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1920, 1080);
        var fps = FpsSelectionPolicy.Evaluate(30.0, 29.97);
        var af  = AutofocusPolicyReport.Unknown();
        var ctx = RecordingSelectionContext.From(res, fps, af);

        Assert.Equal(30.0, ctx.RequestedFps);
        Assert.Equal(29.97, ctx.SelectedFps, 2);
        Assert.Equal("Fallback", ctx.FpsSelectionStatus);
        Assert.True(ctx.FpsFallbackUsed);
    }

    [Fact]
    public void RecordingSelectionContext_From_PropagatesAutofocus()
    {
        var res = ResolutionSelectionPolicy.Evaluate("720p", 1280, 720, 1280, 720);
        var fps = FpsSelectionPolicy.Evaluate(30.0, 30.0);
        var af  = AutofocusPolicyReport.FromAttempt(attempted: true, succeeded: false);
        var ctx = RecordingSelectionContext.From(res, fps, af);

        Assert.True(ctx.AutofocusOffAttempted);
        Assert.False(ctx.AutofocusOffSucceeded);
        Assert.Equal("OffFailed", ctx.AutofocusPolicyResult);
        // ManualExposureUiAvailable / ManualFocusUiAvailable live on BackendMetadata (always false),
        // not on RecordingSelectionContext. Verified via BackendMetadata default tests above.
    }

    [Fact]
    public void RecordingSelectionContext_RequestedBackend_IsPropagated()
    {
        var ctx = RecordingSelectionContext.From(
            ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1920, 1080),
            FpsSelectionPolicy.Evaluate(30.0, 30.0),
            AutofocusPolicyReport.Unknown(),
            requestedBackend: "VideoEngineV2_Stable");

        Assert.Equal("VideoEngineV2_Stable", ctx.RequestedBackend);
    }

    [Fact]
    public void RecordingSelectionContext_EncoderFallback_IsPropagated()
    {
        var ctx = RecordingSelectionContext.From(
            ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1920, 1080),
            FpsSelectionPolicy.Evaluate(30.0, 30.0),
            AutofocusPolicyReport.Unknown(),
            encoderFallbackUsed: true,
            encoderFallbackReason: "HardwareEncoderUnavailable");

        Assert.True(ctx.EncoderFallbackUsed);
        Assert.Equal("HardwareEncoderUnavailable", ctx.EncoderFallbackReason);
    }

    // ── Real-session representative test ─────────────────────────────────────

    [Fact]
    public void V2RealSession_1080p30_AllExact_NoWarnings()
    {
        // Mirrors the real test1_20260628_223229 session (4-camera, V2, 1080p/30fps).
        // All cams: requested 1920x1080@30, selected 1920x1080@30, TimestampCsvStatus=Written.
        var res = ResolutionSelectionPolicy.Evaluate("1080p", 1920, 1080, 1920, 1080);
        var fps = FpsSelectionPolicy.Evaluate(30.0, 30.0, writerFps: 30.0,
            measuredCameraFps: 29.68, driverVfrDetected: false);
        var af  = AutofocusPolicyReport.NotSupported();

        Assert.Equal("Exact", res.ResolutionSelectionStatus);
        Assert.False(res.ResolutionFallbackUsed);
        Assert.Equal("Exact", fps.FpsSelectionStatus);
        Assert.False(fps.FpsFallbackUsed);
        Assert.False(fps.DriverVfrDetected);
        Assert.Equal("NotSupported", af.AutofocusPolicyResult);
        Assert.False(af.ManualExposureUiAvailable);
        Assert.False(af.ManualFocusUiAvailable);
    }
}
