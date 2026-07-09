////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).
// Shared enums, flags, and value types used across all VideoEngineV2 classes.

namespace MultiCamApp.Capture.VideoEngineV2;

// ── Backend ──────────────────────────────────────────────────────────────────

/// <summary>Identifies which camera capture backend is active.</summary>
public enum VideoEngineBackend
{
    /// <summary>Current production backend — OpenCV DirectShow preview + WinRT recording.</summary>
    Legacy,
    /// <summary>Explicit alias for <see cref="Legacy"/>.</summary>
    LegacyOpenCv,
    /// <summary>WinRT MediaCapture + MediaFrameReader path (experimental).</summary>
    MediaFoundation,
}

// ── Preview renderer ─────────────────────────────────────────────────────────

/// <summary>Preview rendering implementation in use for a pipeline slot.</summary>
public enum PreviewRendererType
{
    /// <summary>WPF WriteableBitmap renderer (software; current and V2 fallback path).</summary>
    Wpf,
    /// <summary>Alias for <see cref="Wpf"/> used when emphasising it is the fallback.</summary>
    WpfFallback,
    /// <summary>Direct3D 11 swap chain renderer — GPU-assisted, lower CPU (future phase).</summary>
    Direct3D,
    /// <summary>More specific alias for <see cref="Direct3D"/>.</summary>
    Direct3D11,
    /// <summary>No renderer initialised.</summary>
    None,
}

// ── Encoder ──────────────────────────────────────────────────────────────────

/// <summary>Video encoder backend in use for a recording session.</summary>
public enum EncoderBackendType
{
    NotSelected,
    /// <summary>Legacy OpenCV AVI/MJPEG or MP4V writer.</summary>
    OpenCv,
    /// <summary>Alias: explicit OpenCV MP4V label.</summary>
    LegacyOpenCvMp4v,
    /// <summary>Media Foundation H.264 — hardware-accelerated where available (NVENC, QuickSync).</summary>
    MediaFoundationH264,
    /// <summary>Alias for <see cref="MediaFoundationH264"/> (hardware path).</summary>
    MediaFoundationHardware,
    /// <summary>Media Foundation H.264 — forced software (x264 or MS Software Encoder).</summary>
    MediaFoundationSoftwareH264,
    /// <summary>Alias for <see cref="MediaFoundationSoftwareH264"/>.</summary>
    MediaFoundationSoftware,
}

// ── Pipeline state ────────────────────────────────────────────────────────────

/// <summary>State machine states for <see cref="CameraPipelineV2"/>.</summary>
public enum CameraPipelineState
{
    Idle,
    Initialising,
    Previewing,
    StartingRecording,
    Recording,
    StoppingRecording,
    Error,
    Disposed,
}

// ── Capability probe ──────────────────────────────────────────────────────────

/// <summary>Runtime availability probe result for a system capability.</summary>
public enum V2CapabilityAvailability
{
    Unknown,
    Available,
    Unavailable,
}

// ── Device info (extended) ───────────────────────────────────────────────────

