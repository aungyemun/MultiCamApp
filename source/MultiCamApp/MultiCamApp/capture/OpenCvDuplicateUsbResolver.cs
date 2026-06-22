using OpenCvSharp;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>
/// Verifies selected cameras open on distinct DirectShow devices when duplicate USB models are in the layout.
/// Never probes DirectShow indices outside the selected layout catalog mapping.
/// </summary>
public static class OpenCvDuplicateUsbResolver
{
    private static readonly LogService Log = new();
    private static IReadOnlyList<string?> _lastSelectedDeviceIds = [];

    public static void BindSelectedDevices(
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds)
    {
        _lastSelectedDeviceIds = selectedDeviceIds;
        if (!DuplicateUsbCapturePolicy.HasDuplicateUsbInSelection(devices, selectedDeviceIds))
            return;

        if (PreviewStartTrace.IsActive)
            PreviewStartTrace.NotifyProbe("OpenCvDuplicateUsbResolver.BindSelectedDevices", "brief VideoCapture probe for duplicate USB models (selected devices only)");

        var selected = selectedDeviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => devices.FirstOrDefault(d => d.Id == id))
            .Where(d => d != null)
            .Cast<CameraDevice>()
            .OrderBy(d => d.EnumerationIndex)
            .ToList();

        if (selected.Count == 0) return;

        var claimedIndices = new HashSet<int>();

