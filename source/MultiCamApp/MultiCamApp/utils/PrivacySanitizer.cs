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
        // Separators may be single (plain text), doubled (already JSON-serialized text) or forward
        // slashes. Previously only single backslashes were understood, so a JSON-escaped path
        // ("C:\\Users\\...") was cut after "C:\" and the remainder produced an invalid "\U"
        // JSON escape — and the rest of the path (including the user folder) was not redacted.
        result = WindowsPathRegex().Replace(result, RedactPath);
        result = UncPathRegex().Replace(result, RedactPath);
        result = ReplaceIfKnown(result, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "[user-profile]");
        result = ReplaceIfKnown(result, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "[desktop]");
        result = ReplaceIfKnown(result, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "[documents]");
        result = ReplaceTokenIfKnown(result, Environment.MachineName, "[computer]");
        result = ReplaceTokenIfKnown(result, Environment.UserName, "[user]");

        result = DeviceInstanceRegex().Replace(result, "[device-id]");
        result = VidPidRegex().Replace(result, "[usb-id]");
        result = GuidRegex().Replace(result, "[guid]");
        result = LongHexRegex().Replace(result, "[id]");
        return result;
    }

    private static string RedactPath(Match match)
    {
        var trimmed = match.Value.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var name = cut >= 0 ? trimmed[(cut + 1)..] : "";
        return name.Length > 0 && !name.EndsWith(':') ? $"[path]/{name}" : "[path]";
    }

    private static string ReplaceIfKnown(string input, string? sensitive, string replacement)
    {
        if (string.IsNullOrWhiteSpace(sensitive))
            return input;
        return input.Replace(sensitive, replacement, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces a user/computer name only where it stands alone as a token. A plain substring
    /// replace turned e.g. user "cam" into "[user]eraName" inside unrelated words, JSON keys and
    /// CSV headers.
    /// </summary>
    public static string ReplaceTokenIfKnown(string input, string? sensitive, string replacement)
    {
        if (string.IsNullOrWhiteSpace(sensitive) || sensitive.Length < 3)
            return input;
        return Regex.Replace(
            input,
            $"(?<![A-Za-z0-9]){Regex.Escape(sensitive)}(?![A-Za-z0-9])",
            replacement.Replace("$", "$$"),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Drive paths: separators may be "\", "\\" (JSON-escaped) or "/". The lookbehind keeps
    // URL schemes ("https://") from being treated as a drive letter.
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:(?:\\{1,2}|/)(?:[^\\/:*?""<>|;\r\n]+(?:\\{1,2}|/))*[^\\/:*?""<>|;\r\n]*")]
    private static partial Regex WindowsPathRegex();

    // UNC paths (\\server\share\...), plain or JSON-escaped. Server names cannot contain "?",
    // so "\\?\" device paths are left to DeviceInstanceRegex.
    [GeneratedRegex(@"(?<![\\A-Za-z0-9])\\{2}(?:\\{2})?[A-Za-z0-9._$-]+(?:\\{1,2}[^\\/:*?""<>|;\r\n]+)+")]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"(?i)(?:USB|HID|SWD|DISPLAY|PCI|BTH|ROOT|MMDEVAPI|\\\\\?\\)[\\#][^\s,;""']+|\\\\\?\\[^\s,;""']+")]
    private static partial Regex DeviceInstanceRegex();

    [GeneratedRegex(@"(?i)(VID|PID|MI|REV)_[0-9A-F]{2,}")]
    private static partial Regex VidPidRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidRegex();

    // 2.1.0: not adjacent to "." or other word characters, so the digits of a decimal number
    // (e.g. 0.0016666666666666668) are never mistaken for a hardware ID.
    [GeneratedRegex(@"(?<![0-9A-Za-z_.])[0-9A-Fa-f]{16,}(?![0-9A-Za-z_.])")]
    private static partial Regex LongHexRegex();
}
