// v1.2.0-alpha — backend initialization diagnostics container.
// Produced once per backend activation. Written to logs and surfaced in metadata.
// Do not write fake success values — use "Unknown" / "NotMeasured" when not available.

namespace MultiCamApp.Capture.Backend;

/// <summary>
/// Point-in-time diagnostics snapshot produced during backend initialization.
/// Each backend implements <see cref="IVideoEngineBackend.GetInitDiagnostics"/> to return this.
/// </summary>
public sealed class BackendInitDiagnostics
{
    public string BackendId             { get; init; } = "";
    public bool   InitSucceeded         { get; init; }
    public string InitSummary           { get; init; } = "";
    public string? FailureReason        { get; init; }

    // ── System capability status ──────────────────────────────────────────────
    public string MediaFoundationStatus { get; init; } = "Unknown";
    public string Direct3D11Status      { get; init; } = "Unknown";
    public string EncoderStatus         { get; init; } = "Unknown";
    public string PreviewRendererStatus { get; init; } = "Unknown";

    // ── Additional diagnostics (populated when measured, otherwise NotMeasured) ─
    public string UsbTopologyStatus     { get; init; } = "NotMeasured";
    public string WatchdogStatus        { get; init; } = "NotMeasured";
    public string StorageStatus         { get; init; } = "NotMeasured";
    public string CameraCapabilityStatus { get; init; } = "NotMeasured";
    public string EncoderDiagnostics    { get; init; } = "NotMeasured";
    public string PreviewPerformance    { get; init; } = "NotMeasured";

    public DateTimeOffset Timestamp     { get; } = DateTimeOffset.UtcNow;

    // ── Factory helpers ───────────────────────────────────────────────────────

    public static BackendInitDiagnostics Success(string backendId, string summary) => new()
    {
        BackendId     = backendId,
        InitSucceeded = true,
        InitSummary   = summary,
    };

    public static BackendInitDiagnostics Failure(string backendId, string reason) => new()
    {
        BackendId     = backendId,
        InitSucceeded = false,
        InitSummary   = $"Initialization failed: {reason}",
        FailureReason = reason,
    };
}
