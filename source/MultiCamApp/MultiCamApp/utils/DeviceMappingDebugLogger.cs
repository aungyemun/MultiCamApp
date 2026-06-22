using System.Text;

namespace MultiCamApp.Utils;

public static class DeviceMappingDebugLogger
{
    private static readonly object Gate = new();

    public static void WriteMappingLog(string filename, Func<string> contentProvider)
    {
        try
        {
            var dir = PathHelper.LogsFolder();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, filename);
            File.WriteAllText(path, PrivacySanitizer.SanitizeForLog(contentProvider()), Encoding.UTF8);
        }
        catch
        {
            // best effort
        }
    }

    public static void AppendMappingLines(string filename, IEnumerable<string> lines)
    {
        try
        {
            var dir = PathHelper.LogsFolder();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, filename);
            lock (Gate)
            {
                foreach (var line in lines)
                    File.AppendAllText(path, PrivacySanitizer.SanitizeForLog(line) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // best effort
        }
    }
}
