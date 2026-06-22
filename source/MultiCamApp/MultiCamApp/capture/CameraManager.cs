////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

public sealed class CameraManager
{
    private readonly LogService _log = new();
    private readonly List<CameraSlotPipeline> _slots = new();
    private IReadOnlyList<CameraDevice> _last = Array.Empty<CameraDevice>();
    private AppConfig _config = new();

    public IReadOnlyList<CameraDevice> LastDevices => _last;
    public IReadOnlyList<CameraSlotPipeline> Slots => _slots;

    public void RegisterSlots(IEnumerable<CameraSlotPipeline> pipelines)
    {
        _slots.Clear();
        _slots.AddRange(pipelines);
    }

    public void SetConfig(AppConfig config) => _config = config;

    public async Task<IReadOnlyList<CameraDevice>> DiscoverAsync()
    {
        if (PreviewStartTrace.IsActive)
            PreviewStartTrace.NotifyDiscovery("CameraManager.DiscoverAsync", "full WinRT camera discovery", warnIfDuringPreview: true);

        var list = await CameraDeviceDiscovery.DiscoverAsync(_config);
        OpenCvDirectShowIndexCatalog.Rebuild(list);
        _last = list;
        return list;
    }

    public Task<IReadOnlyList<CameraDevice>> RefreshAsync() => DiscoverAsync();

    public CameraDevice? GetDefaultDevice() =>
        _last.FirstOrDefault(d => d.IsDefault) ?? _last.FirstOrDefault();

    public async Task ReleaseAllCamerasAsync()
    {
        _log.Info("camera", "ReleaseAllCameras");
        await Task.WhenAll(_slots.Select(s => s.CloseAsync(clearSessionHint: true)));
        OpenCvDeviceSession.Reset();
    }

    public CameraSlotPipeline? FindByDeviceId(string? deviceId) =>
        _slots.FirstOrDefault(s => s.AssignedDeviceId == deviceId);
}
