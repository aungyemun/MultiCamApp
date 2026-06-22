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
        var json = wantJa ? ReadLocalizationJson("ja.json") : null;
        var loadedJa = !string.IsNullOrWhiteSpace(json);
        if (!loadedJa)
            json = ReadLocalizationJson("en.json");
        if (string.IsNullOrWhiteSpace(json))
            json = "{}";

        _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        CurrentLanguage = wantJa && loadedJa ? "ja" : "en";
        LanguageChanged?.Invoke();
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
