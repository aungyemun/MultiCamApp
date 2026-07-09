////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// v1.2.22-alpha — V3/V3B removed. VideoEngineV2 Stable is the only backend.

using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Recording.Writers;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture.Backend;

/// <summary>
/// Manages the VideoEngine backend. VideoEngineV2 Stable is the only supported backend.
/// </summary>
public sealed class VideoEngineRegistry : IDisposable
{
    private readonly VideoEngineV2.VideoEngineV2? _v2;
    private string _requestedBackendId = BackendIds.VideoEngineV2Stable;
    private string _activeBackendId    = BackendIds.VideoEngineV2Stable;
    private bool   _fallbackOccurred;
    private string _fallbackReason     = "";
    private RecordingSelectionContext? _selectionCtx;
    private bool   _disposed;

    // ── Public state ──────────────────────────────────────────────────────────

    public string ActiveBackendId    => _activeBackendId;
    public string RequestedBackendId => _requestedBackendId;
    public bool   FallbackOccurred   => _fallbackOccurred;
    public string FallbackReason     => _fallbackReason;

    public string BackendStatusDisplay =>
        _fallbackOccurred
            ? $"VideoEngineV2 Stable (fallback from {_requestedBackendId})"
            : "VideoEngineV2 Stable";

    public VideoEngineRegistry(VideoEngineV2.VideoEngineV2? v2Engine = null)
    {
        _v2 = v2Engine;
    }

    // ── Backend selection ─────────────────────────────────────────────────────

