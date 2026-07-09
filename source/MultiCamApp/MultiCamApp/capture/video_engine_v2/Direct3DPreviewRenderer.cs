////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using MultiCamApp.Utils;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Preview renderer for VideoEngineV2 camera slots. Uses a D3D11 swap-chain host when
/// available and switches live to a WPF <see cref="WriteableBitmap"/> if D3D fails.
/// </summary>
/// <remarks>
/// The overlay data (<see cref="V2PreviewOverlayData"/>) is attached to this renderer
/// and must never be composited into the recorded video stream.
/// </remarks>
public sealed class Direct3DPreviewRenderer : IDisposable
{
    private WriteableBitmap? _previewBitmap;
    private Dispatcher? _dispatcher;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private bool _rendering;
    private bool _disposed;
    private int _renderFrameCount;
    private readonly Stopwatch _renderSw = new();

    // D3D11 probe result — set once at Initialise time
    private V2CapabilityAvailability _d3dAvailability = V2CapabilityAvailability.Unknown;

    // GPU swap-chain renderer + letterbox panel (null = WPF WriteableBitmap fallback)
    private D3D11SwapChainHost?  _gpuHost;
    private D3D11PreviewPanel?   _gpuPanel;

    // ── Frame-drop and throttle fields ────────────────────────────────────────
    // Prevent BeginInvoke flooding: at most one render is queued at any time.
    // Without this, 3 cameras × 30fps = 90 Render-priority BeginInvoke/sec starves input events.
    private int _renderPending;          // 0 = idle, 1 = queued (Interlocked)
    private long _lastRenderTick;        // Environment.TickCount64 of last queued render
    private int _minMsBetweenFrames = 0; // 0 = no throttle; set by SetPreviewFpsLimit
    private byte[]? _pixelBuffer;        // reused across frames to avoid 8 MB/frame allocation

    /// <summary>Raised after each frame is written to <see cref="PreviewBitmap"/>. Fires on the UI thread.</summary>
    public event EventHandler? FrameRendered;

    /// <summary>Raised if the renderer encounters an error it cannot recover from.</summary>
    public event EventHandler<Exception>? RendererError;

    /// <summary>
    /// Raised on the UI thread after a failed D3D11 preview has been replaced with a
    /// WPF bitmap. The camera pipeline remains open and continues delivering frames.
    /// </summary>
    public event Action<WriteableBitmap>? FallenBackToWpf;

    /// <summary>
    /// The WPF <see cref="WriteableBitmap"/> that the UI binds to for cam preview.
    /// Null when the GPU (D3D11) path is active or before <see cref="Initialise"/> is called.
    /// </summary>
    public WriteableBitmap? PreviewBitmap => _previewBitmap;

    /// <summary>
    /// Letterbox panel containing the D3D11 swap-chain HwndHost when the GPU path is active;
    /// null when using WPF WriteableBitmap.  Place this in the cell border instead of the Viewbox.
    /// </summary>
    public UIElement? GpuPreviewElement => _gpuPanel;

    /// <summary>
    /// The WinRT IDirect3DDevice from the GPU renderer's D3D11 device.
    /// Pass to MediaCapture so GPU frames land on the same device (zero-copy CopyResource).
    /// Non-null when GPU renderer is active; null otherwise.
    /// </summary>
    public Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? SharedWinRtDevice =>
        _gpuHost?.SharedWinRtDevice;

    /// <summary>Renderer type actually in use.</summary>
    public PreviewRendererType RendererType { get; private set; } = PreviewRendererType.Wpf;

    /// <summary>True while the renderer is accepting and presenting frames.</summary>
    public bool IsRendering => _rendering;

    /// <summary>D3D11 availability as probed at initialise time.</summary>
    public V2CapabilityAvailability Direct3DAvailability => _d3dAvailability;

