using MultiCamApp.Capture;

namespace MultiCamApp.Utils;

public enum PreviewSlotFailureCategory
{
    UnsupportedPreset,
    DeviceOpen,
    FirstFrameTimeout,
    Timeout,
    Generic
}

public static class PreviewSlotFailureHelper
{
    public const int CameraOpenTimeoutSeconds = 15;
    public const int FirstFrameTimeoutSeconds = 5;

    public static PreviewSlotStateKind ToSlotState(PreviewSlotFailureCategory category) =>
        category == PreviewSlotFailureCategory.UnsupportedPreset
            ? PreviewSlotStateKind.FailedUnsupportedPreset
            : PreviewSlotStateKind.FailedDeviceOpen;

    public static PreviewSlotFailureCategory ClassifyOpenFailure(string? lastError, bool resolutionMatched, bool requestedPreset)
    {
        if (requestedPreset && !resolutionMatched)
            return PreviewSlotFailureCategory.UnsupportedPreset;

        if (!string.IsNullOrWhiteSpace(lastError)
            && lastError.Contains("could not apply", StringComparison.OrdinalIgnoreCase))
            return PreviewSlotFailureCategory.UnsupportedPreset;

        return PreviewSlotFailureCategory.DeviceOpen;
    }

    public static string BuildOverlayMessage(
        Func<string, string> language,
        string slotName,
        int preferredWidth,
        int preferredHeight,
        double requestedFps,
        PreviewSlotFailureCategory category,
        string? actualResolutionText = null)
    {
        var preset = CaptureResolutionPreset.ToLabel(preferredWidth, preferredHeight);
        if (string.IsNullOrEmpty(preset))
            preset = CaptureResolutionPreset.Label720;

        var fpsText = requestedFps > 0 ? $"{requestedFps:F0}" : "30";
        var headline = string.Format(language("previewSlotFailedAtPreset"), slotName, preset, fpsText);
        var advice = GetAdviceKey(preset) is { } key ? language(key) : language("previewSlotTryLowerPresetGeneric");

        if (!string.IsNullOrWhiteSpace(actualResolutionText)
            && category == PreviewSlotFailureCategory.UnsupportedPreset)
        {
            return $"{headline}\n{advice}\n({language("previewSlotOpenedInstead")}: {actualResolutionText})";
        }

        return $"{headline}\n{advice}";
    }

    public static string BuildLostConnectionMessage(Func<string, string> language, string slotName) =>
        string.Format(language("previewSlotLostConnection"), slotName);

    public static CameraOpenFailurePhase ToFailurePhase(PreviewSlotFailureCategory category) => category switch
    {
        PreviewSlotFailureCategory.UnsupportedPreset => CameraOpenFailurePhase.SetResolution,
        PreviewSlotFailureCategory.FirstFrameTimeout => CameraOpenFailurePhase.FirstFrame,
        PreviewSlotFailureCategory.Timeout => CameraOpenFailurePhase.DeviceOpen,
        PreviewSlotFailureCategory.DeviceOpen => CameraOpenFailurePhase.DeviceOpen,
        _ => CameraOpenFailurePhase.Unknown
    };

    private static string? GetAdviceKey(string presetLabel) => presetLabel switch
    {
        CaptureResolutionPreset.Label1080 => "previewSlotTryLowerPreset1080",
        CaptureResolutionPreset.Label720 => "previewSlotTryLowerPreset720",
        CaptureResolutionPreset.Label360 => "previewSlotTryLowerPreset360",
        _ => "previewSlotTryLowerPresetGeneric"
    };
}
