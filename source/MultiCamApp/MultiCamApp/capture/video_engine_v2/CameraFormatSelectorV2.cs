////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Selects the best capture format from a camera's reported format list.
/// Priority order:
///   1. Exact match for the caller's request (W × H @ preferredFps, preferred pixel format)
///   2. Requested resolution at 30 fps, if step 1 failed and preferredFps ≠ 30 — keeps the
///      user's chosen resolution even when their chosen FPS isn't natively available there,
///      instead of silently falling through to a different (usually higher) resolution.
///   3. Priority ladder at preferred FPS (if ≠ 30): 1080p → 720p → 360p → VGA, MJPEG/NV12 → YUY2 → Any
///   4. Same ladder at 30 fps (fallback when the camera doesn't support the requested resolution at all)
///   5. Closest format by pixel-area proximity then FPS proximity
/// </summary>
public sealed class CameraFormatSelectorV2
{
    // Resolution preference: tried in this order within each FPS tier.
    private static readonly (int W, int H)[] _resolutionLadder =
    [
        (1920, 1080),
        (1280,  720),
        ( 640,  360),
        ( 640,  480),
    ];

    // Pixel-format preference groups: each inner array is tried before moving to the next group.
    private static readonly V2PixelFormat[][] _formatLadder =
    [
        [V2PixelFormat.Mjpeg, V2PixelFormat.Nv12],
        [V2PixelFormat.Yuy2],
        [V2PixelFormat.Any],
    ];

    /// <summary>
    /// Selects the best format from <paramref name="available"/> matching <paramref name="request"/>.
    /// Returns a <see cref="V2FormatSelectionResult"/> with the chosen format and fallback diagnostics.
    /// </summary>
    public V2FormatSelectionResult Select(
        IReadOnlyList<V2CaptureFormat> available,
        V2CaptureFormatRequest request)
    {
        if (available.Count == 0)
            return V2FormatSelectionResult.NoFormats();

        // 1. Exact match for caller's request
        var exact = Find(available, request.PreferredWidth, request.PreferredHeight,
                         request.PreferredFps, request.PreferredPixelFormat);
        if (exact is not null)
            return V2FormatSelectionResult.Exact(exact);

        // 2. Same requested resolution at 30 fps (the most commonly supported rate), before
        //    falling through to a different resolution entirely. Without this step, requesting
        //    e.g. 480p or 720p at a non-30 fps (24/60/etc.) that the camera doesn't support at
        //    that exact resolution fell straight into the resolution ladder below — which tries
        //    1080p FIRST regardless of what the user actually asked for, since cheap webcams
        //    commonly support 1080p@30fps as a baseline mode. That silently "upgraded" any
        //    non-30fps request at ANY resolution to 1080p, defeating the entire point of picking
        //    a lower resolution. Confirmed via real recordings: requesting 480p/720p@60fps or
        //    @24fps consistently landed on 1920x1080 instead. Skipped when the request is already
        //    at 30fps (already covered by the exact-match check above).
        if (Math.Abs(request.PreferredFps - 30.0) > 0.5)
        {
            foreach (var fmtGroup in _formatLadder)
                foreach (var pf in fmtGroup)
                {
                    var sameResCandidate = Find(available, request.PreferredWidth, request.PreferredHeight, 30.0, pf);
                    if (sameResCandidate is null) continue;

                    var sameResMsg =
                        $"Exact {request.PreferredWidth}×{request.PreferredHeight}@{request.PreferredFps}" +
                        $" [{request.PreferredPixelFormat}] not available; kept requested resolution at 30fps: {sameResCandidate}";
                    return V2FormatSelectionResult.PriorityFallback(sameResCandidate, sameResMsg);
                }
        }

        // 3. Priority ladder: try preferred FPS first, then 30 fps as fallback, across every
        //    resolution tier. Only reached if step 2 above didn't find the requested resolution
        //    at any pixel format/30fps — i.e. the camera genuinely doesn't support the requested
        //    resolution at all, so falling back to a different resolution is unavoidable.
        double[] fpsLadder = Math.Abs(request.PreferredFps - 30.0) > 0.5
            ? [request.PreferredFps, 30.0]
            : [30.0];

        foreach (var fps in fpsLadder)
            foreach (var (w, h) in _resolutionLadder)
                foreach (var fmtGroup in _formatLadder)
                    foreach (var pf in fmtGroup)
                    {
                        var candidate = Find(available, w, h, fps, pf);
                        if (candidate is null) continue;

                        var msg =
                            $"Exact {request.PreferredWidth}×{request.PreferredHeight}@{request.PreferredFps}" +
                            $" [{request.PreferredPixelFormat}] not available; selected {candidate}";
                        return V2FormatSelectionResult.PriorityFallback(candidate, msg);
                    }

        // 3. Closest fallback
        var closest = available
            .OrderBy(f => Math.Abs(f.Width * f.Height - request.PreferredWidth * request.PreferredHeight))
            .ThenBy(f => Math.Abs(f.NominalFps - request.PreferredFps))
            .First();

        return V2FormatSelectionResult.ClosestFallback(closest,
            $"No priority format available; closest is {closest}");
    }

