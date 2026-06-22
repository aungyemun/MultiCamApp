using System.Globalization;
using System.Text;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

public sealed class PerformanceLogWriter
{
    public const int RetentionFileCount = 30;

    private readonly string _logDir;
    private readonly DateTime _startedLocal;
    private readonly string _csvPath;
    private bool _headerWritten;

    public PerformanceLogWriter(string? logDir = null, DateTime? startedLocal = null)
    {
        _logDir = string.IsNullOrWhiteSpace(logDir) ? PathHelper.LogsFolder() : logDir;
        _startedLocal = startedLocal ?? DateTime.Now;
        Directory.CreateDirectory(_logDir);
        _csvPath = Path.Combine(_logDir, $"performance_monitor_{_startedLocal:yyyyMMdd_HHmmss}.csv");
    }

    public string CsvPath => _csvPath;

    public void Append(PerformanceSnapshot snapshot)
    {
        Directory.CreateDirectory(_logDir);
        try
        {
            using var writer = new StreamWriter(_csvPath, append: true, Encoding.UTF8);
            if (!_headerWritten && new FileInfo(_csvPath).Length == 0)
            {
                writer.WriteLine(BuildHeader());
                _headerWritten = true;
            }
            writer.WriteLine(BuildRow(snapshot));
        }
        catch { /* logs folder read-only or locked — performance sample silently skipped */ }
    }

    public (string SummaryPath, string LatestPath) WriteSummary(IReadOnlyList<PerformanceSnapshot> snapshots)
    {
        Directory.CreateDirectory(_logDir);
        var summaryPath = Path.Combine(_logDir, $"performance_summary_{_startedLocal:yyyyMMdd_HHmmss}.txt");
        var latestPath = Path.Combine(_logDir, "PerformanceSummary.latest.txt");
        var text = BuildSummary(snapshots);
        File.WriteAllText(summaryPath, PrivacySanitizer.SanitizeForOutput(text), Encoding.UTF8);
        File.WriteAllText(latestPath, PrivacySanitizer.SanitizeForOutput(text), Encoding.UTF8);
        CleanupRetention(_logDir);
        return (summaryPath, latestPath);
    }

    public static void CleanupRetention(string logDir)
    {
        if (!Directory.Exists(logDir)) return;
        DeleteOlderThanRetention(logDir, "performance_monitor_*.csv", RetentionFileCount);
        DeleteOlderThanRetention(logDir, "performance_summary_*.txt", RetentionFileCount);
    }

    public static string BuildSummary(IReadOnlyList<PerformanceSnapshot> snapshots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MultiCamApp Performance Diagnostics Summary");
        sb.AppendLine("Diagnostic-only. Not scientific output. Use Video Verification and metadata to judge recording validity.");
        sb.AppendLine($"GeneratedLocal={DateTime.Now:O}");
        sb.AppendLine($"SampleCount={snapshots.Count}");

        if (snapshots.Count == 0)
        {
            sb.AppendLine("CPU Load=Unknown");
            sb.AppendLine("Memory Load=Unknown");
            sb.AppendLine("Preview Load=Unknown");
            sb.AppendLine("Recording Load=Unknown");
            return sb.ToString();
        }

        var cpuMax = snapshots.Select(s => s.ProcessCpuPercent).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Max();
        var memoryMax = snapshots.Max(s => s.WorkingSetMb);
        var maxActive = snapshots.Max(s => s.ActiveCameraCount);
        var totalDropDelta = snapshots.SelectMany(s => s.Cameras).Sum(c => c.WriterQueueDropsDelta);
        var recordingSamples = snapshots.Where(s => string.Equals(s.RunState, "Recording", StringComparison.OrdinalIgnoreCase)).ToList();
        var previewSamples = snapshots.Where(s => s.ActiveCameraCount > 0).ToList();
        var lowPreviewFps = previewSamples
            .SelectMany(s => s.Cameras)
            .Where(c => c.PreviewFps.HasValue)
            .Any(c => c.PreviewFps!.Value > 0 && c.PreviewFps.Value < 8);
        var delayedCameraFrames = previewSamples
            .SelectMany(s => s.Cameras)
            .Any(c => c.FramesCapturedFps < 5 && c.FramesWrittenFps <= 0);

        var cpuLoad = ClassifyCpu(cpuMax);
        var memoryLoad = memoryMax >= 4096 ? "Warning" : "OK";
        var previewLoad = lowPreviewFps ? "Slow" : previewSamples.Count == 0 ? "Unknown" : "OK";
        var recordingLoad = totalDropDelta > 0 ? "Warning" : "OK";

        sb.AppendLine($"DurationSeconds={snapshots.Max(s => s.ElapsedSeconds):F1}");
        sb.AppendLine($"MaxActiveCameraCount={maxActive}");
        sb.AppendLine($"CPU Load={cpuLoad}");
        sb.AppendLine($"Memory Load={memoryLoad}");
        sb.AppendLine($"Preview Load={previewLoad}");
        sb.AppendLine($"Recording Load={recordingLoad}");
        sb.AppendLine($"MaxProcessCpuPercent={(double.IsNaN(cpuMax) ? "Unknown" : cpuMax.ToString("F1", CultureInfo.InvariantCulture))}");
        sb.AppendLine($"MaxWorkingSetMb={memoryMax:F1}");
        sb.AppendLine($"WriterQueueDropDeltaTotal={totalDropDelta}");
        sb.AppendLine();
        sb.AppendLine("Advisory messages:");

        if (previewLoad == "Slow" && recordingLoad == "OK")
            sb.AppendLine("- Preview appears slow, but recording counters are normal.");
        if (totalDropDelta > 0)
            sb.AppendLine("- Recording writer queue drops detected. Check disk speed, CPU load, or resolution.");
        if (cpuLoad == "High" && maxActive >= 3 && recordingSamples.Count > 0)
            sb.AppendLine("- CPU load is high during 3/4-camera recording. Try 720p or 360p.");
        if (cpuLoad is "Low" or "Medium" && delayedCameraFrames)
            sb.AppendLine("- CPU load is low but camera frames are delayed. USB bandwidth or driver behavior may be the bottleneck.");
        if (recordingLoad == "OK")
            sb.AppendLine("- Recording counters show no writer queue drops in this diagnostic window.");
        sb.AppendLine("- GPU usage and hardware encoder switching remain deferred until CPU, preview, recording, disk, USB, or driver bottlenecks are confirmed.");
        return sb.ToString();
    }

