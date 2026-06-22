////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Text;
using MultiCamApp.Capture;
using MultiCamApp.Localization;
using MultiCamApp.Metadata;
using MultiCamApp.Recording;

namespace MultiCamApp.Verification;

public static class VerificationReportMapper
{
    public static VerificationTableRow ToTableRow(
        VideoVerificationResult v,
        string settingsSource,
        LanguageManager? lang = null)
    {
        var entry = v.Entry;
        var row = new VerificationTableRow
        {
            Result = v.Verdict,
            Camera = entry.CameraSlot,
            FileName = entry.FileName,
            FilePath = entry.FullPath,
            CameraFolder = Path.GetDirectoryName(entry.FullPath) ?? "",
            MetadataPath = entry.MetadataPath ?? "",
            MetadataStatus = MetadataDisplayHelper.MetadataStatus(lang ?? EnglishFallback, entry.MetadataFound),
            ExpectedResolution = CaptureResolutionPreset.FormatDisplayLabel(v.ExpectedResolutionDisplay),
            ActualResolution = CaptureResolutionPreset.FormatDisplayLabel(v.ActualResolutionDisplay),
            ResolutionMatch = v.ResolutionMatch,
            ExpectedFps = v.ExpectedFpsDisplay,
            ActualFps = v.ActualFpsDisplay,
            FpsMatch = v.FpsMatch,
            ExpectedDuration = v.ExpectedDurationDisplay,
            ActualDuration = v.DurationDisplay,
            DurationMatch = v.DurationMatch,
            ExpectedFrameCount = v.ExpectedFrameCountDisplay,
            FrameCount = v.FrameCountDisplay,
            Codec = v.CodecDisplay,
            Container = v.ContainerDisplay,
            FileSize = v.FileSizeDisplay,
            ExpectedSettingsSource = settingsSource,
            Details = BuildDetailsShort(v, lang),
            Recommendation = v.Recommendation,
            DetailText = v.DetailText
        };

        if (v.Experiment?.IsExperimentSession == true)
        {
            var e = v.Experiment;
            row.IsExperiment = true;
            row.ExpectedFrameCount = e.ExpectedFrames.ToString();
            row.FrameCount = e.MetadataFrames.ToString();
            row.ExperimentExpectedFrames = e.ExpectedFrames.ToString();
            row.ExperimentFrameDifference = e.FrameDifference.ToString();
            row.ExperimentDroppedFrames = e.DroppedFrames.ToString();
            row.ExperimentDuplicateFrames = e.DuplicateFrames.ToString();
            row.ExperimentDurationError = $"{e.DurationErrorSeconds:F2}s";
            row.ExperimentMeanInterval = $"{e.AverageFrameIntervalMs:F2} ms";
            row.ExperimentIntervalSd = $"{e.FrameIntervalStdDevMs:F2} ms";
            row.ExpectedFps = $"{e.ExpectedFps:F1}";
            row.ActualFps = $"{e.ActualFps:F1}";
            row.ExpectedDuration = $"{e.ExpectedDurationSeconds:F1}s";
            row.ActualDuration = $"{e.ActualDurationSeconds:F2}s";
            row.Details = e.Verdict == VerificationVerdict.Pass
                ? $"Experiment OK — {e.MetadataFrames}/{e.ExpectedFrames} frames"
                : $"Experiment {e.Verdict} — diff {e.FrameDifference} frames, dropped {e.DroppedFrames}";
        }

        row.WarningMessages.AddRange(v.WarningMessages);
        row.ErrorMessages.AddRange(v.ErrorMessages);
        row.SessionLabel = v.Entry.SessionLabel;
        row.SessionFolder = v.Entry.SessionFolder;
        row.AuditStatus = CameraAuditStatus.FromVideoResult(v);
        row.ScientificTimingStatusDisplay = v.ScientificTimingStatus;
        row.MetadataCompletenessPercent = v.Metadata == null ? "-" : $"{v.MetadataCompletenessPercent:F1}%";
        row.MissingRequiredMetadataFields = string.IsNullOrWhiteSpace(v.MissingRequiredMetadataFields)
            ? "-"
            : v.MissingRequiredMetadataFields;
        row.ScientificMetadataComplete = v.ScientificMetadataComplete;
        row.Device = v.Metadata?.CameraDeviceName
            ?? (!string.IsNullOrWhiteSpace(v.Metadata?.DeviceId) ? v.Metadata!.DeviceId! : "-");
        row.RequestedFps = v.Metadata?.RequestedFps > 0 ? $"{v.Metadata.RequestedFps:F3}" : v.ExpectedFpsDisplay;
        row.WriterFps = v.Metadata is { } wm
            ? $"{(wm.WriterFps > 0 ? wm.WriterFps : wm.RecordingWriterFps):F3}"
            : "-";
        row.ContainerFps = v.Metadata?.ContainerFps > 0
            ? $"{v.Metadata.ContainerFps:F3}"
            : v.Probe?.Fps > 0 ? $"{v.Probe.Fps:F3}" : "-";
        row.MeasuredCameraFpsDisplay = v.Metadata?.MeasuredCameraFps > 0
            ? $"{v.Metadata.MeasuredCameraFps:F3}"
            : v.Metadata?.ActualFps > 0 ? $"{v.Metadata.ActualFps:F3}" : "-";
        row.MeasuredNativeFps = row.MeasuredCameraFpsDisplay;
        row.FpsStabilityGrade = string.IsNullOrWhiteSpace(v.Metadata?.FpsStabilityGrade)
            ? "-"
            : v.Metadata!.FpsStabilityGrade;
        row.FramesCapturedDisplay = v.Metadata?.FramesCaptured > 0 ? v.Metadata.FramesCaptured.ToString() : "-";
        row.FramesWrittenDisplay = v.Metadata?.FrameCount > 0 ? v.Metadata.FrameCount.ToString() : v.Probe?.FrameCount?.ToString() ?? "-";
        row.TimestampRowsDisplay = v.Metadata == null
            ? "-"
            : v.Metadata.FrameTimestampCsvWritten
                ? $"{v.Metadata.FrameTimestampCsvRowCount}"
                : "MISSING";
        row.TimingSourceDisplay = string.IsNullOrWhiteSpace(v.Metadata?.TrimRecommendedTimeSource)
            ? "-"
            : v.Metadata!.TrimRecommendedTimeSource;
        row.WallDurationDisplay = v.WallDurationSeconds is > 0
            ? $"{v.WallDurationSeconds:F2}s"
            : "-";
        row.ContainerDurationDisplay = v.ContainerDurationSeconds is > 0 ? $"{v.ContainerDurationSeconds:F2}s" : "-";
        row.ContainerVsWallClockDisplay = v.ContainerVsWallClockDifferenceSeconds.HasValue
            ? $"{v.ContainerVsWallClockDifferenceSeconds.Value:F3}s"
            : "-";
        row.StartOffsetDisplay = v.Metadata?.InterCameraStartOffsetMs > 0
            ? $"{v.Metadata.InterCameraStartOffsetMs:F1} ms"
            : "-";
        row.QueueDropsDisplay = v.Metadata?.WriterQueueDrops.ToString() ?? "-";
        row.CaptureIntervalMeanMinMaxStdDisplay = v.Metadata == null
            ? "-"
            : $"{v.Metadata.CaptureIntervalMeanMs:F3} / {v.Metadata.CaptureIntervalMinMs:F3} / {v.Metadata.CaptureIntervalMaxMs:F3} / {v.Metadata.CaptureIntervalStdMs:F3}";
        row.CaptureIntervalP95P99Display = v.Metadata == null
            ? "-"
            : $"{v.Metadata.CaptureIntervalP95Ms:F3} / {v.Metadata.CaptureIntervalP99Ms:F3}";
        row.CaptureGapCountsDisplay = v.Metadata == null
            ? "-"
            : $"{v.Metadata.LongGapCount} / {v.Metadata.ShortGapCount} / {v.Metadata.SevereLongGapCount}";
        row.DuplicatesDisplay = v.Metadata == null
            ? "-"
            : (v.Metadata.DuplicatedFrames + v.Metadata.DuplicateFrames).ToString();
        row.PlaceholdersDisplay = v.Metadata?.PlaceholderFrames.ToString() ?? "-";
        row.RecommendedAction = v.Recommendation;
        return row;
    }

