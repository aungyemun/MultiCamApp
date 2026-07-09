////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using MultiCamApp.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Owns and coordinates all V2 services for one camera slot (cam1 only for now).
/// Preview, recording, controls, and timestamps are wired here.
/// </summary>
/// <remarks>
/// Architecture:
/// <code>
/// MediaCapture (camera) ──► MediaFrameReader ──► FrameTimestampMonitor ──► CSV
///                                             ──► RecordingHealthMonitor
///                                             ──► Direct3DPreviewRenderer ──► WriteableBitmap
///                                             ──► MediaFoundationEncoderService (IMFSinkWriter) ──► cam1.tmp.mp4
/// CameraControlManagerV2 ──► VideoDeviceController (focus, exposure, etc.)
/// </code>
/// Preview is independent from recording. This pipeline does not control the legacy
/// recording pipeline and must not affect recording timing or output.
/// </remarks>
public sealed class CameraPipelineV2 : IDisposable
{
    private readonly MediaFoundationCaptureService _captureService   = new();
    private readonly Direct3DPreviewRenderer       _previewRenderer  = new();
    private readonly MediaFoundationEncoderService _encoderService   = new();
    private readonly CameraControlManagerV2        _controlManager   = new();
    private readonly FrameTimestampMonitor         _timestampMonitor = new();
    private readonly RecordingHealthMonitor        _healthMonitor    = new();
    private readonly CameraFormatSelectorV2        _formatSelector   = new();
    private readonly Stopwatch                     _recordingClock   = new();

    private CameraPipelineState _state = CameraPipelineState.Idle;
    private V2CameraDeviceInfo? _device;
    private V2FormatSelectionResult? _formatResult;
    private bool _disposed;
    // WinRT MediaCapture/MediaFrameReader (the capture side) is itself built on Media Foundation,
    // same as MediaFoundationEncoderService — but only the encoder used to hold an MF platform ref.
    // That let MFShutdown() fire the moment the LAST active recording finished, even while this
    // slot (and up to 3 others) kept previewing on a MediaCapture session that still depended on
    // the now-torn-down MF platform — the very next MF-dependent call (StopPreviewAsync's
    // _captureService.CloseAsync) then crashed natively with no managed exception to catch.
    // Holding a ref for the whole open-camera lifetime (not just the recording lifetime) keeps MF
    // alive as long as ANY camera slot is open OR any recording is active, whichever is longer.
    private bool _mfRefHeldForCapture;
    // MediaFoundationCaptureService's own doc comment: MediaCapture must be opened/started from a
    // UI-thread (STA apartment) context. Captured in Initialise() so StopPreviewAsync can force
    // capture teardown back onto this exact thread even if earlier ConfigureAwait(false) awaits
    // in the same method (added in v1.2.64 to fix a UI freeze) already moved execution onto a
    // thread-pool thread — see the StopPreviewAsync comment at the capture-teardown block.
    private Dispatcher? _uiDispatcher;

    // Pixel-extraction scratch state for feeding the encoder — mirrors the pooled-buffer pattern
    // D3D11SwapChainHost.PresentFrame already uses for the same SoftwareBitmap → byte[] conversion.
    private readonly ConcurrentBag<byte[]> _encoderBufPool = new();
    private Windows.Storage.Streams.Buffer? _encoderWinRtBuffer;

    // Raw OnFrameArrived invocation counter — increments unconditionally, before any state gating,
    // unlike every other frame counter in this pipeline (CSV rows, health-monitor "delivered", and
    // the encoder's own _framesSubmitted all gate on pipeline state in some way). Exists purely to
    // diagnose a real, isolated discrepancy found in a 2026-07-06 audit: one OBSBOT virtual-camera
    // recording (severely fps-degraded, 284 mid-session timestamp gaps) had 1942 real encoded frames
    // (verified via exact per-frame MD5 hash — no duplicates) but the encoder's own _framesSubmitted
    // counter and the CSV row count both reported far fewer (1328 and 1360 respectively) — a 43-45%
    // undercount not seen in 11 sibling recordings from the same batch, which all matched exactly.
    // Root cause not yet identified; comparing this raw count against CSV rows/_framesSubmitted/
    // nb_frames next time this recurs should show whether OnFrameArrived is firing more often than
    // the gated counters register, or whether a gated counter is silently under-incrementing.
    private long _rawFrameArrivedCount;

