////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Devices;
using Windows.Storage;
using Windows.Devices.Enumeration;
using MultiCamApp.Core;
using MultiCamApp.Diagnostics;
using MultiCamApp.Experiment;
using MultiCamApp.Recording;
using MultiCamApp.Utils;
using System.Text;

namespace MultiCamApp.Capture;

/// <summary>One camera slot: OpenCV preview + WinRT recording.</summary>
public sealed class CameraSlotPipeline : IDisposable
{
    private readonly LogService _log = new();
    private readonly PreviewController _winRtPreview = new();
    private readonly OpenCvPreviewController _openCvPreview = new();
    private readonly FpsMonitor _fpsMonitor = new();
    private readonly CameraModeSelector _modeSelector = new();
    private MediaCapture? _capture;
    private LowLagMediaRecording? _lowLagRecording;
    private StorageFile? _recordingFile;
    private string? _deviceId;
    private DeviceInformation? _deviceInfo;
    private CameraKind _deviceKind = CameraKind.Unknown;
    private int _openCvIndex;
    private OpenCvDeviceBinding? _openCvBinding;
    private bool _useOpenCvPreview = true;
    private bool _winRtOpenedForRecord;
    private bool _resumePreviewAfterRecord;
    private IReadOnlyList<string?>? _layoutSelectedDeviceIds;
    private bool _recordLimitHandlerAttached;
    private ExperimentSessionOptions? _experimentSession;
    private CameraFocusControlStatus _lastFocusControlStatus = CameraFocusControlStatus.NotAttempted(false);
    private CameraExposureControlStatus _lastExposureControlStatus = CameraExposureControlStatus.NotAttempted(true);

    public int SlotIndex { get; }
    public string? AssignedDeviceId => _deviceId;
    public bool WasPreviewingBeforeDisconnect { get; private set; }
    public bool IsDisconnected => Status is "Disconnected" or "Lost connection";
    public string SlotName => $"cam{SlotIndex + 1}";
    public MediaCapture? Capture => _capture;
    public FpsMonitor FpsMonitor => _fpsMonitor;
    public int DirectShowIndex => _openCvIndex;

    /// <summary>Device frames received (OpenCV reads or WinRT arrivals), not UI-throttled preview deliveries.</summary>
    public long CaptureFrameCount =>
        PreviewUsesWinRt ? _winRtPreview.CaptureFrameCount : _openCvPreview.CaptureFrameCount;
    public long OpenCvRecordedFrameCount => _openCvPreview.FramesWritten;
    public long OpenCvPreviewFramesCapturedSinceRecord => _openCvPreview.FramesCapturedSinceRecordStart;
    public long ExperimentFramesWritten => _openCvPreview.FramesWritten;
    public long CurrentOpenCvFramesWritten => _openCvPreview.FramesWritten;
    public long CurrentOpenCvWriterQueueDrops => _openCvPreview.WriterQueueDrops;
    public int ActualPreviewWidth => PreviewUsesWinRt ? RecordWidth : _openCvPreview.LiveWidth;
    public int ActualPreviewHeight => PreviewUsesWinRt ? RecordHeight : _openCvPreview.LiveHeight;
    public double ActualPreviewFps => PreviewUsesWinRt ? ObservedCaptureFps : _openCvPreview.LiveFps;
    public double LatestPreviewFrameAgeMs => PreviewUsesWinRt ? double.NaN : _openCvPreview.LatestFrameAgeMs;
    public bool ShouldStopExperiment =>
        _experimentSession?.Enabled == true && _openCvPreview.ShouldStopExperimentRecording;
    public double OpenCvRecordWriterFps => _openCvPreview.LastRecordWriterFps;
    public RecordingCameraStats? LastOpenCvRecordingStats { get; private set; }
    public string CurrentRecordingFilePath => _recordingFile?.Path ?? LastOpenCvRecordingStats?.OutputFilePath ?? "";
    public string Status { get; private set; } = "Idle";
    public PreviewSlotStateKind PreviewSlotState { get; private set; } = PreviewSlotStateKind.Idle;
    public string ResolutionText { get; private set; } = "-";
    public int RecordWidth { get; private set; }
    public int RecordHeight { get; private set; }
    public double RequestedFps { get; private set; } = 30;
    public double ObservedCaptureFps { get; private set; }
    public double SelectedDeviceFps => SelectedMode?.SelectedDeviceFps > 0
        ? SelectedMode.SelectedDeviceFps
        : SelectedMode?.Fps ?? ObservedCaptureFps;
    public double RecordingWriterFps { get; private set; }
    public CameraMode? SelectedMode { get; private set; }
    public string? LastError { get; private set; }
    public bool CaptureResolutionMatched { get; private set; } = true;
    public bool CameraAccessDenied { get; private set; }
    public CameraFocusControlStatus LastFocusControlStatus => _lastFocusControlStatus;
    public CameraExposureControlStatus LastExposureControlStatus => _lastExposureControlStatus;
    public void SetRecordingPreviewFpsCap(int fpsCap) => _openCvPreview.SetRecordingPreviewFpsCap(fpsCap);

    public event Action? StateChanged;
    public event Action<BitmapSource>? PreviewFrameBitmap;
    public event Action<CameraSlotPipeline, long>? WriterQueueDropDetected;

    public CameraSlotPipeline(int slotIndex)
    {
        SlotIndex = slotIndex;
        _openCvPreview.WriterQueueDropDetected += OnOpenCvWriterQueueDropDetected;
    }

    private void OnOpenCvWriterQueueDropDetected(long drops) =>
        WriterQueueDropDetected?.Invoke(this, drops);

    public OpenCvRecordingDiagnosticsSnapshot? GetOpenCvRecordingDiagnosticsSnapshot() =>
        _useOpenCvPreview ? _openCvPreview.GetRecordingDiagnosticsSnapshot() : null;

    public CameraFocusDiagnostics GetFocusDiagnosticsSnapshot(AppConfig config)
    {
        if (_useOpenCvPreview && _openCvPreview.IsOpened)
        {
            var openCvFocus = _openCvPreview.LastFocusControlStatus;
            return new CameraFocusDiagnostics
            {
                AutoFocusSupported = openCvFocus.AutoFocusApplyAttempted ? "attempted" : "unavailable",
                AutoFocusEnabled = InterpretAutoFocusReadback(openCvFocus.AutoFocusReadbackValue),
                ManualFocusSupported = FormatNullableBool(openCvFocus.ManualFocusSupported),
                ManualFocusValue = openCvFocus.ManualFocusReadbackValue
            };
        }

        try
        {
            var focusControl = _capture?.VideoDeviceController.FocusControl;
            var focusDeviceControl = _capture?.VideoDeviceController.Focus;
            if (focusControl == null && focusDeviceControl == null)
            {
                return new CameraFocusDiagnostics
                {
                    AutoFocusEnabled = config.AutoFocusEnabled ? "configured_true" : "configured_false"
                };
            }

            var autoSupported = focusControl?.Supported == true &&
                                focusControl.SupportedFocusModes.Contains(FocusMode.Auto);
            var manualSupported = focusControl?.Supported == true &&
                                  focusControl.SupportedFocusModes.Contains(FocusMode.Manual);
            var manualValue = "unavailable";
            if (focusDeviceControl?.Capabilities.Supported == true &&
                focusDeviceControl.TryGetValue(out var value))
            {
                manualValue = value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            }

            return new CameraFocusDiagnostics
            {
                AutoFocusSupported = autoSupported ? "true" : "false",
                AutoFocusEnabled = config.AutoFocusEnabled ? "configured_true" : "configured_false",
                ManualFocusSupported = manualSupported ? "true" : "false",
                ManualFocusValue = manualValue
            };
        }
        catch
        {
            return new CameraFocusDiagnostics
            {
                AutoFocusSupported = "unavailable",
                AutoFocusEnabled = config.AutoFocusEnabled ? "configured_true" : "configured_false",
                ManualFocusSupported = "unavailable",
                ManualFocusValue = "unavailable"
            };
        }
    }

    private static string FormatNullableBool(bool? value) => value.HasValue
        ? value.Value ? "true" : "false"
        : "unavailable";

    // Converts raw DirectShow/OpenCV autofocus property readback to a human-readable string.
    // A readback of 0 = off/confirmed; nonzero = still active (camera ignored the request).
    private static string InterpretAutoFocusReadback(string raw)
    {
        if (raw == "unavailable" || string.IsNullOrEmpty(raw)) return raw;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v < 0.5 ? "off_confirmed" : $"active (raw: {raw})";
        return raw;
    }

    public void SetPreviewSlotState(PreviewSlotStateKind state)
    {
        PreviewSlotState = state;
        if (state == PreviewSlotStateKind.LostConnection)
            Status = "Lost connection";
        StateChanged?.Invoke();
    }

    public void ResetPreviewSlotState() => SetPreviewSlotState(PreviewSlotStateKind.Idle);

    public async Task<bool> OpenAsync(
        string deviceId,
        AppConfig config,
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?>? layoutSelectedDeviceIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            LastError = "No device selected";
            Status = "Idle";
            return false;
        }

        if (SlotDeviceOwnership.IsOwnedByOtherSlot(deviceId, SlotIndex))
        {
            var other = SlotDeviceOwnership.GetOwnerSlot(deviceId);
            LastError = other.HasValue
                ? $"Device already open on cam{other.Value + 1}"
                : "Device already open on another slot";
            Status = "Error";
            _log.Info("camera", $"{SlotName} blocked: {LastError}");
            StateChanged?.Invoke();
            return false;
        }

