using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MultiCamApp.Capture;
using MultiCamApp.Metadata;
using MultiCamApp.Recording;
using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class MetadataWriterPrivacyTests
{
    [Fact]
    public async Task WriteFromRecordingStatsAsync_WritesPrivacySafeSummaryAndJson()
    {
        var root = Path.Combine(Path.GetTempPath(), $"multicam_metadata_{Guid.NewGuid():N}");
        var sessionFolder = Path.Combine(root, "scientific_session_001");
        var cameraFolder = Path.Combine(sessionFolder, "cam1");
        Directory.CreateDirectory(cameraFolder);

        try
        {
            var videoPath = Path.Combine(cameraFolder, "video.mp4");
            var timestampPath = Path.Combine(cameraFolder, "cam1_frame_timestamps.csv");
            var stats = new RecordingCameraStats
            {
                AppVersion = "1.0.83",
                BuildNumber = 184,
                ReleaseStage = "experimental",
                RecordingTimingMode = RecordingTimingModes.OriginalCapture,
                OriginalCaptureMode = true,
                ConstantFrameCountMode = false,
                SessionName = @"C:\Users\Alice\Videos\scientific_session_001",
                SessionFolderName = sessionFolder,
                RecordingDateTimeLocal = new DateTime(2026, 6, 21, 10, 30, 0),
                CameraSlot = "cam1",
                CameraDeviceName = @"USB Camera \\?\USB#VID_1234&PID_5678#ABCDEF1234567890",
                Backend = "OpenCV",
                DeviceId = @"USB#VID_1234&PID_5678#ABCDEF1234567890",
                CameraHardwareId = @"\\?\USB#VID_1234&PID_5678#ABCDEF1234567890",
                ComputerName = "SECRET-PC",
                OutputFilePath = videoPath,
                FrameTimestampCsvPath = timestampPath,
                RequestedFps = 30,
                WriterFps = 30,
                ContainerFps = 30,
                MeasuredCameraFps = 29.684,
                Width = 1920,
                Height = 1080,
                Container = "MP4",
                Codec = "H264",
                StartWallClockLocal = new DateTime(2026, 6, 21, 10, 30, 0),
                StopWallClockLocal = new DateTime(2026, 6, 21, 10, 40, 0),
                WallClockDurationSeconds = 600,
                FirstToLastFrameDurationSec = 599.8,
                ContainerDurationSeconds = 593.68,
                ContainerVsWallClockDifferenceSeconds = -6.32,
                FramesCaptured = 17810,
                FramesWritten = 17810,
                DuplicateFrames = 0,
                PlaceholderFrames = 0,
                WriterQueueDrops = 0,
                WriterQueueDepthMax = 2,
                FrameTimestampCsvWritten = true,
                FrameTimestampCsvRowCount = 17810,
                CaptureIntervalCount = 17809,
                CaptureIntervalMeanMs = 33.688,
                CaptureIntervalStdMs = 0.8,
                CaptureIntervalP95Ms = 34.2,
                CaptureIntervalP99Ms = 35.1,
                FpsStabilityGrade = "Excellent",
                ScientificTimingStatus = "PASS_ORIGINAL_TIMING_WITH_NOTE",
                ScientificTimingMessage = "Stable native FPS differs from playback FPS.",
                RecommendedAction = "Ready. Use timestamp CSV for timing-sensitive analysis.",
                TrimRecommendedTimeSource = "PerFrameCaptureTimestamps",
                ScientificTrimStartReference = "firstFrameCaptureMonotonicSec",
                ScientificTrimEndReference = "lastFrameCaptureMonotonicSec",
                SupportsTimestampBasedTrimming = true,
                Status = "completed"
            };

            await new MetadataWriter().WriteFromRecordingStatsAsync(cameraFolder, stats);

            var txt = await File.ReadAllTextAsync(Path.Combine(cameraFolder, "metadata.txt"));
            Assert.Contains("MULTICAMAPP RECORDING SUMMARY", txt);
            Assert.Contains("[1] Quick Result", txt);
            Assert.Contains("- Playback FPS: 30.000", txt);
            Assert.Contains("- Real capture FPS: 29.684", txt);
            Assert.Contains("- Timestamp CSV rows: 17810", txt);
            Assert.Contains("- This TXT file is a privacy-safe human-readable summary.", txt);
            Assert.DoesNotContain(root, txt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET-PC", txt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VID_1234", txt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Writer frames dequeued", txt);
            Assert.DoesNotContain("final flush timed out", txt, StringComparison.OrdinalIgnoreCase);

            using var jsonDoc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cameraFolder, "metadata.json")));
            var json = jsonDoc.RootElement;
            Assert.True(json.GetProperty("privacySafe").GetBoolean());
            Assert.False(json.GetProperty("absolutePathsPersisted").GetBoolean());
            Assert.False(json.GetProperty("hardwareIdentifiersPersisted").GetBoolean());
            Assert.Equal("cam1/video.mp4", json.GetProperty("OutputFilePath").GetString());
            Assert.Equal("cam1_frame_timestamps.csv", json.GetProperty("FrameTimestampCsvPath").GetString());
            Assert.Equal("redacted", json.GetProperty("DeviceId").GetString());
            Assert.Equal("redacted", json.GetProperty("ComputerName").GetString());

            var jsonText = jsonDoc.RootElement.GetRawText();
            Assert.DoesNotContain(root, jsonText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET-PC", jsonText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VID_1234", jsonText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MetadataTxt_RegressionTest_FieldsAreNotMerged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"multicam_regression_{Guid.NewGuid():N}");
        var sessionFolder = Path.Combine(root, "session_123");
        var cameraFolder = Path.Combine(sessionFolder, "cam1");
        Directory.CreateDirectory(cameraFolder);

        try
        {
            var stats = new RecordingCameraStats
            {
                AppName = "MultiCamApp",
                AppVersion = "1.0.83",
                SessionFolderName = sessionFolder,
                OutputFilePath = Path.Combine(cameraFolder, "cam1.mp4"),
                ScientificTimingStatus = "PASS_WITH_WARNING",
                TrimRecommendedTimeSource = "PerFrameCaptureTimestamps",
                RecordingTimingMode = "OriginalCapture",
                FocusWarning = "Focus warning: autofocus OFF was requested but not confirmed. Use camera/vendor controls if focus hunting is visible."
            };

            await new MetadataWriter().WriteFromRecordingStatsAsync(cameraFolder, stats);

            var txt = await File.ReadAllTextAsync(Path.Combine(cameraFolder, "metadata.txt"));
            var lines = txt.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Contains(lines, l => l.Contains("Performance verdict:"));
            Assert.Contains(lines, l => l.Contains("Frames captured after stop boundary:"));
            Assert.Contains(lines, l => l.Contains("Recommended timing source:"));
            Assert.Contains(lines, l => l.Contains("Recording mode:"));
            Assert.Contains(lines, l => l.Contains("Focus warning:"));
            Assert.Contains(lines, l => l.Contains("Video file:"));

            // Verify they each appear on their own line (no two of these appear on the same line)
            foreach (var line in lines)
            {
                int count = 0;
                if (line.Contains("Performance verdict:")) count++;
                if (line.Contains("Frames captured after stop boundary:")) count++;
                if (line.Contains("Recommended timing source:")) count++;
                if (line.Contains("Recording mode:")) count++;
                if (line.Contains("Focus warning:")) count++;
                if (line.Contains("Video file:")) count++;
                Assert.True(count <= 1, $"Multiple fields found on the same line: '{line}'");
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MetadataTxtAndVideoAuditReport_PrivacyTest_DoNotContainSensitivePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"multicam_privacy_{Guid.NewGuid():N}");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userName = Environment.UserName;
        var sessionFolder = Path.Combine(userProfile, "Videos", "session_privacy_test");
        var cameraFolder = Path.Combine(sessionFolder, "cam1");

        // We will output to temp root for safety
        var outputFolder = Path.Combine(root, "output");
        Directory.CreateDirectory(outputFolder);
        var localCamFolder = Path.Combine(outputFolder, "cam1");
        Directory.CreateDirectory(localCamFolder);

        try
        {
            var stats = new RecordingCameraStats
            {
                AppName = "MultiCamApp",
                AppVersion = "1.0.83",
                SessionFolderName = sessionFolder,
                OutputFilePath = Path.Combine(cameraFolder, "video.mp4")
            };

            await new MetadataWriter().WriteFromRecordingStatsAsync(localCamFolder, stats);
            var metadataTxt = await File.ReadAllTextAsync(Path.Combine(localCamFolder, "metadata.txt"));

            Assert.DoesNotContain(@"C:\Users\", metadataTxt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(userProfile, metadataTxt, StringComparison.OrdinalIgnoreCase);
            if (userName.Length >= 3)
                Assert.DoesNotContain(userName, metadataTxt, StringComparison.OrdinalIgnoreCase);

            var report = new VerificationReport
            {
                AppVersion = "1.0.83",
                Summary = new VerificationSummary
                {
                    SelectedFolder = sessionFolder,
                    TotalVideosFound = 1,
                    OverallVerdict = VerificationVerdict.Pass
                }
            };
            
            var videoResult = new VideoVerificationResult
            {
                Entry = new VideoFileEntry
                {
                    CameraSlot = "cam1",
                    FileName = "video.mp4",
                    FullPath = Path.Combine(cameraFolder, "video.mp4")
                },
                Verdict = VerificationVerdict.Pass,
                Metadata = new CameraMetadataRecord
                {
                    FilePath = Path.Combine(cameraFolder, "video.mp4"),
                    SessionFolderName = sessionFolder
                }
            };
            report.Videos.Add(videoResult);

            var auditResult = new RecordingSessionAuditResult
            {
                SessionFolder = sessionFolder,
                SessionLabel = "session_privacy_test",
                SessionTimingMode = "OriginalCapture",
                SessionStatus = "PASS",
                SessionVerdict = VerificationVerdict.Pass
            };
            auditResult.CameraVideos.Add(videoResult);
            report.SessionAudits.Add(auditResult);

            var writer = new VerificationReportWriter();
            var reportPath = Path.Combine(outputFolder, "video_audit_report.txt");
            await writer.ExportVideoAuditReportAsync(report, reportPath);

            var auditTxt = await File.ReadAllTextAsync(reportPath);

            Assert.DoesNotContain(@"C:\Users\", auditTxt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(userProfile, auditTxt, StringComparison.OrdinalIgnoreCase);
            if (userName.Length >= 3)
                Assert.DoesNotContain(userName, auditTxt, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
