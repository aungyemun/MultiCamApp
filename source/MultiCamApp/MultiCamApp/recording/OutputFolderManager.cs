////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Recording;

public sealed class OutputFolderManager
{
    public string ResolveBaseFolder(string? userFolder)
    {
        if (!string.IsNullOrWhiteSpace(userFolder) && Directory.Exists(userFolder))
            return userFolder;
        return Utils.PathHelper.DefaultVideosFolder();
    }

    public SessionFolderPlan CreateSessionFolder(string baseFolder, string? sessionTitle) =>
        SessionFolderNameGenerator.CreateUniqueSessionFolder(baseFolder, sessionTitle);

    public string CameraFolder(string sessionPath, int slotIndex) =>
        Directory.CreateDirectory(Path.Combine(sessionPath, $"cam{slotIndex + 1}")).FullName;
}
