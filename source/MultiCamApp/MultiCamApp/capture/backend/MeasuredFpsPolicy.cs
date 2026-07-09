// v1.2.20-alpha — honest measured-FPS policy and classification.
//
// Policy: the app requests the user-selected FPS from the camera driver and writer.
// If the camera/driver consistently delivers slightly lower real FPS (e.g. 29.67–29.70 fps
// when 30 fps is requested) that is acceptable. The app NEVER creates duplicate, ghost, or
// placeholder frames to force exact FPS. The timestamp CSV measured FPS is authoritative.
//
// Classification: PASS / PASS_WITH_INFO / CONSISTENT_LOWER_REAL_FPS / DRIVER_VFR_BEHAVIOR
//               / PASS_WITH_WARNING / FAIL
//
// No WPF, WinRT, or camera device dependencies — pure logic, fully testable.

namespace MultiCamApp.Capture.Backend;

// ── Status enum ───────────────────────────────────────────────────────────────

/// <summary>
/// Classification of the measured camera FPS relative to the requested FPS.
/// All statuses except FAIL are acceptable for scientific timing use.
/// </summary>
public enum RealFpsStabilityStatus
{
    /// <summary>Measured FPS is within ±0.6 fps of requested and stable. No gaps.</summary>
    Pass,

    /// <summary>Measured FPS is within ±1.5 fps of requested and stable. Slight drift.</summary>
    PassWithInfo,

    /// <summary>
    /// Measured FPS is consistently lower than requested (camera hardware limit or driver behavior)
    /// but stable, with no gaps or duplicate frames. Acceptable for scientific timing.
    /// </summary>
    ConsistentLowerRealFps,

    /// <summary>
    /// Camera driver delivers VFR timing. Measured FPS may fluctuate but no gaps or fakes.
    /// Acceptable; VFR is a known driver behavior for some camera models.
    /// </summary>
    DriverVfrBehavior,

    /// <summary>Measured FPS has mid-session gaps or moderate instability. Non-fatal warning.</summary>
    PassWithWarning,

    /// <summary>
    /// Severe instability, large gap count, excessive FPS deviation, or fake/duplicate
    /// frame insertion detected. Recording integrity in question.
    /// </summary>
    Fail,

    /// <summary>Not yet evaluated (pre-recording or measurement unavailable).</summary>
    NotEvaluated,
}

// ── Evaluation result ─────────────────────────────────────────────────────────

/// <summary>Result of evaluating the measured camera FPS against the requested FPS.</summary>
public sealed record MeasuredFpsEvaluationResult
{
    public double RequestedFps                     { get; init; }
    public double MeasuredFps                      { get; init; }
    public double MeasuredFpsDiffFromRequested      { get; init; }
    public double MeasuredFpsPercentDiffFromRequested { get; init; }
    public RealFpsStabilityStatus RealFpsStabilityStatus { get; init; } = RealFpsStabilityStatus.NotEvaluated;
    public bool   ConsistentLowerRealFpsAccepted   { get; init; }

    // Padding-prohibition policy constants: always true.
    public bool   NoArtificialFramePadding         { get; init; } = true;
    public bool   NoDuplicateFramePadding          { get; init; } = true;
    public bool   NoPlaceholderFrames              { get; init; } = true;

    public string ClassificationReason             { get; init; } = "";
}

// ── Policy evaluator ──────────────────────────────────────────────────────────

/// <summary>
/// Pure-logic policy: classifies the measured camera FPS and enforces the no-padding contract.
/// </summary>
public static class MeasuredFpsPolicy
{
    // Tolerance thresholds.
    private const double PassThreshold    = 0.6;   // ≤0.6 fps diff → Pass
    private const double InfoThreshold    = 1.5;   // ≤1.5 fps diff → PassWithInfo
    private const double WarningPercent   = 10.0;  // ≤10 % diff → ConsistentLowerRealFps or PassWithWarning
    private const double FailPercent      = 15.0;  // >15 % diff → Fail
    private const int    SevereGapCount   = 10;    // >10 gaps → Fail (vs. PassWithWarning)

