namespace MultiCamApp.Experiment;

public static class ExperimentDurationParser
{
    public static int ParseToSeconds(string? text, int defaultSeconds = 600)
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultSeconds;
        text = text.Trim();

        if (text.Contains(':'))
        {
            var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var minutes) &&
                int.TryParse(parts[1], out var seconds))
                return Math.Max(1, minutes * 60 + seconds);
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out var hours) &&
                int.TryParse(parts[1], out minutes) &&
                int.TryParse(parts[2], out seconds))
                return Math.Max(1, hours * 3600 + minutes * 60 + seconds);
        }

        return int.TryParse(text, out var direct) ? Math.Max(1, direct) : defaultSeconds;
    }

    public static string FormatMinutesSeconds(int totalSeconds)
    {
        var m = totalSeconds / 60;
        var s = totalSeconds % 60;
        return $"{m}:{s:D2}";
    }
}
