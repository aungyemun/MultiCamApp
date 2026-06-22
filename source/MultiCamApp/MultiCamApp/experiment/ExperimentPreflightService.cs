using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Experiment;

public sealed class ExperimentPreflightService
{
    private readonly LogService _log = new();

    public async Task<ExperimentPreflightReport> RunAsync(
        IReadOnlyList<CameraSlotPipeline> slots,
        AppConfig config,
        ExperimentSessionOptions session,
        string? outputFolder,
        CancellationToken cancellationToken = default)
    {
        var settings = config.ExperimentMode;
        var durationSec = Math.Clamp(settings.PreflightDurationSeconds, 10, 30);
        var report = new ExperimentPreflightReport
        {
            TargetFps = session.TargetFps,
            PreflightDurationSeconds = durationSec
        };

        var diskOk = await TestDiskWriteSpeedAsync(outputFolder, settings.MinDiskWriteBytesPerSecond, cancellationToken)
            .ConfigureAwait(false);

        var multiCamera = slots.Count >= 2;
        foreach (var slot in slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cam = await TestCameraAsync(slot, config, session, durationSec, diskOk, settings, multiCamera, cancellationToken)
                .ConfigureAwait(false);
            report.Cameras.Add(cam);
        }

        report.OverallResult = Aggregate(report.Cameras.Select(c => c.Result));
        report.CanStartStrictRecording = report.Cameras.All(c => c.Result != ExperimentCheckVerdict.Fail);
        report.Summary = report.CanStartStrictRecording
            ? report.Cameras.Any(c => c.Result == ExperimentCheckVerdict.Warning)
                ? "Preflight WARNING — review cameras before strict experiment."
                : "Preflight PASS — hardware looks suitable for strict timing."
            : "Preflight FAIL — one or more cameras cannot maintain target FPS reliably.";

        _log.Info("experiment", $"Preflight {report.OverallResult}: {report.Cameras.Count} camera(s)");
        return report;
    }

