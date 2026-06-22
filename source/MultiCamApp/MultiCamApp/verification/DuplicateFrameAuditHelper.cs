namespace MultiCamApp.Verification;

public sealed record DuplicateFrameCameraAudit(
    string Camera,
    long FramesWritten,
    long DuplicateFrames,
    long QueueDrops,
    long PlaceholderFrames,
    double DurationSeconds,
    double WriterFps,
    double MeasuredCameraFps,
    string Resolution = "");

public sealed record DuplicateFrameSessionSummary(
    string SessionName,
    string Resolution,
    double ApproxDurationSeconds,
    int CameraCount,
    string ScientificStatus,
    long MaxDuplicateFrames,
    long TotalDuplicateFrames,
    long QueueDropsTotal,
    long PlaceholderTotal,
    long MinFrameCount,
    long MaxFrameCount,
    string BestCamera,
    string CamerasNeedingDuplicateFrameCorrection,
    string Message);

public static class DuplicateFrameAuditHelper
{
    public const string DuplicateCorrectedSessionMessage =
        "Session result: PASS_WITH_WARNING. Videos are valid and constant-frame-count aligned. " +
        "Duplicate-frame correction was applied because one or more cameras delivered below the target FPS. " +
        "No writer queue drops or placeholders were detected.";

    public static double DuplicateFramesPerMinute(long duplicateFrames, double durationSeconds) =>
        durationSeconds > 0 ? duplicateFrames / (durationSeconds / 60.0) : 0;

    public static double DuplicatePercentage(long duplicateFrames, long framesWritten) =>
        framesWritten > 0 ? duplicateFrames * 100.0 / framesWritten : 0;

    public static string ClassifyCameraStability(DuplicateFrameCameraAudit camera)
    {
        var duplicatePercentage = DuplicatePercentage(camera.DuplicateFrames, camera.FramesWritten);
        if (camera.QueueDrops > 0 || camera.PlaceholderFrames > 0 || duplicatePercentage > 2.0)
            return "Poor";

        if (camera.DuplicateFrames <= 1 && IsVeryCloseToTargetFps(camera.WriterFps, camera.MeasuredCameraFps))
            return "Excellent";

        if (duplicatePercentage < 0.5)
            return "Good";

        if (duplicatePercentage <= 2.0)
            return "Warning";

        return "Poor";
    }

    public static string BuildRecommendedPreset(DuplicateFrameCameraAudit camera)
    {
        var stability = ClassifyCameraStability(camera);
        return stability switch
        {
            "Poor" => "Reduce resolution or camera count, use separate USB controllers, and re-record before scientific use.",
            "Warning" when camera.Resolution.Contains("1920x1080", StringComparison.OrdinalIgnoreCase)
                || camera.Resolution.Contains("1080", StringComparison.OrdinalIgnoreCase)
                => "Valid and aligned. Keep duplicate-frame reporting enabled; use 720p or fewer cameras if the duplicate rate must be reduced.",
            "Warning" => "Valid and aligned. Keep duplicate-frame reporting enabled; reduce camera load if the duplicate rate must be reduced.",
            _ => "Current preset is stable. Keep duplicate-frame reporting enabled for audit transparency."
        };
    }

    public static DuplicateFrameSessionSummary BuildSessionSummary(
        string sessionName,
        IReadOnlyCollection<DuplicateFrameCameraAudit> cameras)
    {
        if (cameras.Count == 0)
        {
            return new DuplicateFrameSessionSummary(
                sessionName, "-", 0, 0, CameraAuditStatus.Fail, 0, 0, 0, 0, 0, 0, "-",
                "None",
                "Session result: FAIL. No camera videos were available for audit.");
        }

        var totalDuplicates = cameras.Sum(c => c.DuplicateFrames);
        var totalQueueDrops = cameras.Sum(c => c.QueueDrops);
        var totalPlaceholders = cameras.Sum(c => c.PlaceholderFrames);
        var status = totalQueueDrops > 0 || totalPlaceholders > 0
            ? CameraAuditStatus.Fail
            : totalDuplicates > 0
                ? CameraAuditStatus.PassWithWarning
                : CameraAuditStatus.Pass;

        var best = cameras
            .OrderBy(c => StabilityRank(ClassifyCameraStability(c)))
            .ThenBy(c => c.DuplicateFrames)
            .ThenBy(c => Math.Abs((c.WriterFps > 0 ? c.WriterFps : 30.0) - c.MeasuredCameraFps))
            .ThenBy(c => c.Camera, StringComparer.OrdinalIgnoreCase)
            .First();

        var resolution = string.Join(", ", cameras.Select(c => c.Resolution).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct());
        if (string.IsNullOrWhiteSpace(resolution))
            resolution = "-";

        var needingCorrection = cameras
            .Where(c => c.DuplicateFrames > 0)
            .Select(c => c.Camera)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DuplicateFrameSessionSummary(
            sessionName,
            resolution,
            cameras.Max(c => c.DurationSeconds),
            cameras.Count,
            status,
            cameras.Max(c => c.DuplicateFrames),
            totalDuplicates,
            totalQueueDrops,
            totalPlaceholders,
            cameras.Min(c => c.FramesWritten),
            cameras.Max(c => c.FramesWritten),
            best.Camera,
            needingCorrection.Length == 0 ? "None" : string.Join(", ", needingCorrection),
            BuildSessionSummaryMessage(status, totalDuplicates, totalQueueDrops, totalPlaceholders));
    }

    public static string BuildSessionSummaryMessage(string status, long duplicates, long queueDrops, long placeholders)
    {
        if (string.Equals(status, CameraAuditStatus.PassWithWarning, StringComparison.OrdinalIgnoreCase)
            && duplicates > 0 && queueDrops == 0 && placeholders == 0)
            return DuplicateCorrectedSessionMessage;

        if (queueDrops > 0 || placeholders > 0)
            return $"Session result: {status}. Timing integrity warning: writer queue drops or placeholders were detected; review before scientific use.";

        if (string.Equals(status, CameraAuditStatus.Pass, StringComparison.OrdinalIgnoreCase))
            return "Session result: PASS. Videos are valid and constant-frame-count aligned. No duplicate-frame correction, writer queue drops, or placeholders were detected.";

        return $"Session result: {status}. Review individual camera audit details.";
    }

    private static bool IsVeryCloseToTargetFps(double writerFps, double measuredCameraFps)
    {
        if (writerFps <= 0 || measuredCameraFps <= 0)
            return false;

        return Math.Abs(writerFps - measuredCameraFps) <= 0.05;
    }

    private static int StabilityRank(string stability) => stability switch
    {
        "Excellent" => 0,
        "Good" => 1,
        "Warning" => 2,
        _ => 3
    };
}
