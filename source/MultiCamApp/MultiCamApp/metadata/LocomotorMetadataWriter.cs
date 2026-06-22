using MultiCamApp.Experiment;
using MultiCamApp.Recording;
using MultiCamApp.Verification;

namespace MultiCamApp.Metadata;

public static class LocomotorMetadataWriter
{
    public static IEnumerable<string> BuildMetadataLines(RecordingCameraStats stats, LocomotorStandardSettings settings)
    {
        if (!stats.LocomotorMode) yield break;

        var minSec = stats.MinimumAnalysisDurationSeconds > 0
            ? stats.MinimumAnalysisDurationSeconds
            : settings.DefaultMinimumAnalysisDurationSeconds;
        var plannedSec = stats.PlannedRecordingDurationSeconds > 0
            ? stats.PlannedRecordingDurationSeconds
            : settings.DefaultRecordingDurationSeconds;
        var usable = stats.DurationSeconds >= minSec && stats.FramesWritten > 0;

        yield return "";
        yield return "--- Locomotor Recording Mode ---";
        yield return $"RecordingMode: {LocomotorRecordingController.RecordingModeName}";
        yield return $"MinimumAnalysisDurationSeconds: {minSec:F3}";
        yield return $"PlannedRecordingDurationSeconds: {plannedSec:F3}";
        yield return $"ActualRecordingDurationSeconds: {stats.DurationSeconds:F6}";
        yield return $"ActualFramesWritten: {stats.FramesWritten}";
        yield return $"ActualMeasuredFps: {stats.MeasuredWriterFps:F4}";
        yield return $"UsableFor10MinAnalysis: {usable}";
        yield return $"RecommendedCropMethod: {settings.RecommendedCropMethod}";
        yield return "RecommendedCropStart: recording_start";
        yield return "RecommendedCropDurationSeconds: 600";
        yield return
            "RecommendedCropNote: Crop exactly the first 10 minutes using frame count or metadata timing (not container duration alone).";
    }

    public static IEnumerable<string> BuildSessionSummaryLines(LocomotorSessionVerificationResult? locomotor)
    {
        if (locomotor == null) yield break;

        yield return "";
        yield return "--- Locomotor Standard Verification ---";
        yield return $"LocomotorStandardResult: {locomotor.LocomotorStandardResult}";
        yield return $"MinimumAnalysisDurationMet: {locomotor.MinimumAnalysisDurationMet}";
        yield return $"CamFrameCountDifference: {locomotor.CamFrameCountDifference}";
        yield return $"CamFrameCountDifferencePercent: {locomotor.CamFrameCountDifferencePercent:F3}";
        yield return $"StartTimeDifferenceSeconds: {locomotor.StartTimeDifferenceSeconds:F6}";
        yield return $"StopTimeDifferenceSeconds: {locomotor.StopTimeDifferenceSeconds:F6}";
        yield return $"MonotonicDurationDifferenceSeconds: {locomotor.MonotonicDurationDifferenceSeconds:F6}";
        if (locomotor.ActualFpsCam1.HasValue)
            yield return $"ActualFpsCam1: {locomotor.ActualFpsCam1.Value:F4}";
        if (locomotor.ActualFpsCam2.HasValue)
            yield return $"ActualFpsCam2: {locomotor.ActualFpsCam2.Value:F4}";
        yield return $"RecommendedForLocomotorAnalysis: {locomotor.RecommendedForLocomotorAnalysis}";
        yield return $"LocomotorSummary: {locomotor.SummaryMessage}";
    }
}
