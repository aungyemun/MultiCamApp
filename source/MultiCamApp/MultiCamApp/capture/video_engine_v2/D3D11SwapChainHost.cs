////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// GPU preview renderer: D3D11 + DXGI flip swap chain in a Win32 child HWND (WPF HwndHost).
//
// Design goals vs Windows Camera:
//   • Dedicated render thread  — no WPF dispatcher involvement in D3D11 work
//   • Shared D3D11 device      — same device passed to MediaCapture so GPU frames
//                                 (IDirect3DSurface) can be CopyResource'd without
//                                 cross-device shared-resource overhead
//   • Camera-native swap chain — swap chain sized to camera resolution; DXGI_SCALING_STRETCH
//                                 handles display scaling; avoids CopyResource size mismatch
//   • DXGI_PRESENT_DO_NOT_WAIT — Present returns immediately if flip queue is full;
//                                 render thread never blocks waiting for VSync
//   • Drop policy              — single-slot pending frame; new frame replaces old before render
//
// CPU frame path (USB UVC cameras — MemoryPreference.Cpu or Auto without GPU-capable driver):
//   SoftwareBitmap → CopyToBuffer (capture thread) → byte[] → pending slot →
//   Map(WriteDiscard) staging → Marshal.Copy → Unmap → CopyResource → Present
//
// GPU frame path (MemoryPreference.Auto + GPU-capable camera driver, same D3D device):
//   IDirect3DSurface → pending slot → CopyResource (GPU-to-GPU, 0 CPU copies) → Present
//
// D3D11/DXGI COM interop is provided by Vortice.Direct3D11 / Vortice.DXGI (SharpGen-generated,
// SDK-header-accurate bindings) instead of hand-rolled [ComImport] vtable-slot-counted
// interfaces. The previous hand-rolled interop (v1.2.42-v1.2.58) never once produced a working
// swap chain on real hardware (DXGI_ERROR_INVALID_CALL from CreateSwapChainForHwnd); see
// CHANGELOG for the version history of that investigation.

using MultiCamApp.Utils;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Storage.Streams;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// WPF <see cref="HwndHost"/> that owns a DXGI flip swap chain and presents camera
/// preview frames via a dedicated D3D11 render thread.  Place inside
/// <see cref="D3D11PreviewPanel"/> for letterbox-correct aspect-ratio layout.
/// </summary>
public sealed class D3D11SwapChainHost : HwndHost
{
    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern bool IsWindow(IntPtr hWnd);

    // ── D3D11 / DXGI objects (render-thread only after constructor) ────────────
    private ID3D11Device?        _device;
    private ID3D11DeviceContext? _context;
    private IDXGIFactory2?       _dxgiFactory;
    private IDXGISwapChain1?     _swapChain;
    private ID3D11Texture2D?     _stagingTex;
    private int                  _stagingW, _stagingH;
    private int                  _swapCamW, _swapCamH; // swap chain / staging dimensions (camera res)

    // ── Render thread ──────────────────────────────────────────────────────────
    private readonly Thread                _renderThread;
    private readonly ManualResetEventSlim  _hwndSignal  = new(false); // BuildWindowCore → render thread
    private readonly ManualResetEventSlim  _frameSignal = new(false); // PresentFrame    → render thread
    private volatile IntPtr                _hwndForSwapChain;
    private volatile bool                  _disposed;
    private volatile bool                  _d3dReady;  // true once swap chain created
    // At most one FramePresented BeginInvoke in flight at a time (mirrors the WPF renderer's
    // _renderPending guard). Without this, an unthrottled GPU render thread posts one
    // Dispatcher.BeginInvoke per presented frame with no backpressure — at native camera FPS
    // across several concurrent camera slots this can flood the dispatcher's Background queue
    // faster than it drains, stalling the UI thread (observed as a real ~10s freeze during
    // Stop Recording on a 4-camera/60fps session once GPU rendering started actually engaging).
    private int                            _framePresentedNotifyPending;

    // ── Single-slot pending frame (capture thread → render thread) ─────────────
    private readonly object  _pendingLock = new();
    private byte[]?          _pendingPixels;
    private IDirect3DSurface? _pendingGpuSurface;
    private int              _pendingW, _pendingH;
    // Pool of pixel arrays to avoid per-frame allocation
    private readonly ConcurrentBag<byte[]> _bufPool = new();

