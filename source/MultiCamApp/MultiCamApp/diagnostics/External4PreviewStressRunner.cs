using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Ui;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// Direct preview-only stress runner for four external cameras. This intentionally
/// drives the preview ViewModel path and never starts recording.
/// </summary>
public sealed class External4PreviewStressRunner
{
    private static readonly string[] RequiredCameraLabels =
    [
        "j5 Webcam JVU250 #1",
        "j5 Webcam JVU250 #2",
        "j5 Webcam JVU250 #3",
        "OBSBOT"
    ];

    private const string ProblemCameraLabel = "j5 Webcam JVU250 #3";
    private const int CyclesPerCombination = 5;
    private const int ProblemCameraPreflightCycles = 2;
    private const int OpenTimeoutSeconds = 12;
    private const int StopTimeoutSeconds = 5;
    private const int CombinationTimeoutSeconds = 40;

    private readonly MainViewModel _vm = new();
    private readonly Image[] _previewImages = [new(), new(), new(), new()];
    private readonly List<CycleResult> _results = [];
    private readonly string _logDir = PathHelper.LogsFolder();
    private readonly DateTime _started = DateTime.Now;
    private readonly StringBuilder _txt = new();
    private readonly bool _finalShortMode;
    private int _testWidth = 1280;
    private int _testHeight = 720;
    private string _presetLabel = "720p";

    private string TxtPath => Path.Combine(_logDir, $"external4_preview_stress_{_started:yyyyMMdd_HHmmss}.txt");
    private string CsvPath => Path.Combine(_logDir, $"external4_preview_stress_{_started:yyyyMMdd_HHmmss}.csv");

    public External4PreviewStressRunner(bool finalShortMode = false)
    {
        _finalShortMode = finalShortMode;
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_logDir);
        WriteHeader();

