// Run: dotnet script run_verification_cli.cs -- "C:\path\to\session"
#r "..\..\source\MultiCamApp\MultiCamApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\MultiCamApp.dll"

using MultiCamApp.Core;
using MultiCamApp.Utils;
using MultiCamApp.Verification;

var folder = args.Length > 0 ? args[0] : "";
if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
{
    Console.WriteLine("Usage: dotnet script run_verification_cli.cs -- <session_folder>");
    return;
}

var config = JsonLoader.LoadFromFile<AppConfig>(JsonLoader.ConfigPath("appsettings.json")) ?? new AppConfig();
var svc = new VideoVerificationService(config.Verification);
if (!svc.IsFfprobeAvailable)
{
    Console.WriteLine("FAIL: ffprobe not available");
    return;
}

var entries = svc.Scan(folder);
Console.WriteLine($"Scan: {entries.Count} video(s)");
var report = await svc.VerifyAsync(folder, entries, config);
Console.WriteLine($"Overall: {report.Summary.OverallVerdict}");
Console.WriteLine($"Pass={report.Summary.VideosPassed} Warn={report.Summary.VideosWarning} Fail={report.Summary.VideosFailed}");
Console.WriteLine($"Duration spread: {report.Summary.SessionDurationMatch}");
foreach (var v in report.Videos)
{
    Console.WriteLine($"  {v.Entry.CameraSlot} {v.Entry.FileName} [{v.Verdict}] res {v.ActualResolutionDisplay} fps {v.ActualFpsDisplay} dur {v.DurationDisplay}");
    foreach (var m in v.Messages)
        Console.WriteLine($"    - {m}");
}

var outPath = Path.Combine(folder, "verification_report.txt");
await new VerificationReportWriter().ExportTextAsync(report, outPath);
Console.WriteLine($"Exported: {outPath}");
