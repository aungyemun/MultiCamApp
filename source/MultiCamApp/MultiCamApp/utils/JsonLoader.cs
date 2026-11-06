using System.Text.Json;

namespace MultiCamApp.Utils;

public static class JsonLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T? LoadFromFile<T>(string path) where T : class
    {
        var resolved = ResolveExistingPath(ConfigCandidates(Path.GetFileName(path)));
        if (resolved == null) return null;
        try
        {
            var json = ReadAllTextWithRetry(resolved);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient lock (antivirus scan right after install, an editor) or permissions: the
            // file is not corrupt, so it must NOT be renamed — renaming the shipped config made
            // the next launch fail startup diagnostics until the app was reinstalled.
            TryLogConfigError($"Could not read config {resolved}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var badPath = resolved + ".bad_" + timestamp;
                File.Move(resolved, badPath);
                // Log to a simple file since LogService might not be ready
                var logPath = Path.Combine(PathHelper.LogsFolder(), "config_error.log");
                var logDir = Path.GetDirectoryName(logPath);
                if (logDir != null && !Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                File.AppendAllText(logPath, PrivacySanitizer.SanitizeForLog($"[{DateTime.Now}] Corrupted config found at {resolved}. Renamed to {badPath}. Error: {ex.Message}\n"));
            }
            catch { /* best effort */ }
            return null;
        }
    }

    private static string ReadAllTextWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 3) { Thread.Sleep(150); }
        }
    }

    private static void TryLogConfigError(string message)
    {
        try
        {
            var logPath = Path.Combine(PathHelper.LogsFolder(), "config_error.log");
            var logDir = Path.GetDirectoryName(logPath);
            if (logDir != null && !Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            File.AppendAllText(logPath, PrivacySanitizer.SanitizeForLog($"[{DateTime.Now}] {message}\n"));
        }
        catch { /* best effort */ }
    }

    public static string ConfigPath(string fileName) =>
        ResolveExistingPath(ConfigCandidates(fileName))
        ?? ConfigCandidates(fileName)[0];

    public static string LocalizationPath(string fileName) =>
        ResolveExistingPath(LocalizationCandidates(fileName))
        ?? LocalizationCandidates(fileName)[0];

    public static string[] LocalizationCandidates(string fileName)
    {
        var list = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "localization", fileName)
        };
        if (PathHelper.FindProjectRoot() is { } root)
        {
            list.Add(Path.Combine(root, "dist", "localization", fileName));
            list.Add(Path.Combine(root, "source", "MultiCamApp", "MultiCamApp", "localization", fileName));
        }

        return list.ToArray();
    }

    private static string[] ConfigCandidates(string fileName)
    {
        var list = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "config", fileName)
        };
        if (PathHelper.FindProjectRoot() is { } root)
        {
            list.Add(Path.Combine(root, "dist", "config", fileName));
            list.Add(Path.Combine(root, "source", "MultiCamApp", "MultiCamApp", "config", fileName));
        }

        return list.ToArray();
    }

    private static string? ResolveExistingPath(IEnumerable<string> candidates)
    {
        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