    private static string ClassifyCpu(double maxCpu)
    {
        if (double.IsNaN(maxCpu)) return "Unknown";
        if (maxCpu >= 75) return "High";
        if (maxCpu >= 40) return "Medium";
        return "Low";
    }

    private static string BuildHeader()
    {
        var columns = new List<string>
        {
            "timestampLocal",
            "elapsedSeconds",
            "runState",
            "processCpuPercent",
            "totalCpuPercent",
            "processMemoryMb",
            "workingSetMb",
            "gcMemoryMb",
            "activeCameraCount"
        };

        for (var slot = 1; slot <= 4; slot++)
        {
            columns.AddRange([
                $"cam{slot}_status",
                $"cam{slot}_previewFps",
                $"cam{slot}_framesCapturedTotal",
                $"cam{slot}_framesCapturedDelta",
                $"cam{slot}_framesCapturedFps",
                $"cam{slot}_framesWrittenTotal",
                $"cam{slot}_framesWrittenDelta",
                $"cam{slot}_framesWrittenFps",
                $"cam{slot}_writerQueueDropsTotal",
                $"cam{slot}_writerQueueDropsDelta",
                $"cam{slot}_previewStalenessSeconds"
            ]);
        }

        return string.Join(",", columns);
    }

    private static string BuildRow(PerformanceSnapshot snapshot)
    {
        var values = new List<string>
        {
            Csv(snapshot.TimestampLocal.ToString("O", CultureInfo.InvariantCulture)),
            Num(snapshot.ElapsedSeconds),
            Csv(PrivacySanitizer.SanitizeForOutput(snapshot.RunState)),
            Num(snapshot.ProcessCpuPercent),
            Num(snapshot.TotalCpuPercent),
            Num(snapshot.ProcessMemoryMb),
            Num(snapshot.WorkingSetMb),
            Num(snapshot.GcMemoryMb),
            snapshot.ActiveCameraCount.ToString(CultureInfo.InvariantCulture)
        };

        for (var slot = 1; slot <= 4; slot++)
        {
            var camera = snapshot.Cameras.FirstOrDefault(c => c.Slot == slot);
            values.AddRange([
                Csv(camera?.Status ?? ""),
                Num(camera?.PreviewFps),
                Num(camera?.FramesCapturedTotal),
                Num(camera?.FramesCapturedDelta),
                Num(camera?.FramesCapturedFps),
                Num(camera?.FramesWrittenTotal),
                Num(camera?.FramesWrittenDelta),
                Num(camera?.FramesWrittenFps),
                Num(camera?.WriterQueueDropsTotal),
                Num(camera?.WriterQueueDropsDelta),
                Num(camera?.PreviewStalenessSeconds)
            ]);
        }

        return string.Join(",", values);
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string Num(double? value) =>
        value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : "";

    private static string Num(long? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";

    private static void DeleteOlderThanRetention(string logDir, string pattern, int keep)
    {
        foreach (var file in Directory.EnumerateFiles(logDir, pattern)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Skip(keep))
        {
            try { file.Delete(); }
            catch { /* best-effort diagnostic cleanup only */ }
        }
    }
}
