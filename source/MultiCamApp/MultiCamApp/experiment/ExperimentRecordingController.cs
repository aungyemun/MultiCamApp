using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Recording;
using MultiCamApp.Utils;

namespace MultiCamApp.Experiment;

/// <summary>Monotonic-timer experiment recording with auto-stop at target duration/frame count.</summary>
public sealed class ExperimentRecordingController
{
    private readonly LogService _log = new();
    private readonly MultiCameraRecordingCoordinator _coordinator;
    private readonly AppConfig _config;
    private CancellationTokenSource? _watchCts;
    private ExperimentSessionOptions? _activeSession;
    private IReadOnlyList<CameraSlotPipeline>? _activeSlots;

    public event Action? ExperimentDurationReached;

    public ExperimentRecordingController(MultiCameraRecordingCoordinator coordinator, AppConfig config)
    {
        _coordinator = coordinator;
        _config = config;
    }

    public ExperimentSessionOptions? ActiveSession => _activeSession;

    public async Task StartExperimentAsync(
        IReadOnlyList<CameraSlotPipeline> slots,
        ExperimentSessionOptions session,
        string? outputFolder,
        string sessionTitle)
    {
        _activeSession = session;
        _activeSlots = slots;

        foreach (var slot in slots)
            slot.BeginExperimentRecording(session);

        await _coordinator.StartAllAsync(slots, outputFolder, sessionTitle).ConfigureAwait(false);
        StartDurationWatcher(session, slots);
        _log.Info("experiment",
            $"Experiment recording started: {session.TargetDurationSeconds:F0}s @ {session.TargetFps:F0} fps, expected {session.ExpectedFrames} frames/camera");
    }

    public async Task StopExperimentAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        StopWatcher();
        await _coordinator.StopAllAsync(slots).ConfigureAwait(false);
        foreach (var slot in slots)
            slot.EndExperimentRecording();
        _activeSession = null;
        _activeSlots = null;
    }

    public bool ShouldBlockStart(ExperimentPreflightReport? preflight)
    {
        if (_activeSession?.Enabled != true) return false;
        if (!_config.ExperimentMode.PreflightRequired) return false;
        if (!_config.ExperimentMode.WarnBeforeStartIfPreflightNotPassed) return false;
        if (preflight == null) return true;
        return !preflight.CanStartStrictRecording;
    }

    private void StartDurationWatcher(ExperimentSessionOptions session, IReadOnlyList<CameraSlotPipeline> slots)
    {
        StopWatcher();
        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var targetSec = session.TargetDurationSeconds;
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var freq = (double)System.Diagnostics.Stopwatch.Frequency;

                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                    var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start) / freq;

                    var timeStop = elapsed >= targetSec
                                   || slots.Any(s => s.ShouldStopExperiment);

                    if (!timeStop) continue;

                    _log.Info("experiment",
                        $"Auto-stop: elapsed {elapsed:F2}s, frames {string.Join(",", slots.Select(s => s.ExperimentFramesWritten))}");
                    ExperimentDurationReached?.Invoke();
                    return;
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void StopWatcher()
    {
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _watchCts = null;
    }
}
