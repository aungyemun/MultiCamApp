namespace MultiCamApp.Capture;

/// <summary>UI-only display labels. Device identity always uses <see cref="CameraDevice.Id"/>.</summary>
public static class CameraDeviceDisplayNamer
{
    /// <summary>Assign numbered labels when multiple cameras share the same Windows device name.</summary>
    public static IReadOnlyList<CameraDevice> ApplyDisplayNames(IReadOnlyList<CameraDevice> devices)
    {
        if (devices.Count == 0)
            return devices;

        var groups = devices
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(d => d.EnumerationIndex))
            .ToList();

        var result = new List<CameraDevice>(devices.Count);
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(d => d.EnumerationIndex).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var d = ordered[i];
                result.Add(new CameraDevice
                {
                    Id = d.Id,
                    Name = d.Name,
                    DisplayName = FormatDisplayName(d.Name, d.Kind, i + 1, ordered.Count),
                    Kind = d.Kind,
                    IsEnabled = d.IsEnabled,
                    IsDefault = d.IsDefault,
                    EnumerationIndex = d.EnumerationIndex
                });
            }
        }

        return result
            .OrderBy(d => d.Kind switch
            {
                CameraKind.BuiltInFront => 0,
                CameraKind.BuiltInBack => 1,
                CameraKind.BuiltInOther => 2,
                CameraKind.ExternalUsb => 3,
                CameraKind.Virtual => 4,
                _ => 5
            })
            .ThenBy(d => d.EnumerationIndex)
            .ToList();
    }

    public static string FormatDisplayName(string rawName, CameraKind kind, int ordinalInNameGroup, int nameGroupSize)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "Camera" : rawName.Trim();
        if (nameGroupSize > 1)
            return $"{name} #{ordinalInNameGroup}";

        return FormatSingleDeviceDisplayName(name, kind);
    }

    public static string FormatSingleDeviceDisplayName(string rawName, CameraKind kind)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "Camera" : rawName.Trim();
        return kind switch
        {
            CameraKind.BuiltInFront => $"{name} (Built-in front)",
            CameraKind.BuiltInBack => $"{name} (Built-in back)",
            CameraKind.BuiltInOther => $"{name} (Built-in)",
            CameraKind.Virtual => $"{name} (Virtual)",
            _ => name
        };
    }
}
