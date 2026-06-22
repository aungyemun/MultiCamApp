using System.Text.Json;
using MultiCamApp.Diagnostics;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class PerformanceDiagnosticsTests
{
    [Fact]
    public void PerformanceSnapshot_serializes_core_fields()
    {
        var snapshot = new PerformanceSnapshot
        {
            RunState = "Recording",
            ProcessCpuPercent = 42.5,
            ActiveCameraCount = 2,
            Cameras =
            [
                new CameraPerformanceSnapshot
                {
                    Slot = 1,
                    FramesCapturedDelta = 30,
                    FramesWrittenDelta = 30,
                    WriterQueueDropsDelta = 0
                }
            ]
        };

        var json = JsonSerializer.Serialize(snapshot);

        Assert.Contains("Recording", json);
        Assert.Contains("FramesCapturedDelta", json);
    }

    [Fact]
    public void PerformanceLogWriter_creates_csv_header_safely()
    {
        var dir = CreateTempDir();
        try
        {
            var writer = new PerformanceLogWriter(dir, new DateTime(2026, 6, 18, 18, 0, 0));
            writer.Append(new PerformanceSnapshot
            {
                TimestampLocal = new DateTime(2026, 6, 18, 18, 0, 1),
                RunState = "Previewing",
                ActiveCameraCount = 1
            });

            var text = File.ReadAllText(writer.CsvPath);

            Assert.Contains("timestampLocal,elapsedSeconds,runState", text);
            Assert.Contains("Previewing", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Retention_cleanup_deletes_only_old_performance_logs()
    {
        var dir = CreateTempDir();
        try
        {
            for (var i = 0; i < 35; i++)
            {
                var csv = Path.Combine(dir, $"performance_monitor_20260618_1800{i:00}.csv");
                var summary = Path.Combine(dir, $"performance_summary_20260618_1800{i:00}.txt");
                File.WriteAllText(csv, "csv");
                File.WriteAllText(summary, "summary");
                File.SetLastWriteTimeUtc(csv, DateTime.UtcNow.AddMinutes(-i));
                File.SetLastWriteTimeUtc(summary, DateTime.UtcNow.AddMinutes(-i));
            }

            var keep = Path.Combine(dir, "PerformanceSummary.latest.txt");
            var unrelated = Path.Combine(dir, "recording_session_20260618_180000.txt");
            File.WriteAllText(keep, "latest");
            File.WriteAllText(unrelated, "recording");

            PerformanceLogWriter.CleanupRetention(dir);

            Assert.Equal(30, Directory.GetFiles(dir, "performance_monitor_*.csv").Length);
            Assert.Equal(30, Directory.GetFiles(dir, "performance_summary_*.txt").Length);
            Assert.True(File.Exists(keep));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Monitor_handles_missing_sources_without_crashing()
    {
        using var monitor = new PerformanceMonitorService(
            () => throw new InvalidOperationException("camera source unavailable"),
            () => throw new InvalidOperationException("state source unavailable"));

        var snapshot = monitor.CaptureSnapshotForTest();

        Assert.Equal("Unknown", snapshot.RunState);
        Assert.Empty(snapshot.Cameras);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"multicam_perf_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
