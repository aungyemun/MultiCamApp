////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Recording;

public sealed class RecordingSettings
{
    public int TargetWidth { get; set; } = 1920;
    public int TargetHeight { get; set; } = 1080;
    public double TargetFps { get; set; } = 30;
    public string Codec { get; set; } = "H264";
    public string Container { get; set; } = "mp4";
    public bool UseBestSupportedResolution { get; set; } = true;
}
