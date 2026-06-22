using MultiCamApp.Core;
using MultiCamApp.Utils;
using MultiCamApp.Verification;

var folder = args.Length > 0 ? args[0] : "";
if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
{
    Console.WriteLine("Usage: VerifyFolderCli <folder>");
    return 1;
}

var config = JsonLoader.LoadFromFile<AppConfig>(JsonLoader.ConfigPath("appsettings.json")) ?? new AppConfig();
var svc = new VideoVerificationService(config.Verification);
if (!svc.IsFfprobeAvailable)
{
    Console.WriteLine("FAIL: ffprobe not available. Set MULTICAMAPP_ROOT to dist or install ffmpeg.");
    return 2;
}

var entries = svc.Scan(folder);
Console.WriteLine($"Scan: {entries.Count} video(s) in {entries.Select(e => e.SessionFolder).Distinct(StringComparer.OrdinalIgnoreCase).Count()} session(s)");
var report = await svc.VerifyAsync(folder, entries, config);

Console.WriteLine($"Overall: {report.Summary.OverallVerdict}");
Console.WriteLine($"Per-video: Pass={report.Summary.VideosPassed} Warn={report.Summary.VideosWarning} Fail={report.Summary.VideosFailed}");
Console.WriteLine($"Settings source: {report.Summary.ExpectedSettingsSource}");
Console.WriteLine();

foreach (var audit in report.SessionAudits)
{
    Console.WriteLine($"SESSION: {audit.SessionLabel}");
    Console.WriteLine($"  Status: {audit.SessionStatus}");
    Console.WriteLine($"  Cameras: {string.Join(", ", audit.CamerasFound)}");
    if (audit.InterCameraFrameDifference.HasValue)
        Console.WriteLine($"  Frame diff: {audit.InterCameraFrameDifference}");
    if (audit.StartOffsetMs.HasValue)
        Console.WriteLine($"  Start offset ms: {audit.StartOffsetMs:F1}");
    if (audit.InterCameraWallDurationDifferenceSeconds.HasValue)
        Console.WriteLine($"  Wall duration diff s: {audit.InterCameraWallDurationDifferenceSeconds:F3}");
    foreach (var w in audit.Warnings)
        Console.WriteLine($"  WARN: {w}");
    foreach (var f in audit.Failures)
        Console.WriteLine($"  FAIL: {f}");
    Console.WriteLine();
}

foreach (var row in report.TableRows)
{
    Console.WriteLine($"{row.Camera} | {row.ResultDisplay} | {row.ActualResolution} | fps {row.ActualFps} | frames {row.FrameCount} | meas {row.MeasuredCameraFpsDisplay}");
}

var outTxt = Path.Combine(folder, "verification_page_report.txt");
var outJson = Path.Combine(folder, "verification_page_report.json");
var writer = new VerificationReportWriter();
await writer.ExportTextAsync(report, outTxt);
await writer.ExportJsonAsync(report, outJson);
Console.WriteLine($"Exported: {outTxt}");
Console.WriteLine($"Exported: {outJson}");
return report.Summary.VideosFailed > 0 || report.Summary.OverallVerdict == VerificationVerdict.Fail ? 3 : 0;
