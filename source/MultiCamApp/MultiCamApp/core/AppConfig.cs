////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Experiment;
using MultiCamApp.Verification;

namespace MultiCamApp.Core;

public sealed class AppConfig
{
    public string AppName { get; set; } = "MultiCamApp";
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultOutputFolder { get; set; } = "Videos";
    public int MaxCameras { get; set; } = 4;
    public bool AutoCreateSessionFolder { get; set; } = true;
    public bool UseSystemRecommendedResolution { get; set; } = true;
    public double PreferFps { get; set; } = 30;
    /// <summary>0 = native / auto after probe.</summary>
    public int PreferredCaptureWidth { get; set; }
    /// <summary>0 = native / auto after probe.</summary>
    public int PreferredCaptureHeight { get; set; }
    public bool ForceFixedResolution { get; set; }
    public bool EnableHardwareAcceleration { get; set; } = true;
    public bool ShowHardwareInfoInUi { get; set; }
    public bool EnableResponsiveUi { get; set; } = true;
    public bool PreservePreviewAspectRatio { get; set; } = true;
    public bool ReleaseCamerasOnStopPreview { get; set; } = true;
    public bool ReleaseCamerasOnAppClose { get; set; } = true;
    public bool PausePreviewOnMinimize { get; set; } = true;
    public bool AllowRecordingWhileMinimizedOnlyIfUserStartedRecording { get; set; } = true;
    public bool HiddenRecordingAllowed { get; set; }
    public bool PrivacyMode { get; set; } = true;
    public bool EnableDiagnostics { get; set; } = true;
    public bool EnableCrashLogs { get; set; } = true;
    public int MaxPreviewFpsUi { get; set; } = 20;
    /// <summary>When true, WinRT cameras use auto focus if supported; when false, focus is fixed/manual.</summary>
    public bool AutoFocusEnabled { get; set; } = false;
    public bool ReapplyFocusBeforeRecording { get; set; } = true;
    public double? ManualFocusValue { get; set; }
    /// <summary>High-stability recording mode: original real frames only, adaptive preview FPS, elevated writer thread priority.</summary>
    public bool HighStabilityRecordingMode { get; set; } = true;
    /// <summary>Preview FPS mode during recording. Options: Smooth, Balanced, MaxStability. Default: Balanced.</summary>
    public string RecordingPreviewFpsMode { get; set; } = "Balanced";
    /// <summary>When true, auto exposure is requested; when false, manual exposure is applied best-effort.</summary>
    public bool AutoExposureEnabled { get; set; } = false;
    public bool ReapplyExposureBeforeRecording { get; set; } = true;
    public double? ManualExposureValue { get; set; }
    public bool DisableLowLightCompensation { get; set; } = true;

    // Per-camera focus settings (index 0–3). Used when applying to a specific camera target.
    public bool[] AutoFocusEnabledPerCamera { get; set; } = new bool[4];
    public double?[] ManualFocusValuesPerCamera { get; set; } = new double?[4];
    public bool[] ReapplyFocusBeforeRecordingPerCamera { get; set; } = new[] { true, true, true, true };

    // Per-camera exposure settings (index 0–3).
    public bool[] AutoExposureEnabledPerCamera { get; set; } = new bool[4];
    public double?[] ManualExposureValuesPerCamera { get; set; } = new double?[4];
    public bool[] DisableLowLightCompensationPerCamera { get; set; } = new[] { true, true, true, true };
    public bool[] ReapplyExposureBeforeRecordingPerCamera { get; set; } = new[] { true, true, true, true };

    /// <summary>Returns a shallow copy of this config with per-camera focus settings applied for the given slot.</summary>
    public AppConfig WithSlotFocusSettings(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return this;
        var copy = (AppConfig)MemberwiseClone();
        copy.AutoFocusEnabled = AutoFocusEnabledPerCamera[slotIndex];
        copy.ManualFocusValue = ManualFocusValuesPerCamera[slotIndex];
        copy.ReapplyFocusBeforeRecording = ReapplyFocusBeforeRecordingPerCamera[slotIndex];
        return copy;
    }

    /// <summary>Returns a shallow copy of this config with per-camera exposure settings applied for the given slot.</summary>
    public AppConfig WithSlotExposureSettings(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return this;
        var copy = (AppConfig)MemberwiseClone();
        copy.AutoExposureEnabled = AutoExposureEnabledPerCamera[slotIndex];
        copy.ManualExposureValue = ManualExposureValuesPerCamera[slotIndex];
        copy.DisableLowLightCompensation = DisableLowLightCompensationPerCamera[slotIndex];
        copy.ReapplyExposureBeforeRecording = ReapplyExposureBeforeRecordingPerCamera[slotIndex];
        return copy;
    }
    /// <summary>opencv (default, reliable on Windows) or winrt</summary>
    public string PreviewEngine { get; set; } = "opencv";
    /// <summary>windows_camera (LowLag MP4, like built-in Camera app) or opencv (record in preview loop).</summary>
    public string RecordingEngine { get; set; } = "opencv";
    public int RecordingHandoffDelayMs { get; set; } = 500;
    /// <summary>default | builtin | external | back — how to pick the first camera on refresh.</summary>
    public string PreferredCameraPolicy { get; set; } = "default";
    public bool IncludeDisabledCameras { get; set; }
    /// <summary>PrepareLowLagRecord (Windows Camera style) vs StartRecordToStorageFile.</summary>
    public bool UseLowLagRecording { get; set; } = true;
    /// <summary>camera_roll (MCAM_yyyyMMdd_HHmmss.mp4) or session_slot (cam1.mp4).</summary>
    public string RecordingFileNameStyle { get; set; } = "camera_roll";
    /// <summary>OS record limit awareness (Windows ~3 hours).</summary>
    public double RecordLimitHours { get; set; } = 3;
    public SecuritySettings Security { get; set; } = new();
    public VerificationSettings Verification { get; set; } = new();
    public ExperimentModeSettings ExperimentMode { get; set; } = new();
    public LocomotorStandardSettings LocomotorStandardMode { get; set; } = new();
    public LocomotorVerificationProfile LocomotorStandardVerification { get; set; } = new();
    public Dictionary<string, VerificationProfileSettings> VerificationProfiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
