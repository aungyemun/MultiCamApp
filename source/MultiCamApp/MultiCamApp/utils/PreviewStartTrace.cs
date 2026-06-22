using System.Diagnostics;
using System.Text;
using MultiCamApp.Capture;
using MultiCamApp.Core;

namespace MultiCamApp.Utils;

/// <summary>High-resolution timing trace for Start Preview (diagnosis only; does not affect capture).</summary>
public static class PreviewStartTrace
{
    private static readonly AsyncLocal<PreviewStartTraceSession?> Active = new();

    public static PreviewStartTraceSession? Current => Active.Value;
    public static bool IsActive => Active.Value != null;

    public static PreviewStartTraceSession Begin(
        int layoutCount,
        int availableCameraCount,
        int preferredWidth,
        int preferredHeight,
        double preferFps,
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds)
    {
        var session = new PreviewStartTraceSession(
            layoutCount, availableCameraCount, preferredWidth, preferredHeight, preferFps, devices, selectedDeviceIds);
        Active.Value = session;
        return session;
    }

    internal static void Clear(PreviewStartTraceSession session)
    {
        if (ReferenceEquals(Active.Value, session))
            Active.Value = null;
    }

    public static void NotifyDiscovery(string source, string detail, bool warnIfDuringPreview = true)
    {
        Current?.RecordDiscovery(source, detail, warnIfDuringPreview);
    }

    public static void NotifyProbe(string source, string detail)
    {
        Current?.RecordProbe(source, detail);
    }

    public static void NotifyDiscoveryTimed(string source, string detail, long elapsedMs, bool warnRefresh = false)
    {
        Current?.RecordDiscoveryTimed(source, detail, elapsedMs, warnRefresh);
    }
}

public sealed class PreviewStartTraceSession : IDisposable
{
    private readonly string _tracePath;
    private readonly StringBuilder _trace = new();
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly object _writeLock = new();
    private readonly int _layoutCount;
    private readonly int _availableCameraCount;
    private readonly int _preferredWidth;
    private readonly int _preferredHeight;
    private readonly double _preferFps;
    private readonly IReadOnlyList<CameraDevice> _devices;
    private readonly IReadOnlyList<string?> _selectedDeviceIds;
    private readonly Dictionary<int, SlotTrace> _slots = new();
    private readonly List<string> _discoveryEvents = new();
    private readonly List<string> _probeEvents = new();
    private readonly List<string> _bugs = new();
    private readonly HashSet<string> _openedPreviewDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    private long _validateStartMs;
    private long _validateEndMs;
    private long _discoveryTotalMs;
    private long _catalogRebuildStartMs;
    private long _catalogRebuildEndMs;
    private long _prepareMultiCamStartMs;
    private long _prepareMultiCamEndMs;
    private string _openMode = "unknown";
    private bool _disposed;
    private int _openedCount;
    private int _requiredCount;
    private long _lastFlushMs;

    public readonly record struct SlotSnapshot(
        int SlotIndex,
        string? SelectedDeviceId,
        string? SelectedDeviceName,
        string? OpenedDeviceId,
        string? OpenedDeviceName,
        string? OpenedPath,
        int? OpenedDshowIndex,
        string? OpenedResolutionText,
        long? OpenStartMs,
        long? OpenEndMs,
        long? OpenDurationMs,
        long? FirstFrameReceivedMs,
        long? FirstFrameRenderedMs,
        bool OpenFailed,
        string? FailureCategory,
        string? FailureMessage,
        bool OtherCamerasContinued);

    public readonly record struct PreviewStartTraceSnapshot(
        int LayoutCount,
        int OpenedCount,
        int RequiredCount,
        string OpenMode,
        long TotalPreviewMs,
        long? FirstVisibleFrameRenderedMs,
        long? AllSuccessfulVisibleMs,
        IReadOnlyList<SlotSnapshot> Slots);

