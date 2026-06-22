////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Text.RegularExpressions;
using MultiCamApp.Core;

namespace MultiCamApp.Verification;

public sealed class VideoScanner
{
    private static readonly Regex CamFolderRegex = new(
        @"^cam(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<string> DiscoverSessions(string rootFolder) =>
        RecordingSessionDiscovery.DiscoverSessionFolders(rootFolder);

    public IReadOnlyList<VideoFileEntry> Scan(string rootFolder, VerificationSettings settings)
    {
        if (!Directory.Exists(rootFolder))
            throw new DirectoryNotFoundException($"Folder not found: {rootFolder}");

        var sessions = RecordingSessionDiscovery.DiscoverSessionFolders(rootFolder);
        if (sessions.Count == 0)
            return ScanLooseFiles(rootFolder, settings);

        var entries = new List<VideoFileEntry>();
        foreach (var session in sessions)
            entries.AddRange(ScanSession(session, settings));

        return entries
            .OrderBy(e => e.SessionFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.CameraSlot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<VideoFileEntry> ScanSession(string sessionFolder, VerificationSettings settings)
    {
        var extensions = settings.ScanExtensions
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entries = new List<VideoFileEntry>();
        foreach (var cameraFolder in RecordingSessionDiscovery.GetCameraFolders(sessionFolder))
        {
            var slot = RecordingSessionDiscovery.CameraSlotFromFolder(cameraFolder);
            var mp4Files = Directory.EnumerateFiles(cameraFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (mp4Files.Count == 0)
            {
                entries.Add(CreateMissingEntry(sessionFolder, slot, cameraFolder, null, settings));
                continue;
            }

            foreach (var path in mp4Files)
            {
                entries.Add(CreateEntry(sessionFolder, slot, cameraFolder, path, settings));
            }
        }

        return entries;
    }

    private static VideoFileEntry CreateEntry(
        string sessionFolder,
        string slot,
        string cameraFolder,
        string mp4Path,
        VerificationSettings settings)
    {
        var metaTxt = Path.Combine(cameraFolder, "metadata.txt");
        var metaJson = Path.Combine(cameraFolder, "metadata.json");
        return new VideoFileEntry
        {
            CameraSlot = slot,
            SessionFolder = sessionFolder,
            SessionLabel = Path.GetFileName(sessionFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            CameraFolder = cameraFolder,
            FileName = Path.GetFileName(mp4Path),
            FullPath = mp4Path,
            MetadataPath = File.Exists(metaTxt) ? metaTxt : null,
            MetadataJsonPath = File.Exists(metaJson) ? metaJson : null
        };
    }

    private static VideoFileEntry CreateMissingEntry(
        string sessionFolder,
        string slot,
        string cameraFolder,
        string? mp4Path,
        VerificationSettings settings) =>
        new()
        {
            CameraSlot = slot,
            SessionFolder = sessionFolder,
            SessionLabel = Path.GetFileName(sessionFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            CameraFolder = cameraFolder,
            FileName = mp4Path == null ? "(missing mp4)" : Path.GetFileName(mp4Path),
            FullPath = mp4Path ?? Path.Combine(cameraFolder, $"{slot}.mp4"),
            MetadataPath = File.Exists(Path.Combine(cameraFolder, "metadata.txt"))
                ? Path.Combine(cameraFolder, "metadata.txt")
                : null,
            MetadataJsonPath = File.Exists(Path.Combine(cameraFolder, "metadata.json"))
                ? Path.Combine(cameraFolder, "metadata.json")
                : null,
            IsMissingVideo = mp4Path == null
        };

    private IReadOnlyList<VideoFileEntry> ScanLooseFiles(string rootFolder, VerificationSettings settings)
    {
        var extensions = settings.ScanExtensions
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var option = settings.RecursiveScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(rootFolder, "*.*", option)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = new List<VideoFileEntry>();
        foreach (var path in files)
        {
            var dir = Path.GetDirectoryName(path)!;
            var slot = InferCameraSlot(path, rootFolder);
            var session = InferSessionFolder(path, rootFolder);
            var metaTxt = Path.Combine(dir, "metadata.txt");
            var metaJson = Path.Combine(dir, "metadata.json");
            entries.Add(new VideoFileEntry
            {
                CameraSlot = slot,
                SessionFolder = session,
                SessionLabel = Path.GetFileName(session.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                CameraFolder = dir,
                FileName = Path.GetFileName(path),
                FullPath = path,
                MetadataPath = File.Exists(metaTxt) ? metaTxt : null,
                MetadataJsonPath = File.Exists(metaJson) ? metaJson : null
            });
        }

        return entries;
    }

    private static string InferCameraSlot(string filePath, string sessionRoot)
    {
        var rel = Path.GetRelativePath(sessionRoot, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            var m = CamFolderRegex.Match(part);
            if (m.Success) return $"cam{m.Groups[1].Value}";
        }

        var name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        var camInName = Regex.Match(name, @"cam(\d+)");
        if (camInName.Success) return $"cam{camInName.Groups[1].Value}";
        return "unknown";
    }

    internal static string InferSessionFolder(string filePath, string scanRoot)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            return scanRoot;

        var current = dir;
        while (!string.IsNullOrEmpty(current))
        {
            if (RecordingSessionDiscovery.IsSessionFolder(current))
                return current;

            if (string.Equals(current, scanRoot, StringComparison.OrdinalIgnoreCase))
                break;

            current = Path.GetDirectoryName(current) ?? "";
        }

        return scanRoot;
    }
}
