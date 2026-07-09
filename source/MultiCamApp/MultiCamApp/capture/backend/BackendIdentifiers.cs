////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// v1.2.22-alpha — V3/V3B removed; V2 Stable is the only backend.

namespace MultiCamApp.Capture.Backend;

/// <summary>Backend stability mode reported in metadata.</summary>
public enum BackendMode
{
    Stable,
    Experimental,
}

/// <summary>Well-known backend identifier strings.</summary>
public static class BackendIds
{
    public const string VideoEngineV2Stable = "VideoEngineV2_Stable";
}

/// <summary>
/// Backend metadata for per-camera TXT and JSON output (v1.2.0+).
/// All fields default to "Unknown" or "NotAvailable" — never populate with fake success.
/// </summary>
public sealed record BackendMetadata
{
    // ── Backend identity ──────────────────────────────────────────────────────
    public string RecordingBackend        { get; init; } = "Unknown";
    public string BackendVersion          { get; init; } = "Unknown";
    public string BackendMode             { get; init; } = "Stable";
    public bool   BackendFallbackUsed     { get; init; }
    public string BackendFallbackReason   { get; init; } = "";

    // ── API stack ─────────────────────────────────────────────────────────────
    public string CaptureApi              { get; init; } = "Unknown";
    public string PreviewApi              { get; init; } = "Unknown";
    public string EncoderApi              { get; init; } = "Unknown";

    // ── Encoder ───────────────────────────────────────────────────────────────
    public string HardwareEncoderUsed     { get; init; } = "Unknown";
    public string HardwareEncoderEvidence { get; init; } = "Unknown";

    // ── Color tagging (v1.2.66 Phase 2) ──────────────────────────────────────
    public bool   ColorTaggingApplied     { get; init; }
    public string ColorPrimaries          { get; init; } = "Unknown";
    public string ColorTransferFunction   { get; init; } = "Unknown";
    public string ColorMatrix             { get; init; } = "Unknown";
    public string ColorRange              { get; init; } = "Unknown";

    // ── Preview / recording FPS ───────────────────────────────────────────────
    public bool   PreviewIndependentFromRecording { get; init; }
    public double PreviewTargetFps        { get; init; }
    public double PreviewMeasuredFps      { get; init; }
    public double RecordingMeasuredRealFps { get; init; }

    // ── Timestamp source/availability (v1.2.4-alpha) ─────────────────────────
    public string TimestampSource    { get; init; } = "Unknown";
    public string TimestampCsvStatus { get; init; } = "Unknown";

    // ── V2 frame counter scope (v1.2.14-alpha) ────────────────────────────────
    public long   V2FramesSubmittedSinceRecordingStart { get; init; }
    public string V2FrameCounterScope                  { get; init; } = "";
    public long   V2FramesWrittenDuringRecording        { get; init; }

    // ── Resolution selection hardening (v1.2.19-alpha) ───────────────────────
    public string RequestedResolutionPreset { get; init; } = "Unknown";
    public int    RequestedWidth            { get; init; }
    public int    RequestedHeight           { get; init; }
    public int    SelectedWidth             { get; init; }
    public int    SelectedHeight            { get; init; }
    public string ResolutionSelectionStatus { get; init; } = "Unavailable";
    public bool   ResolutionFallbackUsed    { get; init; }
    public string ResolutionFallbackReason  { get; init; } = "";

    // ── FPS selection hardening (v1.2.19-alpha) ───────────────────────────────
    public double RequestedFps       { get; init; }
    public double SelectedFps        { get; init; }
    public double WriterFps          { get; init; }
    public double ContainerFps       { get; init; }
    public double MeasuredCameraFps  { get; init; }
    public string FpsSelectionStatus { get; init; } = "Unavailable";
    public bool   FpsFallbackUsed    { get; init; }
    public string FpsFallbackReason  { get; init; } = "";
    public bool   DriverVfrDetected  { get; init; }

    // ── Measured FPS honest policy (v1.2.20-alpha) ────────────────────────────
    public double MeasuredFpsDiffFromRequested            { get; init; }
    public double MeasuredFpsPercentDiffFromRequested     { get; init; }
    public string RealFpsStabilityStatus                  { get; init; } = "NotEvaluated";
    public bool   ConsistentLowerRealFpsAccepted          { get; init; }
    public bool   NoArtificialFramePadding                { get; init; } = true;
    public bool   NoDuplicateFramePadding                 { get; init; } = true;
    public bool   NoPlaceholderFrames                     { get; init; } = true;

    // ── Backend/recording engine consistency (v1.2.19-alpha) ─────────────────
    public string RequestedBackend { get; init; } = "Unknown";
    public string RecordingEngine  { get; init; } = "Unknown";

    // ── GPU / hardware encoder hardening (v1.2.19-alpha) ─────────────────────
    public string GpuAccelerationAvailable { get; init; } = "Unknown";
    public string EncoderBackend           { get; init; } = "Unknown";
    public bool   EncoderFallbackUsed      { get; init; }
    public string EncoderFallbackReason    { get; init; } = "";

    // ── Focus / autofocus policy confirmation (v1.2.19-alpha) ────────────────
    public string AutofocusControlSupported { get; init; } = "Unknown";
    public bool   AutofocusOffAttempted     { get; init; }
    public bool   AutofocusOffSucceeded     { get; init; }
    public string AutofocusPolicyResult     { get; init; } = "Unknown";
    public string ExposureControlSupported  { get; init; } = "Unknown";
    public bool   ManualExposureUiAvailable { get; init; } = false;
    public bool   ManualFocusUiAvailable    { get; init; } = false;
}
