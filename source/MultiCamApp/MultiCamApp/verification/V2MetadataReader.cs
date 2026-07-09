////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — reads V2 JSON metadata produced by WriteV2SlotMetadataAsync.
// Does NOT modify MetadataParser (STABLE_CORE_V1).

using MultiCamApp.Utils;

namespace MultiCamApp.Verification;

/// <summary>In-memory representation of one camera slot's V2 metadata JSON file.</summary>
public sealed class V2RecordingMetadata
{
    public bool IsV2 => string.Equals(Engine, "VideoEngineV2", StringComparison.OrdinalIgnoreCase);
    public string Engine               { get; init; } = "";
    public string Backend              { get; init; } = "";
    public string Slot                 { get; init; } = "";
    public string Device               { get; init; } = "";
    public string Resolution           { get; init; } = "";
    public double TargetFps            { get; init; }
    public long   FramesWritten        { get; init; }
    public bool   HardwareEncoderUsed  { get; init; }
    public string EncoderDescription   { get; init; } = "";
    public bool   ColorTaggingApplied     { get; init; }
    public string ColorPrimaries          { get; init; } = "";
    public string ColorTransferFunction   { get; init; } = "";
    public string ColorMatrix             { get; init; } = "";
    public string ColorRange              { get; init; } = "";
    // Frame-integrity verdict — computed and written by MainWindow.xaml.cs at recording-stop
    // time (frameIntegrity.integrityVerdict etc.) but never read back until now. Real-hardware
    // audits found this firing (WARN_CSV_MISMATCH) during genuine camera/USB jitter — the app
    // was already correctly detecting it, just not surfacing it anywhere in Video Verification.
    public string IntegrityVerdict        { get; init; } = "";
    public bool?  CsvRowsMatchFrames      { get; init; }
    public long?  CsvRowsDiff             { get; init; }
    // Session-wide verdict computed by the app itself at recording-stop time (written under
    // "verification" — sessionResult/globalSessionResult — into every camera's own metadata.json,
    // e.g. "PASS_WITH_WARNING"). Already accounts for V2's Original Capture Mode semantics
    // (real per-camera frame-count differences from independently-measured FPS are expected, not
    // a failure) — unlike the legacy ffprobe-based verification stack (VideoVerificationService/
    // SessionComparisonService, STABLE_CORE_V1), which expects the old flat legacy metadata schema
    // and has no visibility into this nested V2 JSON, so it re-derives its own (often wrong) verdict
    // from scratch and can flag a session FAIL for the exact inter-camera frame spread this field
    // already correctly assessed as a warning. See EnrichV2SessionGroups in
    // VideoVerificationPage.xaml.cs for where this authoritative value is used to correct that.
    public string VerificationSessionResult       { get; init; } = "";
    public string VerificationGlobalSessionResult { get; init; } = "";
    // Same schema-blindness problem applies to the Video Verification page's top-level summary
    // cards (Scientific Timing Confidence, Timestamp CSV Status, Session Duration) as it does to
    // session FAIL/PASS — they read "timing"/"verification" fields that only exist in this nested
    // V2 schema, not the legacy flat one. Captured here so ReconcileV2Summary (page code-behind)
    // can correct those cards too, the same authoritative-already-computed-value way.
    public string TimingConfidence             { get; init; } = ""; // timing.timingConfidence
    public string InterCameraTimingConfidence   { get; init; } = ""; // verification.interCameraTimingConfidence
    public bool   TimestampCsvWritten           { get; init; }       // timing.timestampCsvWritten
    public long   TimestampCsvRows              { get; init; }       // timing.timestampCsvRows
    public double ResolvedDurationSeconds       { get; init; }       // timing.resolvedDurationS
    // Real native camera FPS measured from per-frame capture timestamps (timing.estimatedFpsFromTimestamps),
    // written at recording-stop time but — like IntegrityVerdict above — never read back until now.
    // This is the value the "Real Capture FPS" column is meant to show; without it, V2 rows fall
    // back to VerificationReportMapper's legacy-schema read (which is null for V2 recordings), so
    // the column always displayed "-" for V2 sessions even though the real number was on disk.
    public double MeasuredFpsFromTimestamps     { get; init; }       // timing.estimatedFpsFromTimestamps
    // Raw videoSettings.recordingTimingMode string (e.g. "OriginalRealFrameCapture") plus a
    // derived bool — used to enrich CameraMetadataRecord.OriginalCaptureMode/RecordingTimingMode
    // for V2 rows the same way the fields above are used, so the exported report's
    // "Original Capture Mode" line and interpretation note aren't silently skipped for V2 sessions.
    public string RecordingTimingMode          { get; init; } = ""; // videoSettings.recordingTimingMode
    public bool   IsOriginalCaptureMode        =>
        RecordingTimingMode.Contains("Original", StringComparison.OrdinalIgnoreCase);
    public double MeanFrameIntervalMs   { get; init; } // timing.meanFrameIntervalMs
    public double MinFrameIntervalMs    { get; init; } // timing.minFrameIntervalMs
    public double MaxFrameIntervalMs    { get; init; } // timing.maxFrameIntervalMs
    // First/last per-frame timestamp, milliseconds since recording start (timing.firstFrameTimestampMs/
    // lastFrameTimestampMs) — used to map CameraMetadataRecord.FirstFrameCaptureMonotonicSec/
    // LastFrameCaptureMonotonicSec (legacy calls these "critical" scientific-timing fields; V2 has the
    // same information under a different name/unit, just needs converting ms -> seconds).
    public double FirstFrameTimestampMs { get; init; } // timing.firstFrameTimestampMs
    public double LastFrameTimestampMs  { get; init; } // timing.lastFrameTimestampMs
    public double IntervalStdMs         { get; init; } // timingModels.appTimestampTiming.intervalStdMs
    public string FrameTimestampCsvFile { get; init; } = ""; // timing.timestampCsvFile (filename only)
    public string OutputFile           { get; init; } = "";
    public string Status               { get; init; } = "";
    public bool   EnvironmentalLockActive    { get; init; }
    public bool   FocusLocked          { get; init; }
    public bool   ExposureLocked       { get; init; }
    public double? ExposureLockedAtSeconds   { get; init; }
    public bool   WhiteBalanceLocked   { get; init; }
    public uint?  WhiteBalanceLockedAtK      { get; init; }
    public bool   IsoLocked            { get; init; }
    public IReadOnlyList<V2ControlInfo> Controls { get; init; } = Array.Empty<V2ControlInfo>();
}