    /// <summary>
    /// Initialises the renderer on the WPF UI thread.
    /// Probes D3D11 availability and prepares a WPF fallback surface when needed.
    /// Must be called from the UI thread.
    /// </summary>
    /// <param name="dispatcher">WPF dispatcher for frame updates.</param>
    /// <param name="width">Preview surface width in pixels.</param>
    /// <param name="height">Preview surface height in pixels.</param>
    public void Initialise(Dispatcher dispatcher, int width, int height)
    {
        _dispatcher   = dispatcher;
        _bitmapWidth  = width;
        _bitmapHeight = height;

        _d3dAvailability = VideoEngineDiagnostics.ProbeDirect3D11();

        // Attempt D3D11 GPU swap-chain renderer on the UI thread (D3D11CreateDevice must run there).
        // SharedWinRtDevice is available immediately after construction so MediaCapture can be
        // initialised with the shared device BEFORE the HwndHost enters the visual tree.
        if (_d3dAvailability == V2CapabilityAvailability.Available)
        {
            _dispatcher.Invoke(() =>
            {
                try
                {
                    var host  = new D3D11SwapChainHost();
                    var panel = new D3D11PreviewPanel(host, width, height);

                    host.FramePresented += OnGpuFramePresented;
                    host.DeviceLost     += OnGpuDeviceLost;

                    _gpuHost     = host;
                    _gpuPanel    = panel;
                    RendererType = PreviewRendererType.Direct3D;
                    AppDiagnosticLogger.Runtime(
                        $"V2_RENDERER_INIT renderer=D3D11 d3d11={_d3dAvailability} " +
                        $"sharedDevice={host.SharedWinRtDevice is not null}");
                }
                catch (Exception ex)
                {
                    AppDiagnosticLogger.Runtime(
                        $"V2_RENDERER_INIT D3D11 failed ({ex.GetType().Name}: {ex.Message}); falling back to WPF");
                }
            });
        }

        if (_gpuHost is null)
        {
            RendererType = PreviewRendererType.Wpf;
            _dispatcher.Invoke(() =>
            {
                _previewBitmap = new WriteableBitmap(
                    width, height, 96, 96, PixelFormats.Bgra32, null);
            });
            AppDiagnosticLogger.Runtime(
                $"V2_RENDERER_INIT renderer=WPF surface={width}x{height} d3d11={_d3dAvailability}");
        }
    }

    /// <summary>Starts accepting frames for presentation.</summary>
    public void StartRendering()
    {
        _rendering = true;
        Interlocked.Exchange(ref _renderPending, 0);
    }

    /// <summary>Stops accepting frames. Does not release the bitmap.</summary>
    public void StopRendering()
    {
        _rendering = false;
        Interlocked.Exchange(ref _renderPending, 0);
    }

    /// <summary>
    /// Sets the maximum preview frame rate.
    /// Called by VideoEngineV2 to throttle preview during multi-camera recording.
    /// 0 = no throttle (use drop-if-pending logic only).
    /// </summary>
    public void SetPreviewFpsLimit(int maxFps)
    {
        _minMsBetweenFrames = maxFps > 0 ? (int)(1000.0 / maxFps) : 0;
    }

    /// <summary>
    /// Presents one frame to the <see cref="PreviewBitmap"/>.
    /// Called on the capture background thread; dispatches pixel copy to the UI thread.
    /// Takes ownership of <see cref="V2FrameArrivedEventArgs.SoftwareBitmap"/> and disposes it.
    ///
    /// Frame-drop policy (prevents UI freeze with multiple cameras):
    /// 1. If a BeginInvoke is already pending, drop this frame immediately.
    /// 2. If minimum interval between renders hasn't elapsed, drop this frame.
    /// 3. Queue BeginInvoke at Background priority so input events always preempt preview.
    /// </summary>
    public void PresentFrame(V2FrameArrivedEventArgs frame)
    {
        if (!_rendering || _disposed || _dispatcher is null)
        {
            frame.SoftwareBitmap?.Dispose();
            frame.Direct3DSurface?.Dispose();
            return;
        }

        // Route to GPU render thread (fires FramePresented → FrameRendered after Present)
        if (_gpuHost is not null)
        {
            _gpuHost.PresentFrame(frame); // returns immediately; render thread presents async
            return;
        }

        if (_previewBitmap is null)
        {
            frame.SoftwareBitmap?.Dispose();
            frame.Direct3DSurface?.Dispose();
            return;
        }

        var bitmap = frame.SoftwareBitmap;
        if (bitmap is null)
        {
            // A GPU-only frame cannot be drawn by WriteableBitmap. Always release it;
            // subsequent USB/UVC frames normally arrive as SoftwareBitmap instances.
            frame.Direct3DSurface?.Dispose();
            return;
        }

        // Drop frame if minimum interval hasn't elapsed (adaptive throttle).
        if (_minMsBetweenFrames > 0)
        {
            var nowTick = Environment.TickCount64;
            if (nowTick - _lastRenderTick < _minMsBetweenFrames)
            {
                bitmap.Dispose();
                return;
            }
            _lastRenderTick = nowTick;
        }

        // Drop frame if a render is already queued (prevents BeginInvoke queue flood).
        // At most one render is in flight at any time.
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0)
        {
            bitmap.Dispose();
            return;
        }

        bool needsResize = bitmap.PixelWidth != _bitmapWidth || bitmap.PixelHeight != _bitmapHeight;

        // Use Background priority (< Input priority) so mouse/keyboard events always preempt preview.
        // This is the critical fix for the 3-camera UI freeze:
        // Render (7) > Input (5) caused input starvation; Background (4) < Input (5) fixes this.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            // Clear the pending flag FIRST so the next frame can queue immediately
            // while we do the (potentially slow) pixel copy on the UI thread.
            Interlocked.Exchange(ref _renderPending, 0);

