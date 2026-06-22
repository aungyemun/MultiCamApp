namespace MultiCamApp.Capture;

/// <summary>How Windows classifies a video capture device (aligned with Camera app behavior).</summary>
public enum CameraKind
{
    Unknown = 0,
    BuiltInFront,
    BuiltInBack,
    BuiltInOther,
    ExternalUsb,
    Virtual
}
