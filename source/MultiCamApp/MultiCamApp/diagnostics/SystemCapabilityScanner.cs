using System.Management;
using MultiCamApp.Core;

namespace MultiCamApp.Diagnostics;

public sealed class SystemCapabilityScanner
{
    public SystemProfile Scan(VersionInfo version)
    {
        var profile = SystemProfile.CreateEmpty(version);
        profile.Notes.Add("Hardware diagnostics are advisory only. MultiCamApp does not block presets based on this scan.");

        TryReadOperatingSystem(profile);
        TryReadCpu(profile);
        TryReadVideoControllers(profile);
        AddAdapterWarningsAndHints(profile);

        if (profile.TotalPhysicalMemoryBytes == null)
            profile.Warnings.Add("RAM information could not be read.");
        if (profile.DisplayAdapters.Count == 0)
            profile.Warnings.Add("Display adapter information could not be read.");

        return profile;
    }

    public static void AddAdapterWarningsAndHints(SystemProfile profile)
    {
        var names = profile.DisplayAdapters.Select(a => a.Name ?? "").ToList();
        profile.HasMicrosoftBasicDisplayAdapter = names.Any(IsMicrosoftBasicDisplayAdapter);
        profile.HasIntelDisplayAdapter = names.Any(n => n.Contains("Intel", StringComparison.OrdinalIgnoreCase));
        profile.HasNvidiaDisplayAdapter = names.Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        profile.HasAmdDisplayAdapter = names.Any(n =>
            n.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Radeon", StringComparison.OrdinalIgnoreCase));

        if (profile.HasMicrosoftBasicDisplayAdapter)
        {
            profile.Warnings.Add(
                "Microsoft Basic Display Adapter detected. Install the official Intel, NVIDIA, or AMD graphics driver for best preview and recording reliability.");
        }

        if (profile.HasIntelDisplayAdapter)
            profile.EncoderHints.Add("Intel graphics detected. Official Intel graphics drivers may improve preview stability.");
        if (profile.HasNvidiaDisplayAdapter)
            profile.EncoderHints.Add("NVIDIA graphics detected. Official NVIDIA drivers may improve display and encoding reliability.");
        if (profile.HasAmdDisplayAdapter)
            profile.EncoderHints.Add("AMD/Radeon graphics detected. Official AMD drivers may improve display and encoding reliability.");

        if (!profile.HasIntelDisplayAdapter && !profile.HasNvidiaDisplayAdapter && !profile.HasAmdDisplayAdapter)
            profile.EncoderHints.Add("No Intel/NVIDIA/AMD display adapter was identified. This may be normal on some systems.");
    }

    public static bool IsMicrosoftBasicDisplayAdapter(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase);

    private static void TryReadOperatingSystem(SystemProfile profile)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                var caption = Convert.ToString(obj["Caption"])?.Trim();
                var version = Convert.ToString(obj["Version"])?.Trim();
                profile.OsVersion = string.Join(" ", new[] { caption, version }.Where(s => !string.IsNullOrWhiteSpace(s)));

                if (ulong.TryParse(Convert.ToString(obj["TotalVisibleMemorySize"]), out var kb))
                    profile.TotalPhysicalMemoryBytes = kb * 1024UL;
                return;
            }
        }
        catch (Exception ex) when (IsSafeWmiFailure(ex))
        {
            profile.Warnings.Add($"OS/RAM WMI scan unavailable: {ex.GetType().Name}");
        }
    }

    private static void TryReadCpu(SystemProfile profile)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            var cpu = searcher.Get().OfType<ManagementObject>()
                .Select(o => Convert.ToString(o["Name"])?.Trim())
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            if (!string.IsNullOrWhiteSpace(cpu))
                profile.CpuName = cpu!;
        }
        catch (Exception ex) when (IsSafeWmiFailure(ex))
        {
            profile.Warnings.Add($"CPU WMI scan unavailable: {ex.GetType().Name}");
        }
    }

    private static void TryReadVideoControllers(SystemProfile profile)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverProviderName, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                profile.DisplayAdapters.Add(new DisplayAdapterProfile
                {
                    Name = Convert.ToString(obj["Name"])?.Trim() ?? "Unknown",
                    DriverProvider = Convert.ToString(obj["DriverProviderName"])?.Trim() ?? "Unknown",
                    DriverVersion = Convert.ToString(obj["DriverVersion"])?.Trim() ?? "Unknown",
                    DriverDate = Convert.ToString(obj["DriverDate"])?.Trim() ?? "Unknown",
                    AdapterRam = Convert.ToString(obj["AdapterRAM"])?.Trim() ?? "Unknown"
                });
            }
        }
        catch (Exception ex) when (IsSafeWmiFailure(ex))
        {
            profile.Warnings.Add($"Display adapter WMI scan unavailable: {ex.GetType().Name}");
        }
    }

    private static bool IsSafeWmiFailure(Exception ex) =>
        ex is ManagementException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or FileNotFoundException
            or TypeInitializationException;
}
