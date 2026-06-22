using MultiCamApp.Core;
using MultiCamApp.Verification;

namespace MultiCamApp.Experiment;

public static class FrameCountValidator
{
    public static long ComputeExpectedFrames(double durationSeconds, double fps) =>
        (long)Math.Round(durationSeconds * fps, MidpointRounding.AwayFromZero);

    public static ExperimentCheckVerdict Evaluate(
        FrameTimingSummary timing,
        VerificationProfileSettings profile,
        bool strictFrameValidation)
    {
        if (!strictFrameValidation)
            return ExperimentCheckVerdict.Pass;

        var frameDiff = Math.Abs(timing.ActualFramesWritten - timing.ExpectedFrames);
        var durDiff = Math.Abs(timing.DurationSeconds - timing.TargetDurationSeconds);
        var fpsDiff = timing.FpsDrift;

        var fail = frameDiff > profile.FrameFailTolerance
                   || durDiff > profile.DurationFailToleranceSeconds
                   || fpsDiff > profile.FpsFailTolerance
                   || timing.DroppedFrames > profile.DroppedFrameFailThreshold
                   || timing.DuplicateFrames > profile.DuplicateFrameFailThreshold;

        if (fail) return ExperimentCheckVerdict.Fail;

        var warn = frameDiff > profile.FrameWarningTolerance
                   || durDiff > profile.DurationWarningToleranceSeconds
                   || fpsDiff > profile.FpsWarningTolerance
                   || timing.DroppedFrames > profile.DroppedFrameWarningThreshold
                   || timing.DuplicateFrames > profile.DuplicateFrameWarningThreshold;

        return warn ? ExperimentCheckVerdict.Warning : ExperimentCheckVerdict.Pass;
    }

    /// <summary>Locomotor: pass when at least minimum analysis duration captured; do not fail for recording longer than 10 min.</summary>
    public static ExperimentCheckVerdict EvaluateLocomotor(
        FrameTimingSummary timing,
        double minimumAnalysisDurationSeconds,
        LocomotorVerificationProfile profile)
    {
        if (timing.DurationSeconds < minimumAnalysisDurationSeconds)
            return ExperimentCheckVerdict.Fail;

        if (timing.MeanFps < profile.FpsWarningMin || timing.MeanFps > profile.FpsWarningMax)
            return ExperimentCheckVerdict.Warning;

        if (timing.MeanFps < 27.0 || timing.MeanFps > 32.0)
            return ExperimentCheckVerdict.Fail;

        if (timing.DroppedFrames > 0)
        {
            var expected = minimumAnalysisDurationSeconds * timing.TargetFps;
            var dropRate = timing.DroppedFrames / Math.Max(1.0, expected);
            if (dropRate > 0.15) return ExperimentCheckVerdict.Fail;
            if (dropRate > 0.05) return ExperimentCheckVerdict.Warning;
        }

        return ExperimentCheckVerdict.Pass;
    }

    public static VerificationProfileSettings ResolveProfile(AppConfig config) =>
        config.VerificationProfiles.TryGetValue("ExperimentStrict", out var p)
            ? p
            : new VerificationProfileSettings();
}