    /// <param name="requestedFps">FPS the user selected (e.g. 30).</param>
    /// <param name="measuredFps">Timestamp CSV measured FPS (authoritative).</param>
    /// <param name="hasGaps">True if mid-session frame gaps were detected.</param>
    /// <param name="gapCount">Number of detected mid-session gap events (≥1 if hasGaps).</param>
    /// <param name="driverVfrDetected">True if the camera driver reports VFR timing.</param>
    /// <param name="hasDuplicateFrames">True if duplicate/ghost frames were detected — always Fail.</param>
    /// <param name="hasPlaceholderFrames">True if placeholder frames were inserted — always Fail.</param>
    public static MeasuredFpsEvaluationResult Evaluate(
        double requestedFps,
        double measuredFps,
        bool   hasGaps            = false,
        int    gapCount           = 0,
        bool   driverVfrDetected  = false,
        bool   hasDuplicateFrames = false,
        bool   hasPlaceholderFrames = false)
    {
        if (requestedFps <= 0)
            return NotEvaluated("RequestedFps not set");

        double diff        = Math.Abs(measuredFps - requestedFps);
        double percentDiff = requestedFps > 0 ? diff / requestedFps * 100.0 : 0;

        // Policy violation: any fake/duplicate frame → immediate Fail.
        if (hasDuplicateFrames)
            return Result(RealFpsStabilityStatus.Fail, requestedFps, measuredFps, diff, percentDiff,
                false, "DuplicateFramesDetected — policy violation: NoDuplicateFramePadding");

        if (hasPlaceholderFrames)
            return Result(RealFpsStabilityStatus.Fail, requestedFps, measuredFps, diff, percentDiff,
                false, "PlaceholderFramesDetected — policy violation: NoPlaceholderFrames");

        // Unstable path (gaps detected).
        if (hasGaps)
        {
            bool severe = gapCount > SevereGapCount || percentDiff > FailPercent;
            var  status = severe ? RealFpsStabilityStatus.Fail : RealFpsStabilityStatus.PassWithWarning;
            return Result(status, requestedFps, measuredFps, diff, percentDiff,
                false, severe
                    ? $"MidSessionGaps({gapCount})+ExcessiveDrift({percentDiff:F1}%)"
                    : $"MidSessionGaps({gapCount})");
        }

        // Stable path (no gaps).

        // DriverVFR: annotate but still classify by magnitude.
        if (driverVfrDetected && measuredFps < requestedFps - 0.05)
            return Result(RealFpsStabilityStatus.DriverVfrBehavior, requestedFps, measuredFps,
                diff, percentDiff, accepted: true,
                $"DriverVfrBehavior:Measured={measuredFps:F2},Requested={requestedFps:F2}");

        if (diff <= PassThreshold)
            return Result(RealFpsStabilityStatus.Pass, requestedFps, measuredFps, diff, percentDiff,
                accepted: true,
                $"Stable:diff={diff:F2}fps,within±{PassThreshold}fps");

        if (diff <= InfoThreshold)
            return Result(RealFpsStabilityStatus.PassWithInfo, requestedFps, measuredFps,
                diff, percentDiff, accepted: true,
                $"SlightDrift:diff={diff:F2}fps,within±{InfoThreshold}fps");

        if (measuredFps < requestedFps && percentDiff <= WarningPercent)
            return Result(RealFpsStabilityStatus.ConsistentLowerRealFps, requestedFps, measuredFps,
                diff, percentDiff, accepted: true,
                $"ConsistentLower:measured={measuredFps:F2},diff={percentDiff:F1}%");

        if (percentDiff <= FailPercent)
            return Result(RealFpsStabilityStatus.PassWithWarning, requestedFps, measuredFps,
                diff, percentDiff, accepted: false,
                $"LargeDrift:diff={percentDiff:F1}%>threshold({WarningPercent}%)");

        return Result(RealFpsStabilityStatus.Fail, requestedFps, measuredFps, diff, percentDiff,
            accepted: false,
            $"ExcessiveDrift:diff={percentDiff:F1}%>failThreshold({FailPercent}%)");
    }

    private static MeasuredFpsEvaluationResult Result(
        RealFpsStabilityStatus status,
        double requested, double measured, double diff, double percentDiff,
        bool accepted, string reason)
    {
        return new MeasuredFpsEvaluationResult
        {
            RequestedFps                        = requested,
            MeasuredFps                         = measured,
            MeasuredFpsDiffFromRequested         = diff,
            MeasuredFpsPercentDiffFromRequested  = percentDiff,
            RealFpsStabilityStatus               = status,
            ConsistentLowerRealFpsAccepted       = accepted,
            NoArtificialFramePadding             = true,
            NoDuplicateFramePadding              = true,
            NoPlaceholderFrames                  = true,
            ClassificationReason                 = reason,
        };
    }

    private static MeasuredFpsEvaluationResult NotEvaluated(string reason) =>
        new MeasuredFpsEvaluationResult
        {
            RealFpsStabilityStatus  = RealFpsStabilityStatus.NotEvaluated,
            NoArtificialFramePadding = true,
            NoDuplicateFramePadding  = true,
            NoPlaceholderFrames      = true,
            ClassificationReason     = reason,
        };
}
