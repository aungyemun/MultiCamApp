using System.Management;
using MultiCamApp.Core;

namespace MultiCamApp.Diagnostics;

public sealed class SystemCapabilityScanner
{
    public SystemProfile Scan(VersionInfo version)
    {
        var profile = SystemProfile.CreateEmpty(version);
        profile.Notes.Add(DiagnosticsLocalization.T("hwDiagNoteAdvisoryOnly",
            "Hardware diagnostics are advisory only. MultiCamApp does not block presets based on this scan."));

        TryReadOperatingSystem(profile);
        TryReadCpu(profile);
        TryReadVideoControllers(profile);
        AddAdapterWarningsAndHints(profile);

        if (profile.TotalPhysicalMemoryBytes == null)
            profile.Warnings.Add(DiagnosticsLocalization.T("hwDiagWarnRamUnreadable", "RAM information could not be read."));
        if (profile.DisplayAdapters.Count == 0)
            profile.Warnings.Add(DiagnosticsLocalization.T("hwDiagWarnDisplayAdapterUnreadable", "Display adapter information could not be read."));

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
            profile.Warnings.Add(DiagnosticsLocalization.T("hwDiagWarnBasicDisplayAdapter",
                "Microsoft Basic Display Adapter detected. Install the official Intel, NVIDIA, or AMD graphics driver for best preview and recording reliability."));
        }

        if (profile.HasIntelDisplayAdapter)
            profile.EncoderHints.Add(DiagnosticsLocalization.T("hwDiagHintIntel", "Intel graphics detected. Official Intel graphics drivers may improve preview stability."));
        if (profile.HasNvidiaDisplayAdapter)
            profile.EncoderHints.Add(DiagnosticsLocalization.T("hwDiagHintNvidia", "NVIDIA graphics detected. Official NVIDIA drivers may improve display and encoding reliability."));
        if (profile.HasAmdDisplayAdapter)
            profile.EncoderHints.Add(DiagnosticsLocalization.T("hwDiagHintAmd", "AMD/Radeon graphics detected. Official AMD drivers may improve display and encoding reliability."));

        if (!profile.HasIntelDisplayAdapter && !profile.HasNvidiaDisplayAdapter && !profile.HasAmdDisplayAdapter)
            profile.EncoderHints.Add(DiagnosticsLocalization.T("hwDiagHintNoKnownAdapter", "No Intel/NVIDIA/AMD display adapter was identified. This may be normal on some systems."));
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
            profile.Warnings.Add(string.Format(DiagnosticsLocalization.T("hwDiagWarnOsRamWmiUnavailable", "OS/RAM WMI scan unavailable: {0}"), ex.GetType().Name));
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
            profile.Warnings.Add(string.Format(DiagnosticsLocalization.T("hwDiagWarnCpuWmiUnavailable", "CPU WMI scan unavailable: {0}"), ex.GetType().Name));
        }
    }

    private static void TryReadVideoControllers(SystemProfile profile)
    {
        try
        {
            // 2.1.0: Win32_VideoController has no DriverProviderName property; asking for it made WMI
            // reject the whole query ("Invalid query"), so no GPU was ever listed. The vendor is
            // AdapterCompatibility.
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                profile.DisplayAdapters.Add(new DisplayAdapterProfile
                {
                    Name = Convert.ToString(obj["Name"])?.Trim() ?? "Unknown",
                    DriverProvider = Convert.ToString(obj["AdapterCompatibility"])?.Trim() is { Length: > 0 } vendor ? vendor : "Unknown",
                    DriverVersion = Convert.ToString(obj["DriverVersion"])?.Trim() ?? "Unknown",
                    DriverDate = FormatCimDate(Convert.ToString(obj["DriverDate"])),
                    AdapterRam = FormatAdapterRam(obj["AdapterRAM"])
                });
            }
        }
        catch (Exception ex) when (IsSafeWmiFailure(ex))
        {
            profile.Warnings.Add(string.Format(DiagnosticsLocalization.T("hwDiagWarnDisplayWmiUnavailable", "Display adapter WMI scan unavailable: {0}"), ex.GetType().Name));
        }
    }

    /// <summary>CIM datetime ("20260617000000.000000-000") to yyyy-MM-dd; anything else unchanged.</summary>
    public static string FormatCimDate(string? cim)
    {
        if (string.IsNullOrWhiteSpace(cim)) return "Unknown";
        var t = cim.Trim();
        return t.Length >= 8 && t[..8].All(char.IsDigit)
            ? $"{t[..4]}-{t.Substring(4, 2)}-{t.Substring(6, 2)}"
            : t;
    }

    /// <summary>AdapterRAM is a 32-bit WMI value: it cannot report more than 4 GB.</summary>
    public static string FormatAdapterRam(object? value)
    {
        if (value is null || !ulong.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var bytes) || bytes == 0)
            return "Unknown";
        var gb = bytes / 1024d / 1024d / 1024d;
        var text = gb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        return bytes >= 0xFFF00000UL ? text + " (WMI reports at most 4 GB)" : text;
    }

    private static bool IsSafeWmiFailure(Exception ex) =>
        ex is ManagementException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or FileNotFoundException
            or TypeInitializationException;
}
