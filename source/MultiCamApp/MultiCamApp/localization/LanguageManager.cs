using System.Reflection;
using System.Text.Json;
using MultiCamApp.Utils;

namespace MultiCamApp.Localization;

public sealed class LanguageManager
{
    private Dictionary<string, string> _strings = new();
    public string CurrentLanguage { get; private set; } = "en";

    public event Action? LanguageChanged;

    public void Load(string languageCode)
    {
        var wantJa = languageCode == "ja";
        // English is always loaded as the base table and Japanese is overlaid on top, so a key
        // missing from ja.json shows English text instead of the raw key, and a malformed
        // localization file degrades to English instead of throwing during startup.
        var strings = ParseOrEmpty(ReadLocalizationJson("en.json"));
        var loadedJa = false;
        if (wantJa)
        {
            var ja = ParseOrEmpty(ReadLocalizationJson("ja.json"));
            loadedJa = ja.Count > 0;
            foreach (var (key, value) in ja)
                strings[key] = value;
        }

        _strings = strings;
        CurrentLanguage = wantJa && loadedJa ? "ja" : "en";
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    public string Get(string key) =>
        _strings.TryGetValue(key, out var v) ? v : key;

    public string this[string key] => Get(key);

    private static string? ReadLocalizationJson(string fileName)
    {
        var embedded = ReadEmbeddedJson(fileName);
        if (!string.IsNullOrWhiteSpace(embedded))
            return embedded;

        foreach (var path in JsonLoader.LocalizationCandidates(fileName))
        {
            if (!File.Exists(path)) continue;
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                /* try next */
            }
        }

        return null;
    }

    private static string? ReadEmbeddedJson(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var suffix = fileName.Replace('\\', '/');
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null) return null;

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
