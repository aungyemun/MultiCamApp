using System.Globalization;
using System.Text.RegularExpressions;
using MultiCamApp.Localization;

namespace MultiCamApp.Utils;

/// <summary>
/// Turns camera/Media Foundation failures into a short, actionable, localized message for the
/// camera slot that failed. Raw exception text stays in the logs; the user sees what to do next.
/// </summary>
public static partial class CameraErrorExplainer
{
    public enum Kind { UsbBandwidth, InUseByOtherApp, AccessDenied, DeviceLost, Other }

    // HRESULTs observed from MediaCapture / MediaFrameReader on Windows 10/11.
    private const uint HrUsbBandwidth   = 0x800718CF; // camera interface doesn't have the desired bandwidth
    private const uint HrHwMftStart     = 0xC00D3704; // MF_E_HW_MFT_FAILED_START_STREAMING (device busy)
    private const uint HrInUse          = 0x800700AA; // ERROR_BUSY
    private const uint HrSharingViol    = 0x80070020; // ERROR_SHARING_VIOLATION
    private const uint HrAccessDenied   = 0x80070005; // E_ACCESSDENIED (privacy settings)
    private const uint HrDeviceNotFound = 0x80070490; // ERROR_NOT_FOUND
    private const uint HrDeviceGone     = 0x8007048F; // ERROR_DEVICE_NOT_CONNECTED
    private const uint HrVideoDeviceLost= 0xC00D3EA2; // MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED
    private const uint HrDeviceRemoved  = 0x80070651; // ERROR_DEVICE_REMOVED

    public static Kind Classify(Exception ex)
    {
        foreach (var hr in CandidateHResults(ex))
        {
            switch (hr)
            {
                case HrUsbBandwidth: return Kind.UsbBandwidth;
                case HrHwMftStart or HrInUse or HrSharingViol: return Kind.InUseByOtherApp;
                case HrAccessDenied: return Kind.AccessDenied;
                case HrDeviceNotFound or HrDeviceGone or HrVideoDeviceLost or HrDeviceRemoved: return Kind.DeviceLost;
            }
        }
        if (ex is UnauthorizedAccessException) return Kind.AccessDenied;
        return Kind.Other;
    }

    /// <summary>One-line message for the camera panel header and status area.</summary>
    public static string Describe(Exception ex, int slotIndex, string? resolution, LanguageManager? language)
    {
        var slot = $"cam{slotIndex + 1}";
        string T(string key, string fallback) =>
            language?[key] is { Length: > 0 } v && v != key ? v : fallback;
        var res = string.IsNullOrWhiteSpace(resolution) ? "" : resolution;
        return Classify(ex) switch
        {
            Kind.UsbBandwidth => string.Format(CultureInfo.InvariantCulture,
                T("camErrUsbBandwidth", "{0}: not enough USB bandwidth for {1}. Choose a lower resolution (e.g. 480p) or connect each camera to a different USB port / controller (not the same hub)."),
                slot, res),
            Kind.InUseByOtherApp => string.Format(CultureInfo.InvariantCulture,
                T("camErrInUse", "{0}: the camera is busy or used by another app. Close other camera apps and start preview again."), slot),
            Kind.AccessDenied => string.Format(CultureInfo.InvariantCulture,
                T("camErrAccessDenied", "{0}: camera access was denied. Allow desktop apps to use the camera in Windows Settings > Privacy & security > Camera."), slot),
            Kind.DeviceLost => string.Format(CultureInfo.InvariantCulture,
                T("camErrDeviceLost", "{0}: the camera was disconnected or stopped responding. Check the USB cable, then start preview again."), slot),
            _ => string.Format(CultureInfo.InvariantCulture,
                T("camErrGeneric", "{0}: camera error — {1}"), slot, PrivacySanitizer.SanitizeForOutput(ShortMessage(ex))),
        };
    }

    private static IEnumerable<uint> CandidateHResults(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.HResult != 0) yield return unchecked((uint)e.HResult);
            foreach (Match m in HexHResultRegex().Matches(e.Message ?? ""))
                if (uint.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                    yield return parsed;
        }
    }

    private static string ShortMessage(Exception ex)
    {
        var msg = (ex.Message ?? ex.GetType().Name).Trim();
        var nl = msg.IndexOfAny(['\r', '\n']);
        if (nl > 0) msg = msg[..nl];
        return msg.Length > 160 ? msg[..160] + "…" : msg;
    }

    [GeneratedRegex(@"0x([0-9A-Fa-f]{8})")]
    private static partial Regex HexHResultRegex();
}
