////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

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

    /// <summary>
    /// Resolves a per-camera metadata file, preferring the slot-prefixed name VideoEngineV2 writes
    /// as its primary output (e.g. "cam1_metadata.json") over the unprefixed "metadata.json" — a
    /// byte-identical duplicate that used to be the *only* name this scanner recognized, forcing
    /// every V2 recording to write two copies of each metadata file just for compatibility. Falls
    /// back to the unprefixed name for older recordings or the legacy OpenCV engine (which only
    /// ever writes the unprefixed form), so existing sessions keep working either way.
    /// </summary>
    public static string? FindCameraMetadataFile(string cameraFolder, string slot, string extension)
    {
        var prefixed = Path.Combine(cameraFolder, $"{slot}_metadata.{extension}");
        if (File.Exists(prefixed)) return prefixed;
        var unprefixed = Path.Combine(cameraFolder, $"metadata.{extension}");
        return File.Exists(unprefixed) ? unprefixed : null;
    }
}
