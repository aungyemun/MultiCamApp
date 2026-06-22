////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Text;
using System.Text.RegularExpressions;

namespace MultiCamApp.Recording;

public sealed class SessionFolderPlan
{
    public string SessionTitleOriginal { get; init; } = "";
    public string SanitizedTitlePart { get; init; } = "Session";
    public string TimestampPart { get; init; } = "";
    public string FolderName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public DateTime RecordingDateTimeLocal { get; init; }
}

/// <summary>
/// Builds scientific session folder names: Title_YYYYMMDD_HHMMSS with unique suffix if needed.
/// </summary>
public static partial class SessionFolderNameGenerator
{
    public const int MaxFolderNameLength = 80;
    private const int MaxUniqueSuffixAttempts = 99;

    [GeneratedRegex(@"_+", RegexOptions.Compiled)]
    private static partial Regex MultipleUnderscoresRegex();

    public static string SanitizeTitle(string? userTitle)
    {
        if (string.IsNullOrWhiteSpace(userTitle))
            return "Session";

        var sb = new StringBuilder(userTitle.Length);
        foreach (var c in userTitle.Trim())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is ' ' or '_' or '-')
                sb.Append('_');
            else if (!Path.GetInvalidFileNameChars().Contains(c))
                sb.Append(c);
            else
                sb.Append('_');
        }

        var name = MultipleUnderscoresRegex().Replace(sb.ToString(), "_").Trim('_', '.', ' ');
        return string.IsNullOrWhiteSpace(name) ? "Session" : name;
    }

    public static string FormatTimestamp(DateTime localTime) =>
        localTime.ToString("yyyyMMdd_HHmmss");

    public static string BuildFolderName(string sanitizedTitle, DateTime localTime)
    {
        var title = string.IsNullOrWhiteSpace(sanitizedTitle) ? "Session" : sanitizedTitle;
        return $"{title}_{FormatTimestamp(localTime)}";
    }

    public static string PreviewFolderName(string? userTitle, DateTime? localTime = null)
    {
        var original = userTitle?.Trim() ?? "";
        var sanitized = string.IsNullOrWhiteSpace(original) ? "Session" : SanitizeTitle(original);
        return TruncateFolderName(BuildFolderName(sanitized, localTime ?? DateTime.Now));
    }

    public static SessionFolderPlan CreateUniqueSessionFolder(string baseFolder, string? userTitle, DateTime? localTime = null)
    {
        Directory.CreateDirectory(baseFolder);

        var original = userTitle?.Trim() ?? "";
        var recordingTime = localTime ?? DateTime.Now;
        var sanitized = string.IsNullOrWhiteSpace(original) ? "Session" : SanitizeTitle(original);
        var timestamp = FormatTimestamp(recordingTime);
        var baseName = TruncateFolderName($"{sanitized}_{timestamp}");

        var folderName = baseName;
        var fullPath = Path.Combine(baseFolder, folderName);
        var suffix = 1;

        while (Directory.Exists(fullPath))
        {
            if (suffix > MaxUniqueSuffixAttempts)
                throw new IOException($"Could not create unique session folder under '{baseFolder}' (too many duplicates).");

            folderName = $"{baseName}_{suffix:D2}";
            fullPath = Path.Combine(baseFolder, folderName);
            suffix++;
        }

        Directory.CreateDirectory(fullPath);

        return new SessionFolderPlan
        {
            SessionTitleOriginal = string.IsNullOrWhiteSpace(original) ? "" : original,
            SanitizedTitlePart = sanitized,
            TimestampPart = timestamp,
            FolderName = folderName,
            FullPath = fullPath,
            RecordingDateTimeLocal = recordingTime
        };
    }

    public static string TruncateFolderName(string folderName)
    {
        if (folderName.Length <= MaxFolderNameLength)
            return folderName;

        var stampMatch = TimestampSuffixRegex().Match(folderName);
        if (!stampMatch.Success)
            return folderName[..MaxFolderNameLength].TrimEnd('_', '.');

        var stamp = stampMatch.Value;
        var prefixMax = Math.Max(8, MaxFolderNameLength - stamp.Length);
        var prefix = folderName[..stampMatch.Index].TrimEnd('_');
        if (prefix.Length > prefixMax)
            prefix = prefix[..prefixMax].TrimEnd('_');

        return $"{prefix}{stamp}";
    }

    [GeneratedRegex(@"_\d{8}_\d{6}$", RegexOptions.Compiled)]
    private static partial Regex TimestampSuffixRegex();
}
