using MultiCamApp.Core;

namespace MultiCamApp.Verification;

public sealed class BehaviorVerificationService
{
    public BehaviorSessionVerificationResult VerifySession(
        string sessionFolder,
        IReadOnlyList<VideoVerificationResult> videos,
        AppConfig config)
    {
        var result = new BehaviorSessionVerificationResult();
        var verdicts = new List<VerificationVerdict>();
        var notes = new List<string>();

        if (videos.Count == 0)
        {
            result.FinalVerdict = VerificationVerdict.Fail;
            result.SummaryMessage = "FAIL: No videos found for behavior analysis.";
            return result;
        }

        var validVideos = videos.Where(v => v.Probe != null && v.Probe.Success).ToList();
        if (validVideos.Count == 0)
        {
            result.FinalVerdict = VerificationVerdict.Fail;
            result.SummaryMessage = "FAIL: No readable videos found.";
            return result;
        }

        result.RequestedFps = validVideos[0].Expected?.Fps ?? 30.0;
        result.ActualFpsAvg = validVideos.Average(v =>
            VerificationCaptureProfile.ResolveMeasuredFps(v.Probe!, v.Metadata));
        result.DurationSecondsAvg = validVideos.Average(v => v.Probe!.DurationSeconds);
        result.FrameCountAvg = (long)validVideos.Average(v => v.Probe!.FrameCount ?? 0);
        result.CalculatedFpsAvg = validVideos.Average(v => (v.Probe!.FrameCount ?? 0) / Math.Max(0.001, v.Probe!.DurationSeconds));

        var resMatch = validVideos.All(v => v.ResolutionMatch == VerificationMatchStatus.Yes);
        result.ResolutionMatch = resMatch ? "Correct" : "Mismatch or warning";
        if (!resMatch)
        {
            verdicts.Add(VerificationVerdict.Warning);
            notes.Add("resolution mismatch between expected and actual");
        }

        var allCompleted = validVideos.All(v => v.Probe!.DurationSeconds > 0 && v.Probe.FrameCount > 0);
        result.RecordingStatus = allCompleted ? "Completed" : "Incomplete";
        if (!allCompleted)
        {
            verdicts.Add(VerificationVerdict.Fail);
            notes.Add("one or more recordings appear incomplete");
        }

        var (_, fpsFail) = VerificationCaptureProfile.GetFpsTolerances(result.RequestedFps, config.Verification);
        var fpsOk = validVideos.All(v =>
        {
            var actual = VerificationCaptureProfile.ResolveMeasuredFps(v.Probe!, v.Metadata);
            return Math.Abs(actual - result.RequestedFps) <= fpsFail;
        });
        if (!fpsOk)
        {
            verdicts.Add(VerificationVerdict.Warning);
            notes.Add($"FPS outside tolerance for requested {result.RequestedFps:F0} fps");
        }

        if (validVideos.Count >= 2)
        {
            var videosWithMeta = validVideos.Where(v => v.Metadata != null).ToList();
            if (videosWithMeta.Count == validVideos.Count)
            {
                var distinctFrameDiffs = videosWithMeta
                    .Select(v => v.Metadata!.InterCameraFrameDiff)
                    .Distinct()
                    .ToList();
                result.CameraFrameCountDifference = distinctFrameDiffs.Count == 1
                    ? distinctFrameDiffs[0]
                    : distinctFrameDiffs.Max();
            }
            else
            {
                var fcMin = validVideos.Min(v => v.Probe!.FrameCount ?? 0);
                var fcMax = validVideos.Max(v => v.Probe!.FrameCount ?? 0);
                result.CameraFrameCountDifference = fcMax - fcMin;
            }

            var wallDurations = validVideos
                .Select(ResolveWallDurationSeconds)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();
            if (wallDurations.Count >= 2)
                result.CameraDurationDifferenceSec = wallDurations.Max() - wallDurations.Min();
            else
            {
                var durMin = validVideos.Min(v => v.Probe!.DurationSeconds);
                var durMax = validVideos.Max(v => v.Probe!.DurationSeconds);
                result.CameraDurationDifferenceSec = durMax - durMin;
            }

            if (result.CameraDurationDifferenceSec > 5.0)
            {
                verdicts.Add(VerificationVerdict.Fail);
                notes.Add($"cross-camera duration difference exceeds 5 s ({result.CameraDurationDifferenceSec:F2}s)");
            }
            else if (result.CameraDurationDifferenceSec > 1.0)
            {
                verdicts.Add(VerificationVerdict.Warning);
                notes.Add($"cross-camera duration difference {result.CameraDurationDifferenceSec:F2}s");
            }

            if (result.CameraFrameCountDifference > 5)
            {
                verdicts.Add(VerificationVerdict.Fail);
                notes.Add($"cross-camera frame count difference exceeds 5 frames ({result.CameraFrameCountDifference})");
            }
            else if (result.CameraFrameCountDifference > 2)
            {
                verdicts.Add(VerificationVerdict.Warning);
                notes.Add($"cross-camera frame count difference {result.CameraFrameCountDifference}");
            }
        }

        if (result.DurationSecondsAvg >= 600)
        {
            var expectedFrames10m = (long)(result.ActualFpsAvg * 600);
            result.CropRecommendation = $"Start: 0 s, End: 600 s (expected frames: {expectedFrames10m})";
        }
        else
        {
            result.CropRecommendation = "Recording shorter than 10 minutes; no 10-minute crop suggested.";
        }

        result.FinalVerdict = Aggregate(verdicts);
        result.SummaryMessage = BuildSummaryMessage(result.FinalVerdict, notes);
        return result;
    }

    private static string BuildSummaryMessage(VerificationVerdict verdict, IReadOnlyList<string> notes)
    {
        return verdict switch
        {
            VerificationVerdict.Pass =>
                "PASS: Stable FPS, accurate timing, and good cross-camera synchronization.",
            VerificationVerdict.Warning when notes.Count > 0 =>
                $"WARNING: Review before analysis — {string.Join("; ", notes.Distinct())}.",
            VerificationVerdict.Warning =>
                "WARNING: One or more behavioral metrics are outside optimal thresholds.",
            _ when notes.Count > 0 =>
                $"FAIL: {string.Join("; ", notes.Distinct())}.",
            _ => "FAIL: Recording incomplete or cross-camera sync exceeds allowed thresholds."
        };
    }

    private static VerificationVerdict Aggregate(IEnumerable<VerificationVerdict> verdicts)
    {
        if (verdicts.Any(v => v == VerificationVerdict.Fail)) return VerificationVerdict.Fail;
        if (verdicts.Any(v => v == VerificationVerdict.Warning)) return VerificationVerdict.Warning;
        return VerificationVerdict.Pass;
    }

    private static double? ResolveWallDurationSeconds(VideoVerificationResult video)
    {
        if (video.WallDurationSeconds is > 0)
            return video.WallDurationSeconds;
        if (video.Metadata?.WallClockDurationSeconds is > 0)
            return video.Metadata.WallClockDurationSeconds;
        if (video.Metadata?.WallDurationSeconds is > 0)
            return video.Metadata.WallDurationSeconds;
        if (video.Probe?.DurationSeconds is > 0)
            return video.Probe.DurationSeconds;
        return null;
    }
}
