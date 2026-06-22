////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Utils;

public static class PathHelper
{
    public static string UserDataRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MultiCamApp");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// App-folder log folder (<c>{app}\logs\</c>).
    /// Per product requirement, debug logs must stay inside the app folder.
    /// Logging callers must handle any IO failures gracefully.
    /// </summary>
    public static string LogsFolder()
    {
        var installLogs = Path.Combine(AppContext.BaseDirectory, "logs");
        return installLogs;
    }

    public static string DefaultVideosFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos")
            : videos;
    }

    public static string? FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "source", "MultiCamApp")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public static string SanitizeSessionName(string name) =>
        Recording.SessionFolderNameGenerator.SanitizeTitle(name);

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
