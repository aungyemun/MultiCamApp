////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// Win32/WinRT interop glue for the GPU swap-chain preview renderer that Vortice.Windows
// does not provide: raw Win32 child-window creation, and the WinRT <-> DXGI device bridge
// used to hand MediaCapture a shared Direct3D11 device (Windows.Graphics.DirectX.Direct3D11
// has no public managed projection of the native IDirect3D11Device factory function).
//
// All core D3D11/DXGI COM interop (ID3D11Device, ID3D11DeviceContext, IDXGIFactory2,
// IDXGISwapChain1, etc.) is provided by the Vortice.Direct3D11 / Vortice.DXGI NuGet
// packages — see D3D11SwapChainHost.cs.

using System.Runtime.InteropServices;

namespace MultiCamApp.Capture.VideoEngineV2;

internal static partial class D3D11Native
{
    /// <summary>
    /// Wraps a DXGI device as a WinRT IDirect3DDevice so it can be passed to
    /// MediaCaptureInitializationSettings.Direct3D11Device for GPU frame sharing.
    /// Exported from d3d11.dll. Vortice has no WinRT projection layer, so this stays
    /// a direct P/Invoke; the IDXGIDevice pointer passed in comes from Vortice's
    /// ID3D11Device.QueryInterface&lt;IDXGIDevice&gt;().NativePointer.
    /// </summary>
    [DllImport("d3d11.dll", CallingConvention = CallingConvention.Winapi,
               EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", PreserveSig = true)]
    internal static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);
}

internal static class Win32Window
{
    private const uint WS_CHILD        = 0x40000000u;
    private const uint WS_VISIBLE      = 0x10000000u;
    private const uint WS_CLIPCHILDREN = 0x02000000u;
    private const uint WS_CLIPSIBLINGS = 0x04000000u;
    private const uint WS_EX_NOPARENTNOTIFY = 0x00000004u;

    // "STATIC" uses COLOR_WINDOW (white) as background — visible before the first D3D11 Present.
    // Use "MultiCamPreviewHost" — a custom class registered with NULL_BRUSH (transparent/no-paint)
    // so the HWND never flashes white before the D3D11 swap chain presents the first black frame.
    private const string _className = "MultiCamPreviewHost";
    private static bool  _classRegistered;
    private static readonly object _classLock = new();
    // Keep a static delegate so the GC never collects the function pointer while any HWND is alive.
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProc _defWndProcDelegate = DefWindowProcW;

    internal static IntPtr CreateChild(IntPtr parent, int w, int h)
    {
        EnsureClassRegistered();
        return CreateWindowExW(WS_EX_NOPARENTNOTIFY, _className, null,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, Math.Max(1, w), Math.Max(1, h),
            parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private static void EnsureClassRegistered()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;
            // NULL_BRUSH (5) = GDI stock object: tells Windows not to erase the window background.
            // This avoids the white flash before D3D11's first Present clears the surface to black.
            var cls = new WNDCLASSEXW
            {
                cbSize        = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style         = 0,
                lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_defWndProcDelegate),
                cbClsExtra    = 0,
                cbWndExtra    = 0,
                hInstance     = IntPtr.Zero,
                hIcon         = IntPtr.Zero,
                hCursor       = IntPtr.Zero,
                hbrBackground = GetStockObject(5), // NULL_BRUSH — no background erase
                lpszMenuName  = null,
                lpszClassName = _className,
                hIconSm       = IntPtr.Zero,
            };
            RegisterClassExW(ref cls);
            _classRegistered = true;
        }
    }

    internal static void Destroy(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) NativeDestroyWindow(hwnd);
    }

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode,
               EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi, EntryPoint = "DestroyWindow")]
    private static extern bool NativeDestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode,
               EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode,
               EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr GetStockObject(int fnObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint      cbSize;
        public uint      style;
        public IntPtr    lpfnWndProc;
        public int       cbClsExtra;
        public int       cbWndExtra;
        public IntPtr    hInstance;
        public IntPtr    hIcon;
        public IntPtr    hCursor;
        public IntPtr    hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string?   lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string    lpszClassName;
        public IntPtr    hIconSm;
    }
}

/// <summary>
/// WinRT <see cref="Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface"/>'s private
/// DXGI-interop interface — allows pulling the underlying DXGI/D3D11 surface pointer out
/// of a GPU frame MediaCapture delivers. Not part of core D3D11/DXGI, so Vortice.Windows
/// doesn't cover it; the returned pointer is wrapped as a Vortice <c>ID3D11Texture2D</c>
/// at the call site (see D3D11SwapChainHost.PresentGpuSurface).
/// </summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    [PreserveSig] int GetInterface([In] ref Guid iid, out IntPtr ppv);
}
