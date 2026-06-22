////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Recording;

/// <summary>
/// STABLE_CORE_V1 protected component — multi-camera start/stop coordinator.
/// Modification requires regression checklist; do not refactor casually.
/// Windows Camera–style multi-camera recording: one Start/Stop for all active slots (Cam1–Cam4).
/// Prepare session folder → prepare each camera → start all together → stop all → finalize MP4 + metadata.
/// </summary>
public sealed class MultiCameraRecordingCoordinator
{
    private readonly LogService _log = new();
    private readonly RecordingController _recording;
    private readonly AppConfig _config;

    public MultiCameraRecordingCoordinator(RecordingController recording, AppConfig config)
    {
        _recording = recording;
        _config = config;
    }

    /// <summary>OpenCV preview must record in the same pipeline — WinRT handoff stops preview (FPS 0 freeze).</summary>
    public bool UsesOpenCvRecording =>
        string.Equals(_config.PreviewEngine, "opencv", StringComparison.OrdinalIgnoreCase)
        || string.Equals(_config.RecordingEngine, "opencv", StringComparison.OrdinalIgnoreCase);

    public bool UsesWindowsCameraEngine => !UsesOpenCvRecording;

    /// <summary>Slots with a device selected for the current layout (Cam1..CamN).</summary>
    public static List<CameraSlotPipeline> GetLayoutSlots(
        IReadOnlyList<CameraSlotPipeline> pipelines,
        int layoutCount,
        Func<int, string?> getDeviceId)
    {
        var list = new List<CameraSlotPipeline>();
        for (var i = 0; i < layoutCount && i < pipelines.Count; i++)
        {
            if (string.IsNullOrEmpty(getDeviceId(i))) continue;
            list.Add(pipelines[i]);
        }
        return list;
    }

    public async Task StartAllAsync(
        IReadOnlyList<CameraSlotPipeline> slots,
        string? outputFolder,
        string sessionTitle)
    {
        if (slots.Count == 0)
            throw new InvalidOperationException("No camera selected for recording");

        var openCvSlots = slots.Where(s => s.UsesOpenCvRecording(_config)).ToList();
        var winRtSlots = slots.Where(s => !s.UsesOpenCvRecording(_config)).ToList();

        if (UsesOpenCvRecording && slots.Count >= 3 && winRtSlots.Count > 0)
        {
            var mixed = string.Join(", ", winRtSlots.Select(s => $"{s.SlotName}:{s.DeviceName}"));
            AppDiagnosticLogger.Recording($"RECORDING_MIXED_BACKEND_ALLOWED winrtSlots={mixed}");
            _log.Info("recording",
                $"Mixed preview backends detected; recording each ready slot on its working backend. WinRT slot(s): {mixed}");
        }

        if (openCvSlots.Count > 0)
        {
            _log.Info("recording", $"OpenCV record+preview on {openCvSlots.Count} camera(s)");
            await PrepareOpenCvRecordingAsync(openCvSlots);
        }

        if (winRtSlots.Count > 0)
        {
            _log.Info("recording", $"WinRT LowLag on {winRtSlots.Count} camera(s)");
            await PrepareWinRtSlotsForRecordingAsync(winRtSlots);
        }

        var ready = slots.Where(s => s.CanRecord(_config)).ToList();
        if (ready.Count == 0)
            throw new InvalidOperationException("No camera is ready to record — start preview first");

        var winRtOnly = ready.All(s => !s.UsesOpenCvRecording(_config));
        if (winRtOnly && UsesWindowsCameraEngine)
            await PrepareWindowsCameraRecordingAsync(ready);

        var sessionPath = _recording.PrepareSession(outputFolder, sessionTitle);
        _log.Info("recording", $"Session prepared: {sessionPath} ({ready.Count} camera(s))");
        AppDiagnosticLogger.Recording($"PREPARE session={sessionPath} readySlots={ready.Count} opencv={openCvSlots.Count} winrt={winRtSlots.Count}");
        await _recording.StartAsync(ready);
    }

    public async Task StopAllAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        await _recording.StopAllRecordingsSafelyAsync(slots);
        _log.Info("recording", "All cameras stopped (Windows Camera Stop pattern)");
        AppDiagnosticLogger.Recording("STOP_ALL cameras stopped");
    }

    private async Task PrepareWindowsCameraRecordingAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        _log.Info("recording", $"Handoff {slots.Count} camera(s) to WinRT (Windows Camera style)");

        foreach (var slot in slots)
            await slot.StopPreviewForRecordingHandoffAsync();

        var delay = Math.Clamp(_config.RecordingHandoffDelayMs, 200, 2000);
        await Task.Delay(delay);
        OpenCvDeviceSession.Reset();

        foreach (var slot in slots)
            await slot.PrepareWinRtForRecordingAsync(_config);
    }

    /// <summary>WinRT-only slots already on WinRT preview (e.g. duplicate USB #2) — no OpenCV handoff.</summary>
    private async Task PrepareWinRtSlotsForRecordingAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        foreach (var slot in slots)
            await slot.EnsureReadyForRecordingAsync(_config);
    }

    private async Task PrepareOpenCvRecordingAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        foreach (var slot in slots)
            await slot.EnsureReadyForRecordingAsync(_config);
    }
}
