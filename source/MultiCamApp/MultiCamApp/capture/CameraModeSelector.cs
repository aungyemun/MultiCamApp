////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using MultiCamApp.Core;

namespace MultiCamApp.Capture;

public sealed class CameraModeSelector
{
    public IReadOnlyList<CameraMode> ListSupportedModes(MediaCapture capture)
    {
        var list = new List<CameraMode>();
        var props = capture.VideoDeviceController
            .GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord);
        foreach (var p in props)
        {
            if (p is not VideoEncodingProperties vep) continue;
            var fps = vep.FrameRate.Denominator > 0
                ? (double)vep.FrameRate.Numerator / vep.FrameRate.Denominator
                : 30;
            list.Add(new CameraMode
            {
                Width = (int)vep.Width,
                Height = (int)vep.Height,
                Fps = fps,
                RequestedFps = fps,
                SelectedDeviceFps = fps,
                SelectionReason = "supported"
            });
        }
        return list.DistinctBy(m => $"{m.Width}x{m.Height}@{m.Fps:F1}").ToList();
    }

    public CameraMode SelectBest(MediaCapture capture, AppConfig config)
    {
        var modes = ListSupportedModes(capture);
        var requestedFps = config.PreferFps > 0 ? config.PreferFps : 30;
        if (modes.Count == 0)
            return new CameraMode
            {
                Width = 1280,
                Height = 720,
                Fps = requestedFps,
                RequestedFps = requestedFps,
                SelectedDeviceFps = requestedFps,
                SelectionReason = "fallback_default"
            };

        var native = capture.VideoDeviceController
            .GetMediaStreamProperties(MediaStreamType.VideoRecord) as VideoEncodingProperties;
        var nativeW = native != null ? (int)native.Width : 0;
        var nativeH = native != null ? (int)native.Height : 0;
        var desiredW = config.PreferredCaptureWidth > 0 ? config.PreferredCaptureWidth : nativeW;
        var desiredH = config.PreferredCaptureHeight > 0 ? config.PreferredCaptureHeight : nativeH;
        var hasDesiredResolution = desiredW > 0 && desiredH > 0;

        if (config.ForceFixedResolution)
        {
            if (desiredW > 0 && desiredH > 0)
            {
                var forced = modes
                    .Where(m => m.Width == desiredW && m.Height == desiredH)
                    .OrderBy(m => Math.Abs(m.Fps - requestedFps))
                    .FirstOrDefault();
                if (forced != null)
                    return forced with
                    {
                        SelectionReason = $"forced_{desiredW}x{desiredH}",
                        RequestedFps = requestedFps,
                        SelectedDeviceFps = forced.Fps,
                        IsNativeRecommended = false
                    };
            }
            else
            {
                var fixed1080 = modes
                    .Where(m => m.Width == 1920 && m.Height == 1080)
                    .OrderBy(m => Math.Abs(m.Fps - requestedFps))
                    .FirstOrDefault();
                if (fixed1080 != null)
                    return fixed1080 with
                    {
                        SelectionReason = "forced_1080p",
                        RequestedFps = requestedFps,
                        SelectedDeviceFps = fixed1080.Fps,
                        IsNativeRecommended = false
                    };
            }
        }

        var ranked = modes
            .Select(m => (Mode: m, Score: ScoreValue(m, requestedFps, desiredW, desiredH, hasDesiredResolution, nativeW, nativeH)))
            .OrderBy(x => x.Score.ResolutionDistance)
            .ThenBy(x => x.Score.FpsDelta)
            .ThenByDescending(x => x.Score.NativeRecommended)
            .ThenByDescending(x => x.Score.StabilityScore)
            .ThenByDescending(x => x.Score.PixelArea)
            .ToList();

        var pick = ranked[0].Mode;
        var reason = ReasonFor(pick, nativeW, nativeH);
        return pick with
        {
            SelectionReason = reason,
            RequestedFps = requestedFps,
            SelectedDeviceFps = pick.Fps,
            IsNativeRecommended = nativeW > 0 && pick.Width == nativeW && pick.Height == nativeH
        };
    }

    private static string ReasonFor(CameraMode m, int nativeW, int nativeH)
    {
        if (nativeW > 0 && m.Width == nativeW && m.Height == nativeH) return "native_recommended";
        if (m.Width == 1920 && m.Height == 1080 && Math.Abs(m.Fps - 30) < 1.1) return "1080p30_stable";
        return "best_stable";
    }

    private static ModeRank ScoreValue(
        CameraMode m,
        double requestedFps,
        int desiredW,
        int desiredH,
        bool hasDesiredResolution,
        int nativeW,
        int nativeH)
    {
        var fpsDelta = Math.Abs(m.Fps - requestedFps);
        var resolutionDistance = hasDesiredResolution
            ? Math.Abs(m.Width - desiredW) + Math.Abs(m.Height - desiredH)
            : nativeW > 0 && nativeH > 0
                ? (m.Width == nativeW && m.Height == nativeH ? 0 : Math.Abs(m.Width * m.Height - nativeW * nativeH))
                : Math.Abs(m.Width * m.Height - 1920 * 1080);
        var stabilityScore = 0;
        if (m.IsNativeRecommended || (nativeW > 0 && m.Width == nativeW && m.Height == nativeH))
            stabilityScore += 10;
        if (IsCommonStableFps(m.Fps))
            stabilityScore += 5;
        if (m.Width == 1920 && m.Height == 1080)
            stabilityScore += 3;
        if (m.Width <= 1280 && m.Height <= 720)
            stabilityScore += 1;

        return new ModeRank(
            resolutionDistance,
            fpsDelta,
            m.IsNativeRecommended || (nativeW > 0 && m.Width == nativeW && m.Height == nativeH),
            stabilityScore,
            (long)m.Width * m.Height);
    }

    public MediaEncodingProfile BuildProfile(MediaCapture capture, CameraMode mode)
    {
        var chosen = FindBestVideoProperties(capture, mode);

        if (chosen == null)
        {
            var fallback = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            fallback.Audio = null;
            return fallback;
        }

        ApplyFrameRate(chosen, mode.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode.Fps);

        var quality = mode.IsNativeRecommended
            ? VideoEncodingQuality.Auto
            : VideoEncodingQuality.HD1080p;
        var mp4 = MediaEncodingProfile.CreateMp4(quality);
        mp4.Video = chosen;
        mp4.Audio = null;
        return mp4;
    }

    public async Task ApplyRecordModeAsync(MediaCapture capture, CameraMode mode)
    {
        var chosen = FindBestVideoProperties(capture, mode);

        if (chosen == null) return;

        ApplyFrameRate(chosen, mode.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode.Fps);
        await capture.VideoDeviceController.SetMediaStreamPropertiesAsync(
            MediaStreamType.VideoRecord, chosen);
    }

    private static VideoEncodingProperties? FindBestVideoProperties(MediaCapture capture, CameraMode mode)
    {
        var props = capture.VideoDeviceController
            .GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord)
            .OfType<VideoEncodingProperties>()
            .ToList();
        if (props.Count == 0)
            return null;

        var matching = props
            .Where(v => v.Width == (uint)mode.Width && v.Height == (uint)mode.Height)
            .ToList();
        var candidates = matching.Count > 0 ? matching : props;
        return candidates
            .OrderBy(v => Math.Abs((long)v.Width * v.Height - (long)mode.Width * mode.Height))
            .ThenBy(v => Math.Abs(GetFrameRate(v) - (mode.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode.Fps)))
            .FirstOrDefault();
    }

    private static double GetFrameRate(VideoEncodingProperties vep) =>
        vep.FrameRate.Denominator > 0
            ? (double)vep.FrameRate.Numerator / vep.FrameRate.Denominator
            : 0;

    private static void ApplyFrameRate(VideoEncodingProperties chosen, double fps)
    {
        var (numerator, denominator) = NormalizeFrameRate(fps);
        chosen.FrameRate.Numerator = numerator;
        chosen.FrameRate.Denominator = denominator;
    }

    private static (uint Numerator, uint Denominator) NormalizeFrameRate(double fps)
    {
        if (fps <= 0)
            return (30, 1);

        foreach (var (target, num, den) in CommonFrameRates)
        {
            if (Math.Abs(fps - target) <= 0.05)
                return (num, den);
        }

        var scaled = Math.Max(1, (uint)Math.Round(fps * 1000));
        return ReduceFraction(scaled, 1000);
    }

    private static (uint Numerator, uint Denominator) ReduceFraction(uint numerator, uint denominator)
    {
        var a = numerator;
        var b = denominator;
        while (b != 0)
        {
            var t = a % b;
            a = b;
            b = t;
        }

        var gcd = Math.Max(1, a);
        return (Math.Max(1, numerator / gcd), Math.Max(1, denominator / gcd));
    }

    private static bool IsCommonStableFps(double fps) =>
        CommonFrameRates.Any(r => Math.Abs(r.Target - fps) <= 0.05);

    private static readonly (double Target, uint Numerator, uint Denominator)[] CommonFrameRates =
    [
        (23.976, 24000, 1001),
        (24.0, 24, 1),
        (25.0, 25, 1),
        (29.97, 30000, 1001),
        (30.0, 30, 1),
        (50.0, 50, 1),
        (59.94, 60000, 1001),
        (60.0, 60, 1)
    ];

    private sealed record ModeRank(
        int ResolutionDistance,
        double FpsDelta,
        bool NativeRecommended,
        int StabilityScore,
        long PixelArea);
}
