////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Windows.Storage;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Metadata;
using MultiCamApp.Utils;
using MultiCamApp.Verification;
using System.Runtime.InteropServices;

namespace MultiCamApp.Recording;

/// <summary>
/// Windows Camera–style session: shared start time, per-camera subfolders, MCAM_*.mp4, metadata on stop.
/// </summary>
public sealed class RecordingSession
{
    private readonly LogService _log = new();
    private readonly OutputFolderManager _folders = new();
    private readonly Dictionary<int, CameraRecordingMetadata> _metadata = new();
    private readonly MonotonicClock _clock = new();
    private readonly AppConfig _config;
    private readonly PrivacyGuardService _privacy;
    private string? _lastRecordingStartDiagnosticsPath;
    private RecordingSessionLogSession? _recordingSessionLog;
    private RecordingDiagnosticsMonitor? _recordingDiagnosticsMonitor;

    public string? LastRecordingSessionLogPath => _recordingSessionLog?.LogFilePath;

    public string? SessionPath { get; private set; }
    public string SessionName { get; private set; } = "";
    public string SessionTitleOriginal { get; private set; } = "";
    public string SessionFolderName { get; private set; } = "";
    public DateTime? RecordingDateTimeLocal { get; private set; }
    public VersionInfo AppVersionInfo { get; set; } = VersionService.Current;
    public DateTime? RecordStartUtc { get; private set; }
    public DateTime? RecordStartLocal { get; private set; }
    private long _recordStartMonotonicTicks;
    public MonotonicClock SessionClock => _clock;

    public RecordingSession(AppConfig config, PrivacyGuardService privacy)
    {
        _config = config;
        _privacy = privacy;
    }

    public string PrepareSession(string? outputFolder, string sessionTitle)
    {
        var basePath = _folders.ResolveBaseFolder(outputFolder);
        var plan = _folders.CreateSessionFolder(basePath, sessionTitle);

        SessionTitleOriginal = plan.SessionTitleOriginal;
        SessionFolderName = plan.FolderName;
        RecordingDateTimeLocal = plan.RecordingDateTimeLocal;
        SessionName = string.IsNullOrWhiteSpace(plan.SessionTitleOriginal)
            ? plan.SanitizedTitlePart
            : plan.SessionTitleOriginal;
        SessionPath = plan.FullPath;

        _log.Info("output", $"Session folder: {SessionPath} (name: {SessionFolderName})");
        return SessionPath;
    }

    /// <summary>Windows Camera flow: prepare all cameras, then start all together (same wall-clock moment).</summary>
    public async Task StartRecordingAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        if (SessionPath == null) throw new InvalidOperationException("Session not prepared");

        AppDiagnosticLogger.Recording("RECORDING_START_BEGIN");
        var startupStopwatch = Stopwatch.StartNew();

        _recordingSessionLog?.Dispose();
        _recordingSessionLog = AppDiagnosticLogger.BeginRecordingSession(SessionPath);
        AppDiagnosticLogger.Recording($"START session={SessionPath}");

        var diagnostics = RecordingStartDiagnostics.Create();
        _lastRecordingStartDiagnosticsPath = diagnostics.Path;
        _recordingSessionLog.Line($"startupDiagnostics={diagnostics.Path}");
        var startedOpenCvSlots = new List<CameraSlotPipeline>();

        _recordStartMonotonicTicks = MonotonicClock.NowTicks();
        var startUtc = DateTime.UtcNow;
        var startLocal = DateTime.Now;
        RecordStartUtc = startUtc;
        RecordStartLocal = startLocal;
        _clock.Reset();
        _metadata.Clear();

        var activeCount = slots.Count(s => s.SelectedMode != null);

        var useWindowsCamera = UsesWindowsCameraRecording();
        var winRtSlotsCount = slots.Count(s => s.SelectedMode != null && !s.UsesOpenCvRecording(_config));
        var openCvSlotsCount = slots.Count(s => s.SelectedMode != null && s.UsesOpenCvRecording(_config));
        var useLowLag = _config.UseLowLagRecording && openCvSlotsCount == 0 && winRtSlotsCount <= 1;
        var prepared = new List<CameraSlotPipeline>();

        var winRtSlots = slots
            .Where(s => s.SelectedMode != null && !s.UsesOpenCvRecording(_config))
            .ToList();
        var openCvSlots = slots
            .Where(s => s.SelectedMode != null && s.UsesOpenCvRecording(_config))
            .OrderBy(s => s.SlotIndex)
            .ToList();
        // Use a shared start gate for all multi-camera sessions (≥2), not just ≥3,
        // to keep the inter-camera first-frame offset below the 50 ms warning threshold.
        var synchronizedOpenCvStart = openCvSlots.Count >= 2 && !useLowLag;
        var sequentialOpenCv = false;
        var recordingStaggerMs = 0;

        diagnostics.WriteHeader(_config, activeCount, activeCount, SessionPath,
            sequentialOpenCv, recordingStaggerMs, openCvSlotsCount, winRtSlotsCount);

        _log.Info("recording", $"Recording startup diagnostics: {diagnostics.Path}");
        _recordingSessionLog.Line(
            $"layout={activeCount} opencvSlots={openCvSlotsCount} winrtSlots={winRtSlotsCount} " +
            $"sequentialOpenCv={sequentialOpenCv} synchronizedOpenCvStart={synchronizedOpenCvStart} staggerMs={recordingStaggerMs} lowLag={useLowLag} " +
            $"preset={CaptureResolutionPreset.ToLabel(_config.PreferredCaptureWidth, _config.PreferredCaptureHeight)} " +
            $"fps={_config.PreferFps:F0} previewEngine={_config.PreviewEngine} recordingEngine={_config.RecordingEngine}");