    // Stored so they can be unsubscribed in Dispose — lambdas cannot be unsubscribed otherwise.
    private readonly EventHandler<Exception> _encoderErrorHandler;
    private readonly EventHandler<Exception> _rendererErrorHandler;
    private readonly EventHandler            _frameRenderedHandler;
    private readonly Action<System.Windows.Media.Imaging.WriteableBitmap> _fallenBackToWpfHandler;

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<CameraPipelineState>? StateChanged;
    public event EventHandler<CameraFrameTimestampInfo>? FrameTimestampSampled;
    public event EventHandler<Exception>? PipelineError;
    /// <summary>Raised on the UI thread after each preview frame is written to <see cref="PreviewBitmap"/>.</summary>
    public event EventHandler? FrameRendered;
    /// <summary>Raised when the live preview switches from D3D11 to WPF without closing the camera.</summary>
    public event Action<System.Windows.Media.Imaging.WriteableBitmap>? FallenBackToWpf;

    public CameraPipelineV2()
    {
        _encoderErrorHandler = (_, ex) =>
        {
            AppDiagnosticLogger.Recording($"V2_ENC_ERROR {ex.GetType().Name}: {ex.Message}");
            PipelineError?.Invoke(this, ex);
        };
        _rendererErrorHandler = (_, ex) =>
        {
            AppDiagnosticLogger.Runtime($"V2_RENDER_ERROR {ex.GetType().Name}: {ex.Message}");
            PipelineError?.Invoke(this, ex);
        };
        _frameRenderedHandler = (_, _) => FrameRendered?.Invoke(this, EventArgs.Empty);
        _fallenBackToWpfHandler = bitmap => FallenBackToWpf?.Invoke(bitmap);

        _captureService.FrameArrived   += OnFrameArrived;
        _captureService.CaptureError   += OnCaptureError;
        _encoderService.EncoderError   += _encoderErrorHandler;
        _previewRenderer.RendererError += _rendererErrorHandler;
        _previewRenderer.FrameRendered += _frameRenderedHandler;
        _previewRenderer.FallenBackToWpf += _fallenBackToWpfHandler;
    }

    // ── Public state ──────────────────────────────────────────────────────────

    public CameraPipelineState State => _state;
    public PreviewRendererType ActivePreviewRenderer => _previewRenderer.RendererType;
    public System.Windows.Media.Imaging.WriteableBitmap? PreviewBitmap => _previewRenderer.PreviewBitmap;

    /// <summary>
    /// Non-null when the D3D11 GPU renderer is active. Callers should place this UIElement
    /// in the preview area instead of binding to <see cref="PreviewBitmap"/>.
    /// </summary>
    public UIElement? GpuPreviewElement => _previewRenderer.GpuPreviewElement;
    public Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? SharedWinRtDevice =>
        _previewRenderer.SharedWinRtDevice;
    public V2CaptureFormat? ActiveFormat => _captureService.ActiveFormat;
    public EncoderBackendType ActiveEncoderBackend => _encoderService.ActiveEncoderBackend;
    public bool IsHardwareEncoderActive => _encoderService.HardwareEncoderAvailable;
    public string? WindowsStudioEffectsWarning => _controlManager.WindowsStudioEffectsWarning;
    public IReadOnlyList<V2ControlApplyResult> LastControlResults => _controlManager.LastAppliedResults;

