using MultiCamApp.Capture;
using MultiCamApp.Recording;
using MultiCamApp.Utils;

namespace MultiCamApp.Core;

public sealed class ResourceManager
{
    private readonly LogService _log = new();

    public async Task CleanupPreviewAsync(CameraManager cameras)
    {
        foreach (var s in cameras.Slots)
            await s.StopPreviewAsync();
    }

    public async Task CleanupAllAsync(CameraManager cameras, RecordingController? recording = null)
    {
        if (recording != null)
            await recording.StopAllRecordingsSafelyAsync(cameras.Slots);

        await cameras.ReleaseAllCamerasAsync();
        _log.Info("cleanup", "All camera and recording resources released");
    }
}
