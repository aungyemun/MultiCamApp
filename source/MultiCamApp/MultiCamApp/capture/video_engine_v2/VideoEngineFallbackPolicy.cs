////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — backend tier definitions and availability rules.
// No internet required. Evaluated entirely from RuntimeCapabilityReport.

using MultiCamApp.Diagnostics;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Defines the ordered list of backend tiers MultiCamApp can use and the
/// runtime conditions that must be met for each.
/// </summary>
/// <remarks>
/// Tier evaluation order (highest preferred first):
///   1. MediaFoundation + Direct3D11 + Hardware H.264
///   2. MediaFoundation + Direct3D11 + Software H.264
///   3. MediaFoundation + WPF fallback + Software H.264
///   4. Legacy OpenCV  + WPF          + OpenCV MPEG-4
///   5. Preview-only   (recording unavailable — user warned)
/// </remarks>
public static class VideoEngineFallbackPolicy
{
    private static readonly BackendCandidate[] _tiers =
    [
        new BackendCandidate
        {
            Tier            = BackendTier.Tier1_MF_D3D_HWEncoder,
            Description     = "Media Foundation + Direct3D 11 + Hardware H.264",
            Rationale       = "Best quality and lowest CPU overhead. Uses NVENC / QuickSync / AMF.",
            CaptureBackend  = VideoEngineBackend.MediaFoundation,
            PreviewRenderer = PreviewRendererType.Direct3D,
            EncoderBackend  = EncoderBackendType.MediaFoundationH264,
        },
        new BackendCandidate
        {
            Tier            = BackendTier.Tier2_MF_D3D_SWEncoder,
            Description     = "Media Foundation + Direct3D 11 + Software H.264",
            Rationale       = "D3D11 preview available but no hardware H.264 encoder detected.",
            CaptureBackend  = VideoEngineBackend.MediaFoundation,
            PreviewRenderer = PreviewRendererType.Direct3D,
            EncoderBackend  = EncoderBackendType.MediaFoundationSoftwareH264,
        },
        new BackendCandidate
        {
            Tier            = BackendTier.Tier3_MF_WPF_SWEncoder,
            Description     = "Media Foundation + WPF preview + Software H.264",
            Rationale       = "D3D11 unavailable or Basic Render Driver active. WPF WriteableBitmap used for preview.",
            CaptureBackend  = VideoEngineBackend.MediaFoundation,
            PreviewRenderer = PreviewRendererType.WpfFallback,
            EncoderBackend  = EncoderBackendType.MediaFoundationSoftwareH264,
        },
        new BackendCandidate
        {
            Tier            = BackendTier.Tier4_LegacyOpenCv,
            Description     = "Legacy OpenCV capture + WPF preview + MPEG-4",
            Rationale       = "Media Foundation unavailable. Using stable STABLE_CORE_V1 OpenCV pipeline.",
            CaptureBackend  = VideoEngineBackend.Legacy,
            PreviewRenderer = PreviewRendererType.WpfFallback,
            EncoderBackend  = EncoderBackendType.LegacyOpenCvMp4v,
        },
        new BackendCandidate
        {
            Tier            = BackendTier.Tier5_PreviewOnly,
            Description     = "Preview only — recording unavailable",
            Rationale       = "Neither Media Foundation nor OpenCV is available.",
            CaptureBackend  = VideoEngineBackend.Legacy,
            PreviewRenderer = PreviewRendererType.None,
            EncoderBackend  = EncoderBackendType.NotSelected,
            IsPreviewOnly   = true,
        },
    ];

    /// <summary>All defined tiers in priority order (tier 1 first).</summary>
    public static IReadOnlyList<BackendCandidate> Tiers => _tiers;

