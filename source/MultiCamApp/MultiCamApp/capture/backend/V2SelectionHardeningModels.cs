////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// v1.2.19-alpha — pure-logic models for resolution/FPS/backend/GPU/autofocus selection hardening.
// These models are tested in isolation and wired into BackendMetadata via VideoEngineRegistry.
// No WPF, WinRT, or camera device dependencies.

namespace MultiCamApp.Capture.Backend;

// ── Resolution preset lookup ──────────────────────────────────────────────────

/// <summary>A named resolution preset: label, pixel dimensions.</summary>
public sealed record ResolutionPresetDimensions(string Label, int Width, int Height);

/// <summary>
/// Known UI resolution presets for MultiCamApp. Maps label strings to pixel dimensions.
/// </summary>
public static class ResolutionPresets
{
    public const string Label1080p = "1080p";
    public const string Label720p  = "720p";
    public const string Label360p  = "360p";

    private static readonly ResolutionPresetDimensions[] _all =
    [
        new(Label1080p, 1920, 1080),
        new(Label720p,  1280,  720),
        new(Label360p,   640,  360),
    ];

    /// <summary>Returns the preset for <paramref name="label"/>, or <c>null</c> if unknown.</summary>
    public static ResolutionPresetDimensions? ForLabel(string label) =>
        Array.Find(_all, p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if <paramref name="label"/> is a known preset.</summary>
    public static bool IsKnownPreset(string label) => ForLabel(label) is not null;

    /// <summary>Returns a preset whose pixel dimensions match, or <c>null</c> if no match.</summary>
    public static ResolutionPresetDimensions? ForDimensions(int width, int height) =>
        Array.Find(_all, p => p.Width == width && p.Height == height);
}

// ── Resolution selection result ───────────────────────────────────────────────

/// <summary>Outcome of matching the requested resolution preset to the camera-selected mode.</summary>
public sealed record ResolutionSelectionResult
{
    public string RequestedResolutionPreset { get; init; } = "Unknown";
    public int    RequestedWidth            { get; init; }
    public int    RequestedHeight           { get; init; }
    public int    SelectedWidth             { get; init; }
    public int    SelectedHeight            { get; init; }
    /// <summary>"Exact" | "Fallback" | "Unavailable"</summary>
    public string ResolutionSelectionStatus { get; init; } = "Unavailable";
    public bool   ResolutionFallbackUsed    { get; init; }
    public string ResolutionFallbackReason  { get; init; } = "";
}

/// <summary>Pure-logic policy: derives <see cref="ResolutionSelectionResult"/> from requested vs selected dims.</summary>
public static class ResolutionSelectionPolicy
{
    /// <param name="requestedPreset">Label like "1080p".</param>
    /// <param name="requestedWidth">Width from UI/CameraMode.Width for the requested preset.</param>
    /// <param name="requestedHeight">Height from UI/CameraMode.Width for the requested preset.</param>
    /// <param name="selectedWidth">Actual pixel width negotiated with the camera driver.</param>
    /// <param name="selectedHeight">Actual pixel height negotiated with the camera driver.</param>
    /// <param name="selectionReason">Optional driver-provided mode selection reason.</param>
    public static ResolutionSelectionResult Evaluate(
        string requestedPreset,
        int    requestedWidth,
        int    requestedHeight,
        int    selectedWidth,
        int    selectedHeight,
        string selectionReason = "")
    {
        if (selectedWidth <= 0 || selectedHeight <= 0)
            return new ResolutionSelectionResult
            {
                RequestedResolutionPreset = requestedPreset,
                RequestedWidth            = requestedWidth,
                RequestedHeight           = requestedHeight,
                SelectedWidth             = selectedWidth,
                SelectedHeight            = selectedHeight,
                ResolutionSelectionStatus = "Unavailable",
                ResolutionFallbackUsed    = false,
                ResolutionFallbackReason  = "NoSelectedDimensions",
            };

        bool exact = requestedWidth == selectedWidth && requestedHeight == selectedHeight;
        return new ResolutionSelectionResult
        {
            RequestedResolutionPreset = requestedPreset,
            RequestedWidth            = requestedWidth,
            RequestedHeight           = requestedHeight,
            SelectedWidth             = selectedWidth,
            SelectedHeight            = selectedHeight,
            ResolutionSelectionStatus = exact ? "Exact" : "Fallback",
            ResolutionFallbackUsed    = !exact,
            ResolutionFallbackReason  = exact ? ""
                : (selectionReason.Length > 0 ? selectionReason
                    : $"CameraDriverSelectedDifferentResolution:{selectedWidth}x{selectedHeight}"),
        };
    }
}

// ── FPS selection result ──────────────────────────────────────────────────────

/// <summary>Outcome of matching the requested FPS to the camera-selected and writer FPS.</summary>
public sealed record FpsSelectionResult
{
    public double RequestedFps        { get; init; }
    public double SelectedFps         { get; init; }
    public double WriterFps           { get; init; }
    public double ContainerFps        { get; init; }  // 0 if not yet measured (post-recording audit)
    public double MeasuredCameraFps   { get; init; }  // from measuredRealFps (runtime estimate)
    /// <summary>"Exact" | "Fallback" | "Unavailable"</summary>
    public string FpsSelectionStatus  { get; init; } = "Unavailable";
    public bool   FpsFallbackUsed     { get; init; }
    public string FpsFallbackReason   { get; init; } = "";
    public bool   DriverVfrDetected   { get; init; }
}

/// <summary>Pure-logic policy: derives <see cref="FpsSelectionResult"/> from requested vs selected FPS.</summary>
public static class FpsSelectionPolicy
{
    private const double ExactTolerance = 0.02; // ≤0.02 fps difference is "Exact"

