using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Localization;

public static class StartupLanguage
{
    public static LanguageManager Load()
    {
        var language = new LanguageManager();
        try
        {
            var config = JsonLoader.LoadFromFile<AppConfig>(JsonLoader.ConfigPath("appsettings.json"));
            language.Load(config?.DefaultLanguage ?? "en");
        }
        catch
        {
            language.Load("en");
        }

        return language;
    }
}
