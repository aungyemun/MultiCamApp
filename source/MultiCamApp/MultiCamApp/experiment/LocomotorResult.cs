using MultiCamApp.Verification;

namespace MultiCamApp.Experiment;

public sealed class LocomotorCameraVerificationRow
{
    public string CameraSlot { get; init; } = "";
    public VerificationVerdict Result { get; set; }
    public double ActualDurationSeconds { get; set; }
    public double MinimumRequiredDurationSeconds { get; set; }
    public bool DurationOk { get; set; }
    public double ActualFps { get; set; }
    public bool FpsOk { get; set; }
    public long FramesWritten { get; set; }
    public bool FrameCountMatch { get; set; }
    public string Resolution { get; set; } = "";
    public bool ContainerDurationWarning { get; set; }
    public bool UsableForTenMinuteAnalysis { get; set; }
    public string Recommendation { get; set; } = "";
    public List<string> Messages { get; } = [];
}

public sealed class LocomotorSessionVerificationResult
{
    public VerificationVerdict OverallResult { get; set; } = VerificationVerdict.NotChecked;
    public string LocomotorStandardResult { get; set; } = "";
    public string SummaryMessage { get; set; } = "";
    public bool MinimumAnalysisDurationMet { get; set; }
    public bool RecommendedForLocomotorAnalysis { get; set; }
    public long CamFrameCountDifference { get; set; }
    public double CamFrameCountDifferencePercent { get; set; }
    public double StartTimeDifferenceSeconds { get; set; }
    public double StopTimeDifferenceSeconds { get; set; }
    public double MonotonicDurationDifferenceSeconds { get; set; }
    public double? ActualFpsCam1 { get; set; }
    public double? ActualFpsCam2 { get; set; }
    public List<LocomotorCameraVerificationRow> Cameras { get; } = [];
}
