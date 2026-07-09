////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production pipeline from v1.1.7.
// Replaces the OpenCV-centered workflow with a MediaFoundation-based capture and recording path.
// Supports up to 4 independent camera slots; each slot owns its own CameraPipelineV2.

using MultiCamApp.Recording.Writers;
using MultiCamApp.Utils;
using System.Windows;
using System.Windows.Threading;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Multi-camera entry point for the VideoEngineV2 production backend (v1.1.7+).
/// Manages up to <see cref="VideoEngineV2Flags.MaxSlots"/> independent <see cref="CameraPipelineV2"/> instances,
/// one per camera slot (cam1–cam4).
/// </summary>
/// <remarks>
/// <para>Feature flags (all true by default from v1.1.7):</para>
/// <list type="bullet">
///   <item><see cref="VideoEngineV2Flags.Enabled"/> — master switch.</item>
///   <item><see cref="VideoEngineV2Flags.AllowCam1PreviewTest"/> — preview enabled for all slots.</item>
///   <item><see cref="VideoEngineV2Flags.UseAsDefaultPipeline"/> — V2 replaces legacy pipeline.</item>
///   <item><see cref="VideoEngineSettings.AllowCam1RecordingTest"/> — recording enabled for all slots.</item>
/// </list>
/// </remarks>
public sealed class VideoEngineV2 : IDisposable
{
    private static readonly int MaxSlots = VideoEngineV2Flags.MaxSlots;

    private readonly CameraDeviceManagerV2 _deviceManager = new();
    private readonly CameraPipelineV2?[] _pipelines = new CameraPipelineV2?[VideoEngineV2Flags.MaxSlots];
    private readonly V2CameraDeviceInfo?[] _selectedDevices = new V2CameraDeviceInfo?[VideoEngineV2Flags.MaxSlots];
    private readonly V2FormatSelectionResult?[] _formatResults = new V2FormatSelectionResult?[VideoEngineV2Flags.MaxSlots];
    // Prevents display/system sleep during recording (Windows Camera pattern).
    private readonly Windows.System.Display.DisplayRequest _displayRequest = new();
    private int _activeRecordingCount;
    private bool _disposed;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when the engine emits a full diagnostics snapshot.</summary>
    public event EventHandler<VideoEngineDiagnosticsSnapshot>? DiagnosticsAvailable;

    /// <summary>
    /// Raised on the UI thread after each preview frame is written for a specific slot.
    /// Arg is the slot index (0–3). Subscribe to update <c>Image.Source</c> for that slot.
    /// </summary>
    public event EventHandler<int>? SlotFrameRendered;

    /// <summary>
    /// Raised on the UI thread when a slot recovers from D3D11 failure by switching
    /// to a WPF preview bitmap. Arguments are slot index and replacement bitmap.
    /// </summary>
    public event Action<int, System.Windows.Media.Imaging.WriteableBitmap>? SlotFallenBackToWpf;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>True when V2 is enabled and preview is allowed.</summary>
    public bool IsActive => VideoEngineV2Flags.Enabled && VideoEngineV2Flags.AllowCam1PreviewTest;

    /// <summary>True when V2 is the primary production pipeline (replaces legacy).</summary>
    public bool IsDefaultPipeline => IsActive && VideoEngineV2Flags.UseAsDefaultPipeline;

    /// <summary>True when V2 recording is allowed on all slots.</summary>
    public bool IsRecordingTestActive => IsActive && VideoEngineSettings.AllowCam1RecordingTest;

    public VideoEngineBackend ActiveBackend =>
        IsActive ? VideoEngineBackend.MediaFoundation : VideoEngineBackend.Legacy;

    public IReadOnlyList<V2CameraDeviceInfo> DiscoveredDevices => _deviceManager.Devices;

    // ── Backward-compatible cam1 accessors (slot 0) ────────────────────────

    /// <summary>Backward-compatible: selected device for cam1 (slot 0).</summary>
    public V2CameraDeviceInfo? SelectedCam1Device => _selectedDevices[0];