            if (_disposed || _previewBitmap is null)
            {
                bitmap.Dispose();
                return;
            }
            try
            {
                if (needsResize)
                {
                    _bitmapWidth   = bitmap.PixelWidth;
                    _bitmapHeight  = bitmap.PixelHeight;
                    _previewBitmap = new WriteableBitmap(
                        _bitmapWidth, _bitmapHeight, 96, 96, PixelFormats.Bgra32, null);
                    _pixelBuffer   = null; // force buffer reallocate after resize
                    AppDiagnosticLogger.Runtime(
                        $"V2_RENDERER_RESIZE new={_bitmapWidth}x{_bitmapHeight} frame={_renderFrameCount}");
                }

                _renderSw.Restart();
                WriteFrameToWriteableBitmap(bitmap, _previewBitmap, ref _pixelBuffer);
                _renderSw.Stop();
                var frameIdx = ++_renderFrameCount;
                if (frameIdx == 1 || frameIdx % 300 == 0)
                    AppDiagnosticLogger.Runtime(
                        $"V2_RENDER_SAMPLE renderer={RendererType} frame={frameIdx} " +
                        $"writeMs={_renderSw.ElapsedMilliseconds} size={_bitmapWidth}x{_bitmapHeight} " +
                        $"d3d11={_d3dAvailability}");
                FrameRendered?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                RendererError?.Invoke(this, ex);
            }
            finally
            {
                bitmap.Dispose();
            }
        });
    }

    private static void WriteFrameToWriteableBitmap(SoftwareBitmap src, WriteableBitmap dst, ref byte[]? buffer)
    {
        int w      = src.PixelWidth;
        int h      = src.PixelHeight;
        int stride = w * 4; // Bgra8 = 4 bytes per pixel
        int bytes  = stride * h;

        // Reuse the pixel buffer across frames to eliminate per-frame 8 MB allocation.
        // Without reuse: 3 cameras × 30 fps × 8 MB = 720 MB/s of managed allocations → GC pauses.
        // Allocate only when frame size changes (rare after first frame).
        if (buffer is null || buffer.Length != bytes)
            buffer = new byte[bytes];

        // Copy pixel data out of the SoftwareBitmap via Windows.Storage.Streams.
        var pixelBuffer = new Windows.Storage.Streams.Buffer((uint)bytes) { Length = (uint)bytes };
        src.CopyToBuffer(pixelBuffer);

        using var reader = DataReader.FromBuffer(pixelBuffer);
        // DataReader.ReadBytes(byte[]) reads exactly buffer.Length bytes — safe because
        // buffer.Length == bytes (enforced above).
        reader.ReadBytes(buffer);

        // Write pixels into the WPF WriteableBitmap
        dst.Lock();
        try
        {
            dst.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), buffer, stride, 0);
        }
        finally
        {
            dst.Unlock();
        }
    }

    private void OnGpuFramePresented(object? sender, EventArgs e) =>
        FrameRendered?.Invoke(this, EventArgs.Empty);

    private void OnGpuDeviceLost(object? sender, EventArgs e)
    {
        if (_dispatcher is null || _disposed) return;
        if (_dispatcher.CheckAccess())
            FallBackToWpf();
        else
            _dispatcher.BeginInvoke(DispatcherPriority.Send, FallBackToWpf);
    }

    private void FallBackToWpf()
    {
        if (_disposed || _gpuHost is null) return;

        var failedHost = _gpuHost;
        failedHost.FramePresented -= OnGpuFramePresented;
        failedHost.DeviceLost     -= OnGpuDeviceLost;

        _gpuHost      = null;
        _gpuPanel     = null;
        RendererType  = PreviewRendererType.Wpf;
        _pixelBuffer  = null;
        _previewBitmap = new WriteableBitmap(
            Math.Max(1, _bitmapWidth), Math.Max(1, _bitmapHeight),
            96, 96, PixelFormats.Bgra32, null);
        Interlocked.Exchange(ref _renderPending, 0);

        try { failedHost.Dispose(); }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime(
                $"V2_RENDERER_FALLBACK dispose warning: {ex.GetType().Name}: {ex.Message}");
        }

        AppDiagnosticLogger.Runtime(
            $"V2_RENDERER_FALLBACK D3D11->WPF surface={_bitmapWidth}x{_bitmapHeight}");
        FallenBackToWpf?.Invoke(_previewBitmap);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed  = true;
        _rendering = false;
        _dispatcher?.Invoke(() =>
        {
            _previewBitmap = null;
            _gpuPanel      = null;
            // HwndHost.Dispose must be called on UI thread (calls DestroyWindowCore → joins render thread)
            var host = _gpuHost;
            _gpuHost = null;
            if (host is not null)
            {
                host.FramePresented -= OnGpuFramePresented;
                host.DeviceLost     -= OnGpuDeviceLost;
            }
            host?.Dispose();
        });
    }
}
