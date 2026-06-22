namespace MultiCamApp.Diagnostics;

public sealed class UsbTopologyReport
{
    public DateTime ScanTimeLocal { get; set; } = DateTime.Now;
    public string Status { get; set; } = "Unknown";
    public List<UsbCameraTopologyEntry> SelectedCameras { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public sealed class UsbCameraTopologyEntry
{
    public string DisplayName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string CameraKind { get; set; } = "Unknown";
    public string UsbControllerOrHub { get; set; } = "Unknown";
}
