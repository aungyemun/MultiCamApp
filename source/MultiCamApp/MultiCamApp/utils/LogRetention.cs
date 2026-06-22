namespace MultiCamApp.Utils;

internal static class LogRetention
{
    public static void PruneLatestFiles(string dir, string searchPattern, int maxFiles)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, searchPattern);
            if (files.Length <= maxFiles) return;

            foreach (var f in files
                         .Select(p => new FileInfo(p))
                         .OrderByDescending(fi => fi.LastWriteTimeUtc)
                         .Skip(maxFiles))
            {
                try { f.Delete(); } catch { }
            }
        }
        catch { }
    }
}

