namespace MultiCamApp.Capture;

/// <summary>Capture policy when multiple identical USB webcams are in use (OpenCV index ambiguity).</summary>
public static class DuplicateUsbCapturePolicy
{
    public static bool HasDuplicateUsbModels(IReadOnlyList<CameraDevice> devices) =>
        devices
            .Where(d => d.Kind == CameraKind.ExternalUsb)
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);

    public static bool HasDuplicateUsbInSelection(
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds)
    {
        var selected = SelectedDevices(devices, selectedDeviceIds)
            .Where(d => d.Kind == CameraKind.ExternalUsb)
            .ToList();
        return selected
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);
    }

    /// <summary>
    /// Backend preference for duplicate USB models.
    /// New policy: OpenCV is preferred; WinRT is used only as a per-device fallback
    /// when OpenCV cannot provide a usable stream. So this hint is now disabled.
    /// </summary>
    public static bool PreferWinRtForDevice(
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds,
        string deviceId) => false;

    /// <summary>Lower sorts first: WinRT duplicate-USB cameras before OpenCV, higher USB enum before lower.</summary>
    public static int GetOpenSortKey(
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds,
        string deviceId)
    {
        var device = devices.FirstOrDefault(d => d.Id == deviceId);
        var enumIndex = device?.EnumerationIndex ?? 0;
        // When duplicate USB models are selected, open higher enumeration indices first
        // (tends to favour newer/secondary devices) but do not bias WinRT vs OpenCV.
        if (!HasDuplicateUsbInSelection(devices, selectedDeviceIds))
            return enumIndex;
        return -enumIndex;
    }

    public static int CountActiveSlots(IReadOnlyList<string?> selectedDeviceIds) =>
        selectedDeviceIds.Count(id => !string.IsNullOrWhiteSpace(id));

    /// <summary>
    /// WinRT is only used as a per-device fallback after OpenCV probe fails or reports a bad stream.
    /// For 3+ camera layouts, keep every slot on OpenCV — mixed WinRT/OpenCV preview starves late USB slots.
    /// </summary>
    public static bool ShouldFallbackToWinRtAfterOpenCvProbe(
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds,
        CameraDevice device,
        int openCvIndex,
        int probedWidth,
        int probedHeight,
        int preferredCaptureWidth)
    {
        // v1.0.38 stable 1/2-camera baseline used the OpenCV preview path. Switching a
        // successfully-opened duplicate USB camera to WinRT can change color/rendering
        // behavior and reintroduce mixed-backend lifecycle races. Keep successful
        // OpenCV streams on OpenCV; only a true OpenCV open failure is handled by the
        // slot-level failure path.
        return false;
    }

    private static IEnumerable<CameraDevice> SelectedDevices(
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds) =>
        selectedDeviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => devices.FirstOrDefault(d => d.Id == id))
            .Where(d => d != null)
            .Cast<CameraDevice>();
}
