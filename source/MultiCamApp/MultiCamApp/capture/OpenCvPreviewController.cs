////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using MultiCamApp.Core;
using MultiCamApp.Experiment;
using MultiCamApp.Verification;
using MultiCamApp.Metadata;
using MultiCamApp.Recording;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>OpenCV preview + recording on one capture device without stopping the preview loop.</summary>
public sealed class OpenCvPreviewController : IDisposable
{
    private const int MaxPreviewDisplayWidth = 960;
    internal const int DefaultRecordQueueCapacity = 30;
    internal const int MaxRecordQueueCapacity = 90;
    internal const int DefaultRecordingPreviewFpsCap = 15;
    internal const int HighResolutionRecordingPreviewFpsCap = 10;
    private const int DroppedFrameTimestampSampleLimit = 12;
    private const int MeasureCaptureMs = 2200;
    private const int StreamLostConsecutiveFailures = 10;
    private const int StreamLostNoFrameMs = 2000;

    private readonly LogService _log = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureLoopTask;
    private int _deviceIndex;
    private string? _directShowName;
    private string? _directShowOpenUri;
    private int _maxFps = 20;
    private long _framesDelivered;
    private long _captureFramesRead;
    private int _liveWidth;
    private int _liveHeight;
    private double _liveFps = 30;
    private int _requestedWidth;
    private int _requestedHeight;
    private double _requestedFps = 30;
    private bool _fastPreviewOpen;
    private CancellationTokenSource? _openAbortCts;

    private VideoWriter? _writer;
    private readonly object _writerLock = new();
    private long _recordedFrames;
    private long _lastRecordedFrameCount;
    private double _lastRecordWriterFps;
    private long _recordStartTicks;
    private long _recordFirstFrameTicks;
    private long _recordLastFrameTicks;
    private long _recordLastCaptureTicks;
    private long _recordLastWrittenCaptureTicks;
    private long _recordStopRequestedTicks;
    private long _recordStopTicks;
    private long _recordRequestedStartTicks;
    private long _cameraRecordingStartTicks;
    private long _writerClosedTicks;
    private DateTime _recordStartUtc;
    private DateTime _recordStartLocal;
    private DateTime _recordRequestedStartUtc;
    private DateTime _recordRequestedStartLocal;
    private DateTime _cameraRecordingStartUtc;
    private DateTime _cameraRecordingStartLocal;
    private DateTime _firstFrameUtc;
    private DateTime _firstFrameLocal;
    private DateTime _lastFrameUtc;
    private DateTime _lastFrameLocal;
    private DateTime _recordStopRequestedUtc;
    private DateTime _recordStopRequestedLocal;
    private DateTime _recordStopUtc;
    private DateTime _recordStopLocal;
    private DateTime _writerClosedUtc;
    private DateTime _writerClosedLocal;
    private string _recordDiagnosticSlotName = "opencv";
    private Channel<RecordedFramePacket>? _recordChannel;
    private Task? _recordPumpTask;
    private Mat? _recordResizeBuffer;
    private int _recordTargetWidth;
    private int _recordTargetHeight;
    private long _writerQueueDrops;
    private long _writerQueueFullCount;
    private int _recordQueueCapacity = DefaultRecordQueueCapacity;
    private int _recordingPreviewFpsCap = DefaultRecordingPreviewFpsCap;
    private int _recordQueueDepth;
    private int _recordQueueDepthMax;
    private long _writerFramesDequeued;
    private long _writerWriteCount;
    private long _writerWriteTicksTotal;
    private long _writerWriteTicksMax;
    private long _writerLoopStartTicks;
    private long _writerLoopStopTicks;
    private long _previewFramesRenderedDuringRecording;
    private long _recordStartCaptureFrames;
    private long _recordEnqueuedFrames;
    private readonly object _frameTimestampLock = new();
    private readonly List<FrameTimestampRecord> _frameTimestampRecords = [];
    private FrameTimestampCsvResult _lastFrameTimestampCsvResult = FrameTimestampCsvResult.NotWritten;
    private readonly object _dropSampleLock = new();
    private readonly List<double> _droppedFrameRelativeMs = [];
    private readonly object _diagnosticIntervalLock = new();
    private long _diagnosticIntervalCount;
    private double _diagnosticIntervalMeanMs;
    private double _diagnosticIntervalM2Ms;
    private double _diagnosticIntervalMinMs;
    private double _diagnosticIntervalMaxMs;
    private long _diagnosticLongGapCount;
    private long _diagnosticSevereGapCount;
    private long _diagnosticShortIntervalCount;

    private volatile bool _writerOpened;
    private volatile bool _firstFrameReceivedSinceRecord;
    private volatile bool _firstFrameWrittenSinceRecord;
    private TaskCompletionSource<bool>? _firstFrameWrittenTcs;

    private volatile bool _measuringForRecord;
    private int _measureFrameCount;
    private long _measureStartTick;
    private TaskCompletionSource<double>? _measureTcs;

    private ExperimentSessionOptions? _experiment;
    private FrameTimingMonitor? _timingMonitor;
    private FrameTimingSummary? _lastTimingSummary;
    private FrameTimingSummary? _lastFrozenTimingSummary;
    private CaptureTimingSnapshot? _lastCaptureTimingSnapshot;
    private string _lastExperimentVerdict = "";
    private AppConfig? _appConfigForExperiment;
    private int _consecutiveReadFailures;
    private long _lastGoodFrameTick;
    private volatile int _streamLostSignaled;
    private int _previewQualityLogged;
    private CameraFocusControlStatus _lastFocusControlStatus = CameraFocusControlStatus.NotAttempted(false);
    private CameraExposureControlStatus _lastExposureControlStatus = CameraExposureControlStatus.NotAttempted(true);

    private sealed record RecordedFramePacket(
        Mat Frame,
        long FrameIndex,
        DateTime CaptureUtcTime,
        DateTime CaptureLocalTime,
        long CaptureTicks,
        double CaptureMonotonicSec,
        double DeltaFromPreviousCaptureMs,
        double ExpectedIntervalMs,
        double IntervalErrorMs,
        int WriterQueueDepthAtEnqueue,
        string SourceCameraName,
        string RecordingTimingMode);

    private sealed record FrameTimestampRecord(
        long FrameIndex,
        DateTime CaptureUtcTime,
        DateTime CaptureLocalTime,
        double CaptureMonotonicSec,
        double WriteMonotonicSec,
        double DeltaFromPreviousCaptureMs,
        double ExpectedIntervalMs,
        double IntervalErrorMs,
        bool IsOriginalFrame,
        bool IsDuplicateFrame,
        bool IsPlaceholderFrame,
        int WriterQueueDepthAtEnqueue,
        int WriterQueueDepthAtWrite,
        string SourceCameraName,
        string RecordingTimingMode);

    private sealed record FrameTimestampCsvResult(
        string Path,
        bool Written,
        long RowCount,
        DateTime? FirstCaptureUtcTime,
        DateTime? LastCaptureUtcTime,
        double FirstCaptureMonotonicSec,
        double LastCaptureMonotonicSec,
        double FirstToLastFrameDurationSec)
    {
        public static FrameTimestampCsvResult NotWritten { get; } = new("", false, 0, null, null, 0, 0, 0);
    }

    private sealed record FrameTimestampAnalysis(
        double MeasuredCameraFpsFromFirstLastFrame,
        double MeasuredCameraFpsFromMeanInterval,
        double CaptureIntervalMeanMs,
        double CaptureIntervalMedianMs,
        double CaptureIntervalStdMs,
        double CaptureIntervalMinMs,
        double CaptureIntervalMaxMs,
        double CaptureIntervalP95Ms,
        double CaptureIntervalP99Ms,
        double ExpectedIntervalMs,
        double RequestedExpectedIntervalMs,
        double MeanIntervalErrorMs,
        double AbsoluteMeanIntervalErrorMs,
        long LongGapCount,
        long ShortGapCount,
        long SevereLongGapCount,
        double JitterScoreMs,
        string FpsStabilityGrade,
        long IntervalCount,
        string Message)
    {
        public static FrameTimestampAnalysis Missing(double requestedFps) => new(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            requestedFps > 0 ? 1000.0 / requestedFps : 0,
            0, 0, 0, 0, 0, 0, "Failed", 0,
            "Missing per-frame capture timestamps.");
    }

    public bool IsRecording { get; private set; }
    public bool WriterOpened => _writerOpened;
    public bool FirstFrameReceivedSinceRecord => _firstFrameReceivedSinceRecord;
    public bool FirstFrameWrittenSinceRecord => _firstFrameWrittenSinceRecord;
    public long FramesWritten => Interlocked.Read(ref _recordedFrames);
    public long WriterQueueDrops => Interlocked.Read(ref _writerQueueDrops);
    public bool IsExperimentRecording => _experiment?.Enabled == true;
    public bool ShouldStopExperimentRecording =>
        _experiment?.Enabled == true && _timingMonitor != null &&
        _timingMonitor.ElapsedSeconds() >= _experiment.TargetDurationSeconds;
    public bool IsOpened => _capture != null && _capture.IsOpened();
    public int LiveWidth => _liveWidth;
    public int LiveHeight => _liveHeight;
    public double LiveFps => _liveFps;
    public double LatestFrameAgeMs => _lastGoodFrameTick > 0
        ? Stopwatch.GetElapsedTime(_lastGoodFrameTick).TotalMilliseconds
        : double.NaN;
    /// <summary>Every successful frame read from the device (not limited by UI preview throttle).</summary>
    public long CaptureFrameCount => Interlocked.Read(ref _captureFramesRead);
    public long LastRecordedFrameCount => _lastRecordedFrameCount;
    public double LastRecordWriterFps => _lastRecordWriterFps;
    public int CurrentRecordQueueCapacity => Volatile.Read(ref _recordQueueCapacity);
    /// <summary>False when a non-zero request was made but the driver reports a different frame size.</summary>
    public bool LastResolutionMatched { get; private set; } = true;
    public bool FrameQueueConnected => _recordChannel != null;
    public bool RecordingPumpStarted => _recordPumpTask != null;
    public long FramesCapturedSinceRecordStart =>
        IsRecording ? Math.Max(0, Interlocked.Read(ref _captureFramesRead) - _recordStartCaptureFrames) : 0;
    public event Action<BitmapSource>? FrameArrived;
    public event Action? StreamLost;
    public event Action<long>? WriterQueueDropDetected;
    public CameraFocusControlStatus LastFocusControlStatus => _lastFocusControlStatus;
    public CameraExposureControlStatus LastExposureControlStatus => _lastExposureControlStatus;

    public OpenCvRecordingDiagnosticsSnapshot GetRecordingDiagnosticsSnapshot()
    {
        var writeCount = Interlocked.Read(ref _writerWriteCount);
        var writeMeanMs = writeCount > 0
            ? (Interlocked.Read(ref _writerWriteTicksTotal) * 1000.0 / Stopwatch.Frequency) / writeCount
            : (double?)null;
        var writeMaxMs = writeCount > 0
            ? Interlocked.Read(ref _writerWriteTicksMax) * 1000.0 / Stopwatch.Frequency
            : (double?)null;

        double? intervalMean;
        double? intervalMin;
        double? intervalMax;
        double? intervalStd;
        long longGaps;
        long severeGaps;
        long shortIntervals;
        lock (_diagnosticIntervalLock)
        {
            intervalMean = _diagnosticIntervalCount > 0 ? _diagnosticIntervalMeanMs : null;
            intervalMin = _diagnosticIntervalCount > 0 ? _diagnosticIntervalMinMs : null;
            intervalMax = _diagnosticIntervalCount > 0 ? _diagnosticIntervalMaxMs : null;
            intervalStd = _diagnosticIntervalCount > 1
                ? Math.Sqrt(_diagnosticIntervalM2Ms / _diagnosticIntervalCount)
                : _diagnosticIntervalCount > 0 ? 0 : null;
            longGaps = _diagnosticLongGapCount;
            severeGaps = _diagnosticSevereGapCount;
            shortIntervals = _diagnosticShortIntervalCount;
        }

        return new OpenCvRecordingDiagnosticsSnapshot
        {
            IsRecording = IsRecording,
            RequestedFps = _experiment?.TargetFps > 0 ? _experiment.TargetFps : _lastRecordWriterFps,
            WriterFps = _lastRecordWriterFps,
            FramesCaptured = Math.Max(0, Interlocked.Read(ref _captureFramesRead) - _recordStartCaptureFrames),
            FramesEnqueued = Interlocked.Read(ref _recordEnqueuedFrames),
            FramesDequeued = Interlocked.Read(ref _writerFramesDequeued),
            FramesWritten = Interlocked.Read(ref _recordedFrames),
            WriterQueueDepth = Volatile.Read(ref _recordQueueDepth),
            WriterQueueCapacity = Volatile.Read(ref _recordQueueCapacity),
            WriterQueueMaxDepth = Volatile.Read(ref _recordQueueDepthMax),
            WriterQueueFullCount = Interlocked.Read(ref _writerQueueFullCount),
            WriterQueueDrops = Interlocked.Read(ref _writerQueueDrops),
            WriterWriteMeanMs = writeMeanMs,
            WriterWriteMaxMs = writeMaxMs,
            CaptureIntervalMeanMs = intervalMean,
            CaptureIntervalMinMs = intervalMin,
            CaptureIntervalMaxMs = intervalMax,
            CaptureIntervalStdMs = intervalStd,
            LongGapCount = longGaps,
            SevereGapCount = severeGaps,
            ShortIntervalCount = shortIntervals
        };
    }

