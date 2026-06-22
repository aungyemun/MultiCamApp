using MultiCamApp.Capture;
using MultiCamApp.Core;

namespace MultiCamApp.Ui;

/// <summary>Detects when UI capture prefs differ from the live OpenCV pipeline (preview not restarted).</summary>
public static class PreRecordSettingsChecker
{
    public sealed record MismatchLine(
        string SlotName,
        bool IsResolution,
        string RequestedPreset,
        string LivePreset,
        double RequestedFps,
        double LiveFps);

    public static IReadOnlyList<MismatchLine>? Analyze(AppConfig config, IReadOnlyList<CameraSlotPipeline> slots)
    {
        var wantW = config.PreferredCaptureWidth;
        var wantH = config.PreferredCaptureHeight;
        var wantRes = wantW > 0 && wantH > 0;
        var wantFps = config.PreferFps > 0;
        if (!wantRes && !wantFps) return null;

        var lines = new List<MismatchLine>();
        foreach (var slot in slots)
        {
            if (slot.Status is not ("Previewing" or "Recording")) continue;

            var liveW = slot.ActualPreviewWidth > 0 ? slot.ActualPreviewWidth : slot.RecordWidth;
            var liveH = slot.ActualPreviewHeight > 0 ? slot.ActualPreviewHeight : slot.RecordHeight;
            var liveFps = slot.SelectedDeviceFps > 0 ? slot.SelectedDeviceFps : slot.ObservedCaptureFps;

            if (wantRes && liveW > 0 && liveH > 0 && (liveW != wantW || liveH != wantH))
            {
                lines.Add(new MismatchLine(
                    slot.SlotName,
                    true,
                    CaptureResolutionPreset.ToLabel(wantW, wantH),
                    CaptureResolutionPreset.ToLabel(liveW, liveH),
                    config.PreferFps,
                    liveFps));
            }
            else if (wantFps && liveFps > 0 && Math.Abs(liveFps - config.PreferFps) > 1.5)
            {
                lines.Add(new MismatchLine(
                    slot.SlotName,
                    false,
                    CaptureResolutionPreset.ToLabel(wantW, wantH),
                    CaptureResolutionPreset.ToLabel(liveW, liveH),
                    config.PreferFps,
                    liveFps));
            }
        }

        return lines.Count > 0 ? lines : null;
    }
}
