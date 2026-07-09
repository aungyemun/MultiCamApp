////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// Deep Verify — on-demand, independent per-frame MD5 duplicate-frame check.
// Not part of STABLE_CORE_V1: this is a new, additive feature layered on top of the protected
// verification pipeline, not a modification to it. It never runs automatically — the existing
// Scan/Verify flow (VideoScanner/VideoProbeService/VideoVerificationService, all STABLE_CORE_V1)
// is completely untouched and remains the default, fast path.
//
// Why this needs full ffmpeg.exe and not just the already-bundled ffprobe.exe: ffprobe only
// inspects container/stream metadata (and, incidentally, can force a full decode — see
// VideoProbeService — which already catches bitstream corruption). Exact per-frame duplicate
// detection requires actually decoding every frame and hashing the pixel data, which is the
// `-f framemd5` output muxer — an ffmpeg-only feature, not exposed by ffprobe.

using System.Diagnostics;
using MultiCamApp.Core;

namespace MultiCamApp.Verification;

/// <summary>
/// Runs `ffmpeg -f framemd5` on a single video file and counts exact duplicate frames by
/// comparing per-frame MD5 hashes of the decoded pixel data. Slow (roughly real-time relative
/// to the recording's own duration) and CPU-bound — always on-demand, never automatic.
/// </summary>
public sealed class DeepVerifyService
{
    private readonly VerificationSettings _settings;
    private string? _ffmpegPath;
    private bool _resolved;

    public DeepVerifyService(VerificationSettings settings) => _settings = settings;

    public bool IsAvailable => ResolveFfmpeg() != null;

    public string MissingToolMessage =>
        "Deep Verify tool (ffmpeg) is missing. Please reinstall MultiCamApp or check runtime/ffmpeg.";

    /// <summary>
    /// Decodes every frame of <paramref name="filePath"/> and hashes it, then reports how many
    /// of those hashes are exact repeats (true duplicate/frozen frames — not near-identical,
    /// perceptual duplicates, which this deliberately does not attempt; see
    /// feedback_duplicate_frame_detection_method in project history for why exact MD5 hashing
    /// was chosen over a perceptual-similarity approach).
    /// </summary>
    public async Task<DeepVerifyFileResult> VerifyFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new DeepVerifyFileResult { FilePath = filePath };

        if (!File.Exists(filePath))
        {
            result.Error = "File does not exist";
            return result;
        }

        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg == null)
        {
            result.Error = MissingToolMessage;
            return result;
        }

        var timeoutSeconds = _settings.DeepVerifyTimeoutSeconds > 0 ? _settings.DeepVerifyTimeoutSeconds : 300;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        Process? proc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                // -v error: suppress ffmpeg's normal banner/progress noise, only report real
                // decode errors on stderr. -map 0:v:0: hash the first video stream only (this
                // app's recordings are video-only, but audio-less inputs make this a no-op).
                Arguments = $"-v error -i \"{filePath}\" -map 0:v:0 -f framemd5 -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            proc = Process.Start(psi);
            if (proc == null)
            {
                result.Error = "Could not start ffmpeg";
                return result;
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linkedCts.Token);

            string stdout, stderr;
            try
            {
                await proc.WaitForExitAsync(linkedCts.Token);
                stdout = await stdoutTask;
                stderr = await stderrTask;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                TryKill(proc);
                result.Error = $"Deep Verify timed out after {timeoutSeconds}s";
                return result;
            }

            if (proc.ExitCode != 0)
            {
                result.Error = string.IsNullOrWhiteSpace(stderr) ? $"ffmpeg exit {proc.ExitCode}" : stderr.Trim();
                return result;
            }

            ParseFrameMd5(stdout, result);
            result.Success = result.TotalFramesHashed > 0;
            if (!result.Success)
                result.Error = "No frames decoded";
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        finally
        {
            result.Elapsed = sw.Elapsed;
        }

        return result;
    }

    // Parses `-f framemd5` output. Lines starting with '#' are header/comment lines
    // (format/version/tb/dimensions/field-names); data lines are comma-separated with the
    // decoded frame's MD5 hash as the last field, e.g.:
    //   0,          0,          0,        1,  3110400, a5e34e8995ee464104de24b34d9625d4
    // Duplicate frames are exact hash repeats — not necessarily consecutive, since a frozen
    // camera feed could in principle repeat a hash after other frames in between.
    private static void ParseFrameMd5(string stdout, DeepVerifyFileResult result)
    {
        var hashCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var lastComma = line.LastIndexOf(',');
            if (lastComma < 0 || lastComma == line.Length - 1) continue;

            var hash = line[(lastComma + 1)..].Trim();
            if (hash.Length == 0) continue;

            total++;
            hashCounts[hash] = hashCounts.TryGetValue(hash, out var c) ? c + 1 : 1;
        }

        result.TotalFramesHashed = total;
        // Every extra occurrence beyond the first is a duplicate (e.g. a hash seen 3 times
        // contributes 2 duplicate frames, not 3), so this sums to "frames that are exact repeats
        // of an earlier frame" rather than double-counting the original.
        result.DuplicateFrameCount = hashCounts.Values.Where(c => c > 1).Sum(c => c - 1);
    }

    private static void TryKill(Process? proc)
    {
        try { if (proc is { HasExited: false }) proc.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    // Mirrors VideoProbeService.ResolveFfprobe's exact resolution chain (settings path →
    // runtime/ffmpeg/ → MULTICAMAPP_ROOT → PATH) so Deep Verify finds ffmpeg.exe under the
    // same conditions ffprobe.exe is already found under, without depending on that
    // STABLE_CORE_V1-protected class.
    private string? ResolveFfmpeg()
    {
        if (_resolved) return _ffmpegPath;
        _resolved = true;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.FfmpegPath))
        {
            candidates.Add(Path.IsPathRooted(_settings.FfmpegPath)
                ? _settings.FfmpegPath
                : Path.Combine(AppContext.BaseDirectory, _settings.FfmpegPath));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "ffmpeg", "ffmpeg.exe"));
        var root = Environment.GetEnvironmentVariable("MULTICAMAPP_ROOT");
        if (!string.IsNullOrEmpty(root))
            candidates.Add(Path.Combine(root, "runtime", "ffmpeg", "ffmpeg.exe"));

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                _ffmpegPath = c;
                return _ffmpegPath;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', Path.PathSeparator))
        {
            var exe = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(exe))
            {
                _ffmpegPath = exe;
                return _ffmpegPath;
            }
        }

        return null;
    }
}

/// <summary>Result of an independent per-frame MD5 duplicate-frame check on one video file.</summary>
public sealed class DeepVerifyFileResult
{
    public string FilePath { get; init; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long TotalFramesHashed { get; set; }
    public long DuplicateFrameCount { get; set; }
    public double DuplicateFrameRatio => TotalFramesHashed > 0 ? (double)DuplicateFrameCount / TotalFramesHashed : 0;
    public TimeSpan Elapsed { get; set; }

    public string Verdict => !Success
        ? "ERROR"
        : DuplicateFrameCount == 0
            ? "PASS_NO_DUPLICATES"
            : "WARN_DUPLICATES_FOUND";
}
