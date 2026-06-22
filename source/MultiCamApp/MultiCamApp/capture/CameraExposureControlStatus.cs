namespace MultiCamApp.Capture;

public sealed record CameraExposureControlStatus
{
    public bool AutoExposureRequested { get; init; }
    public bool AutoExposureApplyAttempted { get; init; }
    public bool? AutoExposureApplySucceeded { get; init; }
    public string AutoExposureReadbackValue { get; init; } = "unavailable";
    public bool? ManualExposureSupported { get; init; }
    public double? ManualExposureRequestedValue { get; init; }
    public string ManualExposureReadbackValue { get; init; } = "unavailable";
    public bool LowLightCompensationOffRequested { get; init; }
    public bool? LowLightCompensationOffConfirmed { get; init; }
    public string ExposureControlMode { get; init; } = "unavailable";
    public string ExposureWarning { get; init; } = "";

    public static CameraExposureControlStatus NotAttempted(bool autoExposureRequested, bool lowLightCompensationOffRequested = false) => new()
    {
        AutoExposureRequested = autoExposureRequested,
        LowLightCompensationOffRequested = lowLightCompensationOffRequested,
        ExposureControlMode = autoExposureRequested ? "auto_exposure_requested" : "manual_or_fixed_requested"
    };
}
