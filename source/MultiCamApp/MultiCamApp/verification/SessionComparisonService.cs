////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

using MultiCamApp.Localization;
using MultiCamApp.Metadata;

namespace MultiCamApp.Verification;

public sealed class RecordingSessionAuditResult
{
    public string SessionFolder { get; set; } = "";
    public string SessionLabel { get; set; } = "";
    public string SessionTimingMode { get; set; } = "";
    public string SessionStatus { get; set; } = CameraAuditStatus.Pass;
    public VerificationVerdict SessionVerdict { get; set; } = VerificationVerdict.Pass;
    public List<string> CamerasFound { get; } = [];
    public int ExpectedCameraCount { get; set; }
    public List<string> MissingCameraFolders { get; } = [];
    public List<string> MissingMp4Files { get; } = [];
    public List<string> MissingMetadataJson { get; } = [];
    public List<string> MissingMetadataTxt { get; } = [];
    public long? InterCameraFrameDifference { get; set; }
    public double? InterCameraWallDurationDifferenceSeconds { get; set; }
    public double? InterCameraFrameBasedDurationDifferenceSeconds { get; set; }
    public double? InterCameraMeasuredFpsDifference { get; set; }
    public double? StartOffsetMs { get; set; }
    public double? StopOffsetMs { get; set; }
    public double? MaxMeasuredFpsDifference => InterCameraMeasuredFpsDifference;
    public double? MaxWallClockDurationDifferenceSec => InterCameraWallDurationDifferenceSeconds;
    public double? MaxStartOffsetSec => StartOffsetMs.HasValue ? StartOffsetMs.Value / 1000.0 : null;
    public double? MaxEndOffsetSec => StopOffsetMs.HasValue ? StopOffsetMs.Value / 1000.0 : null;
    public long? MaxFrameCountDifference => InterCameraFrameDifference;
    public bool FrameCountDifferenceAcceptedBecauseOriginalMode { get; set; }
    public string ResolutionConsistency { get; set; } = "-";
    public string CodecConsistency { get; set; } = "-";
    public string PixelFormatConsistency { get; set; } = "-";
    public long TotalQueueDrops { get; set; }
    public long TotalDuplicates { get; set; }
    public long TotalPlaceholders { get; set; }
    public string SessionScientificTimingConfidence { get; set; } = ScientificTimingConfidence.Low;
    public List<string> Warnings { get; } = [];
    public List<string> Failures { get; } = [];
    public List<string> InterpretationNotes { get; } = [];
    public string ComparisonSummaryText { get; set; } = "";
    public List<VideoVerificationResult> CameraVideos { get; } = [];
}

