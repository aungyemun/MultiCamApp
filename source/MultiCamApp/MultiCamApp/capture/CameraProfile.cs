////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Capture;

public sealed class CameraProfile
{
    public string Name { get; set; } = "default_hd";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 30;
}
