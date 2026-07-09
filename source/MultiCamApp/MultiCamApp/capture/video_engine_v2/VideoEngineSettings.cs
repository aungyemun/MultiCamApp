////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production pipeline from v1.1.7.
// VideoEngineV2 is now the default pipeline for preview and recording on all camera slots.

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>Bitrate presets for H.264 encoding. Setting <see cref="VideoEngineSettings.BitrateProfile"/> updates <see cref="VideoEngineSettings.TargetBitrateKbps"/>.</summary>
public enum V2BitrateProfile
{
    Standard         = 7_500,
    High             = 12_000,
    WindowsCameraLike = 18_000,
}

/// <summary>
/// Configurable settings for VideoEngineV2. References <see cref="VideoEngineV2Flags"/>
/// for the master enable switches so both access points stay in sync.
/// </summary>
public static class VideoEngineSettings
{
    // ── Feature flags (delegate to VideoEngineV2Flags) ──────────────────────

    /// <summary>Master switch. False = legacy OpenCV pipeline only.</summary>
    public static bool Enabled
    {
        get => VideoEngineV2Flags.Enabled;
        set => VideoEngineV2Flags.Enabled = value;
    }

    /// <summary>Allow cam1 preview to use the V2 pipeline (developer/test mode).</summary>
    public static bool AllowCam1PreviewTest
    {
        get => VideoEngineV2Flags.AllowCam1PreviewTest;
        set => VideoEngineV2Flags.AllowCam1PreviewTest = value;
    }

    /// <summary>Allow all camera slots to use the V2 H.264 recording path. True by default from v1.1.7.</summary>
    public static bool AllowCam1RecordingTest { get; set; } = true;

    // ── Format preferences ───────────────────────────────────────────────────

    public static int DefaultPreferredWidth  { get; set; } = 1280;
    public static int DefaultPreferredHeight { get; set; } = 720;
    public static double DefaultPreferredFps { get; set; } = 30.0;

    /// <summary>
    /// Preferred pixel format. MJPEG is preferred because it reduces USB bandwidth
    /// and is widely supported by UVC webcams (including j5/OBSBOT/Logitech on this machine).
    /// </summary>
    public static V2PixelFormat DefaultPreferredPixelFormat { get; set; } = V2PixelFormat.Mjpeg;

    // ── Preview surface ──────────────────────────────────────────────────────

    public static int PreviewWidth  { get; set; } = 1280;
    public static int PreviewHeight { get; set; } = 720;

    // ── Encoder ──────────────────────────────────────────────────────────────

    public static EncoderBackendType PreferredEncoderBackend { get; set; } = EncoderBackendType.MediaFoundationH264;
    /// <summary>Request hardware H.264 (NVENC / QuickSync). Falls back to software.</summary>
    public static bool PreferHardwareEncoder { get; set; } = true;
    public static int TargetBitrateKbps { get; set; } = (int)V2BitrateProfile.WindowsCameraLike;

    private static V2BitrateProfile _bitrateProfile = V2BitrateProfile.WindowsCameraLike;

    /// <summary>Preset bitrate profile. Setting this updates <see cref="TargetBitrateKbps"/>.</summary>
    public static V2BitrateProfile BitrateProfile
    {
        get => _bitrateProfile;
        set { _bitrateProfile = value; TargetBitrateKbps = (int)value; }
    }

    // ── Recording file policy ────────────────────────────────────────────────

    /// <summary>Write per-frame timestamp CSV alongside every V2 recording.</summary>
    public static bool WriteTimestampCsv { get; set; } = true;

    /// <summary>
    /// Write to a temp file during recording (<c>cam1.tmp.mp4</c>) and rename
    /// to <c>cam1.mp4</c> only after successful finalisation.
    /// </summary>
    public static bool UseTempFileDuringRecording { get; set; } = true;

    // ── Device selection ─────────────────────────────────────────────────────

    /// <summary>Zero-based device index to use as cam1 (0 = first available).</summary>
    public static int DefaultCam1DeviceIndex { get; set; } = 0;

    // ── Camera controls ──────────────────────────────────────────────────────

    /// <summary>Disable autofocus on open — recommended to prevent hidden refocus during recording.</summary>
    public static bool DisableAutoFocus { get; set; } = true;

    /// <summary>Disable auto-exposure by default so shutter timing remains stable during research recordings.</summary>
    public static bool DisableAutoExposure { get; set; } = true;

    /// <summary>Disable low-light compensation to prevent hidden brightness adjustment.</summary>
    public static bool DisableLowLightCompensation { get; set; } = true;

    /// <summary>Disable optical image stabilisation (OpticalImageStabilizationControl).</summary>
    public static bool DisableOpticalStabilization { get; set; } = true;

    /// <summary>Warn in overlay if Windows Studio Effects may be active.</summary>
    public static bool WarnOnWindowsStudioEffects { get; set; } = true;

    /// <summary>
    /// Apply Auto flicker reduction at camera open to eliminate 50/60 Hz banding from artificial lighting.
    /// The driver self-detects the power line frequency; falls back to explicit 60 Hz then 50 Hz
    /// if Auto is not in the camera's supported-values list.
    /// </summary>
    public static bool EnableFlickerReduction { get; set; } = true;

    // ── Backend selection (v1.2.0-alpha) ────────────────────────────────────────

    /// <summary>
    /// Requested backend identifier. VideoEngineV2_Stable is the only backend (v1.2.22-alpha+).
    /// </summary>
    public static string RequestedBackendId { get; set; } = "VideoEngineV2_Stable";
}
