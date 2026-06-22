////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using MultiCamApp.Capture;
using MultiCamApp.Core;

namespace MultiCamApp.Recording;

public static class MediaProfileBuilder
{
    public static (MediaEncodingProfile Profile, CameraMode Mode) Build(
        MediaCapture capture, AppConfig config, CameraModeSelector selector)
    {
        var mode = selector.SelectBest(capture, config);
        var profile = selector.BuildProfile(capture, mode);
        return (profile, mode);
    }
}
