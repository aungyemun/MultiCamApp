namespace MultiCamApp.Capture;

/// <summary>Matches WinRT camera device IDs to DirectShow device paths (stable for duplicate USB names).</summary>
public static class DirectShowDeviceMatcher
{
    public static bool TryMatch(
        string winRtDeviceId,
        DirectShowVideoDeviceEnumerator.DirectShowVideoDevice dshow,
        out int score)
    {
        score = 0;
        if (string.IsNullOrWhiteSpace(winRtDeviceId) || string.IsNullOrWhiteSpace(dshow.MatchKey))
            return false;

        var winKey = BuildMatchKey(winRtDeviceId);
        if (string.IsNullOrEmpty(winKey))
            return false;

        if (string.Equals(winKey, dshow.MatchKey, StringComparison.OrdinalIgnoreCase))
        {
            score = 100;
            return true;
        }

        if (winKey.Length >= 24
            && dshow.MatchKey.Contains(winKey, StringComparison.OrdinalIgnoreCase))
        {
            score = 90;
            return true;
        }

        if (dshow.MatchKey.Length >= 24
            && winKey.Contains(dshow.MatchKey, StringComparison.OrdinalIgnoreCase))
        {
            score = 85;
            return true;
        }

        var winUsb = ExtractUsbSegment(winRtDeviceId);
        var dshowUsb = ExtractUsbSegment(dshow.DevicePath);
        if (!string.IsNullOrEmpty(winUsb) && string.Equals(winUsb, dshowUsb, StringComparison.OrdinalIgnoreCase))
        {
            score = 70;
            return true;
        }

        return false;
    }

  public static string BuildMatchKey(string deviceIdOrPath)
    {
        if (string.IsNullOrWhiteSpace(deviceIdOrPath)) return "";

        var s = deviceIdOrPath.Trim();
        var usb = s.IndexOf("USB#", StringComparison.OrdinalIgnoreCase);
        if (usb < 0)
            return NormalizeKey(s);

        s = s[usb..];
        var brace = s.IndexOf("#{", StringComparison.Ordinal);
        if (brace > 0)
            s = s[..brace];

        return NormalizeKey(s);
    }

    public static string BuildOpenCvCaptureUri(string devicePath)
    {
        var path = devicePath.Trim();
        if (string.IsNullOrEmpty(path)) return "";
        return "@device:pnp:" + path.ToLowerInvariant();
    }

    /// <summary>Build OpenCV PnP capture URI from a WinRT MediaFoundation device id when DirectShow COM enum is unavailable.</summary>
    public static string? TryBuildOpenCvCaptureUriFromWinRtDeviceId(string? winRtDeviceId)
    {
        if (string.IsNullOrWhiteSpace(winRtDeviceId)) return null;
        if (winRtDeviceId.IndexOf("USB#", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var path = winRtDeviceId.Trim();
        var globalIdx = path.LastIndexOf(@"\GLOBAL", StringComparison.OrdinalIgnoreCase);
        if (globalIdx > 0)
            path = path[..globalIdx];

        var brace = path.IndexOf("#{", StringComparison.Ordinal);
        if (brace > 0)
            path = path[..brace];

        path = path.TrimEnd('\\');
        return string.IsNullOrEmpty(path) ? null : BuildOpenCvCaptureUri(path);
    }

    private static string ExtractUsbSegment(string id)
    {
        var key = BuildMatchKey(id);
        return key.StartsWith("usb#", StringComparison.OrdinalIgnoreCase) ? key : "";
    }

    private static string NormalizeKey(string s) =>
        s.Trim().TrimEnd('\\').ToLowerInvariant();
}
