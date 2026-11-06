using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// 2.1.0 release check: the visual stability audit must time each video with that video's own
/// timestamp CSV, and only when the CSV demonstrably matches the video; otherwise it falls back to
/// the container packet times and says why.
/// </summary>
public sealed class VisualStabilityTimestampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mc_vis_ts_" + Guid.NewGuid().ToString("N"));

    public VisualStabilityTimestampTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static string Csv(IEnumerable<double> ms) =>
        "﻿frameIndex,captureTimestampUtc,capturePtsMsFromFirstFrame\r\n" +
        string.Concat(ms.Select((v, i) => $"{i},2026-11-06T00:00:00Z,{v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}\r\n"));

    private static double[] Times(int n, double stepMs, double offsetMs = 0) =>
        Enumerable.Range(0, n).Select(i => offsetMs + i * stepMs).ToArray();

    private void Touch(string path, string content = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ── which CSV belongs to which video ──────────────────────────────────────

    [Fact]
    public void FourCameraSession_EachVideoGetsItsOwnCsv_PerCameraFolders()
    {
        for (var c = 1; c <= 4; c++)
        {
            Touch(Path.Combine(_root, "s", $"cam{c}", $"cam{c}.mp4"));
            Touch(Path.Combine(_root, "s", $"cam{c}", $"cam{c}_timestamps.csv"), Csv(Times(40, 30 + c)));
        }
        for (var c = 1; c <= 4; c++)
            Assert.Equal(Path.Combine(_root, "s", $"cam{c}", $"cam{c}_timestamps.csv"),
                VisualStabilityAuditService.ResolveTimestampCsv(Path.Combine(_root, "s", $"cam{c}", $"cam{c}.mp4")));
    }

    [Fact]
    public void FourCameraSession_SharedFolder_NeverUsesAnotherCamerasCsv()
    {
        // All four videos in one folder: the old code took the first *timestamps*.csv for every camera.
        var dir = Path.Combine(_root, "flat");
        for (var c = 1; c <= 4; c++) Touch(Path.Combine(dir, $"cam{c}.mp4"));
        foreach (var c in new[] { 1, 2, 4 }) Touch(Path.Combine(dir, $"cam{c}_timestamps.csv"), Csv(Times(40, 33)));

        Assert.EndsWith("cam2_timestamps.csv", VisualStabilityAuditService.ResolveTimestampCsv(Path.Combine(dir, "cam2.mp4")));
        Assert.EndsWith("cam4_timestamps.csv", VisualStabilityAuditService.ResolveTimestampCsv(Path.Combine(dir, "cam4.mp4")));
        Assert.Null(VisualStabilityAuditService.ResolveTimestampCsv(Path.Combine(dir, "cam3.mp4")));   // cam3 has none
    }

    [Fact]
    public void MissingCsv_FallsBackToPacketTimes()
    {
        Touch(Path.Combine(_root, "m", "cam1", "cam1.mp4"));
        Touch(Path.Combine(_root, "m", "cam1", "other_timestamps.csv"), Csv(Times(40, 33)));   // not this video's
        Assert.Null(VisualStabilityAuditService.ResolveTimestampCsv(Path.Combine(_root, "m", "cam1", "cam1.mp4")));

        var packets = Times(100, 33.3).Select(v => v / 1000).ToArray();
        var t = VisualStabilityAuditService.SelectFrameTimes(null, "no timestamp CSV for this video", packets, 100, out var src);
        Assert.Equal(packets, t);
        Assert.StartsWith(VisualStabilityAuditService.PacketSource, src);
        Assert.Contains("no timestamp CSV", src);
    }

    // ── validation of a CSV against the video ────────────────────────────────

    [Fact]
    public void MatchingCsv_IsUsed_AlignedByFrameIndex()
    {
        var csv = Times(300, 33.4, offsetMs: 0).Select(v => v / 1000).ToArray();
        var packets = Times(300, 33.0).Select(v => v / 1000).ToArray();   // MP4 timeline ~1 % shorter (seen on real files)
        var t = VisualStabilityAuditService.SelectFrameTimes(csv, null, packets, 120, out var src);
        Assert.Equal(VisualStabilityAuditService.CsvSource, src);
        Assert.Equal(120, t!.Length);
        Assert.Equal(csv.Take(120), t);
    }

    [Fact]
    public void CsvWithDifferentFrameCount_IsRejected()
    {
        // Interrupted recording seen on real files: 500 CSV rows, 490 video frames.
        var csv = Times(500, 33.3).Select(v => v / 1000).ToArray();
        var packets = Times(490, 33.3).Select(v => v / 1000).ToArray();
        var t = VisualStabilityAuditService.SelectFrameTimes(csv, null, packets, 490, out var src);
        Assert.Equal(packets, t);
        Assert.Contains("500 rows", src);
        Assert.Contains("490 frames", src);
    }

    [Fact]
    public void CsvOfAnotherRecording_SameCountDifferentDuration_IsRejected()
    {
        var csv = Times(300, 50).Select(v => v / 1000).ToArray();        // 15 s
        var packets = Times(300, 33.3).Select(v => v / 1000).ToArray();  // 10 s
        var t = VisualStabilityAuditService.SelectFrameTimes(csv, null, packets, 300, out var src);
        Assert.Equal(packets, t);
        Assert.Contains("spans", src);
    }

    [Fact]
    public void CsvWithoutPacketTimes_IsNotTrusted()
    {
        var csv = Times(300, 33.3).Select(v => v / 1000).ToArray();
        Assert.Null(VisualStabilityAuditService.SelectFrameTimes(csv, null, null, 300, out _));
    }

    [Theory]
    [InlineData("frameIndex,capturePtsMsFromFirstFrame\r\n0,0\r\n1,33\r\n3,66\r\n", "frameIndex")]          // gap in index
    [InlineData("frameIndex,capturePtsMsFromFirstFrame\r\n0,0\r\n1,33\r\n2,33\r\n", "do not increase")]    // repeated time
    [InlineData("frameIndex,somethingElse\r\n0,0\r\n", "no capturePtsMsFromFirstFrame")]
    [InlineData("frameIndex,capturePtsMsFromFirstFrame\r\n0,abc\r\n", "not readable")]
    [InlineData("frameIndex,capturePtsMsFromFirstFrame\r\n", "empty")]
    public void MalformedCsv_IsRejectedWithReason(string content, string reason)
    {
        Assert.Null(VisualStabilityAuditService.ParseTimestampCsv(new StringReader(content), out var problem));
        Assert.Contains(reason, problem);
    }

    [Fact]
    public void RealCsvLayout_WithBom_IsParsed()
    {
        var parsed = VisualStabilityAuditService.ParseTimestampCsv(new StringReader(Csv(new[] { 0.0, 32.013, 79.908 })), out var problem);
        Assert.Null(problem);
        Assert.Equal(new[] { 0.0, 0.032013, 0.079908 }, parsed!, new ToleranceComparer(1e-9));
    }

    // ── luma decoding ────────────────────────────────────────────────────────

    [Fact]
    public void ProfileFilter_ReadsTheLumaPlaneOnce()
    {
        // The old chain "format=gray16le,scale=…" converted the range twice and inflated every % figure
        // (×1.33 at mid-grey on lossless synthetic video). Pin the verified chain.
        var f = VisualStabilityAuditService.ProfileFilter;
        Assert.StartsWith("extractplanes=y,", f);
        Assert.EndsWith(",format=gray16le", f);
        Assert.Contains("scale=1:180:flags=area", f);
    }

    [Theory]
    [InlineData("yuvj420p", "pc", true)]       // this app's own recordings
    [InlineData("yuv420p", "pc", true)]
    [InlineData("yuv420p", "full", true)]
    [InlineData("yuvj420p", null, true)]
    [InlineData("yuv420p", "tv", false)]
    [InlineData("yuv420p", "unknown", false)]
    [InlineData(null, null, false)]
    public void BlackLevel_AddedBackOnlyForFullRange(string? pixFmt, string? range, bool full)
        => Assert.Equal(full ? 16 * 65535.0 / 219 : 0, VisualStabilityAuditService.BlackLevelOffset((pixFmt, range)));

    // ── variable frame rate ──────────────────────────────────────────────────

    [Fact]
    public void VariableFrameRate_DriftMeasuredOnRealTimes()
    {
        // Camera delivers ~22.5 fps with every third frame missing (like the JVU250 gaps on real files);
        // the bands drift at 1.0 Hz in real time.
        const int n = 450; const double driftHz = 1.0;
        var t = new double[n]; var acc = 0.0;
        for (var i = 0; i < n; i++) { t[i] = acc; acc += i % 2 == 1 ? 2 / 30.0 : 1 / 30.0; }
        var (p, g) = Bands(t, 0.01, 3, driftHz);

        var real = new VisualStabilityResult();
        VisualStabilityAuditService.Analyse(p, g, t, n, real);
        Assert.Equal(3, real.BandsPerFrame);
        Assert.InRange(real.BandDriftHz, 0.95, 1.05);

        // Assuming a constant 30 fps (what the analysis would do without real timestamps) gets it wrong.
        var uniform = Enumerable.Range(0, n).Select(i => i / 30.0).ToArray();
        var wrong = new VisualStabilityResult();
        VisualStabilityAuditService.Analyse(p, g, uniform, n, wrong);
        Assert.False(wrong.BandDriftHz is >= 0.95 and <= 1.05);
    }

    private static (double[,] prof, double[] g) Bands(double[] t, double amp, int k, double driftHz)
    {
        const int rows = 180;
        var prof = new double[t.Length, rows]; var g = new double[t.Length];
        for (var i = 0; i < t.Length; i++)
        {
            double sum = 0;
            for (var y = 0; y < rows; y++)
            {
                var v = 30000 * (1 + amp * Math.Cos(2 * Math.PI * (k * y / (double)rows + driftHz * t[i])));
                prof[i, y] = v; sum += v;
            }
            g[i] = sum / rows;
        }
        return (prof, g);
    }

    private sealed class ToleranceComparer(double tol) : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= tol;
        public int GetHashCode(double v) => 0;
    }
}
