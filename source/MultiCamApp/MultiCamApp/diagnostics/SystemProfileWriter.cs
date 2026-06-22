using System.Text.Json;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

public static class SystemProfileWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static (string TimestampedPath, string LatestPath) Write(SystemProfile profile)
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);

        var stamp = profile.ScanTimeLocal == default ? DateTime.Now : profile.ScanTimeLocal;
        var timestamped = Path.Combine(dir, $"system_profile_{stamp:yyyyMMdd_HHmmss}.json");
        var latest = Path.Combine(dir, "SystemProfile.latest.json");
        var json = PrivacySanitizer.SanitizeForOutput(JsonSerializer.Serialize(profile, JsonOptions));
        File.WriteAllText(timestamped, json);
        File.WriteAllText(latest, json);
        return (timestamped, latest);
    }
}
