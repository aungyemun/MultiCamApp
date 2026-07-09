////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — backend selection from runtime capability report.
// No internet required. Pure selection logic; does not modify any state.

using MultiCamApp.Diagnostics;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Selects the best available VideoEngineV2 backend from a
/// <see cref="RuntimeCapabilityReport"/>, using the tier rules defined in
/// <see cref="VideoEngineFallbackPolicy"/>.
/// </summary>
/// <remarks>
/// Call <see cref="Select"/> once at startup (or after hot-plug events) and
/// store the result in <see cref="VideoEngineSettings"/> before starting any pipeline.
/// </remarks>
public sealed class VideoEngineBackendSelector
{
    /// <summary>
    /// Evaluates all tiers in priority order and returns the first tier whose
    /// requirements are satisfied by <paramref name="capabilities"/>.
    /// </summary>
    public SelectedBackendConfiguration Select(RuntimeCapabilityReport capabilities)
    {
        var skipped = new List<(BackendCandidate Tier, string Reason)>();

        foreach (var tier in VideoEngineFallbackPolicy.Tiers)
        {
            if (!VideoEngineFallbackPolicy.IsTierAvailable(tier, capabilities))
            {
                var reason = VideoEngineFallbackPolicy.GetSkipReason(tier, capabilities) ?? "Requirements not met";
                skipped.Add((tier, reason));
                continue;
            }

            // Found the best available tier
            return new SelectedBackendConfiguration
            {
                Tier             = tier.Tier,
                Description      = tier.Description,
                CaptureBackend   = tier.CaptureBackend,
                PreviewRenderer  = tier.PreviewRenderer,
                EncoderBackend   = tier.EncoderBackend,
                IsPreviewOnly    = tier.IsPreviewOnly,
                FallbackReason   = skipped.Count > 0
                    ? BuildFallbackSummary(skipped)
                    : null,
                SkippedTiers     = skipped.Select(s =>
                    new SkippedTierInfo { Tier = s.Tier.Tier, Reason = s.Reason }).ToList(),
            };
        }

        // Should never reach here because Tier5 is always available
        return new SelectedBackendConfiguration
        {
            Tier            = BackendTier.Tier5_PreviewOnly,
            Description     = "Preview only — no backend available",
            CaptureBackend  = VideoEngineBackend.Legacy,
            PreviewRenderer = PreviewRendererType.None,
            EncoderBackend  = EncoderBackendType.NotSelected,
            IsPreviewOnly   = true,
            FallbackReason  = "All backend tiers were unavailable.",
        };
    }

    private static string BuildFallbackSummary(
        IReadOnlyList<(BackendCandidate Tier, string Reason)> skipped)
    {
        if (skipped.Count == 0) return "";
        if (skipped.Count == 1) return $"Tier {(int)skipped[0].Tier.Tier} skipped: {skipped[0].Reason}";
        return string.Join(" | ",
            skipped.Select(s => $"T{(int)s.Tier.Tier}: {s.Reason}"));
    }
}

// ── Result type ───────────────────────────────────────────────────────────────

/// <summary>The selected backend configuration returned by <see cref="VideoEngineBackendSelector"/>.</summary>
public sealed class SelectedBackendConfiguration
{
    public BackendTier Tier { get; init; }
    public string Description { get; init; } = "";
    public VideoEngineBackend CaptureBackend { get; init; }
    public PreviewRendererType PreviewRenderer { get; init; }
    public EncoderBackendType EncoderBackend { get; init; }
    public bool IsPreviewOnly { get; init; }
    /// <summary>Non-null when higher tiers were skipped; explains why.</summary>
    public string? FallbackReason { get; init; }
    public List<SkippedTierInfo> SkippedTiers { get; init; } = [];

    public bool IsHardwareEncoder =>
        EncoderBackend == EncoderBackendType.MediaFoundationH264;

    public bool IsMediaFoundationCapture =>
        CaptureBackend == VideoEngineBackend.MediaFoundation;

    public override string ToString() =>
        $"[Tier {(int)Tier}] {Description}" +
        (FallbackReason is not null ? $" (fallback: {FallbackReason})" : "");
}

/// <summary>Records why a candidate tier was not selected.</summary>
public sealed class SkippedTierInfo
{
    public BackendTier Tier { get; init; }
    public string Reason { get; init; } = "";
}
