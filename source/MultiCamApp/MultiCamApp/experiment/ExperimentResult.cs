namespace MultiCamApp.Experiment;

public enum ExperimentCheckVerdict
{
    NotChecked,
    Pass,
    Warning,
    Fail
}

public sealed class ExperimentSessionOptions
{
    public bool Enabled { get; init; }
    public bool LocomotorMode { get; init; }
    public double TargetFps { get; init; } = 30;
    /// <summary>Auto-stop duration (planned recording length).</summary>
    public double TargetDurationSeconds { get; init; } = 600;
    public double MinimumAnalysisDurationSeconds { get; init; } = 600;
    public double PlannedRecordingDurationSeconds { get; init; } = 660;
    public bool ConstantFrameCountMode { get; init; }
    public bool StrictFrameValidation { get; init; } = true;
    public bool RequireExactThirtyFps { get; init; }
    public bool RequireExact18000Frames { get; init; }

    public long ExpectedFrames => LocomotorMode && !RequireExact18000Frames
        ? (long)Math.Round(MinimumAnalysisDurationSeconds * TargetFps, MidpointRounding.AwayFromZero)
        : (long)Math.Round(TargetDurationSeconds * TargetFps, MidpointRounding.AwayFromZero);

    public long PlannedFrames =>
        (long)Math.Round(PlannedRecordingDurationSeconds * TargetFps, MidpointRounding.AwayFromZero);

    public double TargetFrameIntervalMs => TargetFps > 0 ? 1000.0 / TargetFps : 33.333333;
}

public sealed class ExperimentPreflightCameraResult
{
    public string CameraSlot { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int SelectedWidth { get; set; }
    public int SelectedHeight { get; set; }
    public double SelectedFps { get; set; }
    public double ActualFps { get; set; }
    public long FramesCaptured { get; set; }
    public long DroppedFrames { get; set; }
    public long DuplicateFrames { get; set; }
    public double MeanFrameIntervalMs { get; set; }
    public double FrameIntervalStdDevMs { get; set; }
    public bool DiskSpeedOk { get; set; }
    public bool EncodingLoadOk { get; set; } = true;
    public bool CameraOpened { get; set; }
    public ExperimentCheckVerdict Result { get; set; }
    public List<string> Messages { get; } = [];
}

public sealed class ExperimentPreflightReport
{
    public DateTime TestedAtUtc { get; init; } = DateTime.UtcNow;
    public double TargetFps { get; init; }
    public int PreflightDurationSeconds { get; init; }
    public List<ExperimentPreflightCameraResult> Cameras { get; } = [];
    public ExperimentCheckVerdict OverallResult { get; set; } = ExperimentCheckVerdict.NotChecked;
    public bool CanStartStrictRecording { get; set; }
    public string Summary { get; set; } = "";
}

public sealed class ExperimentRecordingOutcome
{
    public string CameraSlot { get; init; } = "";
    public ExperimentCheckVerdict Result { get; init; }
    public long ExpectedFrames { get; init; }
    public long ActualFramesWritten { get; init; }
    public long DroppedFrames { get; init; }
    public long DuplicateFrames { get; init; }
    public long PlaceholderFrames { get; init; }
    public double DurationSeconds { get; init; }
    public double MeanFps { get; init; }
    public double FpsDrift { get; init; }
    public string Details { get; init; } = "";
}