    /// <summary>Backward-compatible: format result for cam1 (slot 0).</summary>
    public V2FormatSelectionResult? Cam1FormatResult => _formatResults[0];

    /// <summary>Backward-compatible: preview bitmap for cam1 (slot 0).</summary>
    public System.Windows.Media.Imaging.WriteableBitmap? Cam1PreviewBitmap => GetSlotPreviewBitmap(0);

    /// <summary>Backward-compatible: pipeline state for cam1 (slot 0).</summary>
    public CameraPipelineState PipelineState => GetSlotPipelineState(0);

    // ── Per-slot accessors ─────────────────────────────────────────────────

    public V2CameraDeviceInfo? GetSlotDevice(int slot) =>
        slot >= 0 && slot < MaxSlots ? _selectedDevices[slot] : null;

    public V2FormatSelectionResult? GetSlotFormatResult(int slot) =>
        slot >= 0 && slot < MaxSlots ? _formatResults[slot] : null;

    public System.Windows.Media.Imaging.WriteableBitmap? GetSlotPreviewBitmap(int slot) =>
        slot >= 0 && slot < MaxSlots ? _pipelines[slot]?.PreviewBitmap : null;

    /// <summary>
    /// Returns the D3D11 GPU preview element for a slot when the GPU renderer is active,
    /// or null when the WPF WriteableBitmap path is in use.
    /// Non-null means the caller should host this element in the preview area.
    /// </summary>
    public UIElement? GetSlotGpuPreviewElement(int slot) =>
        slot >= 0 && slot < MaxSlots ? _pipelines[slot]?.GpuPreviewElement : null;

    public CameraPipelineState GetSlotPipelineState(int slot) =>
        slot >= 0 && slot < MaxSlots ? (_pipelines[slot]?.State ?? CameraPipelineState.Idle) : CameraPipelineState.Idle;

    public PreviewRendererType GetSlotPreviewRenderer(int slot) =>
        slot >= 0 && slot < MaxSlots ? (_pipelines[slot]?.ActivePreviewRenderer ?? PreviewRendererType.Wpf) : PreviewRendererType.Wpf;

    public bool IsAnySlotActive =>
        _pipelines.Any(p => p?.State is CameraPipelineState.Previewing or CameraPipelineState.Recording
                                     or CameraPipelineState.StartingRecording or CameraPipelineState.StoppingRecording);

    public bool IsAnySlotRecording =>
        _pipelines.Any(p => p?.State is CameraPipelineState.Recording or CameraPipelineState.StartingRecording);

    // ── Initialise ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all camera devices (once). Must be called before any slot operation.
    /// Safe to call multiple times — re-enumerates if needed.
    /// </summary>
    public async Task EnumerateDevicesAsync(CancellationToken ct = default)
    {
        if (!IsActive) return;
        ThrowIfDisposed();
        await _deviceManager.EnumerateAsync(ct);
        var devices = _deviceManager.Devices;
        var deviceList = string.Join("; ", devices.Select(d =>
            $"[{d.EnumerationIndex}]\"{d.FriendlyName}\" kind={d.Kind} formats={d.SupportedFormats.Count} source={d.DiscoverySource}"));
        AppDiagnosticLogger.Runtime(
            $"V2_DEVICES enumerated count={devices.Count}" +
            (devices.Count > 0 ? $" devices=[{deviceList}]" : ""));
    }

    /// <summary>
    /// Backward-compatible: enumerate devices and select device for cam1 (slot 0).
    /// </summary>
    public async Task InitializeAsync(
        int cam1DeviceIndex = -1,
        string? cam1DeviceId = null,
        CancellationToken ct = default)
    {
        if (!IsActive) return;
        ThrowIfDisposed();
        await EnumerateDevicesAsync(ct);
        if (_deviceManager.Devices.Count == 0) { EmitDiagnostics(0); return; }
        await SelectSlotDeviceAsync(0, cam1DeviceId, cam1DeviceIndex, ct);
        EmitDiagnostics(0);
    }