    public static string BuildDetailsShort(VideoVerificationResult v, LanguageManager? lang = null)
    {
        var ok = T(lang, "verifyDetailsOk", "OK");
        var seePanel = T(lang, "verifyDetailsSeePanel", "See detail panel");
        if (v.Verdict == VerificationVerdict.Pass && v.Messages.Count == 0)
            return ok;
        var issue = v.Messages.FirstOrDefault(m =>
            !m.Contains("optional", StringComparison.OrdinalIgnoreCase)
            && !m.Contains("Bitrate not", StringComparison.OrdinalIgnoreCase));
        if (issue != null)
        {
            if (issue.Contains("Duration vs metadata", StringComparison.OrdinalIgnoreCase))
            {
                var off = ExtractOffBySeconds(issue);
                return off.HasValue
                    ? Tf(lang, "verifyDetailsMetadataDurationDiff", "Metadata duration differs by {0:F2}s", off.Value)
                    : issue;
            }
            if (issue.Contains("Frame count unavailable", StringComparison.OrdinalIgnoreCase))
                return T(lang, "verifyDetailsFrameCountUnavailable", "Frame count unavailable");
            if (issue.Contains("Codec", StringComparison.OrdinalIgnoreCase))
                return T(lang, "verifyDetailsCodecDiff", "Codec differs from expected");
            if (issue.Length > 72) return issue[..69] + "…";
            return issue;
        }
        return v.Verdict == VerificationVerdict.Pass ? ok : seePanel;
    }

