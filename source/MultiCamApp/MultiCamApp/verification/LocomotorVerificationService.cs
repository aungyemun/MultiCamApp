using System.Globalization;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Experiment;

namespace MultiCamApp.Verification;

public sealed class LocomotorVerificationService
{
    public LocomotorSessionVerificationResult VerifySession(
        string sessionFolder,
        IReadOnlyList<VideoVerificationResult> videos,
        AppConfig config)
    {
        var profile = ResolveProfile(config);
        var result = new LocomotorSessionVerificationResult();
        var verdicts = new List<VerificationVerdict>();

        var cameraRows = new List<(CameraMetadataRecord Meta, VideoProbeData? Probe)>();
        foreach (var v in videos.OrderBy(x => x.Entry.CameraSlot, StringComparer.OrdinalIgnoreCase))
        {
            if (!v.Entry.MetadataFound || v.Entry.MetadataPath == null)
            {
                verdicts.Add(VerificationVerdict.Fail);
                result.Cameras.Add(new LocomotorCameraVerificationRow
                {
                    CameraSlot = v.Entry.CameraSlot,
                    Result = VerificationVerdict.Fail,
                    Recommendation = "Missing metadata.txt",
                    Messages = { "metadata.txt not found" }
                });
                continue;
            }

            var meta = MetadataParser.ParseCameraMetadataFile(v.Entry.MetadataPath);
            if (meta == null)
            {
                verdicts.Add(VerificationVerdict.Fail);
                continue;
            }

            cameraRows.Add((meta, v.Probe));
        }

        if (cameraRows.Count == 0)
        {
            result.OverallResult = VerificationVerdict.Fail;
            result.LocomotorStandardResult = "FAIL";
            result.SummaryMessage =
                "FAIL: This session is not recommended for 10-minute locomotor analysis. No camera metadata found.";
            return result;
        }

        foreach (var (meta, probe) in cameraRows)
        {
            var row = EvaluateCamera(meta, probe, profile);
            result.Cameras.Add(row);
            verdicts.Add(row.Result);
        }

        if (cameraRows.Count >= 2)
            ApplyCrossCameraChecks(cameraRows.Select(c => c.Meta).ToList(), result, profile, verdicts);

        result.OverallResult = Aggregate(verdicts);
        result.LocomotorStandardResult = result.OverallResult switch
        {
            VerificationVerdict.Pass => "PASS",
            VerificationVerdict.Warning => "WARNING",
            _ => "FAIL"
        };
        result.MinimumAnalysisDurationMet = result.Cameras.All(c => c.DurationOk);
        result.RecommendedForLocomotorAnalysis = result.OverallResult != VerificationVerdict.Fail;
        result.SummaryMessage = BuildSummary(result);
        return result;
    }

    public static LocomotorVerificationProfile ResolveProfile(AppConfig config) =>
        config.LocomotorStandardVerification ?? new LocomotorVerificationProfile();

