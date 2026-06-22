namespace MultiCamApp.Experiment;

public sealed class ExperimentModeSettings
{
    public bool Enabled { get; set; } = true;
    public int DefaultDurationSeconds { get; set; } = 660;
    public double DefaultFps { get; set; } = 30;
    public string ExpectedFramesFormula { get; set; } = "durationSeconds * fps";
    public bool StrictFrameValidation { get; set; } = true;
    public bool ConstantFrameCountMode { get; set; }
    public bool AllowConstantFrameCountMode { get; set; } = true;
    public bool PreflightRequired { get; set; } = true;
    public int PreflightDurationSeconds { get; set; } = 20;
    public bool AutoVerifyAfterRecording { get; set; } = true;
    public bool WarnBeforeStartIfPreflightNotPassed { get; set; } = true;
    /// <summary>Max |actual − target| FPS for Pass (Warning above this, below fail thresholds).</summary>
    public double PreflightFpsPassTolerance { get; set; } = 2.0;
    /// <summary>Max |actual − target| FPS before Fail (single-camera).</summary>
    public double PreflightFpsWarningTolerance { get; set; } = 5.0;
    /// <summary>Fail if mean FPS is below target × this ratio (e.g. 0.93 → 27.9 at 30 fps).</summary>
    public double PreflightMinFpsRatio { get; set; } = 0.93;
    /// <summary>Relaxed minimum FPS ratio when two or more cameras preview together (USB bandwidth).</summary>
    public double PreflightMinFpsRatioMultiCamera { get; set; } = 0.90;
    /// <summary>Warning when dropped frames exceed this fraction of expected (e.g. 0.05 = 5%).</summary>
    public double PreflightMaxDroppedFrameRatioWarning { get; set; } = 0.05;
    /// <summary>Fail when dropped frames exceed this fraction of expected.</summary>
    public double PreflightMaxDroppedFrameRatioFail { get; set; } = 0.15;
    public double PreflightIntervalStabilityPassMs { get; set; } = 2.0;
    public long MinDiskWriteBytesPerSecond { get; set; } = 5_000_000;
}

public sealed class VerificationProfileSettings
{
    public double FpsWarningTolerance { get; set; } = 0.1;
    public double FpsFailTolerance { get; set; } = 0.5;
    public double DurationWarningToleranceSeconds { get; set; } = 0.2;
    public double DurationFailToleranceSeconds { get; set; } = 1.0;
    public int FrameWarningTolerance { get; set; } = 3;
    public int FrameFailTolerance { get; set; } = 30;
    public int DroppedFrameWarningThreshold { get; set; }
    public int DroppedFrameFailThreshold { get; set; } = 30;
    public int DuplicateFrameWarningThreshold { get; set; }
    public int DuplicateFrameFailThreshold { get; set; } = 30;
}
