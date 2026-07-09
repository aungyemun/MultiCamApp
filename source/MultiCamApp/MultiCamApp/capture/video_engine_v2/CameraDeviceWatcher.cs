////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).
// V2-namespaced PnP device watcher; does not replace the legacy CameraDeviceWatcher.

using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Watches for camera device arrival and removal events using the Windows
/// <see cref="Windows.Devices.Enumeration.DeviceWatcher"/> API.
/// Scoped to <c>MultiCamApp.Capture.VideoEngineV2</c> — does not replace the
/// legacy <c>MultiCamApp.Capture.CameraDeviceWatcher</c>.
/// </summary>
public sealed class CameraDeviceWatcher : IDisposable
{
    private DeviceWatcher? _watcher;
    private bool _disposed;

    /// <summary>Raised when a new camera device is connected.</summary>
    public event EventHandler<V2CameraDeviceInfo>? DeviceAdded;

    /// <summary>Raised when a camera device is disconnected. Argument is the device ID.</summary>
    public event EventHandler<string>? DeviceRemoved;

    /// <summary>Raised when the initial device enumeration is complete.</summary>
    public event EventHandler? EnumerationCompleted;

    /// <summary>True while the underlying <see cref="DeviceWatcher"/> is running.</summary>
    public bool IsWatching { get; private set; }

    /// <summary>Starts watching for camera device hot-plug events.</summary>
    public void Start()
    {
        if (_disposed || _watcher is not null) return;

        // Use the same selector the legacy pipeline uses: MediaDevice.GetVideoCaptureSelector()
        string selector = MediaDevice.GetVideoCaptureSelector();
        _watcher = DeviceInformation.CreateWatcher(selector);

        _watcher.Added             += OnAdded;
        _watcher.Removed           += OnRemoved;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;

        _watcher.Start();
        IsWatching = true;
    }

    /// <summary>Stops the watcher if running.</summary>
    public void Stop()
    {
        if (_watcher is null) return;

        try
        {
            _watcher.Added             -= OnAdded;
            _watcher.Removed           -= OnRemoved;
            _watcher.EnumerationCompleted -= OnEnumerationCompleted;

            if (_watcher.Status is DeviceWatcherStatus.Started
                                or DeviceWatcherStatus.EnumerationCompleted)
            {
                _watcher.Stop();
            }
        }
        catch
        {
            // Best effort: the watcher may already be in a stopping state.
        }
        finally
        {
            _watcher   = null;
            IsWatching = false;
        }
    }

    private void OnAdded(DeviceWatcher _, DeviceInformation info)
    {
        DeviceAdded?.Invoke(this, new V2CameraDeviceInfo
        {
            DeviceId         = info.Id,
            FriendlyName     = info.Name,
            Kind             = info.Kind,
            SupportedFormats = Array.Empty<V2CaptureFormat>(),
            DiscoverySource  = V2DeviceDiscoverySource.LiveWatcher,
        });
    }

    private void OnRemoved(DeviceWatcher _, DeviceInformationUpdate update)
    {
        DeviceRemoved?.Invoke(this, update.Id);
    }

    private void OnEnumerationCompleted(DeviceWatcher _, object __)
    {
        EnumerationCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
