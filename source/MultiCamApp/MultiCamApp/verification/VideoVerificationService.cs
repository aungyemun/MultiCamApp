////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Experiment;
using MultiCamApp.Localization;
using MultiCamApp.Metadata;

namespace MultiCamApp.Verification;

/// <summary>
/// STABLE_CORE_V1 protected component — folder scan, ffprobe validation, metadata checks,
/// PASS/PASS_WITH_WARNING/FAIL classification, and report export. Modification requires regression checklist.
/// </summary>
public sealed class VideoVerificationService
{
    private readonly VerificationSettings _settings;
    private AppConfig? _verifyAppConfig;
    private readonly VideoScanner _scanner = new();
    private readonly VideoProbeService _probe;
    private readonly ExpectedSettingsResolver _expectedResolver = new();

    public VideoVerificationService(VerificationSettings settings)
    {
        _settings = settings;
        _probe = new VideoProbeService(settings);
    }

    public bool IsFfprobeAvailable => _probe.IsAvailable;

    public IReadOnlyList<VideoFileEntry> Scan(string folder) => _scanner.Scan(folder, _settings);

    public IReadOnlyList<string> DiscoverSessions(string folder) => _scanner.DiscoverSessions(folder);

    public async Task<VerificationReport> VerifyAsync(
        string folder,
        IReadOnlyList<VideoFileEntry> entries,
        AppConfig? appConfig,
        IProgress<VerificationProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        string verificationProfile = "Standard",
        LanguageManager? language = null)
    {
        _verifyAppConfig = appConfig;
        var report = new VerificationReport
        {
            VerifiedAtUtc = DateTime.UtcNow,
            AppVersion = VersionService.Current.Version,
            VerificationProfileUsed = verificationProfile
        };
        report.Summary.SelectedFolder = folder;
        if (appConfig?.VerificationProfiles.ContainsKey("ExperimentStrict") == true)
            report.Summary.ExpectedSettingsSource = "Experiment Strict profile when applicable";

        void Log(string line)
        {
            var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            report.LogLines.Add(stamped);
        }

        if (entries.Count == 0)
        {
            report.Summary.OverallVerdict = VerificationVerdict.Warning;
            Log(language != null ? language["verifyMsgNoVideosFound"] : "No MP4 videos found in the selected folder.");
            return report;
        }

        if (!_probe.IsAvailable)
        {
            report.Summary.OverallVerdict = VerificationVerdict.Fail;
            Log(_probe.GetMissingToolMessage(language));
            return report;
        }

        var sessionGroups = entries
            .GroupBy(e => e.SessionFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var multiSession = sessionGroups.Count > 1;
        report.Summary.ExpectedSettingsSource = multiSession
            ? $"per-session ({sessionGroups.Count} sessions)"
            : _expectedResolver.ResolveSourceLabel(
                sessionGroups[0].Key,
                sessionGroups[0].ToList());
        report.Summary.TotalVideosFound = entries.Count;

        var total = entries.Count;
        var i = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            i++;
            entry.Status = VerificationVerdict.Verifying;
            progress?.Report(new VerificationProgressUpdate
            {
                Current = i,
                Total = total,
                Message = $"Verifying {i} / {total}: {entry.FileName}"
            });

            var expected = _expectedResolver.ResolveForEntry(entry, appConfig);
            var source = multiSession
                ? _expectedResolver.ResolveSourceLabel(entry.SessionFolder, [entry])
                : report.Summary.ExpectedSettingsSource;
            var result = await VerifyOneAsync(entry, expected, source, Log, cancellationToken, language);
            report.Videos.Add(result);
            entry.Status = result.Verdict;

            var tableRow = VerificationReportMapper.ToTableRow(result, source, language);
            report.TableRows.Add(tableRow);
            progress?.Report(new VerificationProgressUpdate
            {
                Current = i,
                Total = total,
                Message = $"{entry.FileName}: {result.Verdict}",
                CompletedVideo = result,
                CompletedTableRow = tableRow
            });
        }

        ApplySessionChecks(report, sessionGroups, multiSession, Log);

        var sessionComparison = new SessionComparisonService();
        report.SessionAudits.Clear();
        foreach (var group in sessionGroups)
        {
            var sessionVideos = report.Videos
                .Where(v => string.Equals(v.Entry.SessionFolder, group.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var audit = sessionComparison.CompareSession(group.Key, sessionVideos, group.ToList(), language);
            report.SessionAudits.Add(audit);
            Log(language != null
                ? string.Format(language["verifyLogSessionStatusLine"], audit.SessionLabel, audit.SessionStatus)
                : $"Session {audit.SessionLabel}: {audit.SessionStatus}");
            foreach (var line in audit.ComparisonSummaryText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                Log($"  {line}");
        }

        report.Summary.SessionMessages.Add(
            language != null
                ? language["verifySessionScopeNote"]
                : "Inter-camera comparison is performed only within the same recording session. Videos from different sessions are audited individually but not compared with each other.");

        foreach (var audit in report.SessionAudits)
        {
            if (audit.SessionStatus == CameraAuditStatus.Pass)
                continue;
            report.Summary.SessionMessages.Add($"{audit.SessionLabel}: {audit.SessionStatus}");
            foreach (var warning in audit.Warnings.Take(3))
                report.Summary.SessionMessages.Add($"  • {audit.SessionLabel}: {warning}");
            foreach (var failure in audit.Failures.Take(3))
                report.Summary.SessionMessages.Add($"  • {audit.SessionLabel}: {failure}");
        }

        report.Summary.VideosVerified = report.Videos.Count;
        report.Summary.VideosPassed = report.Videos.Count(v => CameraAuditStatus.IsPassLevel(CameraAuditStatus.FromVideoResult(v)));
        report.Summary.VideosWarning = report.Videos.Count(v => CameraAuditStatus.FromVideoResult(v) == CameraAuditStatus.PassWithWarning);
        report.Summary.VideosFailed = report.Videos.Count(v => CameraAuditStatus.FromVideoResult(v) == CameraAuditStatus.Fail);

        report.Summary.OverallVerdict = report.SessionAudits.Any(a => a.SessionStatus == CameraAuditStatus.Fail)
            ? VerificationVerdict.Fail
            : report.SessionAudits.Any(a => a.SessionStatus == CameraAuditStatus.PassWithWarning)
                || report.Summary.VideosWarning > 0
                ? VerificationVerdict.Warning
                : VerificationVerdict.Pass;

        if (multiSession)
        {
            report.Summary.SessionDurationMatch = $"{sessionGroups.Count} sessions audited separately";
            report.Summary.FpsSpreadDisplay = string.Join("; ",
                report.SessionAudits.Select(a => $"{a.SessionLabel}: {a.SessionStatus}"));
        }
        else if (report.SessionAudits.Count == 1)
        {
            var audit = report.SessionAudits[0];
            report.Summary.SessionDurationMatch = audit.SessionStatus;
            if (audit.InterCameraWallDurationDifferenceSeconds.HasValue)
                report.Summary.DurationSpreadSeconds = audit.InterCameraWallDurationDifferenceSeconds;
        }

        var expectedBySlot = BuildCombinedExpectedBySlot(sessionGroups, appConfig);
        report.SessionResult = VerificationReportMapper.BuildSessionResult(report, expectedBySlot, multiSession);
        report.SessionResult.OverallResult = report.Summary.OverallVerdict;
        if (report.SessionAudits.Count == 1)
        {
            var audit = report.SessionAudits[0];
            report.SessionResult.ScientificTimingStatus = audit.SessionStatus;
            report.SessionResult.SessionScientificTimingConfidence = audit.SessionScientificTimingConfidence;
            report.SessionResult.SessionTimingMode = audit.SessionTimingMode;
            report.SessionResult.InterCameraFrameDifference = audit.InterCameraFrameDifference;
            report.SessionResult.InterCameraDurationDifferenceSeconds = audit.InterCameraWallDurationDifferenceSeconds;
            report.SessionResult.MaxMeasuredFpsDifference = audit.MaxMeasuredFpsDifference;
            report.SessionResult.MaxWallClockDurationDifferenceSec = audit.MaxWallClockDurationDifferenceSec;
            report.SessionResult.MaxStartOffsetSec = audit.MaxStartOffsetSec;
            report.SessionResult.MaxEndOffsetSec = audit.MaxEndOffsetSec;
            report.SessionResult.MaxFrameCountDifference = audit.MaxFrameCountDifference;
            report.SessionResult.FrameCountDifferenceAcceptedBecauseOriginalMode = audit.FrameCountDifferenceAcceptedBecauseOriginalMode;
            ApplyOriginalCaptureSessionCardFields(report.SessionResult, audit, report.Videos, language);
        }
        else if (report.SessionAudits.Count > 1)
        {
            report.SessionResult.ScientificTimingStatus = report.SessionAudits.Any(a => a.SessionStatus == CameraAuditStatus.Fail)
                ? CameraAuditStatus.Fail
                : report.SessionAudits.Any(a => a.SessionStatus == CameraAuditStatus.PassWithWarning)
                    ? CameraAuditStatus.PassWithWarning
                    : CameraAuditStatus.Pass;
            report.SessionResult.SessionScientificTimingConfidence =
                AggregateSessionScientificTimingConfidence(report.SessionAudits);
            report.SessionResult.InterCameraFrameDifference = report.SessionAudits
                .Where(a => a.InterCameraFrameDifference.HasValue)
                .Select(a => a.InterCameraFrameDifference!.Value)
                .DefaultIfEmpty(0)
                .Max();
            ApplyOriginalCaptureSessionCardFields(report.SessionResult, report.SessionAudits, report.Videos, language);
        }

        try
        {
            var auditReportPath = Path.Combine(folder, "video_audit_report.txt");
            await new VerificationReportWriter(language).ExportVideoAuditReportAsync(report, auditReportPath);
            Log(language != null
                ? string.Format(language["verifyLogSavedAuditReport"], auditReportPath)
                : $"Saved audit report: {auditReportPath}");
        }
        catch (Exception ex)
        {
            Log(language != null
                ? string.Format(language["verifyLogCouldNotSaveAuditReport"], ex.Message)
                : $"Could not save video_audit_report.txt: {ex.Message}");
        }

        Log(language != null
            ? string.Format(language["verifyLogOverallLine"], report.Summary.OverallVerdict)
            : $"Overall: {report.Summary.OverallVerdict}");
        progress?.Report(new VerificationProgressUpdate
        {
            Current = total,
            Total = total,
            Message = $"Done — {report.Summary.OverallVerdict}",
            IsFinished = true,
            Report = report
        });
        return report;
    }

    private static void ApplyOriginalCaptureSessionCardFields(
        SessionVerificationResult session,
        RecordingSessionAuditResult audit,
        IReadOnlyList<VideoVerificationResult> videos,
        LanguageManager? language = null) =>
        ApplyOriginalCaptureSessionCardFields(session, [audit], videos, language);

    private static string AggregateSessionScientificTimingConfidence(IReadOnlyList<RecordingSessionAuditResult> audits)
    {
        if (audits.Any(a => a.SessionScientificTimingConfidence == ScientificTimingConfidence.Failed))
            return ScientificTimingConfidence.Failed;
        if (audits.Any(a => a.SessionScientificTimingConfidence == ScientificTimingConfidence.PassWithWarning))
            return ScientificTimingConfidence.PassWithWarning;
        if (audits.Count > 0 && audits.All(a => a.SessionScientificTimingConfidence == ScientificTimingConfidence.PassOriginalTiming))
            return ScientificTimingConfidence.PassOriginalTiming;
        if (audits.Any(a => a.SessionScientificTimingConfidence == ScientificTimingConfidence.Low))
            return ScientificTimingConfidence.Low;
        if (audits.Any(a => a.SessionScientificTimingConfidence == ScientificTimingConfidence.Medium))
            return ScientificTimingConfidence.Medium;
        return ScientificTimingConfidence.High;
    }

    private static void ApplyOriginalCaptureSessionCardFields(
        SessionVerificationResult session,
        IReadOnlyList<RecordingSessionAuditResult> audits,
        IReadOnlyList<VideoVerificationResult> videos,
        LanguageManager? language = null)
    {
        var metadata = videos.Select(v => v.Metadata).Where(m => m != null).Select(m => m!).ToList();
        var original = metadata.Count > 0 && metadata.All(OriginalCaptureVerificationPolicy.IsOriginalCapture);
        session.OriginalFramesOnly = original
            && metadata.All(m => (m.FrameCount == m.FramesCaptured || OriginalCaptureVerificationPolicy.IsAcceptedStopBoundaryDifference(m))
                && m.DuplicatedFrames + m.DuplicateFrames == 0
                && m.PlaceholderFrames == 0);
        session.DuplicateFramesTotal = metadata.Sum(m => m.DuplicatedFrames + m.DuplicateFrames);
        session.PlaceholderFramesTotal = metadata.Sum(m => m.PlaceholderFrames);
        session.WriterQueueDropsTotal = metadata.Sum(m => m.WriterQueueDrops);
        session.TimestampCsvStatus = metadata.Count == 0
            ? "Missing metadata"
            : metadata.All(m => m.FrameTimestampCsvWritten && m.FrameTimestampCsvRowCount == m.FrameCount)
                ? "Complete"
                : "MISSING/INCOMPLETE";
        session.RecommendedTrimSource = original
            ? "timestamp CSV"
            : metadata
                .Select(m => m.TrimRecommendedTimeSource)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?? "-";
        session.ShowContainerWallClockWarning = metadata.Any(m => Math.Abs(m.ContainerVsWallClockDifferenceSeconds) > 0.5);
        session.ContainerWallClockWarning = session.ShowContainerWallClockWarning
            ? OriginalCaptureVerificationPolicy.GetContainerWallClockNote(language)
            : "";
        if (string.IsNullOrWhiteSpace(session.SessionTimingMode))
            session.SessionTimingMode = audits.Select(a => a.SessionTimingMode).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
    }

    private async Task<VideoVerificationResult> VerifyOneAsync(
        VideoFileEntry entry,
        ExpectedCameraSettings? expected,
        string settingsSource,
        Action<string> log,
        CancellationToken cancellationToken,
        LanguageManager? language = null)
    {
        var r = new VideoVerificationResult { Entry = entry, Expected = expected };
        var verdicts = new List<VerificationVerdict>();

        string Msg(string key, string fallback) => language != null ? language[key] : fallback;

        void AddMsg(string msg, VerificationVerdict v)
        {
            r.Messages.Add(msg);
            verdicts.Add(v);
            if (v == VerificationVerdict.Fail) r.ErrorMessages.Add(msg);
            else if (v == VerificationVerdict.Warning) r.WarningMessages.Add(msg);
            log($"{entry.FileName}: {msg}");
        }

        if (entry.IsMissingVideo)
        {
            AddMsg("MP4 file missing in camera folder", VerificationVerdict.Fail);
            if (!entry.MetadataJsonFound)
                AddMsg(Msg("verifyMsgMetadataJsonMissing", "metadata.json missing"), VerificationVerdict.Fail);
            r.Verdict = VerificationVerdict.Fail;
            r.ScientificTimingStatus = CameraAuditStatus.Fail;
            r.Recommendation = VerificationReportMapper.BuildRecommendation(r, language);
            VerificationReportMapper.BuildDetailAndRecommendation(r, settingsSource, language);
            return r;
        }

        if (!entry.MetadataFound)
            AddMsg(Msg("verifyMsgMetadataTxtNotFound", "metadata.txt not found"), VerificationVerdict.Warning);
        if (!entry.MetadataJsonFound)
            AddMsg(Msg("verifyMsgMetadataJsonNotFound", "metadata.json not found"), VerificationVerdict.Fail);

        if (!File.Exists(entry.FullPath))
        {
            AddMsg("Video file missing", VerificationVerdict.Fail);
            r.Verdict = Worst(verdicts);
            return r;
        }

        if (!string.Equals(Path.GetExtension(entry.FullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            AddMsg("File extension is not .mp4", VerificationVerdict.Warning);

        var slotOk = entry.CameraSlot.StartsWith("cam", StringComparison.OrdinalIgnoreCase);
        if (!slotOk)
            AddMsg($"Camera slot folder not recognized: {entry.CameraSlot}", VerificationVerdict.Warning);

        var probe = await _probe.ProbeAsync(entry.FullPath, cancellationToken);
        r.Probe = probe;

        if (!probe.Success)
        {
            AddMsg(probe.Error ?? "ffprobe failed", VerificationVerdict.Fail);
            FillDisplays(r);
            r.Verdict = Worst(verdicts);
            return r;
        }

        if (probe.FileSizeBytes <= 0)
            AddMsg("File size is zero", VerificationVerdict.Fail);

        if (!probe.HasVideoStream)
            AddMsg("No video stream", VerificationVerdict.Fail);

        if (!string.IsNullOrEmpty(probe.ContainerFormat) &&
            !probe.ContainerFormat.Contains("mp4", StringComparison.OrdinalIgnoreCase) &&
            !probe.ContainerFormat.Contains("mov", StringComparison.OrdinalIgnoreCase))
            AddMsg($"Container format unexpected: {probe.ContainerFormat}", VerificationVerdict.Warning);

        r.ActualResolutionDisplay = CaptureResolutionPreset.ToLabel(probe.Width, probe.Height);
        r.DurationDisplay = $"{probe.DurationSeconds:F2}s";
        r.FrameCountDisplay = probe.FrameCount?.ToString() ?? "-";
        r.CodecDisplay = probe.VideoCodec ?? "-";
        r.ContainerDisplay = probe.ContainerFormat ?? "-";
        r.FileSizeDisplay = VerificationReportMapper.FormatBytes(probe.FileSizeBytes);

        r.Metadata = MetadataParser.LoadCameraMetadata(entry.MetadataJsonPath, entry.MetadataPath);
        var originalCaptureVerification = OriginalCaptureVerificationPolicy.IsOriginalCapture(r.Metadata);
        var measuredFpsForCompare = VerificationCaptureProfile.ResolveMeasuredFps(probe, r.Metadata);
        r.ActualFpsDisplay = $"{measuredFpsForCompare:F1}";

        // Heuristic: detect built‑in cameras from metadata so we can treat
        // 720p‑only hardware more leniently on resolution checks.
        var isBuiltIn = IsBuiltInCamera(entry);

        if (expected != null)
        {
            r.ExpectedResolutionDisplay = expected.Width > 0 && expected.Height > 0
                ? CaptureResolutionPreset.ToLabel(expected.Width.Value, expected.Height.Value) : "-";
            r.ExpectedFpsDisplay = expected.Fps?.ToString("F1") ?? "-";
            r.ExpectedDurationDisplay = expected.DurationSeconds is > 0
                ? $"{expected.DurationSeconds.Value:F2}s" : "-";
            r.ExpectedFrameCountDisplay = expected.FrameCount is > 0
                ? expected.FrameCount.Value.ToString() : "-";

            if (expected.Width > 0 && expected.Height > 0)
            {
                if (probe.Width == expected.Width && probe.Height == expected.Height)
                    r.ResolutionMatch = VerificationMatchStatus.Yes;
                else
                {
                    var diff = Math.Abs(probe.Width - expected.Width.Value) + Math.Abs(probe.Height - expected.Height.Value);
                    var severe = diff > 400 && !isBuiltIn;
                    r.ResolutionMatch = severe ? VerificationMatchStatus.No : VerificationMatchStatus.Warning;
                    var verdict = severe ? VerificationVerdict.Fail : VerificationVerdict.Warning;
                    AddMsg($"Resolution mismatch: expected {r.ExpectedResolutionDisplay}, actual {r.ActualResolutionDisplay}", verdict);
                }
            }

            if (expected.Fps is > 0)
            {
                var actualFps = VerificationCaptureProfile.ResolveMeasuredFps(probe, r.Metadata);
                r.ActualFpsDisplay = $"{actualFps:F1}";

                if (actualFps > 0)
                {
                    var expectedFps = expected.Fps.Value;
                    var fpsDiff = Math.Abs(actualFps - expectedFps);
                    var (fpsWarn, fpsFail) = VerificationCaptureProfile.GetFpsTolerances(expectedFps, _settings);
                    if (fpsDiff > fpsFail)
                    {
                        r.FpsMatch = originalCaptureVerification ? VerificationMatchStatus.Warning : VerificationMatchStatus.No;
                        AddMsg(originalCaptureVerification
                                ? $"Measured FPS differs from requested FPS in Original Capture Mode: requested {expectedFps:F1}, measured {actualFps:F1}"
                                : $"FPS mismatch: expected {expectedFps:F1}, actual {actualFps:F1}",
                            originalCaptureVerification ? VerificationVerdict.Warning : VerificationVerdict.Fail);
                    }
                    else if (fpsDiff > fpsWarn)
                    {
                        r.FpsMatch = VerificationMatchStatus.Warning;
                        AddMsg($"FPS slightly off: expected {expectedFps:F1}, actual {actualFps:F1}", VerificationVerdict.Warning);
                    }
                    else
                        r.FpsMatch = VerificationMatchStatus.Yes;
                }
            }

            var frameCountsAgree = false;
            if (expected.FrameCount is > 0 && probe.FrameCount is > 0)
            {
                var fcDiff = Math.Abs(probe.FrameCount.Value - expected.FrameCount.Value);
                frameCountsAgree = fcDiff <= _settings.FrameFailTolerance;
                if (fcDiff > _settings.FrameFailTolerance)
                    AddMsg($"Frame count mismatch vs metadata: {fcDiff}", VerificationVerdict.Fail);
                else if (fcDiff > _settings.FrameWarningTolerance)
                    AddMsg($"Frame count slightly off vs metadata: {fcDiff}", VerificationVerdict.Warning);
            }

            if (expected.DurationSeconds is > 0 && probe.DurationSeconds > 0)
            {
                var durDiff = Math.Abs(probe.DurationSeconds - expected.DurationSeconds.Value);
                if (durDiff > _settings.DurationFailToleranceSeconds)
                {
                    if (frameCountsAgree)
                    {
                        r.DurationMatch = VerificationMatchStatus.Yes;
                        AddMsg(
                            $"Container duration vs metadata (frames match): off by {durDiff:F2}s",
                            VerificationVerdict.Pass);
                    }
                    else if (originalCaptureVerification)
                    {
                        r.DurationMatch = VerificationMatchStatus.Warning;
                        AddMsg(OriginalCaptureVerificationPolicy.GetContainerWallClockNote(language), VerificationVerdict.Warning);
                    }
                    else
                    {
                        r.DurationMatch = VerificationMatchStatus.No;
                        AddMsg($"Duration vs metadata: off by {durDiff:F2}s", VerificationVerdict.Fail);
                    }
                }
                else if (durDiff > _settings.DurationWarningToleranceSeconds)
                {
                    r.DurationMatch = VerificationMatchStatus.Yes;
                    AddMsg($"Duration vs metadata: off by {durDiff:F2}s", VerificationVerdict.Pass);
                }
                else
                    r.DurationMatch = VerificationMatchStatus.Yes;
            }

            if (!string.IsNullOrEmpty(expected.Codec) && !string.IsNullOrEmpty(probe.VideoCodec))
            {
                if (!CodecMatches(expected.Codec, probe.VideoCodec))
                    AddMsg($"Codec note: metadata {expected.Codec}, actual {probe.VideoCodec}", VerificationVerdict.Warning);
            }
        }
        else
        {
            r.ExpectedResolutionDisplay = "-";
            r.ExpectedFpsDisplay = "-";
            r.ExpectedDurationDisplay = "-";
            r.ExpectedFrameCountDisplay = "-";
            AddMsg("No expected settings; verified technical details only", VerificationVerdict.Warning);
        }

        if (probe.Fps > 0 && probe.DurationSeconds > 0)
        {
            var estimated = (long)Math.Round(probe.Fps * probe.DurationSeconds);
            if (probe.FrameCount is > 0)
            {
                var fcDiff = Math.Abs(probe.FrameCount.Value - estimated);
                var failThreshold = Math.Max(_settings.FrameFailTolerance, (long)(probe.Fps * 1.0));
                if (fcDiff > failThreshold)
                    AddMsg($"Frame count vs duration×fps: diff {fcDiff} (est {estimated})", VerificationVerdict.Fail);
                else if (fcDiff > _settings.FrameWarningTolerance)
                    AddMsg($"Frame count vs duration×fps: diff {fcDiff}", VerificationVerdict.Warning);
            }
            else
                AddMsg("Frame count unavailable from probe", VerificationVerdict.Warning);
        }

        if (!probe.BitRate.HasValue)
            AddMsg("Bitrate not reported", VerificationVerdict.Warning);

        if (probe.HasAudioStream)
            AddMsg("Audio stream present (optional, not a failure)", VerificationVerdict.Pass);

        CameraMetadataRecord? metaRecord = r.Metadata;
        if (metaRecord != null)
        {
            r.FrameBasedDurationSeconds = metaRecord.FrameBasedDurationSeconds > 0
                ? metaRecord.FrameBasedDurationSeconds
                : metaRecord.FrameCount > 0 && metaRecord.RecordingWriterFps > 0
                    ? metaRecord.FrameCount / metaRecord.RecordingWriterFps
                    : null;
            r.ContainerDurationSeconds = metaRecord.ContainerDurationSeconds > 0
                ? metaRecord.ContainerDurationSeconds
                : probe?.DurationSeconds;
            r.WallDurationSeconds = metaRecord.WallClockDurationSeconds > 0
                ? metaRecord.WallClockDurationSeconds
                : metaRecord.WallDurationSeconds > 0
                    ? metaRecord.WallDurationSeconds
                    : metaRecord.DurationSeconds;
            r.ContainerVsWallClockDifferenceSeconds = metaRecord.ContainerVsWallClockDifferenceSeconds != 0
                ? metaRecord.ContainerVsWallClockDifferenceSeconds
                : r.ContainerDurationSeconds.HasValue && r.WallDurationSeconds.HasValue
                    ? r.ContainerDurationSeconds.Value - r.WallDurationSeconds.Value
                    : null;
            r.TimestampDriftSeconds = r.ContainerVsWallClockDifferenceSeconds;
            r.ConstantFpsDisplay = probe?.ConstantFps == true ? "YES" : "NO";
            // VideoEngineV2 recordings (metaRecord.IsV2Source) already carry their own
            // already-computed, already-correct scientific-timing verdict (see
            // MetadataParser.BuildRecordFromV2Metadata and V2RecordingMetadata's
            // VerificationGlobalSessionResult doc comment). The three checks in the `else` branch
            // below (AssessScientificTimingStatus, the CSV-row-count equality check, and the
            // metadata-completeness critical-fields check) all assume the legacy OpenCV pipeline's
            // exact-match semantics — e.g. FrameTimestampCsvRowCount must equal FrameCount exactly —
            // which V2's own preview-inclusive frame-counting model routinely and legitimately
            // violates by a small amount (V2 itself already tolerates this via its own
            // frameIntegrity.csvRowsDiff/integrityVerdict check). Forcing V2 data through those
            // legacy-specific equality checks reliably produced false FAILs. Trust V2's own verdict
            // instead of re-deriving one from a schema it was never designed for. The `else` branch
            // is byte-for-byte the original legacy logic, unchanged, for non-V2 (or older/partial V2)
            // metadata.
            if (metaRecord.IsV2Source && !string.IsNullOrWhiteSpace(metaRecord.ScientificTimingStatus))
            {
                r.ScientificTimingStatus = metaRecord.ScientificTimingStatus;
                r.ScientificTimingMessage = metaRecord.ScientificTimingMessage ?? "";
                if (string.IsNullOrWhiteSpace(r.ScientificTimingMessage)
                    && string.Equals(r.ScientificTimingStatus, "PASS_WITH_WARNING", StringComparison.OrdinalIgnoreCase))
                    r.ScientificTimingMessage = ScientificTimingAssessor.DefaultMessage;

                // Still computed for display/export (shown in reports as metadata completeness %),
                // but never allowed to downgrade/override the verdict V2 already trusts — V2
                // metadata legitimately doesn't populate several legacy-only statistical fields
                // (P95/P99 interval percentiles, fpsStabilityGrade, first/last-frame monotonic
                // seconds) that have no V2 equivalent, and treating their absence as a
                // scientific-accuracy failure would be wrong for a recording V2 already validated.
                var v2Completeness = MetadataCompletenessPolicy.Assess(metaRecord);
                r.MetadataCompletenessPercent = v2Completeness.Percent;
                r.MissingRequiredMetadataFields = string.Join(", ", v2Completeness.MissingRequiredFields);
                r.MissingCriticalMetadataFields = string.Join(", ", v2Completeness.MissingCriticalFields);
                r.ScientificMetadataComplete = v2Completeness.ScientificMetadataComplete;
            }
            else
            {
                r.ScientificTimingStatus = OriginalCaptureVerificationPolicy.IsOriginalCapture(metaRecord)
                    ? AssessScientificTimingStatus(probe, metaRecord)
                    : !string.IsNullOrWhiteSpace(metaRecord.ScientificTimingStatus)
                        ? metaRecord.ScientificTimingStatus
                        : AssessScientificTimingStatus(probe, metaRecord);
                r.ScientificTimingMessage = OriginalCaptureVerificationPolicy.IsAcceptedStopBoundaryDifference(metaRecord)
                    ? OriginalCaptureVerificationPolicy.GetStopBoundaryAcceptedMessage(language)
                    : metaRecord.ScientificTimingMessage ?? "";
                if (string.IsNullOrWhiteSpace(r.ScientificTimingMessage)
                    && string.Equals(r.ScientificTimingStatus, "PASS_WITH_WARNING", StringComparison.OrdinalIgnoreCase))
                    r.ScientificTimingMessage = ScientificTimingAssessor.DefaultMessage;

                if (metaRecord.OriginalCaptureMode && metaRecord.FrameCount > 0)
                {
                    var timestampCsvIncomplete =
                        !metaRecord.FrameTimestampCsvWritten ||
                        metaRecord.FrameTimestampCsvRowCount != metaRecord.FrameCount ||
                        string.IsNullOrWhiteSpace(metaRecord.FrameTimestampCsvPath) ||
                        !File.Exists(ResolveMetadataRelativePath(entry, metaRecord.FrameTimestampCsvPath));
                    if (timestampCsvIncomplete)
                    {
                        r.ScientificTimingStatus = CameraAuditStatus.Fail;
                        var timestampMsg =
                            $"Frame timestamp CSV metadata incomplete: written={metaRecord.FrameTimestampCsvWritten}, rows={metaRecord.FrameTimestampCsvRowCount}, frames={metaRecord.FrameCount}.";
                        r.ScientificTimingMessage = string.IsNullOrWhiteSpace(r.ScientificTimingMessage)
                            ? timestampMsg
                            : $"{r.ScientificTimingMessage} {timestampMsg}";
                    }
                }

                if (metaRecord.OriginalCaptureMode)
                {
                    var completeness = MetadataCompletenessPolicy.Assess(metaRecord);
                    r.MetadataCompletenessPercent = completeness.Percent;
                    r.MissingRequiredMetadataFields = string.Join(", ", completeness.MissingRequiredFields);
                    r.MissingCriticalMetadataFields = string.Join(", ", completeness.MissingCriticalFields);
                    r.ScientificMetadataComplete = completeness.ScientificMetadataComplete;

                    if (!completeness.ScientificMetadataComplete)
                    {
                        var completenessMsg =
                            $"Scientific metadata completeness: {completeness.Percent:F1}% complete; missingRequiredMetadataFields={r.MissingRequiredMetadataFields}.";
                        if (completeness.MissingCriticalFields.Count > 0)
                        {
                            r.ScientificTimingStatus = CameraAuditStatus.Fail;
                            r.ScientificTimingMessage = AppendScientificTimingMessage(
                                r.ScientificTimingMessage,
                                $"{completenessMsg} Critical timing fields missing: {r.MissingCriticalMetadataFields}.");
                        }
                        else if (CameraAuditStatus.IsPassLevel(r.ScientificTimingStatus))
                        {
                            r.ScientificTimingStatus = CameraAuditStatus.PassWithWarning;
                            r.ScientificTimingMessage = AppendScientificTimingMessage(r.ScientificTimingMessage, completenessMsg);
                        }
                    }
                }
                else
                {
                    r.ScientificMetadataComplete = false;
                }
            }

            var measuredCameraFps = metaRecord.MeasuredCameraFps > 0
                ? metaRecord.MeasuredCameraFps
                : metaRecord.ActualFps;
            var containerFps = metaRecord.ContainerFps > 0
                ? metaRecord.ContainerFps
                : metaRecord.RecordingWriterFps > 0 ? metaRecord.RecordingWriterFps : probe?.Fps ?? 0;
            var containerVsWall = r.ContainerVsWallClockDifferenceSeconds?.ToString("F3") ?? "-";
            r.TimingStatusDisplay = language != null
                ? string.Format(language["metaTimingStatusLine"],
                    $"{measuredCameraFps:F3}", $"{containerFps:F3}", containerVsWall, r.ScientificTimingStatus)
                : $"Real Capture FPS: {measuredCameraFps:F3}; Playback FPS: {containerFps:F3}; " +
                  $"Container vs wall-clock: {containerVsWall}s; " +
                  $"Status: {r.ScientificTimingStatus}";
            if (!string.IsNullOrWhiteSpace(metaRecord.FpsStabilityGrade))
                r.TimingStatusDisplay += $"; FPS stability: {metaRecord.FpsStabilityGrade}";
        }
        else
        {
            r.ScientificTimingStatus = probe?.Success == true ? (probe.ConstantFps ? "PASS" : "PASS_WITH_WARNING") : "FAIL";
            r.ConstantFpsDisplay = probe?.ConstantFps == true ? "YES" : "NO";
            r.TimingStatusDisplay = language != null
                ? string.Format(language["metaTimingStatusNoMeta"], r.ConstantFpsDisplay, r.ScientificTimingStatus)
                : $"Constant FPS: {r.ConstantFpsDisplay}; Status: {r.ScientificTimingStatus}";
        }

        if (metaRecord?.ExperimentMode == true && _verifyAppConfig != null)
        {
            var expSvc = new ExperimentVerificationService();
            r.Experiment = expSvc.Evaluate(r, metaRecord, _verifyAppConfig);
            foreach (var msg in r.Experiment.Messages)
            {
                r.Messages.Add($"[Experiment] {msg}");
                if (r.Experiment.Verdict == VerificationVerdict.Fail)
                    r.ErrorMessages.Add(msg);
                else if (r.Experiment.Verdict == VerificationVerdict.Warning)
                    r.WarningMessages.Add(msg);
            }

            if (r.Experiment.Verdict == VerificationVerdict.Fail)
                verdicts.Add(VerificationVerdict.Fail);
            else if (r.Experiment.Verdict == VerificationVerdict.Warning)
                verdicts.Add(VerificationVerdict.Warning);

            if (r.Experiment.IsExperimentSession)
            {
                r.ExpectedFrameCountDisplay = r.Experiment.ExpectedFrames.ToString();
                r.ExpectedFpsDisplay = $"{r.Experiment.ExpectedFps:F1}";
                r.ExpectedDurationDisplay = $"{r.Experiment.ExpectedDurationSeconds:F1}s";
            }
        }

        if (string.Equals(r.ScientificTimingStatus, CameraAuditStatus.Fail, StringComparison.OrdinalIgnoreCase))
        {
            var integrityMsg = !string.IsNullOrWhiteSpace(r.ScientificTimingMessage)
                ? r.ScientificTimingMessage
                : Msg("verifyMsgScientificTimingFail", "Scientific timing status: FAIL");
            AddMsg(integrityMsg, VerificationVerdict.Fail);
        }
        else if (string.Equals(r.ScientificTimingStatus, CameraAuditStatus.PassWithWarning, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(r.ScientificTimingMessage)
                 && !r.Messages.Contains(r.ScientificTimingMessage))
        {
            AddMsg(
                language != null
                    ? MetadataDisplayHelper.LocalizeScientificTimingMessage(language, r.ScientificTimingMessage)
                    : r.ScientificTimingMessage,
                VerificationVerdict.Warning);
        }

        if (verdicts.Count == 0 || verdicts.All(v => v == VerificationVerdict.Pass))
            r.Verdict = VerificationVerdict.Pass;
        else
            r.Verdict = Worst(verdicts);

        if (r.Experiment?.IsExperimentSession == true)
            r.Verdict = Worst([r.Verdict, r.Experiment.Verdict]);

        r.NotesDisplay = r.Messages.Count > 0 ? string.Join("; ", r.Messages) : "";
        r.Recommendation = VerificationReportMapper.BuildRecommendation(r, language);
        VerificationReportMapper.BuildDetailAndRecommendation(r, settingsSource, language);
        FillDisplays(r);
        return r;
    }

    private static string AssessScientificTimingStatus(VideoProbeData? probe, CameraMetadataRecord meta)
    {
        var writerFps = meta.WriterFps > 0
            ? meta.WriterFps
            : meta.RecordingWriterFps > 0
                ? meta.RecordingWriterFps
                : meta.SelectedDeviceFps > 0
                    ? meta.SelectedDeviceFps
                    : meta.RequestedFps > 0 ? meta.RequestedFps : probe?.Fps ?? 0;
        var containerFps = meta.ContainerFps > 0 ? meta.ContainerFps : probe?.Fps ?? writerFps;
        var measuredCameraFps = meta.MeasuredCameraFps > 0
            ? meta.MeasuredCameraFps
            : meta.WallClockDurationSeconds > 0 && meta.FramesCaptured > 0
                ? meta.FramesCaptured / meta.WallClockDurationSeconds
                : 0;

        var framesWritten = meta.FrameCount > 0 ? meta.FrameCount : probe?.FrameCount ?? 0;
        var framesAccepted = OriginalCaptureVerificationPolicy.ResolveFramesAccepted(meta);
        var cameraDiagnostics = meta.RecordingDiagnostics?.Camera;

        return ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            VideoReadable = probe?.Success == true && probe.HasVideoStream,
            HasMetadata = true,
            FramesWritten = framesWritten,
            FramesCaptured = OriginalCaptureVerificationPolicy.ResolveFramesCaptured(meta),
            QueueDrops = meta.WriterQueueDrops,
            DuplicateFrames = meta.DuplicatedFrames + meta.DuplicateFrames,
            PlaceholderFrames = meta.PlaceholderFrames,
            ConstantFrameCountMode = meta.ConstantFrameCountMode,
            OriginalCaptureMode = meta.OriginalCaptureMode,
            RequestedFps = meta.RequestedFps,
            Width = meta.PixelWidth,
            Height = meta.PixelHeight,
            WriterFps = writerFps,
            ContainerFps = containerFps,
            MeasuredCameraFps = measuredCameraFps,
            InterCameraFrameDifference = meta.InterCameraFrameDiff,
            InterCameraStartOffsetMs = meta.InterCameraStartOffsetMs,
            CaptureIntervalCount = meta.CaptureIntervalCount,
            CaptureIntervalStdMs = meta.CaptureIntervalStdMs,
            CaptureIntervalP99Ms = meta.CaptureIntervalP99Ms,
            ExpectedIntervalMs = meta.ExpectedIntervalMs,
            LongGapCount = meta.LongGapCount,
            SevereLongGapCount = meta.SevereLongGapCount,
            FpsStabilityGrade = meta.FpsStabilityGrade,
            RequireFrameTimestampCsvValidation = meta.OriginalCaptureMode,
            FrameTimestampCsvWritten = meta.FrameTimestampCsvWritten,
            FrameTimestampCsvRowCount = meta.FrameTimestampCsvRowCount,
            FramesAcceptedForRecording = framesAccepted,
            FramesAcceptedMinusWritten = framesAccepted - framesWritten,
            StopBoundaryCapturedNotRecorded = cameraDiagnostics?.FramesNotRecordedAfterStopRequested ?? 0,
            FinalFlushTimedOut = cameraDiagnostics?.FinalFlushTimedOut == true,
            MaxConsecutiveNoFrame = meta.MaxConsecutiveNoFrame
        }).Status;
    }

    private static string AppendScientificTimingMessage(string? current, string message) =>
        string.IsNullOrWhiteSpace(current)
            ? message
            : $"{current} {message}";

    private static string ResolveMetadataRelativePath(VideoFileEntry entry, string path)
    {
        if (Path.IsPathFullyQualified(path))
            return path;
        var metadataFolder = !string.IsNullOrWhiteSpace(entry.MetadataJsonPath)
            ? Path.GetDirectoryName(entry.MetadataJsonPath)
            : !string.IsNullOrWhiteSpace(entry.MetadataPath)
                ? Path.GetDirectoryName(entry.MetadataPath)
                : Path.GetDirectoryName(entry.FullPath);
        return Path.Combine(metadataFolder ?? "", path);
    }

    /// <summary>
    /// Best‑effort detection of built‑in cameras from metadata.txt so we can
    /// relax resolution expectations (e.g. 1280x720‑only hardware) without
    /// failing verification.
    /// </summary>
    private static bool IsBuiltInCamera(VideoFileEntry entry)
    {
        try
        {
            var metaPath = entry.MetadataPath;
            if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
                return false;

            // Scan a small prefix of the metadata file for known built‑in markers.
            var lines = File.ReadLines(metaPath).Take(40);
            foreach (var line in lines)
            {
                if (line.Contains("BuiltInFront", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Built-in", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Integrated Webcam", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // If anything goes wrong, fall back to "not built‑in".
        }

        return false;
    }

    private void ApplySessionChecks(
        VerificationReport report,
        List<IGrouping<string, VideoFileEntry>> sessionGroups,
        bool multiSession,
        Action<string> log)
    {
        var allDurations = new List<double>();
        var allFps = new List<double>();

        foreach (var group in sessionGroups)
        {
            var sessionVideos = report.Videos
                .Where(v => string.Equals(v.Entry.SessionFolder, group.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sessionVideos.Count == 0) continue;

            var label = sessionGroups.Count > 1
                ? Path.GetFileName(group.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : "";
            ApplySessionChecksForVideos(sessionVideos, label, report, log, allDurations, allFps);
        }

        if (allDurations.Count >= 1)
        {
            report.Summary.MinDurationSeconds = allDurations.Min();
            report.Summary.MaxDurationSeconds = allDurations.Max();
        }

        if (allFps.Count >= 1)
        {
            report.Summary.MinFps = allFps.Min();
            report.Summary.MaxFps = allFps.Max();
        }

        var durationDrifts = report.Videos
            .Where(v => v.TimestampDriftSeconds.HasValue)
            .Select(v => Math.Abs(v.TimestampDriftSeconds!.Value))
            .ToList();
        if (durationDrifts.Count > 0)
            report.SessionResult.TimestampDriftSeconds = durationDrifts.Average();

        var frameBasedDurations = report.Videos
            .Where(v => v.FrameBasedDurationSeconds.HasValue)
            .Select(v => v.FrameBasedDurationSeconds!.Value)
            .ToList();
        if (frameBasedDurations.Count > 0)
            report.SessionResult.FrameBasedDurationSeconds = frameBasedDurations.Average();

        var containerDurations = report.Videos
            .Where(v => v.ContainerDurationSeconds.HasValue)
            .Select(v => v.ContainerDurationSeconds!.Value)
            .ToList();
        if (containerDurations.Count > 0)
            report.SessionResult.ContainerDurationSeconds = containerDurations.Average();

        var wallDurations = report.Videos
            .Where(v => v.WallDurationSeconds.HasValue)
            .Select(v => v.WallDurationSeconds!.Value)
            .ToList();
        if (wallDurations.Count > 0)
            report.SessionResult.WallDurationSeconds = wallDurations.Average();

        var frameCounts = report.Videos
            .Where(v => v.Probe?.FrameCount is > 0)
            .Select(v => v.Probe!.FrameCount!.Value)
            .ToList();
        if (!multiSession && frameCounts.Count > 0)
            report.SessionResult.InterCameraFrameDifference = frameCounts.Max() - frameCounts.Min();
        else if (multiSession)
        {
            long maxFrameDiff = 0;
            var hasFrameDiff = false;
            foreach (var group in sessionGroups)
            {
                var sessionVideos = report.Videos
                    .Where(v => string.Equals(v.Entry.SessionFolder, group.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var metaDiff = sessionVideos
                    .Where(v => v.Metadata != null)
                    .Select(v => v.Metadata!.InterCameraFrameDiff)
                    .FirstOrDefault();
                if (sessionVideos.Any(v => v.Metadata != null))
                {
                    maxFrameDiff = Math.Max(maxFrameDiff, metaDiff);
                    hasFrameDiff = true;
                    continue;
                }

                var sessionFrameCounts = sessionVideos
                    .Where(v => v.Probe?.FrameCount is > 0)
                    .Select(v => v.Probe!.FrameCount!.Value)
                    .ToList();
                if (sessionFrameCounts.Count >= 2)
                {
                    maxFrameDiff = Math.Max(maxFrameDiff, sessionFrameCounts.Max() - sessionFrameCounts.Min());
                    hasFrameDiff = true;
                }
            }

            if (hasFrameDiff)
                report.SessionResult.InterCameraFrameDifference = maxFrameDiff;
        }

        if (frameBasedDurations.Count > 1 && !multiSession)
            report.SessionResult.InterCameraDurationDifferenceSeconds = frameBasedDurations.Max() - frameBasedDurations.Min();
        else if (multiSession)
        {
            double maxDurationDiff = 0;
            foreach (var group in sessionGroups)
            {
                var sessionDurations = report.Videos
                    .Where(v => string.Equals(v.Entry.SessionFolder, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.FrameBasedDurationSeconds ?? v.Probe?.DurationSeconds ?? 0)
                    .Where(d => d > 0)
                    .ToList();
                if (sessionDurations.Count >= 2)
                    maxDurationDiff = Math.Max(maxDurationDiff, sessionDurations.Max() - sessionDurations.Min());
            }

            if (maxDurationDiff > 0)
                report.SessionResult.InterCameraDurationDifferenceSeconds = maxDurationDiff;
        }

        if (multiSession)
        {
            report.Summary.MinDurationSeconds = null;
            report.Summary.MaxDurationSeconds = null;
        }

        report.SessionResult.ScientificTimingStatus = report.Videos.Any(v => string.Equals(v.ScientificTimingStatus, "FAIL", StringComparison.OrdinalIgnoreCase))
            ? "FAIL"
            : report.Videos.Any(v => string.Equals(v.ScientificTimingStatus, "PASS_WITH_WARNING", StringComparison.OrdinalIgnoreCase))
                ? "PASS_WITH_WARNING"
                : report.Videos.Any(v => string.Equals(v.ScientificTimingStatus, CameraAuditStatus.PassOriginalTimingWithNote, StringComparison.OrdinalIgnoreCase))
                    ? CameraAuditStatus.PassOriginalTimingWithNote
                    : report.Videos.All(v => CameraAuditStatus.IsPassLevel(v.ScientificTimingStatus))
                        ? (report.Videos.All(v => string.Equals(v.ScientificTimingStatus, CameraAuditStatus.PassOriginalTiming, StringComparison.OrdinalIgnoreCase))
                            ? CameraAuditStatus.PassOriginalTiming
                            : "PASS")
                    : "-";
        if (string.IsNullOrWhiteSpace(report.SessionResult.SessionScientificTimingConfidence)
            || report.SessionResult.SessionScientificTimingConfidence == ScientificTimingConfidence.Low && report.SessionAudits.Count == 0)
        {
            report.SessionResult.SessionScientificTimingConfidence = ScientificTimingConfidence.FromSessionVideos(report.Videos);
        }

        if (sessionGroups.Count > 1)
            report.Summary.SessionDurationMatch = $"{sessionGroups.Count} sessions verified";
    }

    private void ApplySessionChecksForVideos(
        List<VideoVerificationResult> sessionVideos,
        string label,
        VerificationReport report,
        Action<string> log,
        List<double> allDurations,
        List<double> allFps)
    {
        var durations = sessionVideos
            .Where(v => v.Probe?.DurationSeconds > 0)
            .Select(v => v.Probe!.DurationSeconds)
            .ToList();
        allDurations.AddRange(durations);

        if (durations.Count >= 2)
        {
            var min = durations.Min();
            var max = durations.Max();
            var spread = max - min;
            var spreadText = string.IsNullOrEmpty(label)
                ? $"spread {spread:F2}s (min {min:F2}s, max {max:F2}s)"
                : $"{label}: spread {spread:F2}s (min {min:F2}s, max {max:F2}s)";

            if (sessionVideos.Count == report.Videos.Count)
                report.Summary.SessionDurationMatch = spreadText;
            else if (string.IsNullOrEmpty(report.Summary.SessionDurationMatch) ||
                     report.Summary.SessionDurationMatch.StartsWith("per-session", StringComparison.OrdinalIgnoreCase))
                report.Summary.SessionDurationMatch = spreadText;
            else
                report.Summary.SessionDurationMatch += "; " + spreadText;

            var prefix = string.IsNullOrEmpty(label) ? "" : $"{label}: ";
            if (spread > 60.0) // Extremely high spread
            {
                report.Summary.SessionMessages.Add($"{prefix}extreme duration spread {spread:F2}s (check cameras)");
                log($"{prefix}FAIL: extreme duration spread {spread:F2}s");
            }
            else if (spread > _settings.DurationFailToleranceSeconds)
            {
                report.Summary.SessionMessages.Add($"{prefix}duration spread {spread:F2}s (review sync)");
                log($"{prefix}WARNING: high duration spread {spread:F2}s");
            }
            else if (spread > _settings.DurationWarningToleranceSeconds)
            {
                report.Summary.SessionMessages.Add($"{prefix}duration spread {spread:F2}s (minor)");
                log($"{prefix}NOTE: duration spread {spread:F2}s");
            }
            else
                log($"{prefix}duration spread OK: {spread:F2}s");
        }
        else if (durations.Count == 1 && sessionVideos.Count == report.Videos.Count)
            report.Summary.SessionDurationMatch = $"{durations[0]:F2}s";

        var fpsList = sessionVideos.Where(v => v.Probe?.Fps > 0).Select(v => v.Probe!.Fps).ToList();
        allFps.AddRange(fpsList);

        if (fpsList.Count >= 2)
        {
            var fpsSpread = fpsList.Max() - fpsList.Min();
            var fpsPrefix = string.IsNullOrEmpty(label) ? "" : $"{label}: ";
            var fpsText = $"{fpsPrefix}FPS spread {fpsSpread:F2} (min {fpsList.Min():F1}, max {fpsList.Max():F1})";
            if (fpsSpread > _settings.FpsFailTolerance)
                report.Summary.SessionMessages.Add($"{fpsPrefix}FPS spread {fpsSpread:F1} exceeds fail tolerance");
            else if (fpsSpread > _settings.FpsWarningTolerance)
                report.Summary.SessionMessages.Add($"{fpsPrefix}FPS spread {fpsSpread:F1} (warning)");
            else
                fpsText += " OK";

            if (string.IsNullOrEmpty(report.Summary.FpsSpreadDisplay) || report.Summary.FpsSpreadDisplay == "-")
                report.Summary.FpsSpreadDisplay = fpsText;
            else
                report.Summary.FpsSpreadDisplay += "; " + fpsText;
            report.Summary.FpsSpread = Math.Max(report.Summary.FpsSpread ?? 0, fpsSpread);
        }
        else if (fpsList.Count == 1 && string.IsNullOrEmpty(report.Summary.FpsSpreadDisplay))
            report.Summary.FpsSpreadDisplay = $"{fpsList[0]:F1}";

        if (durations.Count >= 2)
            report.Summary.DurationSpreadSeconds = Math.Max(
                report.Summary.DurationSpreadSeconds ?? 0,
                durations.Max() - durations.Min());
    }

    private Dictionary<string, ExpectedCameraSettings> BuildCombinedExpectedBySlot(
        List<IGrouping<string, VideoFileEntry>> sessionGroups,
        AppConfig? appConfig)
    {
        var combined = new Dictionary<string, ExpectedCameraSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in sessionGroups)
        {
            var (bySlot, _) = _expectedResolver.Resolve(group.Key, group.ToList(), appConfig);
            foreach (var kv in bySlot)
                combined[kv.Key] = kv.Value;
        }

        return combined;
    }

    private static BehaviorSessionVerificationResult AggregateBehaviorResults(
        IReadOnlyList<BehaviorSessionVerificationResult> sessions)
    {
        if (sessions.Count == 0)
            return new BehaviorSessionVerificationResult
            {
                FinalVerdict = VerificationVerdict.NotChecked,
                SummaryMessage = "No sessions analyzed."
            };

        if (sessions.Count == 1)
            return sessions[0];

        var worst = sessions.Max(s => s.FinalVerdict);
        var failed = sessions.Count(s => s.FinalVerdict == VerificationVerdict.Fail);
        var warned = sessions.Count(s => s.FinalVerdict == VerificationVerdict.Warning);
        var passed = sessions.Count(s => s.FinalVerdict == VerificationVerdict.Pass);

        return new BehaviorSessionVerificationResult
        {
            SessionLabel = $"{sessions.Count} sessions",
            FinalVerdict = worst,
            SummaryMessage = worst switch
            {
                VerificationVerdict.Fail =>
                    $"FAIL: {failed} of {sessions.Count} session(s) failed behavioral analysis.",
                VerificationVerdict.Warning =>
                    $"WARNING: {warned} of {sessions.Count} session(s) need review; {passed} passed.",
                _ => $"PASS: All {sessions.Count} session(s) passed behavioral analysis."
            },
            RequestedFps = sessions.Average(s => s.RequestedFps),
            ActualFpsAvg = sessions.Average(s => s.ActualFpsAvg),
            DurationSecondsAvg = sessions.Average(s => s.DurationSecondsAvg),
            FrameCountAvg = (long)sessions.Average(s => s.FrameCountAvg),
            CalculatedFpsAvg = sessions.Average(s => s.CalculatedFpsAvg),
            CameraDurationDifferenceSec = sessions.Max(s => s.CameraDurationDifferenceSec),
            CameraFrameCountDifference = sessions.Max(s => s.CameraFrameCountDifference),
            ResolutionMatch = sessions.All(s => string.Equals(s.ResolutionMatch, "Correct", StringComparison.OrdinalIgnoreCase))
                ? "Correct"
                : "Mismatch or warning",
            RecordingStatus = sessions.All(s => string.Equals(s.RecordingStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                ? "Completed"
                : "Incomplete",
            CropRecommendation = sessions.Count == 1
                ? sessions[0].CropRecommendation
                : "See per-session recommendations below."
        };
    }

    private static bool CodecMatches(string expected, string actual)
    {
        var e = expected.ToLowerInvariant();
        var a = actual.ToLowerInvariant();
        if (e.Contains(a) || a.Contains(e)) return true;
        if ((e.Contains("mp4v") || e.Contains("opencv")) && (a is "mpeg4" or "mp4v")) return true;
        if (e.Contains("h264") && a is "h264" or "avc1") return true;
        return false;
    }

    private static void FillDisplays(VideoVerificationResult r)
    {
        if (r.Probe == null) return;
        if (r.ActualResolutionDisplay == "-") r.ActualResolutionDisplay = CaptureResolutionPreset.ToLabel(r.Probe.Width, r.Probe.Height);
        if (r.ExpectedResolutionDisplay != "-")
            r.ExpectedResolutionDisplay = CaptureResolutionPreset.FormatDisplayLabel(r.ExpectedResolutionDisplay);
        if (r.ActualResolutionDisplay != "-")
            r.ActualResolutionDisplay = CaptureResolutionPreset.FormatDisplayLabel(r.ActualResolutionDisplay);
        if (r.ActualFpsDisplay == "-") r.ActualFpsDisplay = $"{r.Probe.Fps:F1}";
        if (r.DurationDisplay == "-") r.DurationDisplay = $"{r.Probe.DurationSeconds:F2}s";
        if (r.CodecDisplay == "-") r.CodecDisplay = r.Probe.VideoCodec ?? "-";
    }

    private static VerificationVerdict Worst(IReadOnlyList<VerificationVerdict> list)
    {
        if (list.Any(v => v == VerificationVerdict.Fail)) return VerificationVerdict.Fail;
        if (list.Any(v => v == VerificationVerdict.Warning)) return VerificationVerdict.Warning;
        return VerificationVerdict.Pass;
    }
}