    public static void BuildDetailAndRecommendation(
        VideoVerificationResult v,
        string settingsSource,
        LanguageManager? lang = null)
    {
        var l = lang ?? EnglishFallback;
        var sb = new StringBuilder();
        sb.AppendLine($"{T(lang, "metaDetailCamera", "Camera")}: {v.Entry.CameraSlot}");
        sb.AppendLine($"{T(lang, "metaDetailResult", "Result")}: {v.Verdict.ToString().ToUpperInvariant()}");
        sb.AppendLine($"{T(lang, "metaDetailFile", "File")}: {v.Entry.FullPath}");
        sb.AppendLine($"{T(lang, "metaDetailCameraFolder", "Camera folder")}: {Path.GetDirectoryName(v.Entry.FullPath)}");
        sb.AppendLine($"{T(lang, "metaDetailMetadataPath", "Metadata")}: {(v.Entry.MetadataFound ? v.Entry.MetadataPath : T(lang, "metaDetailMetadataNotFound", "not found"))}");
        sb.AppendLine($"{T(lang, "metaDetailExpectedSettingsSource", "Expected settings source")}: {settingsSource}");
        sb.AppendLine();

        if (v.Probe != null && v.Probe.Success)
        {
            sb.AppendLine($"{T(lang, "metaSectionFfprobe", "ffprobe")}:");
            sb.AppendLine($"  {T(lang, "metaFieldResolution", "Resolution")}: {CaptureResolutionPreset.ToLabel(v.Probe.Width, v.Probe.Height)}");
            sb.AppendLine($"  {T(lang, "metaFieldFps", "FPS")}: {v.Probe.Fps:F2}");
            sb.AppendLine($"  {T(lang, "metaFieldConstantFps", "Constant FPS")}: {YesNo(lang, v.Probe.ConstantFps)}");
            sb.AppendLine($"  {T(lang, "metaFieldDuration", "Duration")}: {v.Probe.DurationSeconds:F2}s");
            sb.AppendLine($"  {T(lang, "metaFieldFrames", "Frames")}: {v.Probe.FrameCount?.ToString() ?? T(lang, "metaValueNotAvailable", "n/a")}");
            sb.AppendLine($"  {T(lang, "metaFieldCodec", "Codec")}: {v.Probe.VideoCodec ?? "-"}");
            sb.AppendLine($"  {T(lang, "metaFieldContainer", "Container")}: {v.Probe.ContainerFormat ?? "-"}");
            sb.AppendLine($"  {T(lang, "metaFieldFileSize", "File size")}: {FormatBytes(v.Probe.FileSizeBytes)}");
            sb.AppendLine();
        }

        if (v.Metadata != null)
        {
            var m = v.Metadata;
            var requested = CaptureResolutionPreset.FormatDisplayLabel(m.RequestedResolution);
            var selected = CaptureResolutionPreset.FormatDisplayLabel(
                m.SelectedResolution ?? m.Resolution, m.PixelWidth, m.PixelHeight);
            if (requested != "-" || selected != "-")
            {
                sb.AppendLine($"{T(lang, "metaSectionMetadata", "Metadata")}:");
                if (requested != "-")
                    sb.AppendLine($"  {T(lang, "metaFieldRequestedResolution", "Requested resolution")}: {requested}");
                if (selected != "-" && !string.Equals(selected, requested, StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"  {T(lang, "metaFieldSelectedResolution", "Selected resolution")}: {selected}");
                else if (selected != "-" && requested == "-")
                    sb.AppendLine($"  {T(lang, "metaFieldResolution", "Resolution")}: {selected}");
                sb.AppendLine();
            }

            var measuredCameraFps = m.MeasuredCameraFps > 0 ? m.MeasuredCameraFps : m.ActualFps;
            var containerFps = m.ContainerFps > 0 ? m.ContainerFps : m.RecordingWriterFps;
            sb.AppendLine($"{T(lang, "metaSectionTiming", "Timing")}:");
            sb.AppendLine($"  recordingTimingMode: {ResolveTimingMode(m)}");
            sb.AppendLine($"  originalCaptureMode: {m.OriginalCaptureMode}");
            if (m.OriginalCaptureMode)
                sb.AppendLine($"  {OriginalCaptureAuditPolicy.SessionInterpretation}");
            else
                sb.AppendLine($"  constantFrameCountMode: {m.ConstantFrameCountMode}");
            sb.AppendLine($"  {T(lang, "metaFieldRequestedFps", "Requested FPS")}: {m.RequestedFps:F3}");
            sb.AppendLine($"  {T(lang, "metaFieldSelectedDeviceFps", "Selected device FPS")}: {m.SelectedDeviceFps:F3}");
            sb.AppendLine($"  {T(lang, "metaFieldWriterFps", "Writer FPS")}: {(m.WriterFps > 0 ? m.WriterFps : m.RecordingWriterFps):F3}");
            sb.AppendLine($"  Playback FPS: {containerFps:F3}");
            sb.AppendLine($"  Real Capture FPS: {measuredCameraFps:F3}");
            sb.AppendLine($"  Scientific timing FPS: {measuredCameraFps:F3}");
            sb.AppendLine($"  Scientific timing source: Timestamp CSV");
            sb.AppendLine($"  {T(lang, "metaFieldFrameBasedDuration", "Frame-based duration")}: {(v.FrameBasedDurationSeconds.HasValue ? $"{v.FrameBasedDurationSeconds.Value:F2}s" : "-")}");
            sb.AppendLine($"  {T(lang, "metaFieldContainerDuration", "Container duration")}: {(v.ContainerDurationSeconds.HasValue ? $"{v.ContainerDurationSeconds.Value:F2}s" : "-")}");
            sb.AppendLine($"  {T(lang, "metaFieldWallClockDuration", "Wall-clock duration")}: {(v.WallDurationSeconds.HasValue ? $"{v.WallDurationSeconds.Value:F2}s" : "-")}");
            sb.AppendLine($"  {T(lang, "metaFieldContainerVsWallClock", "Container vs wall-clock difference")}: {(v.ContainerVsWallClockDifferenceSeconds.HasValue ? $"{v.ContainerVsWallClockDifferenceSeconds.Value:F3}s" : "-")}");
            sb.AppendLine($"  {T(lang, "metaFieldFramesWrittenCaptured", "Frames written/captured")}: {m.FrameCount} / {m.FramesCaptured}");
            sb.AppendLine($"  duplicateFrames / placeholderFrames / writerQueueDrops: {m.DuplicatedFrames + m.DuplicateFrames} / {m.PlaceholderFrames} / {m.WriterQueueDrops}");
            sb.AppendLine($"  {T(lang, "metaFieldCaptureIntervalCount", "Capture interval count")}: {FormatIntervalCount(l, m.CaptureIntervalCount, m.CaptureIntervalStatsMessage)}");
            sb.AppendLine($"  {T(lang, "metaFieldCaptureIntervalMs", "Capture interval ms (mean/min/max/std)")}: " +
                $"{FormatIntervalMs(l, m.CaptureIntervalMeanMs, m.CaptureIntervalCount, m.CaptureIntervalStatsMessage)} / " +
                $"{FormatIntervalMs(l, m.CaptureIntervalMinMs, m.CaptureIntervalCount, m.CaptureIntervalStatsMessage)} / " +
                $"{FormatIntervalMs(l, m.CaptureIntervalMaxMs, m.CaptureIntervalCount, m.CaptureIntervalStatsMessage)} / " +
                $"{FormatIntervalMs(l, m.CaptureIntervalStdMs, m.CaptureIntervalCount, m.CaptureIntervalStatsMessage)}");
            if (!string.IsNullOrWhiteSpace(m.FpsStabilityGrade))
            {
                sb.AppendLine($"  FPS stability grade: {m.FpsStabilityGrade}");
                sb.AppendLine($"  measuredCameraFpsFromFirstLastFrame / meanInterval: {m.MeasuredCameraFpsFromFirstLastFrame:F6} / {m.MeasuredCameraFpsFromMeanInterval:F6}");
                sb.AppendLine($"  expectedIntervalMs / requestedExpectedIntervalMs: {m.ExpectedIntervalMs:F6} / {m.RequestedExpectedIntervalMs:F6}");
                sb.AppendLine($"  interval median/p95/p99 ms: {m.CaptureIntervalMedianMs:F6} / {m.CaptureIntervalP95Ms:F6} / {m.CaptureIntervalP99Ms:F6}");
                sb.AppendLine($"  interval error mean/absMean ms: {m.MeanIntervalErrorMs:F6} / {m.AbsoluteMeanIntervalErrorMs:F6}");
                sb.AppendLine($"  long/short/severeLong gaps: {m.LongGapCount} / {m.ShortGapCount} / {m.SevereLongGapCount}");
                sb.AppendLine($"  jitterScoreMs: {m.JitterScoreMs:F6}");
            }
            var captureNote = MetadataDisplayHelper.LocalizeCaptureIntervalNote(
                l,
                CaptureIntervalMetadataFormatter.DescribeAvailability(m.CaptureIntervalCount, m.CaptureIntervalStatsMessage));
            if (!string.IsNullOrWhiteSpace(captureNote))
                sb.AppendLine($"  {T(lang, "metaFieldCaptureIntervalNote", "Capture interval note")}: {captureNote}");
            sb.AppendLine($"  {T(lang, "metaFieldScientificTimingStatus", "Scientific timing status")}: {v.ScientificTimingStatus}");
            sb.AppendLine($"  metadataCompletenessPercent: {v.MetadataCompletenessPercent:F1}");
            sb.AppendLine($"  missingRequiredMetadataFields: {(string.IsNullOrWhiteSpace(v.MissingRequiredMetadataFields) ? "-" : v.MissingRequiredMetadataFields)}");
            sb.AppendLine($"  scientificMetadataComplete: {v.ScientificMetadataComplete}");
            if (!string.IsNullOrWhiteSpace(v.ScientificTimingMessage))
            {
                sb.AppendLine($"  {T(lang, "metaFieldScientificTimingMessage", "Scientific timing message")}: " +
                    MetadataDisplayHelper.LocalizeScientificTimingMessage(l, v.ScientificTimingMessage));
            }
            sb.AppendLine($"  {T(lang, "metaFieldTimingStatus", "Timing status")}: {v.TimingStatusDisplay}");
            sb.AppendLine();
            AppendRecordingResourceDiagnostics(sb, m);
        }

        sb.AppendLine($"{T(lang, "metaSectionComparison", "Comparison")}:");
        sb.AppendLine($"  {T(lang, "metaFieldResolution", "Resolution")}: {T(lang, "metaFieldExpected", "expected")} {v.ExpectedResolutionDisplay}, {T(lang, "metaFieldActual", "actual")} {v.ActualResolutionDisplay} ({VerificationTableRow.MatchLabel(v.ResolutionMatch)})");
        sb.AppendLine($"  {T(lang, "metaFieldFps", "FPS")}: {T(lang, "metaFieldExpected", "expected")} {v.ExpectedFpsDisplay}, {T(lang, "metaFieldActual", "actual")} {v.ActualFpsDisplay} ({VerificationTableRow.MatchLabel(v.FpsMatch)})");
        sb.AppendLine($"  {T(lang, "metaFieldDuration", "Duration")}: {T(lang, "metaFieldExpected", "expected")} {v.ExpectedDurationDisplay}, {T(lang, "metaFieldActual", "actual")} {v.DurationDisplay} ({VerificationTableRow.MatchLabel(v.DurationMatch)})");
        sb.AppendLine($"  {T(lang, "metaFieldFrames", "Frames")}: {T(lang, "metaFieldExpected", "expected")} {v.ExpectedFrameCountDisplay}, {T(lang, "metaFieldActual", "actual")} {v.FrameCountDisplay}");
        sb.AppendLine();

        if (v.WarningMessages.Count > 0)
        {
            sb.AppendLine($"{T(lang, "metaSectionWarnings", "Warnings")}:");
            foreach (var w in v.WarningMessages) sb.AppendLine($"  - {w}");
            sb.AppendLine();
        }

        if (v.ErrorMessages.Count > 0)
        {
            sb.AppendLine($"{T(lang, "metaSectionFailures", "Failures")}:");
            foreach (var e in v.ErrorMessages) sb.AppendLine($"  - {e}");
            sb.AppendLine();
        }

        if (v.Experiment?.IsExperimentSession == true)
        {
            var e = v.Experiment;
            sb.AppendLine($"{T(lang, "metaSectionExperiment", "Experiment Mode (strict)")}:");
            sb.AppendLine($"  {T(lang, "metaDetailResult", "Result")}: {e.Verdict}");
            sb.AppendLine($"  Expected frames: {e.ExpectedFrames}");
            sb.AppendLine($"  Metadata frames: {e.MetadataFrames}");
            sb.AppendLine($"  ffprobe frames: {e.ProbeFrames?.ToString() ?? T(lang, "metaValueNotAvailable", "n/a")}");
            sb.AppendLine($"  Frame difference: {e.FrameDifference}");
            sb.AppendLine($"  Expected FPS: {e.ExpectedFps:F2}  Actual: {e.ActualFps:F2}");
            sb.AppendLine($"  Expected duration: {e.ExpectedDurationSeconds:F2}s  Actual: {e.ActualDurationSeconds:F2}s");
            sb.AppendLine($"  Duration error: {e.DurationErrorSeconds:F2}s");
            sb.AppendLine($"  Dropped frames: {e.DroppedFrames}");
            sb.AppendLine($"  Duplicate frames: {e.DuplicateFrames}");
            sb.AppendLine($"  Constant frame count mode: {e.ConstantFrameCountMode}");
            sb.AppendLine($"  Mean frame interval: {e.AverageFrameIntervalMs:F3} ms  SD: {e.FrameIntervalStdDevMs:F3} ms");
            sb.AppendLine($"  Recording experiment result: {e.ExperimentResult}");
            sb.AppendLine();
        }

        if (v.Messages.Count > 0)
        {
            sb.AppendLine($"{T(lang, "metaSectionReason", "Reason")}:");
            foreach (var m in v.Messages) sb.AppendLine($"  - {m}");
            sb.AppendLine();
        }

        sb.AppendLine($"{T(lang, "metaSectionRecommendation", "Recommendation")}:");
        sb.AppendLine($"  {v.Recommendation}");
        sb.AppendLine();
        sb.AppendLine($"{T(lang, "metaSectionTimingGuidance", "Timing guidance")}:");
        sb.AppendLine($"  {T(lang, "metaTimingGuidance1", "Video is valid for frame-based analysis when scientific timing status is PASS or PASS_WITH_WARNING.")}");
        sb.AppendLine($"  {T(lang, "metaTimingGuidance2", "Container duration (ffprobe) is frame-based; wall-clock duration is the real recording time.")}");
        sb.AppendLine($"  {T(lang, "metaTimingGuidance3", "Container vs wall-clock difference is expected when camera delivery (~29 fps) differs from the 30 fps MP4 tag.")}");
        sb.AppendLine($"  {T(lang, "metaTimingGuidance4", "Use frame count and wall-clock/monotonic timing for scientific analysis, not ffprobe duration alone.")}");
        v.DetailText = sb.ToString().TrimEnd();
    }

    public static string BuildRecommendation(VideoVerificationResult v, LanguageManager? lang = null)
    {
        var l = lang ?? EnglishFallback;
        if (v.Metadata?.OriginalCaptureMode == true)
        {
            if (!v.ScientificMetadataComplete && !string.IsNullOrWhiteSpace(v.MissingCriticalMetadataFields))
                return $"Original Capture scientific metadata is missing critical timing fields ({v.MissingCriticalMetadataFields}). Re-record before scientific use.";
            if (!v.ScientificMetadataComplete)
                return "Original Capture scientific metadata is incomplete. Review missing fields before scientific use.";
            if (v.Metadata.WriterQueueDrops > 0)
                return "Original Capture Mode recorded writer queue drops. Re-record or reduce camera load before scientific use.";
            if (v.Metadata.PlaceholderFrames > 0)
                return "Original Capture Mode should not contain placeholders. Review metadata and re-record before scientific use.";
            if (v.Metadata.DuplicatedFrames + v.Metadata.DuplicateFrames > 0)
                return "Original Capture Mode expected duplicateFrames=0. Re-record before scientific use.";
            return OriginalCaptureAuditPolicy.SessionInterpretation;
        }

        if (v.Verdict == VerificationVerdict.Fail)
        {
            if (v.ErrorMessages.Any(e => e.Contains("missing", StringComparison.OrdinalIgnoreCase)))
                return T(lang, "verifyRecVideoMissing", "Video file or stream is missing; re-record or copy the file into the session folder.");
            if (v.ResolutionMatch == VerificationMatchStatus.No)
                return T(lang, "verifyRecResolutionMismatch", "Resolution does not match expected settings; check camera mode and metadata.");
            return T(lang, "verifyRecReviewFailures", "Review failure messages and re-record if the file is unusable.");
        }

        if (v.Verdict == VerificationVerdict.Warning)
        {
            if (string.Equals(v.ScientificTimingStatus, "PASS_WITH_WARNING", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(v.ScientificTimingMessage)
                    ? MetadataDisplayHelper.LocalizeScientificTimingMessage(l, v.ScientificTimingMessage)
                    : l["metaScientificTimingDefaultMessage"];
            if (v.DurationMatch is VerificationMatchStatus.Warning or VerificationMatchStatus.No)
                return T(lang, "verifyRecDurationFfmpeg", "Check per-camera metadata writing accuracy; ffprobe duration may differ slightly from writer timing.");
            if (v.FpsMatch == VerificationMatchStatus.Warning)
                return T(lang, "verifyRecFpsOff", "FPS differs from requested rate; confirm capture settings and camera capability.");
            if (!v.Entry.MetadataFound)
                return T(lang, "verifyRecMetadataMissing", "Add or restore metadata.txt for full verification against expected settings.");
            return T(lang, "verifyRecReviewWarnings", "Video is readable; review warnings before using for analysis.");
        }

        if (string.Equals(v.ScientificTimingStatus, "FAIL", StringComparison.OrdinalIgnoreCase))
            return T(lang, "verifyRecTimingFailed", "Timing integrity failed; rely on the frame-based metadata or re-record the session.");

        return T(lang, "verifyRecNoAction", "Video matches expected settings within tolerance; no action required.");
    }

    public static SessionVerificationResult BuildSessionResult(
        VerificationReport report,
        Dictionary<string, ExpectedCameraSettings> expectedBySlot,
        bool multiSession = false)
    {
        var s = report.Summary;
        var sessionCount = report.Videos
            .Select(v => v.Entry.SessionFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var prior = report.SessionResult;
        var session = new SessionVerificationResult
        {
            OverallResult = s.OverallVerdict,
            ExpectedCameras = multiSession
                ? report.Videos.Select(v => (v.Entry.SessionFolder, v.Entry.CameraSlot)).Distinct().Count()
                : expectedBySlot.Count > 0 ? expectedBySlot.Count : report.Videos.Count,
            DetectedVideos = s.TotalVideosFound,
            MinDurationSeconds = s.MinDurationSeconds,
            MaxDurationSeconds = s.MaxDurationSeconds,
            DurationSpreadSeconds = s.DurationSpreadSeconds,
            MinFps = s.MinFps,
            MaxFps = s.MaxFps,
            FpsSpread = s.FpsSpread,
            ScientificTimingStatus = prior.ScientificTimingStatus,
            SessionScientificTimingConfidence = string.IsNullOrWhiteSpace(prior.SessionScientificTimingConfidence)
                ? ScientificTimingConfidence.FromSessionVideos(report.Videos)
                : prior.SessionScientificTimingConfidence,
            SessionTimingMode = prior.SessionTimingMode,
            TimestampDriftSeconds = prior.TimestampDriftSeconds,
            FrameBasedDurationSeconds = prior.FrameBasedDurationSeconds,
            ContainerDurationSeconds = prior.ContainerDurationSeconds,
            WallDurationSeconds = prior.WallDurationSeconds,
            InterCameraFrameDifference = prior.InterCameraFrameDifference,
            InterCameraDurationDifferenceSeconds = prior.InterCameraDurationDifferenceSeconds,
            MaxMeasuredFpsDifference = prior.MaxMeasuredFpsDifference,
            MaxWallClockDurationDifferenceSec = prior.MaxWallClockDurationDifferenceSec,
            MaxStartOffsetSec = prior.MaxStartOffsetSec,
            MaxEndOffsetSec = prior.MaxEndOffsetSec,
            MaxFrameCountDifference = prior.MaxFrameCountDifference,
            FrameCountDifferenceAcceptedBecauseOriginalMode = prior.FrameCountDifferenceAcceptedBecauseOriginalMode
        };
        session.SessionMessages.AddRange(s.SessionMessages);
        if (multiSession && sessionCount > 1)
        {
            session.SessionMessages.Insert(0, $"Verified {sessionCount} session folders under the selected path.");
            if (report.SessionResult.InterCameraFrameDifference.HasValue)
                session.SessionMessages.Add(
                    $"Max inter-camera frame difference within any session: {report.SessionResult.InterCameraFrameDifference} frame(s).");
        }

        var foundSlots = report.Videos.Select(v => v.Entry.CameraSlot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expectedBySlot.Keys.Where(k => !foundSlots.Contains(k)).ToList();
        session.MissingCameraVideos = missing.Count > 0 ? string.Join(", ", missing) : "none";

        return session;
    }

    public static VerificationTableRow ScanPlaceholderRow(VideoFileEntry entry, LanguageManager? lang = null)
    {
        var row = new VerificationTableRow
        {
            Result = VerificationVerdict.NotChecked,
            Camera = entry.CameraSlot,
            FileName = entry.FileName,
            FilePath = entry.FullPath,
            CameraFolder = Path.GetDirectoryName(entry.FullPath) ?? "",
            MetadataPath = entry.MetadataPath ?? "",
            MetadataStatus = MetadataDisplayHelper.MetadataStatus(lang ?? EnglishFallback, entry.MetadataFound),
            Details = T(lang, "verifyNotVerifiedYet", "Not verified yet"),
            SessionLabel = entry.SessionLabel,
            SessionFolder = entry.SessionFolder
        };

        var meta = MetadataParser.LoadCameraMetadata(entry.MetadataJsonPath, entry.MetadataPath);
        if (meta != null)
        {
            var (ew, eh) = VerificationCaptureProfile.ResolveExpectedDimensions(meta);
            if (ew is > 0 && eh is > 0)
                row.ExpectedResolution = CaptureResolutionPreset.ToLabel(ew.Value, eh.Value);
            if (meta.PixelWidth > 0 && meta.PixelHeight > 0)
                row.ActualResolution = CaptureResolutionPreset.ToLabel(meta.PixelWidth, meta.PixelHeight);
            else if (!string.IsNullOrWhiteSpace(meta.SelectedResolution))
                row.ActualResolution = CaptureResolutionPreset.FormatDisplayLabel(meta.SelectedResolution);
        }

        return row;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "-";
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static readonly LanguageManager EnglishFallback = CreateEnglishFallback();

    private static LanguageManager CreateEnglishFallback()
    {
        var lm = new LanguageManager();
        lm.Load("en");
        return lm;
    }

    private static string T(LanguageManager? lang, string key, string fallback) =>
        lang != null ? lang[key] : fallback;

    private static string Tf(LanguageManager? lang, string key, string fallback, params object[] args) =>
        string.Format(T(lang, key, fallback), args);

    private static string YesNo(LanguageManager? lang, bool value) =>
        value ? T(lang, "metaValueYes", "YES") : T(lang, "metaValueNo", "NO");

    private static string FormatIntervalMs(
        LanguageManager lang,
        double value,
        long intervalCount,
        string? unavailableMessage) =>
        MetadataDisplayHelper.LocalizeUnavailableToken(
            lang,
            CaptureIntervalMetadataFormatter.FormatMs(value, intervalCount, unavailableMessage));

    private static string FormatIntervalCount(
        LanguageManager lang,
        long intervalCount,
        string? unavailableMessage) =>
        MetadataDisplayHelper.LocalizeUnavailableToken(
            lang,
            CaptureIntervalMetadataFormatter.FormatCount(intervalCount, unavailableMessage));

    private static void AppendRecordingResourceDiagnostics(StringBuilder sb, CameraMetadataRecord meta)
    {
        var diag = meta.RecordingDiagnostics;
        var camera = diag?.Camera;
        sb.AppendLine("Recording Resource Diagnostics:");
        if (!string.IsNullOrWhiteSpace(camera?.TimingVerdict))
            sb.AppendLine($"  Timing verdict: {camera.TimingVerdict}");
        if (!string.IsNullOrWhiteSpace(diag?.SessionVerdictText))
            sb.AppendLine($"  Session verdict: {diag.SessionVerdictText}");
        sb.AppendLine($"  CPU average/peak: {FormatPercent(diag?.AverageCpuPercent)} / {FormatPercent(diag?.MaxCpuPercent)}");
        sb.AppendLine($"  CPU samples over 90%: {FormatInt(diag?.CpuSamplesOver90Percent)}");
        sb.AppendLine($"  RAM total: {FormatMb(diag?.SystemTotalMemoryMB)}");
        sb.AppendLine($"  Minimum available RAM: {FormatMb(diag?.MinSystemAvailableMemoryMB)}");
        sb.AppendLine($"  Process memory peak: {FormatMb(diag?.MaxProcessMemoryMB)}");
        sb.AppendLine($"  Process memory continuously increases: {FormatBool(diag?.ProcessMemoryContinuouslyIncreases)}");
        sb.AppendLine($"  Disk free space: {FormatGb(diag?.MinDiskFreeSpaceGB)}");
        sb.AppendLine($"  Total session file size: {FormatMb(diag?.TotalSessionSizeMB)}");
        sb.AppendLine($"  Estimated GB/hour: {FormatGbPerHour(diag?.EstimatedGBPerHourAllCameras)} all cameras / {FormatGbPerHour(diag?.EstimatedGBPerHourPerCamera)} per camera");
        sb.AppendLine($"  Requested FPS: {FormatNumber(camera?.RequestedFps)}");
        sb.AppendLine($"  Measured FPS by frame count: {FormatNumber(camera?.MeasuredFpsByFrameCount)}");
        sb.AppendLine($"  Measured FPS by valid intervals: {FormatNumber(camera?.MeasuredFpsByIntervals)}");
        sb.AppendLine($"  Capture interval mean/median/std ms: {FormatMs(camera?.CaptureIntervalMeanMs)} / {FormatMs(camera?.CaptureIntervalMedianMs)} / {FormatMs(camera?.CaptureIntervalStdMs)}");
        sb.AppendLine($"  Capture interval min/max/p95/p99 ms: {FormatMs(camera?.CaptureIntervalMinMs)} / {FormatMs(camera?.CaptureIntervalMaxMs)} / {FormatMs(camera?.CaptureIntervalP95Ms)} / {FormatMs(camera?.CaptureIntervalP99Ms)}");
        sb.AppendLine($"  Frames accepted for recording vs written: {FormatLong(ResolveFramesAccepted(meta, camera))} / {FormatLong(ResolveFramesWritten(meta, camera))}");
        sb.AppendLine($"  Frames accepted-minus-written: {FormatLong(ResolveAcceptedWrittenDifference(meta, camera))}");
        sb.AppendLine($"  Total captured vs written (advisory/debug): {FormatLong(ResolveFramesCaptured(meta, camera))} / {FormatLong(ResolveFramesWritten(meta, camera))}");
        sb.AppendLine($"  Stop-boundary captured/not recorded: {FormatLong(camera?.FramesCapturedAfterStopRequested)} / {FormatLong(camera?.FramesNotRecordedAfterStopRequested)}");
        sb.AppendLine($"  Frames dropped before enqueue: {FormatLong(camera?.FramesDroppedBeforeEnqueue)}");
        sb.AppendLine($"  Final flush completed/timed out: {FormatBool(camera?.FinalFlushCompleted)} / {FormatBool(camera?.FinalFlushTimedOut)}");
        sb.AppendLine($"  Writer released successfully: {FormatBool(camera?.WriterReleasedSuccessfully)}");
        sb.AppendLine($"  Writer drops: {FormatLong(camera?.WriterQueueDrops ?? meta.WriterQueueDrops)}");
        sb.AppendLine($"  Writer queue max depth/capacity: {FormatInt(camera?.WriterQueueMaxDepth ?? camera?.MaxWriterQueueMaxDepth)} / {FormatInt(camera?.WriterQueueCapacity)}");
        sb.AppendLine($"  Writer write mean/max ms: {FormatMs(camera?.WriterWriteMeanMs ?? camera?.MaxWriterWriteMeanMs)} / {FormatMs(camera?.WriterWriteMaxMs ?? camera?.MaxWriterWriteMaxMs)}");
        sb.AppendLine($"  Final file size: {FormatMb(camera?.FinalFileSizeMB)}");
        if (camera?.InstantaneousFpsSpikeIgnoredCount > 0)
        {
            sb.AppendLine($"  Instantaneous FPS spikes ignored: {FormatInt(camera.InstantaneousFpsSpikeIgnoredCount)} (max ignored {FormatNumber(camera.InstantaneousFpsSpikeMaxIgnored)})");
            sb.AppendLine($"  {diag?.ArtifactNote ?? "Instantaneous FPS spikes were ignored because they exceeded the realistic range for the requested FPS."}");
        }
        sb.AppendLine($"  Autofocus status: autoSupported={camera?.AutoFocusSupported ?? "unavailable"}, autoEnabled={camera?.AutoFocusEnabled ?? "unavailable"}, manualSupported={camera?.ManualFocusSupported ?? "unavailable"}, manualValue={camera?.ManualFocusValue ?? "unavailable"}");
        sb.AppendLine($"  Focus control metadata: requestedAuto={meta.AutoFocusRequested}, attempted={meta.AutoFocusApplyAttempted}, confirmed={FormatBool(meta.AutoFocusApplySucceeded)}, autoReadback={meta.AutoFocusReadbackValue}, manualSupported={FormatBool(meta.ManualFocusSupported)}, manualRequested={FormatNumber(meta.ManualFocusRequestedValue)}, manualReadback={meta.ManualFocusReadbackValue}, mode={meta.FocusControlMode}");
        if (!meta.AutoFocusRequested && meta.AutoFocusApplySucceeded != true)
            sb.AppendLine("  Focus warning: autofocus OFF was requested but not confirmed.");
        if (!string.IsNullOrWhiteSpace(meta.FocusWarning))
            sb.AppendLine($"  {meta.FocusWarning}");
        sb.AppendLine("  Stable measured FPS below requested FPS is acceptable in Original Capture Mode when timestamps are recorded.");
        sb.AppendLine($"  {OriginalCaptureVerificationPolicy.ContainerWallClockNote}");
        sb.AppendLine();
        sb.AppendLine("Likely Bottleneck:");
        foreach (var note in RecordingDiagnosticsInterpreter.BuildLikelyBottlenecks(meta))
            sb.AppendLine($"  {note}");
        sb.AppendLine();
        sb.AppendLine("Recommended Action:");
        foreach (var action in RecordingDiagnosticsInterpreter.BuildRecommendedActions(meta))
            sb.AppendLine($"  {action}");
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
        value.HasValue ? $"{value.Value:F3} ms" : "unavailable";

    private static string FormatInt(int? value) =>
        value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable";

    private static string FormatLong(long? value) =>
        value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable";

    private static string FormatBool(bool? value) =>
        value.HasValue ? value.Value.ToString() : "unavailable";

    private static string FormatNumber(double? value) =>
        value.HasValue ? value.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "unavailable";

    private static long ResolveFramesCaptured(CameraMetadataRecord meta, RecordingDiagnosticsCameraSummary? camera) =>
        camera?.FramesCapturedTotal > 0 ? camera.FramesCapturedTotal : camera?.FramesCaptured > 0 ? camera.FramesCaptured : meta.FramesCaptured;

    private static long ResolveFramesAccepted(CameraMetadataRecord meta, RecordingDiagnosticsCameraSummary? camera) =>
        camera?.FramesAcceptedForRecording > 0 ? camera.FramesAcceptedForRecording : meta.FrameTimestampCsvRowCount > 0 ? meta.FrameTimestampCsvRowCount : meta.FrameCount;

    private static long ResolveFramesWritten(CameraMetadataRecord meta, RecordingDiagnosticsCameraSummary? camera) =>
        camera?.FramesWritten > 0 ? camera.FramesWritten : meta.FrameCount;

    private static long ResolveAcceptedWrittenDifference(CameraMetadataRecord meta, RecordingDiagnosticsCameraSummary? camera) =>
        ResolveFramesAccepted(meta, camera) - ResolveFramesWritten(meta, camera);

    private static long ResolveFramesDifference(CameraMetadataRecord meta, RecordingDiagnosticsCameraSummary? camera)
    {
        if (camera != null && (camera.FramesCaptured > 0 || camera.FramesWritten > 0))
            return camera.FramesCapturedMinusWritten;
        return meta.FramesCaptured - meta.FrameCount;
    }

    private static string ResolveTimingMode(CameraMetadataRecord meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.RecordingTimingMode))
            return meta.RecordingTimingMode;
        if ((meta.DuplicatedFrames + meta.DuplicateFrames) > 0 || meta.ConstantFrameCountMode)
            return OriginalCaptureAuditPolicy.LegacyConstantFrameCountMode;
        return "-";
    }

    private static double? ExtractOffBySeconds(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, @"off by ([\d.]+)s");
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
