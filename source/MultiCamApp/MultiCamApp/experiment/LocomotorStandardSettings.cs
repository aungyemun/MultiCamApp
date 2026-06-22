namespace MultiCamApp.Experiment;

/// <summary>App-wide defaults for Locomotor Recording Mode (10 min analysis, ~11 min capture).</summary>
public sealed class LocomotorStandardSettings
{
    public bool Enabled { get; set; } = true;
    public int DefaultMinimumAnalysisDurationSeconds { get; set; } = 600;
    public int DefaultRecordingDurationSeconds { get; set; } = 660;
    public bool RecordLongerThanAnalysisDuration { get; set; } = true;
    public double PreferredFps { get; set; } = 30;
    public bool UseMeasuredFpsForFinalReport { get; set; } = true;
    public bool RequireExactThirtyFps { get; set; }
    public bool RequireExact18000Frames { get; set; }
    public string RecommendedCropMethod { get; set; } = "frame_count_based";
    public bool AutoVerifyAfterRecording { get; set; } = true;
}
