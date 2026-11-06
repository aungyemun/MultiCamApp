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
public enum V2RateControl
{
    ConstantBitrate,
    QualityVbr,
}

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

    /// <summary>
    /// STABLE_CORE_V2 exception #10: <see cref="TargetBitrateKbps"/> is defined for 1920x1080 @ 30 fps.
    /// It used to be applied unchanged to every resolution, so 640x480 was encoded at ~18 Mbit/s
    /// (measured 140 MB per camera-minute) — ~7x the bits per pixel of 1080p, with no visible gain.
    /// Scale by pixel rate so every preset gets the same bits-per-pixel quality as 1080p30,
    /// floored at 2 Mbit/s and capped at 1.5x the base (e.g. 1080p60).
    /// </summary>
    public static int ResolveTargetBitrateKbps(int width, int height, double fps)
    {
        if (width <= 0 || height <= 0 || fps <= 0) return TargetBitrateKbps;
        var scaled = TargetBitrateKbps * (width * (double)height * fps) / (1920.0 * 1080.0 * 30.0);
        return (int)Math.Round(Math.Clamp(scaled, Math.Min(2000, TargetBitrateKbps), TargetBitrateKbps * 1.5));
    }

    /// <summary>Expected MB per camera-minute for a bitrate profile at a capture mode (same scaling as above).</summary>
    public static double MegabytesPerMinute(V2BitrateProfile profile, int width, int height, double fps)
    {
        var baseKbps = (int)profile;
        var scaled = baseKbps * (width * (double)height * fps) / (1920.0 * 1080.0 * 30.0);
        var kbps = Math.Clamp(scaled, Math.Min(2000, baseKbps), baseKbps * 1.5);
        return kbps * 1000 / 8 * 60 / 1_000_000;
    }

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

    /// <summary>
    /// STABLE_CORE_V2 exception #9: mains frequency applied to the camera's UVC Power Line
    /// Frequency (anti-flicker) control at open. 0 = keep the camera's own setting,
    /// 50 / 60 = Hz (eastern / western Japan, Europe / Americas), -1 = camera auto (default).
    /// Default changed 0 → -1 (2.1.0) on measured evidence: under 120 Hz room-light flicker the C922's
    /// factory 50 Hz setting gave 1.30 % drifting bands; Auto gave 0.076 % (60 Hz: 0.095 %).
    /// Loaded from appsettings.json "powerLineFrequencyHz"; changeable in Advanced Camera Controls.
    /// </summary>
    public static int PowerlineFrequencyHz { get; set; } = -1;

    /// <summary>
    /// Lock UVC-only white balance at recording start. Default OFF: verified on real UVC webcams
    /// that the value read back while auto WB is active is the stored manual default (4600 K),
    /// not the live auto value, so "locking" produced a strong colour cast (cyan under lab light).
    /// </summary>
    public static bool LockWhiteBalanceDuringRecording { get; set; } = false;

    /// <summary>
    /// Lock UVC-only exposure at recording start. Default OFF: the UVC readback during auto
    /// exposure is not guaranteed to be the live AE value, and UVC exposure steps are powers of two
    /// (1/64 s, 1/32 s …) that cannot match 50/60 Hz flicker periods (1/100, 1/120 s), so a forced
    /// lock can defeat anti-flicker. Use the manual exposure control (Advanced Camera Controls)
    /// when a fixed shutter is required. Focus is still locked (its readback is the live position).
    /// </summary>
    /// <summary>
    /// STABLE_CORE_V2 exception #15: write fragmented MP4 so a recording interrupted by a crash,
    /// forced close or power loss stays playable up to the last fragment. Standard players, ffmpeg,
    /// OpenCV and browsers read fragmented MP4.
    /// </summary>
    public static bool CrashSafeFragmentedMp4 { get; set; } = true;

    /// <summary>
    /// H.264 rate control. ConstantBitrate (default, validated) spends the target bitrate whatever the scene;
    /// QualityVbr asks the encoder for constant quality (<see cref="EncoderQuality"/>, 0–100), so static scenes
    /// use fewer bits. Whether a hardware encoder honours QualityVbr is checked on hardware before relying on it.
    /// </summary>
    public static V2RateControl RateControl { get; set; } = V2RateControl.ConstantBitrate;

    /// <summary>Encoder quality for <see cref="V2RateControl.QualityVbr"/> (0–100, higher = better).</summary>
    public static uint EncoderQuality { get; set; } = 70;

    /// <summary>Quality used by the "Efficient (constant quality)" preset — chosen from measurements (see CHANGELOG).</summary>
    public static uint EfficientEncoderQuality { get; set; } = 80;

    /// <summary>Open and discard one encoder in the background after preview starts (removes the ~2 s
    /// first-Start-Recording delay caused by one-time hardware encoder initialisation).</summary>
    public static bool EncoderWarmUp { get; set; } = true;

    /// <summary>Exception #15: H.264 keyframe interval in seconds (0 = encoder default).</summary>
    public static double KeyframeIntervalSeconds { get; set; } = 1.0;

    public static bool LockUvcExposureDuringRecording { get; set; } = false;

    // ── Backend selection (v1.2.0-alpha) ────────────────────────────────────────

    /// <summary>
    /// Requested backend identifier. VideoEngineV2_Stable is the only backend (v1.2.22-alpha+).
    /// </summary>
    public static string RequestedBackendId { get; set; } = "VideoEngineV2_Stable";
}
