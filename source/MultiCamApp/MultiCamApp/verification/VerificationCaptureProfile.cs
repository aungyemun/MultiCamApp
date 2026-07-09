////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

using MultiCamApp.Capture;
using MultiCamApp.Core;

namespace MultiCamApp.Verification;

/// <summary>
/// UI capture presets (MainWindow resolution/FPS combos) and verification helpers.
/// </summary>
public static class VerificationCaptureProfile
{
    public static readonly (int Width, int Height)[] KnownResolutions =
    [
        (640, 480),
        (1280, 720),
        (1920, 1080)
    ];

    public static readonly double[] KnownFps = [15, 24, 30, 60];

    public static bool TryParseResolution(string? text, out int width, out int height) =>
        CaptureResolutionPreset.TryFromLabel(text, out width, out height);

    public static bool IsKnownResolution(int width, int height) =>
        KnownResolutions.Any(r => r.Width == width && r.Height == height);

    public static bool IsExplicitUserResolution(int width, int height) =>
        width > 0 && height > 0 && IsKnownResolution(width, height);

    public static double NormalizeFps(double fps)
    {
        if (fps <= 0)
            return fps;

        foreach (var known in KnownFps)
        {
            if (Math.Abs(fps - known) < 0.75)
                return known;
        }

        return Math.Round(fps, 3);
    }

    public static bool IsKnownFps(double fps) =>
        KnownFps.Any(f => Math.Abs(f - fps) < 0.75);

    public static (double Warning, double Fail) GetFpsTolerances(double requestedFps, VerificationSettings settings)
    {
        if (requestedFps <= 0)
            return (settings.FpsWarningTolerance, settings.FpsFailTolerance);

        var warn = Math.Max(settings.FpsWarningTolerance, requestedFps * 0.05);
        var fail = Math.Max(settings.FpsFailTolerance, requestedFps * 0.10);
        return (warn, fail);
    }

    public static double ResolveMeasuredFps(VideoProbeData probe, CameraMetadataRecord? meta)
    {
        if (meta != null)
        {
            if (meta.MeasuredCameraFps > 0)
                return meta.MeasuredCameraFps;
            if (meta.SelectedDeviceFps > 0)
                return meta.SelectedDeviceFps;
            if (meta.RecordingWriterFps > 0)
                return meta.RecordingWriterFps;
            if (meta.WriterFps > 0)
                return meta.WriterFps;
            if (meta.ActualFps > 0)
                return meta.ActualFps;
        }

        return probe.Fps;
    }

    public static (int? Width, int? Height) ResolveExpectedDimensions(CameraMetadataRecord meta)
    {
        if (TryParseResolution(meta.RequestedResolution, out var reqW, out var reqH)
            && IsExplicitUserResolution(reqW, reqH))
            return (reqW, reqH);

        if (meta.PixelWidth > 0 && meta.PixelHeight > 0)
            return (meta.PixelWidth, meta.PixelHeight);

        if (TryParseResolution(meta.Resolution, out reqW, out reqH))
            return (reqW, reqH);

        return (null, null);
    }

    public static double? ResolveExpectedFps(CameraMetadataRecord meta)
    {
        if (meta.RequestedFps > 0)
            return NormalizeFps(meta.RequestedFps);

        if (meta.SelectedDeviceFps > 0)
            return NormalizeFps(meta.SelectedDeviceFps);
        if (meta.RecordingWriterFps > 0)
            return NormalizeFps(meta.RecordingWriterFps);
        if (meta.ActualFps > 0)
            return NormalizeFps(meta.ActualFps);

        return null;
    }
}