        await CloseAsync(clearSessionHint: false);
        _layoutSelectedDeviceIds = layoutSelectedDeviceIds;
        _deviceId = deviceId;
        _useOpenCvPreview = !string.Equals(config.PreviewEngine, "winrt", StringComparison.OrdinalIgnoreCase);

        try
        {
            _deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId);
            var device = devices.FirstOrDefault(d => d.Id == deviceId);
            if (device != null) _deviceKind = device.Kind;

            bool ok;
            if (_useOpenCvPreview)
                ok = await OpenForOpenCvPreviewAsync(config, devices, cancellationToken).ConfigureAwait(false);
            else
                ok = await OpenWinRtAsync(deviceId, config).ConfigureAwait(false);

            if (!ok)
                return false;

            if (!SlotDeviceOwnership.TryAssign(deviceId, SlotIndex))
            {
                await CloseAsync(clearSessionHint: false);
                LastError = "Device already open on another camera slot";
                Status = "Error";
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            AbortPendingOpen();
            LastError = "open cancelled";
            Status = "Idle";
            StateChanged?.Invoke();
            throw;
        }
        catch (Exception ex)
        {
            CameraAccessDenied = CameraAccessHelper.IsAccessDenied(ex);
            LastError = CameraAccessDenied ? "Camera access denied" : ex.Message;
            Status = "Error";
            _log.Error("camera", $"{SlotName} open failed", ex);
            StateChanged?.Invoke();
            return false;
        }
    }

    private bool ShouldFallbackToWinRt(AppConfig config, IReadOnlyList<CameraDevice> devices, int width, int height)
    {
        if (string.IsNullOrEmpty(_deviceId)) return false;
        var device = devices.FirstOrDefault(d => d.Id == _deviceId);
        if (device == null) return false;

        var selectedIds = _layoutSelectedDeviceIds ?? Array.Empty<string?>();
        return DuplicateUsbCapturePolicy.ShouldFallbackToWinRtAfterOpenCvProbe(
            devices,
            selectedIds,
            device,
            _openCvIndex,
            width,
            height,
            config.PreferredCaptureWidth);
    }

    public void AbortPendingOpen() => _openCvPreview.AbortPendingOpen();

    private void WriteMappingDebug(
        string phase,
        CameraDevice? selectedDevice,
        OpenCvDeviceBinding binding,
        OpenCvDeviceSession.BindingConflict conflict)
    {
        var now = DateTime.Now;
        var file = $"device_mapping_debug_{now:yyyyMMdd_HHmmss_fff}.txt";
        var lines = new List<string>
        {
            $"timestamp={now:O}",
            $"CAMERA_SELECTED: slot={SlotName} display=\"{selectedDevice?.DisplayName ?? _deviceInfo?.Name ?? "?"}\" selectedId={_deviceId ?? "-"}",
            $"CAMERA_RESOLVED: slot={SlotName} dshowIndex={binding.Index} dshowName=\"{binding.DirectShowName ?? "-"}\" dshowPath=\"{binding.DirectShowOpenUri ?? "-"}\"",
            $"CAMERA_OWNERSHIP: slot={SlotName} claimedIndex={binding.Index} claimedName=\"{binding.DirectShowName ?? "-"}\" claimedUri=\"{binding.DirectShowOpenUri ?? "-"}\" conflict={(conflict.Conflict ? "yes" : "no")} conflictWithSlot={(conflict.OwnerSlot.HasValue ? $"cam{conflict.OwnerSlot.Value + 1}" : "-")} conflictKey={conflict.KeyType}:{conflict.KeyValue}",
            $"CAMERA_OPEN_TARGET: slot={SlotName} finalIndex={binding.Index}"
        };

        if (_layoutSelectedDeviceIds != null && _layoutSelectedDeviceIds.Count > 0)
        {
            var sb = new StringBuilder();
            sb.Append("selectedLayoutIds=");
            for (var i = 0; i < _layoutSelectedDeviceIds.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append($"cam{i + 1}:{_layoutSelectedDeviceIds[i] ?? "<none>"}");
            }
            lines.Add(sb.ToString());
        }

        var snapshot = OpenCvDirectShowIndexCatalog.GetSnapshot();
        if (snapshot.Count == 0)
        {
            lines.Add("catalogSnapshot=<empty>");
        }
        else
        {
            foreach (var s in snapshot.OrderBy(x => x.Index))
                lines.Add($"catalogSnapshot index={s.Index} name=\"{s.Name}\" uri=\"{s.Uri}\" pathMatched={s.PathMatched}");
        }

        DeviceMappingDebugLogger.AppendMappingLines(file, lines);
        _log.Info("camera",
            $"{phase} {SlotName} selected={selectedDevice?.DisplayName ?? _deviceInfo?.Name ?? "?"} id={_deviceId} -> index={binding.Index} uri={binding.DirectShowOpenUri ?? "-"} conflict={conflict.Conflict}");
    }

    private async Task<bool> OpenForOpenCvPreviewAsync(
        AppConfig config,
        IReadOnlyList<CameraDevice> devices,
        CancellationToken cancellationToken = default)
    {
        if (_openCvBinding != null)
            OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);

        var selected = devices.FirstOrDefault(d => d.Id == _deviceId);
        var binding = OpenCvDeviceMapper.Resolve(_deviceId!, devices);
        if (!binding.HasCaptureTarget)
        {
            if (selected?.Kind == CameraKind.ExternalUsb)
            {
                _log.Info("camera",
                    $"{SlotName} has no verified OpenCV target; trying exact-device WinRT preview fallback for {selected.DisplayName}");
                _useOpenCvPreview = false;
                return await OpenWinRtAsync(_deviceId!, config, exclusiveCapture: true).ConfigureAwait(false);
            }

            LastError = "Could not map this camera to a DirectShow device. Refresh cameras and try again.";
            _log.Error("camera", $"{SlotName} OpenCV map failed for device {_deviceId}");
            return false;
        }

        var ownershipConflict = OpenCvDeviceSession.DetectConflict(binding, SlotIndex);
        WriteMappingDebug("initial-resolve", selected, binding, ownershipConflict);

        _openCvBinding = binding;
        _openCvIndex = binding.Index;
        _openCvPreview.SetCapturePreferences(
            config.PreferredCaptureWidth,
            config.PreferredCaptureHeight,
            config.PreferFps);
        _openCvPreview.SetFastPreviewOpen(true);
        _openCvPreview.Attach(binding, config.MaxPreviewFpsUi);
        var (ok, w, h, fps) = await _openCvPreview.OpenAndProbeAsync(cancellationToken).ConfigureAwait(false);
        _openCvPreview.SetFastPreviewOpen(false);
        if (!ok)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var retry = 1; retry <= 2 && !ok; retry++)
            {
                var delayMs = retry == 1 ? 1500 : 2500;
                _log.Info("camera",
                    $"{SlotName} OpenCV open failed for {selected?.DisplayName ?? _deviceInfo?.Name}; retry {retry}/2 same binding after {delayMs}ms before remap");

                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
                _openCvPreview.Attach(binding, config.MaxPreviewFpsUi);
                _openCvPreview.SetFastPreviewOpen(true);
                (ok, w, h, fps) = await _openCvPreview.OpenAndProbeAsync(cancellationToken).ConfigureAwait(false);
                _openCvPreview.SetFastPreviewOpen(false);
            }

            if (ok)
                goto OPEN_OK;

            // Invalidate only this device's stale binding and refresh just this selected mapping.
            var refreshed = OpenCvDeviceMapper.ResolveAfterInvalidatingStale(_deviceId!, devices, _layoutSelectedDeviceIds);
            if (refreshed.HasCaptureTarget)
            {
                OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
                _openCvBinding = refreshed;
                _openCvIndex = refreshed.Index;
                var refreshedConflict = OpenCvDeviceSession.DetectConflict(refreshed, SlotIndex);
                WriteMappingDebug("refresh-single-device", selected, refreshed, refreshedConflict);
                _openCvPreview.Attach(refreshed, config.MaxPreviewFpsUi);
                _openCvPreview.SetFastPreviewOpen(true);
                (ok, w, h, fps) = await _openCvPreview.OpenAndProbeAsync(cancellationToken).ConfigureAwait(false);
                _openCvPreview.SetFastPreviewOpen(false);
            }

            if (ok)
                goto OPEN_OK;

            var alternate = OpenCvDeviceMapper.TryResolveAlternate(
                _deviceId!, devices, binding.Index, _layoutSelectedDeviceIds);
            if (alternate is { } altBinding)
            {
                OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
                _openCvBinding = altBinding;
                _openCvIndex = altBinding.Index;
                var altConflict = OpenCvDeviceSession.DetectConflict(altBinding, SlotIndex);
                WriteMappingDebug("alternate-index", selected, altBinding, altConflict);
                _openCvPreview.Attach(altBinding, config.MaxPreviewFpsUi);
                _openCvPreview.SetFastPreviewOpen(true);
                (ok, w, h, fps) = await _openCvPreview.OpenAndProbeAsync(cancellationToken).ConfigureAwait(false);
                _openCvPreview.SetFastPreviewOpen(false);
            }
        }

