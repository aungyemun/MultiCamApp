namespace MultiCamApp.Capture;

public sealed class CameraDevice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>User-visible label (name + built-in / USB hint).</summary>
    public string DisplayName { get; init; } = "";
    public CameraKind Kind { get; init; } = CameraKind.Unknown;
    public bool IsEnabled { get; init; } = true;
    public bool IsDefault { get; init; }
    /// <summary>Order from <see cref="MediaDevice.GetVideoCaptureSelector"/> enumeration (matches Windows Camera app list).</summary>
    public int EnumerationIndex { get; init; }
    public bool IsExternal => Kind == CameraKind.ExternalUsb;
    public bool IsBuiltIn => Kind is CameraKind.BuiltInFront or CameraKind.BuiltInBack or CameraKind.BuiltInOther;

    public override string ToString() => DisplayName;
}
