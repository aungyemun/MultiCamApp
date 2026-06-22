using MultiCamApp.Utils;

namespace MultiCamApp.Core;

public static class VersionService
{
    private static VersionInfo? _current;

    public static VersionInfo Current
    {
        get
        {
            _current ??= Load();
            return _current;
        }
    }

    public static void Reload() => _current = Load();

    public static VersionInfo Load()
    {
        var info = JsonLoader.LoadFromFile<VersionInfo>(JsonLoader.ConfigPath("version.json"))
                   ?? new VersionInfo();
        if (string.IsNullOrWhiteSpace(info.Stage))
            info.Stage = InferStage(info.Version);
        return info;
    }

    public static string InferStage(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 2) return "experimental";
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return "experimental";

        if (major >= 1) return "stable";
        if (minor >= 9) return "release_candidate";
        if (minor >= 5) return "beta";
        if (minor >= 2) return "feature_milestone";
        if (minor >= 1) return "alpha";
        return "experimental";
    }
}
