////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>
/// Discovers cameras the same way as the Windows Camera app: MediaDevice video capture selector,
/// enclosure panel for built-in cameras, USB instance IDs for external webcams.
/// </summary>
public static class CameraDeviceDiscovery
{
    private static readonly LogService Log = new();

    public static async Task<IReadOnlyList<CameraDevice>> DiscoverAsync(AppConfig config)
    {
        var selector = MediaDevice.GetVideoCaptureSelector();
        var raw = await DeviceInformation.FindAllAsync(selector);
        var list = new List<CameraDevice>();
        var enumerationIndex = 0;

        foreach (var info in raw)
        {
            if (!config.IncludeDisabledCameras && info.IsEnabled == false)
                continue;

            var device = Map(info, enumerationIndex++);
            if (device != null)
                list.Add(device);
        }

        var ordered = CameraDeviceDisplayNamer.ApplyDisplayNames(list).ToList();
        var preferredId = SelectPreferredDeviceId(ordered, config);
        if (preferredId != null)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Id != preferredId) continue;
                var d = ordered[i];
                ordered[i] = new CameraDevice
                {
                    Id = d.Id,
                    Name = d.Name,
                    DisplayName = d.DisplayName,
                    Kind = d.Kind,
                    IsEnabled = d.IsEnabled,
                    IsDefault = true,
                    EnumerationIndex = d.EnumerationIndex
                };
                break;
            }
        }

        Log.Info("camera", $"Discovered {ordered.Count} video device(s) (selector=MediaDevice)");
        foreach (var d in ordered.OrderBy(x => x.EnumerationIndex))
            Log.Info("camera", $"  [{d.EnumerationIndex}] [{d.Kind}] {(d.IsDefault ? "*" : " ")} {d.DisplayName}");

        return ordered;
    }

    private static CameraDevice? Map(DeviceInformation info, int enumerationIndex)
    {
        if (string.IsNullOrWhiteSpace(info.Id))
            return null;

        var name = string.IsNullOrWhiteSpace(info.Name) ? "Camera" : info.Name.Trim();
        var kind = Classify(info);
        var displayName = CameraDeviceDisplayNamer.FormatSingleDeviceDisplayName(name, kind);
        return new CameraDevice
        {
            Id = info.Id,
            Name = name,
            DisplayName = displayName,
            Kind = kind,
            IsEnabled = info.IsEnabled,
            EnumerationIndex = enumerationIndex
        };
    }

    internal static CameraKind Classify(DeviceInformation info)
    {
        var id = info.Id;
        if (IsVirtualDevice(id, info.Name))
            return CameraKind.Virtual;

        var panel = info.EnclosureLocation?.Panel;
        if (panel == Panel.Front)
            return CameraKind.BuiltInFront;
        if (panel == Panel.Back)
            return CameraKind.BuiltInBack;
        if (panel is Panel.Top or Panel.Bottom or Panel.Left or Panel.Right)
            return CameraKind.BuiltInOther;

        if (IsUsbDevice(id))
            return CameraKind.ExternalUsb;

        var fromName = GuessBuiltInFromName(info.Name);
        if (fromName is CameraKind.BuiltInFront or CameraKind.BuiltInBack or CameraKind.BuiltInOther)
            return fromName;

        return CameraKind.Unknown;
    }

    private static CameraKind GuessBuiltInFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CameraKind.Unknown;

        var n = name.ToLowerInvariant();
        if (n.Contains("integrated") || n.Contains("built-in") || n.Contains("builtin") ||
            n.Contains("facetime") || n.Contains("internal"))
            return CameraKind.BuiltInFront;

        if (n.Contains("usb") || n.Contains("external"))
            return CameraKind.ExternalUsb;

        return CameraKind.BuiltInOther;
    }

    private static bool IsUsbDevice(string id) =>
        id.Contains(@"\\?\usb", StringComparison.OrdinalIgnoreCase) ||
        id.Contains("&usb#", StringComparison.OrdinalIgnoreCase) ||
        id.Contains(@"usb#", StringComparison.OrdinalIgnoreCase);

    private static bool IsVirtualDevice(string id, string? name)
    {
        var text = $"{id} {name}".ToLowerInvariant();
        return text.Contains("virtual") || text.Contains("obs") || text.Contains("snap camera");
    }

    internal static string BuildDisplayName(string name, CameraKind kind) =>
        CameraDeviceDisplayNamer.FormatSingleDeviceDisplayName(name, kind);

    /// <summary>Windows Camera–style default: built-in front, else first built-in, else first USB.</summary>
    public static string? SelectPreferredDeviceId(IReadOnlyList<CameraDevice> devices, AppConfig config)
    {
        if (devices.Count == 0) return null;

        var policy = (config.PreferredCameraPolicy ?? "default").Trim().ToLowerInvariant();
        return policy switch
        {
            "external" => devices.FirstOrDefault(d => d.Kind == CameraKind.ExternalUsb)?.Id
                          ?? devices[0].Id,
            "builtin" or "built-in" => devices.FirstOrDefault(d => d.IsBuiltIn)?.Id
                                       ?? devices[0].Id,
            "back" => devices.FirstOrDefault(d => d.Kind == CameraKind.BuiltInBack)?.Id
                      ?? devices.FirstOrDefault(d => d.IsBuiltIn)?.Id
                      ?? devices[0].Id,
            _ => devices.FirstOrDefault(d => d.Kind == CameraKind.BuiltInFront)?.Id
                 ?? devices.FirstOrDefault(d => d.IsBuiltIn)?.Id
                 ?? devices.FirstOrDefault(d => d.Kind == CameraKind.ExternalUsb)?.Id
                 ?? devices[0].Id
        };
    }
}
