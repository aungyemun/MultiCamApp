using MultiCamApp.Capture;
using MultiCamApp.Recording;
using MultiCamApp.Ui;
using MultiCamApp.Utils;

namespace MultiCamApp.Core;

public sealed class AppLifecycleService
{
    private readonly LogService _log = new();
    private readonly AppConfig _config;
    private readonly PrivacyGuardService _privacy;
    private readonly ResourceManager _resources = new();
    private bool _userStartedRecording;
    private bool _wasPreviewingBeforeMinimize;
    private bool _windowCleanupRunning;

    public AppLifecycleService(AppConfig config, PrivacyGuardService privacy)
    {
        _config = config;
        _privacy = privacy;
    }

    public void NotifyRecordingStartedByUser() => _userStartedRecording = true;

    public void NotifyRecordingStopped() => _userStartedRecording = false;

    public async Task OnStopPreviewAsync(MainViewModel vm, CameraManager cameras)
    {
        await vm.StopPreviewAsync();
        if (_config.ReleaseCamerasOnStopPreview)
            await cameras.ReleaseAllCamerasAsync();
        _log.Info("lifecycle", "Preview stopped; cameras released");
    }

    public async Task OnStopRecordingAsync(RecordingController recording, CameraManager cameras)
    {
        await recording.StopAllRecordingsSafelyAsync(cameras.Slots);
        _log.Info("lifecycle", "Recording stopped safely");
    }

    public async Task OnWindowClosingAsync(MainViewModel vm, CameraManager cameras, RecordingController recording)
    {
        if (_windowCleanupRunning)
            return;

        _windowCleanupRunning = true;
        _log.Info("lifecycle", "App closing cleanup");
        if (vm.State.IsRecording)
            await vm.StopRecordingAsync();
        if (vm.IsPreviewLifecycleBusy)
            await vm.StopPreviewAsync();
        if (_config.ReleaseCamerasOnAppClose)
            await _resources.CleanupAllAsync(cameras, recording);
    }

    public async Task OnMinimizedAsync(MainViewModel vm, CameraManager cameras)
    {
        if (_privacy.ShouldContinueRecordingWhenMinimized(vm.State.RunState, _userStartedRecording))
        {
            _log.Info("lifecycle", "Minimized while recording — recording continues");
            return;
        }

        if (_privacy.ShouldPausePreviewOnMinimize(vm.State.RunState))
        {
            _wasPreviewingBeforeMinimize = true;
            await _resources.CleanupPreviewAsync(cameras);
            await cameras.ReleaseAllCamerasAsync();
            _log.Info("lifecycle", "Minimized — preview paused, cameras released");
        }
    }

    public async Task OnRestoredAsync(MainViewModel vm)
    {
        if (!_wasPreviewingBeforeMinimize) return;
        _wasPreviewingBeforeMinimize = false;
        _log.Info("lifecycle", "Restored — user may start preview again");
        await Task.CompletedTask;
    }

    public async Task OnFatalErrorAsync(CameraManager cameras, RecordingController recording)
    {
        _log.Error("lifecycle", "Fatal error cleanup");
        try { await _resources.CleanupAllAsync(cameras, recording); }
        catch { /* best effort */ }
    }
}
