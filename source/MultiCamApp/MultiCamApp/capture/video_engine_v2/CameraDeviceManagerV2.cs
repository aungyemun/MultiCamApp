////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Enumerates and tracks camera devices for VideoEngineV2 using Windows-native APIs.
/// Uses <see cref="MediaDevice.GetVideoCaptureSelector"/> for device IDs (stable symbolic links)
/// and <see cref="MediaFrameSourceGroup"/> for available capture formats — no camera is opened.
/// </summary>
public sealed class CameraDeviceManagerV2 : IDisposable
{
    private readonly CameraDeviceWatcher _watcher;
    private readonly List<V2CameraDeviceInfo> _devices = new();
    private bool _disposed;

    /// <summary>Raised when the live watcher detects a new camera.</summary>
    public event EventHandler<V2CameraDeviceInfo>? DeviceAdded;
    /// <summary>Raised when the live watcher detects a camera removal. Argument is device ID.</summary>
    public event EventHandler<string>? DeviceRemoved;

    public CameraDeviceManagerV2()
    {
        _watcher = new CameraDeviceWatcher();
        _watcher.DeviceAdded   += OnWatcherAdded;
        _watcher.DeviceRemoved += OnWatcherRemoved;
    }

    /// <summary>Snapshot of currently enumerated camera devices.</summary>
    public IReadOnlyList<V2CameraDeviceInfo> Devices => _devices.AsReadOnly();

    /// <summary>
    /// Enumerates all available video capture devices using
    /// <see cref="MediaFrameSourceGroup.FindAllAsync"/> (no camera is opened).
    /// Populates <see cref="Devices"/> with name, stable device ID, and supported formats.
    /// </summary>
    public async Task EnumerateAsync(CancellationToken ct = default)
    {
        _devices.Clear();

        // MediaFrameSourceGroup gives us device info + format descriptions without opening a camera.
        IReadOnlyList<MediaFrameSourceGroup> groups;
        try
        {
            groups = await MediaFrameSourceGroup.FindAllAsync().AsTask(ct);
        }
        catch (Exception ex)
        {
            // Fallback: enumerate via DeviceInformation only (no format data)
            await EnumerateFallbackAsync(ex.Message, ct);
            return;
        }

        int index = 0;
        foreach (var group in groups)
        {
            // Only include groups that have at least one color/video source
            bool hasVideo = group.SourceInfos.Any(si =>
                si.MediaStreamType is MediaStreamType.VideoPreview
                                   or MediaStreamType.VideoRecord);
            if (!hasVideo) continue;

            // Merge MediaFrameSourceGroup formats with recording-specific profiles from
            // FindKnownVideoProfiles(VideoRecording). The latter can surface high-FPS
            // (60/100/120) or resolution modes not listed in the preview source.
            var formats = MergeWithKnownVideoProfiles(group.Id, BuildFormatList(group));

            _devices.Add(new V2CameraDeviceInfo
            {
                DeviceId      = group.Id,       // stable symbolic link
                FriendlyName  = group.DisplayName,
                EnumerationIndex = index,
                SupportedFormats = formats,
                DiscoverySource  = V2DeviceDiscoverySource.MediaFrameSourceGroup,
            });
            index++;
        }
    }

    // Fallback when MediaFrameSourceGroup.FindAllAsync is unavailable.
    private async Task EnumerateFallbackAsync(string reason, CancellationToken ct)
    {
        var selector = MediaDevice.GetVideoCaptureSelector();
        var devices  = await DeviceInformation.FindAllAsync(selector).AsTask(ct);
        int index    = 0;
        foreach (var d in devices)
        {
            _devices.Add(new V2CameraDeviceInfo
            {
                DeviceId         = d.Id,
                FriendlyName     = d.Name,
                EnumerationIndex = index,
                SupportedFormats = Array.Empty<V2CaptureFormat>(),
                DiscoverySource  = V2DeviceDiscoverySource.DeviceInformationFallback,
                FallbackReason   = reason,
            });
            index++;
        }
    }

    /// <summary>Starts live hot-plug watching. Devices discovered after enumeration will raise <see cref="DeviceAdded"/>.</summary>
    public void StartWatching() => _watcher.Start();

    /// <summary>Stops the live watcher.</summary>
    public void StopWatching() => _watcher.Stop();

    private static IReadOnlyList<V2CaptureFormat> BuildFormatList(MediaFrameSourceGroup group)
    {
        var seen    = new HashSet<string>();
        var formats = new List<V2CaptureFormat>();

        foreach (var si in group.SourceInfos)
        {
            if (si.MediaStreamType is not MediaStreamType.VideoPreview
                                  and not MediaStreamType.VideoRecord)
                continue;

            foreach (var desc in si.VideoProfileMediaDescription)
            {
                var key = $"{desc.Width}x{desc.Height}@{desc.FrameRate:F2}";
                if (!seen.Add(key)) continue;

                formats.Add(new V2CaptureFormat
                {
                    Width        = (int)desc.Width,
                    Height       = (int)desc.Height,
                    NominalFps   = desc.FrameRate,
                    PixelFormat  = V2PixelFormat.Any, // exact subtype not exposed at this enumeration level
                    FormatSource = V2FormatSource.FrameSourceGroup,
                });
            }
        }

        return formats.Count > 0 ? formats.AsReadOnly()
                                 : BuildStandardFallbackFormats();
    }