    public void SelectBackend(string backendId)
    {
        _requestedBackendId = backendId;
        _activeBackendId    = BackendIds.VideoEngineV2Stable;
        _fallbackOccurred   = false;
        _fallbackReason     = "";
        AppDiagnosticLogger.Runtime("BACKEND_REGISTRY active=VideoEngineV2_Stable");
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    public BackendInitDiagnostics GetActiveDiagnostics() =>
        BackendInitDiagnostics.Success(
            BackendIds.VideoEngineV2Stable,
            "VideoEngineV2 Stable — production pipeline (MediaFoundation, WPF WriteableBitmap preview).");

    // ── Recording selection context ───────────────────────────────────────────

    public void SetRecordingSelectionContext(RecordingSelectionContext ctx) => _selectionCtx = ctx;

    public RecordingSelectionContext? GetRecordingSelectionContext() => _selectionCtx;

    // ── Metadata building ─────────────────────────────────────────────────────

    public BackendMetadata BuildMetadata(
        RecordingFinalizeResult result,
        double measuredRealFps,
        double previewMeasuredFps,
        double previewTargetFps,
        PreviewRendererType previewRenderer = PreviewRendererType.Wpf)
    {
        string hwEncoder  = result.HardwareEncoderUsed
            ? "Yes"
            : result.EncoderDescription is { Length: > 0 } d ? $"No ({d})" : "No";
        string hwEvidence = result.EncoderDescription ?? "Unknown";

        string timestampCsvStatus = result.TimestampCsvRows > 0 || result.FramesWrittenDuringRecording > 0
            ? "Written" : "Skipped";

        return new BackendMetadata
        {
            RecordingBackend               = BackendIds.VideoEngineV2Stable,
            BackendVersion                 = "2.0.2",
            BackendMode                    = "Stable",
            BackendFallbackUsed            = false,
            BackendFallbackReason          = "",
            CaptureApi                     = "Windows.Media.Capture.MediaCapture (WinRT / MediaFoundation)",
            PreviewApi                     = DescribePreviewApi(previewRenderer),
            EncoderApi                     = "IMFSinkWriter (H.264 / MediaFoundation, Vortice.MediaFoundation)",
            HardwareEncoderUsed            = hwEncoder,
            HardwareEncoderEvidence        = hwEvidence,
            ColorTaggingApplied            = true,
            ColorPrimaries                 = "BT.709",
            ColorTransferFunction          = "BT.709",
            ColorMatrix                    = "BT.709",
            ColorRange                     = "Full (0-255 / pc, matches Windows Camera)",
            PreviewIndependentFromRecording = true,
            PreviewTargetFps               = previewTargetFps,
            PreviewMeasuredFps             = previewMeasuredFps,
            RecordingMeasuredRealFps       = measuredRealFps,
            TimestampSource                = "V2_FrameTimestampMonitor",
            TimestampCsvStatus             = timestampCsvStatus,
            V2FramesSubmittedSinceRecordingStart = result.FramesSubmittedSinceRecordingStart,
            V2FrameCounterScope                  = result.FrameCounterScope,
            V2FramesWrittenDuringRecording        = result.FramesWrittenDuringRecording,
            RequestedResolutionPreset = _selectionCtx?.RequestedResolutionPreset ?? "Unknown",
            RequestedWidth            = _selectionCtx?.RequestedWidth            ?? 0,
            RequestedHeight           = _selectionCtx?.RequestedHeight           ?? 0,
            SelectedWidth             = _selectionCtx?.SelectedWidth             ?? 0,
            SelectedHeight            = _selectionCtx?.SelectedHeight            ?? 0,
            ResolutionSelectionStatus = _selectionCtx?.ResolutionSelectionStatus ?? "Unavailable",
            ResolutionFallbackUsed    = _selectionCtx?.ResolutionFallbackUsed    ?? false,
            ResolutionFallbackReason  = _selectionCtx?.ResolutionFallbackReason  ?? "",
            RequestedFps              = _selectionCtx?.RequestedFps              ?? 0,
            SelectedFps               = _selectionCtx?.SelectedFps               ?? 0,
            WriterFps                 = _selectionCtx?.WriterFps                 ?? 0,
            ContainerFps              = 0,
            MeasuredCameraFps         = measuredRealFps,
            FpsSelectionStatus        = _selectionCtx?.FpsSelectionStatus        ?? "Unavailable",
            FpsFallbackUsed           = _selectionCtx?.FpsFallbackUsed           ?? false,
            FpsFallbackReason         = _selectionCtx?.FpsFallbackReason         ?? "",
            DriverVfrDetected         = _selectionCtx?.DriverVfrDetected         ?? false,
            MeasuredFpsDiffFromRequested        = _selectionCtx?.MeasuredFpsEvaluation?.MeasuredFpsDiffFromRequested       ?? 0,
            MeasuredFpsPercentDiffFromRequested = _selectionCtx?.MeasuredFpsEvaluation?.MeasuredFpsPercentDiffFromRequested ?? 0,
            RealFpsStabilityStatus              = _selectionCtx?.MeasuredFpsEvaluation?.RealFpsStabilityStatus.ToString()  ?? Backend.RealFpsStabilityStatus.NotEvaluated.ToString(),
            ConsistentLowerRealFpsAccepted      = _selectionCtx?.MeasuredFpsEvaluation?.ConsistentLowerRealFpsAccepted      ?? false,
            NoArtificialFramePadding            = true,
            NoDuplicateFramePadding             = true,
            NoPlaceholderFrames                 = true,
            RequestedBackend          = _selectionCtx?.RequestedBackend          ?? BackendIds.VideoEngineV2Stable,
            RecordingEngine           = BackendIds.VideoEngineV2Stable,
            GpuAccelerationAvailable  = _selectionCtx?.GpuAccelerationAvailable  ?? "Unknown",
            EncoderBackend            = _selectionCtx?.EncoderBackend            ?? hwEvidence,
            EncoderFallbackUsed       = _selectionCtx?.EncoderFallbackUsed       ?? false,
            EncoderFallbackReason     = _selectionCtx?.EncoderFallbackReason     ?? "",
            AutofocusControlSupported = _selectionCtx?.AutofocusControlSupported ?? "Unknown",
            AutofocusOffAttempted     = _selectionCtx?.AutofocusOffAttempted     ?? false,
            AutofocusOffSucceeded     = _selectionCtx?.AutofocusOffSucceeded     ?? false,
            AutofocusPolicyResult     = _selectionCtx?.AutofocusPolicyResult     ?? "Unknown",
            ExposureControlSupported  = _selectionCtx?.ExposureControlSupported  ?? "Unknown",
            // MainWindow's Advanced Camera Controls expander exposes manual exposure/focus
            // sliders unconditionally (ManualExposureSlider/ManualFocusSlider) — whether the
            // driver actually honors them is reported separately via ExposureControlSupported/
            // AutofocusControlSupported, so these two fields only describe UI availability.
            ManualExposureUiAvailable = true,
            ManualFocusUiAvailable    = true,
        };
    }

    /// <summary>Describes the preview API actually in use for a slot, reflecting whether the
    /// Direct3D11 (Vortice.Windows) GPU renderer engaged or the app fell back to the WPF
    /// WriteableBitmap software path.</summary>
    private static string DescribePreviewApi(PreviewRendererType renderer) => renderer switch
    {
        PreviewRendererType.Direct3D or PreviewRendererType.Direct3D11
            => "Direct3D11 GPU swap chain (Vortice.Windows)",
        _ => "WPF WriteableBitmap (software)",
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
