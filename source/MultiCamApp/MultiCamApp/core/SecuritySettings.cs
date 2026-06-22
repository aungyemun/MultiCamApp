namespace MultiCamApp.Core;

/// <summary>Security and privacy policy flags (appsettings.json security section).</summary>
public sealed class SecuritySettings
{
    public bool DisableAntivirusBypass { get; set; } = true;
    public bool RequireAntivirusExclusion { get; set; }
    public bool HiddenRecordingAllowed { get; set; }
    public bool RunAtStartup { get; set; }
    public bool InstallBackgroundService { get; set; }
    public bool InstallScheduledTask { get; set; }
    public bool TelemetryEnabled { get; set; }
    public bool NetworkRequired { get; set; }
    public bool AutoDownloadExecutables { get; set; }
    public bool ManualUpdateOnly { get; set; } = true;
    public bool CameraAccessOnlyAfterUserAction { get; set; } = true;
    public bool RecordingOnlyAfterUserAction { get; set; } = true;
    public bool ReleaseCameraOnExit { get; set; } = true;
}