/// <summary>
/// Extended camera device descriptor as requested by the V2 spec.
/// Includes USB path, busy status, and PnP information beyond <see cref="V2CameraDeviceInfo"/>.
/// </summary>
public sealed class CameraDeviceInfoV2
{
    /// <summary>Stable symbolic link device ID (e.g. <c>\\?\USB#VID_...</c>).</summary>
    public string DeviceId { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public int EnumerationIndex { get; init; }
    /// <summary>Full device instance path from the PnP manager, if available.</summary>
    public string? DevicePath { get; init; }
    /// <summary>True if the device reports it is currently in use by another application.</summary>
    public bool IsBusy { get; init; }
    /// <summary>True if the device is connected via USB.</summary>
    public bool IsUsb { get; init; }
    public IReadOnlyList<CameraFormatInfoV2> SupportedFormats { get; init; } = Array.Empty<CameraFormatInfoV2>();
    public V2DeviceDiscoverySource DiscoverySource { get; init; }
    public string? FallbackReason { get; init; }
    public override string ToString() => $"{FriendlyName} [{EnumerationIndex}]{(IsBusy ? " [BUSY]" : "")}";
}

// ── Format info (extended) ────────────────────────────────────────────────────

/// <summary>
/// Extended capture format descriptor with pixel subtype and bandwidth information.
/// Complement to <see cref="V2CaptureFormat"/> (which is used internally in the pipeline).
/// </summary>
public sealed class CameraFormatInfoV2
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double NominalFps { get; init; }
    public V2PixelFormat PixelFormat { get; init; }
    /// <summary>Raw MF subtype name (e.g. "MJPEG", "YUY2", "NV12", "RGB24").</summary>
    public string? SubtypeName { get; init; }
    /// <summary>True if the format uses intra-frame compression (MJPEG = true, YUY2 = false).</summary>
    public bool IsCompressed { get; init; }
    /// <summary>Estimated USB 2.0 bandwidth usage in MB/s (uncompressed).</summary>
    public double EstimatedBandwidthMBs => IsCompressed ? 0 : (Width * Height * 2 * NominalFps) / 1_000_000.0;
    public override string ToString() => $"{Width}×{Height}@{NominalFps:F2} {SubtypeName ?? PixelFormat.ToString()}";
}

// ── Control status ────────────────────────────────────────────────────────────

/// <summary>Status of a single camera control after an apply + readback attempt.</summary>
public sealed class CameraControlStatusV2
{
    public string ControlName { get; init; } = "";
    public bool Supported { get; init; }
    /// <summary>Value that was requested (null if none).</summary>
    public string? RequestedValue { get; init; }
    public bool Applied { get; init; }
    /// <summary>Value read back from the device after apply.</summary>
    public string? ReadbackValue { get; init; }
    public V2ControlConfirmation Confirmed { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>Confirmation result after a control apply + readback.</summary>
public enum V2ControlConfirmation
{
    NotAttempted,
    Confirmed,
    Mismatch,
    NotSupported,
    Unknown,
    Failed,
}

// ── Feature flags ─────────────────────────────────────────────────────────────

/// <summary>
/// Internal feature flags for VideoEngineV2.
/// From v1.1.7, V2 is the default production pipeline (all flags enabled by default).
/// </summary>
public static class VideoEngineV2Flags
{
    /// <summary>
    /// Master switch. True by default from v1.1.7 — VideoEngineV2 is the production pipeline.
    /// Set to false to revert to legacy OpenCV pipeline only.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Allows preview to use the V2 pipeline for all camera slots.
    /// True by default from v1.1.7.
    /// </summary>
    public static bool AllowCam1PreviewTest { get; set; } = true;

    /// <summary>
    /// When true, V2 is used as the primary pipeline replacing the legacy OpenCV path
    /// for both preview and recording on all active camera slots.
    /// False forces legacy pipeline even when V2 is enabled.
    /// </summary>
    public static bool UseAsDefaultPipeline { get; set; } = true;

    /// <summary>
    /// Number of camera slots supported by the V2 engine (max 4).
    /// </summary>
    public const int MaxSlots = 4;
}

// ── Preview overlay ───────────────────────────────────────────────────────────

/// <summary>
/// Preview-only overlay data for the V2 cam1 preview surface.
/// This data MUST NEVER be burned into recorded video.
/// </summary>
public sealed class V2PreviewOverlayData
{
    public string CameraLabel       { get; init; } = "cam1";
    public string DeviceName        { get; init; } = "";
    public string Resolution        { get; init; } = "";
    public double TargetFps         { get; init; }
    public double LiveFps           { get; init; }
    public VideoEngineBackend Backend   { get; init; }
    public PreviewRendererType Renderer { get; init; }
    /// <summary>True while the legacy recording pipeline is recording on this slot.</summary>
    public bool IsLegacyRecording   { get; init; }
    public long DroppedFrames       { get; init; }
    public string EncoderBackend    { get; init; } = "";
    public bool IsRecording         { get; init; }
    public TimeSpan RecordingElapsed { get; init; }
    public long RecordedFrames      { get; init; }
    public string? FallbackWarning  { get; init; }
    public string? ControlWarning   { get; init; }
}
