// Visual stability audit (new in 2.1.0; additive, on demand via Deep Verify — not part of the frozen
// Scan/Verify path). Measures what the eye sees as "moving horizontal lines", "flashing" and "noise":
//
//  * Rolling bands: room lights flicker at twice the mains frequency (100/120 Hz). A rolling-shutter
//    camera whose shutter is not a multiple of the flicker period records horizontal bright/dark
//    bands that drift up or down. Each frame is reduced by ffmpeg to a 180-row luma profile (16-bit,
//    area-averaged); the static scene (temporal mean profile) and global brightness are removed; the
//    residual is decomposed into k = 1..8 bands per frame height. Strength = median over frames of
//    the band amplitude (% of luma) — the median ignores sporadic motion.
//    "% of luma" = % of the luma signal above black level: Y′ − 16 for limited-range video, Y′ for
//    full-range video (this app's own recordings are full range, yuvj420p).
//    2.1.0 release check (exception #22), measured on lossless synthetic video with the bundled
//    ffmpeg 8.1.1: its gray16 conversion always returns 65535/219 · (Y′ − 16), even for full-range
//    input, and the earlier chain "format=gray16le,scale=…" expanded limited-range video twice
//    (≈ 1.36·(Y′ − 30)). Every % figure was therefore inflated: ×Y′/(Y′ − 16) for full-range
//    recordings (≈ ×1.15 at mid-grey) and ×Y′/(Y′ − 30) for limited-range files (≈ ×1.33).
//    Frequency, direction, flash counts and the noise metric were not affected. Remaining limit:
//    full-range pixels above Y′ 235 (below 16) are clipped by the conversion, so bands inside
//    highlights are under-read. The drift rate and
//    direction come from a temporal DFT at the real frame timestamps: the video's own
//    camN_timestamps.csv (capture clock) when it demonstrably belongs to this video, else the
//    container packet times. Note: the MP4 timeline is NOT identical to the capture clock — on real
//    recordings it ran up to 0.75 % short (−157 ms over 21 s) because some frame gaps are stored
//    with shorter durations — which is why the CSV is preferred when it is valid.
//  * Flashes: frames whose mean luma deviates > 4 % from the local median (±15 frames).
//  * Temporal noise: median of ffmpeg signalstats YDIF (mean |Y(t) − Y(t−1)|, 8-bit levels).
//
// Validation (2.1.0 release check): lossless synthetic videos with known bands, drift, flashes and
// noise (scripts/autotest/synth_visual.py, limited and full range, constant and variable frame rate)
// and the separate Python implementation (scripts/autotest/visual_audit.py) on real recordings.
// See docs/user_guide/video_verification.md → "Visual stability: accuracy and limits".
// Thresholds for the verdict (0.3 % / 1.0 %) are practical visibility guides, not standards; they were
// chosen before the scale correction of exception #22.

using System.Diagnostics;
using System.Globalization;
using MultiCamApp.Core;

namespace MultiCamApp.Verification;

public sealed class VisualStabilityResult
{
    public string FilePath { get; init; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int FramesAnalysed { get; set; }
    public double SecondsAnalysed { get; set; }
    public double BandStrengthPct { get; set; }
    public int BandsPerFrame { get; set; }
    public double BandDriftHz { get; set; }
    public string BandDirection { get; set; } = "";
    public int FlashFrames { get; set; }
    public double LumaRangePct { get; set; }
    public double? TemporalNoiseYdif { get; set; }
    public string TimestampSource { get; set; } = "";

    public const double BandWarnPct = 0.3, BandVisiblePct = 1.0;
    /// <summary>PASS / WARNING_BANDS_FAINT / WARNING_BANDS_VISIBLE / WARNING_FLASH / ERROR</summary>
    public string Verdict => !Success ? "ERROR"
        : FlashFrames > 0 ? "WARNING_FLASH"
        : BandStrengthPct >= BandVisiblePct ? "WARNING_BANDS_VISIBLE"
        : BandStrengthPct >= BandWarnPct ? "WARNING_BANDS_FAINT"
        : "PASS";
}

public sealed class VisualStabilityAuditService
{
    private const int Rows = 180, KMax = 8;

