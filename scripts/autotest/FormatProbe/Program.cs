// Test-only tool: lists WinRT MediaFrameSource formats (in driver order) for matching cameras,
// then optionally tries to start N cameras concurrently with a given subtype/size/fps.
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

var filter = args.Length > 0 ? args[0] : "j5";
if (args.Length >= 2 && args[1] == "timing")
{
    // FormatProbe <filter> timing <w> <h> <fps> <seconds> <outSub|RAW> [lockexp]
    var (w, h, fps, secs) = (uint.Parse(args[2]), uint.Parse(args[3]), double.Parse(args[4]), int.Parse(args[5]));
    var outSub = args.Length > 6 ? args[6] : "RAW"; var lockExp = args.Length > 7 && args[7] == "lockexp";
    var g = (await MediaFrameSourceGroup.FindAllAsync()).First(x => x.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
    using var mc = new MediaCapture();
    await mc.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = g, SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = StreamingCaptureMode.Video });
    var src = mc.FrameSources.Values.First(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
    var f = src.SupportedFormats.First(f => f.VideoFormat.Width == w && f.VideoFormat.Height == h && Math.Abs(f.FrameRate.Numerator / (double)f.FrameRate.Denominator - fps) < 0.5 && f.Subtype == (args.Contains("mjpg") ? "MJPG" : "NV12"));
    Console.WriteLine($"input subtype {f.Subtype}");
    await src.SetFormatAsync(f);
    var rd = outSub == "RAW" ? await mc.CreateFrameReaderAsync(src) : await mc.CreateFrameReaderAsync(src, outSub);
    rd.AcquisitionMode = MediaFrameReaderAcquisitionMode.Buffered;
    var ts = new List<double>(); var gate = new object();
    rd.FrameArrived += (s, e) => { using var fr = s.TryAcquireLatestFrame(); if (fr?.SystemRelativeTime is { } t) lock (gate) ts.Add(t.TotalMilliseconds); };
    await rd.StartAsync();
    // UVC exposure state persists in the camera across processes: always report it, and always restore it.
    var expC = mc.VideoDeviceController.Exposure;
    expC.TryGetAuto(out var expWasAuto); expC.TryGetValue(out var expWasVal);
    Console.WriteLine($"exposure at start: auto={expWasAuto} value={expWasVal}");
    var vdc = mc.VideoDeviceController; vdc.TryGetPowerlineFrequency(out var plWas);
    var plReq = args.Contains("pl50") ? Windows.Media.Capture.PowerlineFrequency.FiftyHertz : args.Contains("pl60") ? Windows.Media.Capture.PowerlineFrequency.SixtyHertz : args.Contains("pl0") ? Windows.Media.Capture.PowerlineFrequency.Disabled : (Windows.Media.Capture.PowerlineFrequency?)null;
    if (plReq is { } plv) { vdc.TrySetPowerlineFrequency(plv); vdc.TryGetPowerlineFrequency(out var plNow); Console.WriteLine($"powerline {plWas} -> {plNow}"); } else Console.WriteLine($"powerline {plWas} (unchanged)");
    var aep = vdc.ExposurePriorityVideoControl; var aepWas = aep.Supported && aep.Enabled;
    Console.WriteLine($"exposurePriority supported={aep.Supported} enabled={(aep.Supported ? aep.Enabled.ToString() : "-")}");
    if (aep.Supported && (args.Contains("aeprio0") || args.Contains("aeprio1"))) { aep.Enabled = args.Contains("aeprio1"); Console.WriteLine($"exposurePriority set enabled={aep.Enabled}"); }
    if (args.Contains("autoexp")) { expC.TrySetAuto(true); await Task.Delay(3000); expC.TryGetAuto(out var a2); Console.WriteLine($"exposure forced auto={a2}"); }
    if (lockExp) { await Task.Delay(3000); expC.TrySetAuto(false); expC.TryGetValue(out var ev); Console.WriteLine($"exposure locked at {ev}"); }
    try { await Task.Delay(secs * 1000); }
    finally
    {
        expC.TryGetValue(out var expEnd); Console.WriteLine($"exposure during run ended at value={expEnd}");
        expC.TrySetAuto(expWasAuto); if (!expWasAuto) expC.TrySetValue(expWasVal);
        if (plReq is not null) vdc.TrySetPowerlineFrequency(plWas);
        if (aep.Supported) aep.Enabled = aepWas;
    }
    await rd.StopAsync();
    List<double> iv; lock (gate) iv = ts.Zip(ts.Skip(1), (a, b) => b - a).Skip(30).ToList();
    var sorted = iv.OrderBy(x => x).ToList();
    Console.WriteLine($"mode={outSub} buffered frames={iv.Count + 1} fps={(iv.Count) / (iv.Sum() / 1000):F3} median={sorted[sorted.Count / 2]:F2} min={sorted[0]:F2} max={sorted[^1]:F2} gaps>1.5x={iv.Count(x => x > 1.5 * 1000 / fps)}");
    var hist = iv.GroupBy(x => Math.Round(x / 4) * 4).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Count()}");
    Console.WriteLine("hist(ms:count) " + string.Join(" ", hist));
    var gapIdx = iv.Select((x, i) => (x, i)).Where(p => p.x > 1.5 * 1000 / fps).Select(p => p.i).Take(12);
    Console.WriteLine("gap positions " + string.Join(",", gapIdx));
    return;
}
if (args.Length >= 2 && args[1] == "aftest")
{
    // FormatProbe <filter> aftest <startPos> : streams 720p, sets manual focus <startPos>, then re-enables AF and
    // prints the focus position every 0.5 s (does a fresh auto toggle make the camera search?). Restores AF on.
    var g = (await MediaFrameSourceGroup.FindAllAsync()).First(x => x.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
    using var mc = new MediaCapture();
    await mc.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = g, SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = StreamingCaptureMode.Video });
    var src = mc.FrameSources.Values.First(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
    var rd = await mc.CreateFrameReaderAsync(src); await rd.StartAsync();
    var fo = mc.VideoDeviceController.Focus;
    fo.TryGetAuto(out var a0); fo.TryGetValue(out var v0); Console.WriteLine($"start auto={a0} pos={v0}");
    var startPos = args.Length > 2 ? double.Parse(args[2]) : 250;
    fo.TrySetAuto(false); fo.TrySetValue(startPos); await Task.Delay(1500); fo.TryGetValue(out var v1); Console.WriteLine($"manual pos={v1}");
    Console.Write("AF on (no toggle, already auto?) ->");
    fo.TrySetAuto(true);
    for (var i = 0; i < 12; i++) { await Task.Delay(500); fo.TryGetValue(out var v); Console.Write($" {v}"); }
    Console.WriteLine();
    Console.Write("AF toggle off/on ->");
    fo.TrySetAuto(false); await Task.Delay(200); fo.TrySetAuto(true);
    for (var i = 0; i < 12; i++) { await Task.Delay(500); fo.TryGetValue(out var v); Console.Write($" {v}"); }
    Console.WriteLine();
    fo.TrySetAuto(true); await rd.StopAsync();
    return;
}
if (args.Length >= 2 && args[1] == "reset")
{
    // FormatProbe <filter> reset : put exposure, white balance and focus back to camera auto (factory behaviour).
    foreach (var g in (await MediaFrameSourceGroup.FindAllAsync()).Where(x => x.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
    {
        using var mc = new MediaCapture();
        try { await mc.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = g, SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = StreamingCaptureMode.Video }); }
        catch (Exception ex) { Console.WriteLine($"{g.DisplayName}: init failed {ex.Message}"); continue; }
        var c = mc.VideoDeviceController;
        c.Exposure.TryGetAuto(out var ea0); c.Exposure.TryGetValue(out var ev0);
        Console.WriteLine($"{g.DisplayName}: before exposure auto={ea0} value={ev0}; set exposure auto={c.Exposure.TrySetAuto(true)} wb auto={c.WhiteBalance.TrySetAuto(true)} focus auto={c.Focus.TrySetAuto(true)}");
        c.Exposure.TryGetAuto(out var ea); c.Exposure.TryGetValue(out var ev); Console.WriteLine($"  readback exposure auto={ea} value={ev}");
    }
    return;
}
if (args.Length >= 2 && args[1] == "controls")
{
    foreach (var g in (await MediaFrameSourceGroup.FindAllAsync()).Where(g => g.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
    {
        using var mc = new MediaCapture();
        try { await mc.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = g, SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = StreamingCaptureMode.Video }); }
        catch (Exception ex) { Console.WriteLine($"{g.DisplayName}: init failed {ex.Message}"); continue; }
        var c = mc.VideoDeviceController;
        Console.WriteLine($"== {g.DisplayName}");
        void Mdc(string n, Windows.Media.Devices.MediaDeviceControl m)
        {
            var cap = m.Capabilities;
            string val = "-"; bool auto = false;
            if (cap.Supported) { if (m.TryGetValue(out var v)) val = v.ToString(); if (cap.AutoModeSupported) m.TryGetAuto(out auto); }
            Console.WriteLine($"  {n,-22} supported={cap.Supported} auto={cap.AutoModeSupported}/{auto} min={cap.Min} max={cap.Max} step={cap.Step} default={cap.Default} value={val}");
        }
        Mdc("Exposure(legacy)", c.Exposure); Mdc("Brightness", c.Brightness); Mdc("Contrast", c.Contrast); Mdc("Hue", c.Hue);
        Mdc("WhiteBalance(legacy)", c.WhiteBalance); Mdc("Focus(legacy)", c.Focus); Mdc("Zoom", c.Zoom); Mdc("BacklightComp", c.BacklightCompensation);
        Console.WriteLine($"  ExposureControl.Supported={c.ExposureControl.Supported} FocusControl={c.FocusControl.Supported} WhiteBalanceControl={c.WhiteBalanceControl.Supported} IsoSpeed={c.IsoSpeedControl.Supported} ExposureComp={c.ExposureCompensationControl.Supported}");
        var plGet = c.TryGetPowerlineFrequency(out var pl);
        Console.WriteLine($"  PowerlineFrequency get={plGet} value={pl}");
        if (plGet)
        {
            var ok50 = c.TrySetPowerlineFrequency(Windows.Media.Capture.PowerlineFrequency.FiftyHertz);
            c.TryGetPowerlineFrequency(out var after); Console.WriteLine($"  set 50Hz ok={ok50} readback={after}");
            c.TrySetPowerlineFrequency(pl); c.TryGetPowerlineFrequency(out var restored); Console.WriteLine($"  restored={restored}");
        }
        if (c.Exposure.Capabilities.Supported)
        {
            c.Exposure.TryGetAuto(out var wasAuto); c.Exposure.TryGetValue(out var wasVal);
            var okManual = c.Exposure.TrySetAuto(false) && c.Exposure.TrySetValue(c.Exposure.Capabilities.Default);
            c.Exposure.TryGetAuto(out var nowAuto); c.Exposure.TryGetValue(out var nowVal);
            Console.WriteLine($"  exposure manual set ok={okManual} auto={nowAuto} value={nowVal} (was auto={wasAuto} value={wasVal})");
            c.Exposure.TrySetAuto(wasAuto);
        }
    }
    return;
}
var groups = (await MediaFrameSourceGroup.FindAllAsync()).Where(g => g.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
Console.WriteLine($"groups matching '{filter}': {groups.Count}");
if (args.Length >= 5)
{
    // concurrent start test: FormatProbe <filter> <subtype|ANY> <w> <h> <fps>
    var (sub, w, h, fps) = (args[1], uint.Parse(args[2]), uint.Parse(args[3]), double.Parse(args[4]));
    var outSub = args.Length > 5 ? args[5] : "BGRA8"; var mem = args.Length > 6 && args[6] == "auto" ? MediaCaptureMemoryPreference.Auto : MediaCaptureMemoryPreference.Cpu;
    var caps = new List<(MediaCapture mc, MediaFrameReader rd, int frames)>();
    var counts = new int[groups.Count];
    for (int i = 0; i < groups.Count; i++)
    {
        var mc = new MediaCapture();
        await mc.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = groups[i], SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = mem, StreamingCaptureMode = StreamingCaptureMode.Video });
        var src = mc.FrameSources.Values.First(s => s.Info.MediaStreamType is Windows.Media.Capture.MediaStreamType.VideoPreview or Windows.Media.Capture.MediaStreamType.VideoRecord);
        var f = src.SupportedFormats.FirstOrDefault(f => f.VideoFormat.Width == w && f.VideoFormat.Height == h
            && Math.Abs(f.FrameRate.Numerator / (double)f.FrameRate.Denominator - fps) < 0.5
            && (sub == "ANY" || string.Equals(f.Subtype, sub, StringComparison.OrdinalIgnoreCase)));
        if (f is null) { Console.WriteLine($"cam{i}: no format {sub} {w}x{h}@{fps}"); return; }
        await src.SetFormatAsync(f);
        var rd = outSub == "RAW" ? await mc.CreateFrameReaderAsync(src) : await mc.CreateFrameReaderAsync(src, outSub);
        var idx = i; rd.FrameArrived += (s, e) => { using var fr = s.TryAcquireLatestFrame(); if (fr?.VideoMediaFrame != null) { if (Interlocked.Increment(ref counts[idx]) == 10) Console.WriteLine($"cam{idx} frame10: sw={fr.VideoMediaFrame.SoftwareBitmap?.BitmapPixelFormat} gpu={fr.VideoMediaFrame.Direct3DSurface != null} sub={fr.Format.Subtype}"); } };
        var st = await rd.StartAsync();
        Console.WriteLine($"cam{i}: {f.Subtype} {w}x{h}@{fps} out={outSub} mem={mem} start={st}");
        caps.Add((mc, rd, 0));
    }
    await Task.Delay(5000);
    for (int i = 0; i < caps.Count; i++) Console.WriteLine($"cam{i}: frames in 5s = {counts[i]} ({counts[i] / 5.0:F1} fps)");
    foreach (var c in caps) { await c.rd.StopAsync(); c.rd.Dispose(); c.mc.Dispose(); }
    return;
}
for (int gi = 0; gi < groups.Count; gi++)
{
    var g = groups[gi];
    using var mc = new MediaCapture();
    await mc.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = g, SharingMode = MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = StreamingCaptureMode.Video });
    foreach (var src in mc.FrameSources.Values.Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color))
    {
        Console.WriteLine($"[{gi}] {g.DisplayName} stream={src.Info.MediaStreamType}");
        int k = 0;
        foreach (var f in src.SupportedFormats)
            Console.WriteLine($"   {k++,2}: {f.Subtype,-5} {f.VideoFormat.Width}x{f.VideoFormat.Height} @ {f.FrameRate.Numerator / (double)f.FrameRate.Denominator:F2}");
    }
}
