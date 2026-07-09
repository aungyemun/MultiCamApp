////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Text.Json;
using MultiCamApp.Capture;
using MultiCamApp.Experiment;
using MultiCamApp.Recording;
using MultiCamApp.Utils;

namespace MultiCamApp.Metadata;

/// <summary>
/// STABLE_CORE_V1 protected component — metadata.json, metadata.txt, and field layout.
/// Modification requires regression checklist; do not refactor casually.
/// </summary>
public sealed class MetadataWriter
{
    public Task WriteFromRecordingStatsAsync(string cameraFolder, RecordingCameraStats stats) =>
        WriteFromRecordingStatsAsync(cameraFolder, stats, null);

    public async Task WriteFromRecordingStatsAsync(
        string cameraFolder,
        RecordingCameraStats stats,
        LocomotorStandardSettings? locomotorSettings)
    {
        var path = Path.Combine(cameraFolder, "metadata.txt");
        var lines = BuildStatsLines(stats, cameraFolder).ToList();
        if (stats.LocomotorMode && locomotorSettings != null)
            lines.AddRange(LocomotorMetadataWriter.BuildMetadataLines(stats, locomotorSettings));
        await File.WriteAllLinesAsync(path, lines.Select(PrivacySanitizer.SanitizeForOutput));
        await File.WriteAllTextAsync(
            Path.Combine(cameraFolder, "metadata.json"),
            JsonSerializer.Serialize(CreatePrivacySafeStats(cameraFolder, stats), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<string> BuildStatsLines(RecordingCameraStats stats, string cameraFolder)
    {
        foreach (var line in BuildStatsSummaryLines(stats, cameraFolder))
            yield return line;
    }


    public async Task WriteCameraMetadataAsync(string cameraFolder, CameraRecordingMetadata meta)
    {
        var path = Path.Combine(cameraFolder, "metadata.txt");
        var summaryLines = BuildMetadataSummaryLines(meta, cameraFolder).ToList();
        await File.WriteAllLinesAsync(path, summaryLines.Select(PrivacySanitizer.SanitizeForOutput));
        await File.WriteAllTextAsync(
            Path.Combine(cameraFolder, "metadata.json"),
            JsonSerializer.Serialize(CreatePrivacySafeMetadata(cameraFolder, meta), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<string> BuildStatsSummaryLines(RecordingCameraStats stats, string cameraFolder)
    {
        var diag = stats.RecordingDiagnostics;
        if (diag == null)
        {
            try
            {
                var sessionFolder = Directory.GetParent(cameraFolder)?.FullName ?? cameraFolder;
                var summaryPath = Path.Combine(sessionFolder, "recording_diagnostics_summary.json");
                if (File.Exists(summaryPath))
                {
                    var jsonContent = File.ReadAllText(summaryPath);
                    var summary = JsonSerializer.Deserialize<RecordingDiagnosticsSummary>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (summary != null)
                    {
                        var cameraIdx = stats.DeviceIndex;
                        var foundCameraDiag = summary.Cameras.FirstOrDefault(c => c.CameraIndex == cameraIdx);
                        if (foundCameraDiag == null && summary.Cameras.Count > 0)
                        {
                            foundCameraDiag = summary.Cameras.FirstOrDefault(c =>
                                string.Equals(c.CameraName, stats.CameraDeviceName, StringComparison.OrdinalIgnoreCase))
                                ?? summary.Cameras.FirstOrDefault();
                        }

                        diag = new RecordingDiagnosticsMetadataSummary
                        {
                            CsvPath = summary.CsvPath,
                            SummaryJsonPath = summary.SummaryJsonPath,
                            SampleCount = summary.SampleCount,
                            SampleIntervalSeconds = summary.SampleIntervalSeconds,
                            AverageCpuPercent = summary.AverageCpuPercent,
                            MaxCpuPercent = summary.MaxCpuPercent,
                            CpuSamplesOver90Percent = summary.CpuSamplesOver90Percent,
                            MaxProcessMemoryMB = summary.MaxProcessMemoryMB,
                            ProcessMemoryContinuouslyIncreases = summary.ProcessMemoryContinuouslyIncreases,
                            SystemTotalMemoryMB = summary.SystemTotalMemoryMB,
                            MinSystemAvailableMemoryMB = summary.MinSystemAvailableMemoryMB,
                            MinDiskFreeSpaceGB = summary.MinDiskFreeSpaceGB,
                            MaxTotalCurrentFileSizeMB = summary.MaxTotalCurrentFileSizeMB,
                            TotalSessionSizeMB = summary.TotalSessionSizeMB,
                            EstimatedGBPerHourPerCamera = summary.EstimatedGBPerHourPerCamera,
                            EstimatedGBPerHourAllCameras = summary.EstimatedGBPerHourAllCameras,
                            MaxTotalFileSizeGrowthMBps = summary.MaxTotalFileSizeGrowthMBps,
                            MaxTotalWriterQueueDepth = summary.MaxTotalWriterQueueDepth,
                            MaxTotalWriterQueueCapacity = summary.MaxTotalWriterQueueCapacity,
                            MaxTotalQueueDrops = summary.MaxTotalQueueDrops,
                            SessionVerdictText = summary.SessionVerdictText,
                            ArtifactNote = summary.ArtifactNote,
                            Camera = foundCameraDiag
                        };
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        var cameraDiag = diag?.Camera;
        var playbackFps = stats.ContainerFps > 0
            ? stats.ContainerFps
            : stats.WriterFps > 0
                ? stats.WriterFps
                : stats.RecordingWriterFps;
        var measuredFps = stats.MeasuredCameraFps > 0 ? stats.MeasuredCameraFps : stats.MeasuredWriterFps;
        var timestampCsv = FileNameOnly(stats.FrameTimestampCsvPath);
        var sessionName = SafeText(stats.SessionName);
        var sessionFolderName = SafeFolderName(stats.SessionFolderName);
        var cameraDeviceName = SafeText(stats.CameraDeviceName);
        
        var qr = GetQuickResultFields(stats);
        var frameIntegrityPass = stats.DuplicateFrames == 0 && stats.PlaceholderFrames == 0 && stats.WriterQueueDrops == 0;
        var frameIntegrityResult = frameIntegrityPass ? "PASS" : "WARNING";
        var frameIntegrityReason = frameIntegrityPass
            ? "Real frames only. No duplicate frames, placeholder frames, or writer drops were detected."
            : BuildFrameIntegrityReason(stats);
        var realFpsDiffersFromPlayback = measuredFps > 0 && playbackFps > 0 && Math.Abs(measuredFps - playbackFps) > 0.5;
        var timingNote = BuildTimingNote(stats, measuredFps, playbackFps, realFpsDiffersFromPlayback);
        var captureStabilityNote = BuildCaptureStabilityNote(stats);

        yield return "MULTICAMAPP RECORDING SUMMARY";
        yield return "";
        yield return "[1] Quick Result";
        yield return $"- Recording mode: {DisplayTimingMode(stats.RecordingTimingMode)}";
        yield return $"- Scientific use: {qr.Status}";
        yield return $"- Frame integrity: {frameIntegrityResult}";
        yield return $"- Reason: {frameIntegrityReason}";
        yield return "";
        var activeCameraCount = stats.ActiveCameraCount > 0 ? stats.ActiveCameraCount : 1;
        var activeSlotsDisplay = stats.ActiveCameraSlots?.Length > 0
            ? string.Join(", ", stats.ActiveCameraSlots)
            : stats.CameraSlot;

        yield return "[2] Session";
        yield return $"- Session name: {ValueOrUnavailable(sessionName)}";
        yield return $"- Recording date/time: {FormatHumanDate(stats.RecordingDateTimeLocal)}";
        yield return $"- App version: {ValueOrUnavailable(stats.AppVersion)}";
        yield return $"- Build number: {(stats.BuildNumber > 0 ? stats.BuildNumber.ToString() : "Not available for this recording")}";
        yield return $"- Active cameras: {activeCameraCount}";
        yield return $"- Active camera slots: {activeSlotsDisplay}";
        yield return "";
        yield return "[3] Camera";
        yield return $"- Camera slot: {ValueOrUnavailable(stats.CameraSlot)}";
        yield return $"- Camera device: {ValueOrUnavailable(cameraDeviceName)}";
        yield return $"- Resolution: {ValueOrUnavailable(stats.Resolution)}";
        yield return $"- Backend: {ValueOrUnavailable(stats.Backend)}";
        yield return $"- Video file: {RelativeSessionPath(cameraFolder, stats.OutputFilePath)}";
        yield return "";
        yield return "[4] Recording Timing";
        yield return $"- Requested FPS: {FormatFps(stats.RequestedFps)}";
        yield return $"- Playback FPS: {FormatFps(playbackFps)}";
        yield return $"- Real capture FPS: {FormatFps(measuredFps)}";
        yield return $"- Wall-clock duration: {FormatDurationSec(stats.WallClockDurationSeconds > 0 ? stats.WallClockDurationSeconds : stats.WallDurationSeconds)}";
        yield return $"- Container duration: {FormatDurationSec(stats.ContainerDurationSeconds)}";
        yield return $"- Timing note: {timingNote}";
        yield return "";
        yield return "[5] Frame Integrity";
        yield return $"- Frames captured: {stats.FramesCaptured}";
        yield return $"- Frames written: {stats.FramesWritten}";
        yield return $"- Frames captured after stop boundary: {(cameraDiag != null ? cameraDiag.FramesCapturedAfterStopRequested.ToString() : "Not available for this recording")}";
        yield return $"- Duplicate frames: {stats.DuplicateFrames}";
        yield return $"- Placeholder frames: {stats.PlaceholderFrames}";
        yield return $"- Writer drops: {stats.WriterQueueDrops}";
        yield return $"- Recording status: {ValueOrUnavailable(stats.Status)}";
        yield return "";
        yield return "[6] Capture Stability";
        yield return $"- Stability grade: {ValueOrUnavailable(stats.FpsStabilityGrade)}";
        yield return $"- Mean frame interval: {FormatMsMetric(stats.CaptureIntervalMeanMs, stats.CaptureIntervalCount, stats.CaptureIntervalStatsMessage)}";
        yield return $"- Frame interval std: {FormatMsMetric(stats.CaptureIntervalStdMs, stats.CaptureIntervalCount, stats.CaptureIntervalStatsMessage)}";
        yield return $"- P95 frame interval: {FormatMsMetric(stats.CaptureIntervalP95Ms, stats.CaptureIntervalCount, stats.CaptureIntervalStatsMessage)}";
        yield return $"- Long gaps: {stats.LongGapCount}";
        yield return $"- Severe long gaps: {stats.SevereLongGapCount}";
        if (!string.IsNullOrWhiteSpace(captureStabilityNote))
            yield return $"- Note: {captureStabilityNote}";
        yield return "";
        yield return "[7] Scientific Use";
        yield return $"- Recommended timing source: {DisplayTimingSource(stats.TrimRecommendedTimeSource)}";
        yield return $"- Timestamp CSV: {ValueOrUnavailable(timestampCsv)}";
        yield return $"- Timestamp CSV rows: {stats.FrameTimestampCsvRowCount}";
        yield return $"- Trim start reference: {ValueOrUnavailable(stats.ScientificTrimStartReference)}";
        yield return $"- Trim end reference: {ValueOrUnavailable(stats.ScientificTrimEndReference)}";
        if (activeCameraCount > 1 && (realFpsDiffersFromPlayback || stats.SupportsTimestampBasedTrimming))
            yield return "- Frame-count note: Do not assume identical frame counts between cameras in Original Capture Mode.";
        yield return "";
        yield return "[8] Focus / Exposure";
        yield return $"- Focus mode (requested): {DisplayFocusMode(stats.FocusControlMode, stats.AutoFocusRequested, stats.ManualFocusRequestedValue)}";
        yield return $"- Final focus mode: {BuildFinalFocusMode(stats.AutoFocusRequested, stats.AutoFocusApplySucceeded, stats.AutoFocusReadbackValue, stats.ManualFocusRequestedValue, stats.FocusControlMode)}";
        yield return $"- Manual focus setting applied by user: {FriendlyBool(ManualFocusControlUsed(stats.FocusAppliedByUser, stats.FocusControlMode, stats.ManualFocusRequestedValue))}";
        yield return $"- Manual focus value requested for this camera: {FormatManualFocusRequest(stats.ManualFocusRequestedValue, focusFieldsExist: true)}";
        yield return $"- Manual focus readback: {FormatReadbackValue(stats.ManualFocusReadbackValue)}";
        yield return $"- Autofocus off requested for this camera: {FriendlyBool(!stats.AutoFocusRequested)}";
        yield return $"- Autofocus off confirmed: {FormatAutofocusOffConfirmation(stats.AutoFocusRequested, stats.AutoFocusApplySucceeded, stats.FocusControlMode, stats.AutoFocusReadbackValue)}";
        yield return $"- Focus warning: {FormatFocusWarning(stats.FocusWarning)}";
        yield return $"- Exposure: {ValueOrUnavailable(!string.IsNullOrWhiteSpace(stats.AutoExposureReadbackValue) && stats.AutoExposureReadbackValue != "unavailable" ? (stats.AutoExposureRequested ? "Auto exposure" : "Manual exposure") : "Not applied")}";
        yield return $"- Low-light compensation off requested: {FriendlyBool(stats.LowLightCompensationOffRequested)}";
        yield return $"- Low-light compensation off confirmed: {FormatLlcConfirmation(stats.LowLightCompensationOffConfirmed)}";
        yield return $"- White balance: {ValueOrUnavailable(stats.AutoWhiteBalanceStatus)}";
        if (stats.EnvironmentalLockActive)
        {
            yield return $"- Environmental lock: ACTIVE (hardware parameters frozen at recording start for dataset purity)";
            if (stats.FocusHardwareLocked)    yield return $"  - Focus locked at: {stats.FocusLockedAtSteps} steps";
            if (stats.ExposureHardwareLocked) yield return $"  - Exposure locked at: {stats.ExposureLockedAtSeconds:F6} s";
            if (stats.WhiteBalanceHardwareLocked) yield return $"  - White balance locked at: {stats.WhiteBalanceLockedAtK} K";
            if (stats.IsoHardwareLocked)      yield return $"  - ISO/gain: locked (fixed)";
        }
        else
        {
            yield return $"- Environmental lock: not active (camera auto modes may have adapted during recording)";
        }
        if (!string.IsNullOrWhiteSpace(stats.ExposureWarning))
            yield return $"- Exposure warning: {stats.ExposureWarning}";
        if (activeCameraCount > 1)
            yield return "- Note: Focus and exposure values are camera-specific and may differ between cameras.";
        yield return "";
        yield return "[9] Storage / Performance Summary";
        if (diag == null)
        {
            yield return $"- Writer queue max depth: {stats.WriterQueueDepthMax}";
            yield return "- Performance verdict: Not available for this recording";
        }
        else
        {
            var maxDepth = stats.WriterQueueDepthMax > 0 ? stats.WriterQueueDepthMax : diag.MaxTotalWriterQueueDepth;
            yield return $"- Writer queue max depth: {maxDepth}";
            yield return $"- Average CPU: {FormatCpu(diag.AverageCpuPercent)}";
            yield return $"- Max CPU: {FormatCpu(diag.MaxCpuPercent)}";
            yield return $"- Disk free space: {FormatDisk(diag.MinDiskFreeSpaceGB)}";
            yield return $"- Performance verdict: {ValueOrUnavailable(diag.SessionVerdictText ?? cameraDiag?.TimingVerdict)}";
        }
        yield return "";
        yield return "[10] Notes";
        yield return "- This TXT file is a privacy-safe human-readable summary.";
        yield return "- Full technical diagnostics are stored in metadata.json.";
    }

    private static IEnumerable<string> BuildMetadataSummaryLines(CameraRecordingMetadata meta, string cameraFolder)
    {
        var stats = new RecordingCameraStats
        {
            ActiveCameraCount = meta.ActiveCameraCount > 0 ? meta.ActiveCameraCount : 1,
            ActiveCameraSlots = meta.ActiveCameraSlots?.Length > 0 ? meta.ActiveCameraSlots : [meta.CameraSlot],
            MetadataSchemaVersion = meta.MetadataSchemaVersion,
            TxtMetadataPrivacySafe = meta.TxtMetadataPrivacySafe,
            AppName = meta.AppName,
            AppVersion = meta.AppVersion,
            BuildNumber = meta.BuildNumber,
            ReleaseStage = meta.ReleaseStage,
            RecordingTimingMode = meta.RecordingTimingMode,
            OriginalCaptureMode = meta.OriginalCaptureMode,
            ConstantFrameCountMode = meta.ConstantFrameCountMode,
            SessionName = meta.SessionName,
            SessionTitleOriginal = meta.SessionTitleOriginal,
            SessionFolderName = meta.SessionFolderName,
            RecordingDateTimeLocal = meta.RecordingDateTimeLocal,
            CameraSlot = meta.CameraSlot,
            CameraDeviceName = meta.CameraDeviceName,
            Backend = meta.Backend,
            DeviceIndex = meta.DeviceIndex,
            OutputFilePath = meta.FilePath,
            RequestedFps = meta.RequestedFps,
            RecordingWriterFps = meta.RecordingWriterFps,
            WriterFps = meta.WriterFps,
            ContainerFps = meta.ContainerFps,
            MeasuredCameraFps = meta.MeasuredCameraFps,
            EffectivePlaybackFps = meta.EffectivePlaybackFps,
            Width = meta.PixelWidth,
            Height = meta.PixelHeight,
            Codec = meta.Codec,
            Container = meta.ContainerFormat,
            RecordingApi = meta.RecordingApi,
            StartWallClockLocal = meta.RecordingStartTimeLocal,
            StopWallClockLocal = meta.RecordingStopTime.ToLocalTime(),
            FirstFrameLocalTime = meta.FirstFrameLocalTime,
            LastFrameLocalTime = meta.LastFrameLocalTime,
            DurationSeconds = meta.MonotonicDuration.TotalSeconds,
            WallClockDurationSeconds = meta.WallClockDurationSeconds,
            FrameBasedDurationSeconds = meta.FrameBasedDurationSeconds,
            ContainerDurationSeconds = meta.ContainerDurationSeconds,
            ContainerVsWallClockDifferenceSeconds = meta.ContainerVsWallClockDifferenceSeconds,
            FramesCaptured = meta.FramesCaptured,
            FramesWritten = meta.FrameCount,
            DuplicateFrames = meta.DuplicateFrames,
            PlaceholderFrames = meta.PlaceholderFrames,
            WriterQueueDrops = meta.WriterQueueDrops,
            Status = "completed",
            CaptureIntervalCount = meta.CaptureIntervalCount,
            CaptureIntervalStatsMessage = meta.CaptureIntervalStatsMessage,
            CaptureIntervalMeanMs = meta.CaptureIntervalMeanMs,
            CaptureIntervalMedianMs = meta.CaptureIntervalMedianMs,
            CaptureIntervalMinMs = meta.CaptureIntervalMinMs,
            CaptureIntervalMaxMs = meta.CaptureIntervalMaxMs,
            CaptureIntervalP95Ms = meta.CaptureIntervalP95Ms,
            CaptureIntervalP99Ms = meta.CaptureIntervalP99Ms,
            CaptureIntervalStdMs = meta.CaptureIntervalStdMs,
            MeasuredCameraFpsFromFirstLastFrame = meta.MeasuredCameraFpsFromFirstLastFrame,
            MeasuredCameraFpsFromMeanInterval = meta.MeasuredCameraFpsFromMeanInterval,
            ExpectedIntervalMs = meta.ExpectedIntervalMs,
            MeanIntervalErrorMs = meta.MeanIntervalErrorMs,
            LongGapCount = meta.LongGapCount,
            ShortGapCount = meta.ShortGapCount,
            SevereLongGapCount = meta.SevereLongGapCount,
            JitterScoreMs = meta.JitterScoreMs,
            FpsStabilityGrade = meta.FpsStabilityGrade,
            InterCameraStartOffsetMs = meta.InterCameraStartOffsetMs,
            InterCameraStopOffsetMs = meta.InterCameraStopOffsetMs,
            InterCameraFrameDifference = meta.InterCameraFrameDiff,
            MaxConsecutiveLateFrames = meta.MaxConsecutiveLateFrames,
            MaxConsecutiveNoFrame = meta.MaxConsecutiveNoFrame,
            ScientificTimingStatus = meta.ScientificTimingStatus,
            ScientificTimingMessage = meta.ScientificTimingMessage,
            RecommendedAction = meta.RecommendedAction,
            TrimRecommendedTimeSource = meta.TrimRecommendedTimeSource,
            ScientificTrimStartReference = meta.ScientificTrimStartReference,
            ScientificTrimEndReference = meta.ScientificTrimEndReference,
            SupportsTimestampBasedTrimming = meta.SupportsTimestampBasedTrimming,
            FrameTimestampCsvPath = meta.FrameTimestampCsvPath,
            FrameTimestampCsvWritten = meta.FrameTimestampCsvWritten,
            FrameTimestampCsvRowCount = meta.FrameTimestampCsvRowCount,
            RecordingDiagnostics = meta.RecordingDiagnostics,
            AutoFocusRequested = meta.AutoFocusRequested,
            AutoFocusApplyAttempted = meta.AutoFocusApplyAttempted,
            AutoFocusApplySucceeded = meta.AutoFocusApplySucceeded,
            AutoFocusReadbackValue = meta.AutoFocusReadbackValue,
            ManualFocusSupported = meta.ManualFocusSupported,
            ManualFocusRequestedValue = meta.ManualFocusRequestedValue,
            ManualFocusReadbackValue = meta.ManualFocusReadbackValue,
            FocusWarning = meta.FocusWarning,
            FocusAppliedByUser = meta.FocusAppliedByUser,
            AutoExposureRequested = meta.AutoExposureRequested,
            AutoExposureApplyAttempted = meta.AutoExposureApplyAttempted,
            AutoExposureApplySucceeded = meta.AutoExposureApplySucceeded,
            AutoExposureReadbackValue = meta.AutoExposureReadbackValue,
            ManualExposureSupported = meta.ManualExposureSupported,
            ManualExposureRequestedValue = meta.ManualExposureRequestedValue,
            ManualExposureReadbackValue = meta.ManualExposureReadbackValue,
            LowLightCompensationOffRequested = meta.LowLightCompensationOffRequested,
            LowLightCompensationOffConfirmed = meta.LowLightCompensationOffConfirmed,
            ExposureWarning = meta.ExposureWarning,
            AutoWhiteBalanceStatus = meta.AutoWhiteBalanceStatus,
            WhiteBalanceReadbackValue = meta.WhiteBalanceReadbackValue,
            EnvironmentalLockActive = meta.EnvironmentalLockActive,
            FocusHardwareLocked = meta.FocusHardwareLocked,
            FocusLockedAtSteps = meta.FocusLockedAtSteps,
            ExposureHardwareLocked = meta.ExposureHardwareLocked,
            ExposureLockedAtSeconds = meta.ExposureLockedAtSeconds,
            WhiteBalanceHardwareLocked = meta.WhiteBalanceHardwareLocked,
            WhiteBalanceLockedAtK = meta.WhiteBalanceLockedAtK,
            IsoHardwareLocked = meta.IsoHardwareLocked,
        };

        return BuildStatsSummaryLines(stats, cameraFolder);
    }

    private static string BuildFrameIntegrityReason(RecordingCameraStats stats)
    {
        var parts = new List<string>();
        if (stats.DuplicateFrames > 0) parts.Add($"{stats.DuplicateFrames} duplicate frame(s)");
        if (stats.PlaceholderFrames > 0) parts.Add($"{stats.PlaceholderFrames} placeholder frame(s)");
        if (stats.WriterQueueDrops > 0) parts.Add($"{stats.WriterQueueDrops} writer drop(s)");
        return parts.Count > 0
            ? $"Detected: {string.Join(", ", parts)}."
            : "Recording completed.";
    }

    private static string BuildTimingNote(RecordingCameraStats stats, double measuredFps, double playbackFps, bool realFpsDiffers)
    {
        if (!string.IsNullOrWhiteSpace(stats.ScientificTimingMessage) &&
            !string.Equals(stats.ScientificTimingMessage, "Not available for this recording", StringComparison.OrdinalIgnoreCase))
            return stats.ScientificTimingMessage;
        if (realFpsDiffers)
            return "Real capture FPS differs from playback FPS. Use timestamp CSV for timing-sensitive analysis.";
        return "Camera delivered stable real frames near requested FPS.";
    }

    private static string BuildCaptureStabilityNote(RecordingCameraStats stats)
    {
        if (stats.LongGapCount > 0 || stats.SevereLongGapCount > 0)
            return "Some frame arrival intervals were uneven. Use timestamp-based timing for timing-sensitive analysis.";
        if (stats.CaptureIntervalStdMs > 5 && stats.CaptureIntervalCount > 0)
            return "Minor frame interval variation detected. Timestamp CSV provides precise per-frame timing.";
        return "";
    }

    private static string FormatDurationSec(double seconds)
    {
        if (seconds <= 0) return "Not available for this recording";
        return $"{seconds:F3} sec";
    }

    private static IEnumerable<string> BuildFocusSection(
        bool focusFieldsExist,
        bool autoFocusRequested,
        bool? autoFocusApplySucceeded,
        string autoFocusReadbackValue,
        bool? manualFocusSupported,
        double? manualFocusRequestedValue,
        string manualFocusReadbackValue,
        string focusWarning)
    {
        yield return "";
        yield return "[10] Focus";
        yield return $"- Focus mode: {(focusFieldsExist ? DisplayFocusMode("", autoFocusRequested, manualFocusRequestedValue) : "Unknown")}";
        yield return $"- Manual focus control used in session: {FriendlyBool(manualFocusRequestedValue.HasValue)}";
        yield return $"- Manual focus value requested for this camera: {FormatManualFocusRequest(manualFocusRequestedValue, focusFieldsExist)}";
        yield return $"- Manual focus readback: {FormatReadbackValue(manualFocusReadbackValue)}";
        yield return $"- Autofocus off requested for this camera: {(focusFieldsExist ? FriendlyBool(!autoFocusRequested) : "No")}";
        yield return $"- Autofocus off confirmed: {(focusFieldsExist ? FormatAutofocusOffConfirmation(autoFocusRequested, autoFocusApplySucceeded, "") : "Unknown")}";
        yield return $"- Focus warning: {FormatFocusWarning(focusWarning)}";
    }

    private static IEnumerable<string> BuildExposureSection(
        bool autoExposureRequested,
        bool autoExposureApplyAttempted,
        bool? autoExposureApplySucceeded,
        string autoExposureReadbackValue,
        bool? manualExposureSupported,
        double? manualExposureRequestedValue,
        string manualExposureReadbackValue,
        bool lowLightCompensationOffRequested,
        bool? lowLightCompensationOffConfirmed,
        string exposureWarning)
    {
        yield return "";
        yield return "[11] Exposure";
        yield return $"- Auto exposure requested: {autoExposureRequested}";
        yield return $"- Auto exposure confirmed: {FormatNullableBool(autoExposureApplySucceeded)}";
        yield return $"- Manual exposure requested value: {(manualExposureRequestedValue.HasValue ? manualExposureRequestedValue.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "Not requested")}";
        yield return $"- Manual exposure readback value: {ValueOrUnavailable(manualExposureReadbackValue)}";
        yield return $"- Low-light compensation off requested: {FriendlyBool(lowLightCompensationOffRequested)}";
        yield return $"- Low-light compensation off confirmed: {FormatLlcConfirmation(lowLightCompensationOffConfirmed)}";
        yield return $"- Exposure warning: {(string.IsNullOrWhiteSpace(exposureWarning) ? "None" : exposureWarning)}";
        yield return "- Note: Exposure warning does NOT affect recording verdict or frame quality metrics.";
    }

    private static RecordingCameraStats CreatePrivacySafeStats(string cameraFolder, RecordingCameraStats stats) =>
        stats with
        {
            MetadataSchemaVersion = "2.0",
            TxtMetadataPrivacySafe = true,
            PrivacySafe = true,
            AbsolutePathsPersisted = false,
            HardwareIdentifiersPersisted = false,
            SessionName = SafeText(stats.SessionName),
            SessionTitleOriginal = SafeText(stats.SessionTitleOriginal),
            SessionFolderName = SafeFolderName(stats.SessionFolderName),
            CameraDeviceName = SafeText(stats.CameraDeviceName),
            Backend = SafeText(stats.Backend),
            CameraHardwareId = PrivacySanitizer.Redacted,
            DeviceId = PrivacySanitizer.Redacted,
            ComputerName = PrivacySanitizer.Redacted,
            OutputFilePath = RelativeSessionPath(cameraFolder, stats.OutputFilePath),
            FrameTimestampCsvPath = FileNameOnly(stats.FrameTimestampCsvPath),
            ScientificTimingMessage = SafeText(stats.ScientificTimingMessage),
            RecommendedAction = SafeText(stats.RecommendedAction),
            TrimWarning = SafeText(stats.TrimWarning),
            ScientificTrimStartReference = SafeText(stats.ScientificTrimStartReference),
            ScientificTrimEndReference = SafeText(stats.ScientificTrimEndReference),
            FocusWarning = SafeText(stats.FocusWarning),
            ExposureWarning = SafeText(stats.ExposureWarning),
            FocusModeSummary = SafeText(stats.FocusModeSummary),
            RecordingDiagnostics = SanitizeDiagnosticsSummary(stats.RecordingDiagnostics)
        };

    private static CameraRecordingMetadata CreatePrivacySafeMetadata(string cameraFolder, CameraRecordingMetadata meta)
    {
        var json = JsonSerializer.Serialize(meta);
        var copy = JsonSerializer.Deserialize<CameraRecordingMetadata>(json) ?? new CameraRecordingMetadata();
        copy.MetadataSchemaVersion = "2.0";
        copy.TxtMetadataPrivacySafe = true;
        copy.PrivacySafe = true;
        copy.AbsolutePathsPersisted = false;
        copy.HardwareIdentifiersPersisted = false;
        copy.SessionName = SafeText(meta.SessionName);
        copy.SessionTitleOriginal = SafeText(meta.SessionTitleOriginal);
        copy.SessionFolderName = SafeFolderName(meta.SessionFolderName);
        copy.CameraDeviceName = SafeText(meta.CameraDeviceName);
        copy.Backend = SafeText(meta.Backend);
        copy.CameraHardwareId = PrivacySanitizer.Redacted;
        copy.DeviceId = PrivacySanitizer.Redacted;
        copy.PcName = PrivacySanitizer.Redacted;
        copy.ComputerName = PrivacySanitizer.Redacted;
        copy.FilePath = RelativeSessionPath(cameraFolder, meta.FilePath);
        copy.FrameTimestampCsvPath = FileNameOnly(meta.FrameTimestampCsvPath);
        copy.ScientificTimingMessage = SafeText(meta.ScientificTimingMessage);
        copy.RecommendedAction = SafeText(meta.RecommendedAction);
        copy.TrimWarning = SafeText(meta.TrimWarning);
        copy.ScientificTrimStartReference = SafeText(meta.ScientificTrimStartReference);
        copy.ScientificTrimEndReference = SafeText(meta.ScientificTrimEndReference);
        copy.FocusWarning = SafeText(meta.FocusWarning);
        copy.ExposureWarning = SafeText(meta.ExposureWarning);
        copy.RecordingDiagnostics = SanitizeDiagnosticsSummary(meta.RecordingDiagnostics);
        return copy;
    }

    private static RecordingDiagnosticsMetadataSummary? SanitizeDiagnosticsSummary(RecordingDiagnosticsMetadataSummary? summary) =>
        summary == null
            ? null
            : summary with
            {
                CsvPath = FileNameOnly(summary.CsvPath),
                SummaryJsonPath = FileNameOnly(summary.SummaryJsonPath),
                ArtifactNote = PrivacySanitizer.SanitizeForOutput(summary.ArtifactNote),
                SessionVerdictText = PrivacySanitizer.SanitizeForOutput(summary.SessionVerdictText),
                Camera = summary.Camera == null
                    ? null
                    : summary.Camera with
                    {
                        CameraName = PrivacySanitizer.SanitizeForOutput(summary.Camera.CameraName)
                    }
            };

    private static bool DiagnosticsCountersDiffer(long framesCaptured, long framesWritten, RecordingDiagnosticsCameraSummary? camera) =>
        camera != null && (camera.FramesCaptured != framesCaptured || camera.FramesWritten != framesWritten);

    private static string RelativeSessionPath(string cameraFolder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Not available for this recording";
        try
        {
            var sessionFolder = Directory.GetParent(cameraFolder)?.FullName ?? cameraFolder;
            var relative = Path.GetRelativePath(sessionFolder, path);
            return relative.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(path) ?? "Not available for this recording";
        }
    }

    private static string FileNameOnly(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "Not available for this recording" : Path.GetFileName(path);

    private static string SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : PrivacySanitizer.SanitizeForOutput(value);

    private static string SafeFolderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        try
        {
            return Path.IsPathFullyQualified(value)
                ? Path.GetFileName(value)
                : SafeText(value);
        }
        catch
        {
            return SafeText(value);
        }
    }

    private static string FormatDate(DateTime? value) => value.HasValue ? value.Value.ToString("O") : "";
    private static string FormatHumanDate(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Not available for this recording";
    private static string FormatCpu(double? value) =>
        value.HasValue ? $"{value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%" : "Not available for this recording";
    private static string FormatMemory(double? value) =>
        value.HasValue ? $"{Math.Round(value.Value).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} MB" : "Not available for this recording";
    private static string FormatDisk(double? value) =>
        value.HasValue ? $"{value.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} GB" : "Not available for this recording";
    private static string FormatEstimatedGb(double? value) =>
        value.HasValue ? value.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "Not available for this recording";
    private static string FormatDuration(double value) =>
        $"{value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} sec";
    private static string FormatFps(double value) =>
        value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    private static string FormatMsValue(double value) =>
        $"{value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} ms";
    private static string FormatMsMetric(double value, long count, string message) =>
        count >= 1 ? $"{value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} ms" : "Not available for this recording";
    private static string FormatNullableBool(bool? value) => value.HasValue ? value.Value.ToString() : "unknown";
    private static string FormatNullableDouble(double? value) => value.HasValue ? value.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "unknown";
    private static string FriendlyBool(bool value) => value ? "Yes" : "No";
    private static bool ManualFocusControlUsed(bool focusAppliedByUser, string? focusControlMode, double? manualFocusRequestedValue) =>
        focusAppliedByUser
        || manualFocusRequestedValue.HasValue
        || string.Equals(focusControlMode, "manual", StringComparison.OrdinalIgnoreCase);

    private static string FormatManualFocusRequest(double? manualFocusRequestedValue, bool focusFieldsExist)
    {
        if (!focusFieldsExist)
            return "Not available";
        return manualFocusRequestedValue.HasValue
            ? manualFocusRequestedValue.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            : "Not changed during this recording";
    }

    private static string FormatReadbackValue(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "unavailable", StringComparison.OrdinalIgnoreCase)
            ? "Unavailable"
            : value.Trim();

    private static string DisplayFocusMode(string? focusControlMode, bool autoFocusRequested, double? manualFocusRequestedValue)
    {
        if (string.Equals(focusControlMode, "manual", StringComparison.OrdinalIgnoreCase) || manualFocusRequestedValue.HasValue)
            return "Manual focus";
        if (string.Equals(focusControlMode, "autofocus", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(focusControlMode, "autofocus_requested", StringComparison.OrdinalIgnoreCase) ||
            autoFocusRequested)
            return "Autofocus";
        if (string.Equals(focusControlMode, "autofocus_off_best_effort", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(focusControlMode, "manual_or_fixed_requested", StringComparison.OrdinalIgnoreCase))
            return "Autofocus off best-effort";
        return "Unknown";
    }

    private static string BuildFinalFocusMode(bool autoFocusRequested, bool? autoFocusApplySucceeded,
        string? autoFocusReadbackValue, double? manualFocusRequestedValue, string? focusControlMode)
    {
        var wantedManual = manualFocusRequestedValue.HasValue
            || string.Equals(focusControlMode, "manual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(focusControlMode, "manual_or_fixed_requested", StringComparison.OrdinalIgnoreCase)
            || string.Equals(focusControlMode, "autofocus_off_best_effort", StringComparison.OrdinalIgnoreCase);

        if (wantedManual)
        {
            // Driver readback says autofocus is active — camera may not support programmatic focus control
            if (string.Equals(autoFocusReadbackValue, "active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(autoFocusReadbackValue, "enabled", StringComparison.OrdinalIgnoreCase))
                return "Unknown/readback unreliable — manual was requested but autofocus readback shows active; camera may not support programmatic focus control";
            if (string.Equals(autoFocusReadbackValue, "unavailable", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(autoFocusReadbackValue))
                return "Manual (readback unavailable)";
            return "Manual";
        }

        if (autoFocusRequested)
            return "Auto";

        return "Unknown";
    }

    private static string FormatAutofocusOffConfirmation(bool autoFocusRequested, bool? autoFocusApplySucceeded,
        string? focusControlMode, string? autoFocusReadbackValue = null)
    {
        if (autoFocusRequested)
            return "No";
        if (string.Equals(focusControlMode, "not_supported", StringComparison.OrdinalIgnoreCase))
            return "Not supported";
        if (autoFocusApplySucceeded == true)
            return "Yes";
        if (autoFocusApplySucceeded == false)
        {
            // Readback shows autofocus still active — driver may not honour the request
            if (string.Equals(autoFocusReadbackValue, "active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(autoFocusReadbackValue, "enabled", StringComparison.OrdinalIgnoreCase))
                return "Unknown/readback unreliable";
            return "No";
        }
        return "Unknown";
    }
    private static string FormatLlcConfirmation(bool? value) =>
        value == true ? "Yes" : value == false ? "No" : "Unknown";
    private static string DisplayTimingMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "unavailable", StringComparison.OrdinalIgnoreCase))
            return "Not available for this recording";
        if (string.Equals(value, "OriginalCapture", StringComparison.OrdinalIgnoreCase))
            return "Original Capture Mode";
        return value;
    }
    private static string DisplayTimingSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "unavailable", StringComparison.OrdinalIgnoreCase))
            return "Not available for this recording";
        if (string.Equals(value, "PerFrameCaptureTimestamps", StringComparison.OrdinalIgnoreCase))
            return "Timestamp CSV";
        return value;
    }
    private static string ValueOrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "unavailable", StringComparison.OrdinalIgnoreCase)
            ? "Not available for this recording"
            : value;
    private static string FormatFocusWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning) || string.Equals(warning, "unavailable", StringComparison.OrdinalIgnoreCase))
            return "Not available for this recording";
        
        var cleaned = warning.Trim();
        if (cleaned.StartsWith("Focus warning:", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring("Focus warning:".Length).Trim();
        }
        if (cleaned.Length > 0)
        {
            cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
        }
        return cleaned;
    }

    private sealed record QuickResultFields(
        string Status,
        string Summary,
        string TimingNote,
        string RecommendedAction,
        string OriginalRealFramesOnly,
        long DuplicateFrames,
        long PlaceholderFrames,
        long WriterQueueDrops,
        string TimestampCsvWritten
    );

    private static QuickResultFields GetQuickResultFields(RecordingCameraStats stats)
    {
        var status = string.IsNullOrWhiteSpace(stats.ScientificTimingStatus) ? "Not available for this recording" : stats.ScientificTimingStatus;
        var message = string.IsNullOrWhiteSpace(stats.ScientificTimingMessage) ? "Not available for this recording" : stats.ScientificTimingMessage;
        var action = string.IsNullOrWhiteSpace(stats.RecommendedAction) ? "Review metadata.json and verification report." : stats.RecommendedAction;
        
        var originalFramesOnly = stats.OriginalCaptureMode
            && !stats.ConstantFrameCountMode
            && stats.DuplicateFrames == 0
            && stats.PlaceholderFrames == 0;

        string originalRealFramesOnlyStr = originalFramesOnly ? "Yes" : "No";
        
        if (string.Equals(status, "PASS_WITH_WARNING", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickResultFields(
                Status: "PASS_WITH_WARNING",
                Summary: "Real frames were preserved. No duplicate, placeholder, or dropped writer frames were detected.",
                TimingNote: "Capture jitter was detected. Use timestamp CSV for timing-sensitive analysis.",
                RecommendedAction: "Use timestamp CSV for trimming and analysis.",
                OriginalRealFramesOnly: "Yes",
                DuplicateFrames: 0,
                PlaceholderFrames: 0,
                WriterQueueDrops: 0,
                TimestampCsvWritten: "Yes"
            );
        }
        
        if (string.Equals(status, "PASS_ORIGINAL_TIMING", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickResultFields(
                Status: "PASS_ORIGINAL_TIMING",
                Summary: "Original Capture Mode preserved real camera frames only.",
                TimingNote: "No major timing issue detected.",
                RecommendedAction: "Ready for analysis. Use timestamp CSV for precise trimming.",
                OriginalRealFramesOnly: "Yes",
                DuplicateFrames: 0,
                PlaceholderFrames: 0,
                WriterQueueDrops: 0,
                TimestampCsvWritten: "Yes"
            );
        }

        string summary = "";
        string timingNote = "";
        string recAction = action;

        if (status.StartsWith("PASS_ORIGINAL_TIMING", StringComparison.OrdinalIgnoreCase))
        {
            summary = "Original Capture Mode preserved real camera frames only.";
            timingNote = message;
            if (string.Equals(action, "Review metadata.json and verification report.", StringComparison.OrdinalIgnoreCase))
            {
                recAction = "Ready for analysis. Use timestamp CSV for precise trimming.";
            }
        }
        else if (string.Equals(status, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            summary = message;
            timingNote = "Timing check failed. Review summary for details.";
            if (string.Equals(action, "Review metadata.json and verification report.", StringComparison.OrdinalIgnoreCase))
            {
                recAction = "Review diagnostics and log files for errors.";
            }
        }
        else
        {
            summary = "Recording completed successfully.";
            timingNote = message;
            if (string.Equals(action, "Review metadata.json and verification report.", StringComparison.OrdinalIgnoreCase))
            {
                recAction = "Ready for analysis. Use timestamp CSV for precise trimming.";
            }
        }

        return new QuickResultFields(
            Status: status,
            Summary: summary,
            TimingNote: timingNote,
            RecommendedAction: recAction,
            OriginalRealFramesOnly: originalRealFramesOnlyStr,
            DuplicateFrames: stats.DuplicateFrames,
            PlaceholderFrames: stats.PlaceholderFrames,
            WriterQueueDrops: stats.WriterQueueDrops,
            TimestampCsvWritten: stats.FrameTimestampCsvWritten ? "Yes" : "No"
        );
    }
}