    /// <summary>
    /// Selects the best available camera device for a slot. Call after <see cref="EnumerateDevicesAsync"/>.
    /// </summary>
    public Task SelectSlotDeviceAsync(int slot, string? preferredDeviceId, int fallbackIndex = 0,
                                      CancellationToken ct = default)
    {
        if (slot < 0 || slot >= MaxSlots) return Task.CompletedTask;
        if (_deviceManager.Devices.Count == 0) return Task.CompletedTask;

        var device = SelectDevice(preferredDeviceId, fallbackIndex,
            // Exclude devices already claimed by earlier slots
            _selectedDevices.Take(slot).Where(d => d != null).Select(d => d!.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase));

        _selectedDevices[slot] = device;

        var selector = new CameraFormatSelectorV2();
        _formatResults[slot] = selector.Select(
            device.SupportedFormats,
            new V2CaptureFormatRequest
            {
                PreferredWidth       = VideoEngineSettings.DefaultPreferredWidth,
                PreferredHeight      = VideoEngineSettings.DefaultPreferredHeight,
                PreferredFps         = VideoEngineSettings.DefaultPreferredFps,
                PreferredPixelFormat = VideoEngineSettings.DefaultPreferredPixelFormat,
            });

        AppDiagnosticLogger.Runtime(
            $"V2_SLOT_DEVICE slot={slot} selected=\"{device.FriendlyName}\" " +
            $"index={device.EnumerationIndex} formats={device.SupportedFormats.Count}");

        return Task.CompletedTask;
    }

    // ── Prepare preview pipeline for a slot ───────────────────────────────────

    /// <summary>
    /// Backward-compatible: initialises preview renderer and opens camera for cam1 (slot 0).
    /// </summary>
    public async Task PreparePreviewAsync(
        Dispatcher uiDispatcher,
        int previewWidth  = -1,
        int previewHeight = -1,
        CancellationToken ct = default)
        => await PrepareSlotPreviewAsync(0, uiDispatcher, previewWidth, previewHeight, ct);

    /// <summary>
    /// Initialises the preview renderer for <paramref name="slotIndex"/> and opens the camera device.
    /// No frames are delivered until <see cref="StartSlotPreviewAsync"/>.
    /// </summary>
    public async Task PrepareSlotPreviewAsync(
        int slotIndex,
        Dispatcher uiDispatcher,
        int previewWidth  = -1,
        int previewHeight = -1,
        CancellationToken ct = default)
    {
        if (!IsActive || slotIndex < 0 || slotIndex >= MaxSlots) return;
        ThrowIfDisposed();

        var device = _selectedDevices[slotIndex];
        if (device is null) return;

        int w = previewWidth  > 0 ? previewWidth  : VideoEngineSettings.PreviewWidth;
        int h = previewHeight > 0 ? previewHeight : VideoEngineSettings.PreviewHeight;

        _pipelines[slotIndex]?.Dispose();
        var pipeline = new CameraPipelineV2();
        _pipelines[slotIndex] = pipeline;

        var capturedSlot = slotIndex;
        pipeline.PipelineError   += (_, ex) => OnPipelineError(capturedSlot, ex);
        pipeline.FrameRendered   += (_, _)  => SlotFrameRendered?.Invoke(this, capturedSlot);
        pipeline.FallenBackToWpf += bitmap => SlotFallenBackToWpf?.Invoke(capturedSlot, bitmap);

        pipeline.Initialise(uiDispatcher, w, h);
        await pipeline.OpenAsync(device, ct: ct);
        EmitDiagnostics(slotIndex);
    }

    // ── Preview ────────────────────────────────────────────────────────────────

    /// <summary>Backward-compatible: starts preview for cam1 (slot 0).</summary>
    public async Task StartPreviewAsync(CancellationToken ct = default)
        => await StartSlotPreviewAsync(0, ct);

