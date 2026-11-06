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

    // ── STABLE_CORE_V2 exception #14: camera-independent format negotiation ─────────────────
    // Previously the camera mode was chosen by resolution/FPS distance only and, among equal
    // matches, whichever subtype the driver listed first. The CPU frame reader delivered NO frames
    // from MJPG on real hardware (C922 probe), so any camera listing MJPG first showed a black
    // panel with "Previewing". Now: uncompressed subtypes are preferred, and a first-frame watchdog
    // switches to the next ranked mode when a mode delivers nothing.
    private MediaFrameSource? _colorSource;
    private List<MediaFrameFormat> _rankedFormats = new();
    private int _formatAttempt;
    private volatile TaskCompletionSource<bool>? _firstFrameTcs;
    private const int MaxFormatAttempts = 4;

    /// <summary>How long a mode may stay silent after start before the next mode is tried.</summary>
    public static TimeSpan FirstFrameTimeout { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>Camera-side subtype actually negotiated (e.g. NV12, YUY2, MJPG), or null.</summary>
    public string? ActiveSubtype { get; private set; }

    /// <summary>Set when the watchdog had to switch camera mode; shown in the UI and metadata.</summary>
    public string? NegotiationNote { get; private set; }

    /// <summary>Preference of camera subtypes for the CPU BGRA8 frame reader (lower = preferred).</summary>
    public static int SubtypeRank(string? subtype) => (subtype ?? "").ToUpperInvariant() switch
    {
        "NV12"                                   => 0,
        "YUY2" or "YUYV"                         => 1,
        "UYVY" or "I420" or "IYUV" or "YV12"     => 2,
        "RGB32" or "ARGB32" or "BGRA8" or "RGB24" => 2,
        "MJPG" or "MJPEG"                        => 8,
        "H264" or "HEVC" or "H265"               => 9,
        _                                        => 5,
    };

    /// <summary>
    /// Orders camera modes for a request: closest resolution/FPS first (resolution weighted x100,
    /// unchanged rule), then preferred subtype, then driver order. Pure function for unit tests.
    /// </summary>
    public static IReadOnlyList<int> RankFormats(
        IReadOnlyList<(int Width, int Height, double Fps, string? Subtype)> formats, int reqW, int reqH, double reqFps)
        => Enumerable.Range(0, formats.Count)
            .OrderBy(i =>
            {
                var (w, h, fps, _) = formats[i];
                double dw = w - reqW, dh = h - reqH, df = fps - reqFps;
                return (dw * dw + dh * dh) * 100 + df * df;
            })
            .ThenBy(i => SubtypeRank(formats[i].Subtype))
            .ThenBy(i => i)
            .ToList();

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

        var openWatch = System.Diagnostics.Stopwatch.StartNew();
        var mc = await InitializeMediaCaptureAsync(settings, ct);
        var initMs = openWatch.ElapsedMilliseconds;
        _mediaCapture = mc;

        // Find the first colour video source (Preview preferred, Record as fallback). Exception #14:
        // only Color sources — RGB+IR (Windows Hello) devices can expose an infrared preview stream
        // in the same group. Falls back to the previous rule if a driver reports no Color kind.
        var sources = _mediaCapture.FrameSources.Values.ToList();
        var colour  = sources.Where(fs => fs.Info.SourceKind == MediaFrameSourceKind.Color).ToList();
        if (colour.Count == 0) colour = sources;
        var colorSource = colour.FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoPreview)
            ?? colour.FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoRecord);

        if (colorSource is null)
            throw new InvalidOperationException(
                $"No video frame source found on device {deviceId}. " +
                "Check camera drivers and Windows privacy settings.");

        // Try to set the requested format on the source
        _colorSource = colorSource;
        NegotiationNote = null;
        await NegotiateFormatAsync(colorSource, requestedFormat, ct);

        // Request Bgra8 output so the renderer can write directly to WriteableBitmap
        await CreateReaderAsync(ct);

        OpenedDeviceId = deviceId;
        _frameIndex    = 0;
        AppDiagnosticLogger.Runtime($"V2_CAPTURE_OPEN_TIMING initMs={initMs} totalMs={openWatch.ElapsedMilliseconds}");
    }

    /// <summary>
    /// Creates and initialises the <see cref="MediaCapture"/> on the calling (UI, STA) thread.
    /// Kept on the UI thread deliberately: initialising on a thread-pool (MTA) thread removed the
    /// ~2.7 s UI stall at Start Preview (init 0.2 s instead of 2.6 s), but in the same test session
    /// the first app close afterwards hung inside <c>MediaCapture.Dispose()</c> on the UI thread and
    /// left an unkillable process holding the executable. Until creation and disposal can be moved
    /// to the same dedicated thread and soak-tested, the proven UI-thread path stays (2.1.0).
    /// </summary>
    private async Task<MediaCapture> InitializeMediaCaptureAsync(MediaCaptureInitializationSettings settings, CancellationToken ct)
    {
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
        return mc;
    }

    private async Task CreateReaderAsync(CancellationToken ct)
    {
        _frameReader = await _mediaCapture!.CreateFrameReaderAsync(_colorSource!, MediaEncodingSubtypes.Bgra8).AsTask(ct);
        _frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        _frameReader.FrameArrived   += OnFrameArrived;
    }

    /// <summary>
    /// Starts frame delivery. Call after <see cref="OpenAsync"/>. Returns once the first usable
    /// frame has arrived; if the negotiated mode stays silent for <see cref="FirstFrameTimeout"/>
    /// (or the reader refuses to start), the next ranked camera mode is tried (exception #14).
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_frameReader is null)
            throw new InvalidOperationException("Call OpenAsync before StartAsync.");

        var tried = new List<string>();
        while (true)
        {
            var modeTag = $"{ActiveFormat?.Width}x{ActiveFormat?.Height}@{ActiveFormat?.NominalFps:F2} {ActiveSubtype}";
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _firstFrameTcs = tcs;
            var status = await _frameReader!.StartAsync().AsTask(ct);
            if (status == MediaFrameReaderStartStatus.Success)
            {
                _running = true;
                var winner = await Task.WhenAny(tcs.Task, Task.Delay(FirstFrameTimeout, ct)).ConfigureAwait(true);
                if (winner == tcs.Task) return;
                _running = false;
                try { await _frameReader.StopAsync().AsTask(ct); } catch { /* best effort */ }
                tried.Add($"{modeTag} (no frames in {FirstFrameTimeout.TotalSeconds:F0} s)");
            }
            else
            {
                tried.Add($"{modeTag} ({status})");
            }
            AppDiagnosticLogger.Runtime($"V2_CAPTURE_MODE_FAILED device={OpenedDeviceId} mode=\"{tried[^1]}\"");
            ct.ThrowIfCancellationRequested();
            if (!await TryNextFormatAsync(ct))
                throw new InvalidOperationException(
                    "The camera delivered no frames in any compatible mode. Tried: " + string.Join("; ", tried) + ".");
            NegotiationNote = $"Camera mode {string.Join("; ", tried)} could not be used; " +
                              $"switched to {ActiveFormat?.Width}×{ActiveFormat?.Height}@{ActiveFormat?.NominalFps:F2} {ActiveSubtype}.";
        }
    }

    private async Task<bool> TryNextFormatAsync(CancellationToken ct)
    {
        var limit = Math.Min(MaxFormatAttempts, _rankedFormats.Count);
        while (++_formatAttempt < limit)
        {
            var next = _rankedFormats[_formatAttempt];
            try
            {
                DisposeReader();
                await ApplyFormatAsync(_colorSource!, next, ct);
                await CreateReaderAsync(ct);
                AppDiagnosticLogger.Runtime($"V2_CAPTURE_MODE_RETRY device={OpenedDeviceId} attempt={_formatAttempt + 1} " +
                    $"mode={ActiveFormat?.Width}x{ActiveFormat?.Height}@{ActiveFormat?.NominalFps:F2} {ActiveSubtype}");
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppDiagnosticLogger.Runtime($"V2_CAPTURE_MODE_RETRY_FAILED device={OpenedDeviceId} {ex.Message}");
            }
        }
        return false;
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
        // Prefer an exact match; fall back to closest by area then FPS; among equal matches prefer
        // subtypes the CPU BGRA8 reader converts reliably (exception #14).
        var all = source.SupportedFormats.Where(f => f.VideoFormat != null).ToList();
        var keys = all.Select(f => ((int)f.VideoFormat!.Width, (int)f.VideoFormat!.Height,
            f.FrameRate.Denominator == 0 ? 0 : f.FrameRate.Numerator / (double)f.FrameRate.Denominator, (string?)f.Subtype)).ToList();
        _rankedFormats = RankFormats(keys, requested.Width, requested.Height, requested.NominalFps).Select(i => all[i]).ToList();
        _formatAttempt = 0;

        // Test hook (off unless the environment variable is set): moves the given subtype to the front
        // among modes equal to the best match, so the first-frame watchdog can be exercised on real
        // hardware (e.g. MULTICAMAPP_TEST_PREFER_SUBTYPE=MJPG). Documented in the test guide.
        var forced = Environment.GetEnvironmentVariable("MULTICAMAPP_TEST_PREFER_SUBTYPE");
        if (!string.IsNullOrWhiteSpace(forced) && _rankedFormats.Count > 1)
        {
            var top = _rankedFormats[0];
            var twin = _rankedFormats.FirstOrDefault(f => string.Equals(f.Subtype, forced, StringComparison.OrdinalIgnoreCase)
                && f.VideoFormat!.Width == top.VideoFormat!.Width && f.VideoFormat.Height == top.VideoFormat.Height
                && f.FrameRate.Numerator * top.FrameRate.Denominator == top.FrameRate.Numerator * f.FrameRate.Denominator);
            if (twin is not null && !ReferenceEquals(twin, top))
            {
                _rankedFormats.Remove(twin);
                _rankedFormats.Insert(0, twin);
                AppDiagnosticLogger.Runtime($"V2_CAPTURE_TEST_HOOK prefer_subtype={forced}");
            }
        }

        if (_rankedFormats.Count > 0)
        {
            await ApplyFormatAsync(source, _rankedFormats[0], ct);
        }
        else
        {
            // No SupportedFormats — accept whatever the camera negotiates
            ActiveFormat = requested;
            ActiveSubtype = null;
        }
    }

    private async Task ApplyFormatAsync(MediaFrameSource source, MediaFrameFormat best, CancellationToken ct)
    {
        await source.SetFormatAsync(best).AsTask(ct);
        ActiveFormat = new V2CaptureFormat
        {
            Width       = (int)best.VideoFormat!.Width,
            Height      = (int)best.VideoFormat!.Height,
            NominalFps  = best.FrameRate.Denominator == 0 ? 0 : best.FrameRate.Numerator / (double)best.FrameRate.Denominator,
            PixelFormat = V2PixelFormat.Bgra8,
        };
        ActiveSubtype = best.Subtype;
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
        _firstFrameTcs?.TrySetResult(true);   // exception #14: first-frame watchdog
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