    public static FpsSelectionResult Evaluate(
        double requestedFps,
        double selectedFps,
        double writerFps          = 0,
        double measuredCameraFps  = 0,
        string selectionReason    = "",
        bool   driverVfrDetected  = false)
    {
        if (requestedFps <= 0 && selectedFps <= 0)
            return new FpsSelectionResult
            {
                FpsSelectionStatus = "Unavailable",
                FpsFallbackReason  = "NoFpsData",
            };

        bool exact = Math.Abs(requestedFps - selectedFps) <= ExactTolerance;
        return new FpsSelectionResult
        {
            RequestedFps       = requestedFps,
            SelectedFps        = selectedFps,
            WriterFps          = writerFps,
            ContainerFps       = 0,           // populated post-recording by audit
            MeasuredCameraFps  = measuredCameraFps,
            FpsSelectionStatus = exact ? "Exact" : "Fallback",
            FpsFallbackUsed    = !exact,
            FpsFallbackReason  = exact ? ""
                : (selectionReason.Length > 0 ? selectionReason
                    : $"CameraDriverSelectedDifferentFps:{selectedFps:F3}"),
            DriverVfrDetected  = driverVfrDetected,
        };
    }
}

// ── Autofocus / exposure policy report ───────────────────────────────────────

/// <summary>
/// Per-session report of autofocus and exposure control behaviour.
/// ManualExposureUiAvailable and ManualFocusUiAvailable are always false:
/// MultiCamApp intentionally omits manual focus/exposure sliders.
/// </summary>
public sealed record AutofocusPolicyReport
{
    /// <summary>"Supported" | "NotSupported" | "Unknown"</summary>
    public string AutofocusControlSupported { get; init; } = "Unknown";
    public bool   AutofocusOffAttempted     { get; init; }
    public bool   AutofocusOffSucceeded     { get; init; }
    /// <summary>"OffConfirmed" | "OffFailed" | "NotAttempted" | "NotSupported" | "Unknown"</summary>
    public string AutofocusPolicyResult     { get; init; } = "Unknown";
    /// <summary>"Supported" | "NotSupported" | "Unknown"</summary>
    public string ExposureControlSupported  { get; init; } = "Unknown";
    /// <summary>Always false — manual exposure sliders are not implemented.</summary>
    public bool   ManualExposureUiAvailable { get; init; } = false;
    /// <summary>Always false — manual focus sliders are not implemented.</summary>
    public bool   ManualFocusUiAvailable    { get; init; } = false;

    public static AutofocusPolicyReport NotSupported(string exposureControlSupported = "Unknown") =>
        new()
        {
            AutofocusControlSupported = "NotSupported",
            AutofocusOffAttempted     = false,
            AutofocusOffSucceeded     = false,
            AutofocusPolicyResult     = "NotSupported",
            ExposureControlSupported  = exposureControlSupported,
            ManualExposureUiAvailable = false,
            ManualFocusUiAvailable    = false,
        };

    public static AutofocusPolicyReport FromAttempt(bool attempted, bool succeeded,
        string exposureControlSupported = "Unknown") =>
        new()
        {
            AutofocusControlSupported = "Supported",
            AutofocusOffAttempted     = attempted,
            AutofocusOffSucceeded     = succeeded,
            AutofocusPolicyResult     = !attempted ? "NotAttempted"
                : succeeded ? "OffConfirmed" : "OffFailed",
            ExposureControlSupported  = exposureControlSupported,
            ManualExposureUiAvailable = false,
            ManualFocusUiAvailable    = false,
        };