    private static V2CaptureFormat? Find(
        IReadOnlyList<V2CaptureFormat> available, int w, int h, double fps, V2PixelFormat pf)
    {
        return available.FirstOrDefault(f =>
            f.Width  == w &&
            f.Height == h &&
            Math.Abs(f.NominalFps - fps) < 0.5 &&
            (pf == V2PixelFormat.Any || f.PixelFormat == pf || f.PixelFormat == V2PixelFormat.Any));
    }
}

// ── Supporting types ─────────────────────────────────────────────────────────

/// <summary>Result of a format selection, including the chosen format and fallback diagnostics.</summary>
public sealed class V2FormatSelectionResult
{
    public V2CaptureFormat? SelectedFormat { get; init; }
    public V2FormatSelectionKind Kind      { get; init; }
    public string? FallbackReason          { get; init; }

    internal static V2FormatSelectionResult Exact(V2CaptureFormat f) =>
        new() { SelectedFormat = f, Kind = V2FormatSelectionKind.ExactMatch };

    internal static V2FormatSelectionResult PriorityFallback(V2CaptureFormat f, string reason) =>
        new() { SelectedFormat = f, Kind = V2FormatSelectionKind.PriorityFallback, FallbackReason = reason };

    internal static V2FormatSelectionResult ClosestFallback(V2CaptureFormat f, string reason) =>
        new() { SelectedFormat = f, Kind = V2FormatSelectionKind.ClosestFallback, FallbackReason = reason };

    internal static V2FormatSelectionResult NoFormats() =>
        new() { Kind = V2FormatSelectionKind.NoFormatsAvailable, FallbackReason = "Device reported no supported formats." };
}

/// <summary>How the format was chosen.</summary>
public enum V2FormatSelectionKind
{
    ExactMatch,
    PriorityFallback,
    ClosestFallback,
    NoFormatsAvailable,
}

/// <summary>Describes one capture format reported by the camera or driver.</summary>
public sealed class V2CaptureFormat
{
    public int Width        { get; init; }
    public int Height       { get; init; }
    /// <summary>Nominal FPS from the device media type. Not guaranteed to be delivered.</summary>
    public double NominalFps { get; init; }
    public V2PixelFormat PixelFormat { get; init; }
    /// <summary>Raw MF subtype name if known at enumeration time (e.g. "MJPEG", "YUY2").</summary>
    public string? SubtypeName { get; init; }
    /// <summary>True if this format was confirmed on real hardware by the Capability Scanner.</summary>
    public bool IsConfirmed { get; init; }
    /// <summary>How this format entry was discovered during device enumeration.</summary>
    public V2FormatSource FormatSource { get; init; }
    public override string ToString() =>
        $"{Width}×{Height}@{NominalFps:F2} {SubtypeName ?? PixelFormat.ToString()}";
}

/// <summary>Caller-specified constraints for format selection.</summary>
public sealed class V2CaptureFormatRequest
{
    public int PreferredWidth    { get; init; } = 1920;
    public int PreferredHeight   { get; init; } = 1080;
    public double PreferredFps   { get; init; } = 30.0;
    public V2PixelFormat PreferredPixelFormat { get; init; } = V2PixelFormat.Mjpeg;
}

/// <summary>Camera pixel/subtype formats relevant to VideoEngineV2.</summary>
public enum V2PixelFormat
{
    Any,
    Mjpeg,
    Yuy2,
    Nv12,
    Bgra8,
    Rgb24,
}

/// <summary>How a <see cref="V2CaptureFormat"/> entry was discovered during device enumeration.</summary>
public enum V2FormatSource
{
    /// <summary>From <see cref="Windows.Media.Capture.Frames.MediaFrameSourceGroup.FindAllAsync"/>.</summary>
    FrameSourceGroup,
    /// <summary>From <see cref="Windows.Media.Capture.MediaCapture.FindKnownVideoProfiles"/> (VideoRecording profile).</summary>
    KnownVideoProfile,
    /// <summary>Synthetic fallback when the camera reports no video profiles.</summary>
    FallbackStandard,
}
