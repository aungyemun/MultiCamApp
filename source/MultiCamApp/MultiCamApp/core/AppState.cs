namespace MultiCamApp.Core;

public enum AppRunState
{
    Idle,
    Previewing,
    Recording
}

public sealed class AppState
{
    public AppRunState RunState { get; set; } = AppRunState.Idle;
    public int CameraLayout { get; set; } = 1;
    public string? OutputFolder { get; set; }
    public string SessionTitle { get; set; } = "";
    public string? ActiveSessionPath { get; set; }
    public bool ExperimentModeActive { get; set; }
    public double ExperimentTargetFps { get; set; } = 30;
    public double ExperimentDurationSeconds { get; set; } = 660;
    public double ExperimentMinimumAnalysisSeconds { get; set; } = 600;
    public bool ExperimentRecordLongerThanAnalysis { get; set; } = true;
    public bool ExperimentConstantFrameCountMode { get; set; }
    public string VerificationProfileName { get; set; } = "Standard";
    public bool IsRecording => RunState == AppRunState.Recording;
    public bool IsPreviewing => RunState == AppRunState.Previewing || RunState == AppRunState.Recording;
}
