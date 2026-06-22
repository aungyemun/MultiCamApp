////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Recording;

/// <summary>
/// File names aligned with Windows Camera (WIN_yyyyMMdd_HHmmss_Pro.mp4) for single-camera sessions.
/// Multi-cam adds a slot suffix under the session folder.
/// </summary>
public static class RecordingFileNaming
{
    public const string DefaultStyle = "camera_roll";

    /// <param name="style">camera_roll | session_slot</param>
    public static string BuildMp4FileName(DateTime recordStartLocal, int slotIndex, int activeCameraCount, string style)
    {
        var stamp = recordStartLocal.ToString("yyyyMMdd_HHmmss");
        var normalized = (style ?? DefaultStyle).Trim().ToLowerInvariant();

        if (normalized == "session_slot")
            return $"cam{slotIndex + 1}.mp4";

        if (activeCameraCount <= 1)
            return $"MCAM_{stamp}.mp4";

        return $"MCAM_{stamp}_cam{slotIndex + 1}.mp4";
    }
}
