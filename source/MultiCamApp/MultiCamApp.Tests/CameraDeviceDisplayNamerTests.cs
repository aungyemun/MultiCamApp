using MultiCamApp.Capture;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class CameraDeviceDisplayNamerTests
{
    private static CameraDevice Device(string id, string name, int index, CameraKind kind = CameraKind.ExternalUsb) =>
        new()
        {
            Id = id,
            Name = name,
            DisplayName = name,
            Kind = kind,
            EnumerationIndex = index
        };

    [Fact]
    public void ApplyDisplayNames_DuplicateUsbNames_GetNumberedLabels()
    {
        var devices = new[]
        {
            Device("usb-a", "USB Live camera", 0),
            Device("usb-b", "USB Live camera", 1)
        };

        var named = CameraDeviceDisplayNamer.ApplyDisplayNames(devices);

        Assert.Equal(2, named.Count);
        Assert.Equal("USB Live camera #1", named[0].DisplayName);
        Assert.Equal("USB Live camera #2", named[1].DisplayName);
        Assert.Equal("usb-a", named[0].Id);
        Assert.Equal("usb-b", named[1].Id);
    }

    [Fact]
    public void ApplyDisplayNames_DuplicateJ5Webcams_GetNumberedLabels()
    {
        var devices = new[]
        {
            Device("j5-a", "j5 Webcam JVU250", 0),
            Device("j5-b", "j5 Webcam JVU250", 1)
        };

        var named = CameraDeviceDisplayNamer.ApplyDisplayNames(devices);

        Assert.Equal("j5 Webcam JVU250 #1", named[0].DisplayName);
        Assert.Equal("j5 Webcam JVU250 #2", named[1].DisplayName);
    }

    [Fact]
    public void ApplyDisplayNames_UniqueUsbName_KeepsPlainName()
    {
        var devices = new[] { Device("only", "USB Camera", 0) };

        var named = CameraDeviceDisplayNamer.ApplyDisplayNames(devices);

        Assert.Single(named);
        Assert.Equal("USB Camera", named[0].DisplayName);
    }

    [Fact]
    public void ApplyDisplayNames_SingleBuiltIn_AddsBuiltInHint()
    {
        var devices = new[] { Device("builtin", "Integrated Camera", 0, CameraKind.BuiltInFront) };

        var named = CameraDeviceDisplayNamer.ApplyDisplayNames(devices);

        Assert.Equal("Integrated Camera (Built-in front)", named[0].DisplayName);
    }

    [Fact]
    public void ApplyDisplayNames_DuplicateBuiltInNames_UseNumbersOnly()
    {
        var devices = new[]
        {
            Device("b1", "Integrated Camera", 0, CameraKind.BuiltInFront),
            Device("b2", "Integrated Camera", 1, CameraKind.BuiltInFront)
        };

        var named = CameraDeviceDisplayNamer.ApplyDisplayNames(devices);

        Assert.Equal("Integrated Camera #1", named[0].DisplayName);
        Assert.Equal("Integrated Camera #2", named[1].DisplayName);
    }
}
