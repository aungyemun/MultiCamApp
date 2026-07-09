////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// v1.2.0-alpha — backend abstraction layer.
// Defines the common contract for VideoEngine backends. VideoEngineV2 Stable is the
// only backend as of v1.2.22-alpha (the experimental V3/V3B backends were removed).
// New backends must implement all members. Recording/preview methods may throw
// NotSupportedException for operations not yet implemented in future experimental
// backends; VideoEngineRegistry handles the fallback to V2 in those cases.

using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Recording.Writers;
using System.Windows.Threading;

namespace MultiCamApp.Capture.Backend;

/// <summary>
/// Common contract for VideoEngine backends.
/// VideoEngineV2 Stable is the only production implementation (v1.2.22-alpha+).
/// </summary>
public interface IVideoEngineBackend : IDisposable
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Unique backend identifier string (e.g. "VideoEngineV2_Stable").</summary>
    string BackendId { get; }

    /// <summary>Backend version string for metadata reporting.</summary>
    string BackendVersion { get; }

    /// <summary>Stable or Experimental.</summary>
    BackendMode Mode { get; }

    /// <summary>True when the backend has been activated and is ready for use.</summary>
    bool IsActive { get; }

    /// <summary>True when this backend supports full recording (not preview-only).</summary>
    bool IsRecordingCapable { get; }

    // ── Device enumeration ────────────────────────────────────────────────────

    Task EnumerateDevicesAsync(CancellationToken ct = default);
    IReadOnlyList<V2CameraDeviceInfo> DiscoveredDevices { get; }
    Task SelectSlotDeviceAsync(int slot, string? preferredDeviceId, int fallbackIndex = 0, CancellationToken ct = default);
    V2CameraDeviceInfo? GetSlotDevice(int slot);
    V2FormatSelectionResult? GetSlotFormatResult(int slot);

    // ── Preview ───────────────────────────────────────────────────────────────

    Task PrepareSlotPreviewAsync(int slot, Dispatcher uiDispatcher,
        int previewWidth = -1, int previewHeight = -1, CancellationToken ct = default);
    Task StartSlotPreviewAsync(int slot, CancellationToken ct = default);
    Task StopSlotPreviewAsync(int slot, CancellationToken ct = default);
    Task StopAllSlotsPreviewAsync(CancellationToken ct = default);

    // ── Recording ─────────────────────────────────────────────────────────────

    Task StartSlotRecordingAsync(int slot, RecordingFileSet fileSet, CancellationToken ct = default);
    Task<RecordingFinalizeResult> StopSlotRecordingAsync(int slot, RecordingFileSet fileSet, CancellationToken ct = default);
    Task<RecordingFinalizeResult[]> StopAllSlotsRecordingAsync(RecordingFileSet?[] fileSets, CancellationToken ct = default);

    // ── State queries ─────────────────────────────────────────────────────────

    CameraPipelineState GetSlotPipelineState(int slot);
    System.Windows.Media.Imaging.WriteableBitmap? GetSlotPreviewBitmap(int slot);
    V2PreviewOverlayData GetSlotOverlayData(int slot, bool legacyIsRecording = false);
    V2CameraCapabilitySnapshot? GetSlotCapabilities(int slot);
    VideoEngineDiagnosticsSnapshot GetSlotDiagnosticsSnapshot(int slot);

    // ── Backend diagnostics ───────────────────────────────────────────────────

    BackendInitDiagnostics GetInitDiagnostics();
}
