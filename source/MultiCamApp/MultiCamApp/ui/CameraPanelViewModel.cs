using MultiCamApp.Capture;
using MultiCamApp.Localization;

namespace MultiCamApp.Ui;

public sealed class CameraPanelViewModel
{
    public int Index { get; }
    public CameraSlotPipeline Pipeline { get; }
    public string Label { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public string PreviewOpenProgress { get; set; } = "";
    public string StatusText => Pipeline.IsDisconnected
        ? $"{Pipeline.Status}: {Pipeline.LastError}"
        : Pipeline.Status;
    public string StatsText => $"{Pipeline.ResolutionText}";

    public CameraPanelViewModel(int index, CameraSlotPipeline pipeline)
    {
        Index = index;
        Pipeline = pipeline;
        pipeline.StateChanged += () => StatsChanged?.Invoke();
    }

    public event Action? StatsChanged;
}