    /// <summary>
    /// Luma plane → 180-row profile, 16-bit. Verified on lossless synthetic video (limited and full
    /// range): output = 299.25 · (Y′ − 16) for every row; see <see cref="BlackLevelOffset"/>.
    /// </summary>
    public const string ProfileFilter = "extractplanes=y,scale=1:180:flags=area:in_range=full:out_range=full,format=gray16le";
    private readonly VerificationSettings _settings;
    public VisualStabilityAuditService(VerificationSettings settings) => _settings = settings;
    public static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(180);

    public async Task<VisualStabilityResult> AuditAsync(string ffmpegPath, string filePath, double seconds = 30, CancellationToken ct = default)
    {
        var r = new VisualStabilityResult { FilePath = filePath };
        try
        {
            var inv = CultureInfo.InvariantCulture;
            var (code, raw, err) = await RunAsync(ffmpegPath,
                $"-nostdin -v error -i \"{filePath}\" -t {seconds.ToString(inv)} -fps_mode passthrough " +
                $"-vf {ProfileFilter} -f rawvideo -pix_fmt gray16le -", binary: true, ct);
            var n = raw.Length / (2 * Rows);
            if (code != 0 || n < 30) { r.Error = $"decode failed ({n} frames) {err}".Trim(); return r; }
            var black = BlackLevelOffset(await ProbeStreamAsync(ffmpegPath, filePath, ct));

            // 2.1.0 (release check): the CSV is chosen by exact file name and validated against this
            // video's own packet count and duration before use. Previously the first *timestamps*.csv
            // in the folder was used without any check, so a CSV of another camera, or of an
            // interrupted recording with a different frame count, was silently applied.
            var packets = await ProbePacketTimesAsync(ffmpegPath, filePath, ct);
            var csvPath = ResolveTimestampCsv(filePath);
            double[]? csv = null; string? csvProblem = csvPath is null ? "no timestamp CSV for this video" : null;
            if (csvPath is not null)
                using (var rd = new StreamReader(csvPath))
                    csv = ParseTimestampCsv(rd, out csvProblem);
            var t = SelectFrameTimes(csv, csvProblem, packets, n, out var src);
            if (t is null || t.Length < 30) { r.Error = "no frame timestamps"; return r; }
            r.TimestampSource = src;
            n = Math.Min(n, t.Length);

            var prof = new double[n, Rows];
            var g = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var y = 0; y < Rows; y++)
                {
                    var v = BitConverter.ToUInt16(raw, (i * Rows + y) * 2) + black;
                    prof[i, y] = v; sum += v;
                }
                g[i] = Math.Max(1, sum / Rows);
            }
            Analyse(prof, g, t, n, r);
            r.TemporalNoiseYdif = await NoiseAsync(ffmpegPath, filePath, Math.Min(seconds, 30), ct);
            r.Success = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { r.Error = ex.Message; }
        return r;
    }

    /// <summary>Pure analysis (unit-testable): row profiles [frame,row], per-frame mean luma, timestamps (s).</summary>
    public static void Analyse(double[,] prof, double[] g, double[] t, int n, VisualStabilityResult r)
    {
        var mean = new double[Rows];
        for (var y = 0; y < Rows; y++) { double s = 0; for (var i = 0; i < n; i++) s += prof[i, y]; mean[y] = s / n; }
        var gm = mean.Average();

        // Spatial DFT of the normalised residual: c[k][i]
        var cre = new double[KMax, n]; var cim = new double[KMax, n];
        for (var i = 0; i < n; i++)
            for (var y = 0; y < Rows; y++)
            {
                var res = prof[i, y] / g[i] - mean[y] / gm;
                for (var k = 1; k <= KMax; k++)
                {
                    var a = -2 * Math.PI * k * y / Rows;
                    cre[k - 1, i] += res * Math.Cos(a); cim[k - 1, i] += res * Math.Sin(a);
                }
            }

        var perFrame = new double[n];
        for (var i = 0; i < n; i++)
        {
            double e = 0;
            for (var k = 0; k < KMax; k++) e += cre[k, i] * cre[k, i] + cim[k, i] * cim[k, i];
            perFrame[i] = Math.Sqrt(e) * 2 / Rows;
        }
        r.BandStrengthPct = Math.Round(100 * Median(perFrame), 3);

        var span = Math.Max(1e-3, t[n - 1] - t[0]);
        var fps = (n - 1) / span;
        var df = Math.Max(0.1, 1 / span); var fmin = Math.Max(0.1, 2 / span);
        double best = -1; int bestK = 0; double bestF = 0;
        for (var f = -fps / 2; f <= fps / 2; f += df)
        {
            if (Math.Abs(f) < fmin) continue;
            for (var k = 0; k < KMax; k++)
            {
                double sr = 0, si = 0;
                for (var i = 0; i < n; i++)
                {
                    var a = -2 * Math.PI * f * (t[i] - t[0]);
                    double c = Math.Cos(a), s = Math.Sin(a);
                    sr += cre[k, i] * c - cim[k, i] * s; si += cre[k, i] * s + cim[k, i] * c;
                }
                var m = sr * sr + si * si;
                if (m > best) { best = m; bestK = k + 1; bestF = f; }
            }
        }
        r.BandsPerFrame = bestK;
        r.BandDriftHz = Math.Round(Math.Abs(bestF), 2);
        r.BandDirection = bestF > 0 ? "up" : "down";

        var med = Median(g);
        var h = g.Select(x => x / med).ToArray();
        var flashes = 0;
        for (var i = 0; i < n; i++)
        {
            var lo = Math.Max(0, i - 15); var hi = Math.Min(n, i + 16);
            if (Math.Abs(h[i] - Median(h[lo..hi])) > 0.04) flashes++;
        }
        r.FlashFrames = flashes;
        r.LumaRangePct = Math.Round(100 * (h.Max() - h.Min()), 2);
        r.FramesAnalysed = n;
        r.SecondsAnalysed = Math.Round(span, 2);
    }