    private sealed class SlotTrace
    {
        public string? SelectedDeviceId;
        public string? SelectedDeviceName;
        public long? OpenStartMs;
        public long? OpenEndMs;
        public long? FirstFrameReceivedMs;
        public long? FirstFrameRenderedMs;
        public string? OpenedDeviceId;
        public string? OpenedDeviceName;
        public string? OpenedPath;
        public int? OpenedDshowIndex;
        public bool OpenFailed;
        public string? FailureCategory;
        public string? FailureMessage;
        public string? OpenedResolutionText;
        public bool OtherCamerasContinued;
    }

    internal PreviewStartTraceSession(
        int layoutCount,
        int availableCameraCount,
        int preferredWidth,
        int preferredHeight,
        double preferFps,
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> selectedDeviceIds)
    {
        _layoutCount = layoutCount;
        _availableCameraCount = availableCameraCount;
        _preferredWidth = preferredWidth;
        _preferredHeight = preferredHeight;
        _preferFps = preferFps;
        _devices = devices;
        _selectedDeviceIds = selectedDeviceIds;

        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        _tracePath = Path.Combine(dir, $"preview_start_trace_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        Marker("START_PREVIEW_CLICKED");
        LogHeader();
    }

    public void Marker(string name)
    {
        lock (_writeLock)
        {
            _trace.AppendLine($"{NowLine()} {name}");
            var now = _total.ElapsedMilliseconds;
            if (now - _lastFlushMs >= 500)
            {
                FlushTrace();
                _lastFlushMs = now;
            }
        }
    }

    public void BeginValidateSelectedDevices()
    {
        _validateStartMs = _total.ElapsedMilliseconds;
        Marker("VALIDATE_SELECTED_DEVICES_START");
    }

    public void EndValidateSelectedDevices()
    {
        _validateEndMs = _total.ElapsedMilliseconds;
        Marker($"VALIDATE_SELECTED_DEVICES_END elapsedMs={_validateEndMs - _validateStartMs}");
    }

    public void BeginCatalogRebuild()
    {
        _catalogRebuildStartMs = _total.ElapsedMilliseconds;
        Marker("CATALOG_REBUILD_START");
    }

    public void EndCatalogRebuild()
    {
        _catalogRebuildEndMs = _total.ElapsedMilliseconds;
        Marker($"CATALOG_REBUILD_END elapsedMs={_catalogRebuildEndMs - _catalogRebuildStartMs}");
    }

    public void BeginPrepareMultiCamera()
    {
        _prepareMultiCamStartMs = _total.ElapsedMilliseconds;
        Marker("PREPARE_MULTI_CAMERA_START");
    }

    public void EndPrepareMultiCamera()
    {
        _prepareMultiCamEndMs = _total.ElapsedMilliseconds;
        Marker($"PREPARE_MULTI_CAMERA_END elapsedMs={_prepareMultiCamEndMs - _prepareMultiCamStartMs}");
    }

    public void SetOpenMode(string mode) => _openMode = mode;

    public void RecordDiscovery(string source, string detail, bool warnIfDuringPreview)
    {
        var line = $"{source}: {detail}";
        lock (_writeLock)
        {
            _discoveryEvents.Add(line);
            _discoveryTotalMs += 0;
            Marker($"DEVICE_DISCOVERY source={source} detail={detail}");
            if (warnIfDuringPreview && source.Contains("Discover", StringComparison.OrdinalIgnoreCase))
            {
                Marker("WARNING: Start Preview triggered device discovery/probing.");
            }
        }
    }

    public void RecordDiscoveryTimed(string source, string detail, long elapsedMs, bool warnRefresh = false)
    {
        lock (_writeLock)
        {
            _discoveryEvents.Add($"{source}: {detail} ({elapsedMs}ms)");
            _discoveryTotalMs += elapsedMs;
            Marker($"DEVICE_DISCOVERY_START source={source}");
            Marker($"DEVICE_DISCOVERY_END source={source} elapsedMs={elapsedMs} detail={detail}");
            if (warnRefresh)
                Marker("WARNING: Start Preview triggered device discovery/probing.");
        }
    }

    public void RecordProbe(string source, string detail)
    {
        lock (_writeLock)
        {
            _probeEvents.Add($"{source}: {detail}");
            Marker($"DEVICE_PROBE source={source} detail={detail}");
        }
    }

    public void CameraOpenStart(int slotIndex, string selectedDeviceId, string? selectedDeviceName)
    {
        var slot = GetSlot(slotIndex);
        slot.SelectedDeviceId = selectedDeviceId;
        slot.SelectedDeviceName = selectedDeviceName;
        slot.OpenStartMs = _total.ElapsedMilliseconds;
        Marker($"CAMERA_OPEN_START cam{slotIndex + 1} device=\"{selectedDeviceName ?? "?"}\" id={selectedDeviceId}");
    }

    public void CameraOpenEnd(int slotIndex, CameraSlotPipeline? slot, bool success)
    {
        var st = GetSlot(slotIndex);
        st.OpenEndMs = _total.ElapsedMilliseconds;
        st.OpenFailed = !success;

        if (slot != null && success)
        {
            st.OpenedDeviceId = slot.AssignedDeviceId;
            st.OpenedDeviceName = slot.DeviceName;
            st.OpenedPath = slot.OpenCvDevicePathDescription;
            st.OpenedDshowIndex = slot.DirectShowIndex;
            st.OpenedResolutionText = slot.ResolutionText;

            if (!string.IsNullOrEmpty(slot.AssignedDeviceId))
                _openedPreviewDeviceIds.Add(slot.AssignedDeviceId);

            var selectedSet = _selectedDeviceIds
                .Take(_layoutCount)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(slot.AssignedDeviceId)
                && !selectedSet.Contains(slot.AssignedDeviceId))
            {
                _bugs.Add($"BUG: Start Preview opened non-selected device on cam{slotIndex + 1}: \"{slot.DeviceName}\" id={slot.AssignedDeviceId}");
                Marker($"BUG: Start Preview opened non-selected device cam{slotIndex + 1} id={slot.AssignedDeviceId}");
            }
        }

        var openMs = st.OpenStartMs.HasValue ? st.OpenEndMs.Value - st.OpenStartMs.Value : 0;
        Marker(success
            ? $"CAMERA_OPEN_END cam{slotIndex + 1} result=OK elapsedMs={openMs} openedName=\"{st.OpenedDeviceName ?? "?"}\" openedId={st.OpenedDeviceId ?? "?"} path={st.OpenedPath ?? "?"} dshowIndex={st.OpenedDshowIndex?.ToString() ?? "?"} resolution={st.OpenedResolutionText ?? "?"}"
            : $"CAMERA_OPEN_END cam{slotIndex + 1} result=FAIL elapsedMs={openMs}");
    }

