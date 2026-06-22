using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>
/// Maps WinRT device IDs to DirectShow indices and PnP capture URIs (matched by device path, not list order).
/// </summary>
public static class OpenCvDirectShowIndexCatalog
{
    private static readonly LogService Log = new();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> DeviceToIndex = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> ProbeOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CatalogEntry> DeviceEntries = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CatalogEntry
    {
        public int Index { get; init; }
        public string? DirectShowName { get; init; }
        public string? DirectShowOpenUri { get; init; }
        public string? DevicePath { get; init; }
        public bool PathMatched { get; init; }

        public OpenCvDeviceBinding ToBinding(int indexOverride = -1)
        {
            var idx = indexOverride >= 0 ? indexOverride : Index;
            return new OpenCvDeviceBinding
            {
                Index = idx,
                DirectShowName = DirectShowName,
                DirectShowOpenUri = DirectShowOpenUri
            };
        }
    }

    public static void Rebuild(IReadOnlyList<CameraDevice> devices) => RebuildCore(devices);

    /// <summary>Maps only selected layout devices (preview start). Full list still used for device lookup.</summary>
    public static void RebuildForSelected(IReadOnlyList<CameraDevice> allDevices, IReadOnlyList<string?> selectedDeviceIds)
    {
        var selected = selectedDeviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => allDevices.FirstOrDefault(d => d.Id == id))
            .Where(d => d != null)
            .Cast<CameraDevice>()
            .ToList();
        RebuildCore(selected);
    }

    private static void RebuildCore(IReadOnlyList<CameraDevice> devices)
    {
        lock (Gate)
        {
            DeviceToIndex.Clear();
            ProbeOverrides.Clear();
            DeviceEntries.Clear();
            if (devices.Count == 0) return;

            var dshow = DirectShowVideoDeviceEnumerator.Enumerate();
            var claimedDshow = new HashSet<int>();

            foreach (var device in devices.OrderBy(d => d.EnumerationIndex))
            {
                DirectShowVideoDeviceEnumerator.DirectShowVideoDevice? best = null;
                var bestScore = 0;
                foreach (var ds in dshow)
                {
                    if (claimedDshow.Contains(ds.OpenCvIndex)) continue;
                    if (!DirectShowDeviceMatcher.TryMatch(device.Id, ds, out var score)) continue;
                    if (score <= bestScore) continue;
                    best = ds;
                    bestScore = score;
                }

                if (best != null)
                {
                    claimedDshow.Add(best.OpenCvIndex);
                    var entry = new CatalogEntry
                    {
                        Index = best.OpenCvIndex,
                        DirectShowName = best.FriendlyName,
                        DevicePath = best.DevicePath,
                        DirectShowOpenUri = DirectShowDeviceMatcher.BuildOpenCvCaptureUri(best.DevicePath),
                        PathMatched = true
                    };
                    DeviceEntries[device.Id] = entry;
                    DeviceToIndex[device.Id] = best.OpenCvIndex;
                    Log.Info("camera",
                        $"DirectShow map [{best.OpenCvIndex}] {device.DisplayName} <- {best.FriendlyName} (path)");
                    continue;
                }

                if (dshow.Count == 0)
                {
                    var enumIndex = device.EnumerationIndex;
                    var pnpUri = DirectShowDeviceMatcher.TryBuildOpenCvCaptureUriFromWinRtDeviceId(device.Id);
                    var entry = new CatalogEntry
                    {
                        Index = enumIndex,
                        DirectShowOpenUri = pnpUri,
                        PathMatched = !string.IsNullOrEmpty(pnpUri)
                    };
                    DeviceEntries[device.Id] = entry;
                    DeviceToIndex[device.Id] = enumIndex;
                    Log.Info("camera",
                        string.IsNullOrEmpty(pnpUri)
                            ? $"DirectShow map [{enumIndex}] {device.DisplayName} (MediaDevice index; DShow enum unavailable)"
                            : $"DirectShow map [{enumIndex}] {device.DisplayName} (PnP path; DShow enum unavailable)");
                    continue;
                }

                var fallback = device.EnumerationIndex;
                if (fallback >= dshow.Count)
                    fallback = dshow[^1].OpenCvIndex;

                var fb = dshow.FirstOrDefault(d => d.OpenCvIndex == fallback) ?? dshow[0];
                var fbEntry = new CatalogEntry
                {
                    Index = fb.OpenCvIndex,
                    DirectShowName = fb.FriendlyName,
                    DevicePath = fb.DevicePath,
                    DirectShowOpenUri = DirectShowDeviceMatcher.BuildOpenCvCaptureUri(fb.DevicePath),
                    PathMatched = false
                };
                DeviceEntries[device.Id] = fbEntry;
                DeviceToIndex[device.Id] = fb.OpenCvIndex;
                Log.Info("camera",
                    $"DirectShow map [{fb.OpenCvIndex}] {device.DisplayName} (enumeration fallback)");
            }

            Log.Info("camera",
                $"DirectShow catalog: {DeviceToIndex.Count} device(s) on indices [{string.Join(", ", DeviceToIndex.Values.Distinct().OrderBy(x => x))}]");
            // expose snapshot for diagnostics
            _lastSnapshot = DeviceEntries.Values
                .Select(e => (Index: e.Index, Name: e.DirectShowName ?? "", Uri: e.DirectShowOpenUri ?? "", PathMatched: e.PathMatched))
                .ToList();
        }
    }

    private static List<(int Index, string Name, string Uri, bool PathMatched)>? _lastSnapshot;

    public static List<(int Index, string Name, string Uri, bool PathMatched)> GetSnapshot()
    {
        lock (Gate)
        {
            return _lastSnapshot != null ? new List<(int, string, string, bool)>(_lastSnapshot) : new List<(int, string, string, bool)>();
        }
    }

    public static void ClearProbeOverrides()
    {
        lock (Gate)
            ProbeOverrides.Clear();
    }

    public static void SetProbeOverride(string deviceId, int index)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || index < 0) return;
        lock (Gate)
            ProbeOverrides[deviceId] = index;
    }

    public static bool TryGetIndex(string deviceId, out int index)
    {
        lock (Gate)
        {
            if (ProbeOverrides.TryGetValue(deviceId, out index))
                return true;
            return DeviceToIndex.TryGetValue(deviceId, out index);
        }
    }

    public static bool TryGetBinding(string deviceId, out OpenCvDeviceBinding binding)
    {
        lock (Gate)
        {
            if (!DeviceEntries.TryGetValue(deviceId, out var entry))
            {
                binding = default;
                return false;
            }

            var index = ProbeOverrides.TryGetValue(deviceId, out var over) ? over : entry.Index;
            binding = entry.ToBinding(index);
            return true;
        }
    }

    public static void InvalidateDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return;

        lock (Gate)
        {
            DeviceToIndex.Remove(deviceId);
            ProbeOverrides.Remove(deviceId);
            DeviceEntries.Remove(deviceId);
        }
    }

    public static bool RefreshSingleDeviceBinding(CameraDevice device, IReadOnlyList<string?>? selectedDeviceIds = null)
    {
        if (device == null || string.IsNullOrWhiteSpace(device.Id))
            return false;

        lock (Gate)
        {
            ProbeOverrides.Remove(device.Id);
            DeviceToIndex.Remove(device.Id);
            DeviceEntries.Remove(device.Id);

            var dshow = DirectShowVideoDeviceEnumerator.Enumerate();
            var reserved = GetIndicesReservedForOtherDevices(device.Id, selectedDeviceIds ?? []);

            DirectShowVideoDeviceEnumerator.DirectShowVideoDevice? best = null;
            var bestScore = 0;
            foreach (var ds in dshow)
            {
                if (reserved.Contains(ds.OpenCvIndex)) continue;
                if (!DirectShowDeviceMatcher.TryMatch(device.Id, ds, out var score)) continue;
                if (score <= bestScore) continue;
                best = ds;
                bestScore = score;
            }

            if (best != null)
            {
                var entry = new CatalogEntry
                {
                    Index = best.OpenCvIndex,
                    DirectShowName = best.FriendlyName,
                    DevicePath = best.DevicePath,
                    DirectShowOpenUri = DirectShowDeviceMatcher.BuildOpenCvCaptureUri(best.DevicePath),
                    PathMatched = true
                };
                DeviceEntries[device.Id] = entry;
                DeviceToIndex[device.Id] = best.OpenCvIndex;
                return true;
            }

            if (dshow.Count == 0)
            {
                var enumIndex = device.EnumerationIndex;
                if (reserved.Contains(enumIndex))
                    return false;
                var pnpUri = DirectShowDeviceMatcher.TryBuildOpenCvCaptureUriFromWinRtDeviceId(device.Id);
                var entry = new CatalogEntry
                {
                    Index = enumIndex,
                    DirectShowOpenUri = pnpUri,
                    PathMatched = !string.IsNullOrEmpty(pnpUri)
                };
                DeviceEntries[device.Id] = entry;
                DeviceToIndex[device.Id] = enumIndex;
                return true;
            }

            var fb = dshow.FirstOrDefault(ds => !reserved.Contains(ds.OpenCvIndex));
            if (fb == null) return false;

            var fbEntry = new CatalogEntry
            {
                Index = fb.OpenCvIndex,
                DirectShowName = fb.FriendlyName,
                DevicePath = fb.DevicePath,
                DirectShowOpenUri = DirectShowDeviceMatcher.BuildOpenCvCaptureUri(fb.DevicePath),
                PathMatched = false
            };
            DeviceEntries[device.Id] = fbEntry;
            DeviceToIndex[device.Id] = fb.OpenCvIndex;
            return true;
        }
    }

    /// <summary>
    /// DirectShow indices already assigned in the catalog to other cameras in the active layout.
    /// Prevents alternate-index fallback from opening the wrong physical device (e.g. stealing index 0 from j5 #1).
    /// </summary>
    public static HashSet<int> GetIndicesReservedForOtherDevices(
        string currentDeviceId,
        IReadOnlyList<string?> selectedDeviceIds)
    {
        var reserved = new HashSet<int>();
        if (selectedDeviceIds == null || selectedDeviceIds.Count == 0)
            return reserved;

        lock (Gate)
        {
            foreach (var id in selectedDeviceIds)
            {
                if (string.IsNullOrWhiteSpace(id)
                    || id.Equals(currentDeviceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ProbeOverrides.TryGetValue(id, out var overrideIndex) && overrideIndex >= 0)
                    reserved.Add(overrideIndex);
                else if (DeviceToIndex.TryGetValue(id, out var catalogIndex) && catalogIndex >= 0)
                    reserved.Add(catalogIndex);
            }
        }

        return reserved;
    }

    /// <summary>DirectShow indices assigned in catalog to selected layout devices only.</summary>
    public static HashSet<int> GetCatalogIndicesForSelectedDevices(IReadOnlyList<string?> selectedDeviceIds)
    {
        var indices = new HashSet<int>();
        if (selectedDeviceIds == null) return indices;
        lock (Gate)
        {
            foreach (var id in selectedDeviceIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (ProbeOverrides.TryGetValue(id, out var over) && over >= 0)
                    indices.Add(over);
                else if (DeviceToIndex.TryGetValue(id, out var idx) && idx >= 0)
                    indices.Add(idx);
            }
        }
        return indices;
    }
}