    private static double Median(double[] v)
    {
        if (v.Length == 0) return 0;
        var s = (double[])v.Clone(); Array.Sort(s);
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }

    /// <summary>
    /// Value to add to the decoded profile so it is proportional to the luma above black level. The
    /// conversion in <see cref="ProfileFilter"/> subtracts the limited-range black level (16) from
    /// every input; for full-range video (black = 0) that offset is added back.
    /// </summary>
    public static double BlackLevelOffset((string? PixFmt, string? ColorRange) stream) =>
        string.Equals(stream.ColorRange, "pc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(stream.ColorRange, "full", StringComparison.OrdinalIgnoreCase)
        || (stream.PixFmt?.StartsWith("yuvj", StringComparison.OrdinalIgnoreCase) ?? false)
            ? 16 * 65535.0 / 219 : 0;

    private static async Task<(string? PixFmt, string? ColorRange)> ProbeStreamAsync(string ffmpegPath, string file, CancellationToken ct)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        if (!File.Exists(ffprobe)) return (null, null);
        var (code, raw, _) = await RunAsync(ffprobe, $"-v error -select_streams v:0 -show_entries stream=pix_fmt,color_range -of csv=p=0 \"{file}\"", false, ct, ffprobe);
        if (code != 0) return (null, null);
        var parts = System.Text.Encoding.UTF8.GetString(raw).Trim().Split(',');
        return (parts.Length > 0 ? parts[0].Trim() : null, parts.Length > 1 ? parts[1].Trim() : null);
    }

    public const string CsvSource = "camN_timestamps.csv (capture clock)";
    public const string PacketSource = "container packet times";

    /// <summary>
    /// The timestamp CSV that belongs to <paramref name="videoPath"/>: exactly "&lt;video name&gt;_timestamps.csv"
    /// in the same folder (camN.mp4 → camN_timestamps.csv). No other CSV is ever used, so a camera can
    /// never pick up another camera's timing even when several cameras share one folder.
    /// </summary>
    public static string? ResolveTimestampCsv(string videoPath)
    {
        var dir = Path.GetDirectoryName(videoPath) ?? "";
        var csv = Path.Combine(dir, Path.GetFileNameWithoutExtension(videoPath) + "_timestamps.csv");
        return File.Exists(csv) ? csv : null;
    }