    private static LocomotorCameraVerificationRow EvaluateCamera(
        CameraMetadataRecord meta,
        VideoProbeData? probe,
        LocomotorVerificationProfile profile)
    {
        var row = new LocomotorCameraVerificationRow
        {
            CameraSlot = meta.CameraSlot,
            MinimumRequiredDurationSeconds = profile.MinimumDurationSeconds,
            Resolution = meta.Resolution ?? CaptureResolutionPreset.ToLabel(meta.PixelWidth, meta.PixelHeight)
        };

        var verdicts = new List<VerificationVerdict>();
        var duration = meta.DurationSeconds ?? 0;
        if (duration <= 0 && probe?.DurationSeconds > 0)
            duration = probe.DurationSeconds;
        row.ActualDurationSeconds = duration;
        row.DurationOk = duration >= profile.MinimumDurationSeconds;
        if (!row.DurationOk)
        {
            verdicts.Add(VerificationVerdict.Fail);
            row.Messages.Add($"Duration {duration:F1}s is below minimum {profile.MinimumDurationSeconds:F0}s");
        }
        else if (duration < profile.PreferredRecordingDurationSeconds - 50
                 && duration >= profile.MinimumDurationSeconds)
        {
            verdicts.Add(VerificationVerdict.Warning);
            row.Messages.Add($"Recording {duration:F0}s is usable but shorter than preferred {profile.PreferredRecordingDurationSeconds:F0}s buffer");
        }

        row.ActualFps = probe?.Fps > 0
            ? probe.Fps
            : meta.RecordingWriterFps > 0
                ? meta.RecordingWriterFps
                : meta.ActualFps > 0 ? meta.ActualFps : 0;
        var requested = meta.SelectedDeviceFps > 0
            ? meta.SelectedDeviceFps
            : meta.RecordingWriterFps > 0
                ? meta.RecordingWriterFps
                : meta.RequestedFps > 0 ? meta.RequestedFps : 30;
        row.FpsOk = ScoreFps(row.ActualFps, requested, profile, verdicts, row.Messages);

        row.FramesWritten = meta.FrameCount;
        if (row.FramesWritten <= 0)
        {
            verdicts.Add(VerificationVerdict.Fail);
            row.Messages.Add("Frame count is 0");
        }

        if (probe != null && probe.Success)
        {
            if (!probe.HasVideoStream)
            {
                verdicts.Add(VerificationVerdict.Fail);
                row.Messages.Add("No video stream in file");
            }

            if (probe.FrameCount is > 0)
            {
                var fcDiff = Math.Abs(probe.FrameCount.Value - row.FramesWritten);
                row.FrameCountMatch = fcDiff <= profile.MetadataProbeFrameFailTolerance;
                if (!row.FrameCountMatch)
                {
                    verdicts.Add(VerificationVerdict.Fail);
                    row.Messages.Add($"metadata frames {row.FramesWritten} vs ffprobe {probe.FrameCount} (diff {fcDiff})");
                }
            }

            if (profile.IgnoreContainerDurationMismatchIfFrameCountsMatch && row.FrameCountMatch
                && meta.DurationSeconds is > 0 && probe.DurationSeconds > 0)
            {
                var durDiff = Math.Abs(probe.DurationSeconds - meta.DurationSeconds.Value);
                if (durDiff > profile.DurationDifferenceWarningSeconds)
                {
                    row.ContainerDurationWarning = true;
                    if (profile.ContainerDurationMismatchAsWarningOnly)
                    {
                        verdicts.Add(VerificationVerdict.Warning);
                        row.Messages.Add(
                            $"Container duration differs from writer by {durDiff:F1}s (frames match — mp4v timestamp)");
                    }
                }
            }

            if (meta.PixelWidth > 0 && meta.PixelHeight > 0
                && (probe.Width != meta.PixelWidth || probe.Height != meta.PixelHeight))
            {
                verdicts.Add(VerificationVerdict.Fail);
                row.Messages.Add(
                    $"Resolution mismatch: metadata {CaptureResolutionPreset.ToLabel(meta.PixelWidth, meta.PixelHeight)}, file {CaptureResolutionPreset.ToLabel(probe.Width, probe.Height)}");
            }
        }
        else
        {
            verdicts.Add(VerificationVerdict.Fail);
            row.Messages.Add(probe?.Error ?? "Video not readable (ffprobe failed)");
        }

        if (meta.DroppedFrames > 0 && duration > 0 && row.ActualFps > 0)
        {
            var expected = duration * row.ActualFps;
            var dropRate = meta.DroppedFrames / Math.Max(1.0, expected);
            if (dropRate > 0.15)
            {
                verdicts.Add(VerificationVerdict.Fail);
                row.Messages.Add($"Severe dropped frames: {meta.DroppedFrames}");
            }
            else if (dropRate > 0.05)
            {
                verdicts.Add(VerificationVerdict.Warning);
                row.Messages.Add($"Dropped frames: {meta.DroppedFrames} (honest report)");
            }
        }

        row.UsableForTenMinuteAnalysis = row.DurationOk && row.FpsOk && row.FramesWritten > 0
                                         && !verdicts.Contains(VerificationVerdict.Fail);
        row.Result = Aggregate(verdicts);
        row.Recommendation = row.Result switch
        {
            VerificationVerdict.Pass => "Usable for 10-minute crop",
            VerificationVerdict.Warning => "Review before analysis",
            _ => "Not recommended"
        };
        return row;
    }

