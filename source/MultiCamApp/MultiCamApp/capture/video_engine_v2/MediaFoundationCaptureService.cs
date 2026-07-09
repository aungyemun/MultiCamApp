////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).
// Uses WinRT MediaCapture + MediaFrameReader (Windows' public camera API, which wraps
// Media Foundation internally). Named "MediaFoundation" to reflect the underlying pipeline.

using MultiCamApp.Utils;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Opens a camera via WinRT <see cref="MediaCapture"/> and delivers frames one-by-one
/// via <see cref="MediaFrameReader"/> to the preview renderer and timestamp monitor.
/// Preview and recording are independent: this service feeds preview only.
/// The legacy recording pipeline is not affected.
/// </summary>
/// <remarks>
/// Must be opened and started from a UI-thread context (STA apartment) because
/// <see cref="MediaCapture.InitializeAsync"/> requires STA in some configurations.
/// Frames are delivered on a background thread — callers must dispatch to UI thread before
/// touching WPF objects.
/// </remarks>
public sealed class MediaFoundationCaptureService : IDisposable
{
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private long _frameIndex;
    private bool _disposed;
    private bool _running;

    /// <summary>Raised for each frame. Fires on a background thread.</summary>
    public event EventHandler<V2FrameArrivedEventArgs>? FrameArrived;

    /// <summary>Raised when the service encounters an unrecoverable error.</summary>
    public event EventHandler<Exception>? CaptureError;

    /// <summary>Device ID currently opened, or null.</summary>
    public string? OpenedDeviceId { get; private set; }

    /// <summary>Format negotiated at open time, or null.</summary>
    public V2CaptureFormat? ActiveFormat { get; private set; }

    /// <summary>
    /// The underlying <see cref="MediaCapture"/> instance while the camera is open.
    /// <see cref="MediaFoundationEncoderService"/> uses this to add a recording stream
    /// to the same already-open camera session (same approach as the legacy pipeline).
    /// Null before <see cref="OpenAsync"/> and after <see cref="CloseAsync"/>.
    /// </summary>
    public MediaCapture? ActiveMediaCapture => _mediaCapture;