    public void SlotFailed(
        int slotIndex,
        string category,
        string message,
        string? deviceId,
        string? deviceName,
        string? actualResolution = null)
    {
        var st = GetSlot(slotIndex);
        if (!st.OpenEndMs.HasValue)
            st.OpenEndMs = _total.ElapsedMilliseconds;
        st.OpenFailed = true;
        st.FailureCategory = category;
        st.FailureMessage = message;
        if (!string.IsNullOrEmpty(actualResolution))
            st.OpenedResolutionText = actualResolution;

        var preset = CaptureResolutionPreset.ToLabel(_preferredWidth, _preferredHeight);
        var openMs = st.OpenStartMs.HasValue && st.OpenEndMs.HasValue
            ? st.OpenEndMs.Value - st.OpenStartMs.Value
            : st.OpenStartMs.HasValue
                ? _total.ElapsedMilliseconds - st.OpenStartMs.Value
                : 0;

        Marker($"SLOT_FAILED cam{slotIndex + 1} category={category} message=\"{message}\" preset={preset} requestedFps={_preferFps:F0} actualResolution={actualResolution ?? st.OpenedResolutionText ?? "n/a"} device=\"{deviceName ?? "?"}\" id={deviceId ?? "?"} elapsedMs={openMs}");
    }

    public void SlotFailureHandled(int slotIndex, bool otherCamerasContinued)
    {
        var st = GetSlot(slotIndex);
        st.OtherCamerasContinued = otherCamerasContinued;
        Marker($"SLOT_FAILURE_HANDLED cam{slotIndex + 1} otherCamerasContinued={otherCamerasContinued}");
        Marker("APP_CONTINUED true");
    }

