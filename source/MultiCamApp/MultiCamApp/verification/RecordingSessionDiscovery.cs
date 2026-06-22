////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Text.RegularExpressions;

namespace MultiCamApp.Verification;

public static class RecordingSessionDiscovery
{
    private static readonly Regex CamFolderRegex = new(
        @"^cam(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSessionFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        return Directory.GetDirectories(folderPath)
            .Select(Path.GetFileName)
            .Any(name => !string.IsNullOrEmpty(name) && CamFolderRegex.IsMatch(name));
    }

    public static IReadOnlyList<string> DiscoverSessionFolders(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            return [];

        if (IsSessionFolder(rootFolder))
            return [Path.GetFullPath(rootFolder)];

        var sessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(rootFolder, "*", SearchOption.AllDirectories))
        {
            if (!IsSessionFolder(dir))
                continue;

            var full = Path.GetFullPath(dir);
            if (!sessions.Any(existing => IsParentOf(existing, full) || IsParentOf(full, existing)))
                sessions.Add(full);
            else if (sessions.Any(existing => IsParentOf(existing, full)))
            {
                // Prefer the deeper / more specific session folder.
                sessions.RemoveWhere(existing => IsParentOf(existing, full));
                sessions.Add(full);
            }
        }

        return sessions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> GetCameraFolders(string sessionFolder)
    {
        if (!Directory.Exists(sessionFolder))
            return [];

        return Directory.GetDirectories(sessionFolder)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return !string.IsNullOrEmpty(name) && CamFolderRegex.IsMatch(name);
            })
            .OrderBy(d => CamSlotSortKey(Path.GetFileName(d)!))
            .ToList();
    }

    public static string CameraSlotFromFolder(string cameraFolderPath)
    {
        var name = Path.GetFileName(cameraFolderPath);
        var match = CamFolderRegex.Match(name ?? "");
        return match.Success ? $"cam{match.Groups[1].Value}" : name ?? "unknown";
    }

    private static int CamSlotSortKey(string folderName)
    {
        var match = CamFolderRegex.Match(folderName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : int.MaxValue;
    }

    private static bool IsParentOf(string parent, string child) =>
        child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
