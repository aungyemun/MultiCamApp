// AuditCheck <ffmpeg.exe> <folder-or-file> [seconds]  — prints one JSON line per video (app's own audit code).
using System.IO;
using System.Text.Json;
using MultiCamApp.Core;
using MultiCamApp.Verification;

var ffmpeg = args[0];
var target = args[1];
var seconds = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 30;
var files = File.Exists(target)
    ? new[] { target }
    : Directory.EnumerateFiles(target, "*.mp4", SearchOption.AllDirectories).Where(f => !f.EndsWith(".tmp.mp4")).OrderBy(f => f).ToArray();
var svc = new VisualStabilityAuditService(new VerificationSettings());
foreach (var f in files)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var r = await svc.AuditAsync(ffmpeg, f, seconds);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        file = Path.GetRelativePath(File.Exists(target) ? Path.GetDirectoryName(target)! : target, f),
        r.Success, r.Error, r.FramesAnalysed, r.SecondsAnalysed, r.BandStrengthPct, r.BandsPerFrame, r.BandDriftHz,
        r.BandDirection, r.FlashFrames, r.LumaRangePct, r.TemporalNoiseYdif, r.TimestampSource, r.Verdict,
        elapsedS = Math.Round(sw.Elapsed.TotalSeconds, 1),
    }));
}