OPEN_OK:
        if (!ok)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
            _openCvBinding = null;
            if (selected?.Kind == CameraKind.ExternalUsb)
            {
                _log.Info("camera",
                    $"{SlotName} OpenCV open failed after mapping retries; trying exact-device WinRT preview fallback for {selected.DisplayName}");
                if (string.IsNullOrEmpty(_deviceId))
                {
                    LastError = "OpenCV could not open this USB camera";
                    return false;
                }

                _useOpenCvPreview = false;
                var winRtOk = await OpenWinRtAsync(_deviceId, config, exclusiveCapture: true).ConfigureAwait(false);
                if (!winRtOk)
                    LastError = "OpenCV and WinRT could not open this USB camera";
                return winRtOk;
            }

            _log.Info("camera", $"{SlotName} OpenCV open failed; trying WinRT for {_deviceInfo?.Name}");
            if (string.IsNullOrEmpty(_deviceId))
                return false;
            _useOpenCvPreview = false;
            return await OpenWinRtAsync(_deviceId, config, exclusiveCapture: true).ConfigureAwait(false);
        }

        if (ShouldFallbackToWinRt(config, devices, w, h))
        {
            await _openCvPreview.StopAsync().ConfigureAwait(false);
            _openCvPreview.ReleaseCamera();
            OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
            _openCvBinding = null;
            _log.Info("camera",
                $"{SlotName} OpenCV index {_openCvIndex} at {w}x{h} — switching to WinRT ({_deviceInfo?.Name})");
            _useOpenCvPreview = false;
            return await OpenWinRtAsync(_deviceId!, config, exclusiveCapture: true).ConfigureAwait(false);
        }

        if (_openCvBinding is not { } claimed)
            return false;

        OpenCvDeviceSession.Claim(SlotIndex, claimed);
        OpenCvDeviceSession.RememberDevice(_deviceId!, claimed);

        RecordWidth = w;
        RecordHeight = h;
        RequestedFps = config.PreferFps > 0 ? config.PreferFps : fps;
        ObservedCaptureFps = fps;
        RecordingWriterFps = fps;
        CaptureResolutionMatched = _openCvPreview.LastResolutionMatched;
        ResolutionText = FormatResolution(w, h, fps);
        SelectedMode = new CameraMode
        {
            Width = w,
            Height = h,
            Fps = fps,
            RequestedFps = RequestedFps,
            SelectedDeviceFps = fps,
            SelectionReason = "opencv_probe"
        };
        Status = "Ready";
        if (!CaptureResolutionMatched && config.PreferredCaptureWidth > 0 && config.PreferredCaptureHeight > 0)
        {
            LastError =
                $"Using {CaptureResolutionPreset.ToLabel(w, h)} — could not apply {CaptureResolutionPreset.ToLabel(config.PreferredCaptureWidth, config.PreferredCaptureHeight)}.";
        }
        else
            LastError = null;
        CameraAccessDenied = false;
        _winRtOpenedForRecord = false;

        _lastFocusControlStatus = await TryApplyFocusModeBestEffortAsync(config).ConfigureAwait(false);

        StateChanged?.Invoke();
        _log.Info("camera", $"{SlotName} opened (OpenCV index {_openCvIndex}): {_deviceInfo?.Name}");
        return true;
    }

    private async Task<bool> OpenWinRtAsync(string deviceId, AppConfig config, bool exclusiveCapture = false)
    {
        await EnsureWinRtCaptureAsync(deviceId, config, exclusiveCapture);
        _winRtPreview.Attach(_capture!, config.MaxPreviewFpsUi);
        SelectedMode = _modeSelector.SelectBest(_capture!, config);
        try
        {
            await _modeSelector.ApplyRecordModeAsync(_capture!, SelectedMode);
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} ApplyRecordMode skipped: {ex.Message}");
        }

        UpdateResolutionFromMode();
        Status = "Ready";
        LastError = null;
        CameraAccessDenied = false;
        _winRtOpenedForRecord = true;
        _lastFocusControlStatus = await ApplyFocusModeAsync(config).ConfigureAwait(false);
        StateChanged?.Invoke();
        _log.Info("camera", $"{SlotName} opened (WinRT): {_deviceInfo?.Name}");
        return true;
    }

    private static string FormatResolution(int w, int h, double fps) =>
        CaptureResolutionPreset.FormatWithFps(w, h, fps);

    private async Task EnsureWinRtCaptureAsync(string deviceId, AppConfig config, bool exclusiveCapture = false)
    {
        if (_capture != null) return;

        var init = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            VideoDeviceId = deviceId,
            SharingMode = exclusiveCapture
                ? MediaCaptureSharingMode.ExclusiveControl
                : MediaCaptureSharingMode.SharedReadOnly,
            MediaCategory = MediaCategory.Media,
            MemoryPreference = config.EnableHardwareAcceleration
                ? MediaCaptureMemoryPreference.Auto
                : MediaCaptureMemoryPreference.Cpu
        };

        _capture = new MediaCapture();
        await _capture.InitializeAsync(init);

        try
        {
            var props = _capture.VideoDeviceController
                .GetMediaStreamProperties(MediaStreamType.VideoRecord) as VideoEncodingProperties;
            if (props != null)
            {
                await _capture.VideoDeviceController.SetMediaStreamPropertiesAsync(
                    MediaStreamType.VideoRecord, props);
            }
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} SetMediaStreamProperties skipped: {ex.Message}");
        }

        _lastFocusControlStatus = await ApplyFocusModeAsync(config);
    }

    private void OnOpenCvFrame(BitmapSource source)
    {
        var w = _openCvPreview.LiveWidth;
        var h = _openCvPreview.LiveHeight;
        var fps = _openCvPreview.LiveFps;
        var changed = false;
        if (w > 0 && h > 0 && (w != RecordWidth || h != RecordHeight))
        {
            RecordWidth = w;
            RecordHeight = h;
            changed = true;
        }

        if (fps > 0 && (ObservedCaptureFps <= 0 || Math.Abs(fps - ObservedCaptureFps) > 0.5))
        {
            ObservedCaptureFps = fps;
            if (SelectedMode != null)
                SelectedMode = SelectedMode with { Fps = fps, SelectedDeviceFps = fps };
            changed = true;
        }

        if (changed)
        {
            ResolutionText = FormatResolution(
                RecordWidth,
                RecordHeight,
                ObservedCaptureFps > 0 ? ObservedCaptureFps : RequestedFps);
            StateChanged?.Invoke();
        }

        DeliverPreviewFrame(source);
    }

    private async void OnOpenCvStreamLost()
    {
        try
        {
            if (Status is not ("Previewing" or "Recording" or "Ready"))
                return;

            _log.Warn("camera", $"{SlotName} lost frame stream");
            AppDiagnosticLogger.Runtime($"{SlotName} device lost frame stream");
            WasPreviewingBeforeDisconnect = Status is "Previewing" or "Recording";
            LastError = "Lost connection";
            SetPreviewSlotState(PreviewSlotStateKind.LostConnection);
            try
            {
                await StopPreviewAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("camera", $"{SlotName} stop after stream lost", ex);
            }

            _openCvPreview.ReleaseCamera();
            if (_openCvBinding != null)
            {
                OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
                _openCvBinding = null;
            }

            Status = "Lost connection";
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error("camera", $"{SlotName} stream lost handler failed", ex);
            AppDiagnosticLogger.Failure("preview", $"{SlotName} stream lost handler", ex);
        }
    }

    private async Task ApplyFocusModeInBackgroundAsync(AppConfig config)
    {
        try
        {
            await Task.Delay(400).ConfigureAwait(false);
            _lastFocusControlStatus = await TryApplyFocusModeBestEffortAsync(config).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} deferred focus skipped: {ex.Message}");
        }
    }

    public async Task<CameraFocusControlStatus> ApplyFocusSettingsAsync(AppConfig config, string reason = "user")
    {
        var status = await TryApplyFocusModeBestEffortAsync(config).ConfigureAwait(false);
        _lastFocusControlStatus = status;
        StateChanged?.Invoke();
        return status;
    }

    public async Task<CameraExposureControlStatus> ApplyExposureSettingsAsync(AppConfig config, string reason = "user")
    {
        var status = await TryApplyExposureModeBestEffortAsync(config).ConfigureAwait(false);
        _lastExposureControlStatus = status;
        StateChanged?.Invoke();
        return status;
    }

    private static CameraExposureControlStatus PreserveExposureRequestState(
        CameraExposureControlStatus? current,
        AppConfig config)
    {
        var existing = current ?? CameraExposureControlStatus.NotAttempted(
            config.AutoExposureEnabled,
            config.DisableLowLightCompensation);
        var llcRequestChanged = existing.LowLightCompensationOffRequested != config.DisableLowLightCompensation;

        return existing with
        {
            AutoExposureRequested = config.AutoExposureEnabled,
            ManualExposureRequestedValue = config.ManualExposureValue,
            LowLightCompensationOffRequested = config.DisableLowLightCompensation,
            LowLightCompensationOffConfirmed = config.DisableLowLightCompensation
                ? llcRequestChanged ? null : existing.LowLightCompensationOffConfirmed
                : null
        };
    }

    private async Task<CameraExposureControlStatus> TryApplyExposureModeBestEffortAsync(AppConfig config)
    {
        if (_useOpenCvPreview && _openCvPreview.IsOpened)
            return _openCvPreview.ApplyExposureSettings(config.AutoExposureEnabled, config.ManualExposureValue, config.DisableLowLightCompensation, "opencv");

        if (_capture != null)
            return await ApplyExposureModeAsync(_capture, config).ConfigureAwait(false);

        if (string.IsNullOrEmpty(_deviceId))
            return CameraExposureControlStatus.NotAttempted(config.AutoExposureEnabled, config.DisableLowLightCompensation);

        MediaCapture? tempCapture = null;
        try
        {
            tempCapture = new MediaCapture();
            var init = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = _deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly
            };
            await tempCapture.InitializeAsync(init);
            return await ApplyExposureModeAsync(tempCapture, config).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} best-effort exposure control skipped: {ex.Message}");
            return CameraExposureControlStatus.NotAttempted(config.AutoExposureEnabled, config.DisableLowLightCompensation) with
            {
                AutoExposureApplyAttempted = true,
                AutoExposureApplySucceeded = false,
                ExposureWarning = "Exposure control was not confirmed. Use camera/vendor settings if blur or brightness changes are visible."
            };
        }
        finally
        {
            tempCapture?.Dispose();
        }
    }

    private async Task<CameraExposureControlStatus> ApplyExposureModeAsync(MediaCapture capture, AppConfig config)
    {
        try
        {
            var controller = capture.VideoDeviceController;
            var exposureControl = controller.ExposureControl;
            var exposureDeviceControl = controller.Exposure;
            var manualReadback = "unavailable";
            if (exposureDeviceControl?.Capabilities.Supported == true && exposureDeviceControl.TryGetValue(out var beforeValue))
                manualReadback = beforeValue.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            if (exposureControl == null || !exposureControl.Supported)
            {
                return new CameraExposureControlStatus
                {
                    AutoExposureRequested = config.AutoExposureEnabled,
                    AutoExposureApplyAttempted = true,
                    AutoExposureApplySucceeded = null,
                    AutoExposureReadbackValue = "unavailable",
                    ManualExposureSupported = exposureDeviceControl?.Capabilities.Supported == true,
                    ManualExposureRequestedValue = config.ManualExposureValue,
                    ManualExposureReadbackValue = manualReadback,
                    LowLightCompensationOffRequested = config.DisableLowLightCompensation,
                    ExposureControlMode = "unavailable",
                    ExposureWarning = "Exposure control was not confirmed. Use camera/vendor settings if blur or brightness changes are visible."
                };
            }

            // Applies regardless of auto/manual exposure mode below — LLC disable was previously
            // nested only inside the manual-exposure branch, so checking "Disable Low-Light
            // Compensation" while Auto Exposure was also checked silently did nothing, even
            // though the returned status still claimed it was requested.
            bool? llcOffConfirmed = null;
            if (config.DisableLowLightCompensation)
            {
                var backlightControl = controller.BacklightCompensation;
                if (backlightControl?.Capabilities.Supported == true)
                {
                    backlightControl.TrySetValue(0);
                    llcOffConfirmed = backlightControl.TryGetValue(out var llcVal) ? llcVal < 0.5 : null;
                }
            }

            if (config.AutoExposureEnabled)
            {
                await Task.WhenAny(exposureControl.SetAutoAsync(true).AsTask(), Task.Delay(500)).ConfigureAwait(false);
                return new CameraExposureControlStatus
                {
                    AutoExposureRequested = true,
                    AutoExposureApplyAttempted = true,
                    AutoExposureApplySucceeded = true,
                    AutoExposureReadbackValue = "auto_requested",
                    ManualExposureSupported = true,
                    ManualExposureRequestedValue = config.ManualExposureValue,
                    ManualExposureReadbackValue = manualReadback,
                    LowLightCompensationOffRequested = config.DisableLowLightCompensation,
                    LowLightCompensationOffConfirmed = llcOffConfirmed,
                    ExposureControlMode = "auto_exposure"
                };
            }
            else
            {
                await Task.WhenAny(exposureControl.SetAutoAsync(false).AsTask(), Task.Delay(500)).ConfigureAwait(false);
                if (config.ManualExposureValue.HasValue && exposureDeviceControl?.Capabilities.Supported == true)
                {
                    var minTicks = exposureControl.Min.Ticks;
                    var maxTicks = exposureControl.Max.Ticks;
                    var mappedTicks = (long)(minTicks + (config.ManualExposureValue.Value / 255.0) * (maxTicks - minTicks));
                    var clamped = TimeSpan.FromTicks(Math.Clamp(mappedTicks, minTicks, maxTicks));
                    await Task.WhenAny(exposureControl.SetValueAsync(clamped).AsTask(), Task.Delay(500)).ConfigureAwait(false);
                    if (exposureDeviceControl.TryGetValue(out var afterValue))
                        manualReadback = afterValue.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                }

                return new CameraExposureControlStatus
                {
                    AutoExposureRequested = false,
                    AutoExposureApplyAttempted = true,
                    AutoExposureApplySucceeded = true,
                    AutoExposureReadbackValue = "manual_mode_requested",
                    ManualExposureSupported = true,
                    ManualExposureRequestedValue = config.ManualExposureValue,
                    ManualExposureReadbackValue = manualReadback,
                    LowLightCompensationOffRequested = config.DisableLowLightCompensation,
                    LowLightCompensationOffConfirmed = llcOffConfirmed,
                    ExposureControlMode = config.ManualExposureValue.HasValue ? "manual_exposure" : "auto_exposure_off_best_effort"
                };
            }
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} exposure control skipped: {ex.Message}");
            return CameraExposureControlStatus.NotAttempted(config.AutoExposureEnabled, config.DisableLowLightCompensation) with
            {
                AutoExposureApplyAttempted = true,
                AutoExposureApplySucceeded = false,
                ExposureWarning = "Exposure control was not confirmed. Use camera/vendor settings if blur or brightness changes are visible."
            };
        }
    }

    private static async Task<T> WithCameraControlTimeoutAsync<T>(Task<T> task, int timeoutMs, T fallback)
    {
        if (await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false) != task)
            return fallback;
        try { return await task.ConfigureAwait(false); }
        catch { return fallback; }
    }

    private async Task<CameraFocusControlStatus> TryApplyFocusModeBestEffortAsync(AppConfig config)
    {
        // If we already have a WinRT capture object, use it directly.
        if (_capture != null)
        {
            return await ApplyFocusModeAsync(config);
        }

        if (_useOpenCvPreview && _openCvPreview.IsOpened)
            return _openCvPreview.ApplyFocusSettings(config.AutoFocusEnabled, config.ManualFocusValue, "opencv");

        // For OpenCV cameras, we briefly open a WinRT MediaCapture to set focus mode.
        if (string.IsNullOrEmpty(_deviceId))
            return CameraFocusControlStatus.NotAttempted(config.AutoFocusEnabled);

        MediaCapture? tempCapture = null;
        try
        {
            tempCapture = new MediaCapture();
            var init = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = _deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly
            };
            await tempCapture.InitializeAsync(init);

            return await ApplyFocusModeAsync(tempCapture, config).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} best-effort focus control skipped: {ex.Message}");
            return CameraFocusControlStatus.NotAttempted(config.AutoFocusEnabled) with
            {
                AutoFocusApplyAttempted = true,
                AutoFocusApplySucceeded = false,
                FocusWarning = "Focus warning: autofocus OFF was requested but not confirmed. Use camera/vendor controls if focus hunting is visible."
            };
        }
        finally
        {
            tempCapture?.Dispose();
        }
    }

    private async Task<CameraFocusControlStatus> ApplyFocusModeAsync(AppConfig config)
    {
        if (_capture == null) return CameraFocusControlStatus.NotAttempted(config.AutoFocusEnabled);
        return await ApplyFocusModeAsync(_capture, config).ConfigureAwait(false);
    }

    private async Task<CameraFocusControlStatus> ApplyFocusModeAsync(MediaCapture capture, AppConfig config)
    {
        try
        {
            var controller = capture.VideoDeviceController;
            var focusControl = controller.FocusControl;
            var focusDeviceControl = controller.Focus;
            var manualReadback = "unavailable";
            if (focusDeviceControl?.Capabilities.Supported == true && focusDeviceControl.TryGetValue(out var beforeValue))
                manualReadback = beforeValue.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (focusControl == null || !focusControl.Supported)
            {
                return new CameraFocusControlStatus
                {
                    AutoFocusRequested = config.AutoFocusEnabled,
                    AutoFocusApplyAttempted = true,
                    AutoFocusApplySucceeded = null,
                    AutoFocusReadbackValue = "unavailable",
                    ManualFocusSupported = focusDeviceControl?.Capabilities.Supported == true,
                    ManualFocusRequestedValue = config.ManualFocusValue,
                    ManualFocusReadbackValue = manualReadback,
                    FocusControlMode = "unavailable",
                    FocusWarning = config.AutoFocusEnabled
                        ? ""
                        : "Focus warning: autofocus OFF was requested but not confirmed. Use camera/vendor controls if focus hunting is visible."
                };
            }

            if (config.AutoFocusEnabled)
            {
                if (focusControl.SupportedFocusModes.Contains(FocusMode.Auto))
                {
                    var settings = new FocusSettings
                    {
                        Mode = FocusMode.Auto,
                        AutoFocusRange = AutoFocusRange.FullRange,
                        DisableDriverFallback = false
                    };
                    focusControl.Configure(settings);
                    await Task.WhenAny(focusControl.FocusAsync().AsTask(), Task.Delay(500)).ConfigureAwait(false);
                    return new CameraFocusControlStatus
                    {
                        AutoFocusRequested = true,
                        AutoFocusApplyAttempted = true,
                        AutoFocusApplySucceeded = true,
                        AutoFocusReadbackValue = "auto_requested",
                        ManualFocusSupported = focusControl.SupportedFocusModes.Contains(FocusMode.Manual),
                        ManualFocusRequestedValue = config.ManualFocusValue,
                        ManualFocusReadbackValue = manualReadback,
                        FocusControlMode = "autofocus"
                    };
                }
            }
            else
            {
                if (focusControl.SupportedFocusModes.Contains(FocusMode.Manual))
                {
                    var settings = new FocusSettings
                    {
                        Mode = FocusMode.Manual,
                        DisableDriverFallback = false
                    };
                    focusControl.Configure(settings);
                    if (config.ManualFocusValue.HasValue && focusDeviceControl?.Capabilities.Supported == true)
                    {
                        var value = Math.Clamp(config.ManualFocusValue.Value, focusDeviceControl.Capabilities.Min, focusDeviceControl.Capabilities.Max);
                        focusDeviceControl.TrySetValue(value);
                        if (focusDeviceControl.TryGetValue(out var afterValue))
                            manualReadback = afterValue.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    }

                    return new CameraFocusControlStatus
                    {
                        AutoFocusRequested = false,
                        AutoFocusApplyAttempted = true,
                        AutoFocusApplySucceeded = true,
                        AutoFocusReadbackValue = "manual_mode_requested",
                        ManualFocusSupported = true,
                        ManualFocusRequestedValue = config.ManualFocusValue,
                        ManualFocusReadbackValue = manualReadback,
                        FocusControlMode = config.ManualFocusValue.HasValue ? "manual" : "autofocus_off_best_effort"
                    };
                }
            }

            return new CameraFocusControlStatus
            {
                AutoFocusRequested = config.AutoFocusEnabled,
                AutoFocusApplyAttempted = true,
                AutoFocusApplySucceeded = false,
                AutoFocusReadbackValue = "not_confirmed",
                ManualFocusSupported = focusControl.SupportedFocusModes.Contains(FocusMode.Manual),
                ManualFocusRequestedValue = config.ManualFocusValue,
                ManualFocusReadbackValue = manualReadback,
                FocusControlMode = "not_supported",
                FocusWarning = config.AutoFocusEnabled
                    ? ""
                    : "Focus warning: autofocus OFF was requested but not confirmed. Use camera/vendor controls if focus hunting is visible."
            };
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} focus control skipped: {ex.Message}");
            return CameraFocusControlStatus.NotAttempted(config.AutoFocusEnabled) with
            {
                AutoFocusApplyAttempted = true,
                AutoFocusApplySucceeded = false,
                FocusWarning = config.AutoFocusEnabled
                    ? ""
                    : "Focus warning: autofocus OFF was requested but not confirmed. Use camera/vendor controls if focus hunting is visible."
            };
        }
    }

    public async Task<bool> ReapplyCaptureSettingsAsync(AppConfig config)
    {
        if (Status is not ("Previewing" or "Ready"))
            return false;

        try
        {
            if (!_useOpenCvPreview || !_openCvPreview.IsOpened)
            {
                if (_capture == null)
                {
                    if (string.IsNullOrEmpty(_deviceId)) return false;
                    await EnsureWinRtCaptureAsync(_deviceId, config);
                }

                if (_capture == null) return false;

                SelectedMode = _modeSelector.SelectBest(_capture, config);
                await _modeSelector.ApplyRecordModeAsync(_capture, SelectedMode);
                RequestedFps = SelectedMode.RequestedFps > 0 ? SelectedMode.RequestedFps : RequestedFps;
                ObservedCaptureFps = SelectedMode.SelectedDeviceFps > 0 ? SelectedMode.SelectedDeviceFps : SelectedMode.Fps;
                RecordingWriterFps = ObservedCaptureFps;
                UpdateResolutionFromMode();
                _lastFocusControlStatus = await ApplyFocusModeAsync(config).ConfigureAwait(false);

                if (Status == "Previewing")
                {
                    await _winRtPreview.StopAsync();
                    _winRtPreview.Attach(_capture, config.MaxPreviewFpsUi);
                    await _winRtPreview.StartAsync();
                }

                StateChanged?.Invoke();
                _log.Info("camera", $"{SlotName} WinRT settings reapplied: {ResolutionText}");
                return true;
            }

            _openCvPreview.SetCapturePreferences(
                config.PreferredCaptureWidth,
                config.PreferredCaptureHeight,
                config.PreferFps);
            var (ok, w, h, fps) = await _openCvPreview.ReapplyCaptureSettingsAsync();
            if (!ok) return false;

            RecordWidth = w;
            RecordHeight = h;
            RequestedFps = config.PreferFps > 0 ? config.PreferFps : fps;
            ObservedCaptureFps = fps;
            RecordingWriterFps = fps;
            CaptureResolutionMatched = _openCvPreview.LastResolutionMatched;
            SelectedMode = new CameraMode
            {
                Width = w,
                Height = h,
                Fps = fps,
                RequestedFps = RequestedFps,
                SelectedDeviceFps = fps,
                SelectionReason = "opencv_reapplied"
            };
            ResolutionText = FormatResolution(w, h, fps);
            _lastFocusControlStatus = await TryApplyFocusModeBestEffortAsync(config).ConfigureAwait(false);
            if (!CaptureResolutionMatched && config.PreferredCaptureWidth > 0 && config.PreferredCaptureHeight > 0)
            {
                LastError =
                    $"Using {CaptureResolutionPreset.ToLabel(w, h)} — could not apply {CaptureResolutionPreset.ToLabel(config.PreferredCaptureWidth, config.PreferredCaptureHeight)} (try MJPEG/USB port for 1080p on cam4).";
                _log.Info("camera", $"{SlotName} {LastError}");
            }
            else
                LastError = null;

            StateChanged?.Invoke();
            _log.Info("camera", $"{SlotName} OpenCV settings reapplied: {ResolutionText}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("camera", $"{SlotName} reapply capture settings failed", ex);
            LastError = ex.Message;
            StateChanged?.Invoke();
            return false;
        }
    }

    public async Task PrepareForPreflightAsync(AppConfig config, double targetFps)
    {
        await ReapplyCaptureSettingsAsync(config).ConfigureAwait(false);
        var cap = (int)Math.Clamp(Math.Ceiling(targetFps), 5, 60);
        if (PreviewUsesWinRt)
            _winRtPreview.SetPreviewFpsCap(cap);
        else
            _openCvPreview.SetPreviewFpsCap(cap);
    }

    public void RestoreAfterPreflight(AppConfig config)
    {
        var uiCap = Math.Max(5, config.MaxPreviewFpsUi);
        if (PreviewUsesWinRt)
            _winRtPreview.SetPreviewFpsCap(uiCap);
        else
            _openCvPreview.SetPreviewFpsCap(uiCap);
    }

    private void OnWinRtFrame(Windows.Graphics.Imaging.SoftwareBitmap bmp)
    {
        try
        {
            var source = PreviewImageHelper.CreateBitmapSource(bmp);
            if (source != null)
                DeliverPreviewFrame(source);
        }
        finally
        {
            bmp.Dispose();
        }
    }

    private void DeliverPreviewFrame(BitmapSource source)
    {
        _fpsMonitor.NotifyFrame();
        PreviewFrameBitmap?.Invoke(source);
    }

    private bool PreviewUsesWinRt => _winRtOpenedForRecord || !_useOpenCvPreview;

    public void BeginExperimentRecording(ExperimentSessionOptions session) => _experimentSession = session;

    public void EndExperimentRecording() => _experimentSession = null;

    public bool UsesOpenCvRecording(AppConfig config) =>
        _useOpenCvPreview && (
            string.Equals(config.RecordingEngine, "opencv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.PreviewEngine, "opencv", StringComparison.OrdinalIgnoreCase));

    public bool CanRecord(AppConfig config) =>
        SelectedMode != null && (
            UsesOpenCvRecording(config)
                ? _openCvPreview.IsOpened && Status is "Previewing" or "Recording"
                : _capture != null && Status is "Previewing" or "Recording");

    public async Task StopPreviewForRecordingHandoffAsync()
    {
        await StopPreviewAsync();
        _openCvPreview.ReleaseCamera();
        if (_openCvBinding != null)
        {
            OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
            _openCvBinding = null;
        }
    }

    public async Task PrepareWinRtForRecordingAsync(AppConfig config)
    {
        if (string.IsNullOrEmpty(_deviceId))
            throw new InvalidOperationException($"{SlotName} has no device");

        await EnsureWinRtCaptureAsync(_deviceId, config);
        SelectedMode = _modeSelector.SelectBest(_capture!, config);
        await _modeSelector.ApplyRecordModeAsync(_capture!, SelectedMode);
        _lastFocusControlStatus = await ApplyFocusModeAsync(config).ConfigureAwait(false);
        RequestedFps = SelectedMode.RequestedFps > 0 ? SelectedMode.RequestedFps : RequestedFps;
        ObservedCaptureFps = SelectedMode.SelectedDeviceFps > 0 ? SelectedMode.SelectedDeviceFps : SelectedMode.Fps;
        RecordingWriterFps = ObservedCaptureFps;
        UpdateResolutionFromMode();
        _winRtOpenedForRecord = true;
        await StartPreviewForRecordingAsync(config);
        _log.Info("camera", $"{SlotName} WinRT ready for recording (Windows Camera flow)");
    }

    public async Task StartPreviewAsync()
    {
        if (PreviewUsesWinRt)
        {
            if (_capture == null) return;
            _winRtPreview.FrameArrived -= OnWinRtFrame;
            _winRtPreview.FrameArrived += OnWinRtFrame;
            await _winRtPreview.StartAsync();
        }
        else
        {
            _openCvPreview.FrameArrived -= OnOpenCvFrame;
            _openCvPreview.FrameArrived += OnOpenCvFrame;
            _openCvPreview.StreamLost -= OnOpenCvStreamLost;
            _openCvPreview.StreamLost += OnOpenCvStreamLost;
            await _openCvPreview.StartAsync();
        }

        _fpsMonitor.Start();
        if (Status != "Recording") Status = "Previewing";
        StateChanged?.Invoke();
    }

    public async Task StopPreviewAsync()
    {
        _openCvPreview.FrameArrived -= OnOpenCvFrame;
        AppDiagnosticLogger.Runtime($"SLOT_FRAME_CALLBACK_UNSUBSCRIBED cam{SlotIndex + 1}");
        _openCvPreview.StreamLost -= OnOpenCvStreamLost;
        AppDiagnosticLogger.Runtime($"SLOT_STREAM_LOST_UNSUBSCRIBED cam{SlotIndex + 1}");
        _winRtPreview.FrameArrived -= OnWinRtFrame;
        AppDiagnosticLogger.Runtime($"SLOT_EVENT_UNSUBSCRIBED cam{SlotIndex + 1} preview");
        _fpsMonitor.Stop();

        if (PreviewUsesWinRt)
            await _winRtPreview.StopAsync();
        else
            await _openCvPreview.StopAsync();
        AppDiagnosticLogger.Runtime($"SLOT_CAPTURE_LOOP_EXITED cam{SlotIndex + 1}");

        if (Status == "Previewing") Status = "Ready";
        StateChanged?.Invoke();
    }

    public async Task<bool> RecoverPreviewAfterFirstFrameTimeoutAsync(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(_deviceId))
            return false;

        try
        {
            await StopPreviewAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Info("camera", $"{SlotName} stop before first-frame recovery skipped: {ex.Message}");
        }

        if (!PreviewUsesWinRt)
        {
            _openCvPreview.ReleaseCamera();
            if (_openCvBinding != null)
            {
                OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
                _openCvBinding = null;
            }

            _useOpenCvPreview = false;
            _log.Info("camera",
                $"{SlotName} OpenCV opened but delivered no preview frame; retrying selected device through WinRT");
            var winRtOk = await OpenWinRtAsync(_deviceId, config, exclusiveCapture: true).ConfigureAwait(false);
            if (!winRtOk)
                return false;

            await StartPreviewAsync().ConfigureAwait(false);
            return true;
        }

        _log.Info("camera",
            $"{SlotName} WinRT opened but delivered no preview frame; retrying selected device through OpenCV");
        await CloseAsync(clearSessionHint: false).ConfigureAwait(false);
        _useOpenCvPreview = true;
        return false;
    }

    public async Task EnsureReadyForRecordingAsync(AppConfig config)
    {
        if (UsesOpenCvRecording(config))
        {
            if (SelectedMode == null)
                throw new InvalidOperationException($"{SlotName} has no video mode (open preview first)");
            if (Status != "Previewing")
                await StartPreviewAsync();
            return;
        }

        if (_winRtOpenedForRecord && _capture != null)
        {
            if (Status != "Recording" && Status != "Previewing")
                await StartPreviewForRecordingAsync(config);
            return;
        }

        await PrepareWinRtForRecordingAsync(config);
    }

    public async Task PrepareLowLagRecordingAsync(
        StorageFile file,
        Windows.Media.MediaProperties.MediaEncodingProfile profile,
        AppConfig config)
    {
        if (_capture == null || SelectedMode == null)
            throw new InvalidOperationException($"{SlotName} not ready for LowLag prepare");

        _recordingFile = file;
        _resumePreviewAfterRecord = Status == "Previewing";
        await _winRtPreview.StopAsync();

        try
        {
            var slotFocusConfig    = config.WithSlotFocusSettings(SlotIndex);
            var slotExposureConfig = config.WithSlotExposureSettings(SlotIndex);
            _lastExposureControlStatus = PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig);
            await _modeSelector.ApplyRecordModeAsync(_capture, SelectedMode);

            // Run focus and exposure apply in parallel with a 300 ms timeout each.
            // Sequential 1500 ms × 2 was the main source of pre-recording latency.
            // For cameras that do not support these controls the tasks complete in < 50 ms.
            var focusTask = WithCameraControlTimeoutAsync(
                ApplyFocusModeAsync(slotFocusConfig), 300,
                _lastFocusControlStatus ?? CameraFocusControlStatus.NotAttempted(slotFocusConfig.AutoFocusEnabled));
            var exposureTask = slotExposureConfig.ReapplyExposureBeforeRecording
                ? WithCameraControlTimeoutAsync(
                    TryApplyExposureModeBestEffortAsync(slotExposureConfig), 300,
                    PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig))
                : Task.FromResult(PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig));

            await Task.WhenAll(focusTask, exposureTask).ConfigureAwait(false);
            _lastFocusControlStatus    = focusTask.Result;
            _lastExposureControlStatus = exposureTask.Result;
        }
        catch (Exception ex)
        {
            _log.Info("recording", $"{SlotName} ApplyRecordMode skipped: {ex.Message}");
        }

        AttachRecordLimitHandler();
        _lowLagRecording = await _capture.PrepareLowLagRecordToStorageFileAsync(profile, file);
        _log.Info("recording", $"{SlotName} LowLag prepared -> {file.Name}");
    }

    public async Task BeginLowLagRecordingAsync()
    {
        if (_lowLagRecording == null)
            throw new InvalidOperationException($"{SlotName} LowLag not prepared");

        await _lowLagRecording.StartAsync();
        _fpsMonitor.Start();
        Status = "Recording";
        StateChanged?.Invoke();
        _log.Info("recording", $"{SlotName} LowLag started ({RecordWidth}x{RecordHeight} @ {RequestedFps:F0})");
    }

    private void UpdateResolutionFromMode()
    {
        if (SelectedMode == null) return;
        RecordWidth = SelectedMode.Width;
        RecordHeight = SelectedMode.Height;
        RequestedFps = SelectedMode.RequestedFps > 0 ? SelectedMode.RequestedFps : SelectedMode.Fps;
        ObservedCaptureFps = SelectedMode.SelectedDeviceFps > 0 ? SelectedMode.SelectedDeviceFps : SelectedMode.Fps;
        RecordingWriterFps = ObservedCaptureFps > 0 ? ObservedCaptureFps : RequestedFps;
        ResolutionText = FormatResolution(
            RecordWidth,
            RecordHeight,
            ObservedCaptureFps > 0 ? ObservedCaptureFps : RequestedFps);
    }

    private async Task StartPreviewForRecordingAsync(AppConfig config)
    {
        if (_capture == null) return;
        _winRtPreview.Attach(_capture, config.MaxPreviewFpsUi);
        _winRtPreview.FrameArrived -= OnWinRtFrame;
        _winRtPreview.FrameArrived += OnWinRtFrame;
        await _winRtPreview.StartAsync();
        _fpsMonitor.Start();
        if (Status != "Recording") Status = "Previewing";
        StateChanged?.Invoke();
    }

    public async Task RestoreOpenCvPreviewAsync(AppConfig config, IReadOnlyList<CameraDevice> devices)
    {
        if (!_useOpenCvPreview) return;

        try
        {
            if (_lowLagRecording != null || Status == "Recording")
                await StopRecordingAsync();

            _winRtPreview.FrameArrived -= OnWinRtFrame;
            await _winRtPreview.StopAsync();

            if (_capture != null)
            {
                DetachRecordLimitHandler();
                _capture.Dispose();
                _capture = null;
            }

            _winRtOpenedForRecord = false;
            _lowLagRecording = null;
            _recordingFile = null;

            if (string.IsNullOrEmpty(_deviceId)) return;

            var ok = await OpenForOpenCvPreviewAsync(config, devices);
            if (ok)
                await StartPreviewAsync();
        }
        catch (Exception ex)
        {
            _log.Error("camera", $"{SlotName} restore OpenCV preview failed", ex);
            Status = "Error";
            LastError = ex.Message;
            StateChanged?.Invoke();
        }
    }

    public async Task TeardownWinRtForRecordingAsync()
    {
        try
        {
            if (_lowLagRecording != null)
            {
                await _lowLagRecording.StopAsync();
                await _lowLagRecording.FinishAsync();
                _lowLagRecording = null;
            }
            else if (_capture != null && Status == "Recording")
            {
                await _capture.StopRecordAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Error("recording", $"{SlotName} teardown record failed", ex);
        }

        DetachRecordLimitHandler();
        _winRtPreview.FrameArrived -= OnWinRtFrame;
        await _winRtPreview.StopAsync();

        if (_capture != null)
        {
            _capture.Dispose();
            _capture = null;
        }

        _winRtOpenedForRecord = false;
        _recordingFile = null;
        if (Status == "Recording") Status = "Ready";
        StateChanged?.Invoke();
    }

    public MediaEncodingProfile BuildRecordingProfile(AppConfig config)
    {
        if (_capture == null || SelectedMode == null)
            throw new InvalidOperationException("Camera not ready for recording");
        return _modeSelector.BuildProfile(_capture, SelectedMode);
    }

    public async Task StartRecordingAsync(
        StorageFile file,
        AppConfig config,
        MediaEncodingProfile? profile = null,
        Task? recordingStartGate = null,
        TaskCompletionSource<bool>? writerReady = null)
    {
        if (SelectedMode == null)
            throw new InvalidOperationException("Camera not ready for recording");

        _recordingFile = file;

        if (UsesOpenCvRecording(config))
        {
            var slotFocusConfig = config.WithSlotFocusSettings(SlotIndex);
            var slotExposureConfig = config.WithSlotExposureSettings(SlotIndex);
            _lastExposureControlStatus = PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig);
            if (slotFocusConfig.ReapplyFocusBeforeRecording)
                _lastFocusControlStatus = await WithCameraControlTimeoutAsync(
                    TryApplyFocusModeBestEffortAsync(slotFocusConfig), 1500,
                    _lastFocusControlStatus ?? CameraFocusControlStatus.NotAttempted(slotFocusConfig.AutoFocusEnabled)).ConfigureAwait(false);
            if (slotExposureConfig.ReapplyExposureBeforeRecording)
                _lastExposureControlStatus = await WithCameraControlTimeoutAsync(
                    TryApplyExposureModeBestEffortAsync(slotExposureConfig), 1500,
                    PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig)).ConfigureAwait(false);
            var rw = _openCvPreview.LiveWidth > 0 ? _openCvPreview.LiveWidth : RecordWidth;
            var rh = _openCvPreview.LiveHeight > 0 ? _openCvPreview.LiveHeight : RecordHeight;
            RecordWidth = rw;
            RecordHeight = rh;
            ResolutionText = FormatResolution(
                rw,
                rh,
                ObservedCaptureFps > 0 ? ObservedCaptureFps : RequestedFps);

            LastOpenCvRecordingStats = null;
            var requestedMetaFps = config.PreferFps > 0 ? config.PreferFps : RequestedFps;
            var selectedDeviceFps = SelectedDeviceFps > 0 ? SelectedDeviceFps : requestedMetaFps;
            var outputFolder = Path.GetDirectoryName(file.Path) ?? "";
            _log.Info("recording",
                $"{SlotName} startup: slot={SlotIndex + 1}, device=\"{DeviceName}\", capturePath={OpenCvDevicePathDescription}, dshowIndex={_openCvIndex}, resolution={CaptureResolutionPreset.ToLabel(rw, rh)}, requestedFps={requestedMetaFps:F3}, selectedFps={selectedDeviceFps:F3}, outputFolder={outputFolder}, mp4={file.Path}");
            AppDiagnosticLogger.Recording(
                $"{SlotName} writerStart device=\"{DeviceName}\" resolution={CaptureResolutionPreset.ToLabel(rw, rh)} mp4={Path.GetFileName(file.Path)}");
            if (_experimentSession?.Enabled == true)
                await _openCvPreview.StartExperimentRecordingAsync(file.Path, rw, rh, _experimentSession, config, SlotName, recordingStartGate, writerReady);
            else
                await _openCvPreview.StartRecordingAsync(file.Path, rw, rh, requestedMetaFps, selectedDeviceFps, SlotName, recordingStartGate, writerReady);
            _log.Info("recording",
                $"{SlotName} startup: writerOpened={_openCvPreview.WriterOpened}, firstFrameReceived={_openCvPreview.FirstFrameReceivedSinceRecord}, firstFrameWritten={_openCvPreview.FirstFrameWrittenSinceRecord}, queueCapacity={_openCvPreview.CurrentRecordQueueCapacity}");
            AppDiagnosticLogger.Recording(
                $"{SlotName} writerReady opened={_openCvPreview.WriterOpened} firstFrameWritten={_openCvPreview.FirstFrameWrittenSinceRecord}");
            RecordingWriterFps = _openCvPreview.LastRecordWriterFps;
            if (Status != "Previewing")
                await StartPreviewAsync();
            _fpsMonitor.Start();
            Status = "Recording";
            StateChanged?.Invoke();
            _log.Info("recording", $"{SlotName} OpenCV record+preview {CaptureResolutionPreset.ToLabel(rw, rh)} -> {file.Name}");
            return;
        }

        if (_lowLagRecording != null)
        {
            await BeginLowLagRecordingAsync();
            return;
        }

        if (_capture == null || profile == null)
            throw new InvalidOperationException("Camera not open");

        _resumePreviewAfterRecord = Status == "Previewing";
        if (_resumePreviewAfterRecord)
            await _winRtPreview.StopAsync();

        try
        {
            var slotFocusConfig = config.WithSlotFocusSettings(SlotIndex);
            var slotExposureConfig = config.WithSlotExposureSettings(SlotIndex);
            _lastExposureControlStatus = PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig);
            await _modeSelector.ApplyRecordModeAsync(_capture, SelectedMode);
            if (slotFocusConfig.ReapplyFocusBeforeRecording)
                _lastFocusControlStatus = await WithCameraControlTimeoutAsync(
                    ApplyFocusModeAsync(slotFocusConfig), 1500,
                    _lastFocusControlStatus ?? CameraFocusControlStatus.NotAttempted(slotFocusConfig.AutoFocusEnabled)).ConfigureAwait(false);
            if (slotExposureConfig.ReapplyExposureBeforeRecording)
                _lastExposureControlStatus = await WithCameraControlTimeoutAsync(
                    TryApplyExposureModeBestEffortAsync(slotExposureConfig), 1500,
                    PreserveExposureRequestState(_lastExposureControlStatus, slotExposureConfig)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Info("recording", $"{SlotName} ApplyRecordMode before record skipped: {ex.Message}");
        }

        AttachRecordLimitHandler();
        await _capture.StartRecordToStorageFileAsync(profile, file);
        _fpsMonitor.Start();
        Status = "Recording";
        RecordingWriterFps = SelectedMode?.SelectedDeviceFps > 0
            ? SelectedMode.SelectedDeviceFps
            : SelectedMode?.Fps ?? RequestedFps;
        StateChanged?.Invoke();
        _log.Info("recording", $"{SlotName} direct record started -> {file.Name}");
    }

    private void AttachRecordLimitHandler()
    {
        if (_capture == null || _recordLimitHandlerAttached) return;
        _capture.RecordLimitationExceeded += OnRecordLimitationExceeded;
        _recordLimitHandlerAttached = true;
    }

    private void DetachRecordLimitHandler()
    {
        if (!_recordLimitHandlerAttached) return;
        _recordLimitHandlerAttached = false;
        // Avoid unsubscribing during teardown — MediaCapture.Dispose() releases the source;
        // removing handlers here has caused AccessViolation on mixed OpenCV+WinRT sessions.
    }

    private void OnRecordLimitationExceeded(MediaCapture sender)
    {
        _log.Info("recording", $"{SlotName} OS record duration limit reached — stopping");
        _ = StopRecordingAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
                _log.Error("recording", $"{SlotName} auto-stop on limit failed", t.Exception.InnerException);
        }, TaskScheduler.Default);
    }

    public async Task<StorageFile?> StopRecordingAsync()
    {
        if (_openCvPreview.IsRecording)
        {
            await _openCvPreview.StopRecordingAsync();
            LastOpenCvRecordingStats = _openCvPreview.BuildRecordingStats(
                SlotName,
                DeviceName,
                _recordingFile?.Path ?? "",
                RequestedFps,
                SelectedDeviceFps > 0 ? SelectedDeviceFps : RequestedFps,
                "OpenCV-mp4v",
                "MP4");
            LastOpenCvRecordingStats = LastOpenCvRecordingStats with { DeviceIndex = _openCvIndex };
            LastOpenCvRecordingStats = LastOpenCvRecordingStats with
            {
                AutoFocusRequested = _lastFocusControlStatus.AutoFocusRequested,
                AutoFocusApplyAttempted = _lastFocusControlStatus.AutoFocusApplyAttempted,
                AutoFocusApplySucceeded = _lastFocusControlStatus.AutoFocusApplySucceeded,
                AutoFocusReadbackValue = _lastFocusControlStatus.AutoFocusReadbackValue,
                ManualFocusSupported = _lastFocusControlStatus.ManualFocusSupported,
                ManualFocusRequestedValue = _lastFocusControlStatus.ManualFocusRequestedValue,
                ManualFocusReadbackValue = _lastFocusControlStatus.ManualFocusReadbackValue,
                FocusControlMode = _lastFocusControlStatus.FocusControlMode,
                FocusWarning = _lastFocusControlStatus.FocusWarning,
                AutoExposureRequested = _lastExposureControlStatus.AutoExposureRequested,
                AutoExposureApplyAttempted = _lastExposureControlStatus.AutoExposureApplyAttempted,
                AutoExposureApplySucceeded = _lastExposureControlStatus.AutoExposureApplySucceeded,
                AutoExposureReadbackValue = _lastExposureControlStatus.AutoExposureReadbackValue,
                ManualExposureSupported = _lastExposureControlStatus.ManualExposureSupported,
                ManualExposureRequestedValue = _lastExposureControlStatus.ManualExposureRequestedValue,
                ManualExposureReadbackValue = _lastExposureControlStatus.ManualExposureReadbackValue,
                LowLightCompensationOffRequested = _lastExposureControlStatus.LowLightCompensationOffRequested,
                LowLightCompensationOffConfirmed = _lastExposureControlStatus.LowLightCompensationOffConfirmed,
                ExposureWarning = _lastExposureControlStatus.ExposureWarning
            };
            RecordingWriterFps = LastOpenCvRecordingStats.RecordingWriterFps > 0
                ? LastOpenCvRecordingStats.RecordingWriterFps
                : RecordingWriterFps;
            var stats = LastOpenCvRecordingStats;
            long fileSize = 0;
            if (!string.IsNullOrEmpty(_recordingFile?.Path) && File.Exists(_recordingFile.Path))
            {
                try { fileSize = new FileInfo(_recordingFile.Path).Length; }
                catch { /* best effort */ }
            }

            _log.Info("recording",
                $"{SlotName} stop summary: FramesCaptured={stats.FramesCaptured}, FramesWritten={stats.FramesWritten}, queueDrops={stats.WriterQueueDrops}, duplicates={stats.DuplicateFrames}, placeholders={stats.PlaceholderFrames}, fileSize={fileSize} bytes, mp4={_recordingFile?.Path}");
            if (Status == "Recording") Status = "Previewing";
            StateChanged?.Invoke();
            _log.Info("recording",
                $"{SlotName} OpenCV recording stopped ({LastOpenCvRecordingStats.FramesWritten} frames, {LastOpenCvRecordingStats.DurationSeconds:F2}s)");
            return _recordingFile;
        }

        if (_capture == null) return _recordingFile;
        try
        {
            if (_lowLagRecording != null)
            {
                await _lowLagRecording.StopAsync();
                await _lowLagRecording.FinishAsync();
                _lowLagRecording = null;
                DetachRecordLimitHandler();
            }
            else if (Status == "Recording")
            {
                await _capture.StopRecordAsync();
            }

            if (Status == "Recording")
                Status = _winRtOpenedForRecord ? "Ready" : "Previewing";
            StateChanged?.Invoke();
            _log.Info("recording", $"{SlotName} recording stopped");

            if (_resumePreviewAfterRecord && _capture != null)
            {
                _resumePreviewAfterRecord = false;
                await StartPreviewAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Error("recording", $"{SlotName} stop failed", ex);
        }

        return _recordingFile;
    }

    private async Task StopRecordingIfActiveAsync()
    {
        if (Status != "Recording" || _capture == null) return;
        if (_lowLagRecording != null)
        {
            await _lowLagRecording.StopAsync();
            await _lowLagRecording.FinishAsync();
            _lowLagRecording = null;
            DetachRecordLimitHandler();
        }
        else
        {
            await _capture.StopRecordAsync();
        }
    }

    public string DeviceName => _deviceInfo?.Name ?? "(unknown)";

    public string OpenCvDevicePathDescription =>
        _openCvBinding is { } b
            ? !string.IsNullOrWhiteSpace(b.DirectShowOpenUri) ? b.DirectShowOpenUri
            : !string.IsNullOrWhiteSpace(b.DirectShowName) ? b.DirectShowName
            : $"index:{b.Index}"
            : $"index:{_openCvIndex}";

    public Task<bool> WaitForFirstFrameWrittenAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _openCvPreview.WaitForFirstFrameWrittenAsync(timeout, cancellationToken);

    public bool FirstFrameReceivedSinceRecordOpenCv => _openCvPreview.FirstFrameReceivedSinceRecord;

    public RecordingSlotStartupSnapshot BuildRecordingStartupSnapshot(
        AppConfig config,
        long framesCapturedAfter1s = -1,
        long framesWrittenAfter1s = -1,
        long framesCapturedAfter3s = -1,
        long framesWrittenAfter3s = -1,
        string? exceptionMessage = null)
    {
        var mode = SelectedMode;
        var mp4 = _recordingFile?.Path ?? "";
        var openCv = UsesOpenCvRecording(config);
        return new RecordingSlotStartupSnapshot
        {
            SlotName = SlotName,
            DeviceName = DeviceName,
            DevicePath = OpenCvDevicePathDescription,
            DirectShowIndex = _openCvIndex,
            PreviewActive = Status is "Previewing" or "Recording",
            SelectedResolution = mode != null
                ? CaptureResolutionPreset.ToLabel(mode.Width, mode.Height)
                : ResolutionText,
            SelectedFps = mode?.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode?.Fps ?? ObservedCaptureFps,
            RequestedWriterFps = RecordingWriterFps > 0 ? RecordingWriterFps : RequestedFps,
            OutputFolder = string.IsNullOrEmpty(mp4) ? "" : Path.GetDirectoryName(mp4) ?? "",
            Mp4Path = mp4,
            VideoWriterCreated = openCv && _openCvPreview.IsRecording,
            VideoWriterOpened = openCv && _openCvPreview.WriterOpened,
            RecordingFlagSet = openCv ? _openCvPreview.IsRecording : Status == "Recording",
            RecordingTaskStarted = openCv && _openCvPreview.RecordingPumpStarted,
            FrameQueueConnected = openCv && _openCvPreview.FrameQueueConnected,
            FirstFrameReceived = openCv && _openCvPreview.FirstFrameReceivedSinceRecord,
            FirstFrameWritten = openCv && _openCvPreview.FirstFrameWrittenSinceRecord,
            FramesCapturedAfter1s = framesCapturedAfter1s,
            FramesWrittenAfter1s = framesWrittenAfter1s,
            FramesCapturedAfter3s = framesCapturedAfter3s,
            FramesWrittenAfter3s = framesWrittenAfter3s,
            Backend = openCv ? "OpenCV-DSHOW" : "WinRT",
            ExceptionMessage = exceptionMessage
        };
    }

    public string? HardwareId => _deviceInfo?.Id;

    public async Task HandleDisconnectAsync()
    {
        if (Status == "Disconnected" || Status == "Idle") return;
        WasPreviewingBeforeDisconnect = Status is "Previewing" or "Recording";
        _log.Error("camera", $"{SlotName} disconnected");
        AppDiagnosticLogger.Runtime($"{SlotName} device disconnected (removed)");
        if (Status == "Recording")
            AppDiagnosticLogger.Recording($"{SlotName} disconnected during recording");
        await StopPreviewAsync();
        _openCvPreview.ReleaseCamera();
        if (_openCvBinding != null)
        {
            OpenCvDeviceSession.Release(SlotIndex, _openCvBinding);
            _openCvBinding = null;
        }
        if (_capture != null)
        {
            try
            {
                await StopRecordingIfActiveAsync();
            }
            catch { /* device gone */ }
            DetachRecordLimitHandler();
            _capture.Dispose();
            _capture = null;
        }
        _recordingFile = null;
        Status = "Lost connection";
        LastError = "Lost connection";
        SetPreviewSlotState(PreviewSlotStateKind.LostConnection);
        StateChanged?.Invoke();
    }

    public async Task CloseAsync(bool clearSessionHint = true)
    {
        _openCvPreview.AbortPendingOpen();
        if (clearSessionHint)
            WasPreviewingBeforeDisconnect = false;
        _fpsMonitor.Stop();
        _openCvPreview.FrameArrived -= OnOpenCvFrame;
        AppDiagnosticLogger.Runtime($"SLOT_FRAME_CALLBACK_UNSUBSCRIBED cam{SlotIndex + 1}");
        _openCvPreview.StreamLost -= OnOpenCvStreamLost;
        AppDiagnosticLogger.Runtime($"SLOT_STREAM_LOST_UNSUBSCRIBED cam{SlotIndex + 1}");
        _winRtPreview.FrameArrived -= OnWinRtFrame;
        AppDiagnosticLogger.Runtime($"SLOT_EVENT_UNSUBSCRIBED cam{SlotIndex + 1} close");
        try { await _openCvPreview.StopAsync(); } catch (Exception ex) { _log.Error("camera", $"{SlotName} OpenCV stop", ex); }
        try { await _winRtPreview.StopAsync(); } catch (Exception ex) { _log.Error("camera", $"{SlotName} WinRT stop", ex); }
        AppDiagnosticLogger.Runtime($"SLOT_CAPTURE_RELEASE_START cam{SlotIndex + 1}");
        try { await Task.Run(() => _openCvPreview.ReleaseCamera()).ConfigureAwait(false); }
        catch (Exception ex) { _log.Error("camera", $"{SlotName} OpenCV release", ex); }
        AppDiagnosticLogger.Runtime($"SLOT_CAPTURE_RELEASE_END cam{SlotIndex + 1}");
        if (_openCvBinding != null)
        {
            try { OpenCvDeviceSession.Release(SlotIndex, _openCvBinding); } catch { }
            _openCvBinding = null;
        }

        if (!string.IsNullOrEmpty(_deviceId))
        {
            OpenCvDeviceSession.ForgetDevice(_deviceId);
            SlotDeviceOwnership.Release(_deviceId, SlotIndex);
        }
        else
            SlotDeviceOwnership.ReleaseSlot(SlotIndex);

        if (_capture != null)
        {
            try
            {
                await StopRecordingIfActiveAsync();
            }
            catch { /* ignore */ }
            DetachRecordLimitHandler();
            try { _capture.Dispose(); } catch (Exception ex) { _log.Error("camera", $"{SlotName} WinRT dispose", ex); }
            _capture = null;
        }

        _recordingFile = null;
        _winRtOpenedForRecord = false;
        _deviceId = null;
        Status = "Idle";
        ResolutionText = "-";
        StateChanged?.Invoke();
    }

    public void Dispose() => _ = CloseAsync();
}
