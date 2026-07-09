////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — verification for V2 recordings.
// Does NOT modify or replace the existing VideoVerificationService (STABLE_CORE_V1).

using MultiCamApp.Recording.Writers;
using MultiCamApp.Utils;

namespace MultiCamApp.Verification;

/// <summary>
/// Verifies a completed VideoEngineV2 cam1 recording.
/// Checks the same quality gates as the legacy verifier but also validates
/// V2-specific artefacts (timestamp CSV, temp file cleanup, encoder metadata fields).
/// </summary>
/// <remarks>
/// Does not modify or call the existing <c>VideoVerificationService</c>.
/// Does not compare inactive cameras.
/// </remarks>
public sealed class V2RecordingVerifier
{
    // Issue strings are always displayed through V2VerificationRunner.BuildV2EngineDetailSection,
    // which already reads MultiCamApp.Ui.Pages.VideoVerificationPage.CurrentLanguage for everything
    // else in that panel — but these messages were never routed through it, so the "V2 File Check"
    // section stayed English in Japanese mode. Only the text after the FAIL/WARN/INFO/OK prefix is
    // localized; the prefix itself must stay literal English since callers (e.g. ToTableRows'
    // issue.StartsWith("FAIL")/"WARN" routing) parse it directly.
    private static string T(string key, string fallback) =>
        MultiCamApp.Ui.Pages.VideoVerificationPage.CurrentLanguage?[key] is { Length: > 0 } v ? v : fallback;

    /// <summary>Verifies a finalised <see cref="RecordingFileSet"/> against pass conditions.</summary>
    public V2VerificationResult Verify(RecordingFileSet fileSet, V2VerificationOptions? options = null)
    {
        options ??= V2VerificationOptions.Default;
        var issues = new List<string>();

        // 1. Final MP4 must exist and not be empty
        if (!File.Exists(fileSet.FinalVideoPath))
        {
            // Check if recording was never finalised (temp file still exists)
            if (File.Exists(fileSet.TempVideoPath))
                issues.Add("FAIL: " + string.Format(
                    T("v2IssueTempFileStillExists", "Temp file still exists — recording was not finalised. Temp: {0}"),
                    Path.GetFileName(fileSet.TempVideoPath)));
            else
                issues.Add("FAIL: " + string.Format(T("v2IssueFinalMp4NotFound", "Final MP4 not found: {0}"), fileSet.FinalVideoPath));
        }
        else
        {
            var mp4Info = new FileInfo(fileSet.FinalVideoPath);
            if (mp4Info.Length == 0)
                issues.Add("FAIL: " + T("v2IssueFinalMp4Empty", "Final MP4 is empty (0 bytes)."));
            else if (mp4Info.Length < 10_000)
                issues.Add("WARN: " + string.Format(T("v2IssueFinalMp4TooSmall", "Final MP4 is very small ({0} bytes) — may be corrupt."), mp4Info.Length));
        }

        // 2. Temp file must NOT exist after successful finalisation
        if (fileSet.IsFinalized && File.Exists(fileSet.TempVideoPath))
            issues.Add("FAIL: " + T("v2IssueTempFileAfterFinalize", "Temp file still present after finalisation — rename did not complete."));

        // 3. Timestamp CSV must exist if enabled
        if (options.RequireTimestampCsv)
        {
            if (!File.Exists(fileSet.TimestampCsvPath))
                issues.Add("FAIL: " + string.Format(T("v2IssueTimestampCsvNotFound", "Timestamp CSV not found: {0}"), fileSet.TimestampCsvPath));
            else
            {
                var csvResult = ValidateTimestampCsv(fileSet.TimestampCsvPath);
                issues.AddRange(csvResult);
            }
        }

        // 4. Metadata JSON must exist
        if (options.RequireMetadataJson && !File.Exists(fileSet.MetadataJsonPath))
            issues.Add("WARN: " + string.Format(T("v2IssueMetadataJsonNotFound", "Metadata JSON not found: {0}"), fileSet.MetadataJsonPath));

        // 5. Metadata TXT must exist
        if (options.RequireMetadataTxt && !File.Exists(fileSet.MetadataTxtPath))
            issues.Add("WARN: " + string.Format(T("v2IssueMetadataTxtNotFound", "Metadata TXT not found: {0}"), fileSet.MetadataTxtPath));

        bool passed = !issues.Any(i => i.StartsWith("FAIL"));

        // Only log when there's something actionable — a clean pass on every verify would just
        // add noise every time the Video Verification page runs. FAIL/WARN issues are exactly
        // the "why did this session get flagged" questions system-wide investigation needs.
        if (!passed || issues.Any(i => i.StartsWith("WARN")))
        {
            AppDiagnosticLogger.Runtime(
                $"V2_VERIFY_ISSUES slot=\"{PrivacySanitizer.FileNameOnly(fileSet.FinalVideoPath)}\" " +
                $"passed={passed} issues=[{string.Join(" | ", issues)}]");
        }

        return new V2VerificationResult
        {
            Passed       = passed,
            Issues       = issues.AsReadOnly(),
            FinalMp4Path = fileSet.FinalVideoPath,
        };
    }

