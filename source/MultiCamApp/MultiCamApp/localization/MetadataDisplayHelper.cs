using MultiCamApp.Metadata;

namespace MultiCamApp.Localization;

/// <summary>
/// Localized labels for metadata shown in verification UI.
/// Recording files (metadata.json / metadata.txt) always use English field names.
/// </summary>
public static class MetadataDisplayHelper
{
    public static string MetadataStatus(LanguageManager lang, bool found) =>
        found ? lang["verifyMetadataFound"] : lang["verifyMetadataMissing"];

    public static string LocalizeScientificTimingMessage(LanguageManager lang, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return lang["metaScientificTimingDefaultMessage"];

        if (string.Equals(message.Trim(), ScientificTimingAssessor.DefaultMessage, StringComparison.Ordinal))
            return lang["metaScientificTimingDefaultMessage"];

        return message;
    }

    public static string LocalizeCaptureIntervalNote(LanguageManager lang, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";

        if (string.Equals(message.Trim(), CaptureTimingSnapshot.UnavailableMessage, StringComparison.Ordinal))
            return lang["metaCaptureIntervalUnavailable"];

        return message;
    }

    public static string LocalizeUnavailableToken(LanguageManager lang, string value) =>
        string.Equals(value, "Unavailable", StringComparison.OrdinalIgnoreCase)
            ? lang["metaValueUnavailable"]
            : value;
}