    /// <summary>True while frame delivery is running.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Opens the camera, negotiates the best available format matching <paramref name="requestedFormat"/>,
    /// and prepares the <see cref="MediaFrameReader"/> for Bgra8 delivery.
    /// No frames are delivered until <see cref="StartAsync"/> is called.
    /// </summary>
    public async Task OpenAsync(string deviceId, V2CaptureFormat requestedFormat,
        Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? sharedD3dDevice = null,
        CancellationToken ct = default)
    {
        if (_mediaCapture is not null)
            await CloseAsync(ct);

        var settings = new MediaCaptureInitializationSettings
        {
            VideoDeviceId        = deviceId,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            SharingMode          = MediaCaptureSharingMode.ExclusiveControl,
            // The desktop WinRT projection cannot attach our renderer's D3D11 device to
            // MediaCapture. Auto may therefore return textures owned by another device,
            // which cannot be copied safely and leaves preview black. CPU guarantees a
            // BGRA SoftwareBitmap for both the D3D staging path and live WPF fallback.
            MemoryPreference     = MediaCaptureMemoryPreference.Cpu,
        };
        // MediaCaptureInitializationSettings.Direct3D11Device is not exposed in the
        // net8.0-windows desktop projection. The sharedD3dDevice parameter is reserved
        // for a future projection that supports explicit device sharing.

        var mc = new MediaCapture();
        mc.Failed += OnCaptureFailed;
        try
        {
            await mc.InitializeAsync(settings).AsTask(ct);
        }
        catch
        {
            mc.Failed -= OnCaptureFailed;
            mc.Dispose();
            throw;
        }
        _mediaCapture = mc;

        // Find the first colour video source (Preview preferred, Record as fallback)
        var colorSource = _mediaCapture.FrameSources.Values
            .FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoPreview)
            ?? _mediaCapture.FrameSources.Values
            .FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoRecord);

        if (colorSource is null)
            throw new InvalidOperationException(
                $"No video frame source found on device {deviceId}. " +
                "Check camera drivers and Windows privacy settings.");

        // Try to set the requested format on the source
        await NegotiateFormatAsync(colorSource, requestedFormat, ct);

        // Request Bgra8 output so the renderer can write directly to WriteableBitmap
        _frameReader = await _mediaCapture.CreateFrameReaderAsync(
            colorSource,
            MediaEncodingSubtypes.Bgra8).AsTask(ct);

        _frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        _frameReader.FrameArrived   += OnFrameArrived;

        OpenedDeviceId = deviceId;
        _frameIndex    = 0;
    }

    /// <summary>Starts frame delivery. Call after <see cref="OpenAsync"/>.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_frameReader is null)
            throw new InvalidOperationException("Call OpenAsync before StartAsync.");

        var status = await _frameReader.StartAsync().AsTask(ct);
        if (status != MediaFrameReaderStartStatus.Success)
            throw new InvalidOperationException($"MediaFrameReader failed to start: {status}.");

        _running = true;
    }

    /// <summary>Stops frame delivery. Camera remains open; call <see cref="CloseAsync"/> to fully release.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _running = false;
        if (_frameReader is not null)
        {
            // Checkpoint logging: three consecutive native crashes have occurred right around
            // this camera's Stop Preview teardown (v1.2.66/68/69) — a native fault can't be
            // caught by try/catch, so these lines exist purely so the LAST one to appear in the
            // log before the process vanishes narrows the fault to this exact native call.
            AppDiagnosticLogger.Runtime($"V2_CAPTURE_FRAMEREADER_STOP_BEGIN device={OpenedDeviceId}");
            await _frameReader.StopAsync().AsTask(ct);
            AppDiagnosticLogger.Runtime($"V2_CAPTURE_FRAMEREADER_STOP_END device={OpenedDeviceId}");
        }
    }

    /// <summary>
    /// Stops delivery, releases <see cref="MediaFrameReader"/> and <see cref="MediaCapture"/>,
    /// and clears the camera light indicator.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        var deviceTag = OpenedDeviceId;
        await StopAsync(ct);
        AppDiagnosticLogger.Runtime($"V2_CAPTURE_DISPOSE_READER_BEGIN device={deviceTag}");
        DisposeReader();
        AppDiagnosticLogger.Runtime($"V2_CAPTURE_DISPOSE_READER_END device={deviceTag}");
        // Logs the managed thread ID actually executing this call, to cross-check against
        // CameraPipelineV2.StopPreviewAsync's own V2_STOP_PREVIEW_THREAD_CHECK line — confirms
        // (or refutes) whether a Dispatcher.Invoke redirect further up the call chain actually
        // changed which thread this specific call runs on.
        AppDiagnosticLogger.Runtime(
            $"V2_CAPTURE_DISPOSE_MEDIACAPTURE_BEGIN device={deviceTag} managedThreadId={Environment.CurrentManagedThreadId}");
        DisposeCapture();
        AppDiagnosticLogger.Runtime($"V2_CAPTURE_DISPOSE_MEDIACAPTURE_END device={deviceTag}");
        OpenedDeviceId = null;
        ActiveFormat   = null;
    }

    // -- private helpers -------------------------------------------------

    private async Task NegotiateFormatAsync(
        MediaFrameSource source, V2CaptureFormat requested, CancellationToken ct)
    {
        // Prefer an exact match; fall back to closest by area then FPS
        var best = source.SupportedFormats
            .Where(f => f.VideoFormat != null)
            .OrderBy(f =>
            {
                int dw  = (int)f.VideoFormat!.Width  - requested.Width;
                int dh  = (int)f.VideoFormat!.Height - requested.Height;
                double dfps = f.FrameRate.Numerator / (double)f.FrameRate.Denominator - requested.NominalFps;
                return (dw * dw + dh * dh) * 100 + dfps * dfps;
            })
            .FirstOrDefault();

        if (best is not null)
        {
            await source.SetFormatAsync(best).AsTask(ct);
            ActiveFormat = new V2CaptureFormat
            {
                Width       = (int)best.VideoFormat!.Width,
                Height      = (int)best.VideoFormat!.Height,
                NominalFps  = best.FrameRate.Numerator / (double)best.FrameRate.Denominator,
                PixelFormat = V2PixelFormat.Bgra8,
            };
        }
        else
        {
            // No SupportedFormats — accept whatever the camera negotiates
            ActiveFormat = requested;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs _)
    {
        using var frameRef = sender.TryAcquireLatestFrame();
        if (frameRef?.VideoMediaFrame is null) return;

        var vmf = frameRef.VideoMediaFrame;
        int fw  = (int)(vmf.VideoFormat?.Width  ?? 0);
        int fh  = (int)(vmf.VideoFormat?.Height ?? 0);

        SoftwareBitmap?  bitmap     = null;
        Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface? d3dSurface = null;

        try
        {
            // GPU path: MemoryPreference.Auto + capable driver → IDirect3DSurface
            // The surface is on our shared D3D11 device; CopyResource works without
            // cross-device overhead. WinRT projection AddRefs the underlying texture,
            // keeping it valid after frameRef is disposed.
            if (vmf.Direct3DSurface is { } gpuFrame)
            {
                d3dSurface = gpuFrame;
            }
            else if (vmf.SoftwareBitmap is { } raw)
            {
                // CPU path: ensure Bgra8 Premultiplied for the staging-texture upload
                bitmap = raw.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                    ? SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                    : SoftwareBitmap.Copy(raw);
                if (fw == 0) fw = bitmap.PixelWidth;
                if (fh == 0) fh = bitmap.PixelHeight;
            }
        }
        catch
        {
            bitmap?.Dispose();
            d3dSurface?.Dispose();
            return;
        }

        var idx = Interlocked.Increment(ref _frameIndex) - 1;
        FrameArrived?.Invoke(this, new V2FrameArrivedEventArgs
        {
            FrameIndex            = idx,
            PresentationTimestamp = frameRef.SystemRelativeTime ?? TimeSpan.Zero,
            WallClockTimestamp    = DateTimeOffset.UtcNow,
            Width                 = fw,
            Height                = fh,
            SoftwareBitmap        = bitmap,      // ownership transferred — handler must Dispose
            Direct3DSurface       = d3dSurface,  // ownership transferred — handler must Dispose
        });
    }

    private void OnCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
        _running = false;
        CaptureError?.Invoke(this,
            new InvalidOperationException($"MediaCapture failed (0x{args.Code:X8}): {args.Message}"));
    }

    private void DisposeReader()
    {
        if (_frameReader is null) return;
        _frameReader.FrameArrived -= OnFrameArrived;
        _frameReader.Dispose();
        _frameReader = null;
    }

    private void DisposeCapture()
    {
        if (_mediaCapture is null) return;
        _mediaCapture.Failed -= OnCaptureFailed;
        _mediaCapture.Dispose();
        _mediaCapture = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running  = false;
        DisposeReader();
        DisposeCapture();
    }
}