    private async Task<ExperimentPreflightCameraResult> TestCameraAsync(
        CameraSlotPipeline slot,
        AppConfig config,
        ExperimentSessionOptions session,
        int durationSec,
        bool diskOk,
        ExperimentModeSettings settings,
        bool multiCamera,
        CancellationToken cancellationToken)
    {
        var result = new ExperimentPreflightCameraResult
        {
            CameraSlot = slot.SlotName,
            DeviceName = slot.DeviceName,
            SelectedWidth = slot.RecordWidth,
            SelectedHeight = slot.RecordHeight,
            SelectedFps = session.TargetFps,
            DiskSpeedOk = diskOk
        };

        if (slot.Status is not ("Previewing" or "Ready"))
        {
            result.Messages.Add("Camera not in preview — start preview first.");
            result.Result = ExperimentCheckVerdict.Fail;
            result.CameraOpened = false;
            return result;
        }

        result.CameraOpened = true;

        if (config.PreferredCaptureWidth > 0 && config.PreferredCaptureHeight > 0
            && slot.RecordWidth > 0 && slot.RecordHeight > 0
            && (slot.RecordWidth != config.PreferredCaptureWidth || slot.RecordHeight != config.PreferredCaptureHeight))
        {
            result.Messages.Add(
                $"Live capture is {CaptureResolutionPreset.ToLabel(slot.RecordWidth, slot.RecordHeight)}; video settings request {CaptureResolutionPreset.ToLabel(config.PreferredCaptureWidth, config.PreferredCaptureHeight)}.");
        }

        var startCapture = slot.CaptureFrameCount;
        await Task.Delay(TimeSpan.FromSeconds(durationSec), cancellationToken).ConfigureAwait(false);
        var captured = Math.Max(0, slot.CaptureFrameCount - startCapture);
        var expectedPreflight = FrameCountValidator.ComputeExpectedFrames(durationSec, session.TargetFps);
        var dropped = Math.Max(0, expectedPreflight - captured);

        var meanFps = captured / Math.Max(0.001, durationSec);

        var summary = new FrameTimingSummary
        {
            TargetFps = session.TargetFps,
            TargetDurationSeconds = durationSec,
            ExpectedFrames = expectedPreflight,
            ActualFramesWritten = captured,
            FramesCaptured = captured,
            DroppedFrames = dropped,
            MeanFps = meanFps,
            DurationSeconds = durationSec,
            FpsDrift = Math.Abs(meanFps - session.TargetFps)
        };
        var targetFps = session.TargetFps;
        var fpsDelta = Math.Abs(summary.MeanFps - targetFps);
        var fpsRatio = targetFps > 0 ? summary.MeanFps / targetFps : 0;
        var dropRatio = expectedPreflight > 0 ? dropped / (double)expectedPreflight : 0;
        var minFpsRatio = multiCamera
            ? Math.Min(settings.PreflightMinFpsRatio, settings.PreflightMinFpsRatioMultiCamera)
            : settings.PreflightMinFpsRatio;
        var intervalOk = summary.FrameIntervalStdDevMs <= settings.PreflightIntervalStabilityPassMs * 5
                         || summary.FrameIntervalStdDevMs <= 0;

        ExperimentCheckVerdict verdict;
        if (fpsRatio < minFpsRatio
            || fpsDelta > settings.PreflightFpsWarningTolerance
            || dropRatio > settings.PreflightMaxDroppedFrameRatioFail)
        {
            verdict = ExperimentCheckVerdict.Fail;
        }
        else if (fpsDelta > settings.PreflightFpsPassTolerance
                 || dropRatio > settings.PreflightMaxDroppedFrameRatioWarning
                 || !intervalOk)
        {
            verdict = ExperimentCheckVerdict.Warning;
        }
        else
        {
            verdict = ExperimentCheckVerdict.Pass;
        }

        if (verdict == ExperimentCheckVerdict.Warning && multiCamera && dropRatio > 0)
        {
            result.Messages.Add(
                "Minor frame drops are common with multiple USB cameras at 1080p; acceptable for most locomotor experiments.");
        }

        if (verdict == ExperimentCheckVerdict.Fail)
            result.Messages.Add(
                "This camera/PC combination may not maintain exact target FPS for 10 minutes. Reduce resolution, use another camera, or use measured FPS/timestamp metadata from Original Capture timing.");

        if (!diskOk)
        {
            result.Messages.Add("Output folder disk write speed is below recommended minimum.");
            if (verdict == ExperimentCheckVerdict.Pass)
                verdict = ExperimentCheckVerdict.Warning;
        }

        if (!slot.UsesOpenCvRecording(config))
            result.Messages.Add("WinRT recording path — frame timing is driver-dependent; OpenCV recommended for strict experiments.");

        result.ActualFps = summary.MeanFps;
        result.FramesCaptured = summary.FramesCaptured;
        result.DroppedFrames = summary.DroppedFrames;
        result.DuplicateFrames = summary.DuplicateFrames;
        result.MeanFrameIntervalMs = summary.AverageFrameIntervalMs;
        result.FrameIntervalStdDevMs = summary.FrameIntervalStdDevMs;
        result.Result = verdict;
        return result;
    }

    private static async Task<bool> TestDiskWriteSpeedAsync(
        string? outputFolder,
        long minBytesPerSecond,
        CancellationToken cancellationToken)
    {
        try
        {
            var basePath = string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                : outputFolder;
            Directory.CreateDirectory(basePath);
            var testFile = Path.Combine(basePath, $".multicam_preflight_{Guid.NewGuid():N}.tmp");
            var data = new byte[512 * 1024];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.Write, FileShare.None,
                           bufferSize: 65536, useAsync: true))
            {
                for (var i = 0; i < 20; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await fs.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                }
                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            sw.Stop();
            try { File.Delete(testFile); } catch { /* ignore */ }

            var bytes = data.Length * 20L;
            var bps = bytes / Math.Max(0.001, sw.Elapsed.TotalSeconds);
            return bps >= minBytesPerSecond;
        }
        catch
        {
            return false;
        }
    }

    private static ExperimentCheckVerdict Aggregate(IEnumerable<ExperimentCheckVerdict> verdicts)
    {
        if (verdicts.Any(v => v == ExperimentCheckVerdict.Fail)) return ExperimentCheckVerdict.Fail;
        if (verdicts.Any(v => v == ExperimentCheckVerdict.Warning)) return ExperimentCheckVerdict.Warning;
        return ExperimentCheckVerdict.Pass;
    }
}
