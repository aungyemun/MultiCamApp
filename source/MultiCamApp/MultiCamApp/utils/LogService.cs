namespace MultiCamApp.Utils;

public sealed class LogService
{
    private readonly object _lock = new();
    private readonly string _logDir;
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFilesPerType = 20;

    public LogService()
    {
        _logDir = PathHelper.LogsFolder();
    }

    public void Info(string category, string message) => Write("INFO", category, message);
    public void Warn(string category, string message) => Write("WARN", category, message);
    public void Error(string category, string message, Exception? ex = null) =>
        Write("ERROR", category, ex == null ? message : $"{message} | {ex}");

    private void Write(string level, string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {PrivacySanitizer.SanitizeForLog(message)}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_logDir);
                var fileBase = $"multicam_{DateTime.Now:yyyyMMdd}";
                var path = Path.Combine(_logDir, $"{fileBase}.txt");
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    var rotated = Path.Combine(_logDir, $"{fileBase}_{DateTime.Now:HHmmss}.txt");
                    File.Move(path, rotated, overwrite: true);
                    LogRetention.PruneLatestFiles(_logDir, "multicam_*.txt", MaxFilesPerType);
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch { /* avoid crash on log failure */ }
        }
    }
}
