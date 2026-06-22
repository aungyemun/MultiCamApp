namespace MultiCamApp.Diagnostics;

public sealed class CameraCapabilityReport
{
    public DateTime ScanTimeLocal { get; set; } = DateTime.Now;
    public List<CameraCapabilityEntry> Cameras { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public sealed class CameraCapabilityEntry
{
    public string DisplayName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string CameraKind { get; set; } = "Unknown";
    public string AutoFocusSupported { get; set; } = "unavailable";
    public string ManualFocusSupported { get; set; } = "unavailable";
    public string CurrentAutoFocusState { get; set; } = "not confirmed";
    public string CurrentManualFocusValue { get; set; } = "unavailable";
    public List<CameraPresetCapability> Presets { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class CameraPresetCapability
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double RequestedFps { get; set; } = 30;
    public string BackendUsed { get; set; } = "None";
    public string Result { get; set; } = "Unknown";
    public int? ActualWidth { get; set; }
    public int? ActualHeight { get; set; }
    public double? ActualFps { get; set; }
    public string Warning { get; set; } = "";
}