        foreach (var device in selected.OrderByDescending(d => d.EnumerationIndex))
        {
            if (!OpenCvDirectShowIndexCatalog.TryGetBinding(device.Id, out var binding))
            {
                TryProbeBindByIndex(device, devices, claimedIndices, selectedDeviceIds);
                continue;
            }

            if (binding.Index >= 0 && claimedIndices.Contains(binding.Index))
            {
                TryProbeBindByIndex(device, devices, claimedIndices, selectedDeviceIds);
                continue;
            }

            if (binding.Index >= 0 && OpenCvDeviceSession.IsIndexTaken(binding.Index))
            {
                claimedIndices.Add(binding.Index);
                Log.Info("camera", $"Probe skip {device.DisplayName}: DirectShow index {binding.Index} already open");
                continue;
            }

            if (binding.Index >= 0 && !claimedIndices.Contains(binding.Index))
            {
                if (!string.IsNullOrWhiteSpace(binding.DirectShowOpenUri))
                {
                    claimedIndices.Add(binding.Index);
                    Log.Info("camera", $"Probe preserved exact PnP binding {device.DisplayName} -> {binding.DirectShowOpenUri}");
                    continue;
                }

                if (ShouldUseExactDeviceFallbackWithoutProbe(device))
                {
                    OpenCvDirectShowIndexCatalog.InvalidateDevice(device.Id);
                    Log.Info("camera", $"Probe skipped for {device.DisplayName}; using exact-device fallback");
                    continue;
                }

                if (TryBriefOpen(binding, out var pw, out var ph))
                {
                    claimedIndices.Add(binding.Index);
                    OpenCvDirectShowIndexCatalog.SetProbeOverride(device.Id, binding.Index);
                    Log.Info("camera", $"Probe verified {device.DisplayName} -> index {binding.Index} ({pw}x{ph})");
                    Thread.Sleep(100);
                    continue;
                }

                Log.Info("camera", $"Probe verify failed for {device.DisplayName} index {binding.Index}; trying selected alternates only");
                TryProbeBindByIndex(device, devices, claimedIndices, selectedDeviceIds);
                continue;
            }

            TryProbeBindByIndex(device, devices, claimedIndices, selectedDeviceIds);
        }
    }

    private static void TryProbeBindByIndex(
        CameraDevice device,
        IReadOnlyList<CameraDevice> devices,
        HashSet<int> claimedIndices,
        IReadOnlyList<string?> selectedDeviceIds)
    {
        if (ShouldUseExactDeviceFallbackWithoutProbe(device))
        {
            OpenCvDirectShowIndexCatalog.InvalidateDevice(device.Id);
            Log.Info("camera", $"Probe bind skipped for {device.DisplayName}; using exact-device fallback");
            return;
        }

        var failed = OpenCvDirectShowIndexCatalog.TryGetIndex(device.Id, out var start) ? start : -1;
        var reserved = OpenCvDirectShowIndexCatalog.GetIndicesReservedForOtherDevices(
            device.Id, selectedDeviceIds);
        foreach (var index in GetAlternateIndices(device, devices, failed, selectedDeviceIds))
        {
            if (reserved.Contains(index)) continue;
            if (claimedIndices.Contains(index) || OpenCvDeviceSession.IsIndexTaken(index)) continue;
            var candidate = new OpenCvDeviceBinding { Index = index };
            if (!TryBriefOpen(candidate, out var pw, out var ph)) continue;

            claimedIndices.Add(index);
            OpenCvDirectShowIndexCatalog.SetProbeOverride(device.Id, index);
            Log.Info("camera", $"Probe bind {device.DisplayName} -> index {index} ({pw}x{ph}) [selected catalog]");
            return;
        }

        OpenCvDirectShowIndexCatalog.InvalidateDevice(device.Id);
        Log.Info("camera", $"Probe bind failed for {device.DisplayName}; OpenCV binding invalidated for exact-device fallback");
    }

    public static IReadOnlyList<int> GetAlternateIndices(
        CameraDevice device,
        IReadOnlyList<CameraDevice> devices,
        int failedIndex,
        IReadOnlyList<string?>? selectedDeviceIds = null)
    {
        selectedDeviceIds ??= _lastSelectedDeviceIds;
        var allowed = OpenCvDirectShowIndexCatalog.GetCatalogIndicesForSelectedDevices(selectedDeviceIds);
        var list = new List<int>();

        if (OpenCvDirectShowIndexCatalog.TryGetIndex(device.Id, out var cat) && cat != failedIndex && allowed.Contains(cat))
            list.Add(cat);

        foreach (var index in allowed.OrderBy(i => i))
        {
            if (index == failedIndex || list.Contains(index)) continue;
            list.Add(index);
        }

        return list;
    }

    public static bool TryBriefOpenIndex(int index, out int width, out int height) =>
        TryBriefOpen(new OpenCvDeviceBinding { Index = index }, out width, out height);

    public static bool TryBriefOpen(OpenCvDeviceBinding binding, out int width, out int height)
    {
        width = 0;
        height = 0;
        VideoCapture? cap = null;
        try
        {
            cap = CreateCapture(binding);
            if (cap == null || !cap.IsOpened()) return false;
            cap.Set(VideoCaptureProperties.BufferSize, 1);
            using var frame = new Mat();
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (cap.Read(frame) && !frame.Empty())
                {
                    width = frame.Width;
                    height = frame.Height;
                    return width > 0 && height > 0;
                }

                Thread.Sleep(40);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Info("camera", $"Brief probe: {ex.Message}");
            return false;
        }
        finally
        {
            cap?.Dispose();
        }
    }

    private static bool ShouldUseExactDeviceFallbackWithoutProbe(CameraDevice device) => false;

    internal static VideoCapture? CreateCapture(OpenCvDeviceBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.DirectShowOpenUri))
        {
            var pnp = new VideoCapture(binding.DirectShowOpenUri, VideoCaptureAPIs.DSHOW);
            if (pnp.IsOpened()) return pnp;
            pnp.Dispose();
        }

        if (binding.Index >= 0)
        {
            var cap = new VideoCapture(binding.Index, VideoCaptureAPIs.DSHOW);
            if (cap.IsOpened()) return cap;
            cap.Dispose();
            return null;
        }

        if (!string.IsNullOrWhiteSpace(binding.DirectShowName))
            return new VideoCapture($"video={binding.DirectShowName}", VideoCaptureAPIs.DSHOW);
        return null;
    }
}
