////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>WinRT MediaFrameReader preview (fallback when OpenCV is disabled).</summary>
public sealed class PreviewController
{
    private readonly LogService _log = new();
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private int _maxPreviewFps = 20;
    private long _lastFrameTicks;
    private long _framesDelivered;
    private long _framesReceived;
    private string _sourceLabel = "";

    public event Action<SoftwareBitmap>? FrameArrived;

    public long CaptureFrameCount => Interlocked.Read(ref _framesReceived);

    public void Attach(MediaCapture capture, int maxPreviewFpsUi = 20)
    {
        _capture = capture;
        SetPreviewFpsCap(maxPreviewFpsUi);
        _framesDelivered = 0;
        Interlocked.Exchange(ref _framesReceived, 0);
    }

    public void SetPreviewFpsCap(int maxPreviewFpsUi) =>
        _maxPreviewFps = Math.Max(5, maxPreviewFpsUi);

    public async Task StartAsync()
    {
        if (_capture == null) return;

        try { await _capture.StartPreviewAsync(); }
        catch (Exception ex) { _log.Info("preview", $"StartPreviewAsync: {ex.Message}"); }

        foreach (var source in EnumerateSources())
        {
            if (await TryStartReaderAsync(source).ConfigureAwait(false))
                return;
        }

        _log.Error("preview", "WinRT preview: no frame source produced frames");
    }

    private IEnumerable<MediaFrameSource> EnumerateSources()
    {
        if (_capture == null) yield break;
        var all = _capture.FrameSources.Values.ToList();
        MediaStreamType[] order = [MediaStreamType.VideoPreview, MediaStreamType.VideoRecord];
        foreach (var t in order)
        {
            foreach (var s in all.Where(s => s.Info.MediaStreamType == t))
                yield return s;
        }
        foreach (var s in all)
            yield return s;
    }

    private async Task<bool> TryStartReaderAsync(MediaFrameSource source)
    {
        await StopReaderOnlyAsync().ConfigureAwait(false);
        _sourceLabel = $"{source.Info.MediaStreamType}/{source.Info.SourceKind}";
        try
        {
            _reader = await _capture!.CreateFrameReaderAsync(source);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrameArrived;
            var status = await _reader.StartAsync();
            _log.Info("preview", $"WinRT reader on {_sourceLabel} status={status}");
            return status == MediaFrameReaderStartStatus.Success;
        }
        catch (Exception ex)
        {
            _log.Error("preview", $"WinRT reader failed on {_sourceLabel}", ex);
            await StopReaderOnlyAsync().ConfigureAwait(false);
            return false;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frameRef = sender.TryAcquireLatestFrame();
        if (frameRef == null) return;
        Interlocked.Increment(ref _framesReceived);

        var minInterval = TimeSpan.TicksPerSecond / _maxPreviewFps;
        var now = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        if (now - _lastFrameTicks < minInterval) return;
        _lastFrameTicks = now;

        var bitmap = ExtractBitmap(frameRef);
        if (bitmap == null) return;

        try
        {
            using (bitmap)
            {
                var clone = SoftwareBitmap.Copy(bitmap);
                Interlocked.Increment(ref _framesDelivered);
                FrameArrived?.Invoke(clone);
            }
        }
        catch (Exception ex)
        {
            _log.Error("preview", "WinRT frame delivery failed", ex);
        }
    }

    private SoftwareBitmap? ExtractBitmap(MediaFrameReference frameRef)
    {
        var video = frameRef.VideoMediaFrame;
        if (video == null) return null;

        var sb = video.SoftwareBitmap;
        if (sb != null)
            return ToBgra8(sb);

        var surface = video.Direct3DSurface;
        if (surface == null) return null;

        try
        {
            var copied = SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).AsTask().GetAwaiter().GetResult();
            return ToBgra8(copied);
        }
        catch
        {
            return null;
        }
    }

    private static SoftwareBitmap ToBgra8(SoftwareBitmap bitmap)
    {
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
            bitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
            return bitmap;
        return SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private async Task StopReaderOnlyAsync()
    {
        if (_reader == null) return;
        _reader.FrameArrived -= OnFrameArrived;
        try { await _reader.StopAsync(); } catch { /* ignore */ }
        _reader.Dispose();
        _reader = null;
    }

    public async Task StopAsync()
    {
        await StopReaderOnlyAsync().ConfigureAwait(false);
        if (_capture != null)
        {
            try { await _capture.StopPreviewAsync(); } catch { /* ignore */ }
        }
        _log.Info("preview", $"WinRT preview stopped ({_framesDelivered} frames, {_sourceLabel})");
        _framesDelivered = 0;
    }
}