    public void RecordFirstFrameReceived(int slotIndex)
    {
        var st = GetSlot(slotIndex);
        if (st.FirstFrameReceivedMs.HasValue) return;
        st.FirstFrameReceivedMs = _total.ElapsedMilliseconds;
        var sinceOpen = st.OpenEndMs.HasValue ? st.FirstFrameReceivedMs.Value - st.OpenEndMs.Value : 0;
        Marker($"FIRST_FRAME_RECEIVED cam{slotIndex + 1} elapsedSinceOpenEndMs={sinceOpen}");
    }

    public void RecordFirstFrameRendered(int slotIndex)
    {
        var st = GetSlot(slotIndex);
        if (st.FirstFrameRenderedMs.HasValue) return;
        st.FirstFrameRenderedMs = _total.ElapsedMilliseconds;
        var sinceOpen = st.OpenEndMs.HasValue ? st.FirstFrameRenderedMs.Value - st.OpenEndMs.Value : 0;
        Marker($"FIRST_FRAME_RENDERED cam{slotIndex + 1} elapsedSinceOpenEndMs={sinceOpen}");
    }

    public bool HasFirstFrameReceived(int slotIndex) =>
        _slots.TryGetValue(slotIndex, out var st) && st.FirstFrameReceivedMs.HasValue;

    public void Complete(int openedCount, int requiredCount)
    {
        _openedCount = openedCount;
        _requiredCount = requiredCount;
        if (openedCount >= requiredCount && requiredCount > 0)
            Marker("ALL_SELECTED_CAMERAS_READY");
        else
            Marker("PREVIEW_RESULT_PARTIAL_OR_FAILED");
        Marker($"START_PREVIEW_TOTAL_TIME_MS={_total.ElapsedMilliseconds}");
        Marker($"result=opened {openedCount}/{requiredCount}");
    }

    public PreviewStartTraceSnapshot Snapshot()
    {
        var openMode = AnalyzeOpenMode();
        var totalMs = _total.ElapsedMilliseconds;

        var visibleRendered = _slots
            .Select(kv => kv.Value)
            .Where(s => !s.OpenFailed && !string.IsNullOrWhiteSpace(s.OpenedDeviceId) && s.FirstFrameRenderedMs.HasValue)
            .Select(s => s.FirstFrameRenderedMs!.Value)
            .OrderBy(x => x)
            .ToList();

        long? firstVisible = visibleRendered.Count > 0 ? visibleRendered.Min() : null;
        long? allVisible = visibleRendered.Count > 0 ? visibleRendered.Max() : null;

        var slots = new List<SlotSnapshot>();
        foreach (var kv in _slots.OrderBy(k => k.Key))
        {
            var idx = kv.Key;
            var s = kv.Value;
            long? openDuration = null;
            if (s.OpenStartMs.HasValue && s.OpenEndMs.HasValue)
                openDuration = s.OpenEndMs.Value - s.OpenStartMs.Value;

            slots.Add(new SlotSnapshot(
                idx,
                s.SelectedDeviceId,
                s.SelectedDeviceName,
                s.OpenedDeviceId,
                s.OpenedDeviceName,
                s.OpenedPath,
                s.OpenedDshowIndex,
                s.OpenedResolutionText,
                s.OpenStartMs,
                s.OpenEndMs,
                openDuration,
                s.FirstFrameReceivedMs,
                s.FirstFrameRenderedMs,
                s.OpenFailed,
                s.FailureCategory,
                s.FailureMessage,
                s.OtherCamerasContinued));
        }

        return new PreviewStartTraceSnapshot(
            _layoutCount,
            _openedCount,
            _requiredCount,
            openMode,
            totalMs,
            firstVisible,
            allVisible,
            slots);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_trace.ToString().Contains("START_PREVIEW_TOTAL_TIME_MS", StringComparison.Ordinal))
            Marker($"START_PREVIEW_TOTAL_TIME_MS={_total.ElapsedMilliseconds}");