    /// <summary>Overlay data for the UI preview surface. Must not be burned into video.</summary>
    public V2PreviewOverlayData BuildOverlayData(bool legacyIsRecording = false, string cameraLabel = "cam1")
    {
        var snap = _healthMonitor.GetSnapshot();
        var fmt  = _captureService.ActiveFormat;
        bool isV2Recording = _state == CameraPipelineState.Recording;
        return new V2PreviewOverlayData
        {
            CameraLabel       = cameraLabel,
            DeviceName        = _device?.FriendlyName ?? "",
            Resolution        = fmt is not null ? $"{fmt.Width}×{fmt.Height}" : "",
            TargetFps         = fmt?.NominalFps ?? 0,
            LiveFps           = snap.LiveFps,
            Backend           = VideoEngineBackend.MediaFoundation,
            Renderer          = _previewRenderer.RendererType,
            IsLegacyRecording = legacyIsRecording,
            IsRecording       = isV2Recording,
            RecordingElapsed  = _recordingClock.Elapsed,
            RecordedFrames    = _encoderService.FramesSubmitted,
            DroppedFrames     = snap.FramesDropped,
            EncoderBackend    = _encoderService.ActiveEncoderBackend.ToString(),
            FallbackWarning   = _formatResult?.FallbackReason,
            ControlWarning    = _controlManager.WindowsStudioEffectsWarning,
        };
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    /// <summary>Initialises the renderer on the WPF UI thread. Call once before <see cref="OpenAsync"/>.</summary>
    public void Initialise(Dispatcher uiDispatcher, int previewWidth = 1280, int previewHeight = 720)
    {
        _uiDispatcher = uiDispatcher;
        _previewRenderer.Initialise(uiDispatcher, previewWidth, previewHeight);
    }

    /// <summary>Opens the camera, negotiates format, and applies research-safe controls.</summary>
    public async Task OpenAsync(V2CameraDeviceInfo device,
                                V2CaptureFormatRequest? formatRequest = null,
                                CancellationToken ct = default)
    {
        ThrowIfDisposed();
        SetState(CameraPipelineState.Initialising);
        _device = device;

        try
        {
            var request = formatRequest ?? new V2CaptureFormatRequest
            {
                PreferredWidth  = VideoEngineSettings.DefaultPreferredWidth,
                PreferredHeight = VideoEngineSettings.DefaultPreferredHeight,
                PreferredFps    = VideoEngineSettings.DefaultPreferredFps,
                PreferredPixelFormat = VideoEngineSettings.DefaultPreferredPixelFormat,
            };

            _formatResult = _formatSelector.Select(device.SupportedFormats, request);
            var chosen = _formatResult.SelectedFormat
                ?? new V2CaptureFormat { Width = 1280, Height = 720, NominalFps = 30 };

            // Hold an MF platform ref for the whole time this camera is open (see field comment) —
            // acquired before opening the capture session so MF is guaranteed already started.
            if (!_mfRefHeldForCapture)
            {
                MediaFoundationRuntime.AddRef();
                _mfRefHeldForCapture = true;
            }

            // Pass the GPU renderer's shared D3D11 device so MediaCapture delivers
            // IDirect3DSurface frames on our device, enabling zero-CPU-copy CopyResource.
            await _captureService.OpenAsync(
                device.DeviceId, chosen, _previewRenderer.SharedWinRtDevice, ct);

            AppDiagnosticLogger.Runtime(
                $"V2_OPENED device=\"{device.FriendlyName}\" " +
                $"format={chosen.Width}x{chosen.Height}@{chosen.NominalFps}fps " +
                $"pixel={chosen.PixelFormat} kind={_formatResult.Kind}" +
                (_formatResult.FallbackReason is not null ? $" fallback={_formatResult.FallbackReason}" : ""));

            // Attach controls and apply research-safe defaults
            if (_captureService.ActiveMediaCapture is not null)
            {
                await _controlManager.AttachAsync(_captureService.ActiveMediaCapture, ct);
                await _controlManager.ApplyResearchDefaultsAsync(ct);
            }

            SetState(CameraPipelineState.Idle);
        }
        catch (Exception ex)
        {
            // Camera never actually opened — release the MF ref acquired above so a failed
            // open doesn't permanently keep the MF platform alive (or hold up MFShutdown for
            // other slots that legitimately close later).
            if (_mfRefHeldForCapture) { MediaFoundationRuntime.Release(); _mfRefHeldForCapture = false; }
            SetState(CameraPipelineState.Error);
            PipelineError?.Invoke(this, ex);
            throw;
        }
    }

    // ── Preview ────────────────────────────────────────────────────────────────

    public async Task StartPreviewAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_state is CameraPipelineState.Previewing) return;

