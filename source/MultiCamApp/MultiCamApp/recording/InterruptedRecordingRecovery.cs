// Crash / sudden-close recovery for recordings (not part of the frozen cores; new in 2.1.0).
//
// Why: a forced close, crash or power loss used to leave camN.tmp.mp4 without an MP4 index
// ("moov atom not found") and no metadata — the whole recording was lost (verified by a kill test).
// Recordings are now fragmented MP4 (STABLE_CORE_V2 exception #15), which stays playable up to the
// last fragment, and every session gets a small journal while recording. On the next start this
// service finds interrupted sessions, verifies each video with ffprobe, gives playable files their
// final name (never overwriting), counts timestamp rows, and writes RECOVERY_REPORT.txt/.json.
// Reports contain only folder/file names — no absolute paths or user names.

using System.Globalization;
using System.Text;
using System.Text.Json;
using MultiCamApp.Verification;

namespace MultiCamApp.Recording;

public static class InterruptedRecordingRecovery
{
    public const string JournalFileName     = "session_in_progress.json";
    public const string InterruptedJournal  = "session_interrupted.json";
    public const string ReportTextFileName  = "RECOVERY_REPORT.txt";
    public const string ReportJsonFileName  = "recovery_report.json";
    private const string TempSuffix = ".tmp.mp4";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public sealed class Journal
    {
        public string AppVersion { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public string RequestedResolution { get; set; } = "";
        public double RequestedFps { get; set; }
        public string Container { get; set; } = "";
        public double KeyframeIntervalSeconds { get; set; }
        public int PowerlineFrequencyHz { get; set; }
        public List<JournalCamera> Cameras { get; set; } = new();
    }

    public sealed class JournalCamera
    {
        public string Slot { get; set; } = "";
        public string Device { get; set; } = "";
    }

    public sealed class CameraResult
    {
        public string Slot { get; set; } = "";
        public string? SourceFile { get; set; }
        public string? RecoveredFile { get; set; }
        public string Status { get; set; } = "";      // Recovered | AlreadyFinal | NotPlayable | NoVideo
        public long? Frames { get; set; }
        public double DurationSeconds { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int? CsvRows { get; set; }
        public string? Problem { get; set; }
    }

    public sealed class SessionResult
    {
        public string SessionFolder { get; set; } = "";
        public string DetectedUtc { get; set; } = "";
        public Journal? Journal { get; set; }
        public List<CameraResult> Cameras { get; set; } = new();
        public int RecoveredCount => Cameras.Count(c => c.Status == "Recovered");
    }

    // ── Journal (written by MainWindow when recording starts / removed after metadata) ─────────

    public static void WriteJournal(string sessionFolder, Journal journal)
    {
        var path = Path.Combine(sessionFolder, JournalFileName);
        var tmp  = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(journal, JsonOptions), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static void CompleteJournal(string sessionFolder)
    {
        try { File.Delete(Path.Combine(sessionFolder, JournalFileName)); } catch { /* best effort */ }
    }

    // ── Recovery ──────────────────────────────────────────────────────────────────────────────

    /// <summary>True when a session folder looks interrupted and has not been reported yet.</summary>
    public static bool NeedsRecovery(string sessionFolder)
    {
        if (File.Exists(Path.Combine(sessionFolder, ReportJsonFileName))) return false;
        if (File.Exists(Path.Combine(sessionFolder, JournalFileName))) return true;
        try
        {
            return Directory.EnumerateDirectories(sessionFolder, "cam*")
                .Any(d => Directory.EnumerateFiles(d, "*" + TempSuffix).Any());
        }
        catch { return false; }
    }

    /// <summary>
    /// Scans the direct sub-folders of <paramref name="outputRoot"/> changed within <paramref name="maxAge"/>
    /// and recovers interrupted sessions. <paramref name="probe"/> is normally VideoProbeService.ProbeAsync.
    /// </summary>
    public static async Task<IReadOnlyList<SessionResult>> RecoverAllAsync(
        string outputRoot, Func<string, CancellationToken, Task<VideoProbeData>> probe,
        TimeSpan maxAge, CancellationToken ct = default)
    {
        var results = new List<SessionResult>();
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot)) return results;
        var cutoff = DateTime.UtcNow - maxAge;
        IEnumerable<string> sessions;
        try
        {
            sessions = Directory.EnumerateDirectories(outputRoot)
                .Where(d => { try { return Directory.GetLastWriteTimeUtc(d) >= cutoff; } catch { return false; } })
                .Take(500).ToList();
        }
        catch { return results; }

        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            if (!NeedsRecovery(session)) continue;
            try { results.Add(await RecoverSessionAsync(session, probe, ct).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Utils.AppDiagnosticLogger.Runtime($"RECOVERY_SESSION_ERROR folder=\"{Path.GetFileName(session)}\" {ex.GetType().Name}: {ex.Message}");
            }
        }
        return results;
    }

    public static async Task<SessionResult> RecoverSessionAsync(
        string sessionFolder, Func<string, CancellationToken, Task<VideoProbeData>> probe, CancellationToken ct = default)
    {
        var result = new SessionResult
        {
            SessionFolder = Path.GetFileName(sessionFolder),
            DetectedUtc   = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
        var journalPath = Path.Combine(sessionFolder, JournalFileName);
        if (File.Exists(journalPath))
        {
            try { result.Journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)); } catch { }
        }

        foreach (var camDir in Directory.EnumerateDirectories(sessionFolder, "cam*").OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var slot = Path.GetFileName(camDir);
            var cam  = new CameraResult { Slot = slot, CsvRows = CountCsvRows(camDir) };
            var tmp  = Directory.EnumerateFiles(camDir, "*" + TempSuffix).FirstOrDefault();
            var finalName = Path.Combine(camDir, slot + ".mp4");

            if (tmp is null)
            {
                cam.Status = File.Exists(finalName) ? "AlreadyFinal" : "NoVideo";
                if (cam.Status == "AlreadyFinal") Fill(cam, await probe(finalName, ct).ConfigureAwait(false));
                result.Cameras.Add(cam);
                continue;
            }

            cam.SourceFile = Path.GetFileName(tmp);
            var data = await probe(tmp, ct).ConfigureAwait(false);
            if (!data.Success || data.FrameCount is null or 0)
            {
                cam.Status  = "NotPlayable";
                cam.Problem = data.Error ?? "no decodable video";
                result.Cameras.Add(cam);
                continue;
            }

            var target = File.Exists(finalName) ? Path.Combine(camDir, slot + "_recovered.mp4") : finalName;
            for (var n = 2; File.Exists(target); n++)
                target = Path.Combine(camDir, $"{slot}_recovered_{n}.mp4");
            File.Move(tmp, target);
            cam.Status        = "Recovered";
            cam.RecoveredFile = Path.GetFileName(target);
            Fill(cam, data);
            result.Cameras.Add(cam);
        }

        WriteReports(sessionFolder, result);
        if (File.Exists(journalPath))
        {
            try { File.Move(journalPath, Path.Combine(sessionFolder, InterruptedJournal), overwrite: true); } catch { }
        }
        Utils.AppDiagnosticLogger.Runtime(
            $"RECOVERY_SESSION folder=\"{result.SessionFolder}\" cameras={result.Cameras.Count} recovered={result.RecoveredCount} " +
            string.Join(" ", result.Cameras.Select(c => $"{c.Slot}={c.Status}/{c.Frames}f/{c.CsvRows}rows")));
        return result;
    }

