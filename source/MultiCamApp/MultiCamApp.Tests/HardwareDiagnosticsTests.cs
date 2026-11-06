using System.Text.Json;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Diagnostics;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class HardwareDiagnosticsTests
{
    [Fact]
    public void SystemProfile_serializes_core_fields()
    {
        var profile = new SystemProfile
        {
            AppVersion = "1.0.60",
            BuildNumber = 161,
            OsVersion = "Windows",
            CpuName = "CPU",
            DisplayAdapters =
            [
                new DisplayAdapterProfile
                {
                    Name = "Intel Graphics",
                    DriverProvider = "Intel",
                    DriverVersion = "1.2.3",
                    DriverDate = "2026-01-01"
                }
            ]
        };

        var json = JsonSerializer.Serialize(profile);

        Assert.Contains("1.0.60", json);
        Assert.Contains("Intel Graphics", json);
    }

    [Fact]
    public void Diagnostic_writers_create_latest_reports()
    {
        var stamp = DateTime.Now;
        var systemPaths = SystemProfileWriter.Write(new SystemProfile
        {
            AppVersion = "1.0.60",
            BuildNumber = 161,
            ScanTimeLocal = stamp
        });
        var cameraPaths = CameraCapabilityScanner.Write(new CameraCapabilityReport { ScanTimeLocal = stamp });
        var usbPaths = UsbTopologyScanner.Write(new UsbTopologyReport { ScanTimeLocal = stamp });

        Assert.True(File.Exists(systemPaths.LatestPath));
        Assert.True(File.Exists(cameraPaths.LatestPath));
        Assert.True(File.Exists(usbPaths.LatestPath));
    }

    [Fact]
    public void Microsoft_basic_display_adapter_adds_warning()
    {
        var profile = new SystemProfile
        {
            DisplayAdapters =
            [
                new DisplayAdapterProfile { Name = "Microsoft Basic Display Adapter" }
            ]
        };

        SystemCapabilityScanner.AddAdapterWarningsAndHints(profile);

        Assert.True(profile.HasMicrosoftBasicDisplayAdapter);
        Assert.Contains(profile.Warnings, w => w.Contains("Microsoft Basic Display Adapter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SystemCapabilityScanner_handles_missing_or_blocked_wmi_without_throwing()
    {
        var scanner = new SystemCapabilityScanner();
        var profile = scanner.Scan(new VersionInfo { Version = "1.0.60", Build = 161 });

        Assert.Equal("1.0.60", profile.AppVersion);
        Assert.True(profile.BuildNumber == 161);
    }

    [Fact]
    public void CameraCapabilityScanner_returns_unknown_without_crash()
    {
        var cameras = new[]
        {
            new CameraDevice
            {
                Id = @"\\?\USB#VID_0000&PID_0000#TEST",
                Name = "Generic USB Camera",
                DisplayName = "Generic USB Camera",
                Kind = CameraKind.ExternalUsb,
                EnumerationIndex = 0
            }
        };

        var report = new CameraCapabilityScanner().Scan(cameras);

        Assert.Single(report.Cameras);
        Assert.All(report.Cameras[0].Presets, p => Assert.Equal("Unknown", p.Result));
    }

    [Fact]
    public void UsbTopologyScanner_returns_unknown_without_crash()
    {
        var selected = new[]
        {
            new CameraDevice { Id = "cam1", DisplayName = "Camera 1", Kind = CameraKind.ExternalUsb },
            new CameraDevice { Id = "cam2", DisplayName = "Camera 2", Kind = CameraKind.ExternalUsb },
            new CameraDevice { Id = "cam3", DisplayName = "Camera 3", Kind = CameraKind.ExternalUsb }
        };

        var report = new UsbTopologyScanner().Scan(selected);

        Assert.Equal("Unknown", report.Status);
        Assert.Empty(report.Warnings);
        Assert.Contains(report.Notes, note => note.Contains("USB topology unavailable", StringComparison.OrdinalIgnoreCase));
    }

    // 2.1.0: GPU list was empty because the WMI query asked for a non-existent property.
    [Theory]
    [InlineData("20260617000000.000000-000", "2026-06-17")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    [InlineData("06/17/2026", "06/17/2026")]
    public void DisplayAdapter_DriverDate_IsReadable(string? cim, string expected)
        => Assert.Equal(expected, MultiCamApp.Diagnostics.SystemCapabilityScanner.FormatCimDate(cim));

    [Theory]
    [InlineData(2147479552u, "2 GB")]
    [InlineData(4293918720u, "4 GB (WMI reports at most 4 GB)")]
    [InlineData(0u, "Unknown")]
    public void DisplayAdapter_Memory_ShowsWmiLimit(uint bytes, string expected)
        => Assert.Equal(expected, MultiCamApp.Diagnostics.SystemCapabilityScanner.FormatAdapterRam(bytes));

    // 2.1.0: USB topology is resolved from the device tree (names only).
    [Theory]
    [InlineData(@"\\?\USB#VID_046D&PID_085C&MI_00#7&1a2b3c&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\GLOBAL", @"USB\VID_046D&PID_085C&MI_00\7&1a2b3c&0&0000")]
    [InlineData("cam1", null)]
    [InlineData("", null)]
    public void UsbTopology_CameraIdToInstanceId(string id, string? expected)
        => Assert.Equal(expected, UsbTopologyResolver.ToInstanceId(id));

    [Fact]
    public void UsbTopology_SharedControllerAndHub_AreReported()
    {
        var xhci = "Intel(R) USB 3.10 eXtensible Host Controller";
        var cams = new List<(string, UsbTopologyResolver.UsbPath?)>
        {
            ("JVU #1", new UsbTopologyResolver.UsbPath(xhci, ["Generic USB Hub"])),
            ("JVU #2", new UsbTopologyResolver.UsbPath(xhci, ["Generic USB Hub"])),
            ("C922", new UsbTopologyResolver.UsbPath(xhci, [])),
            ("Unknown cam", null),
        };
        var (notes, warnings) = UsbTopologyResolver.Summarize(cams, (_, fallback) => fallback);
        Assert.Contains(notes, n => n.StartsWith("3 cameras share one USB host controller") && n.Contains("C922"));
        Assert.Single(warnings);
        Assert.Contains("JVU #1, JVU #2", warnings[0]);
        Assert.DoesNotContain("C922", warnings[0]);
    }

    [Fact]
    public void UsbTopology_SingleCamera_NamesItsController()
    {
        var cams = new List<(string, UsbTopologyResolver.UsbPath?)> { ("C922", new UsbTopologyResolver.UsbPath("Controller 1", [])) };
        var (notes, warnings) = UsbTopologyResolver.Summarize(cams, (_, fallback) => fallback);
        Assert.Empty(warnings);
        Assert.Equal("C922 is connected to Controller 1.", Assert.Single(notes));
    }

    [Fact]
    public void UsbTopology_SeparateControllers_GiveNoWarning()
    {
        var cams = new List<(string, UsbTopologyResolver.UsbPath?)>
        {
            ("A", new UsbTopologyResolver.UsbPath("Controller 1", [])),
            ("B", new UsbTopologyResolver.UsbPath("Controller 2", [])),
        };
        var (notes, warnings) = UsbTopologyResolver.Summarize(cams, (_, fallback) => fallback);
        Assert.Empty(warnings);
        Assert.Equal("Each selected camera is on its own USB host controller.", Assert.Single(notes));
    }
}