        try
        {
            if (!useLowLag && winRtSlots.Count > 1)
            {
                await StartOpenCvRecordingSlotsAsync(
                    openCvSlots, sequentialOpenCv, synchronizedOpenCvStart, recordingStaggerMs, startUtc, startLocal,
                    activeCount, useWindowsCamera, useLowLag, prepared, startedOpenCvSlots, diagnostics);

                foreach (var slot in winRtSlots)
                    await StartOneCameraRecordingAsync(
                        slot, startUtc, startLocal, activeCount, useWindowsCamera, useLowLag, prepared, diagnostics);
            }
            else if (sequentialOpenCv)
            {
                await StartOpenCvRecordingSlotsAsync(
                    openCvSlots, sequentialOpenCv: true, synchronizedOpenCvStart: false, recordingStaggerMs, startUtc, startLocal,
                    activeCount, useWindowsCamera, useLowLag, prepared, startedOpenCvSlots, diagnostics);

                foreach (var slot in winRtSlots)
                    await StartOneCameraRecordingAsync(
                        slot, startUtc, startLocal, activeCount, useWindowsCamera, useLowLag, prepared, diagnostics);
            }
            else
            {
                if (openCvSlots.Count > 0 && winRtSlots.Count == 0)
                {
                    await StartOpenCvRecordingSlotsAsync(
                        openCvSlots, sequentialOpenCv, synchronizedOpenCvStart, recordingStaggerMs, startUtc, startLocal,
                        activeCount, useWindowsCamera, useLowLag, prepared, startedOpenCvSlots, diagnostics);
                    goto RecordingSlotsStarted;
                }

                var startTasks = new List<Task>();
                foreach (var slot in slots)
                {
                    if (slot.SelectedMode == null) continue;
                    if (!useWindowsCamera && !slot.UsesOpenCvRecording(_config) && slot.Capture == null) continue;

                    var slotCapture = slot;
                    startTasks.Add(StartOneCameraRecordingAsync(
                        slotCapture, startUtc, startLocal, activeCount, useWindowsCamera, useLowLag, prepared, diagnostics));
                }

                await Task.WhenAll(startTasks);
                startedOpenCvSlots.AddRange(openCvSlots.Where(s => _metadata.ContainsKey(s.SlotIndex)));

                if (startedOpenCvSlots.Count > 0)
                    await VerifyOpenCvFirstFramesWrittenAsync(startedOpenCvSlots);
            }

        RecordingSlotsStarted:
            if (prepared.Count > 0)
            {
                await Task.WhenAll(prepared.Select(s => s.BeginLowLagRecordingAsync()));
                _log.Info("recording", $"Windows Camera LowLag started together on {prepared.Count} camera(s)");
            }

            if (_metadata.Count == 0)
                throw new InvalidOperationException("No camera could start recording");

            startupStopwatch.Stop();
            AppDiagnosticLogger.Recording($"RECORDING_START_CORE_READY elapsedMs={startupStopwatch.ElapsedMilliseconds}");

            if (startedOpenCvSlots.Count > 0)
            {
                AppDiagnosticLogger.Recording("RECORDING_START_DIAGNOSTICS_BACKGROUND_STARTED");
                _ = Task.Run(async () =>
                {
                    var backgroundStopwatch = Stopwatch.StartNew();
                    try
                    {
                        await WriteOpenCvRecordingSamplesAsync(startedOpenCvSlots, diagnostics).ConfigureAwait(false);
                        diagnostics.WriteFooter(true);
                        backgroundStopwatch.Stop();
                        AppDiagnosticLogger.Recording($"RECORDING_START_DIAGNOSTICS_BACKGROUND_DONE elapsedMs={backgroundStopwatch.ElapsedMilliseconds}");
                    }
                    catch (Exception ex)
                    {
                        _log.Error("recording", $"RECORDING_START_DIAGNOSTICS_FAILED: {ex.Message}", ex);
                        AppDiagnosticLogger.Recording($"RECORDING_START_DIAGNOSTICS_FAILED error={ex.Message}");
                        try { diagnostics.WriteFooter(false, ex.Message); } catch { }
                    }
                    finally
                    {
                        try { diagnostics.Dispose(); } catch { }
                    }
                });
            }
            else
            {
                diagnostics.WriteFooter(true);
                diagnostics.Dispose();
            }

            _log.Info("recording", $"Recording active on {_metadata.Count}/{activeCount} camera(s)");
            _recordingSessionLog.Line($"result=STARTED activeSlots={_metadata.Count}/{activeCount}");
            AppDiagnosticLogger.Recording($"STARTED session={SessionPath} slots={_metadata.Count}/{activeCount}");
            _recordingDiagnosticsMonitor = new RecordingDiagnosticsMonitor(SessionPath, slots, _config);
            _recordingDiagnosticsMonitor.Start();
            AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_STARTED csv={_recordingDiagnosticsMonitor.CsvPath}");
        }
        catch (Exception ex)
        {
            if (_recordingDiagnosticsMonitor != null)
            {
                try { await _recordingDiagnosticsMonitor.StopAsync(); } catch { }
                _recordingDiagnosticsMonitor = null;
            }
            try { diagnostics.WriteFooter(false, ex.Message); } catch { }
            try { diagnostics.Dispose(); } catch { }
            _recordingSessionLog?.Line($"result=START_FAILED error={ex.Message}");
            AppDiagnosticLogger.Recording($"START_FAILED session={SessionPath} error={ex.Message}");
            AppDiagnosticLogger.Failure("recording", "Recording start failed", ex);
            await RollbackPartialRecordingAsync(slots);
            _recordingSessionLog?.Dispose();
            _recordingSessionLog = null;
            throw;
        }
    }

    private async Task StartOpenCvRecordingSlotsAsync(
        IReadOnlyList<CameraSlotPipeline> openCvSlots,
        bool sequentialOpenCv,
        bool synchronizedOpenCvStart,
        int recordingStaggerMs,
        DateTime startUtc,
        DateTime startLocal,
        int activeCount,
        bool useWindowsCamera,
        bool useLowLag,
        List<CameraSlotPipeline> prepared,
        List<CameraSlotPipeline> startedOpenCvSlots,
        RecordingStartDiagnostics diagnostics)
    {
        if (sequentialOpenCv)
        {
            for (var i = 0; i < openCvSlots.Count; i++)
            {
                if (i > 0 && recordingStaggerMs > 0)
                    await Task.Delay(recordingStaggerMs);

                var slot = openCvSlots[i];
                await StartOneCameraRecordingAsync(
                    slot, startUtc, startLocal, activeCount, useWindowsCamera, useLowLag, prepared, diagnostics);
                startedOpenCvSlots.Add(slot);
                await VerifyOpenCvFirstFramesWrittenAsync([slot]);
            }

            return;
        }

        var syncRelease = synchronizedOpenCvStart
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        var readySignals = new List<TaskCompletionSource<bool>>();
        var startTasks = new List<Task>();
        foreach (var slot in openCvSlots)
        {
            var slotCapture = slot;
            var ready = synchronizedOpenCvStart
                ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;
            if (ready != null)
                readySignals.Add(ready);
            startTasks.Add(StartOneCameraRecordingAsync(
                slotCapture, startUtc, startLocal, activeCount, useWindowsCamera, useLowLag, prepared, diagnostics,
                syncRelease?.Task, ready));
        }

        if (syncRelease != null)
        {
            AppDiagnosticLogger.Recording($"OPENCV_SYNC_START_WAIT_WRITERS count={readySignals.Count}");
            var allReady = Task.WhenAll(readySignals.Select(r => r.Task));
            var allStarted = Task.WhenAll(startTasks);
            var completed = await Task.WhenAny(allReady, allStarted, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            if (completed == allStarted && allStarted.IsFaulted)
                await allStarted.ConfigureAwait(false);
            if (completed == allReady && allReady.IsFaulted)
            {
                syncRelease.TrySetCanceled();
                await allReady.ConfigureAwait(false);
            }
            if (completed != allReady)
            {
                syncRelease.TrySetCanceled();
                throw new RecordingStartupException(
                    "OpenCV",
                    RecordingStartupFailureKind.WriterCreation,
                    "Timed out waiting for all OpenCV writers to become ready for synchronized start.");
            }

            AppDiagnosticLogger.Recording("OPENCV_SYNC_START_RELEASE");
            syncRelease.TrySetResult(true);
        }

        await Task.WhenAll(startTasks);
        startedOpenCvSlots.AddRange(openCvSlots.Where(s => _metadata.ContainsKey(s.SlotIndex)));
        await VerifyOpenCvFirstFramesWrittenAsync(startedOpenCvSlots);
    }

    private async Task WriteOpenCvRecordingSamplesAsync(
        IReadOnlyList<CameraSlotPipeline> openCvSlots,
        RecordingStartDiagnostics diagnostics)
    {
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        foreach (var slot in openCvSlots.OrderBy(s => s.SlotIndex))
        {
            if (!_metadata.ContainsKey(slot.SlotIndex)) continue;
            diagnostics.WriteSlotSection(
                "1 second after recording start",
                slot,
                slot.BuildRecordingStartupSnapshot(
                    _config,
                    framesCapturedAfter1s: slot.OpenCvPreviewFramesCapturedSinceRecord,
                    framesWrittenAfter1s: slot.OpenCvRecordedFrameCount));
        }

        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        foreach (var slot in openCvSlots.OrderBy(s => s.SlotIndex))
        {
            if (!_metadata.ContainsKey(slot.SlotIndex)) continue;
            diagnostics.WriteSlotSection(
                "3 seconds after recording start",
                slot,
                slot.BuildRecordingStartupSnapshot(
                    _config,
                    framesCapturedAfter3s: slot.OpenCvPreviewFramesCapturedSinceRecord,
                    framesWrittenAfter3s: slot.OpenCvRecordedFrameCount));
        }
    }

    private async Task VerifyOpenCvFirstFramesWrittenAsync(IReadOnlyList<CameraSlotPipeline> openCvSlots)
    {
        if (openCvSlots.Count == 0)
            return;

        const int timeoutSeconds = 3;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        foreach (var slot in openCvSlots.OrderBy(s => s.SlotIndex))
        {
            if (!_metadata.ContainsKey(slot.SlotIndex))
                continue;

            var ok = await slot.WaitForFirstFrameWrittenAsync(timeout).ConfigureAwait(false);
            var framesAfterCheck = slot.OpenCvRecordedFrameCount;
            var line =
                $"{slot.SlotName} firstFrameCheck received={slot.FirstFrameReceivedSinceRecordOpenCv} written={ok} framesAfterCheck={framesAfterCheck}";
            _log.Info("recording", line);
            _recordingSessionLog?.Line(line);
            if (!ok)
            {
                _recordingSessionLog?.Line($"{slot.SlotName} firstFrameCheck=FAIL timeoutSec={timeoutSeconds}");
                AppDiagnosticLogger.Recording($"{slot.SlotName} first-frame timeout session={SessionPath}");
                throw new RecordingStartupException(
                    slot.SlotName,
                    RecordingStartupFailureKind.FirstFrameTimeout,
                    $"{slot.SlotName} did not write any frames within {timeoutSeconds} seconds. Recording was cancelled safely. Check USB bandwidth, camera support, or selected resolution.");
            }
        }
    }

    private async Task RollbackPartialRecordingAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        foreach (var slot in slots.Where(s => s.Status == "Recording"))
        {
            try
            {
                var file = await slot.StopRecordingAsync();
                RemoveEmptyRecordingFile(file?.Path, slot.SlotName);
            }
            catch (Exception ex)
            {
                _log.Error("recording", $"{slot.SlotName} rollback stop failed", ex);
            }
        }

        _metadata.Clear();
    }

    private void RemoveEmptyRecordingFile(string? path, string slotName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0)
            {
                info.Delete();
                _log.Info("recording", $"{slotName} removed empty MP4 after failed startup: {path}");
            }
        }
        catch (Exception ex)
        {
            _log.Error("recording", $"{slotName} could not remove empty MP4 {path}", ex);
        }
    }

    private async Task StartOneCameraRecordingAsync(
        CameraSlotPipeline slot,
        DateTime startUtc,
        DateTime startLocal,
        int activeCount,
        bool useWindowsCamera,
        bool useLowLag,
        List<CameraSlotPipeline> prepared,
        RecordingStartDiagnostics diagnostics,
        Task? openCvRecordingStartGate = null,
        TaskCompletionSource<bool>? openCvWriterReady = null)
    {
        var slotStart = DateTime.Now;
        try
        {
            var camDir = _folders.CameraFolder(SessionPath!, slot.SlotIndex);
            var fileName = RecordingFileNaming.BuildMp4FileName(
                startLocal, slot.SlotIndex, activeCount, _config.RecordingFileNameStyle);
            var path = Path.Combine(camDir, fileName);
            LogRecordingSlotPrecheck(slot, path);
            diagnostics.WriteSlotSection(
                "before start",
                slot,
                slot.BuildRecordingStartupSnapshot(_config));

            var file = await CreateStorageFileAsync(path);
            var mode = slot.SelectedMode!;
            var ver = AppVersionInfo;
            var openCvRecord = slot.UsesOpenCvRecording(_config);
            MediaEncodingProfile? profile = openCvRecord ? null : slot.BuildRecordingProfile(_config);

            var video = profile?.Video;
            var meta = new CameraRecordingMetadata
            {
                AppName = _config.AppName,
                AppVersion = ver.Version,
                BuildNumber = ver.Build,
                ReleaseStage = ver.Stage,
                SessionName = SessionName,
                SessionTitleOriginal = SessionTitleOriginal,
                SessionFolderName = SessionFolderName,
                RecordingDateTimeLocal = RecordingDateTimeLocal ?? startLocal,
                CameraSlot = slot.SlotName,
                CameraDeviceName = slot.DeviceName,
                CameraHardwareId = slot.HardwareId,
                Resolution = CaptureResolutionPreset.ToLabel(mode.Width, mode.Height),
                RequestedResolution = _config.PreferredCaptureWidth > 0 && _config.PreferredCaptureHeight > 0
                    ? CaptureResolutionPreset.ToLabel(_config.PreferredCaptureWidth, _config.PreferredCaptureHeight)
                    : mode.Width > 0 && mode.Height > 0 ? CaptureResolutionPreset.ToLabel(mode.Width, mode.Height) : "",
                SelectedResolution = CaptureResolutionPreset.ToLabel(mode.Width, mode.Height),
                PixelWidth = mode.Width,
                PixelHeight = mode.Height,
                RequestedFps = mode.RequestedFps > 0 ? mode.RequestedFps : _config.PreferFps,
                SelectedDeviceFps = mode.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode.Fps,
                RecordingWriterFps = openCvRecord
                    ? slot.RecordingWriterFps > 0 ? slot.RecordingWriterFps : mode.SelectedDeviceFps
                    : video?.FrameRate.Denominator > 0
                        ? (double)video.FrameRate.Numerator / video.FrameRate.Denominator
                        : mode.SelectedDeviceFps,
                FrameRateNumerator = video?.FrameRate.Numerator ?? NormalizeFrameRate(mode.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode.Fps).Numerator,
                FrameRateDenominator = video?.FrameRate.Denominator ?? NormalizeFrameRate(mode.SelectedDeviceFps > 0 ? mode.SelectedDeviceFps : mode.Fps).Denominator,
                Codec = openCvRecord ? "OpenCV-mp4v" : "H264",
                ContainerFormat = profile?.Container?.Subtype ?? "MP4",
                VideoSubtype = openCvRecord ? "mp4v" : video?.Subtype ?? "H264",
                FilePath = path,
                RecordingStartTime = startUtc,
                RecordingStartTimeLocal = startLocal,
                ModeSelectionReason = mode.SelectionReason,
                IsNativeRecommended = mode.IsNativeRecommended,
                Backend = openCvRecord ? "OpenCV-DSHOW" : "WinRT",
                DeviceId = slot.HardwareId ?? "",
                PrivacyMode = _config.PrivacyMode,
                HiddenRecordingAllowed = _privacy.HiddenRecordingAllowed,
                CameraReleasedOnStop = _config.ReleaseCamerasOnStopPreview,
                RecordingApi = openCvRecord
                    ? "OpenCV-VideoWriter"
                    : useLowLag ? "LowLagMediaRecording" : "StartRecordToStorageFile",
                TimestampPrecision =
                    "UTC wall clock + local filename stamp + monotonic QPC duration (Windows Camera pattern)"
            };
            ApplyFocusStatusToMetadata(meta, slot.LastFocusControlStatus);

            if (openCvRecord)
            {
                LogRecordingSlotStartRequested(slot, path, "OpenCV-DSHOW");
                await slot.StartRecordingAsync(file, _config, profile, openCvRecordingStartGate, openCvWriterReady);
            }
            else if (useLowLag)
            {
                LogRecordingSlotStartRequested(slot, path, "WinRT-LowLag");
                try
                {
                    await slot.PrepareLowLagRecordingAsync(file, profile!, _config);
                    lock (prepared) prepared.Add(slot);
                }
                catch (Exception lowLagEx)
                {
                    _log.Info("recording", $"{slot.SlotName} LowLag prepare failed, using direct record: {lowLagEx.Message}");
                    await slot.StartRecordingAsync(file, _config, profile);
                }
            }
            else
            {
                LogRecordingSlotStartRequested(slot, path, "WinRT");
                await slot.StartRecordingAsync(file, _config, profile);
            }
            ApplyFocusStatusToMetadata(meta, slot.LastFocusControlStatus);

            lock (_metadata) _metadata[slot.SlotIndex] = meta;
            _log.Info("recording", $"{slot.SlotName} -> {path}");
            var startMs = (DateTime.Now - slotStart).TotalMilliseconds;
            var snap = slot.BuildRecordingStartupSnapshot(_config);
            _recordingSessionLog?.Line(
                $"{slot.SlotName} started device=\"{slot.DeviceName}\" capture={snap.DevicePath} " +
                $"resolution={snap.SelectedResolution} fps={snap.SelectedFps:F1} mp4={path} " +
                $"writerOpened={snap.VideoWriterOpened} firstFrameWritten={snap.FirstFrameWritten} durationMs={startMs:F0}");
            AppDiagnosticLogger.Recording(
                $"{slot.SlotName} started device=\"{slot.DeviceName}\" resolution={snap.SelectedResolution} mp4={Path.GetFileName(path)}");
            diagnostics.WriteSlotSection(
                "immediately after start",
                slot,
                snap);
        }
        catch (Exception ex)
        {
            openCvWriterReady?.TrySetException(ex);
            _log.Error("recording", $"{slot.SlotName} failed to start recording", ex);
            LogRecordingSlotFailed(slot, ex.Message);
            _recordingSessionLog?.Line($"{slot.SlotName} startFailed error={ex.Message} durationMs={(DateTime.Now - slotStart).TotalMilliseconds:F0}");
            AppDiagnosticLogger.Recording($"{slot.SlotName} startFailed error={ex.Message}");
            diagnostics.WriteSlotSection(
                "start failed",
                slot,
                slot.BuildRecordingStartupSnapshot(_config, exceptionMessage: ex.Message));
            throw new RecordingStartupException(slot.SlotName, ex);
        }
    }

    private void LogRecordingSlotPrecheck(CameraSlotPipeline slot, string writerPath)
    {
        var frameAge = double.IsNaN(slot.LatestPreviewFrameAgeMs) ? -1 : slot.LatestPreviewFrameAgeMs;
        var line =
            $"RECORDING_SLOT_PRECHECK {slot.SlotName} " +
            $"slot={slot.SlotIndex + 1} selectedDeviceId=\"{slot.HardwareId ?? "-"}\" selectedDeviceName=\"{slot.DeviceName}\" " +
            $"previewReady={(slot.Status is "Previewing" or "Recording" ? "yes" : "no")} " +
            $"latestFrameAgeMs={frameAge:F0} actualPreviewResolution={slot.ActualPreviewWidth}x{slot.ActualPreviewHeight} " +
            $"actualPreviewFps={slot.ActualPreviewFps:F3} writerTargetPath=\"{writerPath}\" writerFourcc=mp4v " +
            $"writerFps={(slot.RecordingWriterFps > 0 ? slot.RecordingWriterFps : slot.SelectedDeviceFps):F3} " +
            $"writerFrameSize={slot.RecordWidth}x{slot.RecordHeight} backend={(slot.UsesOpenCvRecording(_config) ? "OpenCV-DSHOW" : "WinRT")}";
        _recordingSessionLog?.Line(line);
        AppDiagnosticLogger.Recording(line);
    }

    private void LogRecordingSlotStartRequested(CameraSlotPipeline slot, string writerPath, string backend)
    {
        var line = $"RECORDING_SLOT_START_REQUESTED {slot.SlotName} backend={backend} path=\"{writerPath}\"";
        _recordingSessionLog?.Line(line);
        AppDiagnosticLogger.Recording(line);
    }

    private void LogRecordingSlotFailed(CameraSlotPipeline slot, string reason)
    {
        var line = $"RECORDING_SLOT_FAILED {slot.SlotName} reason=\"{reason}\"";
        _recordingSessionLog?.Line(line);
        AppDiagnosticLogger.Recording(line);
    }

    private bool UsesWindowsCameraRecording() =>
        !string.Equals(_config.RecordingEngine, "opencv", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(_config.PreviewEngine, "opencv", StringComparison.OrdinalIgnoreCase);

    private static async Task<StorageFile> CreateStorageFileAsync(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        var folder = await StorageFolder.GetFolderFromPathAsync(dir);
        return await folder.CreateFileAsync(Path.GetFileName(fullPath), CreationCollisionOption.ReplaceExisting);
    }

    public async Task StopRecordingAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        AppDiagnosticLogger.Recording("RECORDING_STOP_BEGIN");
        var stopTotalStopwatch = Stopwatch.StartNew();
        var sessionStopTicks = MonotonicClock.NowTicks();
        var sessionStopUtc = DateTime.UtcNow;
        var sessionStopLocal = DateTime.Now;

        var probeService = new VideoProbeService(_config.Verification);

        var slotsStopwatch = Stopwatch.StartNew();
        var stopTasks = slots.Select(async slot =>
        {
            AppDiagnosticLogger.Recording($"RECORDING_SLOT_STOP_BEGIN {slot.SlotName}");
            var slotStopwatch = Stopwatch.StartNew();
            try
            {
                await slot.StopRecordingAsync();
            }
            catch (Exception ex)
            {
                _log.Error("recording", $"{slot.SlotName} stop failed", ex);
            }
            finally
            {
                slotStopwatch.Stop();
                AppDiagnosticLogger.Recording($"RECORDING_SLOT_STOP_DONE {slot.SlotName} elapsedMs={slotStopwatch.ElapsedMilliseconds}");
            }
        }).ToList();
        await Task.WhenAll(stopTasks);
        slotsStopwatch.Stop();
        AppDiagnosticLogger.Recording($"RECORDING_STOP_ALL_SLOTS_DONE elapsedMs={slotsStopwatch.ElapsedMilliseconds}");

        var diagnosticsMonitor = _recordingDiagnosticsMonitor;
        _recordingDiagnosticsMonitor = null;
        if (diagnosticsMonitor != null)
        {
            await diagnosticsMonitor.StopAsync();
            AppDiagnosticLogger.Recording($"RECORDING_DIAGNOSTICS_STOPPED csv={diagnosticsMonitor.CsvPath} summary={diagnosticsMonitor.SummaryPath}");
        }

        if (SessionPath == null)
        {
            stopTotalStopwatch.Stop();
            AppDiagnosticLogger.Recording($"RECORDING_STOP_DONE elapsedMs={stopTotalStopwatch.ElapsedMilliseconds}");
            return;
        }

        var sessionMonotonicDurationSec = _recordStartMonotonicTicks > 0
            ? (sessionStopTicks - _recordStartMonotonicTicks) / (double)Stopwatch.Frequency
            : _clock.Elapsed.TotalSeconds;
        var sessionWallDurationSec = RecordStartUtc.HasValue
            ? Math.Max(0, (sessionStopUtc - RecordStartUtc.Value).TotalSeconds)
            : sessionMonotonicDurationSec;
        var timing = new RecordingTimingSnapshot
        {
            StartUtc = RecordStartUtc ?? DateTime.UtcNow,
            StartLocal = RecordStartLocal ?? DateTime.Now,
            StartMonotonicTicks = _recordStartMonotonicTicks,
            StartMonotonicSeconds = _recordStartMonotonicTicks / (double)Stopwatch.Frequency,
            StopUtc = sessionStopUtc,
            StopLocal = sessionStopLocal,
            StopMonotonicTicks = sessionStopTicks,
            StopMonotonicSeconds = sessionStopTicks / (double)Stopwatch.Frequency,
            WallClockDurationSeconds = sessionWallDurationSec,
            MonotonicDurationSeconds = sessionMonotonicDurationSec,
            MonotonicElapsed = TimeSpan.FromSeconds(sessionMonotonicDurationSec)
        };

        var writer = new MetadataWriter();
        var processedMetas = new List<CameraRecordingMetadata>();
        var pendingWrites = new List<(string CamDir, CameraRecordingMetadata Meta, RecordingCameraStats? Stats, VideoProbeData? Probe)>();
        var pendingMetaWrites = new List<(string CamDir, CameraRecordingMetadata Meta)>();

        var probeTasks = slots.Select(async slot =>
        {
            if (!_metadata.TryGetValue(slot.SlotIndex, out var meta) ||
                string.IsNullOrEmpty(meta.FilePath) ||
                !File.Exists(meta.FilePath))
            {
                return (slot.SlotIndex, Probe: (VideoProbeData?)null);
            }

            AppDiagnosticLogger.Recording($"RECORDING_PROBE_BEGIN {slot.SlotName}");
            var probeStopwatch = Stopwatch.StartNew();
            try
            {
                var probe = await probeService.ProbeAsync(meta.FilePath);
                return (slot.SlotIndex, Probe: probe);
            }
            catch (Exception ex)
            {
                _log.Error("recording", $"Probe failed for {slot.SlotName}: {ex.Message}");
                return (slot.SlotIndex, Probe: (VideoProbeData?)null);
            }
            finally
            {
                probeStopwatch.Stop();
                AppDiagnosticLogger.Recording($"RECORDING_PROBE_DONE {slot.SlotName} elapsedMs={probeStopwatch.ElapsedMilliseconds}");
            }
        }).ToList();

        var probeResults = await Task.WhenAll(probeTasks);
        var probeMap = probeResults.ToDictionary(r => r.SlotIndex, r => r.Probe);

        foreach (var slot in slots)
        {
            if (!_metadata.TryGetValue(slot.SlotIndex, out var meta)) continue;

            var camDir = _folders.CameraFolder(SessionPath, slot.SlotIndex);
            var openCvStats = slot.LastOpenCvRecordingStats;
            probeMap.TryGetValue(slot.SlotIndex, out var probe);

            if (openCvStats != null)
            {
                var stats = openCvStats with
                {
                    AppName = meta.AppName,
                    AppVersion = meta.AppVersion,
                    BuildNumber = meta.BuildNumber,
                    ReleaseStage = meta.ReleaseStage,
                    SessionName = meta.SessionName,
                    SessionTitleOriginal = meta.SessionTitleOriginal,
                    SessionFolderName = meta.SessionFolderName,
                    RecordingDateTimeLocal = meta.RecordingDateTimeLocal ?? timing.StartLocal,
                    CameraHardwareId = meta.CameraHardwareId,
                    RecordingApi = meta.RecordingApi,
                    Backend = meta.Backend,
                    DeviceId = meta.DeviceId,
                    RequestedResolution = meta.RequestedResolution,
                    SelectedResolution = meta.SelectedResolution,
                    SelectedDeviceFps = meta.SelectedDeviceFps > 0 ? meta.SelectedDeviceFps : openCvStats.SelectedDeviceFps,
                    RecordingWriterFps = meta.RecordingWriterFps > 0 ? meta.RecordingWriterFps : openCvStats.RecordingWriterFps,
                    SessionStartLocalTime = timing.StartLocal,
                    SessionStartUtcTime = timing.StartUtc,
                    SessionStartMonotonicTicks = timing.StartMonotonicTicks,
                    SessionStartMonotonicSec = timing.StartMonotonicSeconds,
                    SessionStopLocalTime = timing.StopLocalFromMonotonic,
                    SessionStopUtcTime = timing.StopUtcFromMonotonic,
                    SessionStopMonotonicTicks = timing.StopMonotonicTicks,
                    SessionStopMonotonicSec = timing.StopMonotonicSeconds,
                    SessionWallClockDurationSec = timing.WallClockDurationSeconds,
                    SessionMonotonicDurationSec = timing.MonotonicDurationSeconds,
                    ComputerName = meta.ComputerName,
                    CpuName = meta.CpuName,
                    RamGb = meta.RamGb
                };

                ApplyStatsToMetadata(meta, stats, probe, timing, slot);
                meta.RecordingDiagnostics = diagnosticsMonitor?.BuildMetadataSummaryForCamera(slot.SlotIndex + 1);
                processedMetas.Add(meta);
                pendingWrites.Add((camDir, meta, stats, probe));
            }
            else
            {
                meta.RecordingStopTime = timing.StopUtcFromMonotonic;
                meta.MonotonicDuration = timing.MonotonicElapsed;
                meta.TotalDuration = timing.MonotonicElapsed;
                meta.SelectedDeviceFps = slot.SelectedDeviceFps;
                meta.RecordingWriterFps = slot.RecordingWriterFps > 0 ? slot.RecordingWriterFps : slot.SelectedDeviceFps;
                meta.RequestedFps = meta.RequestedFps > 0 ? meta.RequestedFps : slot.RequestedFps;
                meta.ActualFps = probe?.Fps > 0 ? probe.Fps : slot.FpsMonitor.AverageFps;
                meta.FrameCount = probe?.FrameCount ?? slot.FpsMonitor.FrameCount;
                meta.DroppedFrames = slot.FpsMonitor.DroppedFrames;
                meta.FramesCaptured = slot.CaptureFrameCount;
                meta.WallDurationSeconds = timing.MonotonicElapsed.TotalSeconds;
                meta.WallClockDurationSeconds = meta.WallDurationSeconds;
                meta.FrameBasedDurationSeconds = meta.RecordingWriterFps > 0
                    ? meta.FrameCount / meta.RecordingWriterFps
                    : timing.MonotonicElapsed.TotalSeconds;
                meta.ContainerDurationSeconds = probe?.DurationSeconds > 0
                    ? probe.DurationSeconds
                    : meta.FrameBasedDurationSeconds;
                meta.ContainerVsWallClockDifferenceSeconds = RecordingTimingMetrics.ComputeContainerVsWallClockDifference(
                    meta.ContainerDurationSeconds, meta.WallClockDurationSeconds);
                meta.TimestampDriftSeconds = meta.ContainerVsWallClockDifferenceSeconds;
                meta.SelectedFps = meta.SelectedDeviceFps;
                meta.WriterFps = meta.RecordingWriterFps;
                meta.ContainerFps = probe?.Fps > 0 ? probe.Fps : meta.RecordingWriterFps;
                meta.MeasuredCameraFps = RecordingTimingMetrics.ComputeMeasuredCameraFps(
                    meta.FramesCaptured, meta.WallClockDurationSeconds);
                meta.EffectivePlaybackFps = RecordingTimingMetrics.ComputeEffectivePlaybackFps(
                    meta.FrameCount, meta.FrameBasedDurationSeconds);
                var winRtTiming = ScientificTimingAssessor.Assess(new ScientificTimingInput
                {
                    VideoReadable = probe?.Success == true,
                    HasMetadata = true,
                    FramesWritten = meta.FrameCount,
                    FramesCaptured = meta.FramesCaptured,
                    QueueDrops = meta.WriterQueueDrops,
                    DuplicateFrames = meta.DuplicatedFrames + meta.DuplicateFrames,
                    PlaceholderFrames = meta.PlaceholderFrames,
                    ConstantFrameCountMode = meta.ConstantFrameCountMode,
                    OriginalCaptureMode = meta.OriginalCaptureMode,
                    RequestedFps = meta.RequestedFps,
                    Width = meta.PixelWidth,
                    Height = meta.PixelHeight,
                    WriterFps = meta.WriterFps,
                    ContainerFps = meta.ContainerFps,
                    MeasuredCameraFps = meta.MeasuredCameraFps,
                    CaptureIntervalCount = meta.CaptureIntervalCount,
                    CaptureIntervalStdMs = meta.CaptureIntervalStdMs,
                    CaptureIntervalP99Ms = meta.CaptureIntervalP99Ms,
                    ExpectedIntervalMs = meta.ExpectedIntervalMs,
                    LongGapCount = meta.LongGapCount,
                    SevereLongGapCount = meta.SevereLongGapCount,
                    FpsStabilityGrade = meta.FpsStabilityGrade,
                    RequireFrameTimestampCsvValidation = meta.OriginalCaptureMode && !string.IsNullOrWhiteSpace(meta.FrameTimestampCsvPath),
                    FrameTimestampCsvWritten = meta.FrameTimestampCsvWritten,
                    FrameTimestampCsvRowCount = meta.FrameTimestampCsvRowCount,
                    MaxConsecutiveNoFrame = meta.MaxConsecutiveNoFrame
                });
                meta.ScientificTimingStatus = winRtTiming.Status;
                meta.ScientificTimingMessage = winRtTiming.Message;
                meta.TimestampPrecision = "UTC wall clock + monotonic QPC";
                meta.TimingAccuracy = MonotonicClock.PrecisionLabel;
                meta.CameraReleasedOnStop = _config.ReleaseCamerasOnStopPreview;
                ApplySessionTiming(meta, timing);
                meta.RecordingRequestedStartLocalTime = meta.RecordingStartTimeLocal;
                meta.RecordingRequestedStartUtcTime = meta.RecordingStartTime;
                meta.RecordingRequestedStartMonotonicSec = timing.StartMonotonicSeconds;
                meta.CameraRecordingStartLocalTime = meta.RecordingStartTimeLocal;
                meta.CameraRecordingStartUtcTime = meta.RecordingStartTime;
                meta.CameraRecordingStartMonotonicSec = timing.StartMonotonicSeconds;
                meta.FirstFrameLocalTime = meta.RecordingStartTimeLocal;
                meta.FirstFrameUtcTime = meta.RecordingStartTime;
                meta.FirstFrameMonotonicSec = timing.StartMonotonicSeconds;
                meta.LastFrameLocalTime = timing.StopLocalFromMonotonic;
                meta.LastFrameUtcTime = timing.StopUtcFromMonotonic;
                meta.LastFrameMonotonicSec = timing.StopMonotonicSeconds;
                meta.CameraRecordingStopLocalTime = timing.StopLocalFromMonotonic;
                meta.CameraRecordingStopUtcTime = timing.StopUtcFromMonotonic;
                meta.CameraRecordingStopMonotonicSec = timing.StopMonotonicSeconds;
                meta.WriterClosedLocalTime = timing.StopLocalFromMonotonic;
                meta.WriterClosedUtcTime = timing.StopUtcFromMonotonic;
                meta.WriterClosedMonotonicSec = timing.StopMonotonicSeconds;
                meta.CameraStartMonotonicSeconds = meta.CameraRecordingStartMonotonicSec;
                meta.FirstFrameMonotonicSeconds = meta.FirstFrameMonotonicSec;
                meta.LastFrameMonotonicSeconds = meta.LastFrameMonotonicSec;
                meta.StopRequestedMonotonicSeconds = meta.CameraRecordingStopMonotonicSec;
                meta.WriterClosedMonotonicSeconds = meta.WriterClosedMonotonicSec;
                meta.RecordingDiagnostics = diagnosticsMonitor?.BuildMetadataSummaryForCamera(slot.SlotIndex + 1);
                processedMetas.Add(meta);
                pendingMetaWrites.Add((camDir, meta));
            }
        }

        ApplySessionLevelTiming(processedMetas);
        FinalizeScientificTiming(processedMetas);

        foreach (var (camDir, meta) in pendingMetaWrites)
            await writer.WriteCameraMetadataAsync(camDir, meta);

        foreach (var (camDir, meta, stats, probe) in pendingWrites)
        {
            if (stats == null) continue;
            var finalStats = BuildFinalRecordingStats(meta, stats, probe);
            await writer.WriteFromRecordingStatsAsync(camDir, finalStats, _config.LocomotorStandardMode);
            _log.Info("metadata",
                $"{meta.CameraSlot} metadata written: {finalStats.FramesWritten} frames, {finalStats.WallClockDurationSeconds:F2}s @ {finalStats.MeasuredCameraFps:F1} fps, queueDrops={finalStats.WriterQueueDrops}, duplicates={finalStats.DuplicateFrames}, placeholders={finalStats.PlaceholderFrames}");
        }

        WriteStopSummaryToSessionLog(processedMetas, slots, timing);

        var sessionLogPath = _recordingSessionLog?.LogFilePath;
        _recordingSessionLog?.Dispose();
        _recordingSessionLog = null;

        AppDiagnosticLogger.Recording($"STOP session={SessionPath} slots={processedMetas.Count}");
        AppDiagnosticLogger.Runtime("Stop Recording completed");
        if (!string.IsNullOrEmpty(sessionLogPath))
            _log.Info("recording", $"Recording session log: {sessionLogPath}");

        await new SessionSummaryWriter().WriteAsync(
            SessionPath,
            SessionName,
            SessionTitleOriginal,
            SessionFolderName,
            RecordingDateTimeLocal ?? DateTime.Now,
            _metadata.Values);

        if (processedMetas.Count >= 2)
        {
            var diagSlots = new List<CamMultiCameraDiagnosticsReport.SlotDiagnostics>();
            foreach (var slot in slots)
            {
                var stats = slot.LastOpenCvRecordingStats;
                if (stats == null) continue;
                diagSlots.Add(new CamMultiCameraDiagnosticsReport.SlotDiagnostics
                {
                    Slot = slot,
                    Stats = stats
                });
            }

            if (diagSlots.Count >= 2)
            {
                var reportPath = CamMultiCameraDiagnosticsReport.WriteReport(
                    SessionPath, diagSlots, _lastRecordingStartDiagnosticsPath);
                _log.Info("recording", $"Multi-camera diagnostics report: {reportPath}");
                AppDiagnosticLogger.Recording($"MULTICAM_REPORT path={reportPath}");
            }
        }

        _log.Info("metadata", "Session metadata saved");
        stopTotalStopwatch.Stop();
        AppDiagnosticLogger.Recording($"RECORDING_STOP_DONE elapsedMs={stopTotalStopwatch.ElapsedMilliseconds}");
    }

    private static void ApplyStatsToMetadata(
        CameraRecordingMetadata meta,
        RecordingCameraStats stats,
        VideoProbeData? probe,
        RecordingTimingSnapshot timing,
        CameraSlotPipeline slot)
    {
        ApplySessionTiming(meta, timing);
        meta.Resolution = stats.Resolution;
        meta.RecordingTimingMode = stats.RecordingTimingMode;
        meta.OriginalCaptureMode = stats.OriginalCaptureMode;
        meta.RequestedResolution = stats.RequestedResolution;
        meta.SelectedResolution = stats.SelectedResolution;
        meta.PixelWidth = stats.Width;
        meta.PixelHeight = stats.Height;
        meta.RequestedFps = stats.RequestedFps;
        meta.SelectedDeviceFps = stats.SelectedDeviceFps;
        meta.RecordingWriterFps = stats.RecordingWriterFps;
        meta.ActualFps = stats.MeasuredWriterFps;
        var normalizedFps = NormalizeFrameRate(stats.RecordingWriterFps > 0 ? stats.RecordingWriterFps : stats.MeasuredWriterFps);
        meta.FrameRateNumerator = normalizedFps.Numerator;
        meta.FrameRateDenominator = normalizedFps.Denominator;
        meta.FrameCount = stats.FramesWritten;
        meta.FilePath = stats.OutputFilePath;
        meta.RecordingStartTime = stats.StartWallClockUtc;
        meta.RecordingStartTimeLocal = stats.StartWallClockLocal;
        meta.RecordingStopTime = stats.StopWallClockUtc;
        meta.MonotonicDuration = stats.MonotonicDuration;
        meta.TotalDuration = stats.MonotonicDuration;
        meta.FramesCaptured = stats.FramesCaptured;
        meta.DuplicatedFrames = stats.DuplicateFrames;
        meta.DuplicateFrames = stats.DuplicateFrames;
        meta.PlaceholderFrames = stats.PlaceholderFrames;
        meta.ConstantFrameCountMode = stats.ConstantFrameCountMode;
        meta.WriterQueueDrops = stats.WriterQueueDrops;
        meta.RecordingRequestedStartLocalTime = stats.RecordingRequestedStartLocalTime;
        meta.RecordingRequestedStartUtcTime = stats.RecordingRequestedStartUtcTime;
        meta.RecordingRequestedStartMonotonicSec = stats.RecordingRequestedStartMonotonicSec;
        meta.CameraRecordingStartLocalTime = stats.CameraRecordingStartLocalTime;
        meta.CameraRecordingStartUtcTime = stats.CameraRecordingStartUtcTime;
        meta.CameraRecordingStartMonotonicSec = stats.CameraRecordingStartMonotonicSec;
        meta.FirstFrameLocalTime = stats.FirstFrameLocalTime;
        meta.FirstFrameUtcTime = stats.FirstFrameUtcTime;
        meta.FirstFrameMonotonicSec = stats.FirstFrameMonotonicSec > 0 ? stats.FirstFrameMonotonicSec : stats.FirstFrameMonotonicSeconds;
        meta.LastFrameLocalTime = stats.LastFrameLocalTime;
        meta.LastFrameUtcTime = stats.LastFrameUtcTime;
        meta.LastFrameMonotonicSec = stats.LastFrameMonotonicSec > 0 ? stats.LastFrameMonotonicSec : stats.LastFrameMonotonicSeconds;
        meta.CameraRecordingStopLocalTime = stats.CameraRecordingStopLocalTime;
        meta.CameraRecordingStopUtcTime = stats.CameraRecordingStopUtcTime;
        meta.CameraRecordingStopMonotonicSec = stats.CameraRecordingStopMonotonicSec > 0 ? stats.CameraRecordingStopMonotonicSec : stats.StopRequestedMonotonicSeconds;
        meta.WriterClosedLocalTime = stats.WriterClosedLocalTime;
        meta.WriterClosedUtcTime = stats.WriterClosedUtcTime;
        meta.WriterClosedMonotonicSec = stats.WriterClosedMonotonicSec > 0 ? stats.WriterClosedMonotonicSec : stats.WriterClosedMonotonicSeconds;
        meta.SessionStartMonotonicSeconds = stats.SessionStartMonotonicSec > 0 ? stats.SessionStartMonotonicSec : timing.StartMonotonicSeconds;
        meta.CameraStartMonotonicSeconds = meta.CameraRecordingStartMonotonicSec;
        meta.FirstFrameMonotonicSeconds = meta.FirstFrameMonotonicSec;
        meta.LastFrameMonotonicSeconds = meta.LastFrameMonotonicSec;
        meta.StopRequestedMonotonicSeconds = meta.CameraRecordingStopMonotonicSec;
        meta.WriterClosedMonotonicSeconds = meta.WriterClosedMonotonicSec;
        meta.WallDurationSeconds = stats.WallClockDurationSeconds > 0 ? stats.WallClockDurationSeconds : stats.WallDurationSeconds;
        meta.WallClockDurationSeconds = meta.WallDurationSeconds;
        meta.FrameBasedDurationSeconds = stats.FrameBasedDurationSeconds;
        meta.ContainerDurationSeconds = probe?.DurationSeconds > 0
            ? probe.DurationSeconds
            : stats.FrameBasedDurationSeconds;
        meta.ContainerVsWallClockDifferenceSeconds = RecordingTimingMetrics.ComputeContainerVsWallClockDifference(
            meta.ContainerDurationSeconds, meta.WallClockDurationSeconds);
        meta.TimestampDriftSeconds = meta.ContainerVsWallClockDifferenceSeconds;
        meta.TrimRecommendedTimeSource = stats.TrimRecommendedTimeSource;
        meta.TrimWarning = FrameTimestampTrimmingHelper.GetTrimWarning(meta.ContainerVsWallClockDifferenceSeconds);
        if (string.IsNullOrWhiteSpace(meta.TrimWarning))
            meta.TrimWarning = stats.TrimWarning;
        meta.ScientificTrimStartReference = stats.ScientificTrimStartReference;
        meta.ScientificTrimEndReference = stats.ScientificTrimEndReference;
        meta.SupportsTimestampBasedTrimming = stats.SupportsTimestampBasedTrimming;
        meta.SelectedFps = stats.SelectedFps > 0 ? stats.SelectedFps : stats.SelectedDeviceFps;
        meta.WriterFps = stats.WriterFps > 0 ? stats.WriterFps : stats.RecordingWriterFps;
        meta.ContainerFps = stats.ContainerFps > 0 ? stats.ContainerFps : meta.WriterFps;
        meta.MeasuredCameraFps = stats.MeasuredCameraFps > 0 ? stats.MeasuredCameraFps : stats.MeasuredWriterFps;
        meta.EffectivePlaybackFps = stats.EffectivePlaybackFps > 0
            ? stats.EffectivePlaybackFps
            : RecordingTimingMetrics.ComputeEffectivePlaybackFps(meta.FrameCount, meta.FrameBasedDurationSeconds);
        meta.AverageCaptureIntervalMs = stats.AverageCaptureIntervalMs;
        meta.MinCaptureIntervalMs = stats.MinCaptureIntervalMs;
        meta.MaxCaptureIntervalMs = stats.MaxCaptureIntervalMs;
        meta.CaptureJitterMs = stats.CaptureJitterMs;
        meta.CaptureIntervalMeanMs = stats.CaptureIntervalMeanMs > 0 ? stats.CaptureIntervalMeanMs : stats.AverageCaptureIntervalMs;
        meta.CaptureIntervalMedianMs = stats.CaptureIntervalMedianMs;
        meta.CaptureIntervalMinMs = stats.CaptureIntervalMinMs > 0 ? stats.CaptureIntervalMinMs : stats.MinCaptureIntervalMs;
        meta.CaptureIntervalMaxMs = stats.CaptureIntervalMaxMs > 0 ? stats.CaptureIntervalMaxMs : stats.MaxCaptureIntervalMs;
        meta.CaptureIntervalP95Ms = stats.CaptureIntervalP95Ms;
        meta.CaptureIntervalP99Ms = stats.CaptureIntervalP99Ms;
        meta.CaptureIntervalStdMs = stats.CaptureIntervalStdMs > 0 ? stats.CaptureIntervalStdMs : stats.CaptureJitterMs;
        meta.CaptureIntervalCount = stats.CaptureIntervalCount;
        meta.CaptureIntervalStatsMessage = stats.CaptureIntervalStatsMessage;
        meta.MeasuredCameraFpsFromFirstLastFrame = stats.MeasuredCameraFpsFromFirstLastFrame;
        meta.MeasuredCameraFpsFromMeanInterval = stats.MeasuredCameraFpsFromMeanInterval;
        meta.ExpectedIntervalMs = stats.ExpectedIntervalMs;
        meta.RequestedExpectedIntervalMs = stats.RequestedExpectedIntervalMs;
        meta.MeanIntervalErrorMs = stats.MeanIntervalErrorMs;
        meta.AbsoluteMeanIntervalErrorMs = stats.AbsoluteMeanIntervalErrorMs;
        meta.LongGapCount = stats.LongGapCount;
        meta.ShortGapCount = stats.ShortGapCount;
        meta.SevereLongGapCount = stats.SevereLongGapCount;
        meta.JitterScoreMs = stats.JitterScoreMs;
        meta.FpsStabilityGrade = stats.FpsStabilityGrade;
        meta.MaxConsecutiveLateFrames = stats.MaxConsecutiveLateFrames;
        meta.MaxConsecutiveNoFrame = stats.MaxConsecutiveNoFrame;
        meta.ScientificTimingStatus = !string.IsNullOrWhiteSpace(stats.ScientificTimingStatus)
            ? stats.ScientificTimingStatus
            : stats.ExperimentResult;
        meta.ScientificTimingMessage = stats.ScientificTimingMessage;
        meta.RecommendedAction = !string.IsNullOrWhiteSpace(stats.RecommendedAction)
            ? stats.RecommendedAction
            : OriginalCaptureAuditPolicy.SessionInterpretation;
        meta.Codec = stats.Codec;
        meta.ContainerFormat = stats.Container;
        meta.VideoSubtype = stats.Codec;
        meta.TimestampPrecision =
            "UTC wall clock (per-camera first/last frame) + monotonic QPC (per-camera writer)";
        meta.TimingAccuracy = MonotonicClock.PrecisionLabel;
        meta.Backend = string.Equals(stats.RecordingApi, "OpenCV-VideoWriter", StringComparison.OrdinalIgnoreCase)
            ? "OpenCV-DSHOW"
            : "WinRT";
        meta.DeviceId = slot.HardwareId ?? "";
        meta.DeviceIndex = stats.DeviceIndex;
        meta.SelectedResolution = stats.Resolution;
        meta.ComputerName = Environment.MachineName;
        meta.CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
        meta.RamGb = GetRamGb();
        ApplyFocusStatusToMetadata(meta, new CameraFocusControlStatus
        {
            AutoFocusRequested = stats.AutoFocusRequested,
            AutoFocusApplyAttempted = stats.AutoFocusApplyAttempted,
            AutoFocusApplySucceeded = stats.AutoFocusApplySucceeded,
            AutoFocusReadbackValue = stats.AutoFocusReadbackValue,
            ManualFocusSupported = stats.ManualFocusSupported,
            ManualFocusRequestedValue = stats.ManualFocusRequestedValue,
            ManualFocusReadbackValue = stats.ManualFocusReadbackValue,
            FocusControlMode = stats.FocusControlMode,
            FocusWarning = stats.FocusWarning
        });
        ApplyExposureStatusToMetadata(meta, new CameraExposureControlStatus
        {
            AutoExposureRequested = stats.AutoExposureRequested,
            AutoExposureApplyAttempted = stats.AutoExposureApplyAttempted,
            AutoExposureApplySucceeded = stats.AutoExposureApplySucceeded,
            AutoExposureReadbackValue = stats.AutoExposureReadbackValue,
            ManualExposureSupported = stats.ManualExposureSupported,
            ManualExposureRequestedValue = stats.ManualExposureRequestedValue,
            ManualExposureReadbackValue = stats.ManualExposureReadbackValue,
            LowLightCompensationOffRequested = stats.LowLightCompensationOffRequested,
            LowLightCompensationOffConfirmed = stats.LowLightCompensationOffConfirmed,
            ExposureWarning = stats.ExposureWarning
        });
    }

    private static void ApplyFocusStatusToMetadata(CameraRecordingMetadata meta, CameraFocusControlStatus focus)
    {
        meta.AutoFocusRequested = focus.AutoFocusRequested;
        meta.AutoFocusApplyAttempted = focus.AutoFocusApplyAttempted;
        meta.AutoFocusApplySucceeded = focus.AutoFocusApplySucceeded;
        meta.AutoFocusReadbackValue = focus.AutoFocusReadbackValue;
        meta.ManualFocusSupported = focus.ManualFocusSupported;
        meta.ManualFocusRequestedValue = focus.ManualFocusRequestedValue;
        meta.ManualFocusReadbackValue = focus.ManualFocusReadbackValue;
        meta.FocusControlMode = focus.FocusControlMode;
        meta.FocusWarning = focus.FocusWarning;
    }

    private static void ApplyExposureStatusToMetadata(CameraRecordingMetadata meta, CameraExposureControlStatus exposure)
    {
        meta.AutoExposureRequested = exposure.AutoExposureRequested;
        meta.AutoExposureApplyAttempted = exposure.AutoExposureApplyAttempted;
        meta.AutoExposureApplySucceeded = exposure.AutoExposureApplySucceeded;
        meta.AutoExposureReadbackValue = exposure.AutoExposureReadbackValue;
        meta.ManualExposureSupported = exposure.ManualExposureSupported;
        meta.ManualExposureRequestedValue = exposure.ManualExposureRequestedValue;
        meta.ManualExposureReadbackValue = exposure.ManualExposureReadbackValue;
        meta.LowLightCompensationOffRequested = exposure.LowLightCompensationOffRequested;
        meta.LowLightCompensationOffConfirmed = exposure.LowLightCompensationOffConfirmed;
        meta.ExposureWarning = exposure.ExposureWarning;
    }

    private static void ApplySessionTiming(CameraRecordingMetadata meta, RecordingTimingSnapshot timing)
    {
        meta.SessionStartLocalTime = timing.StartLocal;
        meta.SessionStartUtcTime = timing.StartUtc;
        meta.SessionStartMonotonicTicks = timing.StartMonotonicTicks;
        meta.SessionStartMonotonicSec = timing.StartMonotonicSeconds;
        meta.SessionStopLocalTime = timing.StopLocalFromMonotonic;
        meta.SessionStopUtcTime = timing.StopUtcFromMonotonic;
        meta.SessionStopMonotonicTicks = timing.StopMonotonicTicks;
        meta.SessionStopMonotonicSec = timing.StopMonotonicSeconds;
        meta.SessionWallClockDurationSec = timing.WallClockDurationSeconds;
        meta.SessionMonotonicDurationSec = timing.MonotonicDurationSeconds;
    }

    private static void ApplySessionLevelTiming(IReadOnlyList<CameraRecordingMetadata> metas)
    {
        if (metas.Count == 0) return;

        var activeCameraCount = metas.Count;
        var activeCameraSlots = metas.OrderBy(m => m.CameraSlot, StringComparer.OrdinalIgnoreCase)
            .Select(m => m.CameraSlot).ToArray();

        var frameBased = metas.Where(m => m.FrameBasedDurationSeconds > 0).Select(m => m.FrameBasedDurationSeconds).ToList();
        var frames = metas.Where(m => m.FrameCount > 0).Select(m => m.FrameCount).ToList();
        var durationDiff = frameBased.Count >= 2 ? frameBased.Max() - frameBased.Min() : 0;
        var frameDiff = frames.Count >= 2 ? frames.Max() - frames.Min() : 0;
        var firstFrames = metas.Where(m => m.FirstFrameMonotonicSeconds > 0).Select(m => m.FirstFrameMonotonicSeconds).ToList();
        var lastFrames = metas.Where(m => m.LastFrameMonotonicSeconds > 0).Select(m => m.LastFrameMonotonicSeconds).ToList();
        // Only compute inter-camera offsets when 2+ cameras are active; for single-camera they are not applicable.
        var startOffsetMs = activeCameraCount >= 2 ? RecordingTimingMetrics.ComputeInterCameraStartOffsetMs(firstFrames) : 0;
        var stopOffsetMs = activeCameraCount >= 2 ? RecordingTimingMetrics.ComputeInterCameraStopOffsetMs(lastFrames) : 0;

        foreach (var meta in metas)
        {
            meta.ActiveCameraCount = activeCameraCount;
            meta.ActiveCameraSlots = activeCameraSlots;
            meta.InterCameraDurationDiffSec = durationDiff;
            meta.InterCameraFrameDiff = frameDiff;
            meta.InterCameraStartOffsetMs = startOffsetMs;
            meta.InterCameraStopOffsetMs = stopOffsetMs;
        }
    }

    private static void FinalizeScientificTiming(IReadOnlyList<CameraRecordingMetadata> metas)
    {
        foreach (var meta in metas)
        {
            var assessed = ScientificTimingAssessor.Assess(new ScientificTimingInput
            {
                VideoReadable = meta.FrameCount > 0,
                HasMetadata = true,
                FramesWritten = meta.FrameCount,
                FramesCaptured = meta.FramesCaptured,
                QueueDrops = meta.WriterQueueDrops,
                DuplicateFrames = meta.DuplicatedFrames + meta.DuplicateFrames,
                PlaceholderFrames = meta.PlaceholderFrames,
                ConstantFrameCountMode = meta.ConstantFrameCountMode,
                OriginalCaptureMode = meta.OriginalCaptureMode,
                RequestedFps = meta.RequestedFps,
                Width = meta.PixelWidth,
                Height = meta.PixelHeight,
                WriterFps = meta.WriterFps > 0 ? meta.WriterFps : meta.RecordingWriterFps,
                ContainerFps = meta.ContainerFps > 0 ? meta.ContainerFps : meta.RecordingWriterFps,
                MeasuredCameraFps = meta.MeasuredCameraFps,
                ActiveCameraCount = meta.ActiveCameraCount > 0 ? meta.ActiveCameraCount : metas.Count,
                InterCameraFrameDifference = meta.InterCameraFrameDiff,
                InterCameraStartOffsetMs = meta.InterCameraStartOffsetMs,
                CaptureIntervalCount = meta.CaptureIntervalCount,
                CaptureIntervalStdMs = meta.CaptureIntervalStdMs,
                CaptureIntervalP99Ms = meta.CaptureIntervalP99Ms,
                ExpectedIntervalMs = meta.ExpectedIntervalMs,
                LongGapCount = meta.LongGapCount,
                SevereLongGapCount = meta.SevereLongGapCount,
                FpsStabilityGrade = meta.FpsStabilityGrade,
                RequireFrameTimestampCsvValidation = meta.OriginalCaptureMode && !string.IsNullOrWhiteSpace(meta.FrameTimestampCsvPath),
                FrameTimestampCsvWritten = meta.FrameTimestampCsvWritten,
                FrameTimestampCsvRowCount = meta.FrameTimestampCsvRowCount,
                MaxConsecutiveNoFrame = meta.MaxConsecutiveNoFrame
            });

            if (string.Equals(meta.ScientificTimingStatus, "FAIL", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(assessed.Status, "FAIL", StringComparison.OrdinalIgnoreCase))
            {
                meta.ScientificTimingStatus = assessed.Status;
                meta.ScientificTimingMessage = assessed.Message;
                continue;
            }

            if (string.IsNullOrWhiteSpace(meta.ScientificTimingStatus))
                meta.ScientificTimingStatus = assessed.Status;
            else if (string.Equals(meta.ScientificTimingStatus, "PASS", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(assessed.Status, "PASS_WITH_WARNING", StringComparison.OrdinalIgnoreCase))
                meta.ScientificTimingStatus = assessed.Status;

            if (string.IsNullOrWhiteSpace(meta.ScientificTimingMessage))
                meta.ScientificTimingMessage = assessed.Message;
            if (string.IsNullOrWhiteSpace(meta.RecommendedAction))
                meta.RecommendedAction = OriginalCaptureAuditPolicy.SessionInterpretation;
        }
    }

    private static RecordingCameraStats BuildFinalRecordingStats(
        CameraRecordingMetadata meta,
        RecordingCameraStats stats,
        VideoProbeData? probe)
    {
        if (probe?.DurationSeconds > 0)
        {
            var containerDuration = probe.DurationSeconds;
            var containerVsWall = RecordingTimingMetrics.ComputeContainerVsWallClockDifference(
                containerDuration, stats.WallClockDurationSeconds > 0 ? stats.WallClockDurationSeconds : stats.WallDurationSeconds);
            stats = stats with
            {
                ContainerDurationSeconds = containerDuration,
                ContainerFps = probe.Fps > 0 ? probe.Fps : stats.ContainerFps,
                ContainerVsWallClockDifferenceSeconds = containerVsWall,
                TimestampDriftSeconds = containerVsWall,
                TrimWarning = FrameTimestampTrimmingHelper.GetTrimWarning(containerVsWall),
                EffectivePlaybackFps = probe.Fps > 0
                    ? probe.Fps
                    : RecordingTimingMetrics.ComputeEffectivePlaybackFps(stats.FramesWritten, containerDuration)
            };
        }

        var assessed = ScientificTimingAssessor.Assess(new ScientificTimingInput
        {
            VideoReadable = probe?.Success != false && stats.FramesWritten > 0,
            HasMetadata = true,
            FramesWritten = stats.FramesWritten,
            FramesCaptured = stats.FramesCaptured,
            QueueDrops = stats.WriterQueueDrops,
            DuplicateFrames = stats.DuplicateFrames,
            PlaceholderFrames = stats.PlaceholderFrames,
            ConstantFrameCountMode = stats.ConstantFrameCountMode,
            OriginalCaptureMode = stats.OriginalCaptureMode,
            RequestedFps = stats.RequestedFps,
            Width = stats.Width,
            Height = stats.Height,
            WriterFps = stats.WriterFps > 0 ? stats.WriterFps : stats.RecordingWriterFps,
            ContainerFps = stats.ContainerFps > 0 ? stats.ContainerFps : stats.RecordingWriterFps,
            MeasuredCameraFps = stats.MeasuredCameraFps > 0 ? stats.MeasuredCameraFps : stats.MeasuredWriterFps,
            InterCameraFrameDifference = meta.InterCameraFrameDiff,
            InterCameraStartOffsetMs = meta.InterCameraStartOffsetMs,
            CaptureIntervalCount = stats.CaptureIntervalCount,
            CaptureIntervalStdMs = stats.CaptureIntervalStdMs,
            CaptureIntervalP99Ms = stats.CaptureIntervalP99Ms,
            ExpectedIntervalMs = stats.ExpectedIntervalMs,
            LongGapCount = stats.LongGapCount,
            SevereLongGapCount = stats.SevereLongGapCount,
            FpsStabilityGrade = stats.FpsStabilityGrade,
            RequireFrameTimestampCsvValidation = stats.OriginalCaptureMode && !string.IsNullOrWhiteSpace(stats.FrameTimestampCsvPath),
            FrameTimestampCsvWritten = stats.FrameTimestampCsvWritten,
            FrameTimestampCsvRowCount = stats.FrameTimestampCsvRowCount,
            MaxConsecutiveNoFrame = stats.MaxConsecutiveNoFrame
        });

        return stats with
        {
            ActiveCameraCount = meta.ActiveCameraCount,
            ActiveCameraSlots = meta.ActiveCameraSlots,
            InterCameraFrameDifference = meta.InterCameraFrameDiff,
            InterCameraStartOffsetMs = meta.InterCameraStartOffsetMs,
            InterCameraStopOffsetMs = meta.InterCameraStopOffsetMs,
            TrimWarning = FrameTimestampTrimmingHelper.GetTrimWarning(stats.ContainerVsWallClockDifferenceSeconds),
            ScientificTimingStatus = !string.IsNullOrWhiteSpace(meta.ScientificTimingStatus)
                ? meta.ScientificTimingStatus
                : assessed.Status,
            ScientificTimingMessage = !string.IsNullOrWhiteSpace(meta.ScientificTimingMessage)
                ? meta.ScientificTimingMessage
                : assessed.Message,
            RecommendedAction = !string.IsNullOrWhiteSpace(meta.RecommendedAction)
                ? meta.RecommendedAction
                : OriginalCaptureAuditPolicy.SessionInterpretation,
            AutoFocusRequested = meta.AutoFocusRequested,
            AutoFocusApplyAttempted = meta.AutoFocusApplyAttempted,
            AutoFocusApplySucceeded = meta.AutoFocusApplySucceeded,
            AutoFocusReadbackValue = meta.AutoFocusReadbackValue,
            ManualFocusSupported = meta.ManualFocusSupported,
            ManualFocusRequestedValue = meta.ManualFocusRequestedValue,
            ManualFocusReadbackValue = meta.ManualFocusReadbackValue,
            FocusControlMode = meta.FocusControlMode,
            FocusWarning = meta.FocusWarning,
            RecordingDiagnostics = meta.RecordingDiagnostics
        };
    }

    private static double GetRamGb()
    {
        try
        {
            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };
            if (!GlobalMemoryStatusEx(ref status))
                return 0;

            return status.TotalPhysicalMemory / 1024d / 1024d / 1024d;
        }
        catch
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static (uint Numerator, uint Denominator) NormalizeFrameRate(double fps)
    {
        if (fps <= 0)
            return (30, 1);

        var common = new (double Rate, uint Num, uint Den)[]
        {
            (23.976, 24000, 1001),
            (24.0, 24, 1),
            (25.0, 25, 1),
            (29.97, 30000, 1001),
            (30.0, 30, 1),
            (50.0, 50, 1),
            (59.94, 60000, 1001),
            (60.0, 60, 1)
        };

        foreach (var rate in common)
        {
            if (Math.Abs(fps - rate.Rate) <= 0.05)
                return (rate.Num, rate.Den);
        }

        var scaled = Math.Max(1, (uint)Math.Round(fps * 1000));
        var denom = 1000u;
        var a = scaled;
        var b = denom;
        while (b != 0)
        {
            var t = a % b;
            a = b;
            b = t;
        }

        var gcd = Math.Max(1u, a);
        return (Math.Max(1u, scaled / gcd), Math.Max(1u, denom / gcd));
    }

    private void WriteStopSummaryToSessionLog(
        IReadOnlyList<CameraRecordingMetadata> processedMetas,
        IReadOnlyList<CameraSlotPipeline> slots,
        RecordingTimingSnapshot timing)
    {
        void Write(RecordingSessionLogSession log)
        {
            log.Section("stop summary");
            log.Line($"wallDurationSec={timing.MonotonicElapsed.TotalSeconds:F3}");
            log.Line($"activeSlots={processedMetas.Count}");
            foreach (var meta in processedMetas.OrderBy(m => m.CameraSlot, StringComparer.OrdinalIgnoreCase))
            {
                var slotStats = slots
                    .FirstOrDefault(s => string.Equals(s.SlotName, meta.CameraSlot, StringComparison.OrdinalIgnoreCase))
                    ?.LastOpenCvRecordingStats;
                var fileSize = !string.IsNullOrEmpty(meta.FilePath) && File.Exists(meta.FilePath)
                    ? new FileInfo(meta.FilePath).Length
                    : 0L;
                log.Line(
                    $"{meta.CameraSlot} device=\"{meta.CameraDeviceName}\" capturePath={GetSlotCapturePath(slots, meta.CameraSlot)} " +
                    $"requested={meta.RequestedResolution} actual={meta.SelectedResolution} " +
                    $"fpsRequested={meta.RequestedFps:F2} fpsMeasured={meta.MeasuredCameraFps:F2} fpsWriter={meta.RecordingWriterFps:F2} " +
                    $"captured={meta.FramesCaptured} written={meta.FrameCount} drops={meta.WriterQueueDrops} " +
                    $"writerDequeued={slotStats?.WriterFramesDequeued ?? 0} queueDepthMax={slotStats?.WriterQueueDepthMax ?? 0} " +
                    $"queueFull={slotStats?.WriterQueueFullCount ?? 0} avgWriteMs={slotStats?.AverageVideoWriterWriteMs ?? 0:F3} " +
                    $"maxWriteMs={slotStats?.MaxVideoWriterWriteMs ?? 0:F3} previewFramesRendered={slotStats?.PreviewFramesRenderedDuringRecording ?? 0} " +
                    $"duplicates={meta.DuplicatedFrames} placeholders={meta.PlaceholderFrames} " +
                    $"captureIntervalMeanMs={meta.CaptureIntervalMeanMs:F2} captureIntervalStdMs={meta.CaptureIntervalStdMs:F2} " +
                    $"wallSec={meta.WallClockDurationSeconds:F3} frameBasedSec={meta.FrameBasedDurationSeconds:F3} " +
                    $"interCamStartOffsetMs={meta.InterCameraStartOffsetMs:F1} interCamStopOffsetMs={meta.InterCameraStopOffsetMs:F1} " +
                    $"interCamFrameDiff={meta.InterCameraFrameDiff} timingStatus={meta.ScientificTimingStatus} " +
                    $"fileBytes={fileSize} metadata=yes");
            }

            if (processedMetas.Count >= 2)
            {
                var frameDiff = processedMetas.Max(m => m.FrameCount) - processedMetas.Min(m => m.FrameCount);
                var wallSpread = processedMetas.Max(m => m.WallClockDurationSeconds) - processedMetas.Min(m => m.WallClockDurationSeconds);
                log.Line($"sessionInterCamera frameDiff={frameDiff} wallDurationSpreadSec={wallSpread:F3}");
            }

            log.Line("recordingStoppedNormally=yes");
            if (!string.IsNullOrEmpty(_lastRecordingStartDiagnosticsPath))
                log.Line($"startupDiagnostics={_lastRecordingStartDiagnosticsPath}");
        }

        if (_recordingSessionLog != null)
        {
            Write(_recordingSessionLog);
            return;
        }

        using var fallback = AppDiagnosticLogger.BeginRecordingSession(SessionPath ?? "");
        Write(fallback);
    }

    private static string GetSlotCapturePath(IReadOnlyList<CameraSlotPipeline> slots, string cameraSlot)
    {
        var slot = slots.FirstOrDefault(s =>
            string.Equals(s.SlotName, cameraSlot, StringComparison.OrdinalIgnoreCase));
        return slot?.OpenCvDevicePathDescription ?? "-";
    }
}