    /// <summary>
    /// capturePtsMsFromFirstFrame (seconds) in row order. Null with a reason when the column is missing,
    /// a value does not parse, frameIndex is not 0,1,2,… or the times do not increase.
    /// </summary>
    public static double[]? ParseTimestampCsv(TextReader reader, out string? problem)
    {
        problem = null;
        var header = reader.ReadLine()?.TrimStart('\uFEFF').Split(',');
        var col = header is null ? -1 : Array.IndexOf(header, "capturePtsMsFromFirstFrame");
        var idx = header is null ? -1 : Array.IndexOf(header, "frameIndex");
        if (col < 0) { problem = "timestamp CSV has no capturePtsMsFromFirstFrame column"; return null; }
        var list = new List<double>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var parts = line.Split(',');
            if (parts.Length <= col || !double.TryParse(parts[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            { problem = $"timestamp CSV row {list.Count + 1} is not readable"; return null; }
            if (idx >= 0 && (parts.Length <= idx || parts[idx].Trim() != list.Count.ToString(CultureInfo.InvariantCulture)))
            { problem = $"timestamp CSV frameIndex is not continuous at row {list.Count + 1}"; return null; }
            var sec = ms / 1000.0;
            if (list.Count > 0 && sec <= list[^1])
            { problem = $"timestamp CSV times do not increase at row {list.Count + 1}"; return null; }
            list.Add(sec);
        }
        if (list.Count == 0) { problem = "timestamp CSV is empty"; return null; }
        return list.ToArray();
    }

    /// <summary>
    /// Frame times for the first <paramref name="decodedFrames"/> decoded frames (frame i ↔ CSV row i).
    /// The CSV is used only when its row count equals the video's packet count and its span agrees with
    /// the container duration within 5 % (the capture clock and the MP4 timeline differ by up to
    /// ~1 %, see the file header); otherwise the container packet times are used and the reason is
    /// reported in <paramref name="source"/>. Without packet times a CSV cannot be cross-checked and
    /// is not used.
    /// </summary>
    public static double[]? SelectFrameTimes(double[]? csv, string? csvProblem, double[]? packets, int decodedFrames, out string source)
    {
        string? reason = csvProblem;
        if (csv is not null && packets is not null)
        {
            if (csv.Length != packets.Length)
                reason = $"timestamp CSV has {csv.Length} rows, video has {packets.Length} frames";
            else if (decodedFrames > packets.Length)
                reason = $"decoded {decodedFrames} frames, video has {packets.Length} packets";
            else
            {
                var cs = csv[^1] - csv[0]; var ps = packets[^1] - packets[0];
                if (ps <= 0 || Math.Abs(cs - ps) / ps > 0.05)
                    reason = $"timestamp CSV spans {cs:F2} s, video {ps:F2} s";
                else
                {
                    source = CsvSource;
                    return csv.Take(decodedFrames).Select(v => v - csv[0]).ToArray();
                }
            }
        }
        else if (csv is not null)
            reason = "timestamp CSV could not be cross-checked (no packet times)";

        if (packets is null) { source = ""; return null; }
        source = reason is null ? PacketSource : $"{PacketSource} ({reason})";
        return packets.Take(decodedFrames).ToArray();
    }

    /// <summary>All video packet presentation times of the file, sorted, relative to the first.</summary>
    private static async Task<double[]?> ProbePacketTimesAsync(string ffmpegPath, string file, CancellationToken ct)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        if (!File.Exists(ffprobe)) return null;
        var (code, raw, _) = await RunAsync(ffprobe, $"-v error -select_streams v:0 -show_entries packet=pts_time -of csv=p=0 \"{file}\"", false, ct, ffprobe);
        if (code != 0) return null;
        var t = System.Text.Encoding.UTF8.GetString(raw).Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        return t.Length > 0 ? t.Select(v => v - t[0]).ToArray() : null;
    }

    private static async Task<double?> NoiseAsync(string ffmpegPath, string file, double seconds, CancellationToken ct)
    {
        var (code, raw, _) = await RunAsync(ffmpegPath,
            $"-nostdin -v error -t {seconds.ToString(CultureInfo.InvariantCulture)} -i \"{file}\" -vf signalstats,metadata=print:file=- -an -f null -", false, ct);
        if (code != 0) return null;
        var vals = System.Text.Encoding.UTF8.GetString(raw).Split('\n')
            .Where(l => l.Contains("lavfi.signalstats.YDIF="))
            .Select(l => double.TryParse(l[(l.IndexOf('=') + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v)).Skip(1).ToArray();
        return vals.Length > 2 ? Math.Round(Median(vals), 3) : null;
    }

    private static async Task<(int Code, byte[] Out, string Err)> RunAsync(string exe, string args, bool binary, CancellationToken ct, string? _ = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + Path.GetFileName(exe));
        p.StandardInput.Close();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ToolTimeout);
        try
        {
            using var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, cts.Token);
            var err  = p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            await copy;
            var e = await err;
            return (p.ExitCode, ms.ToArray(), e.Length > 300 ? e[..300] : e.Trim());
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            return (-9, Array.Empty<byte>(), $"timed out after {ToolTimeout.TotalSeconds:F0} s");
        }
    }
}
