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
}
