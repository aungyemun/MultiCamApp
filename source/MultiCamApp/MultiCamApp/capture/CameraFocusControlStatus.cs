namespace MultiCamApp.Capture;

public sealed record CameraFocusControlStatus
{
    public bool AutoFocusRequested { get; init; }
    public bool AutoFocusApplyAttempted { get; init; }
    public bool? AutoFocusApplySucceeded { get; init; }
    public string AutoFocusReadbackValue { get; init; } = "unavailable";
    public bool? ManualFocusSupported { get; init; }
    public double? ManualFocusRequestedValue { get; init; }
    public string ManualFocusReadbackValue { get; init; } = "unavailable";
    public string FocusControlMode { get; init; } = "unavailable";
    public string FocusWarning { get; init; } = "";

    public static CameraFocusControlStatus NotAttempted(bool autoFocusRequested) => new()
    {
        AutoFocusRequested = autoFocusRequested,
        FocusControlMode = autoFocusRequested ? "autofocus_requested" : "manual_or_fixed_requested"
    };
}
