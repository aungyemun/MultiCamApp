using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>Watches for USB/built-in camera attach and detach.</summary>
public sealed class CameraDeviceWatcher : IDisposable
{
    private readonly LogService _log = new();
    private DeviceWatcher? _watcher;

    public event Action? DevicesChanged;
    public event Action<string>? DeviceRemoved;

    public void Start()
    {
        if (_watcher != null) return;
        var selector = MediaDevice.GetVideoCaptureSelector();
        _watcher = DeviceInformation.CreateWatcher(selector);
        _watcher.Added += OnAdded;
        _watcher.Removed += OnRemoved;
        _watcher.Updated += OnUpdated;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watcher.Start();
        _log.Info("camera", "Device watcher started");
    }

    private void OnAdded(DeviceWatcher s, DeviceInformation d) => RaiseChanged();
    private void OnUpdated(DeviceWatcher s, DeviceInformationUpdate u) => RaiseChanged();
    private void OnEnumerationCompleted(DeviceWatcher s, object o) => RaiseChanged();

    private void OnRemoved(DeviceWatcher s, DeviceInformationUpdate u)
    {
        _log.Info("camera", $"Camera removed: {u.Id}");
        DeviceRemoved?.Invoke(u.Id);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        _log.Info("camera", "Camera list changed");
        DevicesChanged?.Invoke();
    }

    public void Stop()
    {
        if (_watcher == null) return;
        _watcher.Stop();
        _watcher.Added -= OnAdded;
        _watcher.Removed -= OnRemoved;
        _watcher.Updated -= OnUpdated;
        _watcher.EnumerationCompleted -= OnEnumerationCompleted;
        _watcher = null;
    }

    public void Dispose() => Stop();
}
