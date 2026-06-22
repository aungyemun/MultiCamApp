using MultiCamApp.Core;
using MultiCamApp.Experiment;

// LocomotorStandard sessions use LocomotorVerificationService (session-level), not ExperimentStrict per-file rules.

namespace MultiCamApp.Verification;

public sealed class ExperimentVerificationResult
{
    public bool IsExperimentSession { get; set; }
    public VerificationVerdict Verdict { get; set; }
    public long ExpectedFrames { get; set; }
    public long MetadataFrames { get; set; }
    public long? ProbeFrames { get; set; }
    public long FrameDifference { get; set; }
    public double ExpectedFps { get; set; }
    public double ActualFps { get; set; }
    public double ExpectedDurationSeconds { get; set; }
    public double ActualDurationSeconds { get; set; }
    public double DurationErrorSeconds { get; set; }
    public long DroppedFrames { get; set; }
    public long DuplicateFrames { get; set; }
    public bool ConstantFrameCountMode { get; set; }
    public double AverageFrameIntervalMs { get; set; }
    public double FrameIntervalStdDevMs { get; set; }
    public string ExperimentResult { get; set; } = "";
    public List<string> Messages { get; } = [];
}

public sealed class ExperimentVerificationService
{
    public ExperimentVerificationResult Evaluate(
        VideoVerificationResult video,
        CameraMetadataRecord? meta,
        AppConfig config)
    {
        var result = new ExperimentVerificationResult();
        if (meta == null || !meta.ExperimentMode)
            return result;

        if (string.Equals(meta.RecordingMode, LocomotorRecordingController.RecordingModeName,
                StringComparison.OrdinalIgnoreCase))
            return result;

        result.IsExperimentSession = true;
        var profile = FrameCountValidator.ResolveProfile(config);
        var probe = video.Probe;

        var expectedFrames = meta.ExpectedFrames > 0
            ? meta.ExpectedFrames
            : FrameCountValidator.ComputeExpectedFrames(meta.TargetDurationSeconds, meta.TargetFps);
        var metadataFrames = meta.FrameCount;
        var probeFrames = probe?.FrameCount;
        var actualDuration = probe?.DurationSeconds ?? meta.DurationSeconds ?? 0;
        var actualFps = probe?.Fps ?? meta.ActualFps;

        result.ExpectedFrames = expectedFrames;
        result.MetadataFrames = metadataFrames;
        result.ProbeFrames = probeFrames;
        result.FrameDifference = metadataFrames - expectedFrames;
        result.ExpectedFps = meta.TargetFps > 0
            ? meta.TargetFps
            : meta.SelectedDeviceFps > 0
                ? meta.SelectedDeviceFps
                : meta.RecordingWriterFps > 0 ? meta.RecordingWriterFps : meta.RequestedFps;
        result.ActualFps = actualFps;
        result.ExpectedDurationSeconds = meta.TargetDurationSeconds;
        result.ActualDurationSeconds = actualDuration;
        result.DurationErrorSeconds = Math.Abs(actualDuration - meta.TargetDurationSeconds);
        result.DroppedFrames = meta.DroppedFrames;
        result.DuplicateFrames = meta.DuplicateFrames;
        result.ConstantFrameCountMode = meta.ConstantFrameCountMode;
        result.AverageFrameIntervalMs = meta.AverageFrameIntervalMs;
        result.FrameIntervalStdDevMs = meta.FrameIntervalStdDevMs;
        result.ExperimentResult = meta.ExperimentResult;

        var verdicts = new List<VerificationVerdict>();

        void Add(string msg, VerificationVerdict v)
        {
            result.Messages.Add(msg);
            verdicts.Add(v);
        }

        var frameDiff = Math.Abs(result.FrameDifference);
        if (probeFrames.HasValue && Math.Abs(probeFrames.Value - expectedFrames) > profile.FrameFailTolerance)
            Add($"ffprobe frame count {probeFrames} vs expected {expectedFrames}", VerificationVerdict.Fail);
        else if (frameDiff > profile.FrameFailTolerance)
            Add($"metadata frames {metadataFrames} vs expected {expectedFrames} (diff {frameDiff})", VerificationVerdict.Fail);
        else if (probeFrames.HasValue && Math.Abs(probeFrames.Value - expectedFrames) > profile.FrameWarningTolerance)
            Add($"ffprobe frame count off by {Math.Abs(probeFrames.Value - expectedFrames)}", VerificationVerdict.Warning);
        else if (frameDiff > profile.FrameWarningTolerance)
            Add($"metadata frame count off by {frameDiff}", VerificationVerdict.Warning);

        if (result.DurationErrorSeconds > profile.DurationFailToleranceSeconds)
            Add($"duration error {result.DurationErrorSeconds:F2}s", VerificationVerdict.Fail);
        else if (result.DurationErrorSeconds > profile.DurationWarningToleranceSeconds)
            Add($"duration error {result.DurationErrorSeconds:F2}s", VerificationVerdict.Warning);

        var fpsDiff = Math.Abs(result.ActualFps - result.ExpectedFps);
        if (fpsDiff > profile.FpsFailTolerance)
            Add($"FPS {result.ActualFps:F2} vs expected {result.ExpectedFps:F2}", VerificationVerdict.Fail);
        else if (fpsDiff > profile.FpsWarningTolerance)
            Add($"FPS drift {fpsDiff:F2}", VerificationVerdict.Warning);

        if (result.DroppedFrames > profile.DroppedFrameFailThreshold)
            Add($"dropped frames {result.DroppedFrames}", VerificationVerdict.Fail);
        else if (result.DroppedFrames > profile.DroppedFrameWarningThreshold)
            Add($"dropped frames {result.DroppedFrames}", VerificationVerdict.Warning);

        if (result.DuplicateFrames > profile.DuplicateFrameFailThreshold)
            Add($"duplicate frames {result.DuplicateFrames}", VerificationVerdict.Fail);
        else if (result.DuplicateFrames > profile.DuplicateFrameWarningThreshold)
            Add($"duplicate frames {result.DuplicateFrames}", VerificationVerdict.Warning);

        if (!string.IsNullOrEmpty(meta.ExperimentResult) &&
            meta.ExperimentResult.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            Add($"recording experiment result: {meta.ExperimentResult}", VerificationVerdict.Fail);

        result.Verdict = verdicts.Any(v => v == VerificationVerdict.Fail) ? VerificationVerdict.Fail
            : verdicts.Any(v => v == VerificationVerdict.Warning) ? VerificationVerdict.Warning
            : VerificationVerdict.Pass;

        return result;
    }
}
