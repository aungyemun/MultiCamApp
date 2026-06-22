using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

public sealed class CameraReconnectService
{
    private readonly LogService _log = new();

    public async Task<bool> TryReconnectAsync(
        CameraSlotPipeline slot,
        AppConfig config,
        IReadOnlyList<CameraDevice> devices)
    {
        if (string.IsNullOrEmpty(slot.AssignedDeviceId)) return false;
        var resumePreview = slot.WasPreviewingBeforeDisconnect;
        _log.Info("camera", $"{slot.SlotName} reconnecting…");
        var ok = await slot.OpenAsync(slot.AssignedDeviceId, config, devices);
        if (ok && resumePreview)
            await slot.StartPreviewAsync();
        return ok;
    }
}
