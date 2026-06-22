using MultiCamApp.Core;

namespace MultiCamApp.Experiment;

/// <summary>Builds locomotor session options and recording-time display helpers.</summary>
public static class LocomotorRecordingController
{
    public const string RecordingModeName = "LocomotorStandard";

    public static bool IsLocomotorActive(AppConfig config, bool experimentUiEnabled) =>
        experimentUiEnabled && config.ExperimentMode.Enabled && config.LocomotorStandardMode.Enabled;

    public static ExperimentSessionOptions BuildSession(
        AppConfig config,
        bool experimentUiEnabled,
        double recordingDurationSeconds,
        double minimumAnalysisSeconds,
        double targetFps,
        bool constantFrameCountMode)
    {
        var loco = IsLocomotorActive(config, experimentUiEnabled);
        var locoSettings = config.LocomotorStandardMode;
        var recordingSec = recordingDurationSeconds > 0
            ? recordingDurationSeconds
            : locoSettings.DefaultRecordingDurationSeconds;
        var minSec = minimumAnalysisSeconds > 0
            ? minimumAnalysisSeconds
            : locoSettings.DefaultMinimumAnalysisDurationSeconds;

        if (loco && locoSettings.RecordLongerThanAnalysisDuration && recordingSec < minSec)
            recordingSec = locoSettings.DefaultRecordingDurationSeconds;

        var fps = targetFps > 0 ? targetFps : locoSettings.PreferredFps;

        return new ExperimentSessionOptions
        {
            Enabled = experimentUiEnabled && config.ExperimentMode.Enabled,
            LocomotorMode = loco,
            TargetFps = fps,
            TargetDurationSeconds = recordingSec,
            MinimumAnalysisDurationSeconds = minSec,
            PlannedRecordingDurationSeconds = recordingSec,
            ConstantFrameCountMode = constantFrameCountMode && config.ExperimentMode.AllowConstantFrameCountMode,
            StrictFrameValidation = loco
                ? locoSettings.RequireExactThirtyFps || locoSettings.RequireExact18000Frames
                : config.ExperimentMode.StrictFrameValidation,
            RequireExactThirtyFps = locoSettings.RequireExactThirtyFps,
            RequireExact18000Frames = locoSettings.RequireExact18000Frames
        };
    }

    public static long ApproximateMinimumAnalysisFrames(ExperimentSessionOptions session) =>
        session.LocomotorMode
            ? (long)Math.Round(session.MinimumAnalysisDurationSeconds * session.TargetFps)
            : session.ExpectedFrames;

    public static string FormatVerdict(ExperimentCheckVerdict verdict) => verdict switch
    {
        ExperimentCheckVerdict.Pass => "PASS",
        ExperimentCheckVerdict.Warning => "WARNING",
        ExperimentCheckVerdict.Fail => "FAIL",
        _ => "NOT_CHECKED"
    };
}
