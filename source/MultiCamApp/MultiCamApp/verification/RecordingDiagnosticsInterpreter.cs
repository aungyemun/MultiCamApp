using MultiCamApp.Recording;

namespace MultiCamApp.Verification;

public static class RecordingDiagnosticsInterpreter
{
    private const double WriterWriteSpikeMs = 100;
    private const double LargeSessionGbPerHour = 20;
    private const double LargeSessionSizeMB = 50 * 1024;

    public static IReadOnlyList<string> BuildLikelyBottlenecks(IReadOnlyList<CameraMetadataRecord> cameras)
    {
        if (cameras.Count == 0)
            return ["Recording diagnostics unavailable."];

        var notes = new List<string>();
        var diagnostics = cameras.Select(c => c.RecordingDiagnostics).Where(d => d != null).ToList();
        var sessionDiag = diagnostics.FirstOrDefault();
        var totalQueueDrops = cameras.Sum(c => c.WriterQueueDrops)
            + diagnostics.Sum(d => d?.MaxTotalQueueDrops ?? 0);
        var writerSpike = cameras.Any(c => c.RecordingDiagnostics?.Camera?.MaxWriterWriteMaxMs >= WriterWriteSpikeMs);
        if (totalQueueDrops > 0 && writerSpike)
            notes.Add("Likely writer/encoder/disk stall.");

        if (sessionDiag?.CpuSamplesOver90Percent >= 3)
            notes.Add("Possible CPU bottleneck.");

        if (sessionDiag?.MinSystemAvailableMemoryMB is > 0 and < 1000)
            notes.Add("Possible RAM pressure.");

        if (sessionDiag?.ProcessMemoryContinuouslyIncreases == true)
            notes.Add("Possible memory growth or memory leak.");

        var unstableCount = cameras.Count(IsCameraUnstable);
        if (unstableCount == 1 && cameras.Count > 1)
            notes.Add("Possible camera/USB-port/driver-specific issue.");
        else if (unstableCount == cameras.Count && cameras.Count > 1)
            notes.Add("Possible system-wide stall or USB bandwidth issue.");

        if (cameras.Any(IsAutofocusEnabled))
            notes.Add("Autofocus hunting may affect image sharpness, but does not necessarily cause frame drops.");

        if (totalQueueDrops == 0 && cameras.All(IsStableWithCsv))
            notes.Add("No major recording bottleneck detected.");

        return notes.Count > 0 ? notes : ["Recording diagnostics inconclusive."];
    }

    public static IReadOnlyList<string> BuildRecommendedActions(IReadOnlyList<CameraMetadataRecord> cameras)
    {
        if (cameras.Count == 0)
            return ["Recording diagnostics unavailable."];

        var actions = new List<string>();
        var totalQueueDrops = cameras.Sum(c => c.WriterQueueDrops)
            + cameras.Sum(c => c.RecordingDiagnostics?.MaxTotalQueueDrops ?? 0);
        if (totalQueueDrops > 0)
            actions.Add("Repeat recording or reduce resolution/camera count. Use only queueDrop=0 sessions for strict analysis.");

        var sessionDiag = cameras.Select(c => c.RecordingDiagnostics).FirstOrDefault(d => d != null);
        if (sessionDiag is { EstimatedGBPerHourAllCameras: >= LargeSessionGbPerHour }
            || sessionDiag is { TotalSessionSizeMB: >= LargeSessionSizeMB })
        {
            actions.Add("Check storage capacity before long recordings.");
        }

        if (cameras.Any(IsAutofocusEnabled))
            actions.Add("Consider disabling autofocus or locking focus before recording if supported.");

        if (actions.Count == 0 && totalQueueDrops == 0 && cameras.All(IsStableWithCsv))
            actions.Add("Usable for scientific analysis. Use timestamp CSV for timing-sensitive analysis.");

        return actions.Count > 0 ? actions : ["Review diagnostics before scientific use."];
    }

    public static IReadOnlyList<string> BuildLikelyBottlenecks(CameraMetadataRecord camera) =>
        BuildLikelyBottlenecks([camera]);

    public static IReadOnlyList<string> BuildRecommendedActions(CameraMetadataRecord camera) =>
        BuildRecommendedActions([camera]);

    private static bool IsCameraUnstable(CameraMetadataRecord camera)
    {
        var diag = camera.RecordingDiagnostics?.Camera;
        return camera.WriterQueueDrops > 0
               || diag?.MaxWriterQueueDrops > 0
               || string.Equals(camera.FpsStabilityGrade, "Unstable", StringComparison.OrdinalIgnoreCase)
               || string.Equals(camera.FpsStabilityGrade, "Failed", StringComparison.OrdinalIgnoreCase)
               || camera.SevereLongGapCount > 0
               || (diag?.CaptureIntervalStdMs ?? diag?.MaxCaptureIntervalStdMs) > 20;
    }

    private static bool IsStableWithCsv(CameraMetadataRecord camera)
    {
        var stable = string.Equals(camera.FpsStabilityGrade, "Excellent", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(camera.FpsStabilityGrade, "Good", StringComparison.OrdinalIgnoreCase)
                     || (string.IsNullOrWhiteSpace(camera.FpsStabilityGrade)
                         && camera.CaptureIntervalStdMs > 0
                         && camera.SevereLongGapCount == 0);
        var csvOk = camera.FrameTimestampCsvWritten
                    && camera.FrameTimestampCsvRowCount == camera.FrameCount
                    && camera.FrameCount > 0;
        var noDrops = camera.WriterQueueDrops == 0
                      && (camera.RecordingDiagnostics?.Camera?.MaxWriterQueueDrops ?? 0) == 0;
        return stable && csvOk && noDrops;
    }

    private static bool IsAutofocusEnabled(CameraMetadataRecord camera)
    {
        var enabled = camera.RecordingDiagnostics?.Camera?.AutoFocusEnabled;
        return string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(enabled, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(enabled, "enabled", StringComparison.OrdinalIgnoreCase);
    }
}
