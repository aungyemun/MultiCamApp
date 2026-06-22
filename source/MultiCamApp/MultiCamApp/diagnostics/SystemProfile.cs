using MultiCamApp.Core;

namespace MultiCamApp.Diagnostics;

public sealed class SystemProfile
{
    public string AppVersion { get; set; } = "";
    public int BuildNumber { get; set; }
    public DateTime ScanTimeLocal { get; set; } = DateTime.Now;
    public string OsVersion { get; set; } = "";
    public string CpuName { get; set; } = "Unknown";
    public ulong? TotalPhysicalMemoryBytes { get; set; }
    public List<DisplayAdapterProfile> DisplayAdapters { get; set; } = [];
    public bool HasMicrosoftBasicDisplayAdapter { get; set; }
    public bool HasIntelDisplayAdapter { get; set; }
    public bool HasNvidiaDisplayAdapter { get; set; }
    public bool HasAmdDisplayAdapter { get; set; }
    public List<string> EncoderHints { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Notes { get; set; } = [];

    public static SystemProfile CreateEmpty(VersionInfo version)
    {
        return new SystemProfile
        {
            AppVersion = version.Version,
            BuildNumber = version.Build,
            ScanTimeLocal = DateTime.Now,
            OsVersion = Environment.OSVersion.VersionString,
            CpuName = "Unknown"
        };
    }
}

public sealed class DisplayAdapterProfile
{
    public string Name { get; set; } = "Unknown";
    public string DriverProvider { get; set; } = "Unknown";
    public string DriverVersion { get; set; } = "Unknown";
    public string DriverDate { get; set; } = "Unknown";
    public string AdapterRam { get; set; } = "Unknown";
}