    private static void ApplyCrossCameraChecks(
        IReadOnlyList<CameraMetadataRecord> cameras,
        LocomotorSessionVerificationResult result,
        LocomotorVerificationProfile profile,
        List<VerificationVerdict> verdicts)
    {
        var ordered = cameras.OrderBy(m => m.CameraSlot, StringComparer.OrdinalIgnoreCase).ToList();
        var a = ordered[0];
        var b = ordered[1];

        result.ActualFpsCam1 = a.CameraSlot.Contains("1", StringComparison.OrdinalIgnoreCase) ? a.ActualFps : b.ActualFps;
        result.ActualFpsCam2 = a.CameraSlot.Contains("2", StringComparison.OrdinalIgnoreCase) ? a.ActualFps
            : b.CameraSlot.Contains("2", StringComparison.OrdinalIgnoreCase) ? b.ActualFps : b.ActualFps;

        if (a.RecordingStartLocal.HasValue && b.RecordingStartLocal.HasValue)
        {
            result.StartTimeDifferenceSeconds =
                Math.Abs((a.RecordingStartLocal.Value - b.RecordingStartLocal.Value).TotalSeconds);
            if (result.StartTimeDifferenceSeconds > profile.StartSyncFailSeconds)
                verdicts.Add(VerificationVerdict.Fail);
            else if (result.StartTimeDifferenceSeconds > profile.StartSyncPassSeconds)
                verdicts.Add(VerificationVerdict.Warning);
        }

        if (a.RecordingStopLocal.HasValue && b.RecordingStopLocal.HasValue)
        {
            result.StopTimeDifferenceSeconds =
                Math.Abs((a.RecordingStopLocal.Value - b.RecordingStopLocal.Value).TotalSeconds);
            if (result.StopTimeDifferenceSeconds > profile.StopSyncFailSeconds)
                verdicts.Add(VerificationVerdict.Fail);
            else if (result.StopTimeDifferenceSeconds > profile.StopSyncPassSeconds)
                verdicts.Add(VerificationVerdict.Warning);
        }

        var durA = a.DurationSeconds ?? 0;
        var durB = b.DurationSeconds ?? 0;
        result.MonotonicDurationDifferenceSeconds = Math.Abs(durA - durB);
        if (result.MonotonicDurationDifferenceSeconds > profile.DurationDifferenceWarningSeconds)
            verdicts.Add(VerificationVerdict.Warning);
        if (result.MonotonicDurationDifferenceSeconds > profile.DurationDifferencePassSeconds * 3)
            verdicts.Add(VerificationVerdict.Fail);

        var maxFrames = Math.Max(a.FrameCount, b.FrameCount);
        result.CamFrameCountDifference = Math.Abs(a.FrameCount - b.FrameCount);
        result.CamFrameCountDifferencePercent = maxFrames > 0
            ? 100.0 * result.CamFrameCountDifference / maxFrames
            : 0;

        if (result.CamFrameCountDifferencePercent > profile.FrameDifferenceWarningPercent)
            verdicts.Add(VerificationVerdict.Warning);
        if (result.CamFrameCountDifferencePercent > profile.FrameDifferenceWarningPercent * 1.01
            && result.CamFrameCountDifferencePercent > 3.0)
            verdicts.Add(VerificationVerdict.Fail);

        if (!string.IsNullOrEmpty(a.CameraHardwareId) && !string.IsNullOrEmpty(b.CameraHardwareId)
            && string.Equals(a.CameraHardwareId, b.CameraHardwareId, StringComparison.OrdinalIgnoreCase))
        {
            verdicts.Add(VerificationVerdict.Fail);
            foreach (var row in result.Cameras)
                row.Messages.Add("Same hardware ID on multiple slots — wrong camera mapping");
        }
    }

    private static bool ScoreFps(
        double actual,
        double requested,
        LocomotorVerificationProfile profile,
        List<VerificationVerdict> verdicts,
        List<string> messages)
    {
        if (actual <= 0)
        {
            verdicts.Add(VerificationVerdict.Fail);
            messages.Add("Actual FPS not reported");
            return false;
        }

        if (Math.Abs(requested - 30) < 0.5)
        {
            if (actual < 27.0 || actual > 32.0)
            {
                verdicts.Add(VerificationVerdict.Fail);
                messages.Add($"Actual FPS {actual:F2} outside locomotor range for 30 fps target");
                return false;
            }

            if (actual < profile.FpsWarningMin || actual > profile.FpsWarningMax)
            {
                verdicts.Add(VerificationVerdict.Warning);
                messages.Add($"Actual FPS {actual:F2} (measured, not nominal 30)");
                return true;
            }

            if (actual < profile.FpsPassMin || actual > profile.FpsPassMax)
            {
                verdicts.Add(VerificationVerdict.Warning);
                messages.Add($"Actual FPS {actual:F2}");
                return true;
            }

            return true;
        }

        var delta = Math.Abs(actual - requested);
        if (delta > 3)
        {
            verdicts.Add(VerificationVerdict.Fail);
            return false;
        }

        if (delta > 1.5)
            verdicts.Add(VerificationVerdict.Warning);
        return true;
    }

    private static string BuildSummary(LocomotorSessionVerificationResult result) => result.OverallResult switch
    {
        VerificationVerdict.Pass =>
            "PASS: This session is usable for 10-minute locomotor analysis. Recording is longer than 10 minutes and camera frame counts are consistent. Crop exactly 10 minutes using metadata/frame count.",
        VerificationVerdict.Warning =>
            "WARNING: This session may be usable, but timing/frame differences should be reviewed before analysis.",
        _ =>
            "FAIL: This session is not recommended for 10-minute locomotor analysis."
    };

    private static VerificationVerdict Aggregate(IEnumerable<VerificationVerdict> verdicts)
    {
        if (verdicts.Any(v => v == VerificationVerdict.Fail)) return VerificationVerdict.Fail;
        if (verdicts.Any(v => v == VerificationVerdict.Warning)) return VerificationVerdict.Warning;
        return VerificationVerdict.Pass;
    }
}