/// <summary>Event arguments for a frame from <see cref="MediaFoundationCaptureService"/>.</summary>
public sealed class V2FrameArrivedEventArgs : EventArgs
{
    /// <summary>Zero-based frame index within the current session.</summary>
    public long FrameIndex { get; init; }

    /// <summary>
    /// Presentation timestamp from the MF sample (<c>SystemRelativeTime</c>).
    /// Must be cross-validated against wall clock for scientific use.
    /// </summary>
    public TimeSpan PresentationTimestamp { get; init; }

    /// <summary>Wall-clock time at which this event was raised.</summary>
    public DateTimeOffset WallClockTimestamp { get; init; }

    /// <summary>Frame width in pixels (camera native; from VideoFormat or SoftwareBitmap).</summary>
    public int Width { get; init; }

    /// <summary>Frame height in pixels (camera native; from VideoFormat or SoftwareBitmap).</summary>
    public int Height { get; init; }

    /// <summary>
    /// Bgra8-premultiplied bitmap for this frame.
    /// <b>The receiver owns this object and must call <see cref="SoftwareBitmap.Dispose"/>.</b>
    /// May be null if the frame source delivered no software bitmap.
    /// </summary>
    public SoftwareBitmap? SoftwareBitmap { get; init; }

    /// <summary>
    /// GPU surface for this frame (non-null only when MediaCapture.MemoryPreference = Auto
    /// and the camera driver delivers frames directly as a D3D11 texture).
    /// The receiver owns this object and must Dispose it.
    /// Null for all USB UVC cameras (which always deliver CPU SoftwareBitmap).
    /// </summary>
    public Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface? Direct3DSurface { get; init; }
}