        try
        {
            _vm.PreloadLanguage();
            _vm.UiDispatcher = System.Windows.Application.Current?.Dispatcher;
            await _vm.InitializeAsync().ConfigureAwait(true);
            ApplyCurrentStressCaptureSettings();

            var cameras = ResolveExternalCameras();
            if (cameras.Count != 4)
            {
                WriteLine("FAIL: Could not resolve exactly four required external cameras.");
                WriteLine("Resolved:");
                foreach (var c in cameras)
                    WriteLine($"- {c.DisplayName} id={c.Id}");
                WriteLine("Available non-virtual devices:");
                foreach (var d in _vm.Devices.Where(IsAllowedNonVirtualDevice))
                    WriteLine($"- {d.DisplayName} kind={d.Kind} id={d.Id}");
                FlushOutputs();
                return 2;
            }

            WriteLine("Selected external cameras:");
            foreach (var camera in cameras)
                WriteLine($"- {camera.DisplayName} enumIndex={camera.EnumerationIndex} id={camera.Id}");
            WriteLine("");

            var matrixCameras = cameras;
            var builtInFallback = ResolveBuiltInCamera();
            var problemCamera = cameras.FirstOrDefault(c =>
                c.DisplayName.Contains(ProblemCameraLabel, StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains(ProblemCameraLabel, StringComparison.OrdinalIgnoreCase));
            if (problemCamera != null && builtInFallback != null)
            {
                var anchor = cameras.FirstOrDefault(c => !string.Equals(c.Id, problemCamera.Id, StringComparison.OrdinalIgnoreCase));
                if (anchor != null)
                {
                    WriteLine($"Preflight: checking {problemCamera.DisplayName} before alternate built-in webcam matrix.");
                    var preflightPass = false;
                    for (var cycle = 1; cycle <= ProblemCameraPreflightCycles; cycle++)
                    {
                        var result = await RunCycleAsync([anchor, problemCamera], cycle, "preflight-j5-3").ConfigureAwait(true);
                        _results.Add(result);
                        AppendCycle(result);
                        FlushOutputs();
                        if (result.Result == "PASS")
                            preflightPass = true;
                    }

                    if (!preflightPass)
                    {
                        matrixCameras = cameras
                            .Select(c => string.Equals(c.Id, problemCamera.Id, StringComparison.OrdinalIgnoreCase)
                                ? builtInFallback
                                : c)
                            .ToList();
                        _testWidth = 640;
                        _testHeight = 360;
                        _presetLabel = "360p";
                        ApplyCurrentStressCaptureSettings();
                        WriteLine($"{problemCamera.DisplayName} continued failing in preflight; using built-in webcam as alternate camera.");
                        WriteLine($"Built-in alternate: {builtInFallback.DisplayName} enumIndex={builtInFallback.EnumerationIndex} id={builtInFallback.Id}");
                        WriteLine("Built-in alternate constraint: switching main alternate matrix to 360p / 30 fps; do not use 1080p for built-in webcam tests.");
                        WriteLine("Driver recovery cooldown: waiting 30s after failed j5 #3 preflight before starting the alternate matrix.");
                        WriteLine("");
                        FlushOutputs();
                        try { await _vm.StopPreviewAsync().ConfigureAwait(true); } catch { }
                        await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                        OpenCvDirectShowIndexCatalog.ClearProbeOverrides();
                        OpenCvDirectShowIndexCatalog.RebuildForSelected(_vm.Devices, matrixCameras.Select(c => c.Id).ToList());
                    }
                    else
                    {
                        WriteLine($"{problemCamera.DisplayName} passed preflight; keeping original four-camera external matrix.");
                        WriteLine("");
                    }
                }
            }
            else if (problemCamera != null)
            {
                WriteLine("Built-in webcam alternate was not found; keeping original four-camera external matrix.");
                WriteLine("");
            }

            WriteLine("Matrix cameras:");
            foreach (var camera in matrixCameras)
                WriteLine($"- {camera.DisplayName} enumIndex={camera.EnumerationIndex} kind={camera.Kind} id={camera.Id}");
            WriteLine($"Matrix preset: {_presetLabel} / 30 fps");
            WriteLine("");

            var combinations = _finalShortMode
                ? BuildFinalShortCombinations(matrixCameras)
                : new[] { 2, 3, 4 }
                    .SelectMany(length => Permute(matrixCameras, length))
                    .ToList();
            var cycles = _finalShortMode ? 2 : CyclesPerCombination;

            foreach (var combo in combinations)
            {
                for (var cycle = 1; cycle <= cycles; cycle++)
                {
                    var result = await RunCycleAsync(combo, cycle).ConfigureAwait(true);
                    _results.Add(result);
                    AppendCycle(result);
                    FlushOutputs();
                }
            }

            WriteSummary();
            FlushOutputs();
            return MainMatrixResults().Any(r => r.Result != "PASS") ? 1 : 0;
        }
        catch (Exception ex)
        {
            WriteLine($"FATAL: {ex}");
            FlushOutputs();
            return 3;
        }
        finally
        {
            try { await _vm.StopPreviewAsync().ConfigureAwait(true); } catch { }
            try { await _vm.OnAppClosingAsync().ConfigureAwait(true); } catch { }
        }
    }

