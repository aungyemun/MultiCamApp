////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// Letterbox-correct container for D3D11SwapChainHost.
// WPF Panel with black background; sizes the HwndHost child to the camera's
// native aspect ratio so DXGI_SCALING_STRETCH never distorts the image.
// Black bars appear on the sides (pillarbox) or top/bottom (letterbox)
// when the panel's aspect ratio differs from the camera's.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Hosts a <see cref="D3D11SwapChainHost"/> with aspect-ratio–correct letterboxing.
/// Place this element in the camera cell border instead of the HwndHost directly.
/// </summary>
internal sealed class D3D11PreviewPanel : Grid
{
    private readonly D3D11SwapChainHost _host;
    private double _cameraAr = 16.0 / 9.0; // overwritten below when the caller knows the requested resolution

    /// <param name="host">The swap-chain host to wrap.</param>
    /// <param name="initialWidth">
    /// Requested capture width, if known at construction time (e.g. the user's selected
    /// resolution). Used to seed the letterbox aspect ratio before the first real frame
    /// arrives, so a 4:3 selection (e.g. 480p) doesn't render pillarboxed-as-16:9 for the
    /// brief window before <see cref="D3D11SwapChainHost.CameraResolutionKnown"/> fires.
    /// Pass 0 to keep the 16:9 default.
    /// </param>
    /// <param name="initialHeight">Requested capture height; see <paramref name="initialWidth"/>.</param>
    public D3D11PreviewPanel(D3D11SwapChainHost host, int initialWidth = 0, int initialHeight = 0)
    {
        _host             = host;
        Background        = Brushes.Black;
        if (initialWidth > 0 && initialHeight > 0)
            _cameraAr = (double)initialWidth / initialHeight;

        _host.HorizontalAlignment = HorizontalAlignment.Center;
        _host.VerticalAlignment   = VerticalAlignment.Center;
        Children.Add(host);

        // Recompute letterbox rect whenever the panel or the camera AR changes
        SizeChanged += (_, _) => UpdateHostSize();

        // Camera tells us its native resolution after the first frame
        host.CameraResolutionKnown += OnCameraResolutionKnown;

        // A brand-new D3D11PreviewPanel is created every time a camera slot is (re)opened —
        // including when the Resolution/FPS dropdowns trigger a close+reopen on an already-open
        // slot (see MainWindow.ReapplyV2VideoSettingsToActivePreviewAsync, v1.2.39+). _host's
        // underlying swap chain starts at a tiny 2×2 placeholder, and _host.Width/Height are
        // never explicitly set until UpdateHostSize() runs — which previously only happened
        // reactively via SizeChanged (doesn't fire if the panel is placed into a
        // container that's already the same size as before) or CameraResolutionKnown (only
        // fires once the first real frame arrives, which can take a moment). In that gap, the
        // host rendered at its own tiny natural size instead of filling the cell — visible as a
        // small floating image in an otherwise black panel after changing resolution/FPS mid
        // session. Sizing proactively on Loaded (fires once the panel has real layout
        // dimensions, regardless of whether they changed) closes that gap.
        Loaded += (_, _) => UpdateHostSize();
    }

    private void OnCameraResolutionKnown(int w, int h)
    {
        if (w > 0 && h > 0)
            SetCameraAspectRatio((double)w / h);
    }

    /// <summary>Updates the letterbox layout for the given camera aspect ratio.</summary>
    public void SetCameraAspectRatio(double ar)
    {
        _cameraAr = Math.Max(0.01, ar);
        UpdateHostSize();
    }

    private void UpdateHostSize()
    {
        double panW = ActualWidth;
        double panH = ActualHeight;
        if (panW <= 0 || panH <= 0) return;

        double panAr = panW / panH;
        double hostW, hostH;
        if (panAr > _cameraAr)
        {
            // Wider than camera → pillarbox: full height, reduced width
            hostH = panH;
            hostW = panH * _cameraAr;
        }
        else
        {
            // Taller than camera → letterbox: full width, reduced height
            hostW = panW;
            hostH = panW / _cameraAr;
        }

        _host.Width  = hostW;
        _host.Height = hostH;
    }
}
