////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>Maps WinRT device IDs to OpenCV DirectShow capture targets (path / index).</summary>
public static class OpenCvDeviceMapper
{
    private static readonly LogService Log = new();

    public static OpenCvDeviceBinding Resolve(string deviceId, IReadOnlyList<CameraDevice> devices)
    {
        var device = devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null)
        {
            Log.Info("camera", "OpenCV map: unknown device id");
            return new OpenCvDeviceBinding { Index = -1 };
        }

        if (!OpenCvDirectShowIndexCatalog.TryGetBinding(deviceId, out var binding) || !binding.HasCaptureTarget)
        {
            Log.Info("camera", $"OpenCV map: no catalog binding for {device.DisplayName}");
            // write detailed mapping debug log
            try
            {
                var filename = $"device_mapping_debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                DeviceMappingDebugLogger.WriteMappingLog(filename, () =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"SelectedDevice: id={deviceId} name={device.DisplayName}");
                    sb.AppendLine("Catalog entries:");
                    var dshow = DirectShowVideoDeviceEnumerator.Enumerate();
                    foreach (var ds in dshow)
                        sb.AppendLine($"  [{ds.OpenCvIndex}] name={ds.FriendlyName} path={ds.DevicePath}");
                    return sb.ToString();
                });
            }
            catch { }

            return new OpenCvDeviceBinding { Index = -1 };
        }

        if (OpenCvDeviceSession.IsBindingTaken(binding))
        {
            Log.Info("camera", $"OpenCV map: device already in use ({device.DisplayName})");
            return new OpenCvDeviceBinding { Index = -1 };
        }

        var label = !string.IsNullOrWhiteSpace(binding.DirectShowOpenUri) ? "PnP device"
            : binding.Index >= 0 ? $"index {binding.Index}"
            : binding.DirectShowName ?? "?";
        Log.Info("camera", $"OpenCV map {label} (enumeration {device.EnumerationIndex}) — {device.DisplayName}");
        OpenCvDeviceSession.RememberDevice(deviceId, binding);
        return binding;
    }

    public static OpenCvDeviceBinding ResolveAfterInvalidatingStale(
        string deviceId,
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?>? selectedDeviceIds = null)
    {
        var device = devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null)
            return new OpenCvDeviceBinding { Index = -1 };

        OpenCvDeviceSession.ForgetDevice(deviceId);
        OpenCvDirectShowIndexCatalog.InvalidateDevice(deviceId);
        OpenCvDirectShowIndexCatalog.RefreshSingleDeviceBinding(device, selectedDeviceIds);
        return Resolve(deviceId, devices);
    }

    public static OpenCvDeviceBinding? TryResolveAlternate(
        string deviceId,
        IReadOnlyList<CameraDevice> devices,
        int failedIndex,
        IReadOnlyList<string?>? layoutSelectedDeviceIds = null)
    {
        var device = devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return null;

        var reserved = OpenCvDirectShowIndexCatalog.GetIndicesReservedForOtherDevices(
            deviceId, layoutSelectedDeviceIds ?? []);

        foreach (var index in OpenCvDuplicateUsbResolver.GetAlternateIndices(
            device, devices, failedIndex, layoutSelectedDeviceIds))
        {
            if (reserved.Contains(index))
            {
                Log.Info("camera",
                    $"OpenCV alternate skip index {index} for {device.DisplayName}: reserved for another selected camera");
                continue;
            }

            var candidate = new OpenCvDeviceBinding { Index = index };
            if (OpenCvDeviceSession.IsBindingTaken(candidate)) continue;
            if (!OpenCvDuplicateUsbResolver.TryBriefOpen(candidate, out _, out _)) continue;

            OpenCvDirectShowIndexCatalog.SetProbeOverride(deviceId, index);
            Log.Info("camera", $"OpenCV map alternate index {index} for {device.DisplayName} (failed {failedIndex})");
            var binding = new OpenCvDeviceBinding { Index = index };
            OpenCvDeviceSession.RememberDevice(deviceId, binding);
            return binding;
        }

        return null;
    }

    public static void ClearCache()
    {
        OpenCvDeviceSession.Reset();
        OpenCvDirectShowIndexCatalog.ClearProbeOverrides();
    }
}