    public static AutofocusPolicyReport Unknown() => new()
    {
        AutofocusPolicyResult     = "Unknown",
        ManualExposureUiAvailable = false,
        ManualFocusUiAvailable    = false,
    };
}

// ── Recording selection context ───────────────────────────────────────────────

/// <summary>
/// Carries per-recording selection context into <see cref="VideoEngineRegistry.BuildMetadata"/>.
/// Populated before recording starts from UI selections, CameraMode, and device capabilities.
/// </summary>
public sealed record RecordingSelectionContext
{
    // Resolution
    public string RequestedResolutionPreset { get; init; } = "Unknown";
    public int    RequestedWidth            { get; init; }
    public int    RequestedHeight           { get; init; }
    public int    SelectedWidth             { get; init; }
    public int    SelectedHeight            { get; init; }
    public string ResolutionSelectionStatus { get; init; } = "Unavailable";
    public bool   ResolutionFallbackUsed    { get; init; }
    public string ResolutionFallbackReason  { get; init; } = "";

    // FPS
    public double RequestedFps       { get; init; }
    public double SelectedFps        { get; init; }
    public double WriterFps          { get; init; }
    public string FpsSelectionStatus { get; init; } = "Unavailable";
    public bool   FpsFallbackUsed    { get; init; }
    public string FpsFallbackReason  { get; init; } = "";
    public bool   DriverVfrDetected  { get; init; }

    // Requested backend
    public string RequestedBackend { get; init; } = "Unknown";

    // GPU/encoder
    public string GpuAccelerationAvailable { get; init; } = "Unknown";
    public string EncoderBackend           { get; init; } = "Unknown";
    public bool   EncoderFallbackUsed      { get; init; }
    public string EncoderFallbackReason    { get; init; } = "";

    // Measured FPS honest policy (v1.2.20-alpha)
    public MeasuredFpsEvaluationResult? MeasuredFpsEvaluation { get; init; }

    // Autofocus / exposure
    public string AutofocusControlSupported { get; init; } = "Unknown";
    public bool   AutofocusOffAttempted     { get; init; }
    public bool   AutofocusOffSucceeded     { get; init; }
    public string AutofocusPolicyResult     { get; init; } = "Unknown";
    public string ExposureControlSupported  { get; init; } = "Unknown";

    /// <summary>
    /// Builds a context from pre-evaluated result models.
    /// </summary>
    public static RecordingSelectionContext From(
        ResolutionSelectionResult resolution,
        FpsSelectionResult        fps,
        AutofocusPolicyReport     autofocus,
        string requestedBackend         = "Unknown",
        string gpuAccelerationAvailable = "Unknown",
        string encoderBackend           = "Unknown",
        bool   encoderFallbackUsed      = false,
        string encoderFallbackReason    = "")
    {
        return new RecordingSelectionContext
        {
            RequestedResolutionPreset = resolution.RequestedResolutionPreset,
            RequestedWidth            = resolution.RequestedWidth,
            RequestedHeight           = resolution.RequestedHeight,
            SelectedWidth             = resolution.SelectedWidth,
            SelectedHeight            = resolution.SelectedHeight,
            ResolutionSelectionStatus = resolution.ResolutionSelectionStatus,
            ResolutionFallbackUsed    = resolution.ResolutionFallbackUsed,
            ResolutionFallbackReason  = resolution.ResolutionFallbackReason,
            RequestedFps              = fps.RequestedFps,
            SelectedFps               = fps.SelectedFps,
            WriterFps                 = fps.WriterFps,
            FpsSelectionStatus        = fps.FpsSelectionStatus,
            FpsFallbackUsed           = fps.FpsFallbackUsed,
            FpsFallbackReason         = fps.FpsFallbackReason,
            DriverVfrDetected         = fps.DriverVfrDetected,
            RequestedBackend          = requestedBackend,
            GpuAccelerationAvailable  = gpuAccelerationAvailable,
            EncoderBackend            = encoderBackend,
            EncoderFallbackUsed       = encoderFallbackUsed,
            EncoderFallbackReason     = encoderFallbackReason,
            AutofocusControlSupported = autofocus.AutofocusControlSupported,
            AutofocusOffAttempted     = autofocus.AutofocusOffAttempted,
            AutofocusOffSucceeded     = autofocus.AutofocusOffSucceeded,
            AutofocusPolicyResult     = autofocus.AutofocusPolicyResult,
            ExposureControlSupported  = autofocus.ExposureControlSupported,
            MeasuredFpsEvaluation     = null, // set post-recording via WithMeasuredFps()
        };
    }

    /// <summary>
    /// Returns a copy of this context with <see cref="MeasuredFpsEvaluationResult"/> populated.
    /// Call after recording stops when the timestamp CSV measured FPS is available.
    /// </summary>
    public RecordingSelectionContext WithMeasuredFps(MeasuredFpsEvaluationResult eval) =>
        this with { MeasuredFpsEvaluation = eval };
}

