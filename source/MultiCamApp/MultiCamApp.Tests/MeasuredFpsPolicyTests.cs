// v1.2.21-alpha — unit tests for honest measured-FPS policy and classification.
//
// Policy: the app never duplicates, pads, or inserts placeholder frames to meet
// the requested FPS. The timestamp CSV measured FPS is authoritative.
// Slightly lower measured FPS (e.g. 29.68 when 30 requested) is acceptable and
// classified as Pass or ConsistentLowerRealFps, not a failure.
//
// All tests are pure-logic — no WPF, WinRT, or camera device dependency.

using MultiCamApp.Capture.Backend;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class MeasuredFpsPolicyTests
{
    // ── Pass range: 29.5–30.1 fps, stable ────────────────────────────────────

    [Fact]
    public void Evaluate_Exact30fps_IsPass()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 30.0);
        Assert.Equal(RealFpsStabilityStatus.Pass, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_29_68fps_IsPass()
    {
        // Real session cam1/2/3: measured 29.68 fps with 30 requested.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.68);
        Assert.Equal(RealFpsStabilityStatus.Pass, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_29_5fps_IsPass()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.5);
        Assert.Equal(RealFpsStabilityStatus.Pass, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_30_1fps_IsPass()
    {
        // Slightly above requested is also Pass.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 30.1);
        Assert.Equal(RealFpsStabilityStatus.Pass, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_30_0fps_CameraFour_RealSession_IsPass()
    {
        // Real session cam4 (CFR, reference camera): measured 30.00 fps.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 30.00);
        Assert.Equal(RealFpsStabilityStatus.Pass, r.RealFpsStabilityStatus);
    }

    // ── PassWithInfo range: slightly more drift, still stable ─────────────────

    [Fact]
    public void Evaluate_29_0fps_Stable_IsPassWithInfo()
    {
        // 1.0 fps below requested, within 1.5 threshold, no gaps.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.0);
        Assert.Equal(RealFpsStabilityStatus.PassWithInfo, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_28_6fps_Stable_IsPassWithInfo()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 28.6);
        Assert.Equal(RealFpsStabilityStatus.PassWithInfo, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    // ── ConsistentLowerRealFps: stably lower, within 10% ─────────────────────

    [Fact]
    public void Evaluate_27_5fps_Stable_IsConsistentLower()
    {
        // 27.5 / 30 = 8.3% lower, stable, no driver VFR flag.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 27.5);
        Assert.Equal(RealFpsStabilityStatus.ConsistentLowerRealFps, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_ConsistentLower_DiffAndPercentAreCorrect()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 27.5);
        Assert.Equal(2.5, r.MeasuredFpsDiffFromRequested, 2);
        Assert.InRange(r.MeasuredFpsPercentDiffFromRequested, 8.0, 9.0);
    }

    // ── DriverVfrBehavior ─────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_DriverVfr_MeasuredLower_IsDriverVfrBehavior()
    {
        // cam1/2/3 in real session: driver reports r_frame_rate=60/1 (VFR)
        // but measured avg 29.68 fps. With DriverVfrDetected=true → DriverVfrBehavior.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.68,
            driverVfrDetected: true);
        Assert.Equal(RealFpsStabilityStatus.DriverVfrBehavior, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_DriverVfr_ExactMeasured_IsPassNotVfr()
    {
        // If measured FPS equals requested, driver VFR flag does not override to DriverVfrBehavior.
        // (VFR flag only matters when measured < requested - 0.05.)
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 30.00,
            driverVfrDetected: true);
        Assert.Equal(RealFpsStabilityStatus.Pass, r.RealFpsStabilityStatus);
    }

    // ── PassWithWarning: gaps or moderate drift ───────────────────────────────

    [Fact]
    public void Evaluate_WithGaps_FewGaps_IsPassWithWarning()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.5,
            hasGaps: true, gapCount: 3);
        Assert.Equal(RealFpsStabilityStatus.PassWithWarning, r.RealFpsStabilityStatus);
        Assert.False(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_ExcessiveDrift_12Percent_IsPassWithWarning()
    {
        // 12% below requested, no gaps but beyond ConsistentLower threshold.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 26.4);
        Assert.Equal(RealFpsStabilityStatus.PassWithWarning, r.RealFpsStabilityStatus);
        Assert.False(r.ConsistentLowerRealFpsAccepted);
    }

    // ── Fail: severe gaps or excessive drift ─────────────────────────────────

    [Fact]
    public void Evaluate_SevereGaps_IsFail()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 20.0,
            hasGaps: true, gapCount: 15);
        Assert.Equal(RealFpsStabilityStatus.Fail, r.RealFpsStabilityStatus);
        Assert.False(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_ExcessiveDrift_Over15Percent_IsFail()
    {
        // >15% below requested (e.g. 20 fps when 30 requested = 33% off).
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 20.0);
        Assert.Equal(RealFpsStabilityStatus.Fail, r.RealFpsStabilityStatus);
    }

    // ── Policy violations: duplicate / placeholder frames → always Fail ───────

    [Fact]
    public void Evaluate_DuplicateFrames_AlwaysFail()
    {
        // Even if measured FPS looks fine, duplicate frames are a policy violation.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.9,
            hasDuplicateFrames: true);
        Assert.Equal(RealFpsStabilityStatus.Fail, r.RealFpsStabilityStatus);
        Assert.False(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_PlaceholderFrames_AlwaysFail()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 30.0,
            hasPlaceholderFrames: true);
        Assert.Equal(RealFpsStabilityStatus.Fail, r.RealFpsStabilityStatus);
        Assert.False(r.ConsistentLowerRealFpsAccepted);
    }

    [Fact]
    public void Evaluate_DuplicateFrames_ReasonContainsPolicyViolation()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 30.0,
            hasDuplicateFrames: true);
        Assert.Contains("policy violation", r.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Padding-prohibition constants: always true ────────────────────────────

    [Fact]
    public void Evaluate_NoArtificialFramePadding_AlwaysTrue()
    {
        var cases = new[]
        {
            MeasuredFpsPolicy.Evaluate(30.0, 30.0),
            MeasuredFpsPolicy.Evaluate(30.0, 29.5),
            MeasuredFpsPolicy.Evaluate(30.0, 20.0),
            MeasuredFpsPolicy.Evaluate(30.0, 30.0, hasDuplicateFrames: true),
        };
        Assert.All(cases, r => Assert.True(r.NoArtificialFramePadding));
    }

    [Fact]
    public void Evaluate_NoDuplicateFramePadding_AlwaysTrue()
    {
        var cases = new[]
        {
            MeasuredFpsPolicy.Evaluate(30.0, 30.0),
            MeasuredFpsPolicy.Evaluate(30.0, 20.0, hasGaps: true, gapCount: 20),
            MeasuredFpsPolicy.Evaluate(30.0, 30.0, hasPlaceholderFrames: true),
        };
        Assert.All(cases, r => Assert.True(r.NoDuplicateFramePadding));
    }

    [Fact]
    public void Evaluate_NoPlaceholderFrames_AlwaysTrue()
    {
        var cases = new[]
        {
            MeasuredFpsPolicy.Evaluate(30.0, 30.0),
            MeasuredFpsPolicy.Evaluate(30.0, 15.0),
        };
        Assert.All(cases, r => Assert.True(r.NoPlaceholderFrames));
    }

    // ── Zero requestedFps → NotEvaluated ─────────────────────────────────────

    [Fact]
    public void Evaluate_ZeroRequestedFps_IsNotEvaluated()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 0, measuredFps: 30.0);
        Assert.Equal(RealFpsStabilityStatus.NotEvaluated, r.RealFpsStabilityStatus);
        // Even NotEvaluated must not say fake frames were accepted.
        Assert.True(r.NoArtificialFramePadding);
        Assert.True(r.NoDuplicateFramePadding);
        Assert.True(r.NoPlaceholderFrames);
    }

    // ── Diff and percent calculations ─────────────────────────────────────────

    [Fact]
    public void Evaluate_DiffCalculation_IsAbsolute()
    {
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.68);
        Assert.Equal(0.32, r.MeasuredFpsDiffFromRequested, 2);
    }

    [Fact]
    public void Evaluate_PercentDiff_IsCorrect()
    {
        // (30.0 - 29.68) / 30.0 * 100 ≈ 1.07%
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: 29.68);
        Assert.InRange(r.MeasuredFpsPercentDiffFromRequested, 1.0, 1.2);
    }

    // ── BackendMetadata default field values ──────────────────────────────────

    [Fact]
    public void BackendMetadata_RealFpsStabilityStatus_DefaultIsNotEvaluated()
    {
        var m = new BackendMetadata();
        Assert.Equal("NotEvaluated", m.RealFpsStabilityStatus);
    }

    [Fact]
    public void BackendMetadata_NoArtificialFramePadding_DefaultTrue()
    {
        Assert.True(new BackendMetadata().NoArtificialFramePadding);
    }

    [Fact]
    public void BackendMetadata_NoDuplicateFramePadding_DefaultTrue()
    {
        Assert.True(new BackendMetadata().NoDuplicateFramePadding);
    }

    [Fact]
    public void BackendMetadata_NoPlaceholderFrames_DefaultTrue()
    {
        Assert.True(new BackendMetadata().NoPlaceholderFrames);
    }

    [Fact]
    public void BackendMetadata_ConsistentLowerRealFpsAccepted_DefaultFalse()
    {
        // Default is false — not accepted until evaluation confirms.
        Assert.False(new BackendMetadata().ConsistentLowerRealFpsAccepted);
    }

    // ── RecordingSelectionContext.WithMeasuredFps ─────────────────────────────

    [Fact]
    public void WithMeasuredFps_SetsEvaluation()
    {
        var ctx = new RecordingSelectionContext { RequestedFps = 30.0 };
        var eval = MeasuredFpsPolicy.Evaluate(30.0, 29.68);
        var updated = ctx.WithMeasuredFps(eval);

        Assert.NotNull(updated.MeasuredFpsEvaluation);
        Assert.Equal(RealFpsStabilityStatus.Pass, updated.MeasuredFpsEvaluation!.RealFpsStabilityStatus);
    }

    [Fact]
    public void WithMeasuredFps_DoesNotMutateOriginal()
    {
        var ctx = new RecordingSelectionContext { RequestedFps = 30.0 };
        var eval = MeasuredFpsPolicy.Evaluate(30.0, 29.68);
        var updated = ctx.WithMeasuredFps(eval);

        Assert.Null(ctx.MeasuredFpsEvaluation);       // original unchanged
        Assert.NotNull(updated.MeasuredFpsEvaluation); // copy updated
    }

    // ── Real-session representative test ─────────────────────────────────────

    [Theory]
    [InlineData(29.68, RealFpsStabilityStatus.Pass)]       // cam1 real session
    [InlineData(29.70, RealFpsStabilityStatus.Pass)]       // cam2/3 real session
    [InlineData(30.00, RealFpsStabilityStatus.Pass)]       // cam4 real session (CFR reference)
    public void RealSession_4Camera_AllCamsPass(double measured, RealFpsStabilityStatus expected)
    {
        // Real test1_20260628_223229: 4-camera V2 session, 30 fps requested.
        // cam1/2/3: DriverVFR artifact (r_frame_rate=60/1), avg ~29.68–29.70 fps.
        // cam4: CFR, 30.00 fps.
        // All should classify as Pass — measured FPS is within ±0.6 fps, stable, no gaps.
        var r = MeasuredFpsPolicy.Evaluate(requestedFps: 30.0, measuredFps: measured);
        Assert.Equal(expected, r.RealFpsStabilityStatus);
        Assert.True(r.ConsistentLowerRealFpsAccepted);
        Assert.True(r.NoArtificialFramePadding);
        Assert.True(r.NoDuplicateFramePadding);
        Assert.True(r.NoPlaceholderFrames);
    }
}