    private static void Fill(CameraResult cam, VideoProbeData d)
    {
        cam.Frames = d.FrameCount;
        cam.DurationSeconds = Math.Round(d.DurationSeconds, 3);
        cam.Width = d.Width;
        cam.Height = d.Height;
    }

    public static int? CountCsvRows(string camDir)
    {
        var csv = Directory.EnumerateFiles(camDir, "*timestamps*.csv").FirstOrDefault();
        if (csv is null) return null;
        try
        {
            var lines = File.ReadLines(csv).Count(l => !string.IsNullOrWhiteSpace(l));
            return Math.Max(0, lines - 1);       // minus header
        }
        catch { return null; }
    }

    private static void WriteReports(string sessionFolder, SessionResult r)
    {
        File.WriteAllText(Path.Combine(sessionFolder, ReportJsonFileName),
            JsonSerializer.Serialize(r, JsonOptions), new UTF8Encoding(false));

        var sb = new StringBuilder();
        sb.AppendLine("MULTICAMAPP RECOVERY REPORT");
        sb.AppendLine("Status: INTERRUPTED RECORDING — the app closed before the recording was stopped normally.");
        sb.AppendLine($"Session folder: {r.SessionFolder}");
        sb.AppendLine($"Detected (UTC): {r.DetectedUtc}");
        if (r.Journal is { } j)
        {
            sb.AppendLine($"Recording started (UTC): {j.StartedUtc}");
            sb.AppendLine($"App version: {j.AppVersion}");
            sb.AppendLine($"Requested: {j.RequestedResolution} @ {j.RequestedFps.ToString("0.##", CultureInfo.InvariantCulture)} fps; container: {j.Container}");
            foreach (var c in j.Cameras) sb.AppendLine($"  {c.Slot}: {c.Device}");
        }
        else sb.AppendLine("Session journal: not found (recorded by an older version or removed).");
        sb.AppendLine();
        foreach (var c in r.Cameras)
        {
            sb.AppendLine($"[{c.Slot}] {c.Status}");
            if (c.SourceFile is not null)    sb.AppendLine($"  Source file: {c.SourceFile}");
            if (c.RecoveredFile is not null) sb.AppendLine($"  Recovered file: {c.RecoveredFile}");
            if (c.Frames is not null)        sb.AppendLine($"  Video: {c.Width}x{c.Height}, {c.Frames} frames, {c.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
            if (c.CsvRows is not null)       sb.AppendLine($"  Timestamp CSV rows: {c.CsvRows}" + (c.Frames is { } f ? $" (video frames - CSV rows = {f - c.CsvRows})" : ""));
            if (c.Problem is not null)       sb.AppendLine($"  Problem: {c.Problem}");
        }
        sb.AppendLine();
        sb.AppendLine("Notes:");
        sb.AppendLine("* camN_metadata.txt/json were not written because the recording did not stop normally.");
        sb.AppendLine("* The last fraction of a second to about one second before the interruption may be missing");
        sb.AppendLine("  from the video (last incomplete fragment) and from the timestamp CSV (last unflushed rows).");
        sb.AppendLine("* Run Video Verification on this session before using it for analysis.");
        File.WriteAllText(Path.Combine(sessionFolder, ReportTextFileName), sb.ToString(), new UTF8Encoding(false));
    }
}
