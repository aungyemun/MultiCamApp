////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Capture;

/// <summary>Shared resolution / multi-camera timing helpers (720p, 1080p, 4-cam USB).</summary>
public static class CaptureResolutionHelper
{
    public static bool IsFullHd(int width, int height) =>
        width >= 1920 && height >= 1080;

    public static bool IsHdOrHigher(int preferredWidth) =>
        preferredWidth >= 1280;

    public static bool NeedsMultiCameraStagger(int layoutCount, int preferredWidth) =>
        layoutCount >= 3 && IsHdOrHigher(preferredWidth);

    /// <summary>
    /// Keep Start Preview parallel. Cleanup races are handled by cancellation/release gates, not by
    /// serializing camera open tasks, because serial opens make 2-camera 1080p startup unnecessarily slow.
    /// </summary>
    public static bool UseSequentialPreviewOpen(int layoutCount, int preferredWidth, int preferredHeight) =>
        false;

    /// <summary>Delay between opening/reapplying cameras to reduce USB contention (cam3/cam4).</summary>
    public static int MultiCameraStaggerMs(int layoutCount, int preferredWidth, int preferredHeight)
    {
        if (layoutCount < 2 || preferredWidth <= 0)
            return 0;

        if (IsFullHd(preferredWidth, preferredHeight))
            return layoutCount >= 4 ? 600 : 500;

        if (layoutCount >= 3 && IsHdOrHigher(preferredWidth))
            return 350;

        if (layoutCount >= 2 && IsHdOrHigher(preferredWidth))
            return 250;

        return 0;
    }

    public static int LateSlotExtraOpenDelayMs(int slotIndex, int preferredWidth, int preferredHeight)
    {
        if (slotIndex < 2 || preferredWidth <= 0)
            return 0;

        if (slotIndex == 2)
            return IsFullHd(preferredWidth, preferredHeight) ? 350 : IsHdOrHigher(preferredWidth) ? 200 : 0;

        return IsFullHd(preferredWidth, preferredHeight) ? 500 : IsHdOrHigher(preferredWidth) ? 250 : 0;
    }

    /// <summary>Delay between sequential recording starts when 3+ cameras are active (USB contention).</summary>
    public static int RecordingStartStaggerMs(int activeCameraCount, int preferredWidth, int preferredHeight)
    {
        if (activeCameraCount < 3 || preferredWidth <= 0)
            return 0;

        if (IsFullHd(preferredWidth, preferredHeight))
            return activeCameraCount >= 4 ? 400 : 300;

        if (IsHdOrHigher(preferredWidth))
            return 250;

        return 150;
    }
}