    public void Attach(OpenCvDeviceBinding binding, int maxPreviewFpsUi = 20)
    {
        _deviceIndex = binding.Index;
        _directShowName = binding.DirectShowName;
        _directShowOpenUri = binding.DirectShowOpenUri;
        SetPreviewFpsCap(maxPreviewFpsUi);
        _framesDelivered = 0;
        Interlocked.Exchange(ref _captureFramesRead, 0);
        _consecutiveReadFailures = 0;
        _streamLostSignaled = 0;
        _previewQualityLogged = 0;
        _lastGoodFrameTick = Stopwatch.GetTimestamp();
    }

    public void SetPreviewFpsCap(int maxPreviewFpsUi) =>
        _maxFps = Math.Max(5, maxPreviewFpsUi);

    /// <summary>Width/height 0 = native (use stabilized probe size).</summary>
    public void SetCapturePreferences(int width, int height, double fps)
    {
        _requestedWidth = Math.Max(0, width);
        _requestedHeight = Math.Max(0, height);
        _requestedFps = fps > 0 ? Math.Clamp(fps, 5, 60) : 30;
    }

    /// <summary>Lighter USB warmup and stabilization during Start Preview (recording uses full path).</summary>
    public void SetFastPreviewOpen(bool enabled) => _fastPreviewOpen = enabled;

    private static readonly object DshowOpenGate = new();

    private VideoCapture? OpenCaptureDevice()
    {
        lock (DshowOpenGate)
        {
            // Exact PnP paths are safer than numeric DirectShow indices for duplicate USB cameras.
            if (!string.IsNullOrWhiteSpace(_directShowOpenUri))
            {
                var pnp = new VideoCapture(_directShowOpenUri, VideoCaptureAPIs.DSHOW);
                if (pnp.IsOpened()) return pnp;
                pnp.Dispose();
            }

            if (_deviceIndex >= 0)
            {
                var cap = new VideoCapture(_deviceIndex, VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened()) return cap;
                cap.Dispose();
            }

            if (!string.IsNullOrWhiteSpace(_directShowName))
                return new VideoCapture($"video={_directShowName}", VideoCaptureAPIs.DSHOW);
            return null;
        }
    }

    private string CaptureLabel() =>
        !string.IsNullOrWhiteSpace(_directShowOpenUri) ? "PnP device"
        : _deviceIndex >= 0 ? $"index {_deviceIndex}"
        : !string.IsNullOrWhiteSpace(_directShowName) ? _directShowName
        : "unmapped device";

    public void AbortPendingOpen()
    {
        try { _openAbortCts?.Cancel(); } catch { /* ignore */ }
        try { _openAbortCts?.Dispose(); } catch { /* ignore */ }
        _openAbortCts = null;
        ReleaseCamera();
    }