        SetState(CameraPipelineState.Previewing);
        _healthMonitor.StartSession();
        _timestampMonitor.Start(_captureService.ActiveFormat?.NominalFps ?? 30);
        _previewRenderer.StartRendering();

        try { await _captureService.StartAsync(ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _previewRenderer.StopRendering();
            _healthMonitor.StopSession();
            await _timestampMonitor.StopAsync().ConfigureAwait(false);
            SetState(CameraPipelineState.Error);
            PipelineError?.Invoke(this, ex);
            throw;
        }
    }

    /// <summary>Stops preview delivery and releases the camera. Camera light turns off.</summary>
    public async Task StopPreviewAsync(CancellationToken ct = default)
    {
        if (_state is CameraPipelineState.Idle or CameraPipelineState.Disposed) return;

        // Stop recording first if active
        if (_state == CameraPipelineState.Recording)
        {
            try { await StopRecordingAsync(ct: ct).ConfigureAwait(false); } catch { /* best effort */ }
        }

        // Fine-grained checkpoints: three consecutive native crashes have now occurred in this
        // exact teardown sequence (v1.2.66/68/69), each traced to a different root cause, and
        // ordinary try/catch cannot catch a native-level fault. If it crashes again, the LAST of
        // these lines to appear in the log narrows the fault down to a single native call instead
        // of the whole method — see [[feedback_native_crash_and_sdk_verification]] finding #4.
        var deviceTag = _device?.FriendlyName ?? "unknown";
        AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_BEGIN device=\"{deviceTag}\"");

        _previewRenderer.StopRendering();
        await _timestampMonitor.StopAsync().ConfigureAwait(false);
        _healthMonitor.StopSession();