    /// <summary>
    /// Returns true when the supplied capability report satisfies all requirements
    /// for the given tier.
    /// </summary>
    public static bool IsTierAvailable(BackendCandidate tier, RuntimeCapabilityReport caps) =>
        tier.Tier switch
        {
            BackendTier.Tier1_MF_D3D_HWEncoder =>
                caps.MediaFoundationAvailable
                && caps.Direct3D11Available
                && caps.H264HardwareEncoderAvailable
                && !caps.HasMicrosoftBasicRenderDriver,

            BackendTier.Tier2_MF_D3D_SWEncoder =>
                caps.MediaFoundationAvailable
                && caps.Direct3D11Available
                && caps.H264SoftwareEncoderAvailable,

            BackendTier.Tier3_MF_WPF_SWEncoder =>
                caps.MediaFoundationAvailable
                && caps.H264SoftwareEncoderAvailable,

            BackendTier.Tier4_LegacyOpenCv =>
                caps.OpenCvNativeDllLoaded,

            BackendTier.Tier5_PreviewOnly => true, // always available as last resort

            _ => false,
        };

    /// <summary>
    /// Returns a human-readable explanation of why a tier was skipped.
    /// Returns null if the tier is available.
    /// </summary>
    public static string? GetSkipReason(BackendCandidate tier, RuntimeCapabilityReport caps) =>
        IsTierAvailable(tier, caps) ? null : tier.Tier switch
        {
            BackendTier.Tier1_MF_D3D_HWEncoder => BuildTier1SkipReason(caps),
            BackendTier.Tier2_MF_D3D_SWEncoder => BuildTier2SkipReason(caps),
            BackendTier.Tier3_MF_WPF_SWEncoder => BuildTier3SkipReason(caps),
            BackendTier.Tier4_LegacyOpenCv     => "OpenCvSharpExtern.dll could not be loaded.",
            _                                  => "Requirements not met.",
        };

    private static string BuildTier1SkipReason(RuntimeCapabilityReport caps)
    {
        var reasons = new List<string>();
        if (!caps.MediaFoundationAvailable)        reasons.Add("Media Foundation unavailable");
        if (!caps.Direct3D11Available)             reasons.Add("Direct3D 11 unavailable");
        if (!caps.H264HardwareEncoderAvailable)    reasons.Add("no hardware H.264 encoder");
        if (caps.HasMicrosoftBasicRenderDriver)    reasons.Add("Microsoft Basic Render Driver active");
        return string.Join("; ", reasons);
    }

    private static string BuildTier2SkipReason(RuntimeCapabilityReport caps)
    {
        var reasons = new List<string>();
        if (!caps.MediaFoundationAvailable)     reasons.Add("Media Foundation unavailable");
        if (!caps.Direct3D11Available)          reasons.Add("Direct3D 11 unavailable");
        if (!caps.H264SoftwareEncoderAvailable) reasons.Add("no software H.264 encoder");
        return string.Join("; ", reasons);
    }

    private static string BuildTier3SkipReason(RuntimeCapabilityReport caps)
    {
        var reasons = new List<string>();
        if (!caps.MediaFoundationAvailable)     reasons.Add("Media Foundation unavailable");
        if (!caps.H264SoftwareEncoderAvailable) reasons.Add("no H.264 encoder (hardware or software)");
        return string.Join("; ", reasons);
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

/// <summary>One entry in the backend tier priority list.</summary>
public sealed class BackendCandidate
{
    public BackendTier Tier { get; init; }
    public string Description { get; init; } = "";
    public string Rationale { get; init; } = "";
    public VideoEngineBackend CaptureBackend { get; init; }
    public PreviewRendererType PreviewRenderer { get; init; }
    public EncoderBackendType EncoderBackend { get; init; }
    public bool IsPreviewOnly { get; init; }
}

/// <summary>Backend tier identifier used in logging and metadata.</summary>
public enum BackendTier
{
    NotEvaluated          = 0,
    Tier1_MF_D3D_HWEncoder = 1,
    Tier2_MF_D3D_SWEncoder = 2,
    Tier3_MF_WPF_SWEncoder = 3,
    Tier4_LegacyOpenCv    = 4,
    Tier5_PreviewOnly     = 5,
}
