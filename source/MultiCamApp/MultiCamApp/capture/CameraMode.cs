////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Capture;

public sealed record CameraMode
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public double RequestedFps { get; init; }
    public double SelectedDeviceFps { get; init; }
    public string SelectionReason { get; init; } = "";
    public bool IsNativeRecommended { get; init; }

    public string Label => $"{Width}×{Height} @ {SelectedDeviceFps:F3} ({SelectionReason})";
}