        // Fully tear down the GPU swap-chain host (stops its render thread, joins it, and
        // releases its own D3D11 device/swap chain) BEFORE closing the capture session below.
        // NOTE: verified via MediaFoundationCaptureService.OpenAsync that no D3D11 device is
        // actually shared with MediaCapture (MemoryPreference is hardcoded to Cpu; the desktop
        // WinRT projection doesn't expose Direct3D11Device on MediaCaptureInitializationSettings)
        // — an earlier version of this comment assumed a shared-device race that doesn't exist.
        // Disposing here (rather than lazily on the next Start Preview) still matters: it stops
        // the render thread and releases native resources promptly instead of leaving them alive
        // for an indefinite idle period. Safe because this CameraPipelineV2 instance is never
        // reused for preview afterward — Start Preview always constructs a fresh one (see
        // VideoEngineV2.PrepareSlotPreviewAsync).
        AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_RENDERER_DISPOSE_BEGIN device=\"{deviceTag}\"");
        _previewRenderer.Dispose();
        AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_RENDERER_DISPOSE_END device=\"{deviceTag}\"");

        try
        {
            // Detach our camera-control references (VideoDeviceController and everything derived
            // from it — focus/exposure/white-balance/etc.) BEFORE tearing down MediaCapture. These
            // were attached in OpenAsync (_controlManager.AttachAsync) and, until now, were never
            // released until the whole CameraPipelineV2 was disposed — meaning MediaCapture.Dispose()
            // ran while external references to its own VideoDeviceController sub-object were still
            // held by this class. Cheap, safe, and addresses a real gap regardless of whether it
            // turns out to be this crash's exact mechanism.
            _controlManager.Detach();

            // ROOT CAUSE CONFIRMED (v1.2.72 real-hardware test, proven with thread-ID logging):
            // MediaFoundationCaptureService's own doc comment says MediaCapture must be used from
            // a UI-thread (STA apartment) context; v1.2.71/72's investigation proved MediaCapture.
            // Dispose() was running on the wrong thread because a ConfigureAwait(false) upstream
            // let the continuation drift off the UI thread.
            //
            // v1.2.73 "fixed" that by forcing this block onto the UI dispatcher via a SYNCHRONOUS
            // Dispatcher.Invoke(() => { ...GetAwaiter().GetResult()... }) — which stopped the crash
            // but introduced a genuine DEADLOCK instead (confirmed on the next real-hardware test:
            // UI_FREEZE escalated MINOR -> WARNING -> CRITICAL at 3107ms, then eventually resolved
            // ~34s later, only the first camera slot's teardown completing before the whole UI
            // froze). Mechanism: MediaFoundationCaptureService.StopAsync's internal
            // `await _frameReader.StopAsync().AsTask(ct)` has no ConfigureAwait(false), so once
            // called from inside a Dispatcher.Invoke callback (where SynchronizationContext.Current
            // is the WPF DispatcherSynchronizationContext), its continuation tries to Post() back
            // onto that exact UI thread — which is synchronously blocked in .GetResult() waiting for
            // that very continuation. Classic async-over-sync deadlock.
            //
            // Fix: use Dispatcher.InvokeAsync with an async delegate instead of a synchronous
            // Invoke + GetResult(). The delegate still runs on/resumes on the UI thread (preserving
            // the STA affinity the whole fix exists for), but this awaits normally rather than
            // blocking the thread's message loop — so posted continuations can actually run.
            if (_uiDispatcher is not null)
            {
                await _uiDispatcher.InvokeAsync(async () =>
                {
                    AppDiagnosticLogger.Runtime(
                        $"V2_STOP_PREVIEW_CAPTURE_STOP_BEGIN device=\"{deviceTag}\" managedThreadId={Environment.CurrentManagedThreadId}");
                    await _captureService.StopAsync(ct);
                    AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_STOP_END device=\"{deviceTag}\"");
                    AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_CLOSE_BEGIN device=\"{deviceTag}\"");
                    await _captureService.CloseAsync(ct);
                    AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_CLOSE_END device=\"{deviceTag}\"");
                }).Task.Unwrap();
            }
            else
            {
                // No dispatcher captured (shouldn't normally happen — Initialise always sets one) —
                // fall back to the direct await, accepting the STA-affinity risk this whole fix
                // exists to close, rather than silently no-op the teardown.
                AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_NO_DISPATCHER device=\"{deviceTag}\"");
                AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_STOP_BEGIN device=\"{deviceTag}\"");
                await _captureService.StopAsync(ct);
                AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_STOP_END device=\"{deviceTag}\"");
                AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_CLOSE_BEGIN device=\"{deviceTag}\"");
                await _captureService.CloseAsync(ct);
                AppDiagnosticLogger.Runtime($"V2_STOP_PREVIEW_CAPTURE_CLOSE_END device=\"{deviceTag}\"");
            }
        }
        catch (Exception ex) { PipelineError?.Invoke(this, ex); }
        finally
        {
            // Release only after the capture session is fully closed — it (and the WinRT
            // MediaFrameReader teardown inside it) still depends on the MF platform up to this
            // point. Released even if Close threw, so a failed close can't wedge the ref count.
            if (_mfRefHeldForCapture) { MediaFoundationRuntime.Release(); _mfRefHeldForCapture = false; }
            SetState(CameraPipelineState.Idle);
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts H.264 recording to <paramref name="tempOutputPath"/> alongside the running preview.
    /// The caller is responsible for renaming temp → final after <see cref="StopRecordingAsync"/>.
    /// </summary>
    public async Task StartRecordingAsync(string tempOutputPath,
                                          string? timestampCsvPath = null,
                                          V2EncoderProfile? profile = null,
                                          CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_captureService.ActiveMediaCapture is null)
            throw new InvalidOperationException("Camera not open. Call OpenAsync first.");
        if (_state == CameraPipelineState.Recording) return;
        if (_state is not CameraPipelineState.Previewing)
            throw new InvalidOperationException(
                $"Recording requires Previewing state. Current state: {_state}.");

        SetState(CameraPipelineState.StartingRecording);
        var previewHealth = _healthMonitor.GetSnapshot();
        var nominalFps = _captureService.ActiveFormat?.NominalFps ?? 30;
        var cadenceFps = EncoderCadencePolicy.ResolveTargetFps(
            nominalFps, previewHealth.AverageFps, previewHealth.FramesDelivered, previewHealth.SessionElapsed);
        var encoderProfile = profile ?? new V2EncoderProfile
        {
            Width             = _captureService.ActiveFormat?.Width   ?? 1920,
            Height            = _captureService.ActiveFormat?.Height  ?? 1080,
            TargetFps         = cadenceFps,
            PreferHardware    = VideoEngineSettings.PreferHardwareEncoder,
            TargetBitrateKbps = VideoEngineSettings.TargetBitrateKbps,
        };

        AppDiagnosticLogger.Recording(
            $"V2_ENCODER_CADENCE nominal={nominalFps:F3} measured={previewHealth.AverageFps:F3} " +
            $"target={encoderProfile.TargetFps:F3} previewFrames={previewHealth.FramesDelivered} " +
            $"previewSeconds={previewHealth.SessionElapsed.TotalSeconds:F2}");

        if (VideoEngineSettings.WriteTimestampCsv)
        {
            var csvPath = timestampCsvPath ?? (Path.ChangeExtension(tempOutputPath, null) + "_timestamps.csv");
            // Timestamp monitor may already be running for preview; restart its CSV
            _timestampMonitor.OpenCsv(csvPath);
        }

        try
        {
            // ConfigureAwait(false): this class never touches WPF UI objects directly (verified —
            // its events either have no UI-thread subscribers or are already independently
            // marshaled via Dispatcher.BeginInvoke inside Direct3DPreviewRenderer). Resuming these
            // continuations on a thread-pool thread instead of demanding the UI dispatcher's turn
            // avoids queuing behind the GPU render threads' own dispatcher traffic — under real
            // multi-camera GPU load at 30/60fps this was measured causing multi-second Start/Stop
            // Recording UI freezes even though the underlying recording itself was unaffected.
            await _encoderService.OpenAsync(
                _captureService.ActiveMediaCapture, tempOutputPath, encoderProfile, ct).ConfigureAwait(false);
            await _encoderService.StartAsync(ct).ConfigureAwait(false);
            _recordingClock.Restart();
            AppDiagnosticLogger.Recording(
                $"V2_REC_START file={Path.GetFileName(tempOutputPath)} " +
                $"profile={encoderProfile.Width}x{encoderProfile.Height}@{encoderProfile.TargetFps}fps " +
                $"bitrateKbps={encoderProfile.TargetBitrateKbps} hw={encoderProfile.PreferHardware}");
            SetState(CameraPipelineState.Recording);
        }
        catch (Exception ex)
        {
            SetState(CameraPipelineState.Previewing);
            PipelineError?.Invoke(this, ex);
            throw;
        }
    }

    /// <summary>
    /// Stops recording and finalises the MP4 container. The temp file is written but not renamed.
    /// </summary>
    public async Task StopRecordingAsync(CancellationToken ct = default)
    {
        if (_state != CameraPipelineState.Recording) return;

        SetState(CameraPipelineState.StoppingRecording);
        _recordingClock.Stop();
        // Async: flushes CSV without blocking the calling thread. This is called from the UI
        // thread's Stop Recording chain — a synchronous Task.Wait() here (the old behavior)
        // stalls the UI for however long the background CSV writer takes to drain, once per
        // slot, sequentially. Under real multi-camera GPU-rendering load this stopped being
        // negligible (observed ~10s cumulative UI freeze stopping 4 cameras at once).
        await _timestampMonitor.StopAsync().ConfigureAwait(false);

        var health = _healthMonitor.GetSnapshot();
        AppDiagnosticLogger.Recording(
            $"V2_REC_STOP elapsed={_recordingClock.Elapsed.TotalSeconds:F1}s " +
            $"delivered={health.FramesDelivered} avgFps={health.AverageFps:F2} liveFps={health.LiveFps:F1} " +
            $"drops=N/A(MediaFrameReader.Realtime) " +
            $"rawFrameArrived={RawFrameArrivedCount} csvRows={_timestampMonitor.FrameCount} " +
            $"encoderSubmitted={_encoderService.FramesSubmittedSinceRecordingStart}");

        try { await _encoderService.FinaliseAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) { PipelineError?.Invoke(this, ex); }
        finally { SetState(CameraPipelineState.Previewing); }
    }

    // ── Preview throttle ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets the maximum preview frame rate for this pipeline slot.
    /// Called by VideoEngineV2 to reduce preview load during multi-camera recording.
    /// 0 = unlimited (drop-if-pending logic still active).
    /// </summary>
    public void SetPreviewFpsLimit(int maxFps) => _previewRenderer.SetPreviewFpsLimit(maxFps);

    // ── Camera control restore ─────────────────────────────────────────────────

    /// <summary>
    /// Restores the research-safe focus default: disables autofocus and sets manual/fixed focus mode.
    /// Call while preview is running (state == Previewing or Recording).
    /// Returns NotAttempted result if control manager is not yet attached.
    /// </summary>
    public Task<V2ControlApplyResult> RestoreFocusDefaultAsync(CancellationToken ct = default) =>
        _controlManager.DisableFocusAsync(ct);

    /// <summary>
    /// Restores the research-safe exposure default: disables auto exposure.
    /// Call while preview is running (state == Previewing or Recording).
    /// Returns NotAttempted result if control manager is not yet attached.
    /// </summary>
    public Task<V2ControlApplyResult> RestoreExposureDefaultAsync(CancellationToken ct = default) =>
        _controlManager.DisableAutoExposureAsync(ct);

    /// <summary>
    /// Probes driver-reported capability ranges (exposure min/max/step, focus min/max/step).
    /// Returns a populated snapshot when preview is live; returns NotAttached when camera is not yet open.
    /// </summary>
    public V2CameraCapabilitySnapshot ProbeCapabilities() =>
        _controlManager.ProbeCapabilities();

    public Task<V2EnvironmentLockResult> ExecuteEnvironmentalLockAsync(CancellationToken ct = default) =>
        _controlManager.ExecuteEnvironmentalLockAsync(ct);

    public Task<V2EnvironmentLockResult> OneShotCalibrateAsync(CancellationToken ct = default) =>
        _controlManager.OneShotCalibrateAsync(ct);

    public Task<V2ControlApplyResult> SetWhiteBalanceManualAsync(uint kelvins, CancellationToken ct = default) =>
        _controlManager.SetWhiteBalanceManualAsync(kelvins, ct);

    public Task ReleaseEnvironmentalLockAsync(CancellationToken ct = default) =>
        _controlManager.ReleaseEnvironmentalLockAsync(ct);

    // ── Health ────────────────────────────────────────────────────────────────

    public CameraHealthSnapshot GetHealthSnapshot() => _healthMonitor.GetSnapshot();

    /// <summary>
    /// Frames submitted to the encoder since recording started (recording-relative).
    /// Sourced from <see cref="MediaFoundationEncoderService.FramesSubmittedSinceRecordingStart"/>;
    /// excludes preview frames before recording began.
    /// </summary>
    public long EncoderFramesSubmittedSinceRecordingStart =>
        _encoderService.FramesSubmittedSinceRecordingStart;

    /// <summary>
    /// Timestamp CSV rows written during recording — i.e. frames that completed the full
    /// OnFrameArrived → FrameTimestampMonitor → CSV path. Stable after <see cref="StopRecordingAsync"/>.
    /// Matches <see cref="TimestampCsvRows"/> in <see cref="RecordingFinalizeResult"/>.
    /// </summary>
    public long TimestampMonitorFrameCount => _timestampMonitor.FrameCount;

    /// <summary>
    /// Elapsed time on the internal recording stopwatch.
    /// Valid after <see cref="StopRecordingAsync"/> returns (Stopwatch.Elapsed is stable after Stop()).
    /// </summary>
    public TimeSpan RecordingElapsed => _recordingClock.Elapsed;

    /// <summary>
    /// Total <see cref="OnFrameArrived"/> invocations since this pipeline was opened, counted
    /// unconditionally before any state check. Diagnostics-only — see the field's doc comment.
    /// </summary>
    public long RawFrameArrivedCount => Interlocked.Read(ref _rawFrameArrivedCount);

    // ── Frame routing ─────────────────────────────────────────────────────────

    private void OnFrameArrived(object? sender, V2FrameArrivedEventArgs e)
    {
        Interlocked.Increment(ref _rawFrameArrivedCount);

        if (_state is not (CameraPipelineState.Previewing or CameraPipelineState.Recording
                           or CameraPipelineState.StartingRecording or CameraPipelineState.StoppingRecording))
        {
            e.SoftwareBitmap?.Dispose();
            e.Direct3DSurface?.Dispose();
            return;
        }

        var tsInfo = _timestampMonitor.RecordFrame(e);
        FrameTimestampSampled?.Invoke(this, tsInfo);

        _healthMonitor.NotifyFrameDelivered();

        // Extract pixel bytes for the encoder BEFORE handing SoftwareBitmap ownership to the
        // preview renderer (which disposes it). CPU frames only — GPU (Direct3DSurface) frames are
        // never delivered on this app's capture configuration (MediaFoundationCaptureService hard-
        // codes MemoryPreference.Cpu).
        //
        // This runs on the MediaFrameReader callback thread, not the WPF UI thread — an exception
        // here is NOT caught by WPF's DispatcherUnhandledException handler and crashes the whole
        // process immediately with no log line (observed: changing the FPS/resolution dropdown,
        // which renegotiates the capture format and can briefly let old- and new-format frames
        // race here, silently killed the app). Every other frame-thread code path in this codebase
        // is already guarded this way (see SubmitFrame, FrameTimestampMonitor.RecordFrame) — this
        // block was the one exception, now fixed.
        if (_state == CameraPipelineState.Recording && e.SoftwareBitmap is { } bmpForEncoder)
        {
            try
            {
                int w = bmpForEncoder.PixelWidth, h = bmpForEncoder.PixelHeight, stride = w * 4, bytes = stride * h;
                if (_encoderWinRtBuffer is null || _encoderWinRtBuffer.Capacity < (uint)bytes)
                    _encoderWinRtBuffer = new Windows.Storage.Streams.Buffer((uint)bytes);
                _encoderWinRtBuffer.Length = (uint)bytes;
                bmpForEncoder.CopyToBuffer(_encoderWinRtBuffer);

                _encoderBufPool.TryTake(out var encBuf);
                if (encBuf is null || encBuf.Length != bytes) encBuf = new byte[bytes];
                using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(_encoderWinRtBuffer))
                    reader.ReadBytes(encBuf);

                _encoderService.SubmitFrame(e, encBuf, stride, h);
                _encoderBufPool.Add(encBuf); // SubmitFrame already copied into its own MF-owned buffer
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Recording(
                    $"V2_ENC_SUBMIT_ERROR {ex.GetType().Name}: {ex.Message} — frame dropped from encoder, preview unaffected");
            }
        }

        // Transfer SoftwareBitmap ownership to renderer (renderer disposes it)
        _previewRenderer.PresentFrame(e);
    }

    private void OnCaptureError(object? sender, Exception ex)
    {
        SetState(CameraPipelineState.Error);
        PipelineError?.Invoke(this, ex);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetState(CameraPipelineState next)
    {
        if (_state != next)
            AppDiagnosticLogger.Runtime($"V2_STATE {_state} → {next}");
        _state = next;
        StateChanged?.Invoke(this, next);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraPipelineV2));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetState(CameraPipelineState.Disposed);
        _captureService.FrameArrived   -= OnFrameArrived;
        _captureService.CaptureError   -= OnCaptureError;
        _encoderService.EncoderError   -= _encoderErrorHandler;
        _previewRenderer.RendererError -= _rendererErrorHandler;
        _previewRenderer.FrameRendered -= _frameRenderedHandler;
        _previewRenderer.FallenBackToWpf -= _fallenBackToWpfHandler;
        _controlManager.Dispose();
        _healthMonitor.Dispose();
        _timestampMonitor.Dispose();
        _encoderService.Dispose();
        // Capture service (MediaCapture) must be disposed BEFORE the preview renderer
        // because MediaCapture holds a reference to the shared D3D11 device.
        // Releasing MediaCapture first drops its D3D device ref; then the renderer
        // releases the device itself — correct refcount order, no dangling device ptr.
        _captureService.Dispose();
        // Safety net for abnormal teardown (e.g. a fresh Start Preview replacing a pipeline that
        // was never cleanly stopped via StopPreviewAsync) — release after capture is disposed,
        // same ordering rule as StopPreviewAsync.
        if (_mfRefHeldForCapture) { MediaFoundationRuntime.Release(); _mfRefHeldForCapture = false; }
        _previewRenderer.Dispose();
    }
}
