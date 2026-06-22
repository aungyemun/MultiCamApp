////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Globalization;
using System.Text;
using System.Text.Json;
using MultiCamApp.Capture;
using MultiCamApp.Localization;
using MultiCamApp.Recording;
using MultiCamApp.Utils;

namespace MultiCamApp.Verification;

public sealed class VerificationReportWriter
{
    private readonly LanguageManager _language;

    public VerificationReportWriter(LanguageManager? language = null) =>
        _language = language ?? StartupLanguage.Load();

    public string[] GetCsvHeaders() =>
    [
        _language["reportCsvResult"],
        _language["reportCsvCamera"],
        _language["reportCsvFileName"],
        _language["reportCsvFilePath"],
        _language["reportCsvMetadata"],
        _language["reportCsvExpectedResolution"],
        _language["reportCsvActualResolution"],
        _language["reportCsvResolutionMatch"],
        _language["reportCsvExpectedFps"],
        _language["reportCsvActualFps"],
        _language["reportCsvFpsMatch"],
        _language["reportCsvExpectedDuration"],
        _language["reportCsvActualDuration"],
        _language["reportCsvDurationMatch"],
        _language["reportCsvFrameCount"],
        _language["reportCsvExpectedFrameCount"],
        _language["reportCsvCodec"],
        _language["reportCsvContainer"],
        _language["reportCsvFileSize"],
        "device",
        "requestedFps",
        "writerFps",
        "playbackFps",
        "realCaptureFps",
        "fpsStabilityGrade",
        "framesCaptured",
        "framesWritten",
        "timestampCsv",
        "wallClockDuration",
        "containerDuration",
        "containerVsWallClockDifference",
        "startOffset",
        "scientificTimingStatus",
        "writerDrops",
        "duplicateFrames",
        "placeholderFrames",
        "metadataCompletenessPercent",
        "missingRequiredMetadataFields",
        "scientificMetadataComplete",
        _language["reportCsvDetails"],
        _language["reportCsvRecommendation"]
    ];

    public async Task ExportJsonAsync(VerificationReport report, string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var dto = new
        {
            report.AppVersion,
            report.VerifiedAtUtc,
            language = _language.CurrentLanguage,
            summary = report.Summary,
            sessionResult = report.SessionResult,
            sessionAudits = report.SessionAudits,
            videos = report.TableRows,
            details = report.Videos.Select(v => new
            {
                v.Entry.CameraSlot,
                v.Entry.FileName,
                v.DetailText,
                v.Recommendation,
                v.WarningMessages,
                v.ErrorMessages,
                v.Messages,
                Probe = v.Probe
            }),
            logs = report.LogLines
        };
        await File.WriteAllTextAsync(path, PrivacySanitizer.SanitizeForOutput(JsonSerializer.Serialize(dto, options)), Encoding.UTF8);
    }