    // ── CSV validation ─────────────────────────────────────────────────────────

    private static List<string> ValidateTimestampCsv(string csvPath)
    {
        var issues = new List<string>();
        try
        {
            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2)
            {
                issues.Add("FAIL: " + T("v2IssueCsvNoDataRows", "Timestamp CSV has no data rows (header only or empty)."));
                return issues;
            }

            // Validate header
            var header = lines[0];
            if (!header.Contains("frameIndex"))
                issues.Add("WARN: " + string.Format(T("v2IssueCsvHeaderMissingColumn", "Timestamp CSV header does not contain '{0}'."), "frameIndex"));
            if (!header.Contains("captureTimestampUtc"))
                issues.Add("WARN: " + string.Format(T("v2IssueCsvHeaderMissingColumn", "Timestamp CSV header does not contain '{0}'."), "captureTimestampUtc"));
            if (!header.Contains("droppedFrameWarning"))
                issues.Add("WARN: " + string.Format(T("v2IssueCsvHeaderMissingColumn", "Timestamp CSV header does not contain '{0}'."), "droppedFrameWarning"));

            // Count dropped frame warnings
            long dataRows    = lines.Length - 1;
            long droppedRows = lines.Skip(1).Count(l => l.Split(',').Length > 7 && l.Split(',')[7] == "1");

            if (droppedRows > 0)
                issues.Add("INFO: " + string.Format(
                    T("v2IssueCsvDroppedFramesReported", "{0} dropped frame(s) reported in timestamp CSV ({1} total rows)."),
                    droppedRows, dataRows));

            issues.Add("OK: " + string.Format(T("v2IssueCsvOk", "Timestamp CSV has {0} data rows, {1} dropped frames."), dataRows, droppedRows));
        }
        catch (Exception ex)
        {
            issues.Add("FAIL: " + string.Format(T("v2IssueCsvReadFailed", "Could not read timestamp CSV: {0}"), ex.Message));
        }
        return issues;
    }
}

// ── Result and options types ──────────────────────────────────────────────────

/// <summary>Result of a V2 recording verification pass.</summary>
public sealed class V2VerificationResult
{
    public bool Passed { get; init; }
    /// <summary>All issues found — prefix FAIL = blocking, WARN = advisory, INFO = informational.</summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    public string? FinalMp4Path { get; init; }

    public IEnumerable<string> Failures  => Issues.Where(i => i.StartsWith("FAIL"));
    public IEnumerable<string> Warnings  => Issues.Where(i => i.StartsWith("WARN"));
    public IEnumerable<string> Infos     => Issues.Where(i => i.StartsWith("INFO") || i.StartsWith("OK"));

    public string ToSummaryString() =>
        $"V2 Verification: {(Passed ? "PASSED" : "FAILED")}\n" +
        string.Join("\n", Issues.Select(i => $"  {i}"));
}

/// <summary>Pass/fail options for <see cref="V2RecordingVerifier"/>.</summary>
public sealed class V2VerificationOptions
{
    public bool RequireTimestampCsv  { get; init; } = true;
    public bool RequireMetadataJson  { get; init; } = true;
    public bool RequireMetadataTxt   { get; init; } = false;

    public static V2VerificationOptions Default { get; } = new();
}