    // ── WinRT buffer reused across CPU frames ──────────────────────────────────
    private Windows.Storage.Streams.Buffer? _winRtBuffer;

    // ── Device-lost / device-identity state ────────────────────────────────────
    private volatile bool _deviceLost;             // set when Present returns a fatal DXGI error
    private bool          _gpuDeviceMismatchLogged; // suppress repeated per-frame log noise

    private static readonly Guid s_tex2dGuid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// WinRT device wrapper for this D3D11 device.  Pass to
    /// <see cref="Windows.Media.Capture.MediaCaptureInitializationSettings.Direct3D11Device"/>
    /// so MediaCapture delivers GPU frames on the same device (enabling zero-CPU-copy CopyResource).
    /// Non-null after the constructor returns.
    /// </summary>
    public IDirect3DDevice? SharedWinRtDevice { get; private set; }

    /// <summary>True once the swap chain is created and the render thread is processing frames.</summary>
    public bool IsReady => _d3dReady;

    /// <summary>Fired on the UI thread after each frame is presented to the display.</summary>
    public event EventHandler? FramePresented;

    /// <summary>Fired on the UI thread the first time a frame with known dimensions arrives.</summary>
    public event Action<int, int>? CameraResolutionKnown;

    /// <summary>
    /// Fired on the UI thread when a fatal DXGI error (device removed or reset) stops the render thread.
    /// The preview surface goes black after this event. The caller should surface an error to the user
    /// and restart the camera slot to recreate the D3D11 device and swap chain.
    /// </summary>
    public event EventHandler? DeviceLost;

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the D3D11 device synchronously on the calling thread (must be UI thread).
    /// <see cref="SharedWinRtDevice"/> is available immediately after construction.
    /// The swap chain is created later when the HwndHost is added to the visual tree.
    /// Throws if D3D11 hardware device creation fails (caller should fall back to WPF renderer).
    /// </summary>
    public D3D11SwapChainHost()
    {
        CreateD3D11DeviceAndFactory();

        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name         = "D3D11Render",
            Priority     = ThreadPriority.AboveNormal,
        };
        _renderThread.Start();
    }

    // Creates ID3D11Device + ID3D11DeviceContext + IDXGIFactory2 + WinRT wrapper.
    // Must be called on the UI thread so that this HwndHost's Dispatcher is correct.
    private void CreateD3D11DeviceAndFactory()
    {
        // VideoSupport: this device is handed to MediaCaptureInitializationSettings.Direct3D11Device
        // (see SharedWinRtDevice below) so WinRT MediaCapture can deliver GPU frames on it directly.
        // Some driver stacks expect/require this flag for a device to be valid for that video-capture
        // interop path; omitting it previously left the shared-device video pipeline running on a
        // device the driver never marked as video-capable.
        var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
        D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            featureLevels, out _device, out _, out _context).CheckError();

        using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
        using var adapter    = dxgiDevice.GetAdapter();
        _dxgiFactory = adapter.GetParent<IDXGIFactory2>();

        // Wrap D3D11 device as WinRT IDirect3DDevice for MediaCapture shared-device init
        var hr = D3D11Native.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var winRtPtr);
        if (hr >= 0 && winRtPtr != IntPtr.Zero)
        {
            try   { SharedWinRtDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(winRtPtr); }
            catch { /* WinRT projection unavailable — GPU path degrades gracefully */ }
            finally { Marshal.Release(winRtPtr); }
        }
    }

    // ── HwndHost lifecycle ─────────────────────────────────────────────────────

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // Create a 1×1 placeholder child HWND — swap chain is resized to the real camera
        // resolution on the first frame via EnsureSwapChainAtCameraRes.
        var hwnd = Win32Window.CreateChild(hwndParent.Handle, 1, 1);
        if (hwnd == IntPtr.Zero)
        {
            AppDiagnosticLogger.Runtime("D3D11_HOST: CreateWindowEx failed");
            return new HandleRef(this, IntPtr.Zero);
        }
        _hwndForSwapChain = hwnd;

        // Swap chain creation happens synchronously here, on the UI thread that owns the HWND —
        // some DXGI driver stacks require swap-chain creation to happen on the window's owning
        // thread. The render thread still owns Present/frame delivery afterward.
        var placeholderW = VideoEngineSettings.PreviewWidth;
        var placeholderH = VideoEngineSettings.PreviewHeight;
        try
        {
            CreateSwapChain(hwnd, placeholderW, placeholderH);
            // Present a black frame immediately so the HWND doesn't flash white
            // before the first real camera frame arrives.
            PresentBlackFrame(placeholderW, placeholderH);
            _d3dReady = true;
            AppDiagnosticLogger.Runtime($"D3D11_HOST: swap chain created on UI thread {placeholderW}×{placeholderH}");
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime(
                $"D3D11_HOST: swap chain init FAILED (UI thread): {ex.GetType().Name}: {ex.Message} " +
                $"hresult=0x{ex.HResult:X8}" +
                (ex.InnerException is { } inner ? $" inner={inner.GetType().Name}: {inner.Message}" : ""));
            // Already on the UI/dispatcher thread here, so no BeginInvoke marshaling needed —
            // Direct3DPreviewRenderer.OnGpuDeviceLost is defensive about calling thread anyway.
            DeviceLost?.Invoke(this, EventArgs.Empty);
            _hwndSignal.Set(); // still unblock the render thread so it can see _disposed and exit
            return new HandleRef(this, hwnd);
        }

        _hwndSignal.Set(); // swap chain ready — unblock render thread to enter its present loop
        AppDiagnosticLogger.Runtime($"D3D11_HOST: HWND=0x{hwnd:X} signalled render thread");
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _disposed = true;
        _frameSignal.Set();  // unblock render thread's Wait so it can see _disposed and exit
        _renderThread.Join(TimeSpan.FromSeconds(2));

        lock (_pendingLock)
        {
            _pendingGpuSurface?.Dispose();
            _pendingGpuSurface = null;
            if (_pendingPixels is not null)
            {
                _bufPool.Add(_pendingPixels);
                _pendingPixels = null;
            }
        }

        // All D3D11 objects were created/used only on the render thread.
        // Safe to release here because render thread has fully exited.
        ReleaseStagingTex();
        _swapChain?.Dispose();     _swapChain   = null;
        _dxgiFactory?.Dispose();   _dxgiFactory = null;
        _context?.Dispose();       _context     = null;
        _device?.Dispose();        _device      = null;
        SharedWinRtDevice = null;

        Win32Window.Destroy(hwnd.Handle);
        AppDiagnosticLogger.Runtime("D3D11_HOST: destroyed");
    }

    // HwndHost element resizes do NOT resize the swap chain — DXGI_SCALING_STRETCH
    // handles display scaling; swap chain stays at camera native resolution.
    // D3D11PreviewPanel constrains the HWND to the correct AR in the WPF layout.
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // intentionally empty — swap chain at camera res, DXGI_SCALING_STRETCH scales to display
    }

    // ── Render thread ──────────────────────────────────────────────────────────

    private void RenderLoop()
    {
        // Swap chain creation happens synchronously in BuildWindowCore, on the UI thread that
        // owns the HWND (see that method's comment for why) — this thread only waits for that
        // to finish, then takes over Present/frame delivery for the rest of the host's lifetime.
        // If creation failed, _d3dReady stays false and DeviceLost was already fired from
        // BuildWindowCore, so just exit quietly here.
        _hwndSignal.Wait();
        if (_disposed || !_d3dReady) return;

        bool cameraResReported = false;

        while (!_disposed && !_deviceLost)
        {
            _frameSignal.Wait(50);  // 50 ms timeout to check _disposed
            _frameSignal.Reset();
            if (_disposed) break;

            // Take pending frame (single-slot: newest wins)
            byte[]?          cpuPixels   = null;
            IDirect3DSurface? gpuSurface  = null;
            int w = 0, h = 0;
            lock (_pendingLock)
            {
                cpuPixels  = _pendingPixels;  _pendingPixels   = null;
                gpuSurface = _pendingGpuSurface; _pendingGpuSurface = null;
                w = _pendingW; h = _pendingH;
            }

            if (cpuPixels == null && gpuSurface == null) continue;

            // Notify panel of camera AR on first frame
            if (!cameraResReported && w > 0 && h > 0)
            {
                cameraResReported = true;
                int fw = w, fh = h;
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    () => CameraResolutionKnown?.Invoke(fw, fh));
            }

            try
            {
                if (gpuSurface != null)
                {
                    PresentGpuSurface(gpuSurface, w, h);
                    gpuSurface.Dispose();
                }
                else if (cpuPixels != null)
                {
                    PresentCpuPixels(cpuPixels, w, h);
                    _bufPool.Add(cpuPixels); // return to pool
                }

                // Notify UI thread (lightweight — no D3D11 work). Coalesce: skip posting if a
                // notification is already queued, so the dispatcher never accumulates more than
                // one pending FramePresented callback per slot regardless of camera FPS/count.
                if (Interlocked.CompareExchange(ref _framePresentedNotifyPending, 1, 0) == 0)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        Interlocked.Exchange(ref _framePresentedNotifyPending, 0);
                        FramePresented?.Invoke(this, EventArgs.Empty);
                    });
                }
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Runtime($"D3D11_PRESENT_ERR {ex.GetType().Name}: {ex.Message}");
                gpuSurface?.Dispose();
                if (cpuPixels != null) _bufPool.Add(cpuPixels);
            }
        }

        // Render thread cleanup handled in DestroyWindowCore after Join
    }

    private void CreateSwapChain(IntPtr hwnd, int w, int h)
    {
        var desc = new SwapChainDescription1
        {
            Width             = (uint)w,
            Height            = (uint)h,
            Format            = Format.B8G8R8A8_UNorm,
            Stereo            = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage       = Usage.RenderTargetOutput,
            BufferCount       = 2,
            Scaling           = Scaling.Stretch,
            SwapEffect        = SwapEffect.FlipDiscard,
            AlphaMode         = AlphaMode.Unspecified,
            Flags             = SwapChainFlags.None,
        };

        AppDiagnosticLogger.Runtime(
            $"D3D11_HOST: about to call CreateSwapChainForHwnd — hwnd=0x{hwnd:X} isWindow={IsWindow(hwnd)} " +
            $"w={w} h={h}");

        try
        {
            _swapChain = _dxgiFactory!.CreateSwapChainForHwnd(_device, hwnd, desc);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x887A0001)) // DXGI_ERROR_INVALID_CALL
        {
            // Flip-model presentation rejected — retry once with the legacy BitBlt (DISCARD)
            // swap effect, which has far fewer driver/OS restrictions than flip-model.
            AppDiagnosticLogger.Runtime(
                "D3D11_HOST: flip-model swap chain rejected with DXGI_ERROR_INVALID_CALL; " +
                "retrying once with legacy BitBlt (DISCARD) swap effect");
            var legacyDesc = desc;
            legacyDesc.SwapEffect  = SwapEffect.Discard;
            legacyDesc.BufferCount = 1;
            _swapChain = _dxgiFactory!.CreateSwapChainForHwnd(_device, hwnd, legacyDesc);
        }

        AppDiagnosticLogger.Runtime($"D3D11_HOST: CreateSwapChainForHwnd succeeded");
        _swapCamW = w; _swapCamH = h;
    }

    // ── Present helpers (render thread) ────────────────────────────────────────

    // GPU-to-GPU copy: IDirect3DSurface (on our device) → swap chain back buffer → Present
    private void PresentGpuSurface(IDirect3DSurface surface, int w, int h)
    {
        if (_context is null || _swapChain is null) return;

        EnsureSwapChainAtCameraRes(w, h);

        ID3D11Texture2D? tex = null;
        try
        {
            var access  = (IDirect3DDxgiInterfaceAccess)(object)surface;
            var texGuid = s_tex2dGuid;
            var hr      = access.GetInterface(ref texGuid, out var texPtr);
            if (hr < 0) return;

            tex = new ID3D11Texture2D(texPtr);

            // Issue B: cross-device CopyResource is undefined behaviour (silent corruption or
            // device removal). Verify the texture was created on our device before proceeding.
            if (!TextureIsOnOurDevice(tex))
            {
                if (!_gpuDeviceMismatchLogged)
                {
                    _gpuDeviceMismatchLogged = true;
                    AppDiagnosticLogger.Runtime(
                        "D3D11_GPU_SKIP: IDirect3DSurface is on a different D3D11 device " +
                        "(MediaCapture internal device — Direct3D11Device sharing unavailable at " +
                        "net8.0 TFM). GPU frames dropped; CPU SoftwareBitmap path is used instead.");
                }
                return;
            }

            using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _context.CopyResource(backBuffer, tex);
        }
        catch (InvalidCastException)
        {
            return; // IDirect3DDxgiInterfaceAccess QI failed (CPU-only surface)
        }
        finally
        {
            tex?.Dispose();
        }

        // Issue A: check Present HRESULT.
        // WasStillDrawing = flip queue full → expected drop, ignore.
        // Any other failure (device removed/reset) = fatal → stop render thread.
        var presentResult = _swapChain.Present(0, PresentFlags.DoNotWait);
        if (presentResult.Failure && presentResult != Vortice.DXGI.ResultCode.WasStillDrawing)
        {
            AppDiagnosticLogger.Runtime(
                $"D3D11_PRESENT_FATAL (GPU) hr=0x{(uint)presentResult.Code:X8} — device lost; render thread stopping");
            _deviceLost = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                () => DeviceLost?.Invoke(this, EventArgs.Empty));
        }
    }

    // Clears the swap chain to opaque black — called once after swap chain creation so
    // the underlying HWND ("STATIC" class = white background) never flashes to the user.
    private void PresentBlackFrame(int w, int h)
    {
        // w*h BGRA pixels, all zero = (B=0 G=0 R=0 A=0) = opaque black.
        var pixels = new byte[w * h * 4];
        PresentCpuPixels(pixels, w, h);
    }

    // CPU upload: byte[] → D3D11_USAGE_DYNAMIC staging → CopyResource → swap chain → Present
    private void PresentCpuPixels(byte[] pixels, int w, int h)
    {
        if (_context is null || _swapChain is null || _device is null) return;

        EnsureSwapChainAtCameraRes(w, h);
        EnsureStagingTexture(w, h);
        if (_stagingTex is null) return;

        MappedSubresource mapped;
        try
        {
            mapped = _context.Map(_stagingTex, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"D3D11_MAP_FAIL {ex.GetType().Name}: {ex.Message}");
            return;
        }

        try
        {
            int srcStride = w * 4;
            if (mapped.RowPitch == srcStride)
            {
                Marshal.Copy(pixels, 0, mapped.DataPointer, pixels.Length);
            }
            else
            {
                for (int row = 0; row < h; row++)
                {
                    var dst = mapped.DataPointer + row * (int)mapped.RowPitch;
                    Marshal.Copy(pixels, row * srcStride, dst, srcStride);
                }
            }
        }
        finally { _context.Unmap(_stagingTex, 0); }

        using (var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0))
        {
            _context.CopyResource(backBuffer, _stagingTex);
        }

        // Issue A: check Present HRESULT.
        var presentResult = _swapChain.Present(0, PresentFlags.DoNotWait);
        if (presentResult.Failure && presentResult != Vortice.DXGI.ResultCode.WasStillDrawing)
        {
            AppDiagnosticLogger.Runtime(
                $"D3D11_PRESENT_FATAL (CPU) hr=0x{(uint)presentResult.Code:X8} — device lost; render thread stopping");
            _deviceLost = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                () => DeviceLost?.Invoke(this, EventArgs.Empty));
        }
    }

    // Resize swap chain to camera native resolution when it changes.
    // DXGI_SCALING_STRETCH then scales to whatever size the HWND has.
    private void EnsureSwapChainAtCameraRes(int w, int h)
    {
        if (_swapChain is null || (_swapCamW == w && _swapCamH == h)) return;
        try
        {
            _swapChain.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown, SwapChainFlags.None);
            _swapCamW = w; _swapCamH = h;
            // Staging is now wrong size — release it so EnsureStagingTexture reallocates
            ReleaseStagingTex();
            AppDiagnosticLogger.Runtime($"D3D11_SWAP_RESIZE {w}×{h}");
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"D3D11_SWAP_RESIZE_ERR {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void EnsureStagingTexture(int w, int h)
    {
        if (_stagingTex is not null && _stagingW == w && _stagingH == h) return;
        ReleaseStagingTex();

        var desc = new Texture2DDescription
        {
            Width           = (uint)w,
            Height          = (uint)h,
            MipLevels       = 1,
            ArraySize       = 1,
            Format          = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage           = ResourceUsage.Dynamic,
            BindFlags       = BindFlags.ShaderResource,
            CPUAccessFlags  = CpuAccessFlags.Write,
            MiscFlags       = ResourceOptionFlags.None,
        };

        try
        {
            _stagingTex = _device!.CreateTexture2D(desc);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"D3D11_STAGING_FAIL {ex.GetType().Name}: {ex.Message}");
            _stagingTex = null;
            return;
        }
        _stagingW = w; _stagingH = h;
        AppDiagnosticLogger.Runtime($"D3D11_STAGING_ALLOC {w}×{h}");
    }

    // Issue B helper: returns true if the D3D11 texture behind tex was created on _device.
    // Uses ID3D11DeviceChild.GetDevice to retrieve the texture's owning device, then compares
    // its native pointer to _device's.
    private bool TextureIsOnOurDevice(ID3D11Texture2D tex)
    {
        if (_device is null) return false;
        try
        {
            using var texDevice = tex.Device;
            return texDevice.NativePointer == _device.NativePointer;
        }
        catch { return false; }
    }

    private void ReleaseStagingTex()
    {
        _stagingTex?.Dispose();
        _stagingTex = null;
        _stagingW = _stagingH = 0;
    }

    // ── Frame ingestion (capture background thread) ────────────────────────────

    /// <summary>
    /// Accepts a preview frame from the capture pipeline.
    /// For CPU frames: extracts pixels on the capture thread (minimising UI-thread work),
    /// posts to single-slot pending queue (old frame dropped), signals render thread.
    /// For GPU frames: posts IDirect3DSurface directly (zero CPU copies).
    /// Takes ownership of <see cref="V2FrameArrivedEventArgs.SoftwareBitmap"/> and
    /// <see cref="V2FrameArrivedEventArgs.Direct3DSurface"/>.
    /// </summary>
    public void PresentFrame(V2FrameArrivedEventArgs frame)
    {
        if (_disposed)
        {
            frame.SoftwareBitmap?.Dispose();
            frame.Direct3DSurface?.Dispose();
            return;
        }

        int w = frame.Width, h = frame.Height;

        if (frame.Direct3DSurface is { } gpuSurf)
        {
            // GPU path: post surface to render thread
            IDirect3DSurface? oldGpu;
            lock (_pendingLock)
            {
                oldGpu             = _pendingGpuSurface;
                _pendingGpuSurface = gpuSurf;
                _pendingW          = w;
                _pendingH          = h;
            }
            oldGpu?.Dispose(); // drop the frame that was waiting
        }
        else if (frame.SoftwareBitmap is { } bitmap)
        {
            // CPU path: extract pixels on capture thread, then post byte[] to render thread
            w = bitmap.PixelWidth;
            h = bitmap.PixelHeight;
            int bytes = w * h * 4;

            // Reuse WinRT Buffer to avoid per-frame WinRT allocation
            if (_winRtBuffer is null || _winRtBuffer.Capacity < (uint)bytes)
                _winRtBuffer = new Windows.Storage.Streams.Buffer((uint)bytes);
            _winRtBuffer.Length = (uint)bytes;
            bitmap.CopyToBuffer(_winRtBuffer);
            bitmap.Dispose();

            // Pop a recycled pixel array from the pool; allocate if none available or wrong size
            _bufPool.TryTake(out var buf);
            if (buf is null || buf.Length != bytes) buf = new byte[bytes];
            using var reader = DataReader.FromBuffer(_winRtBuffer);
            reader.ReadBytes(buf);

            byte[]? oldBuf;
            lock (_pendingLock)
            {
                oldBuf         = _pendingPixels;
                _pendingPixels = buf;
                _pendingW      = w;
                _pendingH      = h;
            }
            if (oldBuf != null) _bufPool.Add(oldBuf); // return dropped-frame buffer to pool
        }
        else
        {
            frame.SoftwareBitmap?.Dispose();
            frame.Direct3DSurface?.Dispose();
            return;
        }

        _frameSignal.Set(); // wake render thread
    }
}