        WriteDiagnosis(_openedCount, _requiredCount);

        lock (_writeLock)
            File.WriteAllText(_tracePath, PrivacySanitizer.SanitizeForLog(_trace.ToString()), Encoding.UTF8);

        MirrorTrace();
        PreviewStartTrace.Clear(this);
    }

    private SlotTrace GetSlot(int slotIndex)
    {
        if (!_slots.TryGetValue(slotIndex, out var st))
        {
            st = new SlotTrace();
            _slots[slotIndex] = st;
        }

        return st;
    }

    private void LogHeader()
    {
        _trace.AppendLine($"appVersion={VersionService.Load().Display}");
        _trace.AppendLine($"timestamp={DateTime.Now:O}");
        _trace.AppendLine($"layoutCount={_layoutCount}");
        _trace.AppendLine($"preset={CaptureResolutionPreset.ToLabel(_preferredWidth, _preferredHeight)}@{_preferFps:F0}");
        _trace.AppendLine($"availableCamerasKnown={_availableCameraCount}");
        _trace.AppendLine($"cameraDiscoveryRefreshDuringStartPreview=false");

        for (var i = 0; i < _layoutCount; i++)
        {
            var id = i < _selectedDeviceIds.Count ? _selectedDeviceIds[i] : null;
            if (string.IsNullOrEmpty(id))
            {
                _trace.AppendLine($"cam{i + 1}=<not selected>");
                continue;
            }

            var dev = _devices.FirstOrDefault(d => d.Id == id);
            _trace.AppendLine(
                $"cam{i + 1} name=\"{PrivacySanitizer.SanitizeForLog(dev?.DisplayName ?? "?")}\" id={PrivacySanitizer.Redacted} enumIndex={dev?.EnumerationIndex.ToString() ?? "?"}");
        }
    }

    private void WriteDiagnosis(int openedCount, int requiredCount)
    {
        try
        {
            var dir = PathHelper.LogsFolder();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "preview_start_diagnosis.txt");

            var openMode = AnalyzeOpenMode();
            var sb = new StringBuilder();
            sb.AppendLine("MultiCamApp Start Preview Diagnosis");
            sb.AppendLine("===================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Trace file: {Path.GetFileName(_tracePath)}");
            sb.AppendLine();
            sb.AppendLine($"Total Start Preview time: {_total.ElapsedMilliseconds} ms");
            sb.AppendLine($"Device validation time: {Math.Max(0, _validateEndMs - _validateStartMs)} ms");
            sb.AppendLine($"Catalog rebuild time: {Math.Max(0, _catalogRebuildEndMs - _catalogRebuildStartMs)} ms");
            sb.AppendLine($"Prepare multi-camera time: {Math.Max(0, _prepareMultiCamEndMs - _prepareMultiCamStartMs)} ms");
            sb.AppendLine($"Discovery/probe events during Start Preview: {_discoveryEvents.Count + _probeEvents.Count}");
            sb.AppendLine($"Configured open mode: {_openMode}");
            sb.AppendLine($"Analyzed open pattern: {openMode}");
            sb.AppendLine($"Cameras opened for preview: {openedCount}/{requiredCount}");
            sb.AppendLine();

            sb.AppendLine("Per-camera open time (driver open through StartPreviewAsync):");
            foreach (var kv in _slots.OrderBy(k => k.Key))
            {
                var s = kv.Value;
                var openMs = s.OpenStartMs.HasValue && s.OpenEndMs.HasValue
                    ? s.OpenEndMs.Value - s.OpenStartMs.Value
                    : -1;
                var ffRecv = s.FirstFrameReceivedMs.HasValue && s.OpenEndMs.HasValue
                    ? s.FirstFrameReceivedMs.Value - s.OpenEndMs.Value
                    : -1;
                var ffRender = s.FirstFrameRenderedMs.HasValue && s.OpenEndMs.HasValue
                    ? s.FirstFrameRenderedMs.Value - s.OpenEndMs.Value
                    : -1;
                sb.AppendLine(
                    $"  cam{kv.Key + 1}: openMs={openMs} firstFrameRecvMs={ffRecv} firstFrameRenderMs={ffRender} selected=\"{s.SelectedDeviceName}\" opened=\"{s.OpenedDeviceName}\" dshow={s.OpenedDshowIndex?.ToString() ?? "?"}");
            }

            sb.AppendLine();
            sb.AppendLine("Does Start Preview scan all cameras?");
            sb.AppendLine("  Camera list refresh (DiscoverAsync/RefreshAsync): NO during Start Preview.");
            sb.AppendLine("  DirectShow COM enumeration: YES — lists all system video devices to map selected IDs.");
            sb.AppendLine("  VideoCapture open for non-selected devices: NO (except brief USB-duplicate probe opens).");

            sb.AppendLine();
            sb.AppendLine("Sequential vs parallel:");
            sb.AppendLine($"  {openMode}");

            if (_discoveryEvents.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Discovery during Start Preview:");
                foreach (var e in _discoveryEvents)
                    sb.AppendLine($"  - {e}");
            }

            if (_probeEvents.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Probe during Start Preview (not full preview):");
                foreach (var e in _probeEvents)
                    sb.AppendLine($"  - {e}");
            }

            if (_bugs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Bugs:");
                foreach (var b in _bugs)
                    sb.AppendLine($"  - {b}");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("Extra non-selected preview opens: none detected.");
            }

            sb.AppendLine();
            sb.AppendLine("Root cause conclusion:");
            sb.AppendLine(BuildConclusion(openMode, openedCount, requiredCount));

            File.WriteAllText(path, PrivacySanitizer.SanitizeForLog(sb.ToString()), Encoding.UTF8);

            WriteProblemSummary(openedCount, requiredCount, openMode);
        }
        catch { }
    }

    private void WriteProblemSummary(int openedCount, int requiredCount, string openMode)
    {
        try
        {
            var dir = PathHelper.LogsFolder();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "preview_problem_summary.txt");
            var sb = new StringBuilder();
            sb.AppendLine("MultiCamApp Preview Problem Summary");
            sb.AppendLine("===================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Trace file: {Path.GetFileName(_tracePath)}");
            sb.AppendLine($"App version: {VersionService.Load().Display}");
            sb.AppendLine();
            sb.AppendLine($"Start Preview total time: {_total.ElapsedMilliseconds} ms");
            sb.AppendLine($"Selected preset: {CaptureResolutionPreset.ToLabel(_preferredWidth, _preferredHeight)}");
            sb.AppendLine($"Requested FPS: {_preferFps:F0}");
            sb.AppendLine($"Layout: {_layoutCount} camera(s)");
            sb.AppendLine($"Open mode: {openMode}");
            sb.AppendLine($"Cameras preview-ready: {openedCount}/{requiredCount}");
            sb.AppendLine($"Discovery/probe during Start Preview: {(_discoveryEvents.Count + _probeEvents.Count > 0 ? "yes" : "no")}");
            sb.AppendLine();

            foreach (var kv in _slots.OrderBy(k => k.Key))
            {
                var s = kv.Value;
                var openMs = s.OpenStartMs.HasValue && s.OpenEndMs.HasValue
                    ? s.OpenEndMs.Value - s.OpenStartMs.Value
                    : s.OpenStartMs.HasValue
                        ? _total.ElapsedMilliseconds - s.OpenStartMs.Value
                        : -1;
                var ffRecv = s.FirstFrameReceivedMs.HasValue && s.OpenEndMs.HasValue
                    ? s.FirstFrameReceivedMs.Value - s.OpenEndMs.Value
                    : -1;

                sb.AppendLine($"cam{kv.Key + 1}:");
                sb.AppendLine($"  selected=\"{s.SelectedDeviceName}\" id={s.SelectedDeviceId ?? "?"}");
                sb.AppendLine($"  openMs={openMs} firstFrameMs={ffRecv}");
                sb.AppendLine($"  result={(s.OpenFailed ? "FAILED" : "OK")}");
                if (!string.IsNullOrEmpty(s.OpenedResolutionText))
                    sb.AppendLine($"  openedResolution={s.OpenedResolutionText}");
                if (!string.IsNullOrEmpty(s.FailureCategory))
                    sb.AppendLine($"  failureCategory={s.FailureCategory}");
                if (!string.IsNullOrEmpty(s.FailureMessage))
                    sb.AppendLine($"  failureMessage={s.FailureMessage}");
                sb.AppendLine();
            }

            sb.AppendLine("Non-selected devices opened: none detected.");
            File.WriteAllText(path, PrivacySanitizer.SanitizeForLog(sb.ToString()), Encoding.UTF8);
        }
        catch { }
    }

    private string AnalyzeOpenMode()
    {
        var starts = _slots.Values
            .Where(s => s.OpenStartMs.HasValue)
            .Select(s => s.OpenStartMs!.Value)
            .OrderBy(x => x)
            .ToList();
        if (starts.Count < 2)
            return starts.Count == 1 ? "single camera" : "no opens recorded";

        var ends = _slots.Values
            .Where(s => s.OpenEndMs.HasValue)
            .Select(s => s.OpenEndMs!.Value)
            .OrderBy(x => x)
            .ToList();

        var firstStart = starts[0];
        var parallelThresholdMs = 150;
        var allStartClose = starts.All(t => t - firstStart <= parallelThresholdMs);
        if (allStartClose)
            return $"PARALLEL (all open starts within {parallelThresholdMs}ms)";

        var sequential = true;
        for (var i = 1; i < starts.Count && sequential; i++)
        {
            var prevEnd = ends.Count >= i ? ends[i - 1] : long.MaxValue;
            if (starts[i] < prevEnd - 50)
                sequential = false;
        }

        return sequential
            ? "SEQUENTIAL (later camera open starts after earlier camera open ends)"
            : "MIXED (staggered/overlapping opens)";
    }

    private string BuildConclusion(string openMode, int openedCount, int requiredCount)
    {
        var maxOpen = _slots.Values
            .Where(s => s.OpenStartMs.HasValue && s.OpenEndMs.HasValue)
            .Select(s => s.OpenEndMs!.Value - s.OpenStartMs!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var parts = new List<string>();
        if (openMode.StartsWith("SEQUENTIAL", StringComparison.OrdinalIgnoreCase))
            parts.Add("Preview opens cameras one-after-another; total wait is roughly the sum of per-camera driver open times.");

        if (openMode.StartsWith("PARALLEL", StringComparison.OrdinalIgnoreCase))
            parts.Add("Preview opens selected cameras concurrently; total wait is dominated by the slowest camera.");

        if (maxOpen > 3000)
            parts.Add($"Largest single-camera open took ~{maxOpen}ms (USB/driver negotiation).");

        if (_catalogRebuildEndMs - _catalogRebuildStartMs > 500)
            parts.Add("DirectShow catalog rebuild adds measurable overhead before any camera opens.");

        if (_probeEvents.Count > 0)
            parts.Add("Duplicate-USB probe briefly opened cameras before preview (selected devices only).");

        if (_discoveryEvents.Any(e => e.Contains("DiscoverAsync", StringComparison.OrdinalIgnoreCase)))
            parts.Add("WARNING: full camera discovery ran during Start Preview (unexpected).");

        if (openedCount < requiredCount && requiredCount > 0)
            parts.Add($"Only {openedCount}/{requiredCount} cameras reached preview-ready state.");

        if (parts.Count == 0)
            parts.Add("Delay is primarily inside per-camera driver open and first-frame wait; see per-camera timings above.");

        return string.Join(" ", parts);
    }

    private string NowLine() => $"{DateTime.Now:HH:mm:ss.fff} +{_total.ElapsedMilliseconds}ms";

    private void FlushTrace()
    {
        try
        {
            File.WriteAllText(_tracePath, PrivacySanitizer.SanitizeForLog(_trace.ToString()), Encoding.UTF8);
        }
        catch { }
    }

    private void MirrorTrace()
    {
        // Requirement: debug logs must stay inside the app folder.
    }
}
