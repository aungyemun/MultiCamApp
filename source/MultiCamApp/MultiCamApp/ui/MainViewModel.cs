using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Diagnostics;
using MultiCamApp.Localization;
using MultiCamApp.Recording;
using MultiCamApp.Utils;

namespace MultiCamApp.Ui;

public sealed class MainViewModel
{
    private readonly CameraManager _cameraManager = new();
    private readonly CameraDeviceWatcher _deviceWatcher = new();
    private CancellationTokenSource? _deviceRefreshDebounce;
    private bool _suppressDeviceRefresh;
    private readonly CameraReconnectService _reconnect = new();
    private RecordingController _recording = null!;
    private MultiCameraRecordingCoordinator _recordCoordinator = null!;
    private PrivacyGuardService _privacy = null!;
    private AppLifecycleService _lifecycle = null!;
    private readonly LogService _log = new();
    private readonly MonotonicClock _recordingUiClock = new();
    private readonly CameraSlotPipeline[] _pipelines = [new(0), new(1), new(2), new(3)];
    private readonly PerformanceMonitorService _performanceMonitor;

    public LanguageManager Language { get; } = new();
    public AppState State { get; } = new();
    public AppConfig Config { get; private set; } = new();
    public VersionInfo Version { get; private set; } = new();
    public CameraManager CameraManager => _cameraManager;
    public RecordingController Recording => _recording;
    public AppLifecycleService Lifecycle => _lifecycle;

    public IReadOnlyList<CameraDevice> Devices { get; private set; } = Array.Empty<CameraDevice>();
    public string?[] SelectedDeviceIds { get; } = new string?[4];
    public CameraPanelViewModel[] Panels { get; }
    public string OutputFolderDisplay { get; set; } = "";
    public string ElapsedDisplay { get; set; } = "00:00:00";
    public string StatusDisplay { get; private set; } = "";
    public string PreviewStartupStatus { get; private set; } = "";
    public string MultiCameraUsbWarning { get; private set; } = "";
    public bool PreviewStartInProgress { get; private set; }

    public readonly record struct UiButtonStates(
        bool StartPreviewEnabled,
        bool StopPreviewEnabled,
        bool StartRecordingEnabled,
        bool StopRecordingEnabled);

    public event Action? UiRefreshRequested;
    public event Action? DevicesListChanged;
    public event Action? CameraAccessDenied;
    /// <summary>Other slot cleared because the same device was picked elsewhere.</summary>
    public event Action<int>? SlotDeviceSelectionCleared;
    public Dispatcher? UiDispatcher { get; set; }

    private readonly Action<BitmapSource>?[] _previewHandlers = new Action<BitmapSource>?[4];
    private readonly BitmapSource?[] _latestPreviewFrame = new BitmapSource?[4];
    private readonly int[] _previewUiScheduled = new int[4];
    private readonly int[] _previewFrameSession = new int[4];
    private readonly int[] _previewWarmupFramesRemaining = new int[4];
    private readonly int[] _previewFirstValidLogged = new int[4];
    private readonly int[] _previewFirstRenderedLogged = new int[4];
    private readonly SemaphoreSlim _deviceChangeGate = new(1, 1);
    private readonly SemaphoreSlim _previewLifecycleGate = new(1, 1);
    private static readonly SemaphoreSlim OpenCvReleaseGate = new(1, 1);
    private CancellationTokenSource? _previewOpenCts;
    private readonly Dictionary<string, string> _lastKnownDeviceLabels = new(StringComparer.OrdinalIgnoreCase);
    private int _appClosingCleanupStarted;

    private int _layoutCount = 1;

    // App-folder session debug logs (preview/recording only; never in output folders).
    private AsyncBoundedTextFileLogger? _activePreviewSessionLog;
    private AsyncBoundedTextFileLogger? _activeRecordingSessionLog;
    private string? _activePreviewSessionId;
    private string? _activeRecordingSessionId;
    private DateTime _activePreviewStartClickedLocal;
    private DateTime _activeRecordingStartClickedLocal;

    private readonly object _sessionLogGate = new();
    private bool _previewStopRequested;
    private int _previewSessionGeneration;

    private UiButtonStates _activePreviewUiBefore;
    private bool _hasActivePreviewUiBefore;

    private UiButtonStates _activeRecordingUiBefore;
    private bool _hasActiveRecordingUiBefore;
    private int _recordingTimingFailureNotified;

    public bool IsPreviewLifecycleBusy =>
        PreviewStartInProgress
        || State.RunState is AppRunState.Previewing or AppRunState.Recording
        || _pipelines.Any(p => p.Status is "Previewing" or "Recording" or "Ready"
                              || p.PreviewSlotState is PreviewSlotStateKind.Opening or PreviewSlotStateKind.PreviewReady);

    public bool BlockPreviewMutation(string marker, string message)
    {
        if (!IsPreviewLifecycleBusy)
            return false;

        StatusDisplay = message;
        AppDiagnosticLogger.Runtime(marker);
        RefreshUi();
        return true;
    }

    private int BeginNewPreviewFrameSession()
    {
        var generation = Interlocked.Increment(ref _previewSessionGeneration);
        for (var i = 0; i < 4; i++)
        {
            _latestPreviewFrame[i] = null;
            _previewFrameSession[i] = generation;
            _previewWarmupFramesRemaining[i] = 2;
            _previewFirstValidLogged[i] = 0;
            _previewFirstRenderedLogged[i] = 0;
            Interlocked.Exchange(ref _previewUiScheduled[i], 0);
        }

        return generation;
    }

    private void InvalidatePreviewFrameSession()
    {
        Interlocked.Increment(ref _previewSessionGeneration);
        for (var i = 0; i < 4; i++)
        {
            _latestPreviewFrame[i] = null;
            _previewFrameSession[i] = 0;
            _previewWarmupFramesRemaining[i] = 0;
            _previewFirstValidLogged[i] = 0;
            _previewFirstRenderedLogged[i] = 0;
            Interlocked.Exchange(ref _previewUiScheduled[i], 0);
        }
    }

    private void ClearPreviewImageSources(Func<int, System.Windows.Controls.Image>? getPreviewImage)
    {
        if (getPreviewImage == null) return;

        void Clear()
        {
            for (var i = 0; i < 4; i++)
            {
                try
                {
                    getPreviewImage(i).Source = null;
                    AppDiagnosticLogger.Runtime($"UI_IMAGE_CLEARED cam{i + 1}");
                }
                catch { /* best effort */ }
            }
        }

        if (UiDispatcher?.CheckAccess() == true)
            Clear();
        else
            UiDispatcher?.BeginInvoke(Clear);
    }

    private void FlushPreviewFrame(int idx, System.Windows.Controls.Image img, int sessionGeneration)
    {
        try
        {
            if (sessionGeneration != Volatile.Read(ref _previewSessionGeneration)
                || _previewFrameSession[idx] != sessionGeneration)
            {
                AppDiagnosticLogger.Runtime($"UI_CALLBACK_IGNORED_OLD_SESSION cam{idx + 1} session={sessionGeneration}");
                return;
            }

            var frame = _latestPreviewFrame[idx];
            _latestPreviewFrame[idx] = null;
            if (frame != null)
                img.Source = frame;
        }
        catch (Exception ex)
        {
            _log.Error("preview", $"UI frame update failed slot {idx}", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _previewUiScheduled[idx], 0);
            if (_latestPreviewFrame[idx] != null
                && sessionGeneration == Volatile.Read(ref _previewSessionGeneration)
                && _previewFrameSession[idx] == sessionGeneration
                && Interlocked.CompareExchange(ref _previewUiScheduled[idx], 1, 0) == 0)
            {
                UiDispatcher?.BeginInvoke(DispatcherPriority.Render, () => FlushPreviewFrame(idx, img, sessionGeneration));
            }
        }
    }