    public async Task StartSlotPreviewAsync(int slotIndex, CancellationToken ct = default)
    {
        if (!IsActive || slotIndex < 0 || slotIndex >= MaxSlots) return;
        ThrowIfDisposed();
        var pipeline = _pipelines[slotIndex];
        if (pipeline is null) return;
        await pipeline.StartPreviewAsync(ct);
        EmitDiagnostics(slotIndex);
    }

    /// <summary>Backward-compatible: stops preview for cam1 (slot 0).</summary>
    public async Task StopPreviewAsync(CancellationToken ct = default)
        => await StopSlotPreviewAsync(0, ct);

    public async Task StopSlotPreviewAsync(int slotIndex, CancellationToken ct = default)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots) return;
        var pipeline = _pipelines[slotIndex];
        if (pipeline is null) return;
        await pipeline.StopPreviewAsync(ct);
        EmitDiagnostics(slotIndex);
    }

    /// <summary>Stops preview for ALL active slots.</summary>
    public async Task StopAllSlotsPreviewAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < MaxSlots; i++)
        {
            if (_pipelines[i] is not null)
            {
                try { await StopSlotPreviewAsync(i, ct); }
                catch (Exception ex) { AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_SLOT_ERROR slot={i} {ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>Backward-compatible: starts recording for cam1 (slot 0).</summary>
    public async Task StartRecordingAsync(RecordingFileSet fileSet, CancellationToken ct = default)
        => await StartSlotRecordingAsync(0, fileSet, ct);

    public async Task StartSlotRecordingAsync(int slotIndex, RecordingFileSet fileSet, CancellationToken ct = default)
    {
        if (!IsRecordingTestActive || slotIndex < 0 || slotIndex >= MaxSlots) return;
        ThrowIfDisposed();
        var pipeline = _pipelines[slotIndex];
        if (pipeline is null) return;
        await pipeline.StartRecordingAsync(fileSet.TempVideoPath, fileSet.TimestampCsvPath, ct: ct);
        // Acquire display-keep-alive on first recording slot (prevents OS sleep mid-session)
        if (Interlocked.Increment(ref _activeRecordingCount) == 1)
            _displayRequest.RequestActive();
        EmitDiagnostics(slotIndex);
    }

    /// <summary>Backward-compatible: stops recording for cam1 (slot 0).</summary>
    public async Task<RecordingFinalizeResult> StopRecordingAsync(
        RecordingFileSet fileSet, CancellationToken ct = default)
        => await StopSlotRecordingAsync(0, fileSet, ct);

    public async Task<RecordingFinalizeResult> StopSlotRecordingAsync(
        int slotIndex, RecordingFileSet fileSet, CancellationToken ct = default)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots)
            return RecordingFinalizeResult.Failure("Invalid slot index.");

        var pipeline = _pipelines[slotIndex];
        if (pipeline is null || pipeline.State != CameraPipelineState.Recording)
            return RecordingFinalizeResult.Failure("No recording in progress.");

        await pipeline.StopRecordingAsync(ct);
        // Release display-keep-alive when the last recording slot finishes
        if (Interlocked.Decrement(ref _activeRecordingCount) <= 0)
        {
            _activeRecordingCount = 0;
            _displayRequest.RequestRelease();
        }
        var recordingElapsed = pipeline.RecordingElapsed; // stable after StopRecordingAsync

        bool renamed = false;
        string? renameError = null;
        if (File.Exists(fileSet.TempVideoPath))
        {
            // Sibling slots finalize concurrently (StopAllSlotsRecordingAsync), so the MF sink
            // backing this temp file may not have released its handle the instant StopRecordingAsync
            // returns. Retry a few times on IOException (file-in-use) before giving up; other
            // exceptions (e.g. permissions) fail immediately since retrying won't help.
            const int maxAttempts = 5;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(fileSet.FinalVideoPath))
                        File.Delete(fileSet.FinalVideoPath);
                    File.Move(fileSet.TempVideoPath, fileSet.FinalVideoPath);
                    fileSet.MarkFinalized();
                    renamed = true;
                    renameError = null;
                    break;
                }
                catch (IOException ex)
                {
                    renameError = ex.Message;
                    if (attempt < maxAttempts)
                        await Task.Delay(100 * attempt, ct);
                }
                catch (Exception ex) { renameError = ex.Message; break; }
            }
        }

        EmitDiagnostics(slotIndex);

        var health = pipeline.GetHealthSnapshot();
        bool hasEncoder = pipeline.ActiveEncoderBackend != EncoderBackendType.NotSelected;
        var expectedFrames = hasEncoder ? pipeline.EncoderFramesSubmittedSinceRecordingStart : 0;

        long? probedFrameCount = null;
        bool frameCountMismatch = false;
        if (renamed && expectedFrames > 0)
        {
            (probedFrameCount, frameCountMismatch) =
                await PostFinalizeFrameCheckAsync(fileSet.FinalVideoPath, expectedFrames, ct).ConfigureAwait(false);
        }

        return new RecordingFinalizeResult
        {
            Status              = renamed ? RecordingFinalizeStatus.Success : RecordingFinalizeStatus.RenameFailed,
            FinalVideoPath      = fileSet.FinalVideoPath,
            // FramesWritten = preview-inclusive health monitor counter (preview start → stop).
            FramesWritten       = hasEncoder ? health.FramesDelivered : 0,
            HardwareEncoderUsed = pipeline.IsHardwareEncoderActive,
            EncoderDescription  = pipeline.ActiveEncoderBackend.ToString(),
            FailureReason       = renameError,
            Duration            = recordingElapsed,
            // Recording-relative counter: resets at recording start, excludes preview frames.
            FramesSubmittedSinceRecordingStart = expectedFrames,
            FrameCounterScope = "PreviewInclusive",
            // CSV-confirmed written frames: agrees with TimestampCsvRows and ffprobe frame count.
            FramesWrittenDuringRecording = pipeline.TimestampMonitorFrameCount,
            // TimestampCsvRows: same source as FramesWrittenDuringRecording; used by
            // VideoEngineRegistry.BuildMetadata to set TimestampCsvStatus = Written.
            TimestampCsvRows = pipeline.TimestampMonitorFrameCount,
            PostFinalizeProbedFrameCount    = probedFrameCount,
            PostFinalizeFrameCountMismatch  = frameCountMismatch,
        };
    }

    /// <summary>
    /// Independent ffprobe frame-count check on the just-finalized file — see the doc comment on
    /// <see cref="RecordingFinalizeResult.PostFinalizeProbedFrameCount"/> for why this exists
    /// (WriteSample returning success does not guarantee the sample survived into the final
    /// container). Best-effort: any failure (ffprobe missing, probe error, timeout) leaves the
    /// result at (null, false) rather than blocking or failing the recording-stop flow.
    /// </summary>
    private static async Task<(long? probedCount, bool mismatch)> PostFinalizeFrameCheckAsync(
        string finalVideoPath, long expectedFrames, CancellationToken ct)
    {
        try
        {
            var probe = new MultiCamApp.Verification.VideoProbeService(new MultiCamApp.Core.VerificationSettings());
            if (!probe.IsAvailable) return (null, false);

            var data = await probe.ProbeAsync(finalVideoPath, ct).ConfigureAwait(false);
            if (!data.Success || data.FrameCount <= 0) return (null, false);

            var mismatch = data.FrameCount != expectedFrames;
            if (mismatch)
            {
                AppDiagnosticLogger.Recording(
                    $"V2_POST_FINALIZE_FRAME_MISMATCH file={Path.GetFileName(finalVideoPath)} " +
                    $"expectedFrames={expectedFrames} actualFrames={data.FrameCount} " +
                    $"diff={expectedFrames - data.FrameCount}");
            }
            return (data.FrameCount, mismatch);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Recording(
                $"V2_POST_FINALIZE_FRAME_CHECK_ERROR file={Path.GetFileName(finalVideoPath)} " +
                $"{ex.GetType().Name}: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>Stops recording for ALL slots that are currently recording.</summary>
    public async Task<RecordingFinalizeResult[]> StopAllSlotsRecordingAsync(
        RecordingFileSet?[] fileSets, CancellationToken ct = default)
    {
        var results = new RecordingFinalizeResult[MaxSlots];
        var stopTasks = new List<Task>();

        async Task StopOneAsync(int slot)
        {
            try { results[slot] = await StopSlotRecordingAsync(slot, fileSets[slot]!, ct); }
            catch (Exception ex)
            {
                results[slot] = RecordingFinalizeResult.Failure(ex.Message);
                AppDiagnosticLogger.Runtime($"V2_STOP_REC_SLOT_ERROR slot={slot} {ex.GetType().Name}: {ex.Message}");
            }
        }

        for (var i = 0; i < MaxSlots; i++)
        {
            if (_pipelines[i]?.State is CameraPipelineState.Recording && fileSets[i] is not null)
            {
                stopTasks.Add(StopOneAsync(i));
            }
            else
            {
                results[i] = RecordingFinalizeResult.Failure("No recording in progress.");
            }
        }
        await Task.WhenAll(stopTasks);
        return results;
    }

    // ── Adaptive preview throttle ─────────────────────────────────────────────

    /// <summary>
    /// Sets the preview FPS limit for all active slots.
    /// Called at recording start/stop to adapt preview rate to recording load.
    /// 0 = unlimited (frame-drop-if-pending logic still active).
    /// Recommended values: 1 camera=20, 2=15, 3=10, 4=8.
    /// </summary>
    public void SetAllSlotsPreviewFpsLimit(int maxFps)
    {
        for (var i = 0; i < MaxSlots; i++)
            _pipelines[i]?.SetPreviewFpsLimit(maxFps);
    }

    /// <summary>
    /// Returns the recommended preview FPS limit for the given number of recording cameras.
    /// Lower limits leave more CPU/dispatcher headroom for recording and UI responsiveness.
    /// </summary>
    /// <remarks>
    /// Raised from the original 20/15/10/8 values (v1.2.19-21) after a real side-by-side
    /// comparison against Windows Camera app (2026-07-04, ShareX capture) showed this throttle
    /// visibly ghosting/multi-exposing fast motion in MultiCamApp's preview specifically once
    /// recording starts, while Windows Camera's own preview stayed smooth throughout. These
    /// original low values were tuned back when the frame-arrived callback thread also did
    /// synchronous CSV disk I/O (the actual cause of the severe v1.2.37 freeze finding); that
    /// I/O was moved off the hot path in v1.2.37, so this throttle no longer needs to be this
    /// aggressive to protect UI responsiveness. This is an interim mitigation, not a fix for the
    /// root cause — the WPF/CPU fallback renderer is still active on every slot due to the
    /// still-unresolved D3D11 swap chain QueryInterface failure (see
    /// [[feedback_gpu_preview_fallback_unconfirmed]]); once that's actually fixed, GPU slots
    /// bypass this throttle entirely and the exact value here matters much less. Needs a real
    /// 4-camera stress test to confirm no UI freeze regression before trusting these numbers long-term.
    /// </remarks>
    public static int RecommendedPreviewFpsForRecordingCameras(int activeCameraCount) =>
        activeCameraCount switch
        {
            <= 1 => 30,
            2    => 24,
            3    => 18,
            _    => 12,
        };

    // ── Per-slot control results ───────────────────────────────────────────────

    public IReadOnlyList<V2ControlApplyResult> GetSlotControlResults(int slotIndex) =>
        slotIndex >= 0 && slotIndex < MaxSlots
            ? (_pipelines[slotIndex]?.LastControlResults ?? Array.Empty<V2ControlApplyResult>())
            : Array.Empty<V2ControlApplyResult>();

    /// <summary>
    /// Restores research-safe focus default (autofocus off / manual mode) for the given slot while preview is live.
    /// Safe to call during Previewing or Recording state.
    /// </summary>
    public Task<V2ControlApplyResult> RestoreSlotFocusDefaultAsync(int slotIndex, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return Task.FromResult(new V2ControlApplyResult
            {
                Control = V2CameraControl.Focus,
                Applied = false,
                ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
                WarningMessage = $"Slot {slotIndex} not open.",
            });
        return _pipelines[slotIndex]!.RestoreFocusDefaultAsync(ct);
    }

    /// <summary>
    /// Restores research-safe exposure default (auto exposure off) for the given slot while preview is live.
    /// Safe to call during Previewing or Recording state.
    /// </summary>
    public Task<V2ControlApplyResult> RestoreSlotExposureDefaultAsync(int slotIndex, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return Task.FromResult(new V2ControlApplyResult
            {
                Control = V2CameraControl.Exposure,
                Applied = false,
                ReadbackStatus = V2ControlReadbackStatus.NotAttempted,
                WarningMessage = $"Slot {slotIndex} not open.",
            });
        return _pipelines[slotIndex]!.RestoreExposureDefaultAsync(ct);
    }

    /// <summary>
    /// Probes the driver-reported capability ranges for the given slot.
    /// The pipeline must be in Previewing or Recording state for ranges to be populated.
    /// Returns null if the slot is not open.
    /// </summary>
    public V2CameraCapabilitySnapshot? GetSlotCapabilities(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return null;
        return _pipelines[slotIndex]!.ProbeCapabilities();
    }

    public Task<V2EnvironmentLockResult?> ExecuteSlotEnvironmentalLockAsync(
        int slotIndex, CancellationToken ct = default)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return Task.FromResult<V2EnvironmentLockResult?>(null);
        return _pipelines[slotIndex]!.ExecuteEnvironmentalLockAsync(ct)
            .ContinueWith(t => (V2EnvironmentLockResult?)t.Result, ct);
    }

    public Task<V2EnvironmentLockResult?> OneShotCalibrateSlotAsync(
        int slotIndex, CancellationToken ct = default)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return Task.FromResult<V2EnvironmentLockResult?>(null);
        return _pipelines[slotIndex]!.OneShotCalibrateAsync(ct)
            .ContinueWith(t => (V2EnvironmentLockResult?)t.Result, ct);
    }

    public Task<V2ControlApplyResult?> SetSlotWhiteBalanceManualAsync(
        int slotIndex, uint kelvins, CancellationToken ct = default)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return Task.FromResult<V2ControlApplyResult?>(null);
        return _pipelines[slotIndex]!.SetWhiteBalanceManualAsync(kelvins, ct)
            .ContinueWith(t => (V2ControlApplyResult?)t.Result, ct);
    }

    public Task ReleaseSlotEnvironmentalLockAsync(int slotIndex, CancellationToken ct = default)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots || _pipelines[slotIndex] is null)
            return Task.CompletedTask;
        return _pipelines[slotIndex]!.ReleaseEnvironmentalLockAsync(ct);
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    public VideoEngineDiagnosticsSnapshot GetDiagnosticsSnapshot()
        => VideoEngineDiagnostics.BuildSnapshot(_pipelines[0], _selectedDevices[0], _formatResults[0], ActiveBackend);

    public VideoEngineDiagnosticsSnapshot GetSlotDiagnosticsSnapshot(int slotIndex)
        => slotIndex >= 0 && slotIndex < MaxSlots
            ? VideoEngineDiagnostics.BuildSnapshot(_pipelines[slotIndex], _selectedDevices[slotIndex], _formatResults[slotIndex], ActiveBackend)
            : VideoEngineDiagnostics.BuildSnapshot(null, null, null, ActiveBackend);

    public V2PreviewOverlayData GetCam1OverlayData(bool legacyIsRecording = false)
        => GetSlotOverlayData(0, legacyIsRecording);

    public V2PreviewOverlayData GetSlotOverlayData(int slot, bool legacyIsRecording = false)
    {
        var pipeline = slot >= 0 && slot < MaxSlots ? _pipelines[slot] : null;
        var label = $"cam{slot + 1}";
        return pipeline?.BuildOverlayData(legacyIsRecording, label)
               ?? new V2PreviewOverlayData
               {
                   CameraLabel = label,
                   Backend     = ActiveBackend,
               };
    }

    private void EmitDiagnostics(int slot)
    {
        var snap = GetSlotDiagnosticsSnapshot(slot);
        AppDiagnosticLogger.Runtime(
            $"V2_SNAP slot={slot} state={snap.PipelineState} " +
            $"fps={snap.HealthSnapshot?.LiveFps:F1} " +
            $"delivered={snap.HealthSnapshot?.FramesDelivered} " +
            $"dropped=N/A(Realtime) " +
            $"renderer={snap.ActivePreviewRenderer} " +
            $"encoder={snap.ActiveEncoderBackend} hw={snap.HardwareEncoderUsed} " +
            $"d3d11={snap.Direct3DAvailability} mf={snap.MediaFoundationAvailability}");
        DiagnosticsAvailable?.Invoke(this, snap);
    }

    // ── Device selection helpers ──────────────────────────────────────────────

    private V2CameraDeviceInfo SelectDevice(string? preferredDeviceId, int fallbackIndex, HashSet<string>? excludeIds = null)
    {
        var candidates = _deviceManager.Devices
            .Where(d => excludeIds == null || !excludeIds.Contains(d.DeviceId))
            .ToList();

        if (candidates.Count == 0)
            return _deviceManager.Devices.First();

        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            var exact = candidates.FirstOrDefault(d =>
                string.Equals(d.DeviceId, preferredDeviceId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;

            var key = NormalizeDeviceId(preferredDeviceId);
            var fuzzy = candidates.FirstOrDefault(d =>
            {
                var ck = NormalizeDeviceId(d.DeviceId);
                return ck.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || key.Contains(ck, StringComparison.OrdinalIgnoreCase);
            });
            if (fuzzy is not null) return fuzzy;
        }

        var visibleColor = candidates.Where(d => !LooksLikeInfraredDevice(d)).ToList();

        if (fallbackIndex >= 0 && fallbackIndex < visibleColor.Count)
            return visibleColor[fallbackIndex];

        if (visibleColor.Count > 0) return visibleColor[0];

        return fallbackIndex >= 0 && fallbackIndex < candidates.Count
            ? candidates[fallbackIndex]
            : candidates[0];
    }

    private static string NormalizeDeviceId(string deviceId) =>
        deviceId.Trim()
            .Replace(@"\\?\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("#", "\\", StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();

    private static bool LooksLikeInfraredDevice(V2CameraDeviceInfo device)
    {
        var text = $"{device.FriendlyName} {device.DeviceId}";
        return text.Contains("IR", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Infrared", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Depth", StringComparison.OrdinalIgnoreCase);
    }

    private void OnPipelineError(int slot, Exception ex)
    {
        AppDiagnosticLogger.Runtime($"V2_PIPELINE_ERROR slot={slot} {ex.GetType().Name}: {ex.Message}");
        AppDiagnosticLogger.Failure("VideoEngineV2", $"slot={slot} {ex.Message}", ex);
        EmitDiagnostics(slot);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VideoEngineV2));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_activeRecordingCount > 0)
        {
            _activeRecordingCount = 0;
            try { _displayRequest.RequestRelease(); } catch { }
        }
        for (var i = 0; i < MaxSlots; i++)
        {
            _pipelines[i]?.Dispose();
            _pipelines[i] = null;
        }
        _deviceManager.Dispose();
    }
}