public sealed class V2ControlInfo
{
    public string  Control        { get; init; } = "";
    public bool    Applied        { get; init; }
    public string  ReadbackStatus { get; init; } = "";
    public double? ReadbackValue  { get; init; }
    public string? Warning        { get; init; }
}

/// <summary>
/// Reads V2 JSON metadata produced by <c>WriteV2SlotMetadataAsync</c>.
/// Returns null when the JSON is absent, unreadable, or not a V2 file.
/// </summary>
public static class V2MetadataReader
{
    /// <summary>Parses a V2 metadata JSON file. Returns null if not V2 or on any error.</summary>
    public static V2RecordingMetadata? TryRead(string metadataJsonPath)
    {
        try
        {
            if (!File.Exists(metadataJsonPath)) return null;
            var json = File.ReadAllText(metadataJsonPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // V2 JSON nests engine info under "recordingEngine".
            if (!root.TryGetProperty("recordingEngine", out var reEl)) return null;
            var engineName = Str(reEl, "engine");
            if (!string.Equals(engineName, "VideoEngineV2", StringComparison.OrdinalIgnoreCase))
                return null;

            // Camera info lives under cameras[0]
            string slot = "", device = "", resolution = "";
            double targetFps = 0;
            if (root.TryGetProperty("cameras", out var camsEl)
                && camsEl.ValueKind == System.Text.Json.JsonValueKind.Array
                && camsEl.GetArrayLength() > 0)
            {
                var cam0 = camsEl[0];
                slot       = Str(cam0, "slot");
                device     = Str(cam0, "device");
                resolution = Str(cam0, "selectedResolution");
                targetFps  = Dbl(cam0, "requestedFps") ?? 0;
            }

            // Recording info lives under "recording"
            string outputFile = "", status = "";
            if (root.TryGetProperty("recording", out var recEl))
            {
                outputFile    = Str(recEl, "outputFile");
                status        = Str(recEl, "writerStatus");
            }

            // Timing block — see TimingConfidence/TimestampCsvWritten/ResolvedDurationSeconds doc
            // comments on V2RecordingMetadata.
            long   framesWritten = 0;
            string timingConfidence = "";
            bool   timestampCsvWritten = false;
            long   timestampCsvRows = 0;
            double resolvedDurationSeconds = 0;
            double measuredFpsFromTimestamps = 0;
            double meanFrameIntervalMs = 0, minFrameIntervalMs = 0, maxFrameIntervalMs = 0;
            double firstFrameTimestampMs = 0, lastFrameTimestampMs = 0;
            string frameTimestampCsvFile = "";
            if (root.TryGetProperty("timing", out var timEl))
            {
                // NOTE: "framesWritten" was previously (mis)read from the "recording" block above,
                // which has no such key (only started/finalized/outputFile/writerStatus/etc.) — this
                // silently returned 0 for every V2 recording ever produced. Masked because every
                // consumer either had a working ffprobe fallback (VerificationTableRow's own
                // FramesWrittenDisplay) or simply displayed the resulting "-"/0 without anyone
                // noticing it never carried real data. The real count is here, under "timing".
                framesWritten           = Lng(timEl, "framesWritten") ?? 0;
                timingConfidence        = Str(timEl, "timingConfidence");
                timestampCsvWritten     = Bool(timEl, "timestampCsvWritten");
                timestampCsvRows        = Lng(timEl, "timestampCsvRows") ?? 0;
                resolvedDurationSeconds = Dbl(timEl, "resolvedDurationS") ?? 0;
                measuredFpsFromTimestamps = Dbl(timEl, "estimatedFpsFromTimestamps") ?? 0;
                meanFrameIntervalMs     = Dbl(timEl, "meanFrameIntervalMs") ?? 0;
                minFrameIntervalMs      = Dbl(timEl, "minFrameIntervalMs") ?? 0;
                maxFrameIntervalMs      = Dbl(timEl, "maxFrameIntervalMs") ?? 0;
                frameTimestampCsvFile   = Str(timEl, "timestampCsvFile");
                firstFrameTimestampMs   = Dbl(timEl, "firstFrameTimestampMs") ?? 0;
                lastFrameTimestampMs    = Dbl(timEl, "lastFrameTimestampMs") ?? 0;
            }

            // Per-frame interval standard deviation lives one level deeper, under
            // timingModels.appTimestampTiming — not in the flatter "timing" block above.
            double intervalStdMs = 0;
            string recordingTimingMode = "";
            if (root.TryGetProperty("timingModels", out var tmEl)
                && tmEl.TryGetProperty("appTimestampTiming", out var attEl))
            {
                intervalStdMs = Dbl(attEl, "intervalStdMs") ?? 0;
            }
            if (root.TryGetProperty("videoSettings", out var vsEl))
            {
                recordingTimingMode = Str(vsEl, "recordingTimingMode");
            }

            // Frame integrity verdict lives under "frameIntegrity" — computed at recording-stop
            // time but, until now, never read back anywhere in Video Verification.
            string integrityVerdict = "";
            bool?  csvRowsMatchFrames = null;
            long?  csvRowsDiff = null;
            if (root.TryGetProperty("frameIntegrity", out var fiEl))
            {
                integrityVerdict = Str(fiEl, "integrityVerdict");
                if (fiEl.TryGetProperty("csvRowsMatchFrames", out var matchEl)
                    && matchEl.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    csvRowsMatchFrames = matchEl.GetBoolean();
                csvRowsDiff = Lng(fiEl, "csvRowsDiff");
            }

            // Session-wide verdict — see VerificationSessionResult/VerificationGlobalSessionResult
            // doc comment. Nested under "verification".
            string verificationSessionResult = "", verificationGlobalSessionResult = "", interCameraTimingConfidence = "";
            if (root.TryGetProperty("verification", out var verEl))
            {
                verificationSessionResult       = Str(verEl, "sessionResult");
                verificationGlobalSessionResult = Str(verEl, "globalSessionResult");
                interCameraTimingConfidence     = Str(verEl, "interCameraTimingConfidence");
            }

            // Controls
            var controls = new List<V2ControlInfo>();
            if (root.TryGetProperty("controls", out var ctrlsEl)
                && ctrlsEl.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in ctrlsEl.EnumerateObject())
                {
                    var c = prop.Value;
                    controls.Add(new V2ControlInfo
                    {
                        Control        = prop.Name,
                        Applied        = Bool(c, "applied"),
                        ReadbackStatus = Str(c, "result"),
                        ReadbackValue  = Dbl(c, "readback"),
                        Warning        = StrOrNull(c, "warning"),
                    });
                }
            }

            // Environmental lock
            bool envLockActive = false, focusLocked = false, expLocked = false,
                 wbLocked = false, isoLocked = false;
            double? expLockedAtS = null;
            uint?   wbLockedAtK = null;
            if (root.TryGetProperty("environmentalLock", out var lockEl))
            {
                envLockActive = Bool(lockEl, "activeAtRecordingStart");
                focusLocked   = Bool(lockEl, "focusLocked");
                expLocked     = Bool(lockEl, "exposureLocked");
                expLockedAtS  = Dbl(lockEl, "exposureLockedAtSeconds");
                wbLocked      = Bool(lockEl, "whiteBalanceLocked");
                if (lockEl.TryGetProperty("whiteBalanceLockedAtK", out var wbKEl)
                    && wbKEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                    wbLockedAtK = wbKEl.GetUInt32();
                isoLocked = Bool(lockEl, "isoLocked");
            }

            // Color tagging (v1.2.66 Phase 2) — nested under "backendInfo"
            bool colorTaggingApplied = false;
            string colorPrimaries = "", colorTransferFunction = "", colorMatrix = "", colorRange = "";
            if (root.TryGetProperty("backendInfo", out var biEl))
            {
                colorTaggingApplied   = Bool(biEl, "colorTaggingApplied");
                colorPrimaries        = Str(biEl, "colorPrimaries");
                colorTransferFunction = Str(biEl, "colorTransferFunction");
                colorMatrix           = Str(biEl, "colorMatrix");
                colorRange            = Str(biEl, "colorRange");
            }

            return new V2RecordingMetadata
            {
                Engine                  = engineName,
                Backend                 = Str(reEl, "backend"),
                Slot                    = slot,
                Device                  = device,
                Resolution              = resolution,
                TargetFps               = targetFps,
                FramesWritten           = framesWritten,
                HardwareEncoderUsed     = Bool(reEl, "hardwareEncoderUsed"),
                EncoderDescription      = Str(reEl, "encoderDescription"),
                ColorTaggingApplied     = colorTaggingApplied,
                ColorPrimaries          = colorPrimaries,
                ColorTransferFunction   = colorTransferFunction,
                ColorMatrix             = colorMatrix,
                ColorRange              = colorRange,
                IntegrityVerdict        = integrityVerdict,
                CsvRowsMatchFrames      = csvRowsMatchFrames,
                CsvRowsDiff             = csvRowsDiff,
                VerificationSessionResult       = verificationSessionResult,
                VerificationGlobalSessionResult = verificationGlobalSessionResult,
                TimingConfidence                = timingConfidence,
                InterCameraTimingConfidence      = interCameraTimingConfidence,
                TimestampCsvWritten              = timestampCsvWritten,
                TimestampCsvRows                 = timestampCsvRows,
                ResolvedDurationSeconds          = resolvedDurationSeconds,
                MeasuredFpsFromTimestamps        = measuredFpsFromTimestamps,
                RecordingTimingMode     = recordingTimingMode,
                MeanFrameIntervalMs     = meanFrameIntervalMs,
                MinFrameIntervalMs      = minFrameIntervalMs,
                MaxFrameIntervalMs      = maxFrameIntervalMs,
                FirstFrameTimestampMs   = firstFrameTimestampMs,
                LastFrameTimestampMs    = lastFrameTimestampMs,
                IntervalStdMs           = intervalStdMs,
                FrameTimestampCsvFile   = frameTimestampCsvFile,
                OutputFile              = outputFile,
                Status                  = status,
                EnvironmentalLockActive = envLockActive,
                FocusLocked             = focusLocked,
                ExposureLocked          = expLocked,
                ExposureLockedAtSeconds = expLockedAtS,
                WhiteBalanceLocked      = wbLocked,
                WhiteBalanceLockedAtK   = wbLockedAtK,
                IsoLocked               = isoLocked,
                Controls                = controls,
            };
        }
        catch (Exception ex)
        {
            // Previously fully silent — a corrupted/unexpected-schema metadata file looked
            // identical in the UI to "no metadata written yet", with zero diagnostic trail.
            AppDiagnosticLogger.Runtime(
                $"V2_METADATA_READ_FAILED path={PrivacySanitizer.FileNameOnly(metadataJsonPath)} " +
                $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Given a video file path (e.g. <c>cam1.mp4</c>), finds and reads its sibling
    /// <c>cam1_metadata.json</c> as V2 metadata.
    /// </summary>
    public static V2RecordingMetadata? TryReadForVideo(string videoPath)
    {
        var dir      = Path.GetDirectoryName(videoPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        return TryRead(Path.Combine(dir, baseName + "_metadata.json"));
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────

    private static string Str(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static string? StrOrNull(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() : null;

    private static bool Bool(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;

    private static double? Dbl(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
            ? v.GetDouble() : null;

    private static long? Lng(System.Text.Json.JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
            ? v.GetInt64() : null;
}