/// <summary>
/// STABLE_CORE_V1 protected component — intra-session cam1/cam2/cam3/cam4 comparison,
/// sync metrics, and consistency checks. Modification requires regression checklist.
/// </summary>
public sealed class SessionComparisonService
{
    public RecordingSessionAuditResult CompareSession(
        string sessionFolder,
        IReadOnlyList<VideoVerificationResult> sessionVideos,
        IReadOnlyList<VideoFileEntry> sessionEntries,
        LanguageManager? language = null)
    {
        var result = new RecordingSessionAuditResult
        {
            SessionFolder = sessionFolder,
            SessionLabel = Path.GetFileName(sessionFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        };

        var cameraFolders = RecordingSessionDiscovery.GetCameraFolders(sessionFolder);
        result.ExpectedCameraCount = cameraFolders.Count;
        result.CamerasFound.AddRange(cameraFolders.Select(RecordingSessionDiscovery.CameraSlotFromFolder));

        foreach (var camFolder in cameraFolders)
        {
            var slot = RecordingSessionDiscovery.CameraSlotFromFolder(camFolder);
            var mp4 = Directory.EnumerateFiles(camFolder, "*.mp4").FirstOrDefault();
            if (mp4 == null)
                result.MissingMp4Files.Add(slot);

            if (RecordingSessionDiscovery.FindCameraMetadataFile(camFolder, slot, "json") == null)
                result.MissingMetadataJson.Add(slot);
            if (RecordingSessionDiscovery.FindCameraMetadataFile(camFolder, slot, "txt") == null)
                result.MissingMetadataTxt.Add(slot);
        }

        var readableVideos = sessionVideos
            .Where(v => v.Probe?.Success == true && v.Probe.HasVideoStream)
            .ToList();
        result.CameraVideos.AddRange(sessionVideos);
        var originalCaptureSession = IsOriginalCaptureSession(sessionVideos);
        if (originalCaptureSession)
        {
            result.SessionTimingMode = OriginalCaptureAuditPolicy.Mode;
            result.FrameCountDifferenceAcceptedBecauseOriginalMode = true;
            result.InterpretationNotes.Add(OriginalCaptureAuditPolicy.GetSessionInterpretation(language));
        }
        else if (IsLegacyConstantFrameCountSession(sessionVideos))
        {
            result.SessionTimingMode = OriginalCaptureAuditPolicy.LegacyConstantFrameCountMode;
        }

        foreach (var video in sessionVideos)
        {
            if (video.Probe?.Success != true || !video.Probe.HasVideoStream)
                result.Failures.Add(Tf(language, "sessionAuditVideoUnreadable", "{0}: video unreadable or corrupt", video.Entry.CameraSlot));
            if (video.Metadata == null && RecordingSessionDiscovery.FindCameraMetadataFile(
                    Path.GetDirectoryName(video.Entry.FullPath) ?? "", video.Entry.CameraSlot, "json") != null)
                result.Warnings.Add(Tf(language, "verifyMsgMetadataJsonUnparsed", "{0}: metadata.json present but could not be parsed", video.Entry.CameraSlot));
        }

        foreach (var slot in result.MissingMp4Files)
            result.Failures.Add(Tf(language, "sessionAuditMp4Missing", "{0}: MP4 file missing", slot));
        foreach (var slot in result.MissingMetadataJson)
            result.Failures.Add(Tf(language, "verifyMsgMetadataJsonMissingSlot", "{0}: metadata.json missing", slot));

        result.TotalQueueDrops = sessionVideos.Sum(v => v.Metadata?.WriterQueueDrops ?? 0);
        result.TotalDuplicates = sessionVideos.Sum(v =>
            (v.Metadata?.DuplicatedFrames ?? 0) + (v.Metadata?.DuplicateFrames ?? 0));
        result.TotalPlaceholders = sessionVideos.Sum(v => v.Metadata?.PlaceholderFrames ?? 0);

        if (result.TotalQueueDrops > 0)
        {
            result.Failures.Add(Tf(language, "sessionAuditPipelineIntegrity",
                "Pipeline integrity issue: drops={0}, duplicates={1}, placeholders={2}",
                result.TotalQueueDrops, result.TotalDuplicates, result.TotalPlaceholders));
        }

        if (result.TotalPlaceholders > 0)
        {
            result.Failures.Add(Tf(language, "sessionAuditPipelineIntegrity",
                "Pipeline integrity issue: drops={0}, duplicates={1}, placeholders={2}",
                result.TotalQueueDrops, result.TotalDuplicates, result.TotalPlaceholders));
        }

        var hasUnexpectedDuplicateFrames = sessionVideos.Any(v =>
            v.Metadata is { ConstantFrameCountMode: false, OriginalCaptureMode: false }
            && ((v.Metadata.DuplicatedFrames + v.Metadata.DuplicateFrames) > 0 || v.Metadata.PlaceholderFrames > 0));
        if (hasUnexpectedDuplicateFrames)
        {
            result.Failures.Add(Tf(language, "sessionAuditPipelineIntegrity",
                "Pipeline integrity issue: drops={0}, duplicates={1}, placeholders={2}",
                result.TotalQueueDrops, result.TotalDuplicates, result.TotalPlaceholders));
        }
        else if (originalCaptureSession && result.TotalDuplicates > 0)
        {
            result.Failures.Add(Tf(language, "sessionAuditOriginalCaptureDuplicates",
                "Original Capture Mode expected duplicateFrames=0, but duplicateFrames={0}. Re-record before scientific use.",
                result.TotalDuplicates));
        }
        else if (result.TotalDuplicates > 0 || result.TotalPlaceholders > 0)
        {
            result.Warnings.Add(Tf(language, "sessionAuditConstantFrameCount",
                "Constant frame count sync inserted duplicate frames: duplicates={0}, placeholders={1}",
                result.TotalDuplicates, result.TotalPlaceholders));
        }

        if (readableVideos.Count >= 1)
        {
            result.ResolutionConsistency = EvaluateConsistency(
                readableVideos.Select(v => v.ActualResolutionDisplay).Where(s => s != "-"),
                "resolution");
            result.CodecConsistency = EvaluateConsistency(
                readableVideos.Select(v => v.CodecDisplay).Where(s => s != "-"),
                "codec");
            result.PixelFormatConsistency = EvaluateConsistency(
                readableVideos.Select(v => v.Probe?.PixelFormat ?? "-").Where(s => s != "-"),
                "pixel format");

            if (result.ResolutionConsistency == CameraAuditStatus.Fail)
                result.Failures.Add(T(language, "sessionAuditResolutionMismatch", "Resolution mismatch between cameras in this session"));
            if (result.CodecConsistency == CameraAuditStatus.Fail)
                result.Failures.Add(T(language, "sessionAuditCodecMismatch", "Codec mismatch between cameras in this session"));
            if (result.PixelFormatConsistency == CameraAuditStatus.Fail)
                result.Failures.Add(T(language, "sessionAuditPixelFormatMismatch", "Pixel format mismatch between cameras in this session"));
        }

        if (readableVideos.Count >= 2)
            ApplyInterCameraComparison(result, readableVideos, language);
        else if (readableVideos.Count == 1)
            result.InterpretationNotes.Add(T(language, "sessionAuditSingleCameraNote", "Single-camera session: inter-camera comparison not applicable."));

        foreach (var video in sessionVideos)
        {
            ApplySingleCameraWarnings(result, video, language);
            var cameraStatus = CameraAuditStatus.FromVideoResult(video);
            if (cameraStatus == CameraAuditStatus.Fail)
                result.Failures.Add(Tf(language, "sessionAuditCameraFailed", "{0}: individual camera audit failed", video.Entry.CameraSlot));
        }

        result.SessionStatus = DetermineSessionStatus(result, sessionVideos);
        result.SessionVerdict = CameraAuditStatus.ToVerdict(result.SessionStatus);
        result.SessionScientificTimingConfidence = ScientificTimingConfidence.FromSessionVideos(sessionVideos);
        result.ComparisonSummaryText = BuildComparisonSummary(result, language);
        return result;
    }

    private static void ApplyInterCameraComparison(
        RecordingSessionAuditResult result,
        List<VideoVerificationResult> readableVideos,
        LanguageManager? language)
    {
        var frameCounts = readableVideos
            .Select(v => v.Metadata?.FrameCount > 0 ? v.Metadata.FrameCount : v.Probe?.FrameCount ?? 0)
            .ToList();
        if (frameCounts.Count >= 2)
            result.InterCameraFrameDifference = frameCounts.Max() - frameCounts.Min();

        var wallDurations = readableVideos
            .Select(ResolveWallDuration)
            .Where(d => d > 0)
            .ToList();
        if (wallDurations.Count >= 2)
            result.InterCameraWallDurationDifferenceSeconds = wallDurations.Max() - wallDurations.Min();

        var frameBasedDurations = readableVideos
            .Select(v => v.FrameBasedDurationSeconds ?? v.Metadata?.FrameBasedDurationSeconds ?? 0)
            .Where(d => d > 0)
            .ToList();
        if (frameBasedDurations.Count >= 2)
            result.InterCameraFrameBasedDurationDifferenceSeconds = frameBasedDurations.Max() - frameBasedDurations.Min();

        var measuredFps = readableVideos
            .Select(v => v.Metadata?.MeasuredCameraFps > 0
                ? v.Metadata.MeasuredCameraFps
                : v.Metadata?.ActualFps ?? 0)
            .Where(f => f > 0)
            .ToList();
        if (measuredFps.Count >= 2)
            result.InterCameraMeasuredFpsDifference = measuredFps.Max() - measuredFps.Min();

        var startOffsets = readableVideos
            .Select(v => v.Metadata?.InterCameraStartOffsetMs ?? 0)
            .Where(v => v > 0)
            .ToList();
        if (startOffsets.Count > 0)
            result.StartOffsetMs = startOffsets.Max();

        var stopOffsets = readableVideos
            .Select(v => v.Metadata?.InterCameraStopOffsetMs ?? 0)
            .Where(v => v > 0)
            .ToList();
        if (stopOffsets.Count > 0)
            result.StopOffsetMs = stopOffsets.Max();

        var originalCaptureSession = result.FrameCountDifferenceAcceptedBecauseOriginalMode;
        if (originalCaptureSession && result.InterCameraFrameDifference > 0)
        {
            result.InterpretationNotes.Add(Tf(language, "sessionAuditOriginalFrameDiffInfo",
                "Frame counts may differ because cameras delivered real frames at different measured FPS. Difference: {0} frame(s).",
                result.InterCameraFrameDifference));
        }
        else if (result.InterCameraFrameDifference > 5)
            result.Failures.Add(Tf(language, "sessionAuditFrameDiffTooLarge", "Inter-camera frame difference too large ({0} frames)", result.InterCameraFrameDifference));
        else if (result.InterCameraFrameDifference > 2)
            result.Warnings.Add(Tf(language, "sessionAuditFrameDiffWarning", "Inter-camera frame difference {0} frame(s)", result.InterCameraFrameDifference));

        if (result.StartOffsetMs > OriginalCaptureAuditPolicy.AcceptableStartEndOffsetMs)
            result.Failures.Add(Tf(language, "sessionAuditStartOffsetTooLarge", "Inter-camera start offset too large ({0:F1} ms)", result.StartOffsetMs));
        else if (!originalCaptureSession && result.StartOffsetMs > 50)
            result.Warnings.Add(Tf(language, "sessionAuditStartOffsetWarning", "Inter-camera start offset {0:F1} ms", result.StartOffsetMs));

        if (result.StopOffsetMs > OriginalCaptureAuditPolicy.AcceptableStartEndOffsetMs)
            result.Failures.Add(Tf(language, "sessionAuditStopOffsetTooLarge", "Inter-camera stop offset too large ({0:F1} ms)", result.StopOffsetMs));
        else if (!originalCaptureSession && result.StopOffsetMs > 50)
            result.Warnings.Add(Tf(language, "sessionAuditStopOffsetWarning", "Inter-camera stop offset {0:F1} ms", result.StopOffsetMs));

        if (result.InterCameraMeasuredFpsDifference > OriginalCaptureAuditPolicy.AcceptableMeasuredFpsDifference)
            result.Warnings.Add(Tf(language, "sessionAuditFpsDiffWarning", "Measured camera FPS difference {0:F3} fps", result.InterCameraMeasuredFpsDifference));
        // Only add this bland "frame counts may differ" note when the more specific, more useful
        // one above (with the actual frame-count difference) didn't already fire — in every real
        // session where frames genuinely differ because of FPS differences, both conditions are true
        // simultaneously, and stacking two notes that say the same thing (one with a number, one
        // without) is redundant, not informative.
        else if (originalCaptureSession && result.InterCameraMeasuredFpsDifference > 0.05
                 && !(originalCaptureSession && result.InterCameraFrameDifference > 0))
            result.InterpretationNotes.Add(OriginalCaptureAuditPolicy.GetStableDifferentFpsNote(language));

        if (result.InterCameraWallDurationDifferenceSeconds > OriginalCaptureAuditPolicy.AcceptableWallClockDurationDifferenceSeconds)
            result.Warnings.Add(Tf(language, "sessionAuditWallDurationDiff", "Wall-clock duration difference {0:F2}s between cameras", result.InterCameraWallDurationDifferenceSeconds));
    }

    private static void ApplySingleCameraWarnings(
        RecordingSessionAuditResult result,
        VideoVerificationResult video,
        LanguageManager? language)
    {
        var meta = video.Metadata;
        if (meta == null)
            return;

        if (meta.FramesCaptured > 0 && meta.FrameCount > 0 && meta.FramesCaptured != meta.FrameCount)
        {
            var delta = Math.Abs(meta.FramesCaptured - meta.FrameCount);
            if (meta.OriginalCaptureMode)
            {
                var message = OriginalCaptureVerificationPolicy.IsAcceptedStopBoundaryDifference(meta)
                    ? OriginalCaptureVerificationPolicy.GetStopBoundaryAcceptedMessage(language)
                    : Tf(language, "verifyMsgOriginalCaptureFramesCapturedDiff", "Original Capture framesCaptured differs from framesWritten by {0} frame(s)", delta);
                result.Warnings.Add($"{video.Entry.CameraSlot}: {message}");
            }
            else if (delta == 1)
                result.Warnings.Add(Tf(language, "sessionAuditFramesCapturedDiff", "{0}: FramesCaptured differs from FramesWritten by 1 frame", video.Entry.CameraSlot));
        }

        var writerFps = meta.ContainerFps > 0 ? meta.ContainerFps : meta.WriterFps > 0 ? meta.WriterFps : meta.RecordingWriterFps;
        var measured = meta.MeasuredCameraFps > 0 ? meta.MeasuredCameraFps : meta.ActualFps;
        if (!meta.OriginalCaptureMode && writerFps > 0 && measured > 0 && Math.Abs(writerFps - measured) > OriginalCaptureAuditPolicy.AcceptableMeasuredFpsDifference)
            result.Warnings.Add(Tf(language, "sessionAuditFpsContainerMeasuredDiff", "{0}: container/writer FPS differs from measured camera FPS", video.Entry.CameraSlot));

        if (meta.OriginalCaptureMode
            && meta.CaptureIntervalCount > 0
            && (meta.CaptureIntervalStdMs > OriginalCaptureAuditPolicy.UnstableCaptureIntervalStdMs
                || meta.MaxConsecutiveNoFrame > OriginalCaptureAuditPolicy.UnstableMaxConsecutiveNoFrame))
        {
            result.Warnings.Add(Tf(language, "sessionAuditOriginalCaptureIntervalUnstable", "{0}: Original Capture interval stability warning", video.Entry.CameraSlot));
        }

        if (string.Equals(meta.ScientificTimingStatus, CameraAuditStatus.PassWithWarning, StringComparison.OrdinalIgnoreCase))
        {
            var msg = language != null
                ? MetadataDisplayHelper.LocalizeScientificTimingMessage(language, meta.ScientificTimingMessage)
                : meta.ScientificTimingMessage ?? ScientificTimingAssessor.DefaultMessage;
            result.Warnings.Add($"{video.Entry.CameraSlot}: {msg}");
        }

        if (RecordingSessionDiscovery.FindCameraMetadataFile(
                Path.GetDirectoryName(video.Entry.FullPath) ?? "", video.Entry.CameraSlot, "txt") == null)
            result.Warnings.Add(Tf(language, "verifyMsgMetadataTxtMissingOptional", "{0}: metadata.txt missing (optional warning)", video.Entry.CameraSlot));
    }

    private static string DetermineSessionStatus(
        RecordingSessionAuditResult result,
        IReadOnlyList<VideoVerificationResult> sessionVideos)
    {
        if (result.Failures.Count > 0)
            return CameraAuditStatus.Fail;

        var anyCameraFail = sessionVideos.Any(v => CameraAuditStatus.FromVideoResult(v) == CameraAuditStatus.Fail);
        if (anyCameraFail)
            return CameraAuditStatus.Fail;

        if (result.Warnings.Count > 0)
            return CameraAuditStatus.PassWithWarning;

        var anyCameraWarn = sessionVideos.Any(v =>
            CameraAuditStatus.FromVideoResult(v) == CameraAuditStatus.PassWithWarning);
        return anyCameraWarn ? CameraAuditStatus.PassWithWarning : CameraAuditStatus.Pass;
    }

    private static string BuildComparisonSummary(RecordingSessionAuditResult result, LanguageManager? language)
    {
        var lines = new List<string>
        {
            Tf(language, "sessionAuditSummaryStatus", "Session status: {0}", result.SessionStatus),
            $"Scientific Timing Confidence: {result.SessionScientificTimingConfidence}",
            Tf(language, "sessionAuditSummaryTimingMode", "sessionTimingMode: {0}", string.IsNullOrWhiteSpace(result.SessionTimingMode) ? "-" : result.SessionTimingMode),
            Tf(language, "sessionAuditSummaryCamerasFound", "Cameras found: {0}", string.Join(", ", result.CamerasFound)),
            Tf(language, "sessionAuditSummaryExpectedFolders", "Expected camera folders: {0}", result.ExpectedCameraCount)
        };
        if (string.Equals(result.SessionScientificTimingConfidence, ScientificTimingConfidence.High, StringComparison.OrdinalIgnoreCase))
            lines.Add(ScientificTimingConfidence.HighMessage);

        if (result.InterCameraFrameDifference.HasValue)
            lines.Add(Tf(language, "sessionAuditSummaryFrameDiff", "Inter-camera frame difference: {0} frame(s)", result.InterCameraFrameDifference));
        if (result.FrameCountDifferenceAcceptedBecauseOriginalMode)
            lines.Add("frameCountDifferenceAcceptedBecauseOriginalMode: true");
        if (result.StartOffsetMs.HasValue)
            lines.Add(Tf(language, "sessionAuditSummaryStartOffset", "Start offset: {0:F1} ms", result.StartOffsetMs));
        if (result.StopOffsetMs.HasValue)
            lines.Add(Tf(language, "sessionAuditSummaryStopOffset", "Stop offset: {0:F1} ms", result.StopOffsetMs));
        if (result.InterCameraWallDurationDifferenceSeconds.HasValue)
            lines.Add(Tf(language, "sessionAuditSummaryWallDurationDiff", "Wall-clock duration difference: {0:F3}s", result.InterCameraWallDurationDifferenceSeconds));
        if (result.InterCameraMeasuredFpsDifference.HasValue)
            lines.Add(Tf(language, "sessionAuditSummaryFpsDiff", "Measured camera FPS difference: {0:F3} fps", result.InterCameraMeasuredFpsDifference));

        lines.Add(Tf(language, "sessionAuditSummaryResolutionConsistency", "Resolution consistency: {0}", result.ResolutionConsistency));
        lines.Add(Tf(language, "sessionAuditSummaryCodecConsistency", "Codec consistency: {0}", result.CodecConsistency));
        lines.Add(Tf(language, "sessionAuditSummaryPixelFormatConsistency", "Pixel format consistency: {0}", result.PixelFormatConsistency));
        lines.Add(Tf(language, "sessionAuditSummaryDrops", "Drops / duplicates / placeholders: {0} / {1} / {2}",
            result.TotalQueueDrops, result.TotalDuplicates, result.TotalPlaceholders));

        foreach (var failure in result.Failures)
            lines.Add(Tf(language, "sessionAuditSummaryFailureLine", "FAIL: {0}", failure));
        foreach (var warning in result.Warnings)
            lines.Add(Tf(language, "sessionAuditSummaryWarningLine", "WARN: {0}", warning));
        foreach (var note in result.InterpretationNotes)
            lines.Add(Tf(language, "sessionAuditSummaryNoteLine", "NOTE: {0}", note));

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsOriginalCaptureSession(IReadOnlyList<VideoVerificationResult> sessionVideos)
    {
        var metas = sessionVideos
            .Select(v => v.Metadata)
            .Where(m => m != null)
            .ToList();
        return metas.Count > 0 && metas.All(m => m!.OriginalCaptureMode
            || string.Equals(m.RecordingTimingMode, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLegacyConstantFrameCountSession(IReadOnlyList<VideoVerificationResult> sessionVideos)
    {
        return sessionVideos.Any(v =>
            v.Metadata != null
            && string.IsNullOrWhiteSpace(v.Metadata.RecordingTimingMode)
            && ((v.Metadata.DuplicatedFrames + v.Metadata.DuplicateFrames) > 0
                || v.Metadata.ConstantFrameCountMode));
    }

    private static string EvaluateConsistency(IEnumerable<string> values, string label)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count <= 1)
            return CameraAuditStatus.Pass;
        return CameraAuditStatus.Fail;
    }

    private static double ResolveWallDuration(VideoVerificationResult video)
    {
        if (video.WallDurationSeconds is > 0)
            return video.WallDurationSeconds.Value;
        if (video.Metadata?.WallClockDurationSeconds is > 0)
            return video.Metadata.WallClockDurationSeconds;
        if (video.Metadata?.WallDurationSeconds is > 0)
            return video.Metadata.WallDurationSeconds;
        return video.Probe?.DurationSeconds ?? 0;
    }

    private static string T(LanguageManager? language, string key, string fallback) =>
        language != null ? language[key] : fallback;

    private static string Tf(LanguageManager? language, string key, string fallback, params object[] args) =>
        string.Format(T(language, key, fallback), args);
}