    public bool HasSelectedDeviceForActiveLayout()
    {
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (!string.IsNullOrEmpty(SelectedDeviceIds[i]))
                return true;
        }
        return false;
    }

    public string GetCameraSlotLabel(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _pipelines.Length)
            return "";
        var deviceId = SelectedDeviceIds[slotIndex];
        var name = !string.IsNullOrEmpty(deviceId)
            ? Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            : null;
        return string.IsNullOrWhiteSpace(name) ? $"cam{slotIndex + 1}" : $"cam{slotIndex + 1}: {name}";
    }

    public CameraFocusControlStatus GetFocusStatus(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _pipelines.Length)
            return CameraFocusControlStatus.NotAttempted(Config.AutoFocusEnabled);
        return _pipelines[slotIndex].LastFocusControlStatus;
    }

    public async Task<IReadOnlyList<CameraFocusControlStatus>> ApplyFocusSettingsAsync(int? slotIndex)
    {
        var targets = new List<CameraSlotPipeline>();
        if (slotIndex.HasValue)
        {
            var i = slotIndex.Value;
            if (i >= 0 && i < State.CameraLayout && !string.IsNullOrEmpty(SelectedDeviceIds[i]))
                targets.Add(_pipelines[i]);
        }
        else
        {
            for (var i = 0; i < State.CameraLayout; i++)
            {
                if (!string.IsNullOrEmpty(SelectedDeviceIds[i]))
                    targets.Add(_pipelines[i]);
            }
        }

        var results = new List<CameraFocusControlStatus>();
        foreach (var target in targets)
        {
            var slotConfig = target.SlotIndex >= 0 ? Config.WithSlotFocusSettings(target.SlotIndex) : Config;
            var status = await target.ApplyFocusSettingsAsync(slotConfig, "ui").ConfigureAwait(true);
            results.Add(status);
        }

        if (results.Count == 0)
            StatusDisplay = "No selected/open camera is available for focus control.";
        else
        {
            var effectiveAutoFocus = slotIndex.HasValue ? Config.AutoFocusEnabledPerCamera[slotIndex.Value] : Config.AutoFocusEnabled;
            StatusDisplay = effectiveAutoFocus ? "Focus: autofocus on" : "Focus: autofocus off";
        }

        RefreshUi();
        return results;
    }

    public CameraExposureControlStatus GetExposureStatus(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _pipelines.Length)
            return CameraExposureControlStatus.NotAttempted(
                Config.AutoExposureEnabled,
                Config.DisableLowLightCompensation);
        return _pipelines[slotIndex].LastExposureControlStatus;
    }

    public async Task<IReadOnlyList<CameraExposureControlStatus>> ApplyExposureSettingsAsync(int? slotIndex)
    {
        var targets = new List<CameraSlotPipeline>();
        if (slotIndex.HasValue)
        {
            var i = slotIndex.Value;
            if (i >= 0 && i < State.CameraLayout && !string.IsNullOrEmpty(SelectedDeviceIds[i]))
                targets.Add(_pipelines[i]);
        }
        else
        {
            for (var i = 0; i < State.CameraLayout; i++)
            {
                if (!string.IsNullOrEmpty(SelectedDeviceIds[i]))
                    targets.Add(_pipelines[i]);
            }
        }

        var results = new List<CameraExposureControlStatus>();
        foreach (var target in targets)
        {
            var slotConfig = target.SlotIndex >= 0 ? Config.WithSlotExposureSettings(target.SlotIndex) : Config;
            var status = await target.ApplyExposureSettingsAsync(slotConfig, "ui").ConfigureAwait(true);
            results.Add(status);
        }

        if (results.Count == 0)
            StatusDisplay = "No selected/open camera is available for exposure control.";
        else
        {
            var warned = results.Any(r => !string.IsNullOrEmpty(r.ExposureWarning));
            StatusDisplay = warned
                ? "Exposure: applied (unconfirmed — check camera vendor settings if needed)"
                : Config.AutoExposureEnabled
                    ? "Exposure: auto exposure on"
                    : "Exposure: manual exposure applied";
        }

        RefreshUi();
        return results;
    }

    public MainViewModel()
    {
        _performanceMonitor = new PerformanceMonitorService(GetPerformanceSampleSources, () => State.RunState.ToString());
        Panels = _pipelines.Select((p, i) => new CameraPanelViewModel(i, p)).ToArray();
        foreach (var pipeline in _pipelines)
            pipeline.WriterQueueDropDetected += OnWriterQueueDropDetected;
        foreach (var p in Panels)
            p.StatsChanged += () => RefreshUi();
    }

    private void OnWriterQueueDropDetected(CameraSlotPipeline slot, long drops)
    {
        if (Interlocked.CompareExchange(ref _recordingTimingFailureNotified, 1, 0) != 0)
            return;

        void Notify()
        {
            StatusDisplay = "Recording timing failure detected";
            _activeRecordingSessionLog?.Line(
                $"LIVE_TIMING_FAILURE slot={slot.SlotName} writerQueueDrops={drops} action=continue_recording sessionMarkedScientificallyInvalid=yes");
            AppDiagnosticLogger.Recording(
                $"LIVE_TIMING_FAILURE {slot.SlotName} writerQueueDrops={drops} sessionMarkedScientificallyInvalid=yes");
            RefreshUi();
        }

        var dispatcher = UiDispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke((Action)Notify);
        else
            Notify();
    }

    public void PreloadLanguage()
    {
        Config = JsonLoader.LoadFromFile<AppConfig>(JsonLoader.ConfigPath("appsettings.json")) ?? new AppConfig();
        Language.Load(Config.DefaultLanguage);
        StatusDisplay = Language["idle"];
    }

    public async Task InitializeAsync()
    {
        Config = JsonLoader.LoadFromFile<AppConfig>(JsonLoader.ConfigPath("appsettings.json")) ?? Config;
        Version = VersionService.Load();
        VersionService.Reload();
        Language.Load(Config.DefaultLanguage);
        StatusDisplay = Language["idle"];
        _privacy = new PrivacyGuardService(Config);
        _recording = new RecordingController(Config, _privacy);
        _recordCoordinator = new MultiCameraRecordingCoordinator(_recording, Config);
        _lifecycle = new AppLifecycleService(Config, _privacy);
        _recording.ApplyVersion(Version);
        _cameraManager.RegisterSlots(_pipelines);
        _cameraManager.SetConfig(Config);
        OutputFolderDisplay = PathHelper.DefaultVideosFolder();
        _suppressDeviceRefresh = true;
        Devices = await _cameraManager.DiscoverAsync();
        RememberDeviceLabels(Devices);
        ApplyDefaultDeviceSelections();
        _deviceWatcher.DevicesChanged += OnDevicesChanged;
        _deviceWatcher.DeviceRemoved += OnDeviceRemoved;
        _deviceWatcher.Start();
        _suppressDeviceRefresh = false;
        foreach (var p in _pipelines)
            p.StateChanged += () => _ = OnSlotStateChangedAsync(p);
        _log.Info("startup", $"MultiCamApp {Version.Display}");
        RefreshUi();
    }

    public async Task RefreshDevicesAsync(bool userInitiated = false)
    {
        await _deviceChangeGate.WaitAsync().ConfigureAwait(true);
        using var logSession = userInitiated ? CameraRefreshLog.Begin() : null;
        var previousSelections = SelectedDeviceIds.ToArray();
        try
        {
            if (userInitiated)
            {
                _suppressDeviceRefresh = true;
                _deviceRefreshDebounce?.Cancel();
            }

            _cameraManager.SetConfig(Config);
            Devices = await _cameraManager.RefreshAsync().ConfigureAwait(true);
            OpenCvDeviceMapper.ClearCache();
            OpenCvDirectShowIndexCatalog.Rebuild(Devices);
            RememberDeviceLabels(Devices);

            PreserveSelectionsAfterRefresh(out var preserved, out var missing);

            if (userInitiated)
            {
                logSession?.WriteSummary(Devices.Count, Devices, previousSelections, preserved, missing);
                StatusDisplay = missing.Any(m => m)
                    ? Language["cameraRefreshMissingDevices"]
                    : string.Format(Language["cameraRefreshComplete"], Devices.Count);
            }

            DevicesListChanged?.Invoke();
            RefreshUi();
        }
        finally
        {
            if (userInitiated)
                _suppressDeviceRefresh = false;
            _deviceChangeGate.Release();
        }
    }

    private void RememberDeviceLabels(IReadOnlyList<CameraDevice> devices)
    {
        foreach (var d in devices)
            _lastKnownDeviceLabels[d.Id] = d.DisplayName;
    }

    /// <summary>Keeps selections by unique device ID; marks missing devices without auto-replacing them.</summary>
    private void PreserveSelectionsAfterRefresh(out bool[] preserved, out bool[] missing)
    {
        preserved = new bool[4];
        missing = new bool[4];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 4; i++)
        {
            var current = SelectedDeviceIds[i];
            if (string.IsNullOrEmpty(current))
                continue;

            if (!Devices.Any(d => d.Id == current))
            {
                missing[i] = true;
                continue;
            }

            if (used.Contains(current))
            {
                SelectedDeviceIds[i] = null;
                continue;
            }

            used.Add(current);
            preserved[i] = true;
        }
    }

    public bool IsSlotDeviceMissing(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return false;
        var id = SelectedDeviceIds[slotIndex];
        return !string.IsNullOrEmpty(id) && Devices.All(d => !string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasMissingDeviceInActiveLayout()
    {
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (IsSlotDeviceMissing(i))
                return true;
        }
        return false;
    }

    public string GetUnavailableDeviceLabel(string deviceId)
    {
        var name = _lastKnownDeviceLabels.TryGetValue(deviceId, out var label) ? label : Language["deviceUnavailable"];
        return string.Format(Language["deviceUnavailableNamed"], name);
    }

    /// <summary>Assign devices to slots; keeps user picks when still valid; any slot may use any camera.</summary>
    public void AssignDistinctDevicesForLayout(bool fillEmptySlotsOnly = false)
    {
        if (Devices.Count == 0) return;

        var preferExternal = _layoutCount >= 2;
        var ordered = Devices
            .OrderByDescending(d => preferExternal && d.Kind == CameraKind.ExternalUsb)
            .ThenByDescending(d => d.IsDefault && !preferExternal)
            .ThenBy(d => d.Kind switch
            {
                CameraKind.BuiltInFront => 0,
                CameraKind.BuiltInBack => 1,
                CameraKind.BuiltInOther => 2,
                CameraKind.ExternalUsb => 3,
                _ => 4
            })
            .ThenBy(d => d.EnumerationIndex)
            .ToList();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _layoutCount; i++)
        {
            var current = SelectedDeviceIds[i];
            if (!string.IsNullOrEmpty(current) && Devices.Any(d => d.Id == current))
            {
                if (used.Contains(current))
                    SelectedDeviceIds[i] = null;
                else
                {
                    used.Add(current);
                    continue;
                }
            }
            else if (!string.IsNullOrEmpty(current))
                SelectedDeviceIds[i] = null;

            if (fillEmptySlotsOnly && !string.IsNullOrEmpty(SelectedDeviceIds[i]))
                continue;

            var pick = ordered.FirstOrDefault(d => !used.Contains(d.Id));
            SelectedDeviceIds[i] = pick?.Id;
            if (pick != null)
                used.Add(pick.Id);
        }
    }

    public void SetSelectedDevice(int slotIndex, string? deviceId)
    {
        if (slotIndex < 0 || slotIndex >= 4) return;
        SelectedDeviceIds[slotIndex] = deviceId;
        if (!string.IsNullOrEmpty(deviceId))
        {
            var dev = Devices.FirstOrDefault(d => d.Id == deviceId);
            if (dev != null)
                _lastKnownDeviceLabels[deviceId] = dev.DisplayName;
        }
    }

    /// <summary>Fills only empty slots; never replaces valid user selections.</summary>
    public void ApplyDefaultDeviceSelections() => AssignDistinctDevicesForLayout(fillEmptySlotsOnly: true);

    public bool HasDuplicateDeviceSelection() => TryGetDuplicateDeviceSlots(out _, out _);

    public bool TryGetDuplicateDeviceSlots(out int firstSlot, out int duplicateSlot)
    {
        firstSlot = duplicateSlot = -1;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < State.CameraLayout; i++)
        {
            var id = SelectedDeviceIds[i];
            if (string.IsNullOrEmpty(id)) continue;
            if (seen.TryGetValue(id, out var first))
            {
                firstSlot = first;
                duplicateSlot = i;
                return true;
            }
            seen[id] = i;
        }
        return false;
    }

    public string? GetDuplicateDeviceWarning()
    {
        if (!TryGetDuplicateDeviceSlots(out var first, out var dup)) return null;
        return string.Format(Language["duplicateDeviceInSlot"], first + 1, dup + 1);
    }

    public bool AllActiveSlotsPreviewReady()
    {
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
            if (_pipelines[i].PreviewSlotState is PreviewSlotStateKind.FailedUnsupportedPreset
                or PreviewSlotStateKind.FailedDeviceOpen
                or PreviewSlotStateKind.Opening)
                return false;
            if (_pipelines[i].Status is not ("Previewing" or "Recording"))
                return false;
        }
        return true;
    }

    public (int Ready, int Required) CountPreviewReadySlots()
    {
        var required = 0;
        var ready = 0;
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
            required++;
            if (_pipelines[i].PreviewSlotState == PreviewSlotStateKind.PreviewReady
                && _pipelines[i].Status is "Previewing" or "Recording")
                ready++;
        }
        return (ready, required);
    }

    public CameraSlotPipeline? FirstNotReadyActiveSlot()
    {
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
            if (_pipelines[i].PreviewSlotState is PreviewSlotStateKind.FailedUnsupportedPreset
                or PreviewSlotStateKind.FailedDeviceOpen
                or PreviewSlotStateKind.Opening)
                return _pipelines[i];
            if (_pipelines[i].Status is not ("Previewing" or "Recording"))
                return _pipelines[i];
        }
        return null;
    }

    public async Task ApplyDeviceSelectionForSlotAsync(
        int slotIndex,
        Func<int, System.Windows.Controls.Image> getPreviewImage)
    {
        if (slotIndex < 0 || slotIndex >= 4 || slotIndex >= State.CameraLayout)
            return;

        if (BlockPreviewMutation(
                "CAMERA_SELECTION_CHANGE_BLOCKED_PREVIEW_ACTIVE",
                "Stop preview before changing layout or camera selection."))
            return;

        if (State.IsRecording)
        {
            StatusDisplay = Language["deviceChangeWhileRecording"];
            RefreshUi();
            return;
        }

        await _deviceChangeGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var newId = SelectedDeviceIds[slotIndex];
            for (var j = 0; j < State.CameraLayout; j++)
            {
                if (j == slotIndex || string.IsNullOrEmpty(newId)) continue;
                if (!string.Equals(SelectedDeviceIds[j], newId, StringComparison.OrdinalIgnoreCase))
                    continue;

                await ReleaseSlotCameraAsync(j).ConfigureAwait(true);
                SelectedDeviceIds[j] = null;
                SlotDeviceSelectionCleared?.Invoke(j);
                _log.Info("camera", $"Released {Panels[j].Pipeline.SlotName} — device reassigned to {Panels[slotIndex].Pipeline.SlotName}");
            }

            if (string.IsNullOrEmpty(newId))
            {
                await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
                RefreshRunStateAfterDeviceChange();
                return;
            }

            if (State.RunState != AppRunState.Previewing)
            {
                RefreshUi();
                return;
            }

            await SwapSlotDeviceAsync(slotIndex, newId, getPreviewImage).ConfigureAwait(true);
            RefreshRunStateAfterDeviceChange();
        }
        finally
        {
            _deviceChangeGate.Release();
        }
    }

    private async Task ReleaseSlotCameraAsync(int slotIndex)
    {
        await OpenCvReleaseGate.WaitAsync().ConfigureAwait(true);
        try
        {
            try
            {
                var slot = _pipelines[slotIndex];
                slot.AbortPendingOpen();
                DetachPreviewHandler(slotIndex, slot);
                if (slot.Status is "Previewing" or "Recording")
                {
                    try { await slot.StopPreviewAsync().ConfigureAwait(true); }
                    catch (Exception ex) { _log.Error("camera", $"{slot.SlotName} stop before release", ex); }
                }

                try { await slot.CloseAsync(clearSessionHint: true).ConfigureAwait(true); }
                catch (Exception ex)
                {
                    _log.Error("camera", $"{slot.SlotName} close failed", ex);
                    AppDiagnosticLogger.Failure("preview", $"{slot.SlotName} close failed", ex);
                }

                SlotDeviceOwnership.ReleaseSlot(slotIndex);
            }
            catch (Exception ex)
            {
                _log.Error("camera", $"Release slot cam{slotIndex + 1} failed", ex);
                AppDiagnosticLogger.Failure("preview", $"Release slot cam{slotIndex + 1}", ex);
            }
        }
        finally
        {
            OpenCvReleaseGate.Release();
        }
    }

    /// <summary>Closes cameras on slots outside the active layout (cam3/cam4 when using 2-camera layout).</summary>
    public async Task CloseInactiveLayoutSlotsAsync()
    {
        for (var i = State.CameraLayout; i < 4; i++)
            await ReleaseSlotCameraAsync(i).ConfigureAwait(true);
    }

    private void DetachPreviewHandler(int slotIndex, CameraSlotPipeline slot)
    {
        if (_previewHandlers[slotIndex] is { } h)
        {
            slot.PreviewFrameBitmap -= h;
            _previewHandlers[slotIndex] = null;
            AppDiagnosticLogger.Runtime($"SLOT_EVENT_UNSUBSCRIBED cam{slotIndex + 1} frame");
        }

        _latestPreviewFrame[slotIndex] = null;
        Interlocked.Exchange(ref _previewUiScheduled[slotIndex], 0);
    }

    private void AttachPreviewHandler(CameraSlotPipeline slot, Func<int, System.Windows.Controls.Image> getPreviewImage)
    {
        var idx = slot.SlotIndex;
        var disp = UiDispatcher;
        var sessionGeneration = Volatile.Read(ref _previewSessionGeneration);
        if (disp == null) return;

        DetachPreviewHandler(idx, slot);

        void Handler(BitmapSource bmp)
        {
            if (sessionGeneration != Volatile.Read(ref _previewSessionGeneration))
            {
                AppDiagnosticLogger.Runtime($"OLD_CALLBACK_IGNORED cam{idx + 1} session={sessionGeneration}");
                return;
            }
            _latestPreviewFrame[idx] = bmp;
            if (Interlocked.CompareExchange(ref _previewUiScheduled[idx], 1, 0) != 0)
                return;
            disp.BeginInvoke(DispatcherPriority.Render, () => FlushPreviewFrame(idx, getPreviewImage(idx), sessionGeneration));
        }

        _previewHandlers[idx] = Handler;
        slot.PreviewFrameBitmap += Handler;
    }

    private async Task SwapSlotDeviceAsync(
        int slotIndex,
        string deviceId,
        Func<int, System.Windows.Controls.Image> getPreviewImage)
    {
        var slot = _pipelines[slotIndex];
        var previousId = slot.AssignedDeviceId;
        if (!string.IsNullOrEmpty(previousId))
            OpenCvDeviceSession.ForgetDevice(previousId);
        OpenCvDeviceSession.ForgetDevice(deviceId);
        await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
        OpenCvDuplicateUsbResolver.BindSelectedDevices(
            Devices,
            SelectedDeviceIds.Take(State.CameraLayout).ToList());
        await Task.Delay(CaptureResolutionHelper.MultiCameraStaggerMs(
            State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)).ConfigureAwait(true);

        _cameraManager.SetConfig(Config);
        try
        {
            using var swapTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(PreviewOpenTimeoutSeconds()));
            var ok = await slot.OpenAsync(deviceId, Config, Devices, SelectedDeviceIds)
                .WaitAsync(swapTimeout.Token).ConfigureAwait(true);
            if (!ok)
            {
                StatusDisplay = slot.LastError ?? Language["previewFailed"];
                _log.Info("camera", $"{slot.SlotName} device swap failed");
                return;
            }

            AttachPreviewHandler(slot, getPreviewImage);
            await slot.StartPreviewAsync().ConfigureAwait(true);
            StatusDisplay = Language["previewing"];
            _log.Info("camera",
                $"{slot.SlotName} device swapped successfully (enumeration {Devices.FirstOrDefault(d => d.Id == deviceId)?.EnumerationIndex})");
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                _log.Error("camera", $"{slot.SlotName} device swap timed out");
                StatusDisplay = Language["previewFailed"];
                return;
            }

            _log.Error("camera", $"{slot.SlotName} device swap failed", ex);
            StatusDisplay = Language["previewFailed"];
        }
    }

    private void RefreshRunStateAfterDeviceChange()
    {
        var anyPreviewing = false;
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (_pipelines[i].Status is "Previewing" or "Recording")
            {
                anyPreviewing = true;
                break;
            }
        }

        State.RunState = anyPreviewing ? AppRunState.Previewing : AppRunState.Idle;
        if (!anyPreviewing)
            StatusDisplay = Language["idle"];
        RefreshUi();
    }

    private void OnDeviceRemoved(string deviceId)
    {
        UiDispatcher?.BeginInvoke(new Action(async () =>
        {
            var slot = _cameraManager.FindByDeviceId(deviceId);
            if (slot != null)
                await slot.HandleDisconnectAsync();
            await RefreshDevicesAsync();
            RefreshUi();
        }));
    }

    private void OnDevicesChanged()
    {
        if (_suppressDeviceRefresh) return;
        ScheduleDebouncedDeviceRefresh();
    }

    private void ScheduleDebouncedDeviceRefresh()
    {
        _deviceRefreshDebounce?.Cancel();
        _deviceRefreshDebounce?.Dispose();
        _deviceRefreshDebounce = new CancellationTokenSource();
        var token = _deviceRefreshDebounce.Token;

        UiDispatcher?.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Task.Delay(750, token).ConfigureAwait(true);
                if (token.IsCancellationRequested) return;
                await RefreshDevicesAsync().ConfigureAwait(true);
                await TryReconnectDisconnectedSlotsAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                /* superseded by a newer change */
            }
        }));
    }

    private async Task OnSlotStateChangedAsync(CameraSlotPipeline slot)
    {
        RefreshUi();
        if (!slot.IsDisconnected) return;
        await TryReconnectDisconnectedSlotsAsync();
    }

    private async Task TryReconnectDisconnectedSlotsAsync()
    {
        if (State.RunState == AppRunState.Idle) return;
        foreach (var slot in _pipelines)
        {
            if (!slot.IsDisconnected || string.IsNullOrEmpty(slot.AssignedDeviceId)) continue;
            if (!Devices.Any(d => d.Id == slot.AssignedDeviceId)) continue;
            await _reconnect.TryReconnectAsync(slot, Config, Devices);
        }
        RefreshUi();
    }

    public void SetLanguage(string code) { Language.Load(code); RefreshUi(); }

    public void SetLayout(int count)
    {
        var requested = Math.Clamp(count, 1, 4);
        if (requested != _layoutCount && BlockPreviewMutation(
                "LAYOUT_CHANGE_BLOCKED_PREVIEW_ACTIVE",
                "Stop preview before changing layout or camera selection."))
            return;

        var prev = _layoutCount;
        _layoutCount = requested;
        State.CameraLayout = _layoutCount;
        MultiCameraUsbWarning = _layoutCount >= 3
            ? Language["multiCamUsbBandwidthWarning"]
            : "";
        for (var i = 0; i < 4; i++)
            Panels[i].IsVisible = i < _layoutCount;
        if (_layoutCount > prev)
            AssignDistinctDevicesForLayout();
        RefreshUi();
    }

    public int CurrentLayoutCount => _layoutCount;

    public void SetOutputFolder(string path)
    {
        State.OutputFolder = path;
        OutputFolderDisplay = path;
        RefreshUi();
    }

    public void ApplyCaptureSettings(int width, int height, double fps)
    {
        Config.PreferredCaptureWidth = width;
        Config.PreferredCaptureHeight = height;
        Config.PreferFps = Math.Clamp(fps, 5, 60);
        _log.Info("settings", $"Capture prefs {CaptureResolutionPreset.ToLabel(width, height)} @ {Config.PreferFps:F0} fps");
    }

    public bool IsPreviewOrRecordingActive =>
        State.RunState is AppRunState.Previewing or AppRunState.Recording;

    public async Task<bool> StartPreviewAsync(
        Func<int, System.Windows.Controls.Image> getPreviewImage,
        bool userClicked,
        UiButtonStates uiBefore)
    {
        // Diagnostic dump at Start Preview: remembered bindings, DirectShow catalog, selected devices, resolve per slot
        try
        {
            var dumpLines = new List<string>();
            dumpLines.Add($"StartPreview diagnostics: {DateTime.Now:O}");
            dumpLines.Add("Remembered bindings:");
            try
            {
                var remembered = OpenCvDeviceSession.DumpRememberedBindings();
                foreach (var kv in remembered)
                    dumpLines.Add($"  {kv.Key} -> index={kv.Value.Index} name={kv.Value.DirectShowName} uri={kv.Value.DirectShowOpenUri}");
                if (remembered.Count == 0) dumpLines.Add("  (none)");
            }
            catch (Exception ex) { dumpLines.Add($"  DumpRememberedBindings failed: {ex.Message}"); }

            dumpLines.Add("DirectShow catalog:");
            try
            {
                var cat = OpenCvDirectShowIndexCatalog.GetSnapshot();
                foreach (var e in cat)
                    dumpLines.Add($"  idx={e.Index} name={e.Name} uri={e.Uri}");
                if (!cat.Any()) dumpLines.Add("  (none)");
            }
            catch (Exception ex) { dumpLines.Add($"  DirectShow catalog dump failed: {ex.Message}"); }

            dumpLines.Add("Selected UI devices:");
            for (var i = 0; i < State.CameraLayout; i++)
                dumpLines.Add($"  slot={i} selectedId={SelectedDeviceIds[i] ?? "(none)"}");

            dumpLines.Add("Resolve per slot:");
            for (var i = 0; i < State.CameraLayout; i++)
            {
                var id = SelectedDeviceIds[i];
                if (string.IsNullOrWhiteSpace(id)) { dumpLines.Add($"  slot={i} none selected"); continue; }
                try
                {
                var bind = OpenCvDeviceMapper.Resolve(id, Devices);
                dumpLines.Add($"  slot={i} id={id} -> index={bind.Index} name={bind.DirectShowName} uri={bind.DirectShowOpenUri} hasCapture={bind.HasCaptureTarget}");
                // nop: apply_patch trigger - no change to logic or diagnostics
                }
                catch (Exception ex) { dumpLines.Add($"  slot={i} resolve failed: {ex.Message}"); }
            }

            dumpLines.Add("VideoCapture open result per slot (best-effort):");
            // We cannot open captures here; but report remembered binding presence and intended index
            for (var i = 0; i < State.CameraLayout; i++)
            {
                var id = SelectedDeviceIds[i];
                if (string.IsNullOrWhiteSpace(id)) { dumpLines.Add($"  slot={i} none selected"); continue; }
                try
                {
                    if (OpenCvDeviceSession.TryGetRememberedBinding(id, out var b))
                        dumpLines.Add($"  slot={i} id={id} remembered index={b.Index} name={b.DirectShowName} uri={b.DirectShowOpenUri}");
                    else
                        dumpLines.Add($"  slot={i} id={id} no remembered binding");
                }
                catch (Exception ex) { dumpLines.Add($"  slot={i} remembered lookup failed: {ex.Message}"); }
            }

            try { DeviceMappingDebugLogger.WriteMappingLog("start_preview_diagnostics.txt", () => string.Join(Environment.NewLine, dumpLines)); } catch { }
        }
        catch { }

        var lifecycleLockAcquired = false;
        if (PreviewStartInProgress || State.RunState is AppRunState.Previewing or AppRunState.Recording)
        {
            AppDiagnosticLogger.Runtime("REENTRANT_START_PREVIEW_BLOCKED");
            return false;
        }

        if (!userClicked || !_privacy.CanStartPreview(State.RunState, userClicked))
            return false;

        if (!HasSelectedDeviceForActiveLayout())
        {
            StatusDisplay = Language["selectDevice"];
            _log.Info("preview", "Start preview skipped: no camera selected");
            RefreshUi();
            return false;
        }

        if (HasDuplicateDeviceSelection())
        {
            StatusDisplay = GetDuplicateDeviceWarning() ?? Language["duplicateDeviceWarning"];
            _log.Info("preview", "Start preview skipped: duplicate device on multiple slots");
            AppDiagnosticLogger.Runtime("Start Preview blocked: duplicate device selection");
            RefreshUi();
            return false;
        }

        if (HasMissingDeviceInActiveLayout())
        {
            StatusDisplay = Language["previewMissingDevice"];
            _log.Info("preview", "Start preview skipped: selected camera unavailable");
            RefreshUi();
            return false;
        }

        if (!_previewLifecycleGate.Wait(0))
        {
            AppDiagnosticLogger.Runtime("REENTRANT_START_PREVIEW_BLOCKED");
            return false;
        }

        lifecycleLockAcquired = true;
        AppDiagnosticLogger.Runtime("START_PREVIEW_LOCK_ACQUIRED");

        using var startupLog = AppDiagnosticLogger.BeginPreviewStartup();
        using var previewTrace = PreviewStartTrace.Begin(
            State.CameraLayout,
            Devices.Count,
            Config.PreferredCaptureWidth,
            Config.PreferredCaptureHeight,
            Config.PreferFps,
            Devices,
            SelectedDeviceIds);

        var previewFrameSession = BeginNewPreviewFrameSession();
        ClearPreviewImageSources(getPreviewImage);
        previewTrace.Marker($"PREVIEW_GENERATION_CREATED generation={previewFrameSession}");
        AppDiagnosticLogger.Runtime($"PREVIEW_GENERATION_CREATED generation={previewFrameSession}");

        if (userClicked)
        {
            _activePreviewStartClickedLocal = DateTime.Now;
            _previewStopRequested = false;
            _activePreviewUiBefore = uiBefore;
            _hasActivePreviewUiBefore = true;
            _activePreviewSessionId = Guid.NewGuid().ToString("N")[..8];
            previewTrace.Marker($"PREVIEW_SESSION_CREATED id={_activePreviewSessionId} generation={previewFrameSession}");
            AppDiagnosticLogger.Runtime($"PREVIEW_SESSION_CREATED id={_activePreviewSessionId} generation={previewFrameSession}");
            var stamp = DateTime.Now;
            var file = $"preview_session_{stamp:yyyyMMdd_HHmmss}_{stamp:fff}.txt";
            var dir = PathHelper.LogsFolder();
            var path = System.IO.Path.Combine(dir, file);

            lock (_sessionLogGate)
            {
                _activePreviewSessionLog?.Dispose();
                _activePreviewSessionLog = null;
            }

            LogRetention.PruneLatestFiles(dir, "preview_session_*.*", 20);
            try
            {
                    var headerSb = new StringBuilder()
                        .AppendLine($"appVersion={Version.Display}")
                        .AppendLine($"pcTimestamp={stamp:O}")
                        .AppendLine($"previewSessionId={_activePreviewSessionId}")
                        .AppendLine($"previewGeneration={previewFrameSession}")
                        .AppendLine($"layout={State.CameraLayout}")
                        .AppendLine($"preset={CaptureResolutionPreset.ToLabel(Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)}")
                        .AppendLine($"fps={Config.PreferFps:F0}")
                        .AppendLine(
                            $"uiBefore=startPreview={uiBefore.StartPreviewEnabled} stopPreview={uiBefore.StopPreviewEnabled} startRecording={uiBefore.StartRecordingEnabled} stopRecording={uiBefore.StopRecordingEnabled}")
                        .AppendLine($"duplicateDeviceCheck={(HasDuplicateDeviceSelection() ? "FAIL" : "OK")}")
                        .AppendLine($"missingDeviceCheck={(HasMissingDeviceInActiveLayout() ? "FAIL" : "OK")}");

                    for (var i = 0; i < State.CameraLayout; i++)
                    {
                        if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
                        var dev = Devices.FirstOrDefault(d => d.Id == SelectedDeviceIds[i]);
                        headerSb.AppendLine(
                            $"slotBefore=cam{i + 1} previewSlotState={_pipelines[i].PreviewSlotState} status={_pipelines[i].Status} selectedDeviceId={SelectedDeviceIds[i]} selectedDeviceName=\"{dev?.DisplayName ?? "?"}\"");
                    }

                    headerSb.AppendLine("deviceId/details are finalized after camera open (see per-slot section below).");
                    var header = headerSb.ToString();

                _activePreviewSessionLog = new AsyncBoundedTextFileLogger(
                    path,
                    header,
                    maxBytes: 2 * 1024 * 1024);
            }
            catch
            {
                // ignore logging failures
            }
        }
        AppDiagnosticLogger.Runtime("Start Preview clicked");
        startupLog.Line($"layout={State.CameraLayout}");
        startupLog.Line($"preset={CaptureResolutionPreset.ToLabel(Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)}@{Config.PreferFps:F0}");

        previewTrace.BeginValidateSelectedDevices();
        PreviewStartInProgress = true;
        var layoutIds = SelectedDeviceIds.Take(State.CameraLayout).ToList();
        var requiredCameras = layoutIds.Count(id => !string.IsNullOrEmpty(id));
        StatusDisplay = requiredCameras > 1
            ? string.Format(Language["openingCameras"], requiredCameras)
            : Language["openingCamera"];
        PreviewStartupStatus = Language["openingCameraDriver"];
        MultiCameraUsbWarning = State.CameraLayout >= 3
            ? Language["multiCamUsbBandwidthWarning"]
            : "";
        ClearPreviewOpenProgress();
        _previewOpenCts?.Cancel();
        _previewOpenCts?.Dispose();
        _previewOpenCts = new CancellationTokenSource();
        var previewOpenToken = _previewOpenCts.Token;
        RefreshUi();
        previewTrace.EndValidateSelectedDevices();

        startupLog.Line($"duplicateValidation={(HasDuplicateDeviceSelection() ? "FAIL" : "OK")}");
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
            var dev = Devices.FirstOrDefault(d => d.Id == SelectedDeviceIds[i]);
            startupLog.Line($"slot=cam{i + 1} device=\"{dev?.DisplayName ?? "?"}\" id={SelectedDeviceIds[i]}");
        }

        try
        {
        await CloseInactiveLayoutSlotsAsync().ConfigureAwait(true);
        // Ensure any stale OpenCV device runtime claims (used indices/names/uris) are cleared
        // before rebuilding the DirectShow catalog and mapping selected devices. Preserve
        // remembered bindings cache so duplicate same-name USB cameras remain distinct.
        OpenCvDeviceSession.ClearActiveClaims();
        previewTrace.BeginCatalogRebuild();
        OpenCvDirectShowIndexCatalog.RebuildForSelected(Devices, layoutIds);
        previewTrace.EndCatalogRebuild();

        var useSequential = CaptureResolutionHelper.UseSequentialPreviewOpen(
            State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
        previewTrace.SetOpenMode(useSequential
            ? "sequential per slot (1080p multi-cam)"
            : "parallel per selected slot");
        List<CameraSlotPipeline> active;
        try
        {
            active = useSequential
                ? await OpenActiveSlotsWithIncrementalPreviewAsync(
                    getPreviewImage, startupLog, previewTrace, previewOpenToken, previewFrameSession).ConfigureAwait(true)
                : await OpenSelectedSlotsParallelAsync(getPreviewImage, startupLog, previewTrace, previewOpenToken, previewFrameSession)
                    .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("preview", "Slot open failed", ex);
            AppDiagnosticLogger.Failure("preview", "Slot open failed", ex);
            active = [];
        }

        var required = requiredCameras;
        if (previewOpenToken.IsCancellationRequested || _previewStopRequested)
        {
            foreach (var slot in active)
                await ReleaseSlotCameraAsync(slot.SlotIndex).ConfigureAwait(true);
            State.RunState = AppRunState.Idle;
            StatusDisplay = Language["previewInactive"];
            PreviewStartupStatus = "";
            previewTrace.Complete(0, required);
            RefreshUi();
            return false;
        }

        await WaitForFirstFrameTraceAsync(active, previewTrace, 2500).ConfigureAwait(true);
        if (previewOpenToken.IsCancellationRequested || _previewStopRequested)
        {
            foreach (var slot in active)
                await ReleaseSlotCameraAsync(slot.SlotIndex).ConfigureAwait(true);
            State.RunState = AppRunState.Idle;
            StatusDisplay = Language["previewInactive"];
            PreviewStartupStatus = "";
            previewTrace.Complete(0, required);
            RefreshUi();
            return false;
        }

        if (active.Count == 0)
        {
            for (var i = 0; i < State.CameraLayout; i++)
            {
                if (_pipelines[i].Status is "Ready" or "Previewing")
                    await ReleaseSlotCameraAsync(i).ConfigureAwait(true);
            }

            State.RunState = AppRunState.Idle;
            if (HasCameraAccessDeniedError())
            {
                StatusDisplay = Language["cameraAccessBlocked"];
                CameraAccessDenied?.Invoke();
            }
            else
                StatusDisplay = Language["previewFailedAllNoCameras"];
            LogPreviewOpenFailures(active, required, startupLog);
            PreviewStartupStatus = "";
            previewTrace.Complete(0, required);
            if (userClicked)
            {
                var snap = previewTrace.Snapshot();
                TryWritePreviewSessionSummary(snap, stoppedByUser: _previewStopRequested);
                lock (_sessionLogGate)
                {
                    _activePreviewSessionLog?.Dispose();
                    _activePreviewSessionLog = null;
                }
            }
            RefreshUi();
            return false;
        }

        State.RunState = AppRunState.Previewing;
        StatusDisplay = active.Count < required
            ? string.Format(Language["previewPartial"], active.Count, required)
            : Language["previewing"];
        PreviewStartupStatus = "";
        startupLog.Line($"result=opened {active.Count}/{required}");
        _log.Info("preview", $"Preview started ({active.Count}/{required} camera(s))");
        AppDiagnosticLogger.Runtime($"Preview started {active.Count}/{required} camera(s)");
        _performanceMonitor.StartIfNeeded();
        previewTrace.Complete(active.Count, required);
        if (userClicked)
        {
            var snap = previewTrace.Snapshot();
            TryWritePreviewSessionSummary(snap, stoppedByUser: _previewStopRequested);
        }
        RefreshUi();
        return true;
        }
        catch (Exception ex)
        {
            _log.Error("preview", "Start preview failed", ex);
            AppDiagnosticLogger.Failure("preview", "Start preview failed", ex);
            State.RunState = AppRunState.Idle;
            StatusDisplay = Language["previewFailedAllNoCameras"];
            PreviewStartupStatus = "";
            previewTrace.Complete(0, requiredCameras);
            if (userClicked)
            {
                var snap = previewTrace.Snapshot();
                TryWritePreviewSessionSummary(snap, stoppedByUser: _previewStopRequested);
                lock (_sessionLogGate)
                {
                    _activePreviewSessionLog?.Dispose();
                    _activePreviewSessionLog = null;
                }
            }
            return false;
        }
        finally
        {
            PreviewStartInProgress = false;
            _previewStopRequested = false;
            _hasActivePreviewUiBefore = false;
            if (lifecycleLockAcquired)
            {
                AppDiagnosticLogger.Runtime("START_PREVIEW_LOCK_RELEASED");
                _previewLifecycleGate.Release();
            }
            RefreshUi();
        }
    }

    private void TryWritePreviewSessionSummary(PreviewStartTraceSession.PreviewStartTraceSnapshot snap, bool stoppedByUser)
    {
        if (_activePreviewSessionLog == null) return;

        var result =
            stoppedByUser
                ? "stoppedByUser"
                : snap.OpenedCount <= 0 && snap.RequiredCount > 0
                    ? "allFailed"
                    : snap.OpenedCount >= snap.RequiredCount && snap.RequiredCount > 0
                        ? "allReady"
                        : "partialReady";

        _activePreviewSessionLog.Line($"previewStatusResult={result}");
        _activePreviewSessionLog.Line($"openedCount={snap.OpenedCount} requiredCount={snap.RequiredCount}");
        _activePreviewSessionLog.Line($"totalTimeMs={snap.TotalPreviewMs}");
        _activePreviewSessionLog.Line(
            $"totalTimeToFirstVisibleFrameRenderedMs={snap.FirstVisibleFrameRenderedMs?.ToString() ?? "-"}");
        _activePreviewSessionLog.Line(
            $"totalTimeUntilAllSuccessfulVisibleMs={snap.AllSuccessfulVisibleMs?.ToString() ?? "-"}");
        _activePreviewSessionLog.Line($"openMode={snap.OpenMode}");

        var failedSlots = snap.Slots.Where(s => s.OpenFailed).OrderBy(s => s.SlotIndex).ToList();
        _activePreviewSessionLog.Line($"failedSlots={failedSlots.Count}");

        foreach (var s in snap.Slots.OrderBy(x => x.SlotIndex))
        {
            if (string.IsNullOrWhiteSpace(s.SelectedDeviceId) && string.IsNullOrWhiteSpace(s.OpenedDeviceId))
                continue;

            _activePreviewSessionLog.Line(
                $"slot=cam{s.SlotIndex + 1} selectedDeviceId={s.SelectedDeviceId ?? "?"} selectedDeviceName=\"{s.SelectedDeviceName ?? "?"}\" " +
                $"openedDeviceId={s.OpenedDeviceId ?? "-"} openedDeviceName=\"{s.OpenedDeviceName ?? "-"}\" openedPath=\"{s.OpenedPath ?? "-"}\" dshowIndex={s.OpenedDshowIndex?.ToString() ?? "-"} " +
                $"openStartMs={s.OpenStartMs?.ToString() ?? "-"} openEndMs={s.OpenEndMs?.ToString() ?? "-"} openDurationMs={s.OpenDurationMs?.ToString() ?? "-"} " +
                $"firstFrameReceivedMs={s.FirstFrameReceivedMs?.ToString() ?? "-"} firstFrameRenderedMs={s.FirstFrameRenderedMs?.ToString() ?? "-"} " +
                $"openFailed={s.OpenFailed} failureCategory={s.FailureCategory ?? "-"} failureReason=\"{(s.FailureMessage ?? "-").Replace('\"','\'')}\" " +
                $"otherCamerasContinued={s.OtherCamerasContinued}");

            if (s.OpenFailed)
            {
                if (Enum.TryParse<PreviewSlotFailureCategory>(s.FailureCategory ?? "", out var cat))
                {
                    var phase = PreviewSlotFailureHelper.ToFailurePhase(cat);
                    _activePreviewSessionLog.Line($"slotFailurePhase=cam{s.SlotIndex + 1}:{phase}");
                }
            }
        }
    }

    private static async Task WaitForFirstFrameTraceAsync(
        IReadOnlyList<CameraSlotPipeline> active,
        PreviewStartTraceSession trace,
        int maxWaitMs)
    {
        if (active.Count == 0) return;
        var deadline = Environment.TickCount64 + maxWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (active.All(s => trace.HasFirstFrameReceived(s.SlotIndex)))
                return;
            await Task.Delay(40).ConfigureAwait(true);
        }
    }

    private int PreviewOpenTimeoutSeconds() => PreviewSlotFailureHelper.CameraOpenTimeoutSeconds;

    private async Task<bool> WaitForSlotFirstFrameAsync(
        int slotIndex,
        int timeoutMs,
        PreviewStartTraceSession? previewTrace = null)
    {
        var slot = _pipelines[slotIndex];
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (_latestPreviewFrame[slotIndex] != null
                || previewTrace?.HasFirstFrameReceived(slotIndex) == true
                || slot.CaptureFrameCount > 0)
                return true;
            if (slot.PreviewSlotState is PreviewSlotStateKind.FailedUnsupportedPreset
                or PreviewSlotStateKind.FailedDeviceOpen)
                return false;
            await Task.Delay(40).ConfigureAwait(true);
        }

        return _latestPreviewFrame[slotIndex] != null
               || previewTrace?.HasFirstFrameReceived(slotIndex) == true
               || slot.CaptureFrameCount > 0;
    }

    private async Task MarkSlotPreviewFailedAsync(
        int slotIndex,
        PreviewSlotFailureCategory category,
        string detail,
        PreviewStartTraceSession? previewTrace,
        PreviewStartupLogSession? startupLog,
        long elapsedMs,
        string? actualResolution = null,
        bool otherCamerasContinued = false)
    {
        var slot = _pipelines[slotIndex];
        var selectedDev = Devices.FirstOrDefault(d => d.Id == SelectedDeviceIds[slotIndex]);
        var overlay = PreviewSlotFailureHelper.BuildOverlayMessage(
            key => Language[key],
            slot.SlotName,
            Config.PreferredCaptureWidth,
            Config.PreferredCaptureHeight,
            Config.PreferFps,
            category,
            actualResolution);

        var slotState = PreviewSlotFailureHelper.ToSlotState(category);
        slot.SetPreviewSlotState(slotState);
        SetPreviewOpeningProgress(slotIndex, overlay);

        var preset = CaptureResolutionPreset.ToLabel(Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
        previewTrace?.SlotFailed(
            slotIndex,
            category.ToString(),
            string.IsNullOrWhiteSpace(detail) ? overlay.Replace('\n', ' ') : detail,
            SelectedDeviceIds[slotIndex],
            selectedDev?.DisplayName,
            actualResolution ?? slot.ResolutionText);

        CameraOpenFailureLog.Write(
            slotIndex,
            slot.SlotName,
            State.CameraLayout,
            preset,
            Config.PreferFps,
            selectedDev?.DisplayName,
            SelectedDeviceIds[slotIndex],
            PreviewSlotFailureHelper.ToFailurePhase(category),
            category,
            detail,
            elapsedMs,
            otherCamerasContinued);

        startupLog?.Line(
            $"cam{slotIndex + 1} openResult=FAIL category={category} reason={detail} preset={preset} elapsedMs={elapsedMs:F0}");

        AppDiagnosticLogger.PreviewSlotFailure(
            slotIndex,
            slot.SlotName,
            State.CameraLayout,
            preset,
            Config.PreferFps,
            category.ToString(),
            BuildPreviewFailureDetailWithUiContext(detail, slotIndex, otherCamerasContinued),
            SelectedDeviceIds[slotIndex],
            selectedDev?.DisplayName,
            elapsedMs);

        AppDiagnosticLogger.Runtime($"{slot.SlotName} preview failed ({category}): {detail}");

        _latestPreviewFrame[slotIndex] = null;
        Interlocked.Exchange(ref _previewUiScheduled[slotIndex], 0);

        try
        {
            await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Failure("preview", $"{slot.SlotName} release after failure", ex);
            CameraOpenFailureLog.Write(
                slotIndex, slot.SlotName, State.CameraLayout, preset, Config.PreferFps,
                selectedDev?.DisplayName, SelectedDeviceIds[slotIndex],
                CameraOpenFailurePhase.Release, category, ex.Message, elapsedMs, otherCamerasContinued, ex);
        }

        slot.SetPreviewSlotState(slotState);
        SetPreviewOpeningProgress(slotIndex, overlay);
        previewTrace?.SlotFailureHandled(slotIndex, otherCamerasContinued);
        previewTrace?.CameraOpenEnd(slotIndex, null, success: false);
        UiRefreshRequested?.Invoke();
    }

    private string BuildPreviewFailureDetailWithUiContext(string? baseDetail, int slotIndex, bool otherCamerasContinued)
    {
        var d = string.IsNullOrWhiteSpace(baseDetail) ? "-" : baseDetail.Trim();
        if (!_hasActivePreviewUiBefore)
            return $"{d} | uiBefore=<n/a> currentRunState={State.RunState} previewStartInProgress={PreviewStartInProgress} recoveryOtherCamerasContinued={otherCamerasContinued}";

        var ui = _activePreviewUiBefore;
        return
            $"{d} | uiBefore=startPreview={ui.StartPreviewEnabled} stopPreview={ui.StopPreviewEnabled} " +
            $"startRecording={ui.StartRecordingEnabled} stopRecording={ui.StopRecordingEnabled} " +
            $"currentRunState={State.RunState} previewStartInProgress={PreviewStartInProgress} " +
            $"recoveryOtherCamerasContinued={otherCamerasContinued}";
    }

    private string BuildRecordingFailureDetailWithUiContext(string? baseDetail)
    {
        var d = string.IsNullOrWhiteSpace(baseDetail) ? "-" : baseDetail.Trim();
        if (!_hasActiveRecordingUiBefore)
            return $"{d} | uiBefore=<n/a> currentRunState={State.RunState} ";

        var ui = _activeRecordingUiBefore;
        return
            $"{d} | uiBefore=startPreview={ui.StartPreviewEnabled} stopPreview={ui.StopPreviewEnabled} " +
            $"startRecording={ui.StartRecordingEnabled} stopRecording={ui.StopRecordingEnabled} " +
            $"currentRunState={State.RunState}";
    }

    private async Task<CameraSlotPipeline?> TryOpenSlotForPreviewAsync(
        int slotIndex,
        string deviceId,
        Func<int, System.Windows.Controls.Image> getPreviewImage,
        PreviewStartupLogSession? startupLog,
        PreviewStartTraceSession? previewTrace = null,
        CancellationToken cancelToken = default,
        int previewFrameSession = 0)
    {
        var slot = _pipelines[slotIndex];
        var openStart = DateTime.Now;
        var selectedDev = Devices.FirstOrDefault(d => d.Id == deviceId);
        previewTrace?.Marker($"SLOT_OPEN_TASK_STARTED cam{slotIndex + 1}");
        slot.SetPreviewSlotState(PreviewSlotStateKind.Opening);
        previewTrace?.CameraOpenStart(slotIndex, deviceId, selectedDev?.DisplayName);
        SetPreviewOpeningProgress(slotIndex, string.Format(Language["previewSlotOpening"], slot.SlotName), previewFrameSession);
        startupLog?.Line($"cam{slotIndex + 1} openStart={openStart:HH:mm:ss.fff} deviceId={deviceId} preset={CaptureResolutionPreset.ToLabel(Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)}");

        try
        {
            if (cancelToken.IsCancellationRequested)
                return null;

            if (!string.Equals(slot.AssignedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                && slot.Status is not "Idle")
                await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);

            using var openTimeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(PreviewSlotFailureHelper.CameraOpenTimeoutSeconds));
            using var slotTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancelToken, openTimeoutCts.Token);
            var ok = await slot.OpenAsync(deviceId, Config, Devices, SelectedDeviceIds, slotTimeout.Token)
                .ConfigureAwait(true);
            var openMs = (long)(DateTime.Now - openStart).TotalMilliseconds;

            if (cancelToken.IsCancellationRequested || _previewStopRequested)
            {
                await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
                previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=STOP_REQUESTED durationMs={openMs}");
                return null;
            }

            if (!ok || !string.Equals(slot.AssignedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                var category = PreviewSlotFailureHelper.ClassifyOpenFailure(
                    slot.LastError,
                    slot.CaptureResolutionMatched,
                    Config.PreferredCaptureWidth > 0);
                await MarkSlotPreviewFailedAsync(
                    slotIndex,
                    category,
                    slot.LastError ?? "open failed",
                    previewTrace,
                    startupLog,
                    openMs,
                    slot.ResolutionText,
                    otherCamerasContinued: true).ConfigureAwait(true);
                previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=OPEN_FAILED durationMs={openMs}");
                return null;
            }

            var requestedPreset = Config.PreferredCaptureWidth > 0 && Config.PreferredCaptureHeight > 0;
            if (requestedPreset && !slot.CaptureResolutionMatched)
            {
                await MarkSlotPreviewFailedAsync(
                    slotIndex,
                    PreviewSlotFailureCategory.UnsupportedPreset,
                    slot.LastError ?? "resolution mismatch",
                    previewTrace,
                    startupLog,
                    openMs,
                    slot.ResolutionText,
                    otherCamerasContinued: true).ConfigureAwait(true);
                previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=UNSUPPORTED_PRESET durationMs={openMs}");
                return null;
            }

            AttachPreviewHandlers([slot], getPreviewImage, previewTrace, previewFrameSession);
            await slot.StartPreviewAsync().ConfigureAwait(true);

            var gotFrame = await WaitForSlotFirstFrameAsync(
                slotIndex,
                PreviewSlotFailureHelper.FirstFrameTimeoutSeconds * 1000,
                previewTrace).ConfigureAwait(true);
            if (!gotFrame)
            {
                previewTrace?.Marker($"SLOT_FIRST_FRAME_RECOVERY_START cam{slotIndex + 1}");
                startupLog?.Line($"cam{slotIndex + 1} firstFrameTimeoutRecovery=start backend={(slot.UsesOpenCvRecording(Config) ? "OpenCV" : "WinRT")}");
                var recovered = await slot.RecoverPreviewAfterFirstFrameTimeoutAsync(Config).ConfigureAwait(true);
                if (recovered
                    && !cancelToken.IsCancellationRequested
                    && !_previewStopRequested
                    && string.Equals(slot.AssignedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _latestPreviewFrame[slotIndex] = null;
                    Interlocked.Exchange(ref _previewWarmupFramesRemaining[slotIndex], 0);
                    Interlocked.Exchange(ref _previewFirstValidLogged[slotIndex], 0);
                    Interlocked.Exchange(ref _previewFirstRenderedLogged[slotIndex], 0);
                    Interlocked.Exchange(ref _previewUiScheduled[slotIndex], 0);
                    AttachPreviewHandlers([slot], getPreviewImage, previewTrace, previewFrameSession);
                    gotFrame = await WaitForSlotFirstFrameAsync(
                        slotIndex,
                        PreviewSlotFailureHelper.FirstFrameTimeoutSeconds * 1000,
                        previewTrace).ConfigureAwait(true);
                    previewTrace?.Marker(
                        $"SLOT_FIRST_FRAME_RECOVERY_END cam{slotIndex + 1} result={(gotFrame ? "OK" : "TIMEOUT")} backend={(slot.UsesOpenCvRecording(Config) ? "OpenCV" : "WinRT")}");
                    startupLog?.Line(
                        $"cam{slotIndex + 1} firstFrameTimeoutRecovery result={(gotFrame ? "OK" : "TIMEOUT")} backend={(slot.UsesOpenCvRecording(Config) ? "OpenCV" : "WinRT")}");
                }
                else
                {
                    previewTrace?.Marker($"SLOT_FIRST_FRAME_RECOVERY_END cam{slotIndex + 1} result=SKIPPED");
                    startupLog?.Line($"cam{slotIndex + 1} firstFrameTimeoutRecovery result=SKIPPED");
                }
            }

            if (!gotFrame)
            {
                await MarkSlotPreviewFailedAsync(
                    slotIndex,
                    PreviewSlotFailureCategory.FirstFrameTimeout,
                    "first frame timeout",
                    previewTrace,
                    startupLog,
                    (long)(DateTime.Now - openStart).TotalMilliseconds,
                    slot.ResolutionText,
                    otherCamerasContinued: true).ConfigureAwait(true);
                previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=FIRST_FRAME_TIMEOUT durationMs={(long)(DateTime.Now - openStart).TotalMilliseconds}");
                return null;
            }

            openMs = (long)(DateTime.Now - openStart).TotalMilliseconds;
            slot.SetPreviewSlotState(PreviewSlotStateKind.PreviewReady);
            SetPreviewOpeningProgress(slotIndex, string.Format(Language["previewSlotReady"], slot.SlotName), previewFrameSession);
            startupLog?.Line(
                $"cam{slotIndex + 1} openResult=OK durationMs={openMs} resolution={slot.ResolutionText} backend={(slot.UsesOpenCvRecording(Config) ? "OpenCV" : "WinRT")}");
            _log.Info("camera",
                $"{slot.SlotName} preview-ready in {openMs}ms backend={(slot.UsesOpenCvRecording(Config) ? "OpenCV" : "WinRT")} mode={slot.ResolutionText}");
            previewTrace?.CameraOpenEnd(slotIndex, slot, success: true);
            previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=OK durationMs={openMs}");
            return slot;
        }
        catch (OperationCanceledException)
        {
            if (cancelToken.IsCancellationRequested)
                return null;
            slot.AbortPendingOpen();
            var openMs = (long)(DateTime.Now - openStart).TotalMilliseconds;
            await MarkSlotPreviewFailedAsync(
                slotIndex,
                PreviewSlotFailureCategory.Timeout,
                "open timeout",
                previewTrace,
                startupLog,
                openMs,
                otherCamerasContinued: true).ConfigureAwait(true);
            previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=CANCELLED_OR_TIMEOUT durationMs={openMs}");
            return null;
        }
        catch (Exception ex)
        {
            var openMs = (long)(DateTime.Now - openStart).TotalMilliseconds;
            _log.Error("camera", $"{slot.SlotName} open failed", ex);
            AppDiagnosticLogger.Failure("preview", $"{slot.SlotName} open failed", ex);
            await MarkSlotPreviewFailedAsync(
                slotIndex,
                PreviewSlotFailureCategory.Generic,
                ex.Message,
                previewTrace,
                startupLog,
                openMs,
                otherCamerasContinued: true).ConfigureAwait(true);
            previewTrace?.Marker($"SLOT_OPEN_TASK_COMPLETED cam{slotIndex + 1} result=ERROR durationMs={openMs}");
            return null;
        }
    }

    private async Task<List<CameraSlotPipeline>> OpenSelectedSlotsParallelAsync(
        Func<int, System.Windows.Controls.Image> getPreviewImage,
        PreviewStartupLogSession startupLog,
        PreviewStartTraceSession? previewTrace = null,
        CancellationToken cancelToken = default,
        int previewFrameSession = 0)
    {
        var layoutIds = SelectedDeviceIds.Take(State.CameraLayout).ToList();
        previewTrace?.BeginPrepareMultiCamera();
        await PrepareMultiCameraOpenAsync(layoutIds).ConfigureAwait(true);
        previewTrace?.EndPrepareMultiCamera();

        var plans = Enumerable.Range(0, State.CameraLayout)
            .Where(i => !string.IsNullOrEmpty(SelectedDeviceIds[i]))
            .Select(i => (slotIndex: i, deviceId: SelectedDeviceIds[i]!))
            .ToList();

        if (State.CameraLayout >= 3)
        {
            var openOrder = plans
                .OrderBy(p => DuplicateUsbCapturePolicy.GetOpenSortKey(Devices, layoutIds, p.deviceId))
                .ThenBy(p => p.slotIndex)
                .ToList();

            var opened = new List<CameraSlotPipeline>();
            for (var n = 0; n < openOrder.Count; n++)
            {
                if (cancelToken.IsCancellationRequested)
                    break;

                if (n > 0)
                {
                    var stagger = Math.Max(
                        CaptureResolutionHelper.MultiCameraStaggerMs(
                            State.CameraLayout,
                            Config.PreferredCaptureWidth,
                            Config.PreferredCaptureHeight),
                        0);
                    if (stagger > 0)
                        await Task.Delay(stagger, cancelToken).ConfigureAwait(true);
                }

                var p = openOrder[n];
                previewTrace?.Marker($"SLOT_OPEN_TASK_CREATED cam{p.slotIndex + 1}");
                _log.Info("camera", $"Sequential preview open cam{p.slotIndex + 1}");
                var openedSlot = await TryOpenSlotForPreviewAsync(
                    p.slotIndex,
                    p.deviceId,
                    getPreviewImage,
                    startupLog,
                    previewTrace,
                    cancelToken,
                    previewFrameSession).ConfigureAwait(true);
                if (openedSlot != null)
                    opened.Add(openedSlot);
            }

            return opened;
        }

        var tasks = new List<Task<CameraSlotPipeline?>>();
        foreach (var p in plans)
        {
            previewTrace?.Marker($"SLOT_OPEN_TASK_CREATED cam{p.slotIndex + 1}");
            tasks.Add(TryOpenSlotForPreviewAsync(
                p.slotIndex,
                p.deviceId,
                getPreviewImage,
                startupLog,
                previewTrace,
                cancelToken,
                previewFrameSession));
        }
        CameraSlotPipeline?[] results;
        try
        {
            results = await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("preview", "Task.WhenAll slot open fault", ex);
            AppDiagnosticLogger.Failure("preview", "Task.WhenAll slot open fault", ex);
            results = [];
        }

        return results.Where(s => s != null).Cast<CameraSlotPipeline>().ToList();
    }

    private void ClearPreviewOpenProgress()
    {
        for (var i = 0; i < Panels.Length; i++)
        {
            Panels[i].PreviewOpenProgress = "";
            _pipelines[i].ResetPreviewSlotState();
        }
    }

    private void SetPreviewOpeningProgress(int slotIndex, string message, int previewFrameSession = 0)
    {
        if (slotIndex < 0 || slotIndex >= Panels.Length) return;
        // Ensure UI updates happen on the UI thread.
        var action = new Action(() =>
        {
            if (previewFrameSession > 0
                && previewFrameSession != Volatile.Read(ref _previewSessionGeneration))
            {
                AppDiagnosticLogger.Runtime($"UI_CALLBACK_IGNORED_OLD_SESSION cam{slotIndex + 1} session={previewFrameSession}");
                return;
            }

            Panels[slotIndex].PreviewOpenProgress = message;
            PreviewStartupStatus = message;
            RefreshUi();
        });

        if (UiDispatcher != null)
        {
            if (UiDispatcher.CheckAccess())
                action();
            else
                UiDispatcher.BeginInvoke(action);
        }
        else
        {
            // Fallback to application dispatcher if available.
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
            }
            catch
            {
                // last resort: run inline (best-effort)
                action();
            }
        }
    }

    private void AttachPreviewHandlers(
        IEnumerable<CameraSlotPipeline> slots,
        Func<int, System.Windows.Controls.Image> getPreviewImage,
        PreviewStartTraceSession? previewTrace = null,
        int previewFrameSession = 0)
    {
        foreach (var slot in slots)
        {
            var img = getPreviewImage(slot.SlotIndex);
            var idx = slot.SlotIndex;
            var disp = UiDispatcher;
            var sessionGeneration = previewFrameSession > 0
                ? previewFrameSession
                : Volatile.Read(ref _previewSessionGeneration);
            if (disp == null) continue;

            if (_previewHandlers[idx] is { } existing)
                slot.PreviewFrameBitmap -= existing;

            void Handler(BitmapSource bmp)
            {
                try
                {
                    if (sessionGeneration != Volatile.Read(ref _previewSessionGeneration)
                        || _previewFrameSession[idx] != sessionGeneration)
                    {
                        previewTrace?.Marker($"OLD_CALLBACK_IGNORED cam{idx + 1} session={sessionGeneration}");
                        AppDiagnosticLogger.Runtime($"OLD_CALLBACK_IGNORED cam{idx + 1} session={sessionGeneration}");
                        return;
                    }

                    var remainingWarmup = Interlocked.CompareExchange(ref _previewWarmupFramesRemaining[idx], 0, 0);
                    if (remainingWarmup > 0)
                    {
                        Interlocked.Decrement(ref _previewWarmupFramesRemaining[idx]);
                        previewTrace?.Marker($"WARMUP_FRAME_DISCARDED cam{idx + 1} remaining={remainingWarmup - 1}");
                        AppDiagnosticLogger.Runtime($"WARMUP_FRAME_DISCARDED cam{idx + 1} remaining={remainingWarmup - 1}");
                        return;
                    }

                    if (Interlocked.CompareExchange(ref _previewFirstValidLogged[idx], 1, 0) == 0)
                    {
                        previewTrace?.RecordFirstFrameReceived(idx);
                        previewTrace?.Marker($"SLOT_FIRST_VALID_FRAME cam{idx + 1}");
                    }
                    _latestPreviewFrame[idx] = bmp;
                    if (Interlocked.CompareExchange(ref _previewUiScheduled[idx], 1, 0) != 0)
                        return;

                    // Use Application.Current.Dispatcher as fallback to ensure UI invocation is always marshaled.
                    var invokeDisp = disp ?? System.Windows.Application.Current?.Dispatcher;
                    if (invokeDisp != null)
                    {
                        invokeDisp.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                        {
                            if (sessionGeneration != Volatile.Read(ref _previewSessionGeneration)
                                || _previewFrameSession[idx] != sessionGeneration)
                            {
                                previewTrace?.Marker($"UI_CALLBACK_IGNORED_OLD_SESSION cam{idx + 1} session={sessionGeneration}");
                                AppDiagnosticLogger.Runtime($"UI_CALLBACK_IGNORED_OLD_SESSION cam{idx + 1} session={sessionGeneration}");
                                return;
                            }

                            try
                            {
                                FlushPreviewFrame(idx, img, sessionGeneration);
                                if (Interlocked.CompareExchange(ref _previewFirstRenderedLogged[idx], 1, 0) == 0)
                                {
                                    previewTrace?.RecordFirstFrameRendered(idx);
                                    previewTrace?.Marker($"SLOT_FIRST_RENDERED_FRAME cam{idx + 1}");
                                }
                            }
                            catch (Exception ex)
                            {
                                AppDiagnosticLogger.Failure("preview", $"Frame render cam{idx + 1}", ex);
                            }
                        }));
                    }
                    else
                    {
                        // Last resort: inline call (may throw if not on UI thread) but we try to avoid that earlier.
                        try
                        {
                            if (sessionGeneration != Volatile.Read(ref _previewSessionGeneration)
                                || _previewFrameSession[idx] != sessionGeneration)
                            {
                                previewTrace?.Marker($"UI_CALLBACK_IGNORED_OLD_SESSION cam{idx + 1} session={sessionGeneration}");
                                AppDiagnosticLogger.Runtime($"UI_CALLBACK_IGNORED_OLD_SESSION cam{idx + 1} session={sessionGeneration}");
                                return;
                            }

                            FlushPreviewFrame(idx, img, sessionGeneration);
                            if (Interlocked.CompareExchange(ref _previewFirstRenderedLogged[idx], 1, 0) == 0)
                            {
                                previewTrace?.RecordFirstFrameRendered(idx);
                                previewTrace?.Marker($"SLOT_FIRST_RENDERED_FRAME cam{idx + 1}");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppDiagnosticLogger.Failure("preview", $"Frame render cam{idx + 1}", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppDiagnosticLogger.Failure("preview", $"Frame handler cam{idx + 1}", ex);
                }
            }

            _previewHandlers[idx] = Handler;
            slot.PreviewFrameBitmap += Handler;
        }
    }

    private async Task ReapplyCaptureSettingsForSlotsAsync(IReadOnlyList<CameraSlotPipeline> active)
    {
        _cameraManager.SetConfig(Config);
        var stagger = CaptureResolutionHelper.MultiCameraStaggerMs(
            State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
        for (var i = 0; i < active.Count; i++)
        {
            if (i > 0 && stagger > 0)
                await Task.Delay(stagger).ConfigureAwait(true);
            await active[i].ReapplyCaptureSettingsAsync(Config).ConfigureAwait(true);
        }
    }

    private async Task<List<CameraSlotPipeline>> OpenActiveSlotsWithIncrementalPreviewAsync(
        Func<int, System.Windows.Controls.Image> getPreviewImage,
        PreviewStartupLogSession? startupLog = null,
        PreviewStartTraceSession? previewTrace = null,
        CancellationToken cancelToken = default,
        int previewFrameSession = 0)
    {
        var list = new List<CameraSlotPipeline>();
        var layoutIds = SelectedDeviceIds.Take(State.CameraLayout).ToList();
        var openOrder = Enumerable.Range(0, State.CameraLayout)
            .Where(i => !string.IsNullOrEmpty(SelectedDeviceIds[i]))
            .Select(i => (slotIndex: i, deviceId: SelectedDeviceIds[i]!))
            .ToList();

        previewTrace?.BeginPrepareMultiCamera();
        await PrepareMultiCameraOpenAsync(layoutIds).ConfigureAwait(true);
        previewTrace?.EndPrepareMultiCamera();

        for (var n = 0; n < openOrder.Count; n++)
        {
            if (cancelToken.IsCancellationRequested)
                break;

            var (slotIndex, id) = openOrder[n];
            if (n > 0)
            {
                var stagger = CaptureResolutionHelper.MultiCameraStaggerMs(
                    State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
                if (stagger > 0)
                    await Task.Delay(stagger, cancelToken).ConfigureAwait(true);
            }

            var extra = CaptureResolutionHelper.LateSlotExtraOpenDelayMs(
                slotIndex, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
            if (extra > 0)
                await Task.Delay(extra, cancelToken).ConfigureAwait(true);

            var opened = await TryOpenSlotForPreviewAsync(
                    slotIndex, id, getPreviewImage, startupLog, previewTrace, cancelToken, previewFrameSession)
                .ConfigureAwait(true);
            if (opened != null)
                list.Add(opened);
        }

        return list;
    }

    /// <summary>Opens and starts preview for layout slots that are not previewing yet (e.g. after switching to 3/4 cameras).</summary>
    public async Task ExtendPreviewForActiveLayoutAsync(Func<int, System.Windows.Controls.Image> getPreviewImage)
    {
        if (State.RunState != AppRunState.Previewing || HasDuplicateDeviceSelection())
            return;

        var timeoutSec = Math.Max(45, State.CameraLayout * 12);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        var layoutIds = SelectedDeviceIds.Take(State.CameraLayout).ToList();
        var perSlotTimeoutSec = CaptureResolutionHelper.IsFullHd(
            Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)
            ? 120
            : 35;

        var pending = Enumerable.Range(0, State.CameraLayout)
            .Where(i => !string.IsNullOrEmpty(SelectedDeviceIds[i]))
            .Where(i => _pipelines[i].Status is not ("Previewing" or "Recording"))
            .Select(i =>
            {
                var id = SelectedDeviceIds[i]!;
                var device = Devices.FirstOrDefault(d => d.Id == id);
                return (slotIndex: i, deviceId: id, enumIndex: device?.EnumerationIndex ?? i);
            })
            .OrderBy(x => DuplicateUsbCapturePolicy.GetOpenSortKey(Devices, layoutIds, x.deviceId))
            .ThenBy(x => x.slotIndex)
            .ToList();

        if (pending.Count == 0)
            return;

        await PrepareMultiCameraOpenAsync(layoutIds).ConfigureAwait(true);

        var opened = new List<CameraSlotPipeline>();
        for (var n = 0; n < pending.Count; n++)
        {
            var (slotIndex, id, enumIndex) = pending[n];
            var slot = _pipelines[slotIndex];
            try
            {
                if (n > 0)
                {
                    var stagger = CaptureResolutionHelper.MultiCameraStaggerMs(
                        State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
                    if (stagger > 0)
                        await Task.Delay(stagger).ConfigureAwait(true);
                }

                var extra = CaptureResolutionHelper.LateSlotExtraOpenDelayMs(
                    slotIndex, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
                if (extra > 0)
                    await Task.Delay(extra).ConfigureAwait(true);

                _log.Info("camera", $"{slot.SlotName} extend-preview opening enumeration [{enumIndex}]");
                using var slotTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(perSlotTimeoutSec));
                var ok = await slot.OpenAsync(id, Config, Devices, SelectedDeviceIds).WaitAsync(slotTimeout.Token).ConfigureAwait(true);
                if (!ok)
                    continue;

                var img = getPreviewImage(slotIndex);
                var idx = slotIndex;
                var disp = UiDispatcher;
                var sessionGeneration = Volatile.Read(ref _previewSessionGeneration);
                if (disp != null && _previewHandlers[idx] == null)
                {
                    void Handler(BitmapSource bmp)
                    {
                        if (sessionGeneration != Volatile.Read(ref _previewSessionGeneration))
                        {
                            AppDiagnosticLogger.Runtime($"OLD_FRAME_IGNORED cam{idx + 1} session={sessionGeneration}");
                            return;
                        }
                        _latestPreviewFrame[idx] = bmp;
                        if (Interlocked.CompareExchange(ref _previewUiScheduled[idx], 1, 0) != 0)
                            return;
                        disp.BeginInvoke(DispatcherPriority.Render, () => FlushPreviewFrame(idx, img, sessionGeneration));
                    }

                    _previewHandlers[idx] = Handler;
                    slot.PreviewFrameBitmap += Handler;
                }

                opened.Add(slot);
            }
            catch (OperationCanceledException)
            {
                _log.Error("camera", $"{slot.SlotName} extend preview open timed out");
            }
            catch (Exception ex)
            {
                _log.Error("camera", $"{slot.SlotName} extend preview open failed", ex);
            }
        }

        if (opened.Count == 0)
            return;

        if (State.CameraLayout >= 2 && Config.PreferredCaptureWidth > 0)
        {
            _cameraManager.SetConfig(Config);
            var stagger = CaptureResolutionHelper.MultiCameraStaggerMs(
                State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
            for (var i = 0; i < opened.Count; i++)
            {
                if (i > 0 && stagger > 0)
                    await Task.Delay(stagger).ConfigureAwait(true);
                await opened[i].ReapplyCaptureSettingsAsync(Config).ConfigureAwait(true);
            }
        }

        foreach (var slot in opened)
            await slot.StartPreviewAsync().ConfigureAwait(true);

        RefreshUi();
    }

    public async Task StopPreviewOnHiddenSlotsAsync() =>
        await CloseInactiveLayoutSlotsAsync().ConfigureAwait(true);

    public async Task StopPreviewAsync()
    {
        _previewStopRequested = true;
        AppDiagnosticLogger.Runtime("STOP_PREVIEW_CLICKED");
        StatusDisplay = "Stopping preview...";
        RefreshUi();
        try { _previewOpenCts?.Cancel(); } catch { /* best effort */ }
        AppDiagnosticLogger.Runtime("STOP_PREVIEW_CANCEL_REQUESTED");

        var lockAcquired = _previewLifecycleGate.Wait(0);
        if (!lockAcquired)
        {
            using var waitCancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await _previewLifecycleGate.WaitAsync(waitCancel.Token).ConfigureAwait(true);
                lockAcquired = true;
            }
            catch (OperationCanceledException)
            {
                AppDiagnosticLogger.Runtime("SLOT_TASK_TIMEOUT previewLifecycle");
                await ForceStopPreviewCleanupAsync().ConfigureAwait(true);
                return;
            }
        }

        AppDiagnosticLogger.Runtime("STOP_PREVIEW_LOCK_ACQUIRED");
        try
        {
        InvalidatePreviewFrameSession();
        var stopClickedLocal = DateTime.Now;
        var previewLog = _activePreviewSessionLog;
        if (previewLog != null)
        {
            previewLog.Line($"StopPreviewClickedLocal={stopClickedLocal:O}");
            if (_activePreviewStartClickedLocal != default)
            {
                var durMs = (stopClickedLocal - _activePreviewStartClickedLocal).TotalMilliseconds;
                previewLog.Line($"previewSessionDurationMs={durMs:F0}");
            }
        }

        AppDiagnosticLogger.Runtime("SLOT_STOP_REQUESTED all");
        previewLog?.Line("SLOT_STOP_REQUESTED all");

        if (State.IsRecording)
            await StopRecordingAsync();

        for (var i = 0; i < 4; i++)
        {
            if (_previewHandlers[i] is { } h)
            {
                _pipelines[i].PreviewFrameBitmap -= h;
                _previewHandlers[i] = null;
            }

            _latestPreviewFrame[i] = null;
            Interlocked.Exchange(ref _previewUiScheduled[i], 0);
        }

        try
        {
            var stopRecords = new System.Collections.Generic.List<(int SlotIndex, DateTime StartLocal, DateTime EndLocal)>();
            var stopGate = new object();
            await Task.WhenAll(_pipelines.Select(async p =>
            {
                var t0 = DateTime.Now;
                AppDiagnosticLogger.Runtime($"SLOT_STOP_REQUESTED cam{p.SlotIndex + 1}");
                try
                {
                    var stopTask = p.StopPreviewAsync();
                    var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(true);
                    if (completed == stopTask)
                        await stopTask.ConfigureAwait(true);
                    else
                    {
                        AppDiagnosticLogger.Runtime($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} stopPreview");
                        previewLog?.Line($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} stopPreview");
                    }
                }
                catch (Exception ex) { _log.Error("camera", $"{p.SlotName} stop preview", ex); }
                var t1 = DateTime.Now;
                AppDiagnosticLogger.Runtime($"SLOT_TASK_COMPLETED cam{p.SlotIndex + 1} stopPreview");
                lock (stopGate) stopRecords.Add((p.SlotIndex, t0, t1));
            })).ConfigureAwait(true);

            previewLog?.Line($"--- perSlot stopPreview ---");
            foreach (var r in stopRecords.OrderBy(x => x.SlotIndex))
            {
                var durMs = (r.EndLocal - r.StartLocal).TotalMilliseconds;
                previewLog?.Line($"slot=cam{r.SlotIndex + 1} stopPreviewStartLocal={r.StartLocal:O} stopPreviewEndLocal={r.EndLocal:O} durationMs={durMs:F0}");
            }
        }
        catch (Exception ex)
        {
            _log.Error("preview", "Stop preview batch failed", ex);
        }

        try
        {
            var releaseRecords = new System.Collections.Generic.List<(int SlotIndex, DateTime StartLocal, DateTime EndLocal)>();
            var releaseGate = new object();
            await Task.WhenAll(_pipelines.Select(async p =>
            {
                var t0 = DateTime.Now;
                try
                {
                    var closeTask = p.CloseAsync(clearSessionHint: true);
                    var completed = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(true);
                    if (completed == closeTask)
                        await closeTask.ConfigureAwait(true);
                    else
                    {
                        AppDiagnosticLogger.Runtime($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} close");
                        previewLog?.Line($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} close");
                    }
                }
                catch (Exception ex) { _log.Error("camera", $"{p.SlotName} close on stop", ex); }
                var t1 = DateTime.Now;
                AppDiagnosticLogger.Runtime($"SLOT_CAPTURE_RELEASED cam{p.SlotIndex + 1}");
                AppDiagnosticLogger.Runtime($"SLOT_TASK_COMPLETED cam{p.SlotIndex + 1} close");
                lock (releaseGate) releaseRecords.Add((p.SlotIndex, t0, t1));
            })).ConfigureAwait(true);

            previewLog?.Line($"--- perSlot release ---");
            foreach (var r in releaseRecords.OrderBy(x => x.SlotIndex))
            {
                var durMs = (r.EndLocal - r.StartLocal).TotalMilliseconds;
                previewLog?.Line($"slot=cam{r.SlotIndex + 1} closeStartLocal={r.StartLocal:O} closeEndLocal={r.EndLocal:O} durationMs={durMs:F0}");
            }

            if (Config.ReleaseCamerasOnStopPreview)
            {
                try
                {
                    OpenCvDeviceSession.Reset();
                }
                catch { /* best effort */ }
            }
        }
        catch (Exception ex)
        {
            _log.Error("preview", "Release cameras on stop failed", ex);
            AppDiagnosticLogger.Failure("preview", "Release cameras on stop failed", ex);
        }

        ClearPreviewOpenProgress();
        OpenCvDeviceSession.ClearActiveClaims();
        PreviewStartupStatus = "";
        State.RunState = AppRunState.Idle;
        StatusDisplay = Language["previewInactive"];
        _log.Info("preview", "Preview stopped; cameras released");
        AppDiagnosticLogger.Runtime("STOP_PREVIEW_COMPLETE");
        await StopPerformanceMonitorQuietlyAsync().ConfigureAwait(true);
        RefreshUi();
        }
        finally
        {
            AppDiagnosticLogger.Runtime("STOP_PREVIEW_LOCK_RELEASED");
            _previewLifecycleGate.Release();
        }
    }

    private async Task ForceStopPreviewCleanupAsync()
    {
        AppDiagnosticLogger.Runtime("STOP_PREVIEW_FORCE_RELEASED");
        _previewStopRequested = true;
        InvalidatePreviewFrameSession();

        for (var i = 0; i < 4; i++)
        {
            if (_previewHandlers[i] is { } h)
            {
                try { _pipelines[i].PreviewFrameBitmap -= h; } catch { /* best effort */ }
                _previewHandlers[i] = null;
                AppDiagnosticLogger.Runtime($"SLOT_FRAME_CALLBACK_UNSUBSCRIBED cam{i + 1}");
            }

            _latestPreviewFrame[i] = null;
            Interlocked.Exchange(ref _previewUiScheduled[i], 0);
        }

        await Task.WhenAll(_pipelines.Select(async p =>
        {
            AppDiagnosticLogger.Runtime($"SLOT_STOP_REQUESTED cam{p.SlotIndex + 1}");
            try
            {
                await Task.Run(() => p.AbortPendingOpen()).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
            }
            catch
            {
                AppDiagnosticLogger.Runtime($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} abortPendingOpen");
            }

            try
            {
                var stopTask = p.StopPreviewAsync();
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(true);
                if (completed == stopTask)
                    await stopTask.ConfigureAwait(true);
                else
                    AppDiagnosticLogger.Runtime($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} forceStopPreview");
            }
            catch (Exception ex)
            {
                _log.Error("camera", $"{p.SlotName} force stop preview", ex);
            }

            try
            {
                var closeTask = p.CloseAsync(clearSessionHint: true);
                var completed = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(true);
                if (completed == closeTask)
                    await closeTask.ConfigureAwait(true);
                else
                    AppDiagnosticLogger.Runtime($"SLOT_TASK_TIMEOUT cam{p.SlotIndex + 1} forceClose");
            }
            catch (Exception ex)
            {
                _log.Error("camera", $"{p.SlotName} force close", ex);
            }

            AppDiagnosticLogger.Runtime($"SLOT_TASK_COMPLETED cam{p.SlotIndex + 1} forceCleanup");
        })).ConfigureAwait(true);

        ClearPreviewOpenProgress();
        OpenCvDeviceSession.ClearActiveClaims();
        PreviewStartInProgress = false;
        PreviewStartupStatus = "";
        State.RunState = AppRunState.Idle;
        StatusDisplay = Language["previewInactive"];
        await StopPerformanceMonitorQuietlyAsync().ConfigureAwait(true);
        RefreshUi();
        AppDiagnosticLogger.Runtime("STOP_PREVIEW_COMPLETE");
        AppDiagnosticLogger.Runtime("STOP_PREVIEW_LOCK_RELEASED");
    }

    public void FinalizePreviewSessionLog(UiButtonStates uiAfter)
    {
        lock (_sessionLogGate)
        {
            if (_activePreviewSessionLog == null) return;
            _activePreviewSessionLog.Line(
                $"uiAfter=stopPreview startPreview={uiAfter.StartPreviewEnabled} stopPreview={uiAfter.StopPreviewEnabled} " +
                $"startRecording={uiAfter.StartRecordingEnabled} stopRecording={uiAfter.StopRecordingEnabled}");
            _activePreviewSessionLog.Line("previewSessionDurationMs=done");
            _activePreviewSessionLog?.Dispose();
            _activePreviewSessionLog = null;
        }
    }

    public IReadOnlyList<PreRecordSettingsChecker.MismatchLine>? GetPreRecordSettingsMismatch()
    {
        var layoutSlots = GetLayoutRecordingSlots();
        var active = layoutSlots.Where(s => s.Status is "Previewing" or "Recording").ToList();
        return PreRecordSettingsChecker.Analyze(Config, active);
    }

    public async Task ReapplyCaptureSettingsToActivePreviewAsync()
    {
        if (State.RunState != AppRunState.Previewing)
            return;

        _cameraManager.SetConfig(Config);
        var slots = GetLayoutRecordingSlots()
            .Where(s => s.Status is "Previewing" or "Ready" && !string.IsNullOrEmpty(s.AssignedDeviceId))
            .ToList();

        var staggerMs = CaptureResolutionHelper.MultiCameraStaggerMs(
            State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
        for (var i = 0; i < slots.Count; i++)
        {
            if (i > 0 && staggerMs > 0)
                await Task.Delay(staggerMs).ConfigureAwait(true);
            await slots[i].ReapplyCaptureSettingsAsync(Config).ConfigureAwait(true);
        }

        RefreshUi();
    }

    public void LogPreRecordMismatchChoice(bool continued, int cameraCount)
    {
        if (continued)
            _log.Info("recording",
                $"User chose to record anyway: capture settings mismatch on {cameraCount} camera(s)");
        else
            _log.Info("recording", "Recording cancelled: capture settings not applied to live preview");
    }

    public async Task StartRecordingAsync(bool userClicked, UiButtonStates uiBefore)
    {
        if (!userClicked || !_privacy.CanStartRecording(State.RunState, userClicked))
            return;

        Interlocked.Exchange(ref _recordingTimingFailureNotified, 0);

        if (!HasSelectedDeviceForActiveLayout())
        {
            StatusDisplay = Language["selectDevice"];
            RefreshUi();
            return;
        }

        if (HasDuplicateDeviceSelection())
        {
            StatusDisplay = GetDuplicateDeviceWarning() ?? Language["duplicateDeviceWarning"];
            RefreshUi();
            return;
        }

        var notReady = FirstNotReadyActiveSlot();
        if (notReady != null)
        {
            StatusDisplay = string.Format(Language["recordingSlotNotReady"], notReady.SlotName);
            AppDiagnosticLogger.Runtime($"Start Recording blocked: {notReady.SlotName} status={notReady.Status}");
            RefreshUi();
            return;
        }

        var layoutSlots = GetLayoutRecordingSlots();
        var previewing = layoutSlots.Where(s => s.Status is "Previewing" or "Recording").ToList();
        if (previewing.Count == 0)
        {
            StatusDisplay = Language["selectDevice"];
            RefreshUi();
            return;
        }

        // Preview/recording session debug log (app-folder only).
        if (userClicked)
        {
            _activeRecordingStartClickedLocal = DateTime.Now;
            _activeRecordingUiBefore = uiBefore;
            _hasActiveRecordingUiBefore = true;
            _activeRecordingSessionId = Guid.NewGuid().ToString("N")[..8];
            var stamp = DateTime.Now;
            var file = $"recording_session_{stamp:yyyyMMdd_HHmmss}_{stamp:fff}.txt";
            var dir = PathHelper.LogsFolder();
            var path = System.IO.Path.Combine(dir, file);

            lock (_sessionLogGate)
            {
                _activeRecordingSessionLog?.Dispose();
                _activeRecordingSessionLog = null;
            }

            LogRetention.PruneLatestFiles(dir, "recording_session_*.*", 20);
            try
            {
                var header = new StringBuilder()
                    .AppendLine($"appVersion={Version.Display}")
                    .AppendLine($"pcTimestamp={stamp:O}")
                    .AppendLine($"recordingSessionId={_activeRecordingSessionId}")
                    .AppendLine($"layout={State.CameraLayout}")
                    .AppendLine($"preset={CaptureResolutionPreset.ToLabel(Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)}")
                    .AppendLine($"fps={Config.PreferFps:F0}")
                    .AppendLine($"StartRecordingClickedLocal={_activeRecordingStartClickedLocal:O}")
                    .AppendLine($"uiBefore=startPreview={uiBefore.StartPreviewEnabled} stopPreview={uiBefore.StopPreviewEnabled} " +
                                $"startRecording={uiBefore.StartRecordingEnabled} stopRecording={uiBefore.StopRecordingEnabled}")
                    .ToString();

                _activeRecordingSessionLog = new AsyncBoundedTextFileLogger(
                    path,
                    header,
                    maxBytes: 2 * 1024 * 1024);
            }
            catch { }
        }

        AppDiagnosticLogger.Runtime("Start Recording clicked");
        StatusDisplay = "Preparing recording...";
        RefreshUi();

        try
        {
            var adaptiveFpsCap = OpenCvPreviewController.ComputeAdaptivePreviewFpsCap(
            Config.RecordingPreviewFpsMode, previewing.Count);
        foreach (var slot in previewing)
            slot.SetRecordingPreviewFpsCap(adaptiveFpsCap);

        if (Config.ReapplyFocusBeforeRecording || Config.ReapplyExposureBeforeRecording)
        {
            StatusDisplay = "Applying camera settings...";
            RefreshUi();
        }

        await _recordCoordinator.StartAllAsync(
                previewing,
                State.OutputFolder,
                State.SessionTitle).ConfigureAwait(true);

            State.ActiveSessionPath = _recording.Session.SessionPath;
            _activeRecordingSessionLog?.Line($"outputVideoSessionFolderPath={State.ActiveSessionPath}");
            _activeRecordingSessionLog?.Line($"activeCameraSlotsBeforeStart={previewing.Count}");
            foreach (var slot in previewing.OrderBy(s => s.SlotIndex))
            {
                var uniqueId = slot.HardwareId ?? SelectedDeviceIds[slot.SlotIndex] ?? "-";
                _activeRecordingSessionLog?.Line(
                    $"slot=cam{slot.SlotIndex + 1} deviceDisplayName=\"{slot.DeviceName}\" " +
                    $"deviceUniqueId={uniqueId} directShowPath=\"{slot.OpenCvDevicePathDescription}\"");
            }
            _recordingUiClock.Reset();
            _lifecycle.NotifyRecordingStartedByUser();
            State.RunState = AppRunState.Recording;
            StatusDisplay = Language["recording"];
            _log.Info("recording",
                $"Multi-camera recording started ({previewing.Count} cam, engine={(_recordCoordinator.UsesOpenCvRecording ? "opencv" : "windows_camera")})");
            AppDiagnosticLogger.Runtime($"Recording started {previewing.Count} camera(s) engine={(_recordCoordinator.UsesOpenCvRecording ? "opencv" : "windows_camera")}");
            _performanceMonitor.StartIfNeeded();
        }
        catch (RecordingStartupException ex)
        {
            _log.Error("recording", ex.Message, ex);
            AppDiagnosticLogger.Failure(
                "recording",
                BuildRecordingFailureDetailWithUiContext($"Startup failed {ex.SlotName}: {ex.Message}"),
                ex);
            AppDiagnosticLogger.Recording($"START_FAILED slot={ex.SlotName} kind={ex.Kind}");
            var recovered = await RecoverFromFailedRecordingStartAsync(layoutSlots);
            StatusDisplay = ex.Kind == RecordingStartupFailureKind.FirstFrameTimeout
                ? string.Format(Language["recordingFirstFrameTimeout"], ex.SlotName, 3)
                : string.Format(Language["recordingSlotFailed"], ex.SlotName);
            if (recovered == 0)
                StatusDisplay = Language["previewFailed"];
        }
        catch (Exception ex)
        {
            _log.Error("recording", "Start recording failed", ex);
            AppDiagnosticLogger.Failure("recording", BuildRecordingFailureDetailWithUiContext("Start recording failed"), ex);
            AppDiagnosticLogger.Recording($"START_FAILED error={ex.Message}");
            var recovered = await RecoverFromFailedRecordingStartAsync(layoutSlots);
            StatusDisplay = recovered > 0 ? Language["previewing"] : Language["previewFailed"];
        }

        if (userClicked)
        {
            var started = State.RunState == AppRunState.Recording;
            if (_activeRecordingSessionLog != null)
            {
                _activeRecordingSessionLog.Line($"StartRecordingResult={(started ? "success" : "partialFailureOrFailed")}");                
                if (!started)
                {
                    _activeRecordingSessionLog.Dispose();
                    _activeRecordingSessionLog = null;
                    _hasActiveRecordingUiBefore = false;
                }
            }
        }

        RefreshUi();
    }

    public async Task StopRecordingAsync()
    {
        var stopClickedLocal = DateTime.Now;
        var recordingLog = _activeRecordingSessionLog;
        if (recordingLog != null)
        {
            recordingLog.Line($"StopRecordingClickedLocal={stopClickedLocal:O}");
            if (_activeRecordingStartClickedLocal != default)
            {
                var durMs = (stopClickedLocal - _activeRecordingStartClickedLocal).TotalMilliseconds;
                recordingLog.Line($"recordingSessionDurationMs={durMs:F0}");
            }
            recordingLog.Line("--- perSlot recording stats (post-stop) ---");
        }

        var layoutSlots = GetLayoutRecordingSlots();
        var recording = layoutSlots.Where(s => s.Status == "Recording").ToList();
        var toStop = recording.Count > 0 ? recording : layoutSlots;

        StatusDisplay = "Stopping recording...";
        RefreshUi();

        var stopOk = true;
        try
        {
            await _recordCoordinator.StopAllAsync(toStop);
            StatusDisplay = "Saving video files...";
            RefreshUi();

            if (recordingLog != null)
            {
                var expectedSlots = toStop.Count;
                var writtenSlots = toStop.Count(s => (s.LastOpenCvRecordingStats?.FramesWritten ?? 0) > 0);
                recordingLog.Line($"toStopSlots={expectedSlots} writtenSlots={writtenSlots}");

                foreach (var slot in toStop.OrderBy(s => s.SlotIndex))
                {
                    var stats = slot.LastOpenCvRecordingStats;
                    if (stats == null)
                    {
                        recordingLog.Line($"slot=cam{slot.SlotIndex + 1} stats=null");
                        continue;
                    }

                    var fileExists = !string.IsNullOrWhiteSpace(stats.OutputFilePath) && File.Exists(stats.OutputFilePath);
                    var fileBytes = fileExists ? new FileInfo(stats.OutputFilePath).Length : 0L;

                    recordingLog.Line(
                        $"slot=cam{slot.SlotIndex + 1} deviceId={stats.DeviceId} deviceName=\"{stats.CameraDeviceName}\" " +
                        $"backend={stats.Backend} resolution={stats.SelectedResolution} requestedFps={stats.RequestedFps:F0} " +
                        $"framesCaptured={stats.FramesCaptured} framesWritten={stats.FramesWritten} " +
                        $"measuredCameraFps={stats.MeasuredCameraFps:F2} writerQueueDrops={stats.WriterQueueDrops} " +
                        $"writerDequeued={stats.WriterFramesDequeued} queueDepthMax={stats.WriterQueueDepthMax} queueFull={stats.WriterQueueFullCount} " +
                        $"avgWriteMs={stats.AverageVideoWriterWriteMs:F3} maxWriteMs={stats.MaxVideoWriterWriteMs:F3} " +
                        $"previewFpsCap={stats.RecordingPreviewFpsCap:F1} previewFramesRendered={stats.PreviewFramesRenderedDuringRecording} " +
                        $"duplicateFrames={stats.DuplicateFrames} placeholderFrames={stats.PlaceholderFrames} " +
                        $"captureIntervalMeanMs={stats.CaptureIntervalMeanMs:F2} minMs={stats.CaptureIntervalMinMs:F2} " +
                        $"maxMs={stats.CaptureIntervalMaxMs:F2} stdMs={stats.CaptureIntervalStdMs:F2} intervalCount={stats.CaptureIntervalCount} " +
                        $"firstFrameWrittenMonotonicSec={stats.FirstFrameMonotonicSeconds:F3} writerClosedMonotonicSec={stats.WriterClosedMonotonicSeconds:F3} " +
                        $"writerStartWallClockUtc={stats.StartWallClockUtc:O} writerStopWallClockUtc={stats.StopWallClockUtc:O} " +
                        $"writerFinalizeOk={(fileExists ? "yes" : "no")} fileBytes={fileBytes} metadataWritten={(fileExists && fileBytes > 0 ? "yes" : "no")} " +
                        $"scientificTimingStatus={stats.ScientificTimingStatus} scientificTimingMessage=\"{stats.ScientificTimingMessage}\"");
                }

                var result =
                    writtenSlots <= 0 ? "failed" :
                    writtenSlots >= expectedSlots ? "success" :
                    "partialFailure";
                recordingLog.Line($"finalRecordingResult={result}");
                recordingLog.Line($"recordingStoppedNormally={(stopOk ? "yes" : "no")}");
            }
        }
        catch (Exception ex)
        {
            stopOk = false;
            _log.Error("recording", "Stop recording failed", ex);
            AppDiagnosticLogger.Failure("recording", "Stop recording failed", ex);
            recordingLog?.Line($"stopRecordingExceptionType={ex.GetType().FullName} message=\"{ex.Message}\"");
        }
        finally
        {
            _lifecycle.NotifyRecordingStopped();

            if (_recordCoordinator.UsesWindowsCameraEngine)
            {
                foreach (var slot in toStop)
                {
                    try
                    {
                        await slot.RestoreOpenCvPreviewAsync(Config, Devices);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("recording", $"{slot.SlotName} restore preview after record failed", ex);
                    }
                }
            }

            var previewing = layoutSlots.Count(s => s.Status is "Previewing");
            State.RunState = previewing > 0 ? AppRunState.Previewing : AppRunState.Idle;
            StatusDisplay = stopOk ? "Recording completed." : Language["previewFailed"];
            RefreshUi();
            await Task.Delay(1200).ConfigureAwait(true);
            StatusDisplay = State.RunState == AppRunState.Previewing ? Language["previewing"] : Language["idle"];

            RefreshUi();

            // If the user stopped recording via Stop Preview (not the dedicated Stop Recording button),
            // we still want to close the recording session log.
            var previewingNow = State.RunState == AppRunState.Previewing;
            var startPreviewEnabled =
                !previewingNow
                && HasSelectedDeviceForActiveLayout()
                && !HasDuplicateDeviceSelection()
                && !HasMissingDeviceInActiveLayout()
                && State.RunState != AppRunState.Recording;
            var stopPreviewEnabled = previewingNow;
            var startRecordingEnabled = previewingNow && AllActiveSlotsPreviewReady();
            var stopRecordingEnabled = false;

            FinalizeRecordingSessionLog(new UiButtonStates(
                startPreviewEnabled,
                stopPreviewEnabled,
                startRecordingEnabled,
                stopRecordingEnabled));
        }
    }

    public void FinalizeRecordingSessionLog(UiButtonStates uiAfter)
    {
        lock (_sessionLogGate)
        {
            if (_activeRecordingSessionLog == null) return;
            _activeRecordingSessionLog.Line(
                $"uiAfter=stopRecording startPreview={uiAfter.StartPreviewEnabled} stopPreview={uiAfter.StopPreviewEnabled} " +
                $"startRecording={uiAfter.StartRecordingEnabled} stopRecording={uiAfter.StopRecordingEnabled}");
            _activeRecordingSessionLog?.Dispose();
            _activeRecordingSessionLog = null;
            _hasActiveRecordingUiBefore = false;
        }
    }

    private IReadOnlyList<CameraPerformanceSampleSource> GetPerformanceSampleSources()
    {
        var list = new List<CameraPerformanceSampleSource>();
        for (var i = 0; i < State.CameraLayout && i < _pipelines.Length; i++)
        {
            var slot = _pipelines[i];
            var active = slot.Status is "Previewing" or "Recording"
                         || slot.PreviewSlotState == PreviewSlotStateKind.PreviewReady;
            list.Add(new CameraPerformanceSampleSource
            {
                Slot = slot.SlotIndex + 1,
                Status = slot.Status,
                IsActive = active,
                PreviewFps = slot.FpsMonitor.AverageFps,
                FramesCapturedTotal = slot.CaptureFrameCount,
                FramesWrittenTotal = slot.CurrentOpenCvFramesWritten,
                WriterQueueDropsTotal = slot.CurrentOpenCvWriterQueueDrops,
                PreviewStalenessSeconds = null
            });
        }

        return list;
    }

    private async Task StopPerformanceMonitorQuietlyAsync()
    {
        try { await _performanceMonitor.StopAsync().ConfigureAwait(false); }
        catch { /* diagnostic monitor must never affect preview or recording */ }
    }

    private List<CameraSlotPipeline> GetLayoutRecordingSlots() =>
        MultiCameraRecordingCoordinator.GetLayoutSlots(
            _pipelines, State.CameraLayout, i => SelectedDeviceIds[i]);

    private async Task<int> RecoverFromFailedRecordingStartAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        _lifecycle.NotifyRecordingStopped();
        try
        {
            await _recordCoordinator.StopAllAsync(slots);
        }
        catch { /* best effort */ }

        if (_recordCoordinator.UsesWindowsCameraEngine)
        {
            foreach (var slot in slots)
            {
                try
                {
                    await slot.TeardownWinRtForRecordingAsync();
                    var id = SelectedDeviceIds[slot.SlotIndex];
                    if (!string.IsNullOrEmpty(id))
                        await slot.OpenAsync(id, Config, Devices, SelectedDeviceIds);
                }
                catch (Exception ex)
                {
                    _log.Error("recording", $"{slot.SlotName} recovery failed", ex);
                }
            }
        }

        var recovered = slots.Where(s => s.Status is "Previewing" or "Ready").ToList();
        foreach (var slot in recovered)
        {
            try
            {
                if (slot.Status != "Previewing")
                    await slot.StartPreviewAsync();
            }
            catch (Exception ex)
            {
                _log.Error("recording", $"{slot.SlotName} restart preview failed", ex);
            }
        }

        State.RunState = recovered.Count > 0 ? AppRunState.Previewing : AppRunState.Idle;
        return recovered.Count;
    }

    public async Task OnAppClosingAsync() =>
        await OnAppClosingAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

    public async Task OnAppClosingAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _appClosingCleanupStarted, 1) == 1)
            return;

        AppDiagnosticLogger.Runtime("APP_SHUTDOWN_CLEANUP_REQUESTED");
        try
        {
            try { _deviceRefreshDebounce?.Cancel(); } catch { }
            try { _previewOpenCts?.Cancel(); } catch { }

            var cleanupTask = RunShutdownCleanupCoreAsync();
            var completed = await Task.WhenAny(cleanupTask, Task.Delay(timeout)).ConfigureAwait(true);
            if (completed == cleanupTask)
            {
                await cleanupTask.ConfigureAwait(true);
                AppDiagnosticLogger.Runtime("APP_SHUTDOWN_CLEANUP_COMPLETED");
            }
            else
            {
                AppDiagnosticLogger.Runtime($"APP_SHUTDOWN_CLEANUP_TIMEOUT afterSeconds={timeout.TotalSeconds:0}");
                try { await ForceStopPreviewCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true); }
                catch (Exception ex) { _log.Warn("shutdown", $"Force cleanup timed out or failed: {ex.Message}"); }
            }
        }
        finally
        {
            try
            {
                _deviceRefreshDebounce?.Dispose();
                _deviceRefreshDebounce = null;
                _previewOpenCts?.Dispose();
                _previewOpenCts = null;
            }
            catch { }

            lock (_sessionLogGate)
            {
                _activePreviewSessionLog?.Dispose();
                _activePreviewSessionLog = null;
                _activeRecordingSessionLog?.Dispose();
                _activeRecordingSessionLog = null;
            }
        }
    }

    private async Task RunShutdownCleanupCoreAsync()
    {
        await StopPerformanceMonitorQuietlyAsync().ConfigureAwait(true);
        await _lifecycle.OnWindowClosingAsync(this, _cameraManager, _recording).ConfigureAwait(true);
        OpenCvDeviceSession.ClearActiveClaims();
    }

    public async Task OnMinimizedAsync() =>
        await _lifecycle.OnMinimizedAsync(this, _cameraManager);

    public Task OnRestoredAsync() => _lifecycle.OnRestoredAsync(this);

    public void UpdateRecordingTitle(Action<string> setTitle)
    {
        if (State.IsRecording)
            setTitle($"● {Language["recording"]} — MultiCamApp");
        else
            setTitle("MultiCamApp");
    }

    public void UpdateElapsed()
    {
        if (State.RunState != AppRunState.Recording) return;
        ElapsedDisplay = _recordingUiClock.Elapsed.ToString(@"hh\:mm\:ss");
        UiRefreshRequested?.Invoke();
    }

    /// <summary>Opens selected slots in Windows MediaDevice enumeration order (reduces USB contention).</summary>
    private async Task<List<CameraSlotPipeline>> OpenActiveSlotsInEnumerationOrderAsync(
        Action<int, string>? onSlotOpening = null)
    {
        var list = new List<CameraSlotPipeline>();
        var perSlotTimeoutSec = CaptureResolutionHelper.IsFullHd(
            Config.PreferredCaptureWidth, Config.PreferredCaptureHeight)
            ? 120
            : 10;

        var layoutIds = SelectedDeviceIds.Take(State.CameraLayout).ToList();
        var openOrder = Enumerable.Range(0, State.CameraLayout)
            .Where(i => !string.IsNullOrEmpty(SelectedDeviceIds[i]))
            .Select(i =>
            {
                var id = SelectedDeviceIds[i]!;
                var device = Devices.FirstOrDefault(d => d.Id == id);
                return (slotIndex: i, deviceId: id, enumIndex: device?.EnumerationIndex ?? i);
            })
            .OrderBy(x => DuplicateUsbCapturePolicy.GetOpenSortKey(Devices, layoutIds, x.deviceId))
            .ThenBy(x => x.slotIndex)
            .ToList();

        await PrepareMultiCameraOpenAsync(layoutIds).ConfigureAwait(true);

        for (var n = 0; n < openOrder.Count; n++)
        {
            var (slotIndex, id, enumIndex) = openOrder[n];
            var slot = _pipelines[slotIndex];
            try
            {
                if (n > 0)
                {
                    var stagger = CaptureResolutionHelper.MultiCameraStaggerMs(
                        State.CameraLayout, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
                    if (stagger > 0)
                        await Task.Delay(stagger).ConfigureAwait(true);
                }

                var extra = CaptureResolutionHelper.LateSlotExtraOpenDelayMs(
                    slotIndex, Config.PreferredCaptureWidth, Config.PreferredCaptureHeight);
                if (extra > 0)
                    await Task.Delay(extra).ConfigureAwait(true);

                if (!string.Equals(slot.AssignedDeviceId, id, StringComparison.OrdinalIgnoreCase)
                    && slot.Status is not "Idle")
                    await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);

                onSlotOpening?.Invoke(slotIndex, string.Format(Language["previewSlotOpening"], slot.SlotName));
                var openStart = DateTime.Now;
                _log.Info("camera", $"{slot.SlotName} opening enumeration [{enumIndex}]");
                using var slotTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(perSlotTimeoutSec));
                var ok = await slot.OpenAsync(id, Config, Devices, SelectedDeviceIds).WaitAsync(slotTimeout.Token).ConfigureAwait(true);
                if (ok && string.Equals(slot.AssignedDeviceId, id, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(slot);
                    var openMs = (DateTime.Now - openStart).TotalMilliseconds;
                    onSlotOpening?.Invoke(slotIndex, string.Format(Language["previewSlotReady"], slot.SlotName));
                    _log.Info("camera", $"{slot.SlotName} opened in {openMs:F0}ms: {slot.ResolutionText}");
                }
                else
                    await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _log.Error("camera", $"{slot.SlotName} open timed out");
                await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.Error("camera", $"{slot.SlotName} open failed", ex);
                await ReleaseSlotCameraAsync(slotIndex).ConfigureAwait(true);
            }
        }

        return list;
    }

    /// <summary>Probe-bind DirectShow indices once before sequential opens (avoids re-probing cameras already open).</summary>
    private async Task PrepareMultiCameraOpenAsync(IReadOnlyList<string?> layoutIds)
    {
        if (layoutIds.Count(id => !string.IsNullOrEmpty(id)) < 2)
            return;

        if (string.Equals(Config.PreviewEngine, "winrt", StringComparison.OrdinalIgnoreCase))
            return;

        OpenCvDuplicateUsbResolver.BindSelectedDevices(Devices, layoutIds);
        if (DuplicateUsbCapturePolicy.HasDuplicateUsbInSelection(Devices, layoutIds))
            await Task.Delay(200).ConfigureAwait(true);
    }

    private void LogPreviewOpenFailures(IReadOnlyList<CameraSlotPipeline> opened, int requiredSlots, PreviewStartupLogSession? startupLog = null)
    {
        var openedSet = opened.Select(s => s.SlotIndex).ToHashSet();
        _log.Info("preview",
            opened.Count == 0
                ? "Start preview failed: could not open any camera"
                : $"Start preview failed: opened {opened.Count}/{requiredSlots} camera(s)");

        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
            if (openedSet.Contains(i)) continue;
            var slot = _pipelines[i];
            var device = Devices.FirstOrDefault(d => d.Id == SelectedDeviceIds[i]);
            _log.Error("preview",
                $"{slot.SlotName} failed to open: device=\"{device?.DisplayName ?? SelectedDeviceIds[i]}\", error={slot.LastError ?? "unknown"}");
        }
    }

    public bool HasCameraAccessDeniedError()
    {
        for (var i = 0; i < State.CameraLayout; i++)
        {
            var err = _pipelines[i].LastError;
            if (_pipelines[i].CameraAccessDenied || CameraAccessHelper.IsAccessDeniedMessage(err))
                return true;
        }
        return false;
    }

    private List<CameraSlotPipeline> GetActiveSlots(bool requireWinRtCapture = false)
    {
        var list = new List<CameraSlotPipeline>();
        for (var i = 0; i < State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(SelectedDeviceIds[i])) continue;
            var slot = _pipelines[i];
            if (requireWinRtCapture)
            {
                if (slot.Capture != null)
                    list.Add(slot);
            }
            else if (slot.Status is "Ready" or "Previewing" or "Recording")
            {
                list.Add(slot);
            }
        }
        return list;
    }

    public void RefreshUi()
    {
        if (UiDispatcher != null && !UiDispatcher.CheckAccess())
        {
            UiDispatcher.BeginInvoke(new Action(() => UiRefreshRequested?.Invoke()));
            return;
        }

        UiRefreshRequested?.Invoke();
    }

    public void RefreshUiOnUiThread()
    {
        if (UiDispatcher != null && !UiDispatcher.CheckAccess())
        {
            UiDispatcher.BeginInvoke(new Action(() => UiRefreshRequested?.Invoke()));
            return;
        }

        UiRefreshRequested?.Invoke();
    }
}
