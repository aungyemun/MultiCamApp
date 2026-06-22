using System.Text;
using MultiCamApp.Core;

namespace MultiCamApp.Utils;

/// <summary>Lightweight rotating text logs for runtime, preview startup, recording, and failures.</summary>
public static class AppDiagnosticLogger
{
    private const int MaxFilesPerType = 20;
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string PrimaryDir = PathHelper.LogsFolder();

    public static void Runtime(string message) => Append("app_runtime", $"app_runtime_{DateTime.Now:yyyyMMdd}.txt", message);

    /// <summary>Recording lifecycle events (start/stop, per-slot, failures) — daily rotating file.</summary>
    public static void Recording(string message) =>
        Append("recording_runtime", $"recording_runtime_{DateTime.Now:yyyyMMdd}.log", message);

    public static void Failure(string component, string message, Exception? ex = null) =>
        Append("crash_or_failure", $"crash_or_failure_{DateTime.Now:yyyyMMdd}.txt",
            BuildFailureBlock(component, message, ex));

    public static void PreviewSlotFailure(
        int slotIndex,
        string slotName,
        int layoutCount,
        string presetLabel,
        double requestedFps,
        string category,
        string message,
        string? deviceId,
        string? deviceName,
        long elapsedMs,
        Exception? ex = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"component=preview");
        sb.AppendLine($"slot={slotName}");
        sb.AppendLine($"slotIndex={slotIndex}");
        sb.AppendLine($"layout={layoutCount}");
        sb.AppendLine($"preset={presetLabel}");
        sb.AppendLine($"requestedFps={requestedFps:F0}");
        sb.AppendLine($"category={category}");
        sb.AppendLine($"message={message}");
        sb.AppendLine($"deviceName={deviceName ?? "?"}");
        sb.AppendLine($"deviceId={deviceId ?? "?"}");
        sb.AppendLine($"elapsedMs={elapsedMs}");
        if (ex != null)
        {
            sb.AppendLine($"exception={ex.GetType().FullName}");
            sb.AppendLine($"detail={ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Append("crash_or_failure", $"crash_or_failure_{DateTime.Now:yyyyMMdd}.txt", sb.ToString().TrimEnd());
    }

    public static PreviewStartupLogSession BeginPreviewStartup() => new();

    public static RecordingSessionLogSession BeginRecordingSession(string sessionPath) =>
        new(sessionPath);

    private static string BuildFailureBlock(string component, string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"component={component}");
        sb.AppendLine($"message={message}");
        if (ex != null)
        {
            sb.AppendLine($"exception={ex.GetType().FullName}");
            sb.AppendLine($"detail={ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }
        return sb.ToString().TrimEnd();
    }

    internal static void Append(string typePrefix, string fileName, string body)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {PrivacySanitizer.SanitizeForLog(body)}";
        lock (Gate)
        {
            WriteRotating(PrimaryDir, fileName, line);
            PruneOldFiles(PrimaryDir, typePrefix);
        }
    }

    private static void WriteRotating(string dir, string fileName, string line)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
            {
                var rotated = Path.Combine(dir,
                    Path.GetFileNameWithoutExtension(fileName) + $"_{DateTime.Now:HHmmss}" + Path.GetExtension(fileName));
                File.Move(path, rotated, overwrite: true);
            }
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* never crash on logging */ }
    }

    private static void PruneOldFiles(string dir, string typePrefix)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            // keep preview/recording/failure logs from growing indefinitely
            var files = Directory.GetFiles(dir, $"{typePrefix}*")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(MaxFilesPerType)
                .ToList();
            foreach (var f in files)
                f.Delete();
        }
        catch { }
    }
}

public sealed class PreviewStartupLogSession : IDisposable
{
    private readonly string _path;
    private readonly StringBuilder _sb = new();
    private bool _closed;

    internal PreviewStartupLogSession()
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"preview_startup_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        Line($"appVersion={VersionService.Load().Display}");
        Line($"timestamp={DateTime.Now:O}");
    }

    public void Line(string text) => _sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {PrivacySanitizer.SanitizeForLog(text)}");

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        try
        {
            File.WriteAllText(_path, _sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}

/// <summary>
/// Per-recording session trace written incrementally to logs/recording_session_YYYYMMDD_HHMMSS.log
/// (survives mid-session crashes better than end-only flush).
/// </summary>
public sealed class RecordingSessionLogSession : IDisposable
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private readonly string _path;
    private readonly object _writeLock = new();
    private readonly StringBuilder _sb = new();
    private bool _closed;
    private long _bytesWritten;

    public string LogFilePath => _path;

    internal RecordingSessionLogSession(string sessionPath)
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        var now = DateTime.Now;
        _path = Path.Combine(dir, $"recording_engine_trace_{now:yyyyMMdd_HHmmss}_{now:fff}.txt");
        WriteHeader(sessionPath);
        try { _bytesWritten = new FileInfo(_path).Length; } catch { _bytesWritten = 0; }
        LogRetention.PruneLatestFiles(dir, "recording_engine_trace_*.*", 20);
    }

    private void WriteHeader(string sessionPath)
    {
        Line($"sessionPath={PrivacySanitizer.FileNameOnly(sessionPath)}");
        Line($"timestamp={DateTime.Now:O}");
        Line($"appVersion={VersionService.Load().Display}");
    }

    public void Line(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {PrivacySanitizer.SanitizeForLog(text)}";
        lock (_writeLock)
        {
            _sb.AppendLine(line);
            if (_closed) return;
            try
            {
                var bytes = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
                if (_bytesWritten + bytes > MaxFileBytes)
                {
                    _closed = true;
                    return;
                }
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                _bytesWritten += bytes;
            }
            catch { }
        }
    }

    public void Section(string title) => Line($"--- {title} ---");

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_closed) return;
            _closed = true;
        }

        try
        {
            // keep inside app-folder logs only
        }
        catch { }
    }
}
