namespace MultiCamApp.Capture;

/// <summary>Standard capture presets exposed in the UI as 360p, 720p, and 1080p.</summary>
public static class CaptureResolutionPreset
{
    public const string Label360 = "360p";
    public const string Label720 = "720p";
    public const string Label1080 = "1080p";

    public const int Width360 = 640;
    public const int Height360 = 480;
    public const int Width720 = 1280;
    public const int Height720 = 720;
    public const int Width1080 = 1920;
    public const int Height1080 = 1080;

    public static bool TryFromLabel(string? label, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (label.Trim().Equals("native", StringComparison.OrdinalIgnoreCase))
            return false;

        switch (label.Trim().ToLowerInvariant())
        {
            case Label360:
                width = Width360;
                height = Height360;
                return true;
            case Label720:
                width = Width720;
                height = Height720;
                return true;
            case Label1080:
                width = Width1080;
                height = Height1080;
                return true;
            default:
                return TryParseLegacyWxH(label, out width, out height);
        }
    }

    public static bool TryFromPixels(int width, int height, out string label)
    {
        label = ToLabel(width, height);
        return IsStandardPreset(width, height);
    }

    public static string ToLabel(int width, int height)
    {
        if (width == Width360 && height == Height360) return Label360;
        if (width == Width720 && height == Height720) return Label720;
        if (width == Width1080 && height == Height1080) return Label1080;
        return width > 0 && height > 0 ? $"{width}x{height}" : "";
    }

    public static string FormatWithFps(int width, int height, double fps) =>
        string.IsNullOrEmpty(ToLabel(width, height))
            ? $"@ {fps:F0}"
            : $"{ToLabel(width, height)} @ {fps:F0}";

    /// <summary>User-facing label: preset name when known, legacy WxH otherwise.</summary>
    public static string FormatDisplayLabel(string? text, int width = 0, int height = 0)
    {
        if (!string.IsNullOrWhiteSpace(text) && !text.Trim().Equals("-", StringComparison.Ordinal))
        {
            if (TryFromLabel(text, out var w, out var h))
                return ToLabel(w, h);
            return text.Trim();
        }

        if (width > 0 && height > 0)
            return ToLabel(width, height);

        return string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
    }

    public static bool IsStandardPreset(int width, int height) =>
        (width == Width360 && height == Height360)
        || (width == Width720 && height == Height720)
        || (width == Width1080 && height == Height1080);

    public static bool TryParseLegacyWxH(string? text, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Replace(" ", "").Replace("×", "x");
        if (normalized.Equals("0x0", StringComparison.OrdinalIgnoreCase))
            return false;

        var idx = normalized.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (idx <= 0 || idx >= normalized.Length - 1)
            return false;

        return int.TryParse(normalized[..idx], out width)
               && int.TryParse(normalized[(idx + 1)..], out height)
               && width > 0
               && height > 0;
    }
}
