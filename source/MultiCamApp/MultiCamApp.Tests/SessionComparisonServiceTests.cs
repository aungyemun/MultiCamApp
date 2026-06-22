using MultiCamApp.Metadata;
using MultiCamApp.Recording;
using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class SessionComparisonServiceTests
{
    [Fact]
    public void CompareSession_SingleCameraSession_SkipsInterCameraComparison()
    {
        var root = CreateSession(["cam1"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var camFolder = Path.Combine(sessionFolder, "cam1");
            var mp4 = Path.Combine(camFolder, "video.mp4");
            File.WriteAllText(mp4, "fake");
            File.WriteAllText(Path.Combine(camFolder, "metadata.json"), "{}");

            var entry = new VideoFileEntry
            {
                CameraSlot = "cam1",
                SessionFolder = sessionFolder,
                SessionLabel = Path.GetFileName(sessionFolder),
                FullPath = mp4,
                MetadataJsonPath = Path.Combine(camFolder, "metadata.json")
            };
            var video = BuildVideo(entry, frameCount: 1000, measuredFps: 30.0);

            var result = new SessionComparisonService().CompareSession(sessionFolder, [video], [entry]);

            Assert.True(result.SessionStatus is CameraAuditStatus.Pass or CameraAuditStatus.PassWithWarning);
            Assert.Null(result.InterCameraFrameDifference);
            Assert.Contains(result.InterpretationNotes,
                n => n.Contains("Single-camera session", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_TwoCameraSession_PassesWhenSynchronized()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var entries = new List<VideoFileEntry>();
            var videos = new List<VideoVerificationResult>();
            var frameCounts = new[] { 1000L, 1001L };
            var slots = new[] { "cam1", "cam2" };

            for (var i = 0; i < slots.Length; i++)
            {
                var camFolder = Path.Combine(sessionFolder, slots[i]);
                var mp4 = Path.Combine(camFolder, $"{slots[i]}.mp4");
                File.WriteAllText(mp4, "fake");
                File.WriteAllText(Path.Combine(camFolder, "metadata.json"), "{}");

                var entry = new VideoFileEntry
                {
                    CameraSlot = slots[i],
                    SessionFolder = sessionFolder,
                    SessionLabel = Path.GetFileName(sessionFolder),
                    FullPath = mp4,
                    MetadataJsonPath = Path.Combine(camFolder, "metadata.json")
                };
                entries.Add(entry);
                videos.Add(BuildVideo(entry, frameCounts[i], measuredFps: 29.0, startOffsetMs: 20));
            }

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(CameraAuditStatus.PassWithWarning, result.SessionStatus);
            Assert.Equal(1, result.InterCameraFrameDifference);
            Assert.Equal(20, result.StartOffsetMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_FailsWhenFrameDifferenceTooLarge()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var entries = new List<VideoFileEntry>();
            var videos = new List<VideoVerificationResult>();
            var frameCounts = new[] { 1000L, 2000L };
            var slots = new[] { "cam1", "cam2" };

            for (var i = 0; i < slots.Length; i++)
            {
                var camFolder = Path.Combine(sessionFolder, slots[i]);
                var mp4 = Path.Combine(camFolder, $"{slots[i]}.mp4");
                File.WriteAllText(mp4, "fake");
                File.WriteAllText(Path.Combine(camFolder, "metadata.json"), "{}");

                var entry = new VideoFileEntry
                {
                    CameraSlot = slots[i],
                    SessionFolder = sessionFolder,
                    SessionLabel = Path.GetFileName(sessionFolder),
                    FullPath = mp4,
                    MetadataJsonPath = Path.Combine(camFolder, "metadata.json")
                };
                entries.Add(entry);
                videos.Add(BuildVideo(entry, frameCounts[i], measuredFps: 29.0));
            }

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(CameraAuditStatus.Fail, result.SessionStatus);
            Assert.Equal(1000, result.InterCameraFrameDifference);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_OriginalCaptureAcceptsFrameCountDifferences()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var entries = new List<VideoFileEntry>();
            var videos = new List<VideoVerificationResult>();
            var frameCounts = new[] { 17808L, 18000L };
            var measured = new[] { 29.68, 30.0 };
            var slots = new[] { "cam1", "cam2" };

            for (var i = 0; i < slots.Length; i++)
            {
                var camFolder = Path.Combine(sessionFolder, slots[i]);
                var mp4 = Path.Combine(camFolder, $"{slots[i]}.mp4");
                File.WriteAllText(mp4, "fake");
                File.WriteAllText(Path.Combine(camFolder, "metadata.json"), "{}");

                var entry = new VideoFileEntry
                {
                    CameraSlot = slots[i],
                    SessionFolder = sessionFolder,
                    SessionLabel = Path.GetFileName(sessionFolder),
                    FullPath = mp4,
                    MetadataJsonPath = Path.Combine(camFolder, "metadata.json")
                };
                entries.Add(entry);
                var video = BuildVideo(entry, frameCounts[i], measuredFps: measured[i], startOffsetMs: 40);
                video.Metadata!.OriginalCaptureMode = true;
                video.Metadata.RecordingTimingMode = "OriginalCapture";
                video.Metadata.ConstantFrameCountMode = false;
                video.Metadata.FramesCaptured = frameCounts[i];
                video.Metadata.ScientificTimingStatus = CameraAuditStatus.PassOriginalTimingWithNote;
                video.ScientificTimingStatus = CameraAuditStatus.PassOriginalTimingWithNote;
                videos.Add(video);
            }

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.NotEqual(CameraAuditStatus.Fail, result.SessionStatus);
            Assert.Equal("OriginalCapture", result.SessionTimingMode);
            Assert.Equal(192, result.InterCameraFrameDifference);
            Assert.True(result.FrameCountDifferenceAcceptedBecauseOriginalMode);
            Assert.Contains(result.InterpretationNotes,
                n => n.Contains("Frame counts may differ", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Failures,
                f => f.Contains("frame difference", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_OriginalCaptureAcceptsStopBoundaryCapturedWrittenDifference()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var slots = new[] { "cam1", "cam2" };
            var (entries, videos) = BuildSessionVideos(sessionFolder, slots, [19700L, 19720L]);
            foreach (var video in videos)
            {
                video.Metadata!.RecordingTimingMode = "OriginalCapture";
                video.Metadata.OriginalCaptureMode = true;
                video.Metadata.ConstantFrameCountMode = false;
                video.Metadata.FrameTimestampCsvWritten = true;
                video.Metadata.FrameTimestampCsvRowCount = video.Metadata.FrameCount;
                video.Metadata.ScientificTimingStatus = CameraAuditStatus.PassWithWarning;
                video.ScientificTimingStatus = CameraAuditStatus.PassWithWarning;
            }

            var cam1 = videos[0].Metadata!;
            cam1.FramesCaptured = cam1.FrameCount + 1;
            cam1.RecordingDiagnostics = new RecordingDiagnosticsMetadataSummary
            {
                Camera = new RecordingDiagnosticsCameraSummary
                {
                    FramesWritten = cam1.FrameCount,
                    FramesCapturedTotal = cam1.FrameCount + 1,
                    FramesAcceptedForRecording = cam1.FrameCount,
                    FramesCapturedAfterStopRequested = 1,
                    FramesNotRecordedAfterStopRequested = 1,
                    FinalFlushCompleted = true,
                    WriterReleasedSuccessfully = true
                }
            };

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(CameraAuditStatus.PassWithWarning, result.SessionStatus);
            Assert.Equal(ScientificTimingConfidence.PassWithWarning, result.SessionScientificTimingConfidence);
            Assert.Contains(result.Warnings,
                w => w.Contains("One final frame occurred at the stop boundary", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Failures,
                f => f.Contains("individual camera audit failed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_WarnsForConstantFrameCountDuplicates()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var slots = new[] { "cam1", "cam2" };
            var (entries, videos) = BuildSessionVideos(sessionFolder, slots, [900L, 900L]);
            var metadata = videos[1].Metadata ?? throw new InvalidOperationException("Test video metadata missing");
            metadata.DuplicateFrames = 5;
            metadata.ConstantFrameCountMode = true;

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(CameraAuditStatus.PassWithWarning, result.SessionStatus);
            Assert.Equal(0, result.InterCameraFrameDifference);
            Assert.Contains(result.Warnings,
                w => w.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Failures,
                f => f.Contains("Pipeline integrity", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_InfersLegacyConstantFrameCount_WhenTimingModeMissingAndDuplicatesExist()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var slots = new[] { "cam1", "cam2" };
            var (entries, videos) = BuildSessionVideos(sessionFolder, slots, [900L, 900L]);
            var metadata = videos[1].Metadata ?? throw new InvalidOperationException("Test video metadata missing");
            metadata.RecordingTimingMode = "";
            metadata.OriginalCaptureMode = false;
            metadata.DuplicateFrames = 5;
            metadata.ConstantFrameCountMode = true;

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(OriginalCaptureAuditPolicy.LegacyConstantFrameCountMode, result.SessionTimingMode);
            Assert.Equal(CameraAuditStatus.PassWithWarning, result.SessionStatus);
            Assert.Contains(result.Warnings,
                w => w.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_FailsForDuplicatesInOriginalCapture()
    {
        var root = CreateSession(["cam1", "cam2"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var slots = new[] { "cam1", "cam2" };
            var (entries, videos) = BuildSessionVideos(sessionFolder, slots, [900L, 900L]);
            foreach (var video in videos)
            {
                video.Metadata!.RecordingTimingMode = "OriginalCapture";
                video.Metadata.OriginalCaptureMode = true;
                video.Metadata.FramesCaptured = video.Metadata.FrameCount;
                video.Metadata.ScientificTimingStatus = CameraAuditStatus.PassOriginalTiming;
                video.ScientificTimingStatus = CameraAuditStatus.PassOriginalTiming;
            }
            videos[1].Metadata!.DuplicateFrames = 1;

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal("OriginalCapture", result.SessionTimingMode);
            Assert.Equal(CameraAuditStatus.Fail, result.SessionStatus);
            Assert.Contains(result.Failures,
                w => w.Contains("duplicateFrames=0", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_ThreeCameraSession_ComparesAllThreeCameras()
    {
        var root = CreateSession(["cam1", "cam2", "cam3"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var slots = new[] { "cam1", "cam2", "cam3" };
            var frameCounts = new[] { 3000L, 3002L, 3001L };
            var (entries, videos) = BuildSessionVideos(sessionFolder, slots, frameCounts);

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(CameraAuditStatus.PassWithWarning, result.SessionStatus);
            Assert.Equal(2, result.InterCameraFrameDifference);
            Assert.Equal(3, result.CamerasFound.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompareSession_FourCameraSession_ComparesAllFourCameras()
    {
        var root = CreateSession(["cam1", "cam2", "cam3", "cam4"]);
        try
        {
            var sessionFolder = Directory.GetDirectories(root).Single();
            var slots = new[] { "cam1", "cam2", "cam3", "cam4" };
            var frameCounts = new[] { 4000L, 4001L, 4003L, 4002L };
            var (entries, videos) = BuildSessionVideos(sessionFolder, slots, frameCounts);

            var result = new SessionComparisonService().CompareSession(sessionFolder, videos, entries);

            Assert.Equal(CameraAuditStatus.PassWithWarning, result.SessionStatus);
            Assert.Equal(3, result.InterCameraFrameDifference);
            Assert.Equal(4, result.CamerasFound.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (List<VideoFileEntry> entries, List<VideoVerificationResult> videos) BuildSessionVideos(
        string sessionFolder,
        IReadOnlyList<string> slots,
        IReadOnlyList<long> frameCounts)
    {
        var entries = new List<VideoFileEntry>();
        var videos = new List<VideoVerificationResult>();
        for (var i = 0; i < slots.Count; i++)
        {
            var camFolder = Path.Combine(sessionFolder, slots[i]);
            var mp4 = Path.Combine(camFolder, $"{slots[i]}.mp4");
            File.WriteAllText(mp4, "fake");
            File.WriteAllText(Path.Combine(camFolder, "metadata.json"), "{}");

            var entry = new VideoFileEntry
            {
                CameraSlot = slots[i],
                SessionFolder = sessionFolder,
                SessionLabel = Path.GetFileName(sessionFolder),
                FullPath = mp4,
                MetadataJsonPath = Path.Combine(camFolder, "metadata.json")
            };
            entries.Add(entry);
            videos.Add(BuildVideo(entry, frameCounts[i], measuredFps: 29.0, startOffsetMs: 15 + i * 5));
        }

        return (entries, videos);
    }

    private static string CreateSession(IReadOnlyList<string> cameraSlots)
    {
        var root = Path.Combine(Path.GetTempPath(), "MultiCamAppSessionTests_" + Guid.NewGuid().ToString("N"));
        var sessionFolder = Path.Combine(root, "session_" + Guid.NewGuid().ToString("N"));
        foreach (var slot in cameraSlots)
            Directory.CreateDirectory(Path.Combine(sessionFolder, slot));
        return root;
    }

    private static VideoVerificationResult BuildVideo(
        VideoFileEntry entry,
        long frameCount,
        double measuredFps,
        double startOffsetMs = 0)
    {
        return new VideoVerificationResult
        {
            Entry = entry,
            Verdict = VerificationVerdict.Pass,
            Probe = new VideoProbeData
            {
                Success = true,
                HasVideoStream = true,
                Width = 1280,
                Height = 720,
                Fps = 30,
                DurationSeconds = frameCount / 30.0,
                FrameCount = frameCount,
                VideoCodec = "h264",
                PixelFormat = "yuv420p",
                FileSizeBytes = 1_000_000
            },
            Metadata = new CameraMetadataRecord
            {
                FrameCount = frameCount,
                MeasuredCameraFps = measuredFps,
                WallClockDurationSeconds = frameCount / measuredFps,
                FrameBasedDurationSeconds = frameCount / 30.0,
                InterCameraStartOffsetMs = startOffsetMs,
                ScientificTimingStatus = CameraAuditStatus.Pass
            },
            ActualResolutionDisplay = "1280x720",
            CodecDisplay = "h264",
            ScientificTimingStatus = CameraAuditStatus.Pass
        };
    }
}
