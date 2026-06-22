using System.Text.RegularExpressions;

namespace MultiCamApp.Utils;

public static partial class PrivacySanitizer
{
    public const string Redacted = "redacted";

    public static string SanitizeForLog(string? value) => Sanitize(value);
    public static string SanitizeForOutput(string? value) => Sanitize(value);

    public static string FileNameOnly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try { return Path.GetFileName(path); }
        catch { return Sanitize(path); }
    }

    public static string RelativeOrFileName(string? baseFolder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            if (!string.IsNullOrWhiteSpace(baseFolder)
                && Path.IsPathRooted(path)
                && Directory.Exists(baseFolder))
            {
                var relative = Path.GetRelativePath(baseFolder, path);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                    return relative.Replace('\\', '/');
            }
            return Path.GetFileName(path);
        }
        catch
        {
            return Sanitize(path);
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? "";

        var result = value;
        result = WindowsPathRegex().Replace(result, match => Path.GetFileName(match.Value.TrimEnd('\\', '/')) is { Length: > 0 } name ? $"[path]/{name}" : "[path]");
        result = ReplaceIfKnown(result, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "[user-profile]");
        result = ReplaceIfKnown(result, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "[desktop]");
        result = ReplaceIfKnown(result, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "[documents]");
        result = ReplaceIfKnown(result, Environment.MachineName, "[computer]", requireDistinctToken: true);
        result = ReplaceIfKnown(result, Environment.UserName, "[user]", requireDistinctToken: true);

        result = DeviceInstanceRegex().Replace(result, "[device-id]");
        result = VidPidRegex().Replace(result, "[usb-id]");
        result = GuidRegex().Replace(result, "[guid]");
        result = LongHexRegex().Replace(result, "[id]");
        return result;
    }

    private static string ReplaceIfKnown(
        string input,
        string? sensitive,
        string replacement,
        bool requireDistinctToken = false)
    {
        if (string.IsNullOrWhiteSpace(sensitive))
            return input;
        if (requireDistinctToken && sensitive.Length < 3)
            return input;
        return input.Replace(sensitive, replacement, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\/:*?""<>|;\r\n]+\\)*[^\\/:*?""<>|;\r\n]*")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?i)(?:USB|HID|SWD|DISPLAY|PCI|BTH|ROOT|MMDEVAPI|\\\\\?\\)[\\#][^\s,;""']+|\\\\\?\\[^\s,;""']+")]
    private static partial Regex DeviceInstanceRegex();

    [GeneratedRegex(@"(?i)(VID|PID|MI|REV)_[0-9A-F]{2,}")]
    private static partial Regex VidPidRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{16,}\b")]
    private static partial Regex LongHexRegex();
}