    private static List<IReadOnlyList<CameraDevice>> BuildFinalShortCombinations(IReadOnlyList<CameraDevice> cameras)
    {
        var j5One = cameras.FirstOrDefault(c => c.DisplayName.Contains("j5 Webcam JVU250 #1", StringComparison.OrdinalIgnoreCase));
        var j5Two = cameras.FirstOrDefault(c => c.DisplayName.Contains("j5 Webcam JVU250 #2", StringComparison.OrdinalIgnoreCase));
        var builtIn = cameras.FirstOrDefault(c => c.Kind is CameraKind.BuiltInFront or CameraKind.BuiltInBack
                                                  || c.DisplayName.Contains("Integrated", StringComparison.OrdinalIgnoreCase));
        var obsbot = cameras.FirstOrDefault(c => c.DisplayName.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase)
                                                 || c.Name.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase));

        var combos = new List<IReadOnlyList<CameraDevice>>();
        AddCombo(combos, [j5One, j5Two, obsbot]);
        AddCombo(combos, [obsbot, j5One, j5Two]);
        AddCombo(combos, [j5One, builtIn, obsbot]);
        AddCombo(combos, [j5Two, builtIn, obsbot]);
        AddCombo(combos, [j5One, j5Two, builtIn, obsbot]);
        AddCombo(combos, [obsbot, j5One, j5Two, builtIn]);
        return combos;
    }

    private static void AddCombo(List<IReadOnlyList<CameraDevice>> combos, IReadOnlyList<CameraDevice?> maybeCombo)
    {
        if (maybeCombo.Any(c => c == null))
            return;
        combos.Add(maybeCombo.Cast<CameraDevice>().ToList());
    }

    private async Task<CycleResult> RunCycleAsync(IReadOnlyList<CameraDevice> combo, int cycle, string phase = "matrix")
    {
        var comboName = string.Join(" + ", combo.Select(c => c.DisplayName));
        var startedLocal = DateTime.Now;
        var beforeTrace = LatestFile("preview_start_trace_*.txt");
        var beforeRuntime = LatestFile("app_runtime_*.txt");
        var startStopwatch = Stopwatch.StartNew();
        var startOk = false;
        var stopOk = false;
        var failure = "";
        var stopDurationMs = 0L;
        var readyBeforeStop = 0;

        try
        {
            await EnsureStoppedAsync().ConfigureAwait(true);
            _vm.SetLayout(combo.Count);
            ApplyCurrentStressCaptureSettings();
            for (var i = 0; i < 4; i++)
                _vm.SetSelectedDevice(i, i < combo.Count ? combo[i].Id : null);

            var startTask = _vm.StartPreviewAsync(
                i => _previewImages[i],
                userClicked: true,
                uiBefore: new MainViewModel.UiButtonStates(true, false, false, false));
            var startCompleted = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(CombinationTimeoutSeconds))).ConfigureAwait(true);
            if (startCompleted == startTask)
                startOk = await startTask.ConfigureAwait(true);
            else
                failure = "Start Preview combination timeout";

            var waitReady = await WaitForPreviewReadyOrSettledAsync(combo.Count, TimeSpan.FromSeconds(OpenTimeoutSeconds)).ConfigureAwait(true);
            readyBeforeStop = CountPreviewReady(combo.Count);
            if (string.IsNullOrWhiteSpace(failure) && !waitReady.Ready)
                failure = waitReady.Message;

            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

            var stopWatch = Stopwatch.StartNew();
            var stopTask = _vm.StopPreviewAsync();
            var stopCompleted = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(StopTimeoutSeconds))).ConfigureAwait(true);
            if (stopCompleted == stopTask)
            {
                await stopTask.ConfigureAwait(true);
                stopOk = true;
            }
            else
            {
                failure = AppendFailure(failure, "Stop Preview timeout");
            }

            stopWatch.Stop();
            stopDurationMs = stopWatch.ElapsedMilliseconds;
            foreach (var img in _previewImages)
                img.Source = null;

            var cooldownSeconds = DriverCooldownSeconds(combo);
            await Task.Delay(TimeSpan.FromSeconds(cooldownSeconds)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = AppendFailure(failure, $"{ex.GetType().Name}: {ex.Message}");
            try { await _vm.StopPreviewAsync().ConfigureAwait(true); } catch { }
        }

        startStopwatch.Stop();

        var traceFile = LatestFile("preview_start_trace_*.txt", after: beforeTrace);
        var runtimeFile = LatestFile("app_runtime_*.txt", after: beforeRuntime) ?? beforeRuntime;
        var trace = TraceMetrics.Parse(traceFile);
        var runtime = RuntimeStopMetrics.Parse(runtimeFile, startedLocal);
        var ready = readyBeforeStop;
        var status = ClassifyResult(combo, ready, stopOk, failure, trace);

        return new CycleResult(
            Layout: combo.Count,
            Combination: phase == "matrix" ? comboName : $"{phase}: {comboName}",
            Cycle: cycle,
            SelectedDevices: string.Join(" | ", combo.Select(c => $"{c.DisplayName}={c.Id}")),
            Result: status.Result,
            FailureReason: status.FailureReason,
            Simultaneity: trace.Simultaneity,
            TotalTimeToFirstVisibleMs: trace.FirstRenderedMs,
            TotalTimeToAllVisibleMs: trace.AllReadyMs,
            StopDurationMs: stopDurationMs,
            StopCompleted: stopOk,
            ReadyCount: ready,
            RequiredCount: combo.Count,
            OldCallbackIgnored: runtime.OldCallbackIgnored,
            StaleFrameDetected: runtime.StaleFrameDetected,
            GlitchDetected: runtime.GlitchDetected,
            TraceFile: traceFile ?? "",
            RuntimeFile: runtimeFile ?? "",
            SlotDetails: BuildSlotDetails(combo, trace, runtime));
    }

    private async Task EnsureStoppedAsync()
    {
        if (_vm.PreviewStartInProgress || _vm.State.RunState == AppRunState.Previewing || _vm.IsPreviewLifecycleBusy)
            await _vm.StopPreviewAsync().ConfigureAwait(true);
    }

    private void ApplyCurrentStressCaptureSettings() => _vm.ApplyCaptureSettings(_testWidth, _testHeight, 30);

    private async Task<(bool Ready, string Message)> WaitForPreviewReadyOrSettledAsync(int required, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var (ready, _) = _vm.CountPreviewReadySlots();
            if (ready >= required)
                return (true, "");

            var failed = Enumerable.Range(0, required).Count(i =>
                _vm.Panels[i].Pipeline.PreviewSlotState is PreviewSlotStateKind.FailedDeviceOpen
                    or PreviewSlotStateKind.FailedUnsupportedPreset
                    or PreviewSlotStateKind.LostConnection);
            if (ready + failed >= required)
                return (false, $"Preview settled partial: ready={ready}/{required}, failed={failed}");

            await Task.Delay(100).ConfigureAwait(true);
        }

        var counts = _vm.CountPreviewReadySlots();
        return (false, $"Open timeout: ready={counts.Ready}/{required}");
    }

    private int CountPreviewReady(int required)
    {
        var ready = 0;
        for (var i = 0; i < required; i++)
        {
            if (_vm.Panels[i].Pipeline.PreviewSlotState == PreviewSlotStateKind.PreviewReady)
                ready++;
        }
        return ready;
    }

    private (string Result, string FailureReason) ClassifyResult(
        IReadOnlyList<CameraDevice> combo,
        int ready,
        bool stopOk,
        string failure,
        TraceMetrics trace)
    {
        var slotMismatch = false;
        var duplicateIndex = false;
        var indices = new HashSet<int>();
        for (var i = 0; i < combo.Count; i++)
        {
            if (trace.Slots.TryGetValue(i + 1, out var slot))
            {
                if (!string.IsNullOrWhiteSpace(slot.SelectedDeviceId)
                    && !string.Equals(slot.SelectedDeviceId, combo[i].Id, StringComparison.OrdinalIgnoreCase))
                    slotMismatch = true;
                if (slot.DshowIndex.HasValue && !indices.Add(slot.DshowIndex.Value))
                    duplicateIndex = true;
            }
        }

        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(failure)) reasons.Add(failure);
        if (!stopOk) reasons.Add("Stop Preview did not complete");
        if (slotMismatch) reasons.Add("slot mismatch");
        if (duplicateIndex) reasons.Add("duplicate DirectShow index");

        if (ready == combo.Count && stopOk && !slotMismatch && !duplicateIndex)
            return ("PASS", "");
        if (ready > 0 && stopOk && !slotMismatch && !duplicateIndex)
            return ("PARTIAL", string.Join("; ", reasons.DefaultIfEmpty($"ready={ready}/{combo.Count}")));
        return ("FAIL", string.Join("; ", reasons.DefaultIfEmpty($"ready={ready}/{combo.Count}")));
    }

    private IReadOnlyList<SlotCycleDetail> BuildSlotDetails(
        IReadOnlyList<CameraDevice> combo,
        TraceMetrics trace,
        RuntimeStopMetrics runtime)
    {
        var details = new List<SlotCycleDetail>();
        for (var i = 0; i < combo.Count; i++)
        {
            trace.Slots.TryGetValue(i + 1, out var slot);
            runtime.StopDurations.TryGetValue(i + 1, out var stopMs);
            details.Add(new SlotCycleDetail(
                Slot: i + 1,
                SelectedName: combo[i].DisplayName,
                SelectedId: combo[i].Id,
                DshowIndex: slot?.DshowIndex,
                OpenStartMs: slot?.OpenStartMs,
                OpenEndMs: slot?.OpenEndMs,
                FirstFrameReceivedMs: slot?.FirstFrameReceivedMs,
                FirstFrameRenderedMs: slot?.FirstFrameRenderedMs,
                OpenDurationMs: slot?.OpenDurationMs,
                StopDurationMs: stopMs,
                PreviewFps: _vm.Panels[i].Pipeline.FpsMonitor.AverageFps));
        }
        return details;
    }

    private List<CameraDevice> ResolveExternalCameras()
    {
        var available = _vm.Devices.Where(IsAllowedNonVirtualDevice).ToList();
        var selected = new List<CameraDevice>();
        foreach (var label in RequiredCameraLabels)
        {
            var match = label.Equals("OBSBOT", StringComparison.OrdinalIgnoreCase)
                ? available.FirstOrDefault(d => d.DisplayName.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase)
                                             || d.Name.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase))
                : available.FirstOrDefault(d => d.DisplayName.Contains(label, StringComparison.OrdinalIgnoreCase));
            if (match != null && selected.All(s => !string.Equals(s.Id, match.Id, StringComparison.OrdinalIgnoreCase)))
                selected.Add(match);
        }
        return selected;
    }

    private CameraDevice? ResolveBuiltInCamera()
    {
        return _vm.Devices.FirstOrDefault(d =>
                   d.IsBuiltIn
                   || d.Kind == CameraKind.BuiltInFront
                   || d.DisplayName.Contains("Integrated Webcam", StringComparison.OrdinalIgnoreCase)
                   || d.Name.Contains("Integrated Webcam", StringComparison.OrdinalIgnoreCase))
               ?? _vm.Devices.FirstOrDefault(d =>
                   d.DisplayName.Contains("webcam", StringComparison.OrdinalIgnoreCase)
                   && !IsAllowedNonVirtualDevice(d)
                   && !d.DisplayName.Contains("OBS", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedNonVirtualDevice(CameraDevice device)
    {
        var text = $"{device.DisplayName} {device.Name}";
        if (text.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("OBS Virtual", StringComparison.OrdinalIgnoreCase))
            return true;
        if (device.IsBuiltIn) return false;
        if (text.Contains("Integrated Webcam", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("Built-in", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("virtual", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("OBS Virtual", StringComparison.OrdinalIgnoreCase)) return false;
        return device.Kind == CameraKind.ExternalUsb;
    }

    private static IEnumerable<IReadOnlyList<T>> Permute<T>(IReadOnlyList<T> items, int length)
    {
        if (length == 1)
        {
            foreach (var item in items)
                yield return [item];
            yield break;
        }

        foreach (var item in items)
        {
            var remaining = items.Where(x => !EqualityComparer<T>.Default.Equals(x, item)).ToList();
            foreach (var tail in Permute(remaining, length - 1))
                yield return new[] { item }.Concat(tail).ToList();
        }
    }

    private void WriteHeader()
    {
        WriteLine("=== External 4 Preview Stress Test ===");
        WriteLine($"Started: {_started:O}");
        WriteLine("Initial preset: 720p / 30 fps");
        WriteLine("Built-in webcam alternate, when used, is limited to 720p or 360p only.");
        WriteLine(_finalShortMode ? "Mode: final short 3/4-camera validation" : "Mode: full ordered matrix");
        WriteLine($"Cycles per combination: {(_finalShortMode ? 2 : CyclesPerCombination)}");
        WriteLine(_finalShortMode
            ? "Layouts: targeted high-risk 3-camera groups and representative 4-camera orders"
            : "Layouts: ordered 2-camera pairs, 3-camera permutations, 4-camera permutations");
        WriteLine("Recording: disabled");
        WriteLine("");
    }

    private void AppendCycle(CycleResult result)
    {
        WriteLine($"[{result.Result}] layout={result.Layout} cycle={result.Cycle} combo={result.Combination}");
        WriteLine($"  ready={result.ReadyCount}/{result.RequiredCount} simultaneity={result.Simultaneity} firstVisibleMs={result.TotalTimeToFirstVisibleMs?.ToString() ?? "-"} allVisibleMs={result.TotalTimeToAllVisibleMs?.ToString() ?? "-"} stopMs={result.StopDurationMs}");
        if (!string.IsNullOrWhiteSpace(result.FailureReason))
            WriteLine($"  reason={result.FailureReason}");
        foreach (var slot in result.SlotDetails)
            WriteLine($"  cam{slot.Slot}: {slot.SelectedName} dshow={slot.DshowIndex?.ToString() ?? "-"} openStart={slot.OpenStartMs?.ToString() ?? "-"} openEnd={slot.OpenEndMs?.ToString() ?? "-"} firstFrame={slot.FirstFrameRenderedMs?.ToString() ?? "-"} fps={slot.PreviewFps:F1}");
        WriteLine("");
    }

    private void WriteSummary()
    {
        var main = MainMatrixResults().ToList();
        WriteLine("=== Summary ===");
        WriteLine($"total tests={_results.Count}");
        WriteLine($"pass count={_results.Count(r => r.Result == "PASS")}");
        WriteLine($"partial count={_results.Count(r => r.Result == "PARTIAL")}");
        WriteLine($"fail count={_results.Count(r => r.Result == "FAIL")}");
        WriteLine($"main matrix tests={main.Count}");
        WriteLine($"main matrix pass count={main.Count(r => r.Result == "PASS")}");
        WriteLine($"main matrix partial count={main.Count(r => r.Result == "PARTIAL")}");
        WriteLine($"main matrix fail count={main.Count(r => r.Result == "FAIL")}");
        WriteLine($"main matrix result={(main.Any(r => r.Result == "FAIL") ? "FAIL" : main.Any(r => r.Result == "PARTIAL") ? "PARTIAL" : "PASS")}");
        WriteLine($"average open time ms={Average(_results.SelectMany(r => r.SlotDetails).Select(s => s.OpenDurationMs)):F0}");
        var slowestSlot = _results.SelectMany(r => r.SlotDetails.Select(s => (Result: r, Slot: s)))
            .OrderByDescending(x => x.Slot.OpenDurationMs ?? -1)
            .FirstOrDefault();
        if (slowestSlot.Result != null)
            WriteLine($"slowest camera={slowestSlot.Slot.SelectedName} combo={slowestSlot.Result.Combination} openMs={slowestSlot.Slot.OpenDurationMs}");
        var slowestCombo = _results.OrderByDescending(r => r.TotalTimeToAllVisibleMs ?? -1).FirstOrDefault();
        if (slowestCombo != null)
            WriteLine($"slowest combination={slowestCombo.Combination} allVisibleMs={slowestCombo.TotalTimeToAllVisibleMs}");
        WriteLine($"average stop time ms={Average(_results.Select(r => (long?)r.StopDurationMs)):F0}");
        WriteLine($"stop failures={_results.Count(r => !r.StopCompleted)}");
        WriteLine($"simultaneous count={_results.Count(r => r.Simultaneity == "SIMULTANEOUS")}");
        WriteLine($"near-parallel count={_results.Count(r => r.Simultaneity == "NEAR_PARALLEL")}");
        WriteLine($"sequential count={_results.Count(r => r.Simultaneity == "SEQUENTIAL")}");
        WriteFailures("failing 2-camera pairs", _results.Where(r => r.Layout == 2 && r.Result == "FAIL"));
        WriteFailures("failing 3-camera groups", _results.Where(r => r.Layout == 3 && r.Result == "FAIL"));
        WriteFailures("failing 4-camera orders", _results.Where(r => r.Layout == 4 && r.Result == "FAIL"));
        WriteFailures("failing main matrix 3/4-camera cases", main.Where(r => r.Result == "FAIL"));
        WriteLine("likely cause hints:");
        WriteLine("- wrong selected/opened device or duplicate dshow index => mapping bug");
        WriteLine("- repeated same slot failures across devices => slot bug");
        WriteLine("- 3/4 camera failures at 720p with correct mapping => USB bandwidth or camera capability");
        WriteLine("- stop timeout/failure => stop lifecycle bug");
    }

    private IEnumerable<CycleResult> MainMatrixResults() =>
        _results.Where(r => !r.Combination.StartsWith("preflight-", StringComparison.OrdinalIgnoreCase));

    private void WriteFailures(string title, IEnumerable<CycleResult> results)
    {
        WriteLine($"{title}:");
        var list = results.ToList();
        if (list.Count == 0)
        {
            WriteLine("- none");
            return;
        }
        foreach (var r in list)
            WriteLine($"- {r.Combination} cycle={r.Cycle} reason={r.FailureReason}");
    }

    private static double Average(IEnumerable<long?> values)
    {
        var list = values.Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    private void FlushOutputs()
    {
        File.WriteAllText(TxtPath, PrivacySanitizer.SanitizeForLog(_txt.ToString()), Encoding.UTF8);
        File.WriteAllText(CsvPath, PrivacySanitizer.SanitizeForLog(BuildCsv()), Encoding.UTF8);
    }

    private string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("layout,combination,cycle,result,failureReason,simultaneity,totalFirstVisibleMs,totalAllVisibleMs,stopDurationMs,stopCompleted,readyCount,requiredCount,oldCallbackIgnored,staleFrameDetected,glitchDetected,slot,selectedName,selectedId,dshowIndex,openStartMs,openEndMs,firstFrameReceivedMs,firstFrameRenderedMs,openDurationMs,slotStopDurationMs,previewFps,traceFile,runtimeFile");
        foreach (var r in _results)
        {
            foreach (var s in r.SlotDetails)
            {
                sb.AppendLine(string.Join(",", [
                    Csv(r.Layout), Csv(r.Combination), Csv(r.Cycle), Csv(r.Result), Csv(r.FailureReason),
                    Csv(r.Simultaneity), Csv(r.TotalTimeToFirstVisibleMs), Csv(r.TotalTimeToAllVisibleMs),
                    Csv(r.StopDurationMs), Csv(r.StopCompleted), Csv(r.ReadyCount), Csv(r.RequiredCount),
                    Csv(r.OldCallbackIgnored), Csv(r.StaleFrameDetected), Csv(r.GlitchDetected), Csv(s.Slot),
                    Csv(s.SelectedName), Csv(s.SelectedId), Csv(s.DshowIndex), Csv(s.OpenStartMs),
                    Csv(s.OpenEndMs), Csv(s.FirstFrameReceivedMs), Csv(s.FirstFrameRenderedMs),
                    Csv(s.OpenDurationMs), Csv(s.StopDurationMs), Csv(s.PreviewFps.ToString("F1", CultureInfo.InvariantCulture)),
                    Csv(r.TraceFile), Csv(r.RuntimeFile)
                ]));
            }
        }
        return sb.ToString();
    }

    private void WriteLine(string line) => _txt.AppendLine(line);

    private static string Csv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private string? LatestFile(string pattern, string? after = null)
    {
        var files = Directory.Exists(_logDir)
            ? Directory.GetFiles(_logDir, pattern).OrderByDescending(File.GetLastWriteTimeUtc).ToList()
            : [];
        if (string.IsNullOrWhiteSpace(after))
            return files.FirstOrDefault();
        var afterTime = File.Exists(after) ? File.GetLastWriteTimeUtc(after) : DateTime.MinValue;
        return files.FirstOrDefault(f => File.GetLastWriteTimeUtc(f) > afterTime.AddMilliseconds(1));
    }

    private static int DriverCooldownSeconds(IReadOnlyList<CameraDevice> combo)
    {
        var hasObsbot = combo.Any(c => c.DisplayName.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase)
                                       || c.Name.Contains("OBSBOT", StringComparison.OrdinalIgnoreCase));
        if (!hasObsbot) return 2;

        return combo.Count >= 3 ? 12 : 6;
    }

    private static string AppendFailure(string current, string next) =>
        string.IsNullOrWhiteSpace(current) ? next : $"{current}; {next}";

    private sealed record CycleResult(
        int Layout,
        string Combination,
        int Cycle,
        string SelectedDevices,
        string Result,
        string FailureReason,
        string Simultaneity,
        long? TotalTimeToFirstVisibleMs,
        long? TotalTimeToAllVisibleMs,
        long StopDurationMs,
        bool StopCompleted,
        int ReadyCount,
        int RequiredCount,
        bool OldCallbackIgnored,
        bool StaleFrameDetected,
        bool GlitchDetected,
        string TraceFile,
        string RuntimeFile,
        IReadOnlyList<SlotCycleDetail> SlotDetails);

    private sealed record SlotCycleDetail(
        int Slot,
        string SelectedName,
        string SelectedId,
        int? DshowIndex,
        long? OpenStartMs,
        long? OpenEndMs,
        long? FirstFrameReceivedMs,
        long? FirstFrameRenderedMs,
        long? OpenDurationMs,
        long? StopDurationMs,
        double PreviewFps);

    private sealed class TraceMetrics
    {
        private static readonly Regex Offset = new(@"\+(\d+)ms\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex Cam = new(@"cam(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Dshow = new(@"dshowIndex=(\d+)", RegexOptions.Compiled);
        private static readonly Regex Id = new(@"id=([^ ]+)", RegexOptions.Compiled);
        public Dictionary<int, SlotTraceMetrics> Slots { get; } = [];
        public long? FirstRenderedMs => Slots.Values.Where(s => s.FirstFrameRenderedMs.HasValue).Select(s => s.FirstFrameRenderedMs).DefaultIfEmpty().Min();
        public long? AllReadyMs => Slots.Values.Count == 0 || Slots.Values.Any(s => !s.FirstFrameRenderedMs.HasValue)
            ? null
            : Slots.Values.Max(s => s.FirstFrameRenderedMs);
        public string Simultaneity { get; private set; } = "UNKNOWN";

        public static TraceMetrics Parse(string? path)
        {
            var metrics = new TraceMetrics();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return metrics;
            foreach (var line in File.ReadLines(path))
            {
                var offset = Offset.Match(line);
                if (!offset.Success) continue;
                var ms = long.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
                var text = offset.Groups[2].Value;
                var cam = Cam.Match(text);
                if (cam.Success)
                {
                    var slot = int.Parse(cam.Groups[1].Value, CultureInfo.InvariantCulture);
                    var s = metrics.Slots.TryGetValue(slot, out var existing) ? existing : metrics.Slots[slot] = new SlotTraceMetrics();
                    if (text.Contains("SLOT_OPEN_TASK_CREATED", StringComparison.OrdinalIgnoreCase)) s.OpenTaskCreatedMs = ms;
                    if (text.Contains("SLOT_OPEN_TASK_STARTED", StringComparison.OrdinalIgnoreCase)) s.OpenTaskStartedMs = ms;
                    if (text.Contains("CAMERA_OPEN_START", StringComparison.OrdinalIgnoreCase))
                    {
                        s.OpenStartMs = ms;
                        var id = Id.Match(text);
                        if (id.Success) s.SelectedDeviceId = id.Groups[1].Value;
                    }
                    if (text.Contains("CAMERA_OPEN_END", StringComparison.OrdinalIgnoreCase))
                    {
                        s.OpenEndMs = ms;
                        if (s.OpenStartMs.HasValue) s.OpenDurationMs = ms - s.OpenStartMs.Value;
                        var dshow = Dshow.Match(text);
                        if (dshow.Success) s.DshowIndex = int.Parse(dshow.Groups[1].Value, CultureInfo.InvariantCulture);
                    }
                    if (text.Contains("FIRST_FRAME_RECEIVED", StringComparison.OrdinalIgnoreCase)) s.FirstFrameReceivedMs = ms;
                    if (text.Contains("FIRST_FRAME_RENDERED", StringComparison.OrdinalIgnoreCase)) s.FirstFrameRenderedMs = ms;
                }
            }
            metrics.Classify();
            return metrics;
        }

        private void Classify()
        {
            var starts = Slots.Values.Where(s => s.OpenStartMs.HasValue).Select(s => s.OpenStartMs!.Value).ToList();
            if (starts.Count <= 1)
            {
                Simultaneity = "SIMULTANEOUS";
                return;
            }
            var span = starts.Max() - starts.Min();
            if (span <= 200) Simultaneity = "SIMULTANEOUS";
            else if (span <= 500) Simultaneity = "NEAR_PARALLEL";
            else Simultaneity = "SEQUENTIAL";
        }
    }

    private sealed class SlotTraceMetrics
    {
        public string? SelectedDeviceId { get; set; }
        public int? DshowIndex { get; set; }
        public long? OpenTaskCreatedMs { get; set; }
        public long? OpenTaskStartedMs { get; set; }
        public long? OpenStartMs { get; set; }
        public long? OpenEndMs { get; set; }
        public long? FirstFrameReceivedMs { get; set; }
        public long? FirstFrameRenderedMs { get; set; }
        public long? OpenDurationMs { get; set; }
    }

    private sealed class RuntimeStopMetrics
    {
        private static readonly Regex StopLine = new(@"slot=cam(\d+) .*?durationMs=(\d+)", RegexOptions.Compiled);
        public Dictionary<int, long> StopDurations { get; } = [];
        public bool OldCallbackIgnored { get; private set; }
        public bool StaleFrameDetected { get; private set; }
        public bool GlitchDetected { get; private set; }

        public static RuntimeStopMetrics Parse(string? path, DateTime after)
        {
            var result = new RuntimeStopMetrics();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length >= 23
                    && DateTime.TryParseExact(
                        line[..23],
                        "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal,
                        out var timestamp)
                    && timestamp < after)
                    continue;

                if (line.Contains("OLD_CALLBACK_IGNORED", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("OLD_FRAME_IGNORED", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("UI_CALLBACK_IGNORED_OLD_SESSION", StringComparison.OrdinalIgnoreCase))
                    result.OldCallbackIgnored = true;
                if (line.Contains("STALE_FRAME_RENDERED", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("STALE_FRAME_DETECTED", StringComparison.OrdinalIgnoreCase))
                    result.StaleFrameDetected = true;
                if (line.Contains("glitch", StringComparison.OrdinalIgnoreCase))
                    result.GlitchDetected = true;
                var m = StopLine.Match(line);
                if (m.Success)
                    result.StopDurations[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            return result;
        }
    }
}