    /// <summary>
    /// Queries <see cref="MediaCapture.FindKnownVideoProfiles"/> for the VideoRecording profile
    /// and merges any additional formats (e.g. 60/100/120 fps modes) into the existing list.
    /// Silently returns the original list when the device doesn't support named profiles.
    /// </summary>
    private static IReadOnlyList<V2CaptureFormat> MergeWithKnownVideoProfiles(
        string deviceId, IReadOnlyList<V2CaptureFormat> existing)
    {
        IReadOnlyList<MediaCaptureVideoProfile> profiles;
        try
        {
            profiles = MediaCapture.FindKnownVideoProfiles(deviceId, KnownVideoProfile.VideoRecording);
        }
        catch
        {
            // Device doesn't expose named video profiles (most USB UVC webcams) — use existing list.
            return existing;
        }

        if (profiles.Count == 0) return existing;

        var seen   = new HashSet<string>(existing.Select(f => $"{f.Width}x{f.Height}@{f.NominalFps:F2}"));
        var merged = new List<V2CaptureFormat>(existing);

        foreach (var profile in profiles)
        {
            foreach (var desc in profile.SupportedRecordMediaDescription)
            {
                var key = $"{desc.Width}x{desc.Height}@{desc.FrameRate:F2}";
                if (!seen.Add(key)) continue;

                merged.Add(new V2CaptureFormat
                {
                    Width        = (int)desc.Width,
                    Height       = (int)desc.Height,
                    NominalFps   = desc.FrameRate,
                    PixelFormat  = V2PixelFormat.Any,
                    FormatSource = V2FormatSource.KnownVideoProfile,
                });
            }
        }

        return merged.Count > existing.Count ? merged.AsReadOnly() : existing;
    }

    // When a camera reports no video profiles, assume it supports common UVC resolutions.
    private static IReadOnlyList<V2CaptureFormat> BuildStandardFallbackFormats() =>
    [
        new V2CaptureFormat { Width = 1920, Height = 1080, NominalFps = 30, PixelFormat = V2PixelFormat.Any, FormatSource = V2FormatSource.FallbackStandard },
        new V2CaptureFormat { Width = 1280, Height =  720, NominalFps = 30, PixelFormat = V2PixelFormat.Any, FormatSource = V2FormatSource.FallbackStandard },
        new V2CaptureFormat { Width =  640, Height =  480, NominalFps = 30, PixelFormat = V2PixelFormat.Any, FormatSource = V2FormatSource.FallbackStandard },
    ];

    private void OnWatcherAdded(object? sender, V2CameraDeviceInfo info)
    {
        if (!_devices.Any(d => d.DeviceId == info.DeviceId))
            _devices.Add(info);
        DeviceAdded?.Invoke(this, info);
    }

    private void OnWatcherRemoved(object? sender, string deviceId)
    {
        _devices.RemoveAll(d => d.DeviceId == deviceId);
        DeviceRemoved?.Invoke(this, deviceId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.DeviceAdded   -= OnWatcherAdded;
        _watcher.DeviceRemoved -= OnWatcherRemoved;
        _watcher.Dispose();
    }
}

/// <summary>Camera device descriptor enriched with format information.</summary>
public sealed class V2CameraDeviceInfo
{
    /// <summary>Stable device symbolic link (e.g. <c>\\?\USB#VID_...</c>) from the Windows device stack.</summary>
    public string DeviceId { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    /// <summary>Zero-based index in the enumeration order (index fallback when symbolic link is unavailable).</summary>
    public int EnumerationIndex { get; init; }
    public IReadOnlyList<V2CaptureFormat> SupportedFormats { get; init; } = Array.Empty<V2CaptureFormat>();
    public V2DeviceDiscoverySource DiscoverySource { get; init; }
    public string? FallbackReason { get; init; }
    public DeviceInformationKind Kind { get; init; }
    public override string ToString() => $"{FriendlyName} [{EnumerationIndex}]";
}

/// <summary>How a <see cref="V2CameraDeviceInfo"/> was discovered.</summary>
public enum V2DeviceDiscoverySource
{
    /// <summary>Via <see cref="MediaFrameSourceGroup.FindAllAsync"/> — provides stable ID and format list.</summary>
    MediaFrameSourceGroup,
    /// <summary>Via <see cref="DeviceInformation.FindAllAsync"/> fallback — provides stable ID but no format list.</summary>
    DeviceInformationFallback,
    /// <summary>Injected by the live <see cref="CameraDeviceWatcher"/>.</summary>
    LiveWatcher,
}
