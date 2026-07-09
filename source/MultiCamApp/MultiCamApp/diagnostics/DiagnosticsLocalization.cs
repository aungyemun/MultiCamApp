using MultiCamApp.Localization;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// Set by <c>HardwareDiagnosticsPage.ApplyLanguage</c> so the scanner classes below — which run
/// on a background thread via <c>Task.Run</c> and have no direct UI/LanguageManager reference —
/// can localize the Notes/Warnings/EncoderHints text that gets displayed verbatim in the UI.
/// </summary>
internal static class DiagnosticsLocalization
{
    internal static LanguageManager? Current;

    internal static string T(string key, string fallback) =>
        Current?[key] is { Length: > 0 } v ? v : fallback;
}