    public async Task<(bool Ok, int Width, int Height, double Fps)> OpenAndProbeAsync(
        CancellationToken cancelToken = default)
    {
        if (string.IsNullOrWhiteSpace(_directShowOpenUri)
            && string.IsNullOrWhiteSpace(_directShowName)
            && _deviceIndex < 0)
            return (false, 0, 0, 0);

        _openAbortCts?.Cancel();
        _openAbortCts?.Dispose();
        _openAbortCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        var linkedToken = _openAbortCts.Token;

        var openSec = _fastPreviewOpen
            ? PreviewSlotFailureHelper.CameraOpenTimeoutSeconds
            : CaptureResolutionHelper.IsFullHd(_requestedWidth, _requestedHeight) ? 90 : 25;
        using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(openSec));
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(linkedToken, openTimeout.Token);
        var token = combined.Token;
        try
        {
            ReleaseExistingCaptureForReopen();
            var openTask = Task.Run(() =>
            {
                VideoCapture? opened = null;
                try
                {
                    token.ThrowIfCancellationRequested();
                    _log.Info("preview",
                        $"CAMERA_OPEN_TARGET index={_deviceIndex} dshowName=\"{_directShowName ?? "-"}\" dshowUri=\"{_directShowOpenUri ?? "-"}\"");
                    opened = OpenCaptureDevice();
                    if (opened == null || !opened.IsOpened())
                    {
                        var label = CaptureLabel();
                        throw new InvalidOperationException($"OpenCV could not open camera ({label})");
                    }

                    token.ThrowIfCancellationRequested();
                    ApplyDriverCaptureProperties(opened);
                    ApplyCaptureResolutionSettings(opened);
                    return opened;
                }
                catch
                {
                    if (opened != null)
                        ReleaseCaptureHandle(opened);
                    throw;
                }
            }, CancellationToken.None);

            var delayTask = Task.Delay(TimeSpan.FromSeconds(openSec), linkedToken);
            var completed = await Task.WhenAny(openTask, delayTask).ConfigureAwait(false);
            if (completed != openTask)
            {
                _ = openTask.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        ReleaseCaptureHandle(t.Result);
                    else if (t.IsFaulted)
                        _ = t.Exception;
                }, TaskScheduler.Default);
                throw new OperationCanceledException(token);
            }

            _capture = await openTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AbortPendingOpen();
            _log.Error("preview", $"OpenCV open timed out ({CaptureLabel()})");
            return (false, 0, 0, 0);
        }
        catch (Exception ex)
        {
            AbortPendingOpen();
            _log.Error("preview", "OpenCV open failed", ex);
            return (false, 0, 0, 0);
        }

        if (token.IsCancellationRequested)
        {
            AbortPendingOpen();
            return (false, 0, 0, 0);
        }

        StabilizedCapture stabilized;
        try
        {
            stabilized = await StabilizeFrameSizeAsync(allowReopen: true, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AbortPendingOpen();
            _log.Error("preview", $"OpenCV stabilize cancelled ({CaptureLabel()})");
            return (false, 0, 0, 0);
        }
        _liveWidth = stabilized.Width;
        _liveHeight = stabilized.Height;
        _liveFps = stabilized.Fps;
        _log.Info("preview",
            $"OpenCV camera {_deviceIndex}: {stabilized.Width}x{stabilized.Height} @ {stabilized.Fps:F0} fps (matched={stabilized.ResolutionMatched})");
        return (true, stabilized.Width, stabilized.Height, stabilized.Fps);
    }

    private static void ApplyDriverCaptureProperties(VideoCapture capture) =>
        SafeSet(capture, VideoCaptureProperties.BufferSize, 1);

    public CameraFocusControlStatus ApplyFocusSettings(bool autoFocusEnabled, double? manualFocusValue, string reason)
    {
        var status = ApplyFocusSettings(_capture, autoFocusEnabled, manualFocusValue, reason);
        _lastFocusControlStatus = status;
        if (!string.IsNullOrWhiteSpace(status.FocusWarning))
            _log.Warn("camera", $"{CaptureLabel()} {status.FocusWarning}");
        else
            _log.Info("camera", $"{CaptureLabel()} focus {status.FocusControlMode} autoReadback={status.AutoFocusReadbackValue} manualReadback={status.ManualFocusReadbackValue}");
        return status;
    }

    private static CameraFocusControlStatus ApplyFocusSettings(
        VideoCapture? capture,
        bool autoFocusEnabled,
        double? manualFocusValue,
        string reason)
    {
        if (capture == null || !capture.IsOpened())
        {
            return CameraFocusControlStatus.NotAttempted(autoFocusEnabled) with
            {
                FocusWarning = "Focus warning: camera is not open; focus setting was not applied."
            };
        }

        const int capPropFocus = 28;
        const int capPropAutoFocus = 39;
        var autoAttempted = true;
        bool? autoConfirmed = null;
        var autoReadback = "unavailable";
        var manualReadback = "unavailable";
        bool? manualSupported = null;

        try
        {
            var autoSet = SafeSetWithResult(capture, (VideoCaptureProperties)capPropAutoFocus, autoFocusEnabled ? 1 : 0);
            var autoValue = SafeGetNullable(capture, (VideoCaptureProperties)capPropAutoFocus);
            if (autoValue.HasValue)
            {
                autoReadback = autoValue.Value.ToString("F3", CultureInfo.InvariantCulture);
                autoConfirmed = autoFocusEnabled ? autoValue.Value >= 0.5 : autoValue.Value < 0.5;
            }
            else if (autoSet)
            {
                autoConfirmed = null;
            }
        }
        catch
        {
            autoConfirmed = false;
        }

        if (!autoFocusEnabled && manualFocusValue.HasValue)
        {
            var clamped = Math.Clamp(manualFocusValue.Value, 0, 255);
            var manualSet = SafeSetWithResult(capture, (VideoCaptureProperties)capPropFocus, clamped);
            var manualValue = SafeGetNullable(capture, (VideoCaptureProperties)capPropFocus);
            manualSupported = manualSet || manualValue.HasValue;
            if (manualValue.HasValue)
                manualReadback = manualValue.Value.ToString("F3", CultureInfo.InvariantCulture);
        }
        else
        {
            var manualValue = SafeGetNullable(capture, (VideoCaptureProperties)capPropFocus);
            manualSupported = manualValue.HasValue ? true : null;
            if (manualValue.HasValue)
                manualReadback = manualValue.Value.ToString("F3", CultureInfo.InvariantCulture);
        }

        var warning = "";
        if (!autoFocusEnabled && autoConfirmed != true)
            warning = "Focus warning: autofocus OFF was requested but not confirmed. Use camera/vendor controls if focus hunting is visible.";

        return new CameraFocusControlStatus
        {
            AutoFocusRequested = autoFocusEnabled,
            AutoFocusApplyAttempted = autoAttempted,
            AutoFocusApplySucceeded = autoConfirmed,
            AutoFocusReadbackValue = autoReadback,
            ManualFocusSupported = manualSupported,
            ManualFocusRequestedValue = manualFocusValue,
            ManualFocusReadbackValue = manualReadback,
            FocusControlMode = autoFocusEnabled ? "autofocus" : manualFocusValue.HasValue ? "manual" : "autofocus_off_best_effort",
            FocusWarning = warning
        };
    }

    public CameraExposureControlStatus ApplyExposureSettings(bool autoExposureEnabled, double? manualExposureValue, bool disableLowLightCompensation, string reason)
    {
        var status = ApplyExposureSettings(_capture, autoExposureEnabled, manualExposureValue, disableLowLightCompensation, reason);
        _lastExposureControlStatus = status;
        if (!string.IsNullOrWhiteSpace(status.ExposureWarning))
            _log.Warn("camera", $"{CaptureLabel()} {status.ExposureWarning}");
        else
            _log.Info("camera", $"{CaptureLabel()} exposure {status.ExposureControlMode} autoReadback={status.AutoExposureReadbackValue} manualReadback={status.ManualExposureReadbackValue}");
        return status;
    }

    private static CameraExposureControlStatus ApplyExposureSettings(
        VideoCapture? capture,
        bool autoExposureEnabled,
        double? manualExposureValue,
        bool disableLowLightCompensation,
        string reason)
    {
        if (capture == null || !capture.IsOpened())
        {
            return CameraExposureControlStatus.NotAttempted(autoExposureEnabled, disableLowLightCompensation) with
            {
                ExposureWarning = "Exposure control was not confirmed. Use camera/vendor settings if blur or brightness changes are visible."
            };
        }

        const int capPropExposure = 15;
        const int capPropAutoExposure = 21;
        const int capPropBacklight = 32;

        var autoAttempted = true;
        bool? autoConfirmed = null;
        var autoReadback = "unavailable";
        var manualReadback = "unavailable";
        bool? manualSupported = null;
        bool? llcOffConfirmed = null;

        try
        {
            SafeSetWithResult(capture, (VideoCaptureProperties)capPropAutoExposure, autoExposureEnabled ? 1 : 0);
            var autoValue = SafeGetNullable(capture, (VideoCaptureProperties)capPropAutoExposure);
            if (autoValue.HasValue)
            {
                autoReadback = autoValue.Value.ToString("F3", CultureInfo.InvariantCulture);
                autoConfirmed = autoExposureEnabled ? autoValue.Value >= 0.5 : autoValue.Value < 0.5;
            }
        }
        catch
        {
            autoConfirmed = false;
        }

        if (!autoExposureEnabled && manualExposureValue.HasValue)
        {
            var clamped = Math.Clamp(manualExposureValue.Value, 0, 255);
            var manualSet = SafeSetWithResult(capture, (VideoCaptureProperties)capPropExposure, clamped);
            var manualValue = SafeGetNullable(capture, (VideoCaptureProperties)capPropExposure);
            manualSupported = manualSet || manualValue.HasValue;
            if (manualValue.HasValue)
                manualReadback = manualValue.Value.ToString("F3", CultureInfo.InvariantCulture);
        }
        else
        {
            var manualValue = SafeGetNullable(capture, (VideoCaptureProperties)capPropExposure);
            manualSupported = manualValue.HasValue ? true : null;
            if (manualValue.HasValue)
                manualReadback = manualValue.Value.ToString("F3", CultureInfo.InvariantCulture);
        }

        if (disableLowLightCompensation)
        {
            try
            {
                SafeSet(capture, (VideoCaptureProperties)capPropBacklight, 0);
                var backlightVal = SafeGetNullable(capture, (VideoCaptureProperties)capPropBacklight);
                llcOffConfirmed = backlightVal.HasValue ? backlightVal.Value < 0.5 : null;
            }
            catch
            {
                llcOffConfirmed = null;
            }
        }

        var warning = "";
        if (!autoExposureEnabled && autoConfirmed != true && manualSupported != true)
            warning = "Exposure control was not confirmed. Use camera/vendor settings if blur or brightness changes are visible.";

        return new CameraExposureControlStatus
        {
            AutoExposureRequested = autoExposureEnabled,
            AutoExposureApplyAttempted = autoAttempted,
            AutoExposureApplySucceeded = autoConfirmed,
            AutoExposureReadbackValue = autoReadback,
            ManualExposureSupported = manualSupported,
            ManualExposureRequestedValue = manualExposureValue,
            ManualExposureReadbackValue = manualReadback,
            LowLightCompensationOffRequested = disableLowLightCompensation,
            LowLightCompensationOffConfirmed = llcOffConfirmed,
            ExposureControlMode = autoExposureEnabled ? "auto_exposure" : manualExposureValue.HasValue ? "manual_exposure" : "auto_exposure_off_best_effort",
            ExposureWarning = warning
        };
    }

    private void ApplyCaptureResolutionSettings(VideoCapture capture)
    {
        if (_requestedWidth > 0 && _requestedHeight > 0)
        {
            var fullHd = CaptureResolutionHelper.IsFullHd(_requestedWidth, _requestedHeight);
            var skipWarmup = !string.IsNullOrWhiteSpace(_directShowOpenUri);
            if (fullHd && !skipWarmup)
            {
                if (_deviceIndex <= 0)
                    WarmUpUsbBeforeFullHd(capture, _requestedFps, _fastPreviewOpen);
                else
                    WarmUpSecondaryUsbBeforeFullHd(capture, _requestedFps, _fastPreviewOpen);
            }

            ApplyRequestedResolution(capture, _requestedWidth, _requestedHeight, _requestedFps);
        }
        else
            SafeSet(capture, VideoCaptureProperties.Fps, _requestedFps);
    }

    /// <summary>Later DirectShow indices: MJPEG + 640p reads before 1080p (multi-camera USB).</summary>
    private static void WarmUpSecondaryUsbBeforeFullHd(VideoCapture capture, double fps, bool fast)
    {
        TrySetMjpegFourcc(capture);
        ApplyRequestedResolution(capture, 640, 480, fps);
        using var frame = new Mat();
        var maxIter = fast ? 6 : 15;
        var sleepMs = fast ? 25 : 40;
        for (var i = 0; i < maxIter; i++)
        {
            if (capture.Read(frame) && !frame.Empty()) break;
            Thread.Sleep(sleepMs);
        }
    }

    /// <summary>USB webcams: negotiate at 640p first, then 1080p to avoid DShow access violations.</summary>
    private static void WarmUpUsbBeforeFullHd(VideoCapture capture, double fps, bool fast)
    {
        TrySetMjpegFourcc(capture);
        ApplyRequestedResolution(capture, 640, 480, fps);
        using var frame = new Mat();
        var maxIter = fast ? 6 : 20;
        var sleepMs = fast ? 25 : 50;
        for (var i = 0; i < maxIter; i++)
        {
            if (capture.Read(frame) && !frame.Empty())
                break;
            Thread.Sleep(sleepMs);
        }
        Thread.Sleep(fast ? 40 : 150);
    }

    private static void SafeSet(VideoCapture capture, VideoCaptureProperties prop, double value)
    {
        _ = SafeSetWithResult(capture, prop, value);
    }

    private static bool SafeSetWithResult(VideoCapture capture, VideoCaptureProperties prop, double value)
    {
        lock (DshowOpenGate)
        {
            try
            {
                if (capture == null || !capture.IsOpened()) return false;
                return capture.Set(prop, value);
            }
            catch (Exception)
            {
                /* some drivers throw on unsupported props */
                return false;
            }
        }
    }

    private static double? SafeGetNullable(VideoCapture capture, VideoCaptureProperties prop)
    {
        lock (DshowOpenGate)
        {
            try
            {
                if (capture == null || !capture.IsOpened()) return null;
                var value = capture.Get(prop);
                return double.IsFinite(value) ? value : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static void TrySetMjpegFourcc(VideoCapture capture)
    {
        try
        {
            SafeSet(capture, VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
        }
        catch
        {
            /* optional — USB webcams often need MJPEG for 720p/1080p at 30fps */
        }
    }

    private static void TrySetYuy2Fourcc(VideoCapture capture)
    {
        try
        {
            SafeSet(capture, VideoCaptureProperties.FourCC, VideoWriter.FourCC('Y', 'U', 'Y', '2'));
        }
        catch { /* fallback only */ }
    }

    /// <summary>Apply width/height/FPS with MJPEG-first ordering (1080p-friendly for USB cameras).</summary>
    private static void ApplyRequestedResolution(VideoCapture capture, int width, int height, double fps)
    {
        SafeSet(capture, VideoCaptureProperties.BufferSize, 1);
        TrySetMjpegFourcc(capture);

        var fullHd = CaptureResolutionHelper.IsFullHd(width, height);
        if (fullHd)
        {
            SafeSet(capture, VideoCaptureProperties.FrameHeight, height);
            SafeSet(capture, VideoCaptureProperties.FrameWidth, width);
            SafeSet(capture, VideoCaptureProperties.Fps, fps);
            FlushOneFrame(capture);
            TrySetMjpegFourcc(capture);
        }

        SafeSet(capture, VideoCaptureProperties.FrameWidth, width);
        SafeSet(capture, VideoCaptureProperties.FrameHeight, height);
        SafeSet(capture, VideoCaptureProperties.Fps, fps);
        FlushOneFrame(capture);

        if (fullHd)
        {
            TrySetMjpegFourcc(capture);
            SafeSet(capture, VideoCaptureProperties.FrameWidth, width);
            SafeSet(capture, VideoCaptureProperties.FrameHeight, height);
            SafeSet(capture, VideoCaptureProperties.Fps, fps);
        }
    }

    private static void FlushOneFrame(VideoCapture capture)
    {
        using var frame = new Mat();
        capture.Read(frame);
    }

    private static bool ResolutionMatchesRequest(int actualW, int actualH, int requestedW, int requestedH)
    {
        if (requestedW <= 0 || requestedH <= 0) return true;
        return Math.Abs(actualW - requestedW) <= 16 && Math.Abs(actualH - requestedH) <= 16;
    }

    private readonly record struct StabilizedCapture(int Width, int Height, double Fps, bool ResolutionMatched);

    private async Task<StabilizedCapture> StabilizeFrameSizeAsync(
        bool allowReopen = false,
        CancellationToken cancelToken = default)
    {
        var first = await Task.Run(() =>
        {
            lock (DshowOpenGate)
                return StabilizeFrameSizeSync(cancelToken);
        }, cancelToken).ConfigureAwait(false);
        if (first.ResolutionMatched || _requestedWidth <= 0 || _requestedHeight <= 0 || !allowReopen)
            return first;

        var attempts = !_fastPreviewOpen && CaptureResolutionHelper.IsFullHd(_requestedWidth, _requestedHeight) ? 2 : 1;
        var latest = first;
        for (var attempt = 0; attempt < attempts && !latest.ResolutionMatched; attempt++)
        {
            cancelToken.ThrowIfCancellationRequested();
            _log.Info("preview",
                $"OpenCV {_deviceIndex}: reopen {attempt + 1} after mismatch ({latest.Width}x{latest.Height} vs {_requestedWidth}x{_requestedHeight})");
            await ReopenCaptureDeviceAsync().ConfigureAwait(false);
            latest = await Task.Run(() =>
            {
                lock (DshowOpenGate)
                    return StabilizeFrameSizeSync(cancelToken);
            }, cancelToken).ConfigureAwait(false);
        }

        return latest;
    }

    private async Task ReopenCaptureDeviceAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);
        await Task.Run(() =>
        {
            _capture?.Dispose();
            _capture = OpenCaptureDevice();
            if (_capture == null || !_capture.IsOpened())
            {
                throw new InvalidOperationException($"OpenCV could not reopen camera ({CaptureLabel()})");
            }

            ApplyDriverCaptureProperties(_capture);
            if (_requestedWidth > 0 && _requestedHeight > 0)
                ApplyCaptureResolutionSettings(_capture);
            else
                SafeSet(_capture, VideoCaptureProperties.Fps, _requestedFps);
        }).ConfigureAwait(false);
    }

    private StabilizedCapture StabilizeFrameSizeSync(CancellationToken cancelToken = default)
    {
        if (_capture == null || !_capture.IsOpened())
            throw new InvalidOperationException("OpenCV camera not opened");

        using var frame = new Mat();
        var lastW = 0;
        var lastH = 0;

        var fullHd = CaptureResolutionHelper.IsFullHd(_requestedWidth, _requestedHeight);
        if (_requestedWidth > 0 && _requestedHeight > 0 && !_fastPreviewOpen)
            ApplyRequestedResolution(_capture, _requestedWidth, _requestedHeight, _requestedFps);

        var maxPasses = fullHd ? (_fastPreviewOpen ? 10 : 24) : (_fastPreviewOpen ? 8 : 16);
        var settleMs = fullHd ? (_fastPreviewOpen ? 35 : 80) : (_fastPreviewOpen ? 30 : 50);

        for (var pass = 0; pass < maxPasses; pass++)
        {
            cancelToken.ThrowIfCancellationRequested();
            if (!_capture.Read(frame) || frame.Empty())
            {
                Thread.Sleep(settleMs);
                continue;
            }

            lastW = frame.Width;
            lastH = frame.Height;
            if (ResolutionMatchesRequest(lastW, lastH, _requestedWidth, _requestedHeight))
                break;

            if (_requestedWidth > 0 && _requestedHeight > 0 && !fullHd && pass % 4 == 3)
                ApplyRequestedResolution(_capture, _requestedWidth, _requestedHeight, _requestedFps);

            Thread.Sleep(settleMs);
        }

        if (lastW <= 0 || lastH <= 0)
            throw new InvalidOperationException("OpenCV camera opened but no frames received");

        var matched = ResolutionMatchesRequest(lastW, lastH, _requestedWidth, _requestedHeight);
        LastResolutionMatched = matched;
        if (!matched && _requestedWidth > 0)
        {
            _log.Info("preview",
                $"OpenCV resolution mismatch: requested {_requestedWidth}x{_requestedHeight}, actual {lastW}x{lastH}");
        }

        var fps = _capture.Get(VideoCaptureProperties.Fps);
        if (fps <= 0 || double.IsNaN(fps)) fps = _requestedFps > 0 ? _requestedFps : 30;
        return new StabilizedCapture(lastW, lastH, fps, matched);
    }

    public Task StartAsync()
    {
        if (_capture == null || !_capture.IsOpened())
            throw new InvalidOperationException("OpenCV camera not opened");

        if (_cts != null && !_cts.IsCancellationRequested)
            return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _captureLoopTask = CaptureLoopAsync(_cts.Token);
        _log.Info("preview", $"OpenCV preview started (index {_deviceIndex})");
        return Task.CompletedTask;
    }

    public async Task<(bool Ok, int Width, int Height, double Fps)> ReapplyCaptureSettingsAsync()
    {
        if (_capture == null || !_capture.IsOpened())
            return (false, 0, 0, 0);

        var wasRecording = IsRecording;
        if (wasRecording)
            throw new InvalidOperationException("Cannot change capture settings while recording");

        await StopAsync().ConfigureAwait(false);
        await Task.Delay(80).ConfigureAwait(false);

        await Task.Run(() =>
        {
            ApplyCaptureResolutionSettings(_capture);
        }).ConfigureAwait(false);

        var stabilized = await StabilizeFrameSizeAsync(allowReopen: true).ConfigureAwait(false);
        _liveWidth = stabilized.Width;
        _liveHeight = stabilized.Height;
        _liveFps = stabilized.Fps;
        _log.Info("preview",
            $"OpenCV settings reapplied: {stabilized.Width}x{stabilized.Height} @ {stabilized.Fps:F0} fps (matched={stabilized.ResolutionMatched})");
        await StartAsync().ConfigureAwait(false);
        return (true, stabilized.Width, stabilized.Height, stabilized.Fps);
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        var previewIntervalMs = Math.Max(16, 1000 / _maxFps);
        var lastPreviewTick = Stopwatch.GetTimestamp();
        using var frame = new Mat();

        while (!token.IsCancellationRequested && _capture != null && _capture.IsOpened())
        {
            try
            {
                if (_capture.Read(frame) && !frame.Empty())
                {
                    _consecutiveReadFailures = 0;
                    var captureTicks = Stopwatch.GetTimestamp();
                    var captureUtc = DateTime.UtcNow;
                    var captureLocal = DateTime.Now;
                    _lastGoodFrameTick = captureTicks;
                    Interlocked.Increment(ref _captureFramesRead);
                    UpdateLiveDimensions(frame.Width, frame.Height);
                    LogPreviewQualityDiagnosticsOnce(frame);
                    ProcessMeasureSample();

                    if (IsRecording)
                    {
                        if (!_firstFrameReceivedSinceRecord)
                        {
                            _firstFrameReceivedSinceRecord = true;
                            _log.Info("recording", "OpenCV first frame received for recording");
                        }

                        TryEnqueueRecordFrame(frame, captureTicks, captureUtc, captureLocal);
                    }

                    var elapsedMs = Stopwatch.GetElapsedTime(lastPreviewTick).TotalMilliseconds;
                    var activePreviewIntervalMs = IsRecording
                        ? Math.Max(previewIntervalMs, 1000 / Math.Max(1, _recordingPreviewFpsCap))
                        : previewIntervalMs;
                    if (!_measuringForRecord && elapsedMs < activePreviewIntervalMs)
                    {
                        if (!IsRecording)
                            await Task.Delay(Math.Max(1, (int)(activePreviewIntervalMs - elapsedMs)), token).ConfigureAwait(false);
                        continue;
                    }

                    if (_measuringForRecord || elapsedMs >= activePreviewIntervalMs)
                    {
                        var source = CreatePreviewBitmap(frame);
                        source.Freeze();
                        Interlocked.Increment(ref _framesDelivered);
                        if (IsRecording)
                            Interlocked.Increment(ref _previewFramesRenderedDuringRecording);
                        FrameArrived?.Invoke(source);
                        lastPreviewTick = Stopwatch.GetTimestamp();
                    }
                }
                else
                {
                    CheckStreamLost();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error("preview", "OpenCV frame read failed", ex);
                CheckStreamLost();
            }

            if (IsRecording || _measuringForRecord)
                await Task.Yield();
            else
                await Task.Delay(1, token).ConfigureAwait(false);
        }
    }

    private void LogPreviewQualityDiagnosticsOnce(Mat frame)
    {
        if (Interlocked.CompareExchange(ref _previewQualityLogged, 1, 0) != 0)
            return;

        try
        {
            var fourcc = SafeGet(_capture, VideoCaptureProperties.FourCC);
            var convertRgb = TryGetConvertRgb(_capture);
            var backend = "OpenCV-DSHOW";
            var conversionPath = frame.Channels() switch
            {
                1 => "GRAY2BGRA -> WPF Bgra32",
                3 => "BGR2BGRA -> WPF Bgra32",
                4 => "BGRA copy -> WPF Bgra32",
                _ => $"unsupported channels={frame.Channels()}"
            };

            _log.Info("preview",
                $"PREVIEW_QUALITY slotIndex={_deviceIndex} backend={backend} requested={_requestedWidth}x{_requestedHeight}@{_requestedFps:F0} " +
                $"actual={_liveWidth}x{_liveHeight}@{_liveFps:F0} fourcc={DecodeFourCc(fourcc)}({fourcc:F0}) convertRgb={convertRgb} " +
                $"firstFrame={frame.Width}x{frame.Height} channels={frame.Channels()} type={frame.Type()} conversion=\"{conversionPath}\" render=\"BitmapSource Bgra32 frozen\"");
            AppDiagnosticLogger.Runtime(
                $"PREVIEW_QUALITY index={_deviceIndex} actual={_liveWidth}x{_liveHeight}@{_liveFps:F0} fourcc={DecodeFourCc(fourcc)} channels={frame.Channels()} conversion={conversionPath}");
        }
        catch
        {
            // Diagnostics must never affect capture.
        }
    }

    private static double SafeGet(VideoCapture? capture, VideoCaptureProperties prop)
    {
        try
        {
            if (capture == null || !capture.IsOpened()) return double.NaN;
            return capture.Get(prop);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static string TryGetConvertRgb(VideoCapture? capture)
    {
        try
        {
            if (capture == null || !capture.IsOpened()) return "-";
            return capture.Get(VideoCaptureProperties.ConvertRgb).ToString("F0");
        }
        catch
        {
            return "-";
        }
    }

    private static string DecodeFourCc(double value)
    {
        if (double.IsNaN(value) || value <= 0)
            return "-";

        var code = (int)value;
        Span<char> chars =
        [
            (char)(code & 0xFF),
            (char)((code >> 8) & 0xFF),
            (char)((code >> 16) & 0xFF),
            (char)((code >> 24) & 0xFF)
        ];
        return new string(chars).Trim('\0', ' ');
    }

    private void CheckStreamLost()
    {
        if (_streamLostSignaled != 0 || _cts?.IsCancellationRequested == true) return;

        _consecutiveReadFailures++;
        var noFrameMs = Stopwatch.GetElapsedTime(_lastGoodFrameTick).TotalMilliseconds;
        if (_consecutiveReadFailures < StreamLostConsecutiveFailures && noFrameMs < StreamLostNoFrameMs)
            return;

        if (Interlocked.CompareExchange(ref _streamLostSignaled, 1, 0) != 0)
            return;

        _log.Warn("preview", $"OpenCV stream lost (failures={_consecutiveReadFailures}, noFrameMs={noFrameMs:F0}, index {_deviceIndex})");
        AppDiagnosticLogger.Runtime($"OpenCV stream lost index={_deviceIndex} failures={_consecutiveReadFailures} noFrameMs={noFrameMs:F0}");
        try
        {
            // StreamLost may be handled in UI code; marshal invocation to app dispatcher to avoid UI thread access violations.
            var handler = StreamLost;
            if (handler != null)
            {
                try
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { handler(); } catch { }
                    }));
                }
                catch
                {
                    // fallback: invoke directly (best-effort)
                    try { handler(); } catch { }
                }
            }
        }
        catch { }
    }

    private void ProcessMeasureSample()
    {
        if (!_measuringForRecord) return;

        _measureFrameCount++;
        var elapsedMs = Stopwatch.GetElapsedTime(_measureStartTick).TotalMilliseconds;
        if (elapsedMs < MeasureCaptureMs) return;

        _measuringForRecord = false;
        var elapsedSec = Math.Max(0.001, elapsedMs / 1000.0);
        var measured = _measureFrameCount / elapsedSec;
        if (measured < 5)
            measured = Math.Clamp(_liveFps > 0 ? _liveFps : 30, 10, 60);

        // Measure completion can signal non-UI logic; ensure continuations that touch UI marshal themselves.
        _measureTcs?.TrySetResult(measured);
        _log.Info("recording", $"OpenCV measured capture rate {measured:F1} fps ({_measureFrameCount} frames / {elapsedMs:F0}ms)");
    }

    private void UpdateLiveDimensions(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        _liveWidth = w;
        _liveHeight = h;
    }

    private void TryEnqueueRecordFrame(Mat frame, long captureTicks, DateTime captureUtc, DateTime captureLocal)
    {
        if (!IsRecording || _recordChannel == null) return;

        _timingMonitor?.NotifyFrameCaptured();
        var capacity = CurrentRecordQueueCapacity;
        if (Volatile.Read(ref _recordQueueDepth) >= capacity)
        {
            RecordWriterQueueDrop();
            return;
        }

        var previousCaptureTicks = Volatile.Read(ref _recordLastCaptureTicks);
        var frameIndex = Interlocked.Read(ref _recordEnqueuedFrames);
        var expectedIntervalMs = _lastRecordWriterFps > 0 ? 1000.0 / _lastRecordWriterFps : 0;
        var deltaMs = previousCaptureTicks > 0
            ? (captureTicks - previousCaptureTicks) * 1000.0 / Stopwatch.Frequency
            : 0;
        if (previousCaptureTicks > 0)
            RecordDiagnosticCaptureInterval(deltaMs, expectedIntervalMs);
        var packet = new RecordedFramePacket(
            frame.Clone(),
            frameIndex,
            captureUtc,
            captureLocal,
            captureTicks,
            captureTicks / (double)Stopwatch.Frequency,
            deltaMs,
            expectedIntervalMs,
            previousCaptureTicks > 0 && expectedIntervalMs > 0 ? deltaMs - expectedIntervalMs : 0,
            Volatile.Read(ref _recordQueueDepth),
            _recordDiagnosticSlotName,
            RecordingTimingModes.OriginalCapture);

        if (!_recordChannel.Writer.TryWrite(packet))
        {
            packet.Frame.Dispose();
            RecordWriterQueueDrop();
            return;
        }

        Interlocked.Exchange(ref _recordLastCaptureTicks, captureTicks);
        Interlocked.Increment(ref _recordEnqueuedFrames);
        var depth = Interlocked.Increment(ref _recordQueueDepth);
        UpdateMaxQueueDepth(depth);
    }

    private void RecordWriterQueueDrop()
    {
        Interlocked.Increment(ref _writerQueueDrops);
        Interlocked.Increment(ref _writerQueueFullCount);
        CaptureDropTimestampSample();
        var drops = Interlocked.Read(ref _writerQueueDrops);
        if (drops == 1 || drops % 100 == 0)
        {
            _log.Warn("recording",
                $"{_recordDiagnosticSlotName} writer queue full; drops={drops}, depth={Volatile.Read(ref _recordQueueDepth)}/{CurrentRecordQueueCapacity}");
            AppDiagnosticLogger.Recording(
                $"WRITER_QUEUE_DROP {_recordDiagnosticSlotName} drops={drops} depth={Volatile.Read(ref _recordQueueDepth)} capacity={CurrentRecordQueueCapacity}");
        }
        WriterQueueDropDetected?.Invoke(drops);
    }

    private void CaptureDropTimestampSample()
    {
        try
        {
            var start = _recordStartTicks;
            if (start <= 0)
                return;

            var relativeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            lock (_dropSampleLock)
            {
                if (_droppedFrameRelativeMs.Count < DroppedFrameTimestampSampleLimit)
                    _droppedFrameRelativeMs.Add(relativeMs);
            }
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private void UpdateMaxQueueDepth(int depth)
    {
        while (true)
        {
            var current = Volatile.Read(ref _recordQueueDepthMax);
            if (depth <= current)
                return;
            if (Interlocked.CompareExchange(ref _recordQueueDepthMax, depth, current) == current)
                return;
        }
    }

    private BitmapSource CreatePreviewBitmap(Mat frame)
    {
        var w = frame.Width;
        var h = frame.Height;
        if (w <= MaxPreviewDisplayWidth)
            return MatToBitmapSource(frame);

        var scale = MaxPreviewDisplayWidth / (double)w;
        var pw = MaxPreviewDisplayWidth;
        var ph = Math.Max(1, (int)Math.Round(h * scale));
        using var scaled = new Mat();
        Cv2.Resize(frame, scaled, new OpenCvSharp.Size(pw, ph));
        return MatToBitmapSource(scaled);
    }

    public Task StartExperimentRecordingAsync(
        string filePath,
        int width,
        int height,
        ExperimentSessionOptions session,
        AppConfig config,
        string? diagnosticSlotName = null,
        Task? recordingStartGate = null,
        TaskCompletionSource<bool>? writerReady = null)
    {
        _experiment = session;
        _appConfigForExperiment = config;
        ResetPerSessionRecordingMetadata();
        _timingMonitor = new FrameTimingMonitor(session.TargetFps, constantFrameCountMode: false);
        _timingMonitor.Begin();
        _maxFps = Math.Min(_maxFps, 12);
        return StartRecordingAsync(
            filePath,
            width,
            height,
            session.TargetFps,
            session.TargetFps,
            useExperimentWriterFps: true,
            diagnosticSlotName,
            recordingStartGate,
            writerReady);
    }

    public async Task StartRecordingAsync(string filePath, int width, int height, double fps, string? diagnosticSlotName = null) =>
        await StartRecordingAsync(filePath, width, height, fps, fps, useExperimentWriterFps: false, diagnosticSlotName, null, null);

    public async Task StartRecordingAsync(
        string filePath,
        int width,
        int height,
        double requestedFps,
        double selectedDeviceFps,
        string? diagnosticSlotName = null,
        Task? recordingStartGate = null,
        TaskCompletionSource<bool>? writerReady = null) =>
        await StartRecordingAsync(filePath, width, height, requestedFps, selectedDeviceFps, useExperimentWriterFps: false, diagnosticSlotName, recordingStartGate, writerReady);

    private async Task StartRecordingAsync(
        string filePath,
        int width,
        int height,
        double fps,
        double selectedDeviceFps,
        bool useExperimentWriterFps,
        string? diagnosticSlotName,
        Task? recordingStartGate,
        TaskCompletionSource<bool>? writerReady)
    {
        if (_capture == null || !_capture.IsOpened())
            throw new InvalidOperationException("OpenCV camera not open for recording");

        if (_cts == null)
            throw new InvalidOperationException("OpenCV preview loop not running");

        StopRecordingInternal();
        ResetPerSessionRecordingMetadata();
        _recordRequestedStartTicks = Stopwatch.GetTimestamp();
        _recordRequestedStartUtc = DateTime.UtcNow;
        _recordRequestedStartLocal = DateTime.Now;

        var rw = _liveWidth > 0 ? _liveWidth : width;
        var rh = _liveHeight > 0 ? _liveHeight : height;
        if (rw <= 0 || rh <= 0)
            throw new InvalidOperationException("No live frame size for recording");

        _recordTargetWidth = rw;
        _recordTargetHeight = rh;
        _recordQueueCapacity = ResolveRecordQueueCapacity(rw, rh);
        _recordingPreviewFpsCap = ResolveRecordingPreviewFpsCap(rw, rh);

        double recordFps;
        if (useExperimentWriterFps)
        {
            recordFps = Math.Clamp(fps, 10, 60);
        }
        else if (selectedDeviceFps > 0)
        {
            recordFps = Math.Clamp(selectedDeviceFps, 5, 60);
        }
        else
        {
            var measuredFps = await MeasureCaptureFpsAsync().ConfigureAwait(false);
            recordFps = measuredFps > 0 ? Math.Clamp(measuredFps, 5, 60) : Math.Clamp(fps > 0 ? fps : _liveFps, 5, 60);
        }

        _lastRecordWriterFps = recordFps;
        _timingMonitor = new FrameTimingMonitor(recordFps, constantFrameCountMode: false);
        _timingMonitor.Begin();
        _recordDiagnosticSlotName = string.IsNullOrWhiteSpace(diagnosticSlotName) ? "opencv" : diagnosticSlotName;
        AppDiagnosticLogger.Recording(
            $"RECORDING_WRITER_OPEN_START {_recordDiagnosticSlotName} path={filePath} fourcc=mp4v fps={recordFps:F3} size={rw}x{rh}");

        await Task.Run(() =>
        {
            var fourcc = FourCC.FromString("mp4v");
            var writer = new VideoWriter(filePath, fourcc, recordFps, new OpenCvSharp.Size(rw, rh));
            if (!writer.IsOpened())
            {
                writer.Dispose();
                throw new InvalidOperationException($"OpenCV VideoWriter could not create {filePath}");
            }

            _recordResizeBuffer?.Dispose();
            _recordResizeBuffer = null;

            _recordChannel = Channel.CreateBounded<RecordedFramePacket>(new BoundedChannelOptions(_recordQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

            lock (_writerLock)
            {
                _writer = writer;
                _writerOpened = writer.IsOpened();
                _recordedFrames = 0;
                _recordStartTicks = 0;
                _recordFirstFrameTicks = 0;
                _recordLastFrameTicks = 0;
                _recordLastCaptureTicks = 0;
                _recordLastWrittenCaptureTicks = 0;
                _recordStopRequestedTicks = 0;
                _recordStopTicks = 0;
                _cameraRecordingStartTicks = 0;
                _writerClosedTicks = 0;
                _writerQueueDrops = 0;
                _writerQueueFullCount = 0;
                _recordQueueDepth = 0;
                _recordQueueDepthMax = 0;
                _writerFramesDequeued = 0;
                _writerWriteCount = 0;
                _writerWriteTicksTotal = 0;
                _writerWriteTicksMax = 0;
                _writerLoopStartTicks = 0;
                _writerLoopStopTicks = 0;
                _previewFramesRenderedDuringRecording = 0;
                _recordEnqueuedFrames = 0;
                ResetDiagnosticIntervalStats();
                lock (_frameTimestampLock)
                    _frameTimestampRecords.Clear();
                _lastFrameTimestampCsvResult = FrameTimestampCsvResult.NotWritten;
                lock (_dropSampleLock)
                    _droppedFrameRelativeMs.Clear();
                _firstFrameReceivedSinceRecord = false;
                _firstFrameWrittenSinceRecord = false;
                _firstFrameWrittenTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _recordStartCaptureFrames = Interlocked.Read(ref _captureFramesRead);
            }

            _recordPumpTask = Task.Run(() => RecordPumpAsync(_recordChannel.Reader));
            writerReady?.TrySetResult(true);
            AppDiagnosticLogger.Recording($"RECORDING_PUMP_STARTED {_recordDiagnosticSlotName}");
            _log.Info("recording",
                $"OpenCV writer opened={_writerOpened} writing {rw}x{rh} @ {recordFps:F3} -> {filePath}");
        }).ConfigureAwait(false);

        if (recordingStartGate != null)
        {
            AppDiagnosticLogger.Recording($"RECORDING_WAITING_FOR_SYNC_RELEASE {_recordDiagnosticSlotName}");
            await recordingStartGate.ConfigureAwait(false);
        }

        lock (_writerLock)
        {
            _recordStartCaptureFrames = Interlocked.Read(ref _captureFramesRead);
            _cameraRecordingStartTicks = Stopwatch.GetTimestamp();
            _cameraRecordingStartUtc = DateTime.UtcNow;
            _cameraRecordingStartLocal = DateTime.Now;
            IsRecording = true;
        }

        AppDiagnosticLogger.Recording($"RECORDING_CAPTURE_RELEASED {_recordDiagnosticSlotName}");
        AppDiagnosticLogger.Recording(
            $"RECORDING_WRITER_OPEN_END {_recordDiagnosticSlotName} opened={_writerOpened} path={filePath}");
    }

    public async Task<bool> WaitForFirstFrameWrittenAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_firstFrameWrittenSinceRecord)
            return true;

        var tcs = _firstFrameWrittenTcs;
        if (tcs == null)
            return _firstFrameWrittenSinceRecord;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return _firstFrameWrittenSinceRecord;
        }
    }

    private async Task<double> MeasureCaptureFpsAsync()
    {
        var fallback = Math.Clamp(_liveFps > 0 ? _liveFps : 30, 10, 60);
        _measureTcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        _measureFrameCount = 0;
        _measureStartTick = Stopwatch.GetTimestamp();
        _measuringForRecord = true;

        try
        {
            var measureTask = _measureTcs.Task;
            var completed = await Task.WhenAny(measureTask, Task.Delay(3000)).ConfigureAwait(false);
            return completed == measureTask ? await measureTask.ConfigureAwait(false) : fallback;
        }
        finally
        {
            _measuringForRecord = false;
            _measureTcs = null;
        }
    }

    private async Task RecordPumpAsync(ChannelReader<RecordedFramePacket> reader)
    {
        var originalPriority = Thread.CurrentThread.Priority;
        try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { }
        _writerLoopStartTicks = Stopwatch.GetTimestamp();
        AppDiagnosticLogger.Recording($"RECORDING_PUMP_LOOP_START {_recordDiagnosticSlotName}");
        try
        {
            await foreach (var packet in reader.ReadAllAsync().ConfigureAwait(false))
            {
                Interlocked.Increment(ref _writerFramesDequeued);
                var depthAtWrite = Interlocked.Decrement(ref _recordQueueDepth);
                try
                {
                    WriteFrameToDisk(packet, depthAtWrite);
                }
                finally
                {
                    packet.Frame.Dispose();
                }
            }
        }
        catch (ChannelClosedException)
        {
            // normal on stop
        }
        catch (Exception ex)
        {
            _log.Error("recording", "OpenCV record pump failed", ex);
        }
        finally
        {
            _writerLoopStopTicks = Stopwatch.GetTimestamp();
            AppDiagnosticLogger.Recording(
                $"RECORDING_PUMP_LOOP_STOP {_recordDiagnosticSlotName} dequeued={Interlocked.Read(ref _writerFramesDequeued)} written={Interlocked.Read(ref _recordedFrames)} drops={Interlocked.Read(ref _writerQueueDrops)}");
            try { Thread.CurrentThread.Priority = originalPriority; } catch { }
        }
    }

    private void WriteFrameToDisk(RecordedFramePacket packet, int writerQueueDepthAtWrite)
    {
        lock (_writerLock)
        {
            if (_writer == null || !_writer.IsOpened()) return;

            if (_recordStartTicks == 0)
            {
                _recordStartTicks = packet.CaptureTicks;
                _recordStartUtc = packet.CaptureUtcTime;
                _recordStartLocal = packet.CaptureLocalTime;
                _recordFirstFrameTicks = _recordStartTicks;
                _firstFrameUtc = packet.CaptureUtcTime;
                _firstFrameLocal = packet.CaptureLocalTime;
            }
            _lastFrameUtc = packet.CaptureUtcTime;
            _lastFrameLocal = packet.CaptureLocalTime;
            _recordLastWrittenCaptureTicks = packet.CaptureTicks;

            var writeMonotonicSec = WriteMatFrame(packet.Frame, isDuplicate: false);
            AddFrameTimestampRecord(packet, writeMonotonicSec, writerQueueDepthAtWrite);

            if (!_firstFrameWrittenSinceRecord)
            {
                _firstFrameWrittenSinceRecord = true;
                _firstFrameWrittenTcs?.TrySetResult(true);
                _log.Info("recording", "OpenCV first frame written to disk");
                AppDiagnosticLogger.Recording($"FIRST_FRAME_WRITTEN {_recordDiagnosticSlotName}");
            }
        }
    }

    private double WriteMatFrame(Mat frame, bool isDuplicate)
    {
        var writeStart = Stopwatch.GetTimestamp();
        if (frame.Width == _recordTargetWidth && frame.Height == _recordTargetHeight)
        {
            _writer!.Write(frame);
        }
        else
        {
            _recordResizeBuffer ??= new Mat();
            Cv2.Resize(frame, _recordResizeBuffer, new OpenCvSharp.Size(_recordTargetWidth, _recordTargetHeight));
            _writer!.Write(_recordResizeBuffer);
        }
        var now = Stopwatch.GetTimestamp();
        RecordWriteDuration(now - writeStart);

        _recordLastFrameTicks = now;
        _recordedFrames++;
        _timingMonitor?.NotifyFrameWritten(isDuplicate: isDuplicate);
        return now / (double)Stopwatch.Frequency;
    }

    private void AddFrameTimestampRecord(
        RecordedFramePacket packet,
        double writeMonotonicSec,
        int writerQueueDepthAtWrite)
    {
        var record = new FrameTimestampRecord(
            packet.FrameIndex,
            packet.CaptureUtcTime,
            packet.CaptureLocalTime,
            packet.CaptureMonotonicSec,
            writeMonotonicSec,
            packet.DeltaFromPreviousCaptureMs,
            packet.ExpectedIntervalMs,
            packet.IntervalErrorMs,
            IsOriginalFrame: true,
            IsDuplicateFrame: false,
            IsPlaceholderFrame: false,
            packet.WriterQueueDepthAtEnqueue,
            Math.Max(0, writerQueueDepthAtWrite),
            packet.SourceCameraName,
            packet.RecordingTimingMode);

        lock (_frameTimestampLock)
            _frameTimestampRecords.Add(record);
    }

    private FrameTimestampCsvResult WriteFrameTimestampCsv(string cameraSlot, string outputFilePath)
    {
        var folder = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrWhiteSpace(folder))
            return FrameTimestampCsvResult.NotWritten;

        var safeSlot = string.IsNullOrWhiteSpace(cameraSlot)
            ? "camera"
            : string.Concat(cameraSlot.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'));
        if (string.IsNullOrWhiteSpace(safeSlot))
            safeSlot = "camera";

        var csvPath = Path.Combine(folder, $"{safeSlot}_frame_timestamps.csv");
        List<FrameTimestampRecord> records;
        lock (_frameTimestampLock)
            records = _frameTimestampRecords.ToList();

        if (records.Count == 0)
            return new FrameTimestampCsvResult(csvPath, false, 0, null, null, 0, 0, 0);

        try
        {
            var sb = new StringBuilder(records.Count * 180);
            sb.AppendLine("frameIndex,captureUtcTime,captureLocalTime,captureMonotonicSec,writeMonotonicSec,deltaFromPreviousCaptureMs,expectedIntervalMs,intervalErrorMs,isOriginalFrame,isDuplicateFrame,isPlaceholderFrame,writerQueueDepthAtEnqueue,writerQueueDepthAtWrite,sourceCameraName,recordingTimingMode");

            foreach (var r in records)
                AppendCsvRow(sb, r);

            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            var first = records[0];
            var last = records[^1];
            return new FrameTimestampCsvResult(
                csvPath,
                Written: true,
                RowCount: records.Count,
                first.CaptureUtcTime,
                last.CaptureUtcTime,
                first.CaptureMonotonicSec,
                last.CaptureMonotonicSec,
                Math.Max(0, last.CaptureMonotonicSec - first.CaptureMonotonicSec));
        }
        catch (Exception ex)
        {
            _log.Error("recording", $"Could not write frame timestamp CSV: {csvPath}", ex);
            return new FrameTimestampCsvResult(csvPath, false, 0, null, null, 0, 0, 0);
        }
    }

    private static void AppendCsvRow(StringBuilder sb, FrameTimestampRecord r)
    {
        sb.Append(r.FrameIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
        AppendCsvValue(sb, r.CaptureUtcTime.ToString("O", CultureInfo.InvariantCulture)).Append(',');
        AppendCsvValue(sb, r.CaptureLocalTime.ToString("O", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.CaptureMonotonicSec.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.WriteMonotonicSec.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.DeltaFromPreviousCaptureMs.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.ExpectedIntervalMs.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.IntervalErrorMs.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.IsOriginalFrame ? "true" : "false").Append(',');
        sb.Append(r.IsDuplicateFrame ? "true" : "false").Append(',');
        sb.Append(r.IsPlaceholderFrame ? "true" : "false").Append(',');
        sb.Append(r.WriterQueueDepthAtEnqueue.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.WriterQueueDepthAtWrite.ToString(CultureInfo.InvariantCulture)).Append(',');
        AppendCsvValue(sb, r.SourceCameraName).Append(',');
        AppendCsvValue(sb, r.RecordingTimingMode);
        sb.AppendLine();
    }

    private static StringBuilder AppendCsvValue(StringBuilder sb, string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return sb.Append(value);

        sb.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
                sb.Append("\"\"");
            else
                sb.Append(ch);
        }

        return sb.Append('"');
    }

    private FrameTimestampAnalysis AnalyzeFrameTimestamps(double requestedFps)
    {
        List<FrameTimestampRecord> records;
        lock (_frameTimestampLock)
            records = _frameTimestampRecords.ToList();

        if (records.Count < 2)
            return FrameTimestampAnalysis.Missing(requestedFps);

        records.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        var intervals = new List<double>(records.Count - 1);
        for (var i = 1; i < records.Count; i++)
        {
            var deltaMs = (records[i].CaptureMonotonicSec - records[i - 1].CaptureMonotonicSec) * 1000.0;
            if (deltaMs > 0)
                intervals.Add(deltaMs);
        }

        if (intervals.Count == 0)
            return FrameTimestampAnalysis.Missing(requestedFps);

        intervals.Sort();
        var mean = intervals.Average();
        var median = PercentileSorted(intervals, 0.50);
        var min = intervals[0];
        var max = intervals[^1];
        var p95 = PercentileSorted(intervals, 0.95);
        var p99 = PercentileSorted(intervals, 0.99);
        var variance = intervals.Sum(v => Math.Pow(v - mean, 2)) / intervals.Count;
        var std = Math.Sqrt(variance);
        var first = records[0];
        var last = records[^1];
        var firstToLastSec = last.CaptureMonotonicSec - first.CaptureMonotonicSec;
        var measuredFirstLast = firstToLastSec > 0 ? (records.Count - 1) / firstToLastSec : 0;
        var measuredMean = mean > 0 ? 1000.0 / mean : 0;
        var stableNativeFps = measuredFirstLast > 0 ? measuredFirstLast : measuredMean;
        var expected = stableNativeFps > 0 ? 1000.0 / stableNativeFps : mean;
        var requestedExpected = requestedFps > 0 ? 1000.0 / requestedFps : 0;
        var meanError = expected > 0 ? mean - expected : 0;
        var absMeanError = Math.Abs(meanError);
        var longGaps = intervals.LongCount(v => expected > 0 && v > expected * 1.75);
        var severeLongGaps = intervals.LongCount(v => expected > 0 && v > expected * 2.5);
        var shortGaps = intervals.LongCount(v => expected > 0 && v < expected * 0.50);
        var jitterScore = Math.Sqrt((std * std + Math.Max(0, p99 - expected) * Math.Max(0, p99 - expected)) / 2.0);
        var grade = ClassifyFpsStability(records.Count, intervals.Count, std, p99, expected, longGaps, severeLongGaps, shortGaps);

        return new FrameTimestampAnalysis(
            measuredFirstLast,
            measuredMean,
            mean,
            median,
            std,
            min,
            max,
            p95,
            p99,
            expected,
            requestedExpected,
            meanError,
            absMeanError,
            longGaps,
            shortGaps,
            severeLongGaps,
            jitterScore,
            grade,
            intervals.Count,
            "");
    }

    private static double PercentileSorted(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0;
        if (sorted.Count == 1)
            return sorted[0];
        var position = Math.Clamp(percentile, 0, 1) * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        var weight = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }

    private static string ClassifyFpsStability(
        int records,
        int intervals,
        double stdMs,
        double p99Ms,
        double expectedMs,
        long longGaps,
        long severeLongGaps,
        long shortGaps)
    {
        if (records < 2 || intervals < 1 || expectedMs <= 0)
            return "Failed";

        var longGapRate = intervals > 0 ? longGaps / (double)intervals : 1;
        var severeRate = intervals > 0 ? severeLongGaps / (double)intervals : 1;
        var shortRate = intervals > 0 ? shortGaps / (double)intervals : 1;
        var p99Ratio = p99Ms / expectedMs;

        if (severeLongGaps > 0 && severeRate >= 0.001 || longGapRate >= 0.02 || shortRate >= 0.02)
            return "Unstable";
        if (severeLongGaps > 0 || longGapRate >= 0.005 || p99Ratio > 1.75 || stdMs > expectedMs * 0.25)
            return "Borderline";
        if (stdMs <= expectedMs * 0.08 && p99Ratio <= 1.25 && severeLongGaps == 0)
            return "Excellent";
        return "Good";
    }

    private void RecordWriteDuration(long elapsedTicks)
    {
        Interlocked.Increment(ref _writerWriteCount);
        Interlocked.Add(ref _writerWriteTicksTotal, elapsedTicks);
        while (true)
        {
            var current = Interlocked.Read(ref _writerWriteTicksMax);
            if (elapsedTicks <= current)
                return;
            if (Interlocked.CompareExchange(ref _writerWriteTicksMax, elapsedTicks, current) == current)
                return;
        }
    }

    private void ResetDiagnosticIntervalStats()
    {
        lock (_diagnosticIntervalLock)
        {
            _diagnosticIntervalCount = 0;
            _diagnosticIntervalMeanMs = 0;
            _diagnosticIntervalM2Ms = 0;
            _diagnosticIntervalMinMs = 0;
            _diagnosticIntervalMaxMs = 0;
            _diagnosticLongGapCount = 0;
            _diagnosticSevereGapCount = 0;
            _diagnosticShortIntervalCount = 0;
        }
    }

    private void RecordDiagnosticCaptureInterval(double deltaMs, double expectedIntervalMs)
    {
        if (deltaMs <= 0)
            return;

        lock (_diagnosticIntervalLock)
        {
            _diagnosticIntervalCount++;
            if (_diagnosticIntervalCount == 1)
            {
                _diagnosticIntervalMeanMs = deltaMs;
                _diagnosticIntervalM2Ms = 0;
                _diagnosticIntervalMinMs = deltaMs;
                _diagnosticIntervalMaxMs = deltaMs;
            }
            else
            {
                var diff = deltaMs - _diagnosticIntervalMeanMs;
                _diagnosticIntervalMeanMs += diff / _diagnosticIntervalCount;
                _diagnosticIntervalM2Ms += diff * (deltaMs - _diagnosticIntervalMeanMs);
                _diagnosticIntervalMinMs = Math.Min(_diagnosticIntervalMinMs, deltaMs);
                _diagnosticIntervalMaxMs = Math.Max(_diagnosticIntervalMaxMs, deltaMs);
            }

            if (expectedIntervalMs > 0)
            {
                if (deltaMs > expectedIntervalMs * 1.75)
                    _diagnosticLongGapCount++;
                if (deltaMs > expectedIntervalMs * 2.5)
                    _diagnosticSevereGapCount++;
                if (deltaMs < expectedIntervalMs * 0.5)
                    _diagnosticShortIntervalCount++;
            }
        }
    }

    private void ResetPerSessionRecordingMetadata()
    {
        _lastTimingSummary = null;
        _lastFrozenTimingSummary = null;
        _lastCaptureTimingSnapshot = null;
        _lastExperimentVerdict = "";
    }

    private void FreezeTimingBeforeCleanup(long recordedFrames, double durationSec)
    {
        if (_timingMonitor == null)
            return;

        _lastCaptureTimingSnapshot = _timingMonitor.FreezeCaptureTiming();

        if (_experiment?.Enabled != true)
            _lastFrozenTimingSummary = _timingMonitor.BuildSummary(recordedFrames, durationSec);

        _timingMonitor = null;
    }

    public RecordingCameraStats BuildRecordingStats(
        string cameraSlot,
        string cameraDeviceName,
        string outputFilePath,
        double requestedFps,
        double selectedDeviceFps,
        string codec,
        string container)
    {
        var frames = _lastRecordedFrameCount;
        var rw = _recordTargetWidth;
        var rh = _recordTargetHeight;
        var startTicks = _recordStartTicks;
        var stopTicks = _recordStopTicks > 0 ? _recordStopTicks : Stopwatch.GetTimestamp();
        var freq = (double)Stopwatch.Frequency;
        var startMono = startTicks > 0 ? startTicks / freq : 0;
        var stopMono = stopTicks / freq;
        var wallDurationSec = startTicks > 0 ? (stopTicks - startTicks) / freq : 0;
        var firstFrameMono = _recordFirstFrameTicks > 0 ? _recordFirstFrameTicks / freq : startMono;
        var lastFrameCaptureTicks = _recordLastWrittenCaptureTicks > 0 ? _recordLastWrittenCaptureTicks : _recordLastCaptureTicks;
        var lastFrameMono = lastFrameCaptureTicks > 0 ? lastFrameCaptureTicks / freq : stopMono;
        var requestedStartMono = _recordRequestedStartTicks > 0 ? _recordRequestedStartTicks / freq : 0;
        var cameraStartMono = _cameraRecordingStartTicks > 0 ? _cameraRecordingStartTicks / freq : startMono;
        var cameraStopMono = _recordStopRequestedTicks > 0 ? _recordStopRequestedTicks / freq : stopMono;
        var writerClosedTicks = _writerClosedTicks > 0 ? _writerClosedTicks : stopTicks;
        var writerClosedMono = writerClosedTicks / freq;
        var writerFps = _lastRecordWriterFps;
        var writeCount = Interlocked.Read(ref _writerWriteCount);
        var avgWriteMs = writeCount > 0
            ? (Interlocked.Read(ref _writerWriteTicksTotal) * 1000.0 / Stopwatch.Frequency) / writeCount
            : 0;
        var maxWriteMs = Interlocked.Read(ref _writerWriteTicksMax) * 1000.0 / Stopwatch.Frequency;
        var loopStartMono = _writerLoopStartTicks > 0 ? _writerLoopStartTicks / freq : 0;
        var loopStopMono = _writerLoopStopTicks > 0 ? _writerLoopStopTicks / freq : 0;
        string dropSamples;
        lock (_dropSampleLock)
            dropSamples = string.Join(", ", _droppedFrameRelativeMs.Select(ms => $"{ms:F1}ms"));
        long fileSizeBytes = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(outputFilePath) && File.Exists(outputFilePath))
                fileSizeBytes = new FileInfo(outputFilePath).Length;
        }
        catch
        {
            fileSizeBytes = 0;
        }
        var bitrateMbps = wallDurationSec > 0 && fileSizeBytes > 0
            ? fileSizeBytes * 8.0 / wallDurationSec / 1_000_000.0
            : 0;
        var frameBasedDuration = RecordingTimingMetrics.ComputeFrameBasedDurationSeconds(frames, writerFps);
        if (frameBasedDuration <= 0)
            frameBasedDuration = wallDurationSec;

        var summary = _experiment?.Enabled == true && _lastTimingSummary != null
            ? _lastTimingSummary
            : _lastFrozenTimingSummary;

        var captureTiming = _lastCaptureTimingSnapshot;
        var framesCaptured = captureTiming?.FramesCaptured > 0
            ? captureTiming.FramesCaptured
            : summary?.FramesCaptured ?? frames;
        var measuredCameraFps = RecordingTimingMetrics.ComputeMeasuredCameraFps(framesCaptured, wallDurationSec);
        if (measuredCameraFps <= 0 && wallDurationSec > 0.05)
            measuredCameraFps = frames / wallDurationSec;

        var effectivePlaybackFps = RecordingTimingMetrics.ComputeEffectivePlaybackFps(frames, frameBasedDuration);
        if (effectivePlaybackFps <= 0 && writerFps > 0)
            effectivePlaybackFps = writerFps;

        var containerDurationEstimate = frameBasedDuration;
        var containerVsWall = RecordingTimingMetrics.ComputeContainerVsWallClockDifference(
            containerDurationEstimate, wallDurationSec);
        var timestampAnalysis = AnalyzeFrameTimestamps(requestedFps);
        _lastFrameTimestampCsvResult = WriteFrameTimestampCsv(cameraSlot, outputFilePath);
        var trimWarning = FrameTimestampTrimmingHelper.GetTrimWarning(containerVsWall);
        var fpsStabilityGrade = timestampAnalysis.FpsStabilityGrade;
        if (frames <= 0 || framesCaptured <= 0 || _lastFrameTimestampCsvResult.RowCount <= 0)
            fpsStabilityGrade = "Failed";
        else if (_writerQueueDrops > 0)
            fpsStabilityGrade = "Failed";

        var stats = new RecordingCameraStats
        {
            RecordingTimingMode = RecordingTimingModes.OriginalCapture,
            OriginalCaptureMode = true,
            CameraSlot = cameraSlot,
            CameraDeviceName = cameraDeviceName,
            OutputFilePath = outputFilePath,
            RequestedFps = requestedFps,
            SelectedDeviceFps = selectedDeviceFps,
            SelectedFps = selectedDeviceFps,
            RecordingWriterFps = writerFps,
            WriterFps = writerFps,
            ContainerFps = writerFps,
            MeasuredWriterFps = measuredCameraFps,
            MeasuredCameraFps = measuredCameraFps,
            EffectivePlaybackFps = effectivePlaybackFps,
            Width = rw,
            Height = rh,
            FramesWritten = frames,
            FramesCaptured = framesCaptured,
            WriterQueueDrops = _writerQueueDrops,
            WriterFramesDequeued = Interlocked.Read(ref _writerFramesDequeued),
            WriterQueueCapacity = _recordQueueCapacity,
            WriterQueueDepthMax = Volatile.Read(ref _recordQueueDepthMax),
            WriterQueueDepthAtStop = Volatile.Read(ref _recordQueueDepth),
            WriterQueueFullCount = Interlocked.Read(ref _writerQueueFullCount),
            WriterLoopStartMonotonicSeconds = loopStartMono,
            WriterLoopStopMonotonicSeconds = loopStopMono,
            AverageVideoWriterWriteMs = avgWriteMs,
            MaxVideoWriterWriteMs = maxWriteMs,
            DroppedFrameTimestamps = dropSamples,
            PreviewEnabledDuringRecording = true,
            RecordingPreviewFpsCap = _recordingPreviewFpsCap,
            PreviewFramesRenderedDuringRecording = Interlocked.Read(ref _previewFramesRenderedDuringRecording),
            FileSizeBytes = fileSizeBytes,
            FileBitrateMbps = bitrateMbps,
            WallDurationSeconds = wallDurationSec,
            WallClockDurationSeconds = wallDurationSec,
            FrameBasedDurationSeconds = frameBasedDuration,
            ContainerDurationSeconds = containerDurationEstimate,
            ContainerVsWallClockDifferenceSeconds = containerVsWall,
            TimestampDriftSeconds = containerVsWall,
            StartWallClockUtc = _recordStartUtc,
            StopWallClockUtc = _recordStopUtc,
            StartWallClockLocal = _recordStartLocal,
            StopWallClockLocal = _recordStopLocal,
            StartMonotonicSeconds = startMono,
            StopMonotonicSeconds = stopMono,
            RecordingRequestedStartLocalTime = _recordRequestedStartLocal,
            RecordingRequestedStartUtcTime = _recordRequestedStartUtc,
            RecordingRequestedStartMonotonicSec = requestedStartMono,
            CameraRecordingStartLocalTime = _cameraRecordingStartTicks > 0 ? _cameraRecordingStartLocal : null,
            CameraRecordingStartUtcTime = _cameraRecordingStartTicks > 0 ? _cameraRecordingStartUtc : null,
            CameraRecordingStartMonotonicSec = cameraStartMono,
            FirstFrameLocalTime = _recordFirstFrameTicks > 0 ? _firstFrameLocal : null,
            FirstFrameUtcTime = _recordFirstFrameTicks > 0 ? _firstFrameUtc : null,
            FirstFrameMonotonicSec = firstFrameMono,
            LastFrameLocalTime = lastFrameCaptureTicks > 0 ? _lastFrameLocal : null,
            LastFrameUtcTime = lastFrameCaptureTicks > 0 ? _lastFrameUtc : null,
            LastFrameMonotonicSec = lastFrameMono,
            CameraRecordingStopLocalTime = _recordStopRequestedTicks > 0 ? _recordStopRequestedLocal : null,
            CameraRecordingStopUtcTime = _recordStopRequestedTicks > 0 ? _recordStopRequestedUtc : null,
            CameraRecordingStopMonotonicSec = cameraStopMono,
            WriterClosedLocalTime = _writerClosedTicks > 0 ? _writerClosedLocal : null,
            WriterClosedUtcTime = _writerClosedTicks > 0 ? _writerClosedUtc : null,
            WriterClosedMonotonicSec = writerClosedMono,
            DurationSeconds = wallDurationSec,
            FirstFrameMonotonicSeconds = firstFrameMono,
            LastFrameMonotonicSeconds = lastFrameMono,
            StopRequestedMonotonicSeconds = _recordStopRequestedTicks > 0 ? _recordStopRequestedTicks / freq : stopMono,
            WriterClosedMonotonicSeconds = writerClosedMono,
            FrameTimestampCsvPath = _lastFrameTimestampCsvResult.Path,
            FrameTimestampCsvWritten = _lastFrameTimestampCsvResult.Written,
            FrameTimestampCsvRowCount = _lastFrameTimestampCsvResult.RowCount,
            FirstFrameCaptureUtcTime = _lastFrameTimestampCsvResult.FirstCaptureUtcTime,
            LastFrameCaptureUtcTime = _lastFrameTimestampCsvResult.LastCaptureUtcTime,
            FirstFrameCaptureMonotonicSec = _lastFrameTimestampCsvResult.FirstCaptureMonotonicSec,
            LastFrameCaptureMonotonicSec = _lastFrameTimestampCsvResult.LastCaptureMonotonicSec,
            FirstToLastFrameDurationSec = _lastFrameTimestampCsvResult.FirstToLastFrameDurationSec,
            TrimRecommendedTimeSource = FrameTimestampTrimmingHelper.OriginalCaptureTimeSource,
            TrimWarning = trimWarning,
            ScientificTrimStartReference = FrameTimestampTrimmingHelper.ScientificTrimStartReference,
            ScientificTrimEndReference = FrameTimestampTrimmingHelper.ScientificTrimEndReference,
            SupportsTimestampBasedTrimming = true,
            Codec = codec,
            Container = container,
            AverageCaptureIntervalMs = timestampAnalysis.IntervalCount > 0
                ? timestampAnalysis.CaptureIntervalMeanMs
                : captureTiming?.IsAvailable == true
                    ? captureTiming.MeanMs
                    : summary?.AverageCaptureIntervalMs ?? 0,
            MinCaptureIntervalMs = timestampAnalysis.IntervalCount > 0
                ? timestampAnalysis.CaptureIntervalMinMs
                : captureTiming?.IsAvailable == true
                    ? captureTiming.MinMs
                    : summary?.MinCaptureIntervalMs ?? 0,
            MaxCaptureIntervalMs = timestampAnalysis.IntervalCount > 0
                ? timestampAnalysis.CaptureIntervalMaxMs
                : captureTiming?.IsAvailable == true
                    ? captureTiming.MaxMs
                    : summary?.MaxCaptureIntervalMs ?? 0,
            CaptureJitterMs = timestampAnalysis.IntervalCount > 0
                ? timestampAnalysis.CaptureIntervalStdMs
                : captureTiming?.IsAvailable == true
                    ? captureTiming.StdMs
                    : summary?.CaptureJitterMs ?? 0,
            CaptureIntervalMeanMs = timestampAnalysis.IntervalCount > 0 ? timestampAnalysis.CaptureIntervalMeanMs : captureTiming?.IsAvailable == true ? captureTiming.MeanMs : 0,
            CaptureIntervalMinMs = timestampAnalysis.IntervalCount > 0 ? timestampAnalysis.CaptureIntervalMinMs : captureTiming?.IsAvailable == true ? captureTiming.MinMs : 0,
            CaptureIntervalMaxMs = timestampAnalysis.IntervalCount > 0 ? timestampAnalysis.CaptureIntervalMaxMs : captureTiming?.IsAvailable == true ? captureTiming.MaxMs : 0,
            CaptureIntervalStdMs = timestampAnalysis.IntervalCount > 0 ? timestampAnalysis.CaptureIntervalStdMs : captureTiming?.IsAvailable == true ? captureTiming.StdMs : 0,
            CaptureIntervalCount = timestampAnalysis.IntervalCount > 0 ? timestampAnalysis.IntervalCount : captureTiming?.CaptureIntervalCount ?? summary?.CaptureIntervalCount ?? 0,
            CaptureIntervalStatsMessage = timestampAnalysis.IntervalCount > 0
                ? ""
                : !string.IsNullOrWhiteSpace(timestampAnalysis.Message)
                    ? timestampAnalysis.Message
                    : captureTiming?.AvailabilityMessage ?? CaptureTimingSnapshot.UnavailableMessage,
            MeasuredCameraFpsFromFirstLastFrame = timestampAnalysis.MeasuredCameraFpsFromFirstLastFrame,
            MeasuredCameraFpsFromMeanInterval = timestampAnalysis.MeasuredCameraFpsFromMeanInterval,
            ExpectedIntervalMs = timestampAnalysis.ExpectedIntervalMs,
            RequestedExpectedIntervalMs = timestampAnalysis.RequestedExpectedIntervalMs,
            MeanIntervalErrorMs = timestampAnalysis.MeanIntervalErrorMs,
            AbsoluteMeanIntervalErrorMs = timestampAnalysis.AbsoluteMeanIntervalErrorMs,
            CaptureIntervalMedianMs = timestampAnalysis.CaptureIntervalMedianMs,
            CaptureIntervalP95Ms = timestampAnalysis.CaptureIntervalP95Ms,
            CaptureIntervalP99Ms = timestampAnalysis.CaptureIntervalP99Ms,
            LongGapCount = timestampAnalysis.LongGapCount,
            ShortGapCount = timestampAnalysis.ShortGapCount,
            SevereLongGapCount = timestampAnalysis.SevereLongGapCount,
            JitterScoreMs = timestampAnalysis.JitterScoreMs,
            FpsStabilityGrade = fpsStabilityGrade,
            MaxConsecutiveLateFrames = captureTiming?.MaxConsecutiveLateFrames
                ?? summary?.MaxConsecutiveLateFrames ?? 0,
            MaxConsecutiveNoFrame = captureTiming?.MaxConsecutiveNoFrame
                ?? summary?.MaxConsecutiveNoFrame ?? 0,
            DroppedFrames = 0,
            DuplicateFrames = 0,
            PlaceholderFrames = 0,
            ConstantFrameCountMode = false,
            Status = frames > 0 ? "completed" : "no_frames_written"
        };

        var preliminary = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            VideoReadable = frames > 0,
            HasMetadata = true,
            FramesWritten = frames,
            FramesCaptured = framesCaptured,
            QueueDrops = stats.WriterQueueDrops,
            DuplicateFrames = 0,
            PlaceholderFrames = 0,
            ConstantFrameCountMode = false,
            OriginalCaptureMode = true,
            RequestedFps = requestedFps,
            Width = rw,
            Height = rh,
            WriterFps = writerFps,
            ContainerFps = writerFps,
            MeasuredCameraFps = measuredCameraFps,
            CaptureIntervalCount = stats.CaptureIntervalCount,
            CaptureIntervalStdMs = stats.CaptureIntervalStdMs,
            CaptureIntervalP99Ms = stats.CaptureIntervalP99Ms,
            ExpectedIntervalMs = stats.ExpectedIntervalMs,
            LongGapCount = stats.LongGapCount,
            SevereLongGapCount = stats.SevereLongGapCount,
            FpsStabilityGrade = stats.FpsStabilityGrade,
            RequireFrameTimestampCsvValidation = true,
            FrameTimestampCsvWritten = stats.FrameTimestampCsvWritten,
            FrameTimestampCsvRowCount = stats.FrameTimestampCsvRowCount,
            MaxConsecutiveNoFrame = stats.MaxConsecutiveNoFrame
        });

        stats = stats with
        {
            ScientificTimingStatus = preliminary.Status,
            ScientificTimingMessage = preliminary.Message
        };

        if (_lastTimingSummary == null || _experiment?.Enabled != true)
            return stats;

        var t = _lastTimingSummary;
        var loco = _experiment?.LocomotorMode == true;
        return stats with
        {
            ExperimentMode = true,
            LocomotorMode = loco,
            MinimumAnalysisDurationSeconds = _experiment?.MinimumAnalysisDurationSeconds ?? 600,
            PlannedRecordingDurationSeconds = _experiment?.PlannedRecordingDurationSeconds ?? t.TargetDurationSeconds,
            UsableFor10MinAnalysis = t.DurationSeconds >= (_experiment?.MinimumAnalysisDurationSeconds ?? 600),
            TargetDurationSeconds = t.TargetDurationSeconds,
            TargetFps = t.TargetFps,
            ExpectedFrames = t.ExpectedFrames,
            DroppedFrames = t.DroppedFrames,
            DuplicateFrames = 0,
            PlaceholderFrames = 0,
            ConstantFrameCountMode = false,
            RecordingTimingMode = RecordingTimingModes.OriginalCapture,
            OriginalCaptureMode = true,
            StrictFrameValidation = _experiment?.StrictFrameValidation ?? false,
            MinFrameIntervalMs = t.MinFrameIntervalMs,
            MaxFrameIntervalMs = t.MaxFrameIntervalMs,
            AverageFrameIntervalMs = t.AverageFrameIntervalMs,
            FrameIntervalStdDevMs = t.FrameIntervalStdDevMs,
            FpsDrift = t.FpsDrift,
            FirstFrameUtc = t.FirstFrameUtc,
            LastFrameUtc = t.LastFrameUtc,
            MeasuredWriterFps = t.MeanFps > 0 ? t.MeanFps : measuredCameraFps,
            MeasuredCameraFps = t.MeanFps > 0 ? t.MeanFps : measuredCameraFps,
            DurationSeconds = t.DurationSeconds > 0 ? t.DurationSeconds : wallDurationSec,
            WallClockDurationSeconds = t.DurationSeconds > 0 ? t.DurationSeconds : wallDurationSec,
            ExperimentResult = _lastExperimentVerdict
        };
    }

    public async Task StopRecordingAsync()
    {
        await Task.Run(() =>
        {
            var count = StopRecordingInternal();
            _log.Info("recording", $"OpenCV writer closed ({count} frames @ {_lastRecordWriterFps:F1} fps)");
        }).ConfigureAwait(false);
    }

    private long StopRecordingInternal()
    {
        IsRecording = false;
        _writerOpened = false;
        _firstFrameReceivedSinceRecord = false;
        _firstFrameWrittenSinceRecord = false;
        _firstFrameWrittenTcs?.TrySetCanceled();
        _firstFrameWrittenTcs = null;
        _measuringForRecord = false;
        _measureTcs?.TrySetCanceled();
        _recordStopRequestedTicks = Stopwatch.GetTimestamp();
        _recordStopRequestedUtc = DateTime.UtcNow;
        _recordStopRequestedLocal = DateTime.Now;
        _recordChannel?.Writer.TryComplete();
        try
        {
            _recordPumpTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _log.Error("recording", "Record pump wait failed", ex);
        }

        _recordPumpTask = null;
        _recordChannel = null;
        _recordResizeBuffer?.Dispose();
        _recordResizeBuffer = null;

        lock (_writerLock)
        {
            _recordStopTicks = _recordStopRequestedTicks;
            _recordStopUtc = _recordStopRequestedUtc;
            _recordStopLocal = _recordStopRequestedLocal;
            var freq = (double)Stopwatch.Frequency;
            var durationSec = _recordStartTicks > 0
                ? (_recordStopTicks - _recordStartTicks) / freq
                : 0;

            FinalizeExperimentSummary();
            FreezeTimingBeforeCleanup(_recordedFrames, durationSec);

            if (_writer == null)
            {
                _writerClosedTicks = Stopwatch.GetTimestamp();
                _writerClosedUtc = DateTime.UtcNow;
                _writerClosedLocal = DateTime.Now;
                _lastRecordedFrameCount = _recordedFrames;
                return _recordedFrames;
            }

            var count = _recordedFrames;
            _lastRecordedFrameCount = count;
            _writer.Release();
            _writer.Dispose();
            _writerClosedTicks = Stopwatch.GetTimestamp();
            _writerClosedUtc = DateTime.UtcNow;
            _writerClosedLocal = DateTime.Now;
            _writer = null;
            _recordedFrames = 0;
            _experiment = null;
            return count;
        }
    }

    public static int ResolveRecordQueueCapacity(int width, int height)
    {
        var pixels = Math.Max(0, width) * Math.Max(0, height);
        if (pixels >= 1920 * 1080)
            return MaxRecordQueueCapacity;
        if (pixels >= 1280 * 720)
            return 60;
        return DefaultRecordQueueCapacity;
    }

    public static int ResolveRecordingPreviewFpsCap(int width, int height)
    {
        var pixels = Math.Max(0, width) * Math.Max(0, height);
        return pixels >= 1920 * 1080
            ? HighResolutionRecordingPreviewFpsCap
            : DefaultRecordingPreviewFpsCap;
    }

    public static int ComputeAdaptivePreviewFpsCap(string mode, int activeCameraCount) =>
        mode switch
        {
            "Smooth" => activeCameraCount switch { 1 => 30, 2 => 20, 3 => 15, _ => 15 },
            "MaxStability" => activeCameraCount switch { 1 => 15, 2 => 10, 3 => 8, _ => 6 },
            _ => activeCameraCount switch { 1 => 20, 2 => 15, 3 => 12, _ => 10 } // Balanced
        };

    public void SetRecordingPreviewFpsCap(int fpsCap) => _recordingPreviewFpsCap = Math.Max(1, fpsCap);

    private void FinalizeExperimentSummary()
    {
        if (_experiment?.Enabled != true || _timingMonitor == null) return;

        var appConfig = _appConfigForExperiment ?? new AppConfig();
        var summary = _timingMonitor.BuildSummary(_experiment.ExpectedFrames, _experiment.TargetDurationSeconds);
        ExperimentCheckVerdict verdict;
        if (_experiment.LocomotorMode && !_experiment.RequireExact18000Frames && !_experiment.RequireExactThirtyFps)
        {
            var locoProfile = LocomotorVerificationService.ResolveProfile(appConfig);
            verdict = FrameCountValidator.EvaluateLocomotor(
                summary, _experiment.MinimumAnalysisDurationSeconds, locoProfile);
        }
        else
        {
            var profile = FrameCountValidator.ResolveProfile(appConfig);
            verdict = FrameCountValidator.Evaluate(summary, profile, _experiment.StrictFrameValidation);
        }

        _lastExperimentVerdict = LocomotorRecordingController.FormatVerdict(verdict);
        _lastTimingSummary = summary;
        _appConfigForExperiment = null;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _captureLoopTask;
        try { cts?.Cancel(); } catch { /* ignore */ }
        _cts = null;
        _captureLoopTask = null;
        if (loop != null)
        {
            try
            {
                var completed = await Task.WhenAny(loop, Task.Delay(TimeSpan.FromMilliseconds(1500))).ConfigureAwait(false);
                if (completed == loop)
                    await loop.ConfigureAwait(false);
                else
                    AppDiagnosticLogger.Runtime($"SLOT_TASK_STOP_TIMEOUT opencvLoop index={_deviceIndex}");
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _log.Error("preview", $"OpenCV preview loop stop failed ({OpenLabel})", ex);
            }
        }

        try { cts?.Dispose(); } catch { /* ignore */ }
        _log.Info("preview", $"OpenCV preview stopped ({_framesDelivered} frames, index {_deviceIndex})");
        _framesDelivered = 0;
    }

    public void ReleaseCamera()
    {
        try { _openAbortCts?.Cancel(); } catch { /* ignore */ }
        try { _openAbortCts?.Dispose(); } catch { /* ignore */ }
        _openAbortCts = null;
        StopRecordingInternal();
        _cts?.Cancel();
        _cts = null;
        var cap = _capture;
        _capture = null;
        if (cap == null) return;

        ReleaseCaptureHandle(cap);
    }

    private void ReleaseExistingCaptureForReopen()
    {
        var cap = _capture;
        _capture = null;
        if (cap == null) return;
        ReleaseCaptureHandle(cap);
    }

    private void ReleaseCaptureHandle(VideoCapture cap)
    {
        lock (DshowOpenGate)
        {
            try
            {
                if (cap.IsOpened())
                    cap.Release();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _log.Error("preview", $"OpenCV Release() skipped ({OpenLabel})", ex);
            }

            try
            {
                cap.Dispose();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _log.Error("preview", $"OpenCV Dispose() skipped ({OpenLabel})", ex);
            }
        }
    }

    public string OpenLabel =>
        !string.IsNullOrWhiteSpace(_directShowOpenUri) ? _directShowOpenUri
        : !string.IsNullOrWhiteSpace(_directShowName) ? _directShowName
        : $"index {Math.Max(0, _deviceIndex)}";

    private static BitmapSource MatToBitmapSource(Mat mat)
    {
        using var bgra = new Mat();
        switch (mat.Channels())
        {
            case 1:
                Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA);
                break;
            case 3:
                // v1.0.38 stable preview behavior: OpenCV DirectShow frames arrive as BGR,
                // then WPF renders frozen Bgra32. Do not apply RGB/BGR filters here.
                Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
                break;
            case 4:
                mat.CopyTo(bgra);
                break;
            default:
                throw new InvalidOperationException($"Unsupported OpenCV preview frame channels: {mat.Channels()}");
        }

        var stride = (int)bgra.Step();
        var height = bgra.Rows;
        var width = bgra.Cols;
        var pixels = new byte[stride * height];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        return BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }

    public void Dispose() => ReleaseCamera();
}
