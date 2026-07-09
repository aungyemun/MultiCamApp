////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — standalone session scanner and verifier for V2 recordings.
// Does NOT call VideoVerificationService (STABLE_CORE_V1); runs independently.

using MultiCamApp.Metadata;
using MultiCamApp.Recording.Writers;
using MultiCamApp.Utils;

namespace MultiCamApp.Verification;

/// <summary>
/// Discovers V2 recording sessions under a root folder, runs <see cref="V2RecordingVerifier"/>
/// per slot, and produces <see cref="V2SessionVerificationGroup"/> results.
/// Works without ffprobe — all data comes from V2 metadata JSON and file-system checks.
/// </summary>
public sealed class V2VerificationRunner
{
    private readonly V2RecordingVerifier _verifier = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Scans and verifies all V2 sessions under <paramref name="rootFolder"/>.</summary>
    public IReadOnlyList<V2SessionVerificationGroup> Run(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return [];

        var sessionFolders = DiscoverV2SessionFolders(rootFolder);
        var groups = new List<V2SessionVerificationGroup>(sessionFolders.Count);

        foreach (var sessionFolder in sessionFolders)
        {
            var slots = DiscoverV2Slots(sessionFolder);
            if (slots.Count == 0) continue;

            var group = new V2SessionVerificationGroup
            {
                SessionFolder = sessionFolder,
                SessionLabel  = Path.GetFileName(
                    sessionFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            };

            foreach (var (slotName, cameraFolder) in slots)
            {
                var fileSet  = RecordingFileSet.Create(cameraFolder, slotName);
                var metadata = V2MetadataReader.TryReadForVideo(fileSet.FinalVideoPath);
                var vResult  = _verifier.Verify(fileSet);
                group.Slots.Add(new V2SlotVerificationResult
                {
                    SlotName           = slotName,
                    FileSet            = fileSet,
                    Metadata           = metadata,
                    VerificationResult = vResult,
                });
            }

            groups.Add(group);
        }

        AppDiagnosticLogger.Runtime(
            $"V2_VERIFY_RUN root=\"{PrivacySanitizer.FileNameOnly(rootFolder)}\" sessions={groups.Count} " +
            $"summary=[{string.Join("; ", groups.Select(g => $"{g.SessionLabel}:{g.OverallStatus}({g.Slots.Count} slots)"))}]");

        return groups;
    }

    /// <summary>
    /// Converts a <see cref="V2SessionVerificationGroup"/> to <see cref="VerificationTableRow"/>
    /// entries that can be inserted into the verification page's session groups.
    /// ffprobe-derived values (ContainerFps, ContainerDuration) are filled when provided.
    /// </summary>
    public static IReadOnlyList<VerificationTableRow> ToTableRows(V2SessionVerificationGroup group)
    {
        var rows = new List<VerificationTableRow>(group.Slots.Count);
        foreach (var slot in group.Slots)
        {
            var m = slot.Metadata;
            var v = slot.VerificationResult;

            bool hasFail = v.Failures.Any();
            bool hasWarn = v.Warnings.Any();

            var row = new VerificationTableRow
            {
                Camera               = slot.SlotName,
                FileName             = File.Exists(slot.FileSet.FinalVideoPath)
                                           ? Path.GetFileName(slot.FileSet.FinalVideoPath)
                                           : $"{slot.SlotName}.mp4 (not found)",
                FilePath             = slot.FileSet.FinalVideoPath,
                CameraFolder         = slot.FileSet.CameraFolder,
                SessionFolder        = group.SessionFolder,
                SessionLabel         = group.SessionLabel,
                Device               = m?.Device ?? "-",
                RequestedFps         = m?.TargetFps > 0 ? $"{m.TargetFps:F0}" : "-",
                FramesWrittenDisplay = m?.FramesWritten > 0 ? m.FramesWritten.ToString() : "-",
                MeasuredNativeFps    = m?.MeasuredFpsFromTimestamps > 0 ? $"{m.MeasuredFpsFromTimestamps:F3}" : "-",
                MetadataStatus       = m is not null ? "V2 JSON" : "Missing",
                AuditStatus          = hasFail ? "FAIL" : (hasWarn ? "WARNING" : "PASS"),
                Result               = hasFail ? VerificationVerdict.Fail
                                     : (hasWarn ? VerificationVerdict.Warning : VerificationVerdict.Pass),
                DetailText           = BuildSlotDetailText(slot, group),
                FileSize             = FileSizeDisplay(slot.FileSet.FinalVideoPath),
                TimestampRowsDisplay = TimestampCsvStatus(slot.FileSet.TimestampCsvPath),
            };

            foreach (var issue in v.Issues)
            {
                if (issue.StartsWith("FAIL"))      row.ErrorMessages.Add(issue.Substring(5).Trim());
                else if (issue.StartsWith("WARN"))  row.WarningMessages.Add(issue.Substring(5).Trim());
            }

            rows.Add(row);
        }
        return rows;
    }

    // ── Export/report enrichment ─────────────────────────────────────────────

    /// <summary>
    /// Patches <paramref name="video"/>.Metadata (a <see cref="CameraMetadataRecord"/>, populated by
    /// the legacy schema-blind <c>MetadataParser</c>, STABLE_CORE_V1) with values already read from
    /// the recording's own V2 metadata JSON — the same authoritative-already-computed-value approach
    /// used by <c>EnrichV2SessionGroups</c>/<c>ReconcileV2SessionVerdict</c> in
    /// <c>VideoVerificationPage.xaml.cs</c> for the on-screen table, but applied to the separate
    /// object graph that "Export TXT/JSON/CSV" and the auto-saved video_audit_report.txt read from.
    /// Only fills in a field when the legacy parser left it at its zero/false/empty default; never
    /// overwrites a value the legacy parser did manage to extract.
    /// </summary>
    public static void EnrichLegacyMetadataRecord(VideoVerificationResult video, V2RecordingMetadata meta)
    {
        var m = video.Metadata;
        if (m == null) return; // legacy MetadataParser found no file at all — nothing to patch

        if (m.RequestedFps <= 0 && meta.TargetFps > 0)
            m.RequestedFps = meta.TargetFps;
        if (m.MeasuredCameraFps <= 0 && meta.MeasuredFpsFromTimestamps > 0)
            m.MeasuredCameraFps = meta.MeasuredFpsFromTimestamps;
        if (m.ActualFps <= 0 && meta.MeasuredFpsFromTimestamps > 0)
            m.ActualFps = meta.MeasuredFpsFromTimestamps;
        if (m.ContainerFps <= 0 && video.Probe?.Fps > 0)
            m.ContainerFps = video.Probe.Fps;
        if (m.FrameCount <= 0 && meta.FramesWritten > 0)
            m.FrameCount = meta.FramesWritten;
        // V2's Original Capture Mode writes every captured frame — there is no separate
        // "captured but not written" count the way the legacy OpenCV pipeline tracks it.
        if (m.FramesCaptured <= 0 && meta.FramesWritten > 0)
            m.FramesCaptured = meta.FramesWritten;
        if (m.WallClockDurationSeconds <= 0 && meta.ResolvedDurationSeconds > 0)
            m.WallClockDurationSeconds = meta.ResolvedDurationSeconds;
        if (m.FrameBasedDurationSeconds <= 0 && meta.ResolvedDurationSeconds > 0)
            m.FrameBasedDurationSeconds = meta.ResolvedDurationSeconds;
        if (m.CaptureIntervalMeanMs <= 0 && meta.MeanFrameIntervalMs > 0)
            m.CaptureIntervalMeanMs = meta.MeanFrameIntervalMs;
        if (m.CaptureIntervalMinMs <= 0 && meta.MinFrameIntervalMs > 0)
            m.CaptureIntervalMinMs = meta.MinFrameIntervalMs;
        if (m.CaptureIntervalMaxMs <= 0 && meta.MaxFrameIntervalMs > 0)
            m.CaptureIntervalMaxMs = meta.MaxFrameIntervalMs;
        if (m.CaptureIntervalStdMs <= 0 && meta.IntervalStdMs > 0)
            m.CaptureIntervalStdMs = meta.IntervalStdMs;
        if (string.IsNullOrEmpty(m.FrameTimestampCsvPath) && meta.TimestampCsvWritten)
            m.FrameTimestampCsvPath = meta.FrameTimestampCsvFile;
        if (!m.FrameTimestampCsvWritten && meta.TimestampCsvWritten)
            m.FrameTimestampCsvWritten = true;
        if (m.FrameTimestampCsvRowCount <= 0 && meta.TimestampCsvRows > 0)
            m.FrameTimestampCsvRowCount = meta.TimestampCsvRows;
        if (meta.IsOriginalCaptureMode)
        {
            m.OriginalCaptureMode = true;
            m.RecordingTimingMode = OriginalCaptureAuditPolicy.Mode;
        }

        // Same false-positive as row.ScientificTimingStatusDisplay in EnrichV2SessionGroups — but
        // note the field that actually drives the "Scientific timing status: FAIL" error message
        // and the export verdict is VideoVerificationResult.ScientificTimingStatus (video's own
        // field, set during VerifyOneAsync), NOT CameraMetadataRecord.ScientificTimingStatus (a
        // separate, differently-populated field on video.Metadata used only by the "scientific
        // timing: X - Y" export line below). Correcting only the latter left the former — and the
        // error message it drives — silently unfixed.
        if (string.Equals(video.ScientificTimingStatus, CameraAuditStatus.Fail, StringComparison.OrdinalIgnoreCase))
        {
            var tcSource = meta.InterCameraTimingConfidence is { Length: > 0 } ic ? ic : meta.TimingConfidence;
            if (tcSource is { Length: > 0 })
            {
                var normalized = MultiCamApp.Ui.Pages.VideoVerificationPage.SessionGroupViewModel.NormalizeSessionResult(tcSource);
                video.ScientificTimingStatus = normalized;
                m.ScientificTimingStatus = normalized;
                if (normalized != CameraAuditStatus.Fail)
                    video.ErrorMessages.RemoveAll(e => e.Contains("Scientific timing status", StringComparison.OrdinalIgnoreCase));
            }
        }

        // CameraAuditStatus.FromVideoResult checks video.Verdict == Fail FIRST, before ever looking
        // at ScientificTimingStatus — so any code that re-derives status by calling
        // FromVideoResult(video) fresh (e.g. ExportVideoAuditReportAsync's per-camera "status:" line)
        // kept showing FAIL even after the fix above, because video.Verdict itself was never touched.
        // Uses the same session-wide VerificationGlobalSessionResult/VerificationSessionResult value
        // ReconcileV2SessionVerdict already uses to correct row.AuditStatus on-screen — not the
        // ScientificTimingStatus/timing-confidence value above, which is a differently-scoped field
        // and would otherwise disagree with what the table already shows for this same camera.
        if (video.Verdict == VerificationVerdict.Fail)
        {
            var sessionResult = meta.VerificationGlobalSessionResult is { Length: > 0 } gr ? gr : meta.VerificationSessionResult;
            if (sessionResult is { Length: > 0 })
            {
                var sessionNormalized = MultiCamApp.Ui.Pages.VideoVerificationPage.SessionGroupViewModel.NormalizeSessionResult(sessionResult);
                // NormalizeSessionResult returns the short vocabulary ("PASS"/"WARNING"/"FAIL"), not
                // CameraAuditStatus's long constants (CameraAuditStatus.PassWithWarning ==
                // "PASS_WITH_WARNING") — comparing against the wrong vocabulary here would silently
                // take the Pass branch for every WARNING session, the same class of bug documented
                // for NormalizedResultToVerdict elsewhere in this codebase.
                if (sessionNormalized != CameraAuditStatus.Fail)
                    video.Verdict = sessionNormalized == "WARNING"
                        ? VerificationVerdict.Warning
                        : VerificationVerdict.Pass;
            }
        }
        if (video.MetadataCompletenessPercent <= 0)
            video.MetadataCompletenessPercent = 100.0;
    }

    // ── Detail text ───────────────────────────────────────────────────────────

    private static string T(string key, string fallback) =>
        MultiCamApp.Ui.Pages.VideoVerificationPage.CurrentLanguage?[key] is { Length: > 0 } v ? v : fallback;

    /// <summary>
    /// <see cref="V2ControlInfo.ReadbackStatus"/> is read verbatim from the recording's own metadata
    /// JSON — but that JSON stores the *already-localized* string `MainWindow.xaml.cs`'s `CtrlResult2`
    /// baked in at recording time (e.g. "デバイス/ドライバー非対応" for a session recorded in Japanese
    /// mode), not a language-neutral code. Displaying it as-is means the Video Verification page shows
    /// whichever language a video happened to be *recorded* in, regardless of the page's *current*
    /// language selection — e.g. viewing a Japanese-mode recording while the UI is set to English still
    /// shows Japanese control-status text. `CtrlResult2` only ever produces one of a small closed set of
    /// phrases, so this maps any of them (in either language) back to the current UI language.
    /// Anything outside that known set (there shouldn't be any) passes through unchanged.
    /// </summary>
    private static string NormalizeControlStatus(string? raw) => raw switch
    {
        "Applied" or "適用済み" => T("v2ControlApplied", "Applied"),
        "Applied but readback mismatch" or "適用済み（読み取り値不一致）" => T("v2CtrlMismatch", "Applied but readback mismatch"),
        "Not supported by device/driver" or "デバイス/ドライバー非対応" => T("v2CtrlUnsupported", "Not supported by device/driver"),
        "Failed" or "失敗" => T("v2CtrlFailed", "Failed"),
        "Not attempted" or "未試行" => T("v2CtrlNotAttempted", "Not attempted"),
        "Supported" or "対応" => T("v2CtrlSupported", "Supported"),
        "Unknown" or "不明" => T("v2CtrlUnknown", "Unknown"),
        _ => raw ?? "",
    };

    /// <summary>
    /// Same problem as <see cref="NormalizeControlStatus"/>, for <see cref="V2ControlInfo.Warning"/>:
    /// the recorded JSON may carry an English warning even from a Japanese-mode recording (any file
    /// recorded before `MainWindow.xaml.cs`'s `LocalizeControlWarning` was added), or vice versa.
    /// Recognizes the same fixed set of known warning strings in either language and normalizes to
    /// the current UI language; unrecognized text (including raw driver/exception messages) passes
    /// through unchanged rather than guessing.
    /// </summary>
    private static string? NormalizeControlWarning(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return raw switch
        {
            "BacklightCompensation not supported on this device."
                or "BacklightCompensationはこのデバイスでサポートされていません。" =>
                T("v2WarnBacklightUnsupported", "BacklightCompensation not supported on this device."),
            "OpticalImageStabilizationControl not supported on this device."
                or "OpticalImageStabilizationControlはこのデバイスでサポートされていません。" =>
                T("v2WarnOisUnsupported", "OpticalImageStabilizationControl not supported on this device."),
            "WhiteBalanceControl not supported on this device."
                or "WhiteBalanceControlはこのデバイスでサポートされていません。" =>
                T("v2WarnWhiteBalanceUnsupported", "WhiteBalanceControl not supported on this device."),
            "Flicker reduction disabled in VideoEngineSettings."
                or "フリッカー低減はVideoEngineSettingsで無効化されています。" =>
                T("v2WarnFlickerDisabledInSettings", "Flicker reduction disabled in VideoEngineSettings."),
            "Control not applied in this build."
                or "この製品版ではこのコントロールは適用されません。" =>
                T("v2WarnControlNotApplied", "Control not applied in this build."),
            "CameraControlManagerV2 not attached to an open camera session."
                or "CameraControlManagerV2が開いているカメラセッションに接続されていません。" =>
                T("v2WarnManagerNotAttached", "CameraControlManagerV2 not attached to an open camera session."),
            _ when raw.StartsWith("Skipped — exposure control unsupported", StringComparison.Ordinal)
                || raw.StartsWith("スキップ — このデバイスでは露出制御が非対応", StringComparison.Ordinal) =>
                T("v2WarnLlcSkippedExposureUnsupported",
                    "Skipped — exposure control unsupported on this device, so disabling low-light compensation would only darken the image with no reproducibility benefit."),
            _ when raw.StartsWith("Flicker reduction is not exposed", StringComparison.Ordinal)
                || raw.StartsWith("フリッカー低減はこのシステムのWinRT", StringComparison.Ordinal) =>
                T("v2WarnFlickerNotExposed",
                    "Flicker reduction is not exposed by the WinRT camera control API on this system (VideoDeviceController.FlickerReductionControl is absent from all installed Windows SDK metadata) and cannot be applied by MultiCamApp."),
            _ => raw,
        };
    }

    internal static string BuildV2EngineDetailSection(V2RecordingMetadata m, V2VerificationResult? v)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(T("v2EngineSectionTitle", "VideoEngineV2 Recording Engine"));
        sb.AppendLine($"  {T("v2EngineLabel", "Engine")}:   {m.Engine}");
        sb.AppendLine($"  {T("v2BackendLabel", "Backend")}:  {m.Backend}");
        sb.AppendLine($"  {T("v2EncoderLabel", "Encoder")}:  {(m.HardwareEncoderUsed ? T("v2EncoderHardware", "Hardware H.264 (NVENC / QuickSync)") : T("v2EncoderSoftware", "Software H.264 (MediaFoundation)"))}");
        if (!string.IsNullOrWhiteSpace(m.EncoderDescription))
            sb.AppendLine($"  {T("v2EncoderDescriptionLabel", "Encoder description")}: {m.EncoderDescription}");
        if (m.ColorTaggingApplied)
            sb.AppendLine($"  {T("v2ColorTaggingLabel", "Color tagging")}: {m.ColorPrimaries} primaries / {m.ColorTransferFunction} transfer / {m.ColorMatrix} matrix / {m.ColorRange}");
        sb.AppendLine($"  {T("v2StatusLabel", "Status")}:   {m.Status}");
        sb.AppendLine();

        sb.AppendLine(T("v2CameraSectionTitle", "Camera (from V2 metadata)"));
        sb.AppendLine($"  {T("v2DeviceLabel", "Device")}:     {(string.IsNullOrWhiteSpace(m.Device) ? "-" : m.Device)}");
        sb.AppendLine($"  {T("v2ResolutionLabel", "Resolution")}: {(string.IsNullOrWhiteSpace(m.Resolution) ? "-" : m.Resolution)}");
        sb.AppendLine($"  {T("v2TargetFpsLabel", "Target FPS")}: {(m.TargetFps > 0 ? $"{m.TargetFps:F0}" : "-")}");
        sb.AppendLine($"  {T("v2FramesWrittenLabel", "Frames written")}: {(m.FramesWritten > 0 ? m.FramesWritten.ToString() : "-")}");
        if (!string.IsNullOrWhiteSpace(m.IntegrityVerdict))
        {
            sb.AppendLine($"  {T("v2FrameIntegrityLabel", "Frame integrity")}: {m.IntegrityVerdict}" +
                (m.CsvRowsDiff is { } diff && diff != 0
                    ? " " + string.Format(T("v2CsvRowsDiffNote", "(timestamp CSV has {0} more row(s) than frames written)"), diff)
                    : ""));
            if (m.IntegrityVerdict == "WARN_CSV_MISMATCH")
                sb.AppendLine($"    Note: {T("v2CsvMismatchNote", "the timestamp CSV recorded more rows than the encoder wrote frames — usually real camera/USB timing jitter (see Timestamp CSV for per-frame gaps), not data loss.")}");
        }
        sb.AppendLine();

        if (m.Controls.Count > 0)
        {
            sb.AppendLine(T("v2CameraControlsTitle", "Camera Controls"));
            foreach (var c in m.Controls)
            {
                var rb   = c.ReadbackValue.HasValue ? $" (readback: {c.ReadbackValue.Value:F4})" : "";
                var warn = !string.IsNullOrWhiteSpace(c.Warning) ? $"  [!] {NormalizeControlWarning(c.Warning)}" : "";
                var status = c.Applied
                    ? T("v2ControlApplied", "Applied")
                    : string.IsNullOrWhiteSpace(c.ReadbackStatus) ? T("v2ControlNotApplied", "Not applied") : NormalizeControlStatus(c.ReadbackStatus);
                sb.AppendLine($"  {c.Control}: {status}{rb}{warn}");
            }
            sb.AppendLine();
        }

        if (v is not null)
        {
            sb.AppendLine($"{T("v2FileCheckLabel", "V2 File Check")}: {(v.Passed ? "PASS" : "FAIL")}");
            foreach (var issue in v.Issues)
                sb.AppendLine($"  {issue}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildSlotDetailText(V2SlotVerificationResult slot, V2SessionVerificationGroup group)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Format(T("verifySessionLabel", "Session: {0}"), group.SessionLabel));
        sb.AppendLine($"{T("v2CameraLabel", "Camera")}:  {slot.SlotName}");
        sb.AppendLine();

        if (slot.Metadata is not null)
            sb.AppendLine(BuildV2EngineDetailSection(slot.Metadata, slot.VerificationResult));
        else
        {
            sb.AppendLine(T("v2MetadataNotFound", "V2 metadata JSON not found."));
            sb.AppendLine();
            sb.AppendLine($"{T("v2FileCheckLabel", "V2 File Check")}: {(slot.VerificationResult.Passed ? "PASS" : "FAIL")}");
            foreach (var issue in slot.VerificationResult.Issues)
                sb.AppendLine($"  {issue}");
        }

        return sb.ToString().TrimEnd();
    }

    // ── Discovery helpers ─────────────────────────────────────────────────────

    private static IReadOnlyList<string> DiscoverV2SessionFolders(string rootFolder)
    {
        var result = new List<string>();
        try
        {
            // Check subfolders of rootFolder (normal case: each subfolder is a session)
            foreach (var dir in Directory.EnumerateDirectories(rootFolder).OrderBy(d => d))
            {
                if (HasAnyV2Slot(dir)) result.Add(dir);
            }
            // Also treat rootFolder itself as a session (single-session scan)
            if (result.Count == 0 && HasAnyV2Slot(rootFolder))
                result.Add(rootFolder);
        }
        catch { }
        return result;
    }

    private static bool HasAnyV2Slot(string sessionFolder)
    {
        try
        {
            foreach (var camDir in Directory.EnumerateDirectories(sessionFolder))
            {
                if (!Path.GetFileName(camDir).StartsWith("cam", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var jsonFile in Directory.EnumerateFiles(camDir, "*_metadata.json"))
                {
                    if (V2MetadataReader.TryRead(jsonFile) is not null) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static IReadOnlyList<(string slotName, string cameraFolder)> DiscoverV2Slots(string sessionFolder)
    {
        var result = new List<(string, string)>();
        try
        {
            foreach (var camDir in Directory.EnumerateDirectories(sessionFolder)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(camDir);
                if (!name.StartsWith("cam", StringComparison.OrdinalIgnoreCase)) continue;

                bool isV2 = false;
                foreach (var jsonFile in Directory.EnumerateFiles(camDir, "*_metadata.json"))
                {
                    if (V2MetadataReader.TryRead(jsonFile) is not null) { isV2 = true; break; }
                }
                if (isV2) result.Add((name.ToLowerInvariant(), camDir));
            }
        }
        catch { }
        return result;
    }

    // ── File helpers ──────────────────────────────────────────────────────────

    private static string FileSizeDisplay(string path)
    {
        try
        {
            if (!File.Exists(path)) return "not found";
            var bytes = new FileInfo(path).Length;
            return bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:F1} MB"
                 : bytes >= 1_000     ? $"{bytes / 1_000.0:F0} KB"
                 : $"{bytes} B";
        }
        catch { return "-"; }
    }

    private static string TimestampCsvStatus(string csvPath)
    {
        try
        {
            if (!File.Exists(csvPath)) return "Missing";
            var lines = File.ReadAllLines(csvPath);
            return lines.Length > 1 ? $"{lines.Length - 1} rows" : "Empty";
        }
        catch { return "Error"; }
    }
}

// ── Result types ──────────────────────────────────────────────────────────────

public sealed class V2SessionVerificationGroup
{
    public string SessionFolder { get; init; } = "";
    public string SessionLabel  { get; init; } = "";
    public List<V2SlotVerificationResult> Slots { get; } = [];
    public bool AllPassed => Slots.All(s => s.VerificationResult.Passed);
    public string OverallStatus =>
        Slots.Any(s => s.VerificationResult.Failures.Any()) ? "FAIL"
      : Slots.Any(s => s.VerificationResult.Warnings.Any()) ? "WARNING"
      : "PASS";
}

public sealed class V2SlotVerificationResult
{
    public string                  SlotName           { get; init; } = "";
    public RecordingFileSet        FileSet            { get; init; } = new();
    public V2RecordingMetadata?    Metadata           { get; init; }
    public V2VerificationResult    VerificationResult { get; init; } = new();
}
