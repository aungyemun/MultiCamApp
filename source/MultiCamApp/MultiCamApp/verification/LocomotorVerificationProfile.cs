namespace MultiCamApp.Verification;

/// <summary>Verification tolerances for Locomotor Standard profile (dual-camera locomotor assays).</summary>
public sealed class LocomotorVerificationProfile
{
    public double MinimumDurationSeconds { get; set; } = 600;
    public double PreferredRecordingDurationSeconds { get; set; } = 660;
    public double FpsPassMin { get; set; } = 28.5;
    public double FpsPassMax { get; set; } = 31.0;
    public double FpsWarningMin { get; set; } = 27.0;
    public double FpsWarningMax { get; set; } = 32.0;
    public double StartSyncPassSeconds { get; set; } = 0.5;
    public double StartSyncFailSeconds { get; set; } = 3.0;
    public double StopSyncPassSeconds { get; set; } = 0.5;
    public double StopSyncFailSeconds { get; set; } = 3.0;
    public double DurationDifferencePassSeconds { get; set; } = 1.0;
    public double DurationDifferenceWarningSeconds { get; set; } = 3.0;
    public double FrameDifferencePassPercent { get; set; } = 1.0;
    public double FrameDifferenceWarningPercent { get; set; } = 3.0;
    public bool IgnoreContainerDurationMismatchIfFrameCountsMatch { get; set; } = true;
    public bool ContainerDurationMismatchAsWarningOnly { get; set; } = true;
    public long MetadataProbeFrameFailTolerance { get; set; } = 5;
}