    public async Task ExportTextAsync(VerificationReport report, string path)
    {
        var L = _language;
        var sb = new StringBuilder();
        var s = report.Summary;
        var sess = report.SessionResult;

        sb.AppendLine(L["reportVerificationTitle"]);
        sb.AppendLine($"{L["reportAppVersion"]}: {report.AppVersion}");
        sb.AppendLine($"{L["reportVerifiedUtc"]}: {report.VerifiedAtUtc:O}");
        sb.AppendLine($"{L["reportFolder"]}: {s.SelectedFolder}");
        sb.AppendLine($"{L["reportOverall"]}: {s.OverallVerdict}");
        sb.AppendLine($"{L["reportExpectedSettings"]}: {s.ExpectedSettingsSource}");
        sb.AppendLine($"{L["reportVideosFound"]}: {s.TotalVideosFound}  {L["reportPass"]}: {s.VideosPassed}  {L["reportWarning"]}: {s.VideosWarning}  {L["reportFail"]}: {s.VideosFailed}");
        sb.AppendLine($"{L["reportSessionDuration"]}: {s.SessionDurationMatch}");
        sb.AppendLine($"{L["reportFpsSpread"]}: {s.FpsSpreadDisplay}");
        sb.AppendLine($"{L["reportScientificTimingStatus"]}: {sess.ScientificTimingStatus}");
        sb.AppendLine($"Scientific Timing Confidence: {sess.SessionScientificTimingConfidence}");
        sb.AppendLine();
        sb.AppendLine($"--- {L["reportSectionSession"]} ---");
        sb.AppendLine($"{L["reportExpectedCameras"]}: {sess.ExpectedCameras}");
        sb.AppendLine($"{L["reportDetectedVideos"]}: {sess.DetectedVideos}");
        sb.AppendLine($"{L["reportMissingCameraVideos"]}: {sess.MissingCameraVideos}");
        sb.AppendLine($"{L["reportDurationSpread"]}: {sess.DurationSpreadSeconds:F2}s");
        sb.AppendLine($"{L["reportFpsSpread"]}: {sess.FpsSpread:F2}");
        sb.AppendLine($"{L["reportSessionResult"]}: {sess.OverallResult}");
        sb.AppendLine($"Scientific Timing Confidence: {sess.SessionScientificTimingConfidence}");
        if (string.Equals(sess.SessionScientificTimingConfidence, ScientificTimingConfidence.High, StringComparison.OrdinalIgnoreCase))
            sb.AppendLine(ScientificTimingConfidence.HighMessage);
        sb.AppendLine($"sessionTimingMode: {(string.IsNullOrWhiteSpace(sess.SessionTimingMode) ? "-" : sess.SessionTimingMode)}");
        sb.AppendLine($"maxMeasuredFpsDifference: {sess.MaxMeasuredFpsDifference?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine($"maxWallClockDurationDifferenceSec: {sess.MaxWallClockDurationDifferenceSec?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine($"maxStartOffsetSec: {sess.MaxStartOffsetSec?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine($"maxEndOffsetSec: {sess.MaxEndOffsetSec?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine($"maxFrameCountDifference: {sess.MaxFrameCountDifference?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine($"frameCountDifferenceAcceptedBecauseOriginalMode: {sess.FrameCountDifferenceAcceptedBecauseOriginalMode}");
        sb.AppendLine($"{L["reportContainerVsWallClockAvg"]}: {sess.TimestampDriftSeconds:F3}s");
        sb.AppendLine($"{L["reportFrameBasedDuration"]}: {sess.FrameBasedDurationSeconds:F2}s");
        sb.AppendLine($"{L["reportContainerDuration"]}: {sess.ContainerDurationSeconds:F2}s");
        sb.AppendLine($"{L["reportWallDuration"]}: {sess.WallDurationSeconds:F2}s");
        sb.AppendLine($"{L["reportInterCameraFrameDiff"]}: {sess.InterCameraFrameDifference}");
        sb.AppendLine($"{L["reportInterCameraDurationDiff"]}: {sess.InterCameraDurationDifferenceSeconds:F2}s");
        sb.AppendLine();
        sb.AppendLine(L["verifySessionScopeNote"]);
        sb.AppendLine();
        sb.AppendLine($"--- {L["reportSectionVideoTable"]} ---");
        sb.AppendLine(string.Join("\t", GetCsvHeaders()));
        foreach (var row in report.TableRows)
            sb.AppendLine(FormatTableRowTsv(row));
        sb.AppendLine();

        foreach (var v in report.Videos)
        {
            sb.AppendLine($"--- {v.Entry.CameraSlot} / {v.Entry.FileName} [{v.Verdict}] ---");
            sb.AppendLine(v.DetailText);
            sb.AppendLine();
        }

        sb.AppendLine($"--- {L["reportSectionLog"]} ---");
        foreach (var line in report.LogLines)
            sb.AppendLine(line);

        if (string.Equals(_language.CurrentLanguage, "ja", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine(L["reportExportEnglishByDesignNote"]);
        }

        await File.WriteAllTextAsync(path, PrivacySanitizer.SanitizeForOutput(sb.ToString()), Encoding.UTF8);
    }

    public async Task ExportVideoAuditReportAsync(VerificationReport report, string path)
    {
        var L = _language;
        var sb = new StringBuilder();
        var s = report.Summary;
        sb.AppendLine(L["reportAuditTitle"]);
        sb.AppendLine($"{L["reportAppVersion"]}: {report.AppVersion}");
        sb.AppendLine($"{L["reportGeneratedUtc"]}: {report.VerifiedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{L["reportFolder"]}: {s.SelectedFolder}");
        sb.AppendLine($"{L["reportSessionsAudited"]}: {report.SessionAudits.Count}");
        sb.AppendLine($"{L["reportVideosAudited"]}: {s.TotalVideosFound}");
        sb.AppendLine();
        sb.AppendLine($"=== {L["reportOverallSummary"]} ===");
        sb.AppendLine($"{L["reportOverallVerdict"]}: {s.OverallVerdict}");
        sb.AppendLine(string.Format(L["reportPassCounts"], s.VideosPassed, s.VideosWarning, s.VideosFailed));
        sb.AppendLine();
        sb.AppendLine(L["verifySessionScopeNote"]);
        sb.AppendLine();
        sb.AppendLine($"=== {L["reportSessionSummary"]} ===");
        sb.AppendLine(L["reportSessionSummaryHeader"]);
        foreach (var session in report.SessionAudits)
        {
            sb.AppendLine(string.Join("\t",
                session.SessionLabel,
                session.SessionStatus,
                string.Join(",", session.CamerasFound),
                session.InterCameraFrameDifference?.ToString() ?? "-",
                session.StartOffsetMs?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                session.TotalQueueDrops,
                session.TotalDuplicates));
        }
        sb.AppendLine();

        foreach (var session in report.SessionAudits)
        {
            sb.AppendLine($"=== {string.Format(L["reportSessionBlockTitle"], session.SessionLabel)} ===");
            sb.AppendLine($"  {L["reportSessionFolder"]}: {session.SessionFolder}");
            sb.AppendLine($"  Recording mode: {DisplayTimingMode(session.SessionTimingMode)}");
            if (session.FrameCountDifferenceAcceptedBecauseOriginalMode)
                sb.AppendLine($"  {OriginalCaptureAuditPolicy.SessionInterpretation}");
            sb.AppendLine($"  {L["reportCamerasFound"]}: {string.Join(", ", session.CamerasFound)}");
            sb.AppendLine($"  {L["reportSessionStatus"]}: {session.SessionStatus}");
            sb.AppendLine(session.InterCameraFrameDifference.HasValue
                ? $"  {string.Format(L["reportInterCameraFrameDiffFrames"], session.InterCameraFrameDifference)}"
                : $"  {L["reportInterCameraFrameDiffNa"]}");
            sb.AppendLine(session.StartOffsetMs.HasValue
                ? $"  {string.Format(L["reportStartOffset"], session.StartOffsetMs.Value.ToString("F1", CultureInfo.InvariantCulture))}"
                : $"  {L["reportStartOffsetNa"]}");
            sb.AppendLine(session.StopOffsetMs.HasValue
                ? $"  {string.Format(L["reportStopOffset"], session.StopOffsetMs.Value.ToString("F1", CultureInfo.InvariantCulture))}"
                : $"  {L["reportStopOffsetNa"]}");
            sb.AppendLine($"  maxMeasuredFpsDifference: {session.MaxMeasuredFpsDifference?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
            sb.AppendLine($"  maxWallClockDurationDifferenceSec: {session.MaxWallClockDurationDifferenceSec?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
            sb.AppendLine($"  maxStartOffsetSec: {session.MaxStartOffsetSec?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
            sb.AppendLine($"  maxEndOffsetSec: {session.MaxEndOffsetSec?.ToString("F3", CultureInfo.InvariantCulture) ?? "-"}");
            sb.AppendLine($"  maxFrameCountDifference: {session.MaxFrameCountDifference?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            sb.AppendLine($"  frameCountDifferenceAcceptedBecauseOriginalMode: {session.FrameCountDifferenceAcceptedBecauseOriginalMode}");
            sb.AppendLine($"  {L["reportResolutionConsistency"]}: {session.ResolutionConsistency}");
            sb.AppendLine($"  {L["reportCodecConsistency"]}: {session.CodecConsistency}");
            sb.AppendLine($"  {L["reportPixelFormatConsistency"]}: {session.PixelFormatConsistency}");
            sb.AppendLine($"  {L["reportDropsDuplicatesPlaceholders"]}: {session.TotalQueueDrops} / {session.TotalDuplicates} / {session.TotalPlaceholders}");
            sb.AppendLine();
            AppendRecordingResourceDiagnostics(sb, session);

            foreach (var video in session.CameraVideos)
            {
                var probe = video.Probe;
                var meta = video.Metadata;
                sb.AppendLine($"  {video.Entry.CameraSlot}:");
                sb.AppendLine($"    {L["reportFile"]}: {video.Entry.FileName}");
                sb.AppendLine($"    {L["reportStatus"]}: {CameraAuditStatus.FromVideoResult(video)}");
                if (probe != null && probe.Success)
                {
                    sb.AppendLine($"    {L["reportCodec"]}: {probe.VideoCodec}  {L["reportPixFmt"]}: {probe.PixelFormat}");
                    sb.AppendLine($"    {L["reportResolution"]}: {CaptureResolutionPreset.ToLabel(probe.Width, probe.Height)}");
                    sb.AppendLine($"    {L["reportFramesProbeMetadata"]}: {probe.FrameCount} / {meta?.FrameCount}");
                    sb.AppendLine($"    {L["reportContainerFps"]}: {probe.Fps.ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"    {L["reportContainerDurationLabel"]}: {probe.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture)}s");
                    sb.AppendLine($"    {L["reportFileSize"]}: {VerificationReportMapper.FormatBytes(probe.FileSizeBytes)}  {L["reportBitrate"]}: {probe.BitRate}");
                }
                if (meta != null)
                {
                    sb.AppendLine($"    Recording mode: {DisplayTimingMode(ResolveTimingMode(meta))}");
                    sb.AppendLine($"    originalCaptureMode: {meta.OriginalCaptureMode}");
                    if (meta.OriginalCaptureMode)
                        sb.AppendLine("    Original Capture Mode: Real frames only; no duplicates/placeholders. Frame counts may differ because cameras delivered real frames at different measured FPS.");
                    else
                        sb.AppendLine($"    constantFrameCountMode: {meta.ConstantFrameCountMode}");
                    sb.AppendLine($"    requestedFps / writerFps / containerFps: {meta.RequestedFps.ToString("F3", CultureInfo.InvariantCulture)} / {(meta.WriterFps > 0 ? meta.WriterFps : meta.RecordingWriterFps).ToString("F3", CultureInfo.InvariantCulture)} / {meta.ContainerFps.ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"    {L["reportMeasuredCameraFps"]}: {meta.MeasuredCameraFps.ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"    Playback FPS: {(meta.ContainerFps > 0 ? meta.ContainerFps : meta.WriterFps > 0 ? meta.WriterFps : meta.RecordingWriterFps).ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"    Real Capture FPS: {(meta.MeasuredCameraFps > 0 ? meta.MeasuredCameraFps : meta.ActualFps).ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.AppendLine("    Scientific timing source: Timestamp CSV");
                    sb.AppendLine($"    framesCaptured / framesWritten: {meta.FramesCaptured} / {meta.FrameCount}");
                    sb.AppendLine($"    {L["reportWallClockDuration"]}: {meta.WallClockDurationSeconds.ToString("F3", CultureInfo.InvariantCulture)}s");
                    sb.AppendLine($"    {L["reportFrameBasedDurationLabel"]}: {meta.FrameBasedDurationSeconds.ToString("F3", CultureInfo.InvariantCulture)}s");
                    sb.AppendLine($"    capture interval mean/min/max/std ms: {meta.CaptureIntervalMeanMs.ToString("F3", CultureInfo.InvariantCulture)} / {meta.CaptureIntervalMinMs.ToString("F3", CultureInfo.InvariantCulture)} / {meta.CaptureIntervalMaxMs.ToString("F3", CultureInfo.InvariantCulture)} / {meta.CaptureIntervalStdMs.ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"    frameTimestampCsvPath: {meta.FrameTimestampCsvPath}");
                    sb.AppendLine($"    frameTimestampCsvWritten / frameTimestampCsvRowCount: {meta.FrameTimestampCsvWritten} / {meta.FrameTimestampCsvRowCount}");
                    sb.AppendLine($"    {L["reportQueueDropsDuplicatesPlaceholders"]}: {meta.WriterQueueDrops} / {meta.DuplicatedFrames + meta.DuplicateFrames} / {meta.PlaceholderFrames}");
                    sb.AppendLine($"    metadataCompletenessPercent: {video.MetadataCompletenessPercent.ToString("F1", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"    missingRequiredMetadataFields: {(string.IsNullOrWhiteSpace(video.MissingRequiredMetadataFields) ? "-" : video.MissingRequiredMetadataFields)}");
                    sb.AppendLine($"    scientificMetadataComplete: {video.ScientificMetadataComplete}");
                    sb.AppendLine($"    {L["reportScientificTiming"]}: {meta.ScientificTimingStatus} - {meta.ScientificTimingMessage}");
                    sb.AppendLine($"    recommendedAction: {VerificationReportMapper.BuildRecommendation(video, L)}");
                }
                foreach (var warning in video.WarningMessages)
                    sb.AppendLine($"    {L["reportWarningLabel"]}: {warning}");
                foreach (var error in video.ErrorMessages)
                    sb.AppendLine($"    {L["reportErrorLabel"]}: {error}");
                sb.AppendLine();
            }

            if (session.Warnings.Count > 0)
            {
                sb.AppendLine($"  {L["reportWarningsHeading"]}:");
                foreach (var warning in session.Warnings)
                    sb.AppendLine($"    - {warning}");
            }
            if (session.Failures.Count > 0)
            {
                sb.AppendLine($"  {L["reportFailuresHeading"]}:");
                foreach (var failure in session.Failures)
                    sb.AppendLine($"    - {failure}");
            }
            if (session.InterpretationNotes.Count > 0)
            {
                sb.AppendLine($"  {L["reportNotesHeading"]}:");
                foreach (var note in session.InterpretationNotes)
                    sb.AppendLine($"    - {note}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"=== {L["reportInterpretationHeading"]} ===");
        sb.AppendLine($"- {L["reportInterpretation1"]}");
        sb.AppendLine($"- {L["reportInterpretation2"]}");
        sb.AppendLine($"- {L["reportInterpretation3"]}");
        sb.AppendLine($"- {L["reportInterpretation4"]}");
        if (string.Equals(_language.CurrentLanguage, "ja", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine(L["reportExportEnglishByDesignNote"]);
        }

        await File.WriteAllTextAsync(path, PrivacySanitizer.SanitizeForOutput(sb.ToString()), Encoding.UTF8);
    }

    public async Task ExportCsvAsync(VerificationReport report, string path)
    {
        var headers = GetCsvHeaders();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Csv)));
        foreach (var row in report.TableRows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ResultDisplay),
                Csv(row.Camera),
                Csv(row.FileName),
                Csv(row.FilePath),
                Csv(row.MetadataStatus),
                Csv(row.ExpectedResolution),
                Csv(row.ActualResolution),
                Csv(row.ResolutionMatchDisplay),
                Csv(row.ExpectedFps),
                Csv(row.ActualFps),
                Csv(row.FpsMatchDisplay),
                Csv(row.ExpectedDuration),
                Csv(row.ActualDuration),
                Csv(row.DurationMatchDisplay),
                Csv(row.FrameCount),
                Csv(row.ExpectedFrameCount),
                Csv(row.Codec),
                Csv(row.Container),
                Csv(row.FileSize),
                Csv(row.Device),
                Csv(row.RequestedFps),
                Csv(row.WriterFps),
                Csv(row.ContainerFps),
                Csv(row.MeasuredNativeFps),
                Csv(row.FpsStabilityGrade),
                Csv(row.FramesCapturedDisplay),
                Csv(row.FramesWrittenDisplay),
                Csv(row.TimestampRowsDisplay),
                Csv(row.WallDurationDisplay),
                Csv(row.ContainerDurationDisplay),
                Csv(row.ContainerVsWallClockDisplay),
                Csv(row.StartOffsetDisplay),
                Csv(row.ScientificTimingStatusDisplay),
                Csv(row.QueueDropsDisplay),
                Csv(row.DuplicatesDisplay),
                Csv(row.PlaceholdersDisplay),
                Csv(row.MetadataCompletenessPercent),
                Csv(row.MissingRequiredMetadataFields),
                Csv(row.ScientificMetadataComplete.ToString()),
                Csv(row.Details),
                Csv(row.Recommendation)));
        }

        await File.WriteAllTextAsync(path, PrivacySanitizer.SanitizeForOutput(sb.ToString()), Encoding.UTF8);
    }

    public async Task ExportAllToFolderAsync(VerificationReport report, string folder)
    {
        await ExportTextAsync(report, Path.Combine(folder, "verification_report.txt"));
        await ExportVideoAuditReportAsync(report, Path.Combine(folder, "video_audit_report.txt"));
        await ExportJsonAsync(report, Path.Combine(folder, "verification_report.json"));
        await ExportCsvAsync(report, Path.Combine(folder, "verification_report.csv"));
    }

    private static string FormatTableRowTsv(VerificationTableRow row) =>
        string.Join("\t",
            row.ResultDisplay, row.Camera, row.FileName, row.FilePath, row.MetadataStatus,
            row.ExpectedResolution, row.ActualResolution, row.ResolutionMatchDisplay,
            row.ExpectedFps, row.ActualFps, row.FpsMatchDisplay,
            row.ExpectedDuration, row.ActualDuration, row.DurationMatchDisplay,
            row.FrameCount, row.ExpectedFrameCount, row.Codec, row.Container, row.FileSize,
            row.Device, row.RequestedFps, row.WriterFps, row.ContainerFps, row.MeasuredNativeFps,
            row.FpsStabilityGrade, row.FramesCapturedDisplay, row.FramesWrittenDisplay, row.TimestampRowsDisplay,
            row.WallDurationDisplay, row.ContainerDurationDisplay, row.ContainerVsWallClockDisplay,
            row.StartOffsetDisplay, row.ScientificTimingStatusDisplay, row.QueueDropsDisplay,
            row.DuplicatesDisplay, row.PlaceholdersDisplay,
            row.MetadataCompletenessPercent, row.MissingRequiredMetadataFields, row.ScientificMetadataComplete,
            row.Details, row.Recommendation);

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static void AppendRecordingResourceDiagnostics(StringBuilder sb, RecordingSessionAuditResult session)
    {
        var diagnostics = session.CameraVideos
            .Select(v => v.Metadata?.RecordingDiagnostics)
            .Where(d => d != null)
            .ToList();

        sb.AppendLine("  Recording Resource Diagnostics:");
        if (diagnostics.Count == 0)
        {
            sb.AppendLine("    unavailable");
            sb.AppendLine("    Stable measured FPS below requested FPS is acceptable in Original Capture Mode when timestamps are recorded.");
            sb.AppendLine($"    {OriginalCaptureVerificationPolicy.ContainerWallClockNote}");
            sb.AppendLine();
            AppendLikelyBottleneckAndRecommendedAction(sb, session);
            return;
        }

        var sessionDiag = diagnostics[0]!;
        if (!string.IsNullOrWhiteSpace(sessionDiag.SessionVerdictText))
            sb.AppendLine($"    Session verdict: {sessionDiag.SessionVerdictText}");
        sb.AppendLine($"    CPU average/peak: {FormatPercent(sessionDiag.AverageCpuPercent)} / {FormatPercent(sessionDiag.MaxCpuPercent)}");
        sb.AppendLine($"    CPU samples over 90%: {FormatInt(sessionDiag.CpuSamplesOver90Percent)}");
        sb.AppendLine($"    RAM total: {FormatMb(sessionDiag.SystemTotalMemoryMB)}");
        sb.AppendLine($"    Minimum available RAM: {FormatMb(sessionDiag.MinSystemAvailableMemoryMB)}");
        sb.AppendLine($"    Process memory peak: {FormatMb(sessionDiag.MaxProcessMemoryMB)}");
        sb.AppendLine($"    Process memory continuously increases: {sessionDiag.ProcessMemoryContinuouslyIncreases}");
        sb.AppendLine($"    Disk free space: {FormatGb(sessionDiag.MinDiskFreeSpaceGB)}");
        sb.AppendLine($"    Total session file size: {FormatMb(sessionDiag.TotalSessionSizeMB)}");
        sb.AppendLine($"    Estimated GB/hour: {FormatGbPerHour(sessionDiag.EstimatedGBPerHourAllCameras)} all cameras / {FormatGbPerHour(sessionDiag.EstimatedGBPerHourPerCamera)} per camera");
        sb.AppendLine("    Per camera:");
        foreach (var video in session.CameraVideos)
        {
            var meta = video.Metadata;
            var camera = meta?.RecordingDiagnostics?.Camera;
            sb.AppendLine(
                $"      {video.Entry.CameraSlot}: timingVerdict={camera?.TimingVerdict ?? "unavailable"}, " +
                $"requestedFPS={FormatNumber(camera?.RequestedFps)}, measuredFpsByFrameCount={FormatNumber(camera?.MeasuredFpsByFrameCount)}, " +
                $"measuredFpsByValidIntervals={FormatNumber(camera?.MeasuredFpsByIntervals)}, intervalMeanMedianStdMs={FormatMs(camera?.CaptureIntervalMeanMs)}/{FormatMs(camera?.CaptureIntervalMedianMs)}/{FormatMs(camera?.CaptureIntervalStdMs)}, " +
                $"intervalMinMaxP95P99Ms={FormatMs(camera?.CaptureIntervalMinMs)}/{FormatMs(camera?.CaptureIntervalMaxMs)}/{FormatMs(camera?.CaptureIntervalP95Ms)}/{FormatMs(camera?.CaptureIntervalP99Ms)}, " +
                $"framesAcceptedWrittenDiff={FormatLong(ResolveFramesAccepted(meta, camera))}/{FormatLong(ResolveFramesWritten(meta, camera))}/{FormatLong(ResolveAcceptedWrittenDifference(meta, camera))}, " +
                $"totalCapturedWrittenDebug={FormatLong(ResolveFramesCaptured(meta, camera))}/{FormatLong(ResolveFramesWritten(meta, camera))}/{FormatLong(ResolveFramesDifference(meta, camera))}, " +
                $"stopBoundaryCapturedNotRecorded={FormatLong(camera?.FramesCapturedAfterStopRequested)}/{FormatLong(camera?.FramesNotRecordedAfterStopRequested)}, " +
                $"droppedBeforeEnqueue={FormatLong(camera?.FramesDroppedBeforeEnqueue)}, finalFlushCompleted={camera?.FinalFlushCompleted.ToString() ?? "unavailable"}, finalFlushTimedOut={camera?.FinalFlushTimedOut.ToString() ?? "unavailable"}, writerReleased={camera?.WriterReleasedSuccessfully.ToString() ?? "unavailable"}, " +
                $"writerQueueDrops={FormatLong(camera?.WriterQueueDrops ?? meta?.WriterQueueDrops)}, writerQueueMaxDepthCapacity={FormatInt(camera?.WriterQueueMaxDepth ?? camera?.MaxWriterQueueMaxDepth)}/{FormatInt(camera?.WriterQueueCapacity)}, " +
                $"writerWriteMeanMaxMs={FormatMs(camera?.WriterWriteMeanMs ?? camera?.MaxWriterWriteMeanMs)}/{FormatMs(camera?.WriterWriteMaxMs ?? camera?.MaxWriterWriteMaxMs)}, " +
                $"finalFileSizeMB={FormatNumber(camera?.FinalFileSizeMB)}, estimatedGBPerHour={FormatGbPerHour(camera?.EstimatedGBPerHour)}, " +
                $"autofocus=autoSupported:{camera?.AutoFocusSupported ?? "unavailable"} autoEnabled:{camera?.AutoFocusEnabled ?? "unavailable"} manualSupported:{camera?.ManualFocusSupported ?? "unavailable"} manualValue:{camera?.ManualFocusValue ?? "unavailable"}, " +
                $"focusControl=requestedAuto:{meta?.AutoFocusRequested.ToString() ?? "unavailable"} attempted:{meta?.AutoFocusApplyAttempted.ToString() ?? "unavailable"} confirmed:{FormatBool(meta?.AutoFocusApplySucceeded)} autoReadback:{meta?.AutoFocusReadbackValue ?? "unavailable"} manualSupported:{FormatBool(meta?.ManualFocusSupported)} manualRequested:{FormatNumber(meta?.ManualFocusRequestedValue)} manualReadback:{meta?.ManualFocusReadbackValue ?? "unavailable"} mode:{meta?.FocusControlMode ?? "unavailable"}");
            if (meta is { AutoFocusRequested: false } && meta.AutoFocusApplySucceeded != true)
                sb.AppendLine("      Focus warning: autofocus OFF was requested but not confirmed.");
            if (!string.IsNullOrWhiteSpace(meta?.FocusWarning))
                sb.AppendLine($"      {meta.FocusWarning}");
        }
        if (!string.IsNullOrWhiteSpace(sessionDiag.ArtifactNote))
            sb.AppendLine($"    {sessionDiag.ArtifactNote}");
        sb.AppendLine("    Stable measured FPS below requested FPS is acceptable in Original Capture Mode when timestamps are recorded.");
        sb.AppendLine($"    {OriginalCaptureVerificationPolicy.ContainerWallClockNote}");
        sb.AppendLine();
        AppendLikelyBottleneckAndRecommendedAction(sb, session);
    }

    private static void AppendLikelyBottleneckAndRecommendedAction(StringBuilder sb, RecordingSessionAuditResult session)
    {
        var cameras = session.CameraVideos
            .Select(v => v.Metadata)
            .Where(m => m != null)
            .Cast<CameraMetadataRecord>()
            .ToList();

        sb.AppendLine("  Likely Bottleneck:");
        foreach (var note in RecordingDiagnosticsInterpreter.BuildLikelyBottlenecks(cameras))
            sb.AppendLine($"    {note}");
        sb.AppendLine();

        sb.AppendLine("  Recommended Action:");
        foreach (var action in RecordingDiagnosticsInterpreter.BuildRecommendedActions(cameras))
            sb.AppendLine($"    {action}");
        sb.AppendLine();
    }

    private static string FormatPercent(double? value) =>
        value.HasValue ? $"{value.Value:F1}%" : "unavailable";

    private static string FormatMb(double? value) =>
        value.HasValue ? $"{value.Value:F1} MB" : "unavailable";

    private static string FormatGb(double? value) =>
        value.HasValue ? $"{value.Value:F2} GB" : "unavailable";

    private static string FormatGbPerHour(double? value) =>
        value.HasValue ? $"{value.Value:F2} GB/hour" : "unavailable";

    private static string FormatMs(double? value) =>
        value.HasValue ? $"{value.Value:F3}" : "unavailable";

    private static string FormatInt(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "unavailable";

    private static string FormatLong(long? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "unavailable";

    private static string FormatNumber(double? value) =>
        value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : "unavailable";

    private static string FormatBool(bool? value) =>
        value.HasValue ? value.Value.ToString() : "unavailable";

    private static long ResolveFramesCaptured(CameraMetadataRecord? meta, RecordingDiagnosticsCameraSummary? camera) =>
        camera?.FramesCapturedTotal > 0 ? camera.FramesCapturedTotal : camera?.FramesCaptured > 0 ? camera.FramesCaptured : meta?.FramesCaptured ?? 0;

    private static long ResolveFramesAccepted(CameraMetadataRecord? meta, RecordingDiagnosticsCameraSummary? camera) =>
        camera?.FramesAcceptedForRecording > 0 ? camera.FramesAcceptedForRecording : meta?.FrameTimestampCsvRowCount > 0 ? meta.FrameTimestampCsvRowCount : meta?.FrameCount ?? 0;

    private static long ResolveFramesWritten(CameraMetadataRecord? meta, RecordingDiagnosticsCameraSummary? camera) =>
        camera?.FramesWritten > 0 ? camera.FramesWritten : meta?.FrameCount ?? 0;

    private static long ResolveAcceptedWrittenDifference(CameraMetadataRecord? meta, RecordingDiagnosticsCameraSummary? camera) =>
        ResolveFramesAccepted(meta, camera) - ResolveFramesWritten(meta, camera);

    private static long ResolveFramesDifference(CameraMetadataRecord? meta, RecordingDiagnosticsCameraSummary? camera)
    {
        if (camera != null && (camera.FramesCaptured > 0 || camera.FramesWritten > 0))
            return camera.FramesCapturedMinusWritten;
        return (meta?.FramesCaptured ?? 0) - (meta?.FrameCount ?? 0);
    }

    private static string ResolveTimingMode(CameraMetadataRecord meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.RecordingTimingMode))
            return meta.RecordingTimingMode;
        if ((meta.DuplicatedFrames + meta.DuplicateFrames) > 0 || meta.ConstantFrameCountMode)
            return OriginalCaptureAuditPolicy.LegacyConstantFrameCountMode;
        return "-";
    }

    private static string DisplayTimingMode(string? value) =>
        string.Equals(value, OriginalCaptureAuditPolicy.Mode, StringComparison.OrdinalIgnoreCase)
            ? "Original Capture Mode"
            : string.IsNullOrWhiteSpace(value) ? "-" : value;
}
