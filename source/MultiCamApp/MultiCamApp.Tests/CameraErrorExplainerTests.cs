using MultiCamApp.Utils;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>Per-slot camera error messages (STABLE_CORE_V2 exception #7).</summary>
public sealed class CameraErrorExplainerTests
{
    [Fact]
    public void UsbBandwidth_IsRecognizedFromMessageHResult()
    {
        // Exact text observed on real hardware (two JVU250 cameras on one USB 2.0 hub, 720p30).
        var ex = new InvalidOperationException("MediaCapture failed (0x800718CF): A camera interface doesn't have the desired bandwidth for data transfer.");
        Assert.Equal(CameraErrorExplainer.Kind.UsbBandwidth, CameraErrorExplainer.Classify(ex));
        var text = CameraErrorExplainer.Describe(ex, 1, "1280×720", null);
        Assert.StartsWith("cam2:", text);
        Assert.Contains("1280×720", text);
        Assert.Contains("USB", text);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070005), CameraErrorExplainer.Kind.AccessDenied)]
    [InlineData(unchecked((int)0xC00D3704), CameraErrorExplainer.Kind.InUseByOtherApp)]
    [InlineData(unchecked((int)0xC00D3EA2), CameraErrorExplainer.Kind.DeviceLost)]
    [InlineData(unchecked((int)0x80004005), CameraErrorExplainer.Kind.Other)]
    public void HResult_IsClassified(int hr, CameraErrorExplainer.Kind expected)
    {
        var ex = new System.Runtime.InteropServices.COMException("x", hr);
        Assert.Equal(expected, CameraErrorExplainer.Classify(ex));
    }

    [Fact]
    public void GenericMessage_IsSanitizedAndShort()
    {
        var ex = new Exception(@"failed opening C:\Users\someone\cam.bin " + new string('x', 400));
        var text = CameraErrorExplainer.Describe(ex, 0, null, null);
        Assert.DoesNotContain(@"C:\Users", text);
        Assert.True(text.Length < 260);
    }
}
