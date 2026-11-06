// Visual stability audit (new in 2.1.0; additive, on demand via Deep Verify — not part of the frozen
// Scan/Verify path). Measures what the eye sees as "moving horizontal lines", "flashing" and "noise":
//
//  * Rolling bands: room lights flicker at twice the mains frequency (100/120 Hz). A rolling-shutter
//    camera whose shutter is not a multiple of the flicker period records horizontal bright/dark
//    bands that drift up or down. Each frame is reduced by ffmpeg to a 180-row luma profile (16-bit,
//    area-averaged); the static scene (temporal mean profile) and global brightness are removed; the
//    residual is decomposed into k = 1..8 bands per frame height. Strength = median over frames of
//    the band amplitude (% of luma) — the median ignores sporadic motion. The drift rate and
//    direction come from a temporal DFT at the real frame timestamps (camN_timestamps.csv when
//    present, else container packet times).
//  * Flashes: frames whose mean luma deviates > 4 % from the local median (±15 frames).
//  * Temporal noise: median of ffmpeg signalstats YDIF (mean |Y(t) − Y(t−1)|, 8-bit levels).
//
// Validated against the independent Python implementation (data/temp/autotest/visual_audit.py) on
// real recordings: C922 under 120 Hz light flicker gave 1.30 % (anti-flicker 50 Hz) vs 0.095 % (60 Hz).
// Thresholds for the verdict (0.3 % / 1.0 %) are practical visibility guides, not standards.

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
                $"-vf format=gray16le,scale=1:{Rows}:flags=area -f rawvideo -pix_fmt gray16le -", binary: true, ct);
            var n = raw.Length / (2 * Rows);
            if (code != 0 || n < 30) { r.Error = $"decode failed ({n} frames) {err}".Trim(); return r; }

            var t = ReadTimestamps(filePath, n, out var src) ?? await ProbePacketTimesAsync(ffmpegPath, filePath, n, ct);
            if (t is null || t.Length < 30) { r.Error = "no frame timestamps"; return r; }
            r.TimestampSource = src ?? "container packet times";
            n = Math.Min(n, t.Length);

            var prof = new double[n, Rows];
            var g = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var y = 0; y < Rows; y++)
                {
                    var v = BitConverter.ToUInt16(raw, (i * Rows + y) * 2);
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

    /// <summary>Frame times from the app's own camN_timestamps.csv (capturePtsMsFromFirstFrame = the MP4 sample time).</summary>
    private static double[]? ReadTimestamps(string videoPath, int n, out string? source)
    {
        source = null;
        try
        {
            var dir = Path.GetDirectoryName(videoPath) ?? "";
            var csv = Directory.EnumerateFiles(dir, "*timestamps*.csv").FirstOrDefault();
            if (csv is null) return null;
            using var rd = new StreamReader(csv);
            var header = rd.ReadLine()?.Split(',');
            var col = header is null ? -1 : Array.IndexOf(header, "capturePtsMsFromFirstFrame");
            if (col < 0) return null;
            var list = new List<double>(n);
            string? line;
            while (list.Count < n && (line = rd.ReadLine()) is not null)
            {
                var parts = line.Split(',');
                if (parts.Length > col && double.TryParse(parts[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                    list.Add(ms / 1000.0);
            }
            if (list.Count < 30) return null;
            source = "camN_timestamps.csv (capture PTS)";
            return list.ToArray();
        }
        catch { return null; }
    }

    private static async Task<double[]?> ProbePacketTimesAsync(string ffmpegPath, string file, int n, CancellationToken ct)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        if (!File.Exists(ffprobe)) return null;
        var (code, raw, _) = await RunAsync(ffprobe, $"-v error -select_streams v:0 -show_entries packet=pts_time -of csv=p=0 \"{file}\"", false, ct, ffprobe);
        if (code != 0) return null;
        var t = System.Text.Encoding.UTF8.GetString(raw).Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v)).OrderBy(v => v).Take(n).ToArray();
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
