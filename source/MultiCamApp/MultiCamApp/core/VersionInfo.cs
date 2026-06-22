namespace MultiCamApp.Core;

public sealed class VersionInfo
{
    public string Version { get; set; } = "0.0.1";
    public int Build { get; set; } = 1;
    public string Stage { get; set; } = "experimental";
    public string ReleaseDate { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Legacy JSON key.</summary>
    public string ReleaseStage
    {
        get => Stage;
        set => Stage = value;
    }

    public string Display => $"{Version} (build {Build}, {Stage})";

    /// <summary>Compact label for header bar, e.g. v0.0.30</summary>
    public string HeaderVersionLabel => $"v{Version}";

    /// <summary>Tooltip for version badge in header.</summary>
    public string StageTooltip =>
        string.IsNullOrWhiteSpace(Stage)
            ? $"Build {Build}"
            : $"{char.ToUpper(Stage[0])}{Stage[1..].Replace('_', ' ')} · Build {Build}";

    public string AboutLine => string.IsNullOrEmpty(ReleaseDate)
        ? $"Version {Version} · Build {Build} · {Stage}"
        : $"Version {Version} · Build {Build} · {Stage} · {ReleaseDate}";
}
