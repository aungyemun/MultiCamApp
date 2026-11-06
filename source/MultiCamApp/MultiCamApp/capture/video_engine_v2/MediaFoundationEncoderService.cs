////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).
// Records H.264 MP4 via a raw Media Foundation IMFSinkWriter (Vortice.MediaFoundation), writing
// real per-frame timestamps (honest VFR — matches Windows Camera app's container timing) instead
// of the previous WinRT LowLagMediaRecording approach, which forced CFR container muxing and had
// no defined way to propagate color tags into the muxed output (see ApplyColorTags note below).

using MultiCamApp.Utils;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Records H.264 MP4 via a raw <see cref="IMFSinkWriter"/>, fed one frame at a time from
/// <see cref="CameraPipelineV2.OnFrameArrived"/> via <see cref="SubmitFrame"/>. Each sample is
/// stamped with the frame's real Media Foundation presentation timestamp (already the "ground
/// truth" <see cref="FrameTimestampMonitor"/> writes to the sidecar CSV), so the muxed container
/// preserves genuine per-frame timing instead of a fabricated constant frame rate.
/// </summary>
/// <remarks>
/// Scientific frame policy (from VideoEngineV2 specification):
/// - Do not duplicate frames.
/// - Do not insert placeholder frames.
/// - Do not manufacture a constant frame count.
/// - Record actual per-frame timestamps (via <see cref="FrameTimestampMonitor"/>).
/// - Dropped frames are reported honestly in <see cref="GetHealth"/>.
/// </remarks>
public sealed class MediaFoundationEncoderService : IDisposable
{
    // Media Foundation GUIDs (mfidl.h / codecapi.h) used by exception #15.
    private static readonly Guid MfTranscodeContainerType      = new("150FF23F-4ABC-478B-AC4F-E1916FBA1CCA"); // MF_TRANSCODE_CONTAINERTYPE
    private static readonly Guid MfTranscodeContainerTypeFmpeg4 = new("9BA876F1-419F-4B77-A1E0-35959D9D4004"); // MFTranscodeContainerType_FMPEG4
    private static readonly Guid CodecApiGopSize               = new("95F31B26-95A4-41AA-9303-246A7FC6EEF1"); // CODECAPI_AVEncMPVGOPSize
    private static readonly Guid CodecApiRateControlMode       = new("1C0608E9-370C-4710-8A58-CB6181C42423"); // CODECAPI_AVEncCommonRateControlMode
    private static readonly Guid CodecApiQuality               = new("FCBF57A3-7EA5-4B0C-9644-69B40C39C391"); // CODECAPI_AVEncCommonQuality

    // Guards every call into _sinkWriter. IMFSinkWriter is not documented as safe for concurrent
    // calls from multiple threads; SubmitFrame runs on the camera's frame-arrived callback thread
    // while FinaliseAsync/Dispose can run on the UI/thread-pool thread that stopped the recording —
    // without this lock those two could race on the same COM object (WriteSample vs. Finalize, or
    // even two overlapping WriteSample calls if a frame-arrived callback ever re-enters), which is
    // exactly the kind of unsynchronized native COM access that can corrupt state or crash the
    // whole process below the level any managed exception handler can catch (observed: the app
    // crashed with no exception logged anywhere, including the AppDomain.UnhandledException/
    // crash.log safety net — consistent with a native-level fault, not a normal .NET exception).
    private readonly object _sinkWriterLock = new();
    private IMFSinkWriter? _sinkWriter;
    private int  _videoStreamIndex = -1;
    private long _firstSampleTicks = -1;
    private bool _mfRefHeld;
    private bool _disposed;
    private bool _encoding;
    private long _framesSubmitted;
    private DateTimeOffset _recordingStartTime;

    /// <summary>Raised when a frame has been submitted to the sink writer.</summary>
    public event EventHandler<long>? FrameEncoded;   // long = frame index

    /// <summary>Raised when encoding is finalised and the MP4 file is closed.</summary>
    public event EventHandler<V2EncoderFinalisedEventArgs>? EncodingFinalised;

    /// <summary>Raised on encoder error.</summary>
    public event EventHandler<Exception>? EncoderError;

    /// <summary>Backend encoder actually in use.</summary>
    public EncoderBackendType ActiveEncoderBackend { get; private set; } = EncoderBackendType.NotSelected;

    /// <summary>True if hardware H.264 (NVENC / QuickSync) was detected as available.</summary>
    public bool HardwareEncoderAvailable { get; private set; }

    /// <summary>True while the sink writer is accepting frames.</summary>
    public bool IsEncoding => _encoding;

    /// <summary>Total frames that have been recorded this session.</summary>
    public long FramesSubmitted => _framesSubmitted;

    /// <summary>
    /// Frames submitted since <see cref="StartAsync"/> (recording-relative; excludes preview frames).
    /// Reset in <see cref="StartAsync"/>; identical to <see cref="FramesSubmitted"/> by design.
    /// </summary>
    public long FramesSubmittedSinceRecordingStart => _framesSubmitted;

    /// <summary>Path to the output file as passed to <see cref="OpenAsync"/>.</summary>
    public string? OutputPath { get; private set; }

    // ── Encoder warm-up (2.1.0) ────────────────────────────────────────────────
    // Measured: the FIRST Start Recording in a process took 2.28 s, the next ones 0.31 s — the hardware
    // H.264 MFT/driver initialisation is a one-time per-process cost. Opening and discarding one encoder
    // in the background while the user is still previewing moves that cost out of the Start click.
    private static int s_warmUpStarted;

    public static Task WarmUpOnceAsync(int width, int height, double fps, TimeSpan delay)
    {
        if (!VideoEngineSettings.EncoderWarmUp || Interlocked.Exchange(ref s_warmUpStarted, 1) == 1)
            return Task.CompletedTask;
        return Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var path = Path.Combine(Path.GetTempPath(), $"multicamapp_encoder_warmup_{Environment.ProcessId}.mp4");
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                sw.Restart();
                using var enc = new MediaFoundationEncoderService();
                await enc.OpenAsync(null!, path, new V2EncoderProfile
                {
                    Width = width, Height = height, TargetFps = fps > 0 ? fps : 30,
                    TargetBitrateKbps = VideoEngineSettings.ResolveTargetBitrateKbps(width, height, fps),
                    PreferHardware = VideoEngineSettings.PreferHardwareEncoder,
                }).ConfigureAwait(false);
                AppDiagnosticLogger.Recording($"V2_ENCODER_WARMUP ok ms={sw.ElapsedMilliseconds} size={width}x{height}@{fps:F1}");
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Recording($"V2_ENCODER_WARMUP failed ms={sw.ElapsedMilliseconds} {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
            }
        });
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Prepares an <see cref="IMFSinkWriter"/> targeting <paramref name="outputPath"/>.
    /// No frames are written until <see cref="SubmitFrame"/> is called after <see cref="StartAsync"/>.
    /// </summary>
    public async Task OpenAsync(
        Windows.Media.Capture.MediaCapture mediaCapture,
        string outputPath,
        V2EncoderProfile profile,
        CancellationToken ct = default)
    {
        if (_encoding)
            await FinaliseAsync(ct).ConfigureAwait(false);

        // STABLE_CORE_V2 exception #2: only take the MF ref once. A failed OpenAsync keeps the ref
        // (released by FinaliseAsync/Dispose); re-adding it on every retry leaked platform refs.
        if (!_mfRefHeld)
        {
            MediaFoundationRuntime.AddRef();
            _mfRefHeld = true;
        }

        OutputPath = outputPath;
        _framesSubmitted  = 0;
        _firstSampleTicks = -1;

        HardwareEncoderAvailable = profile.PreferHardware
            && VideoEngineDiagnostics.ProbeDirect3D11() == V2CapabilityAvailability.Available;
        ActiveEncoderBackend = HardwareEncoderAvailable
            ? EncoderBackendType.MediaFoundationH264
            : EncoderBackendType.MediaFoundationSoftwareH264;

        // Everything below is synchronous COM work (sink writer creation + AddStream +
        // SetInputMediaType + BeginWriting, which negotiates/instantiates the encoder MFT and can
        // genuinely take a non-trivial amount of wall-clock time) — unlike the old
        // PrepareLowLagRecordToStorageFileAsync WinRT call it replaced, none of this has a real
        // await point. Run it on a thread-pool thread so OpenAsync actually yields control back to
        // its caller promptly: StartV2DefaultRecordingAsync opens all 4 camera slots via
        // Task.WhenAll, which only achieves real concurrency if each slot's OpenAsync genuinely
        // frees the calling thread instead of blocking it — running this synchronously here (as
        // originally written) serialized all 4 cameras' encoder setup one after another instead of
        // overlapping them, directly worsening the Start Recording UI freeze this session set out
        // to fix.
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            if (File.Exists(outputPath)) File.Delete(outputPath);

            using var sinkAttrs = MediaFactory.MFCreateAttributes(3);
            sinkAttrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, HardwareEncoderAvailable);
            sinkAttrs.Set(SinkWriterAttributeKeys.LowLatency, true);
            // STABLE_CORE_V2 exception #15: crash-safe container. A plain MP4 gets its index (moov)
            // only at Finalize(), so a crash / power loss / forced close left camN.tmp.mp4 unplayable.
            // Fragmented MP4 writes self-describing fragments as it goes (verified by a kill test).
            if (VideoEngineSettings.CrashSafeFragmentedMp4)
                sinkAttrs.Set(MfTranscodeContainerType, MfTranscodeContainerTypeFmpeg4);
            // No IMFDXGIDeviceManager/D3DManager wired up: the capture pipeline always delivers CPU
            // SoftwareBitmap frames on this app's camera configuration (MediaFoundationCaptureService
            // hard-codes MemoryPreference.Cpu), so there is no GPU surface to hand the encoder directly.
            // ReadwriteEnableHardwareTransforms alone still lets the sink writer select a hardware
            // H.264 MFT (NVENC/QuickSync) via normal MFT category resolution.

            // Build and fully configure the writer in a local variable first — only publish it to
            // the _sinkWriter field (under the lock, alongside _videoStreamIndex) once
            // BeginWriting() has succeeded, so a concurrent SubmitFrame call can never observe a
            // half-configured writer.
            var newWriter = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, sinkAttrs);

            var codecSubtype = profile.Codec == V2VideoCodec.Hevc ? VideoFormatGuids.Hevc : VideoFormatGuids.H264;

            using var outType = MediaFactory.MFCreateMediaType();
            outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outType.Set(MediaTypeAttributeKeys.Subtype, codecSubtype);
            outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(Math.Max(profile.TargetBitrateKbps, 1) * 1000));
            MediaFactory.MFSetAttributeSize(outType, MediaTypeAttributeKeys.FrameSize, (uint)profile.Width, (uint)profile.Height);
            MediaFactory.MFSetAttributeRatio(outType, MediaTypeAttributeKeys.FrameRate,
                (uint)Math.Round(profile.TargetFps * 1000), 1000u);
            MediaFactory.MFSetAttributeRatio(outType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
            outType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)2 /* MFVideoInterlace_Progressive */);
            ApplyColorTags(outType);

            try
            {
            var newStreamIndex = newWriter.AddStream(outType);

            using var inType = MediaFactory.MFCreateMediaType();
            inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Argb32); // matches WinRT BGRA8 premultiplied byte layout
            MediaFactory.MFSetAttributeSize(inType, MediaTypeAttributeKeys.FrameSize, (uint)profile.Width, (uint)profile.Height);
            MediaFactory.MFSetAttributeRatio(inType, MediaTypeAttributeKeys.FrameRate,
                (uint)Math.Round(profile.TargetFps * 1000), 1000u);
            MediaFactory.MFSetAttributeRatio(inType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
            inType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)2);
            ApplyColorTags(inType); // tag input too — some MFTs propagate input color tags to output when the output doesn't already specify its own

            // Exception #15: 1-second keyframe interval — bounds crash loss to about one fragment and
            // gives analysis tools frame-accurate seeking (previous encoder default ≈ 4.4 s GOP).
            using var encParams = MediaFactory.MFCreateAttributes(3);
            // Test hook (off unless set): MULTICAMAPP_TEST_RATE_CONTROL=quality:70 — used to measure on hardware
            // whether the selected encoder honours quality-based rate control before relying on it.
            var rcTest = Environment.GetEnvironmentVariable("MULTICAMAPP_TEST_RATE_CONTROL");
            if (rcTest is { } rt && rt.StartsWith("quality:", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(rt[8..], out var tq))
            {
                VideoEngineSettings.RateControl = V2RateControl.QualityVbr;
                VideoEngineSettings.EncoderQuality = tq;
            }
            if (VideoEngineSettings.RateControl == V2RateControl.QualityVbr)
            {
                encParams.Set(CodecApiRateControlMode, 3u);   // eAVEncCommonRateControlMode_Quality
                encParams.Set(CodecApiQuality, Math.Clamp(VideoEngineSettings.EncoderQuality, 1u, 100u));
            }
            if (VideoEngineSettings.KeyframeIntervalSeconds > 0)
                encParams.Set(CodecApiGopSize, (uint)Math.Clamp(Math.Round(profile.TargetFps * VideoEngineSettings.KeyframeIntervalSeconds), 1, 600));
            newWriter.SetInputMediaType(newStreamIndex, inType, encParams);
            newWriter.BeginWriting();

            lock (_sinkWriterLock)
            {
                _sinkWriter = newWriter;
                _videoStreamIndex = newStreamIndex;
            }
            }
            catch
            {
                // STABLE_CORE_V2 exception #2: a rejected media type (e.g. a size/FPS the H.264 MFT
                // refuses) previously leaked the sink writer, keeping camN.tmp.mp4 open.
                newWriter.Dispose();
                throw;
            }
        }, ct).ConfigureAwait(false);

        AppDiagnosticLogger.Recording(
            $"V2_ENC_OPEN codec={profile.Codec} requestedWidth={profile.Width} requestedHeight={profile.Height} " +
            $"bitrateKbps={profile.TargetBitrateKbps} targetFps={profile.TargetFps:F1} " +
            $"rateControl={VideoEngineSettings.RateControl} quality={VideoEngineSettings.EncoderQuality} gopS={VideoEngineSettings.KeyframeIntervalSeconds} " +
            $"preferHardware={profile.PreferHardware} hwAvailable={HardwareEncoderAvailable} " +
            $"backend={ActiveEncoderBackend} sinkWriter=IMFSinkWriter file={Path.GetFileName(outputPath)}");
    }

    /// <summary>Marks the encoder ready to accept frames via <see cref="SubmitFrame"/>.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_sinkWriter is null)
            throw new InvalidOperationException("Call OpenAsync before StartAsync.");

        _encoding           = true;
        _recordingStartTime = DateTimeOffset.UtcNow;
        _framesSubmitted    = 0;
        _firstSampleTicks   = -1;
        AppDiagnosticLogger.Recording(
            $"V2_ENC_START backend={ActiveEncoderBackend} hw={HardwareEncoderAvailable} " +
            $"file={Path.GetFileName(OutputPath ?? "?")} started={_recordingStartTime:HH:mm:ss.fff}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Submits one frame's raw BGRA8 pixel data to the sink writer, stamped with the frame's real
    /// Media Foundation presentation timestamp so the muxed container preserves genuine per-frame
    /// VFR spacing. Must only be called while <see cref="IsEncoding"/> is true. Runs on the
    /// camera's frame-arrived callback thread — stays allocation-light (one MF-owned buffer copy)
    /// and never blocks on I/O, mirroring the constraint already documented on
    /// <see cref="FrameTimestampMonitor.RecordFrame"/>.
    /// </summary>
    /// <param name="height">
    /// Actual pixel height <paramref name="bgra8Pixels"/> was sized for (the caller's
    /// SoftwareBitmap.PixelHeight). Deliberately NOT <c>frame.Height</c> (the WinRT-negotiated
    /// format's reported height) — the two can momentarily disagree while a resolution/FPS change
    /// is renegotiating the capture format, and computing the destination buffer size from a value
    /// that doesn't match the actual source array's length risks an out-of-bounds native copy.
    /// </param>
    /// <returns>True only when the frame was written to the sink writer (STABLE_CORE_V2 exception #8:
    /// the timestamp CSV row is written only for accepted frames).</returns>
    public bool SubmitFrame(V2FrameArrivedEventArgs frame, byte[] bgra8Pixels, int stride, int height)
    {
        if (!_encoding || _videoStreamIndex < 0) return false;

        int byteCount = stride * height;
        if (byteCount <= 0 || bgra8Pixels.Length < byteCount)
        {
            AppDiagnosticLogger.Recording(
                $"V2_ENC_SUBMIT_SIZE_MISMATCH byteCount={byteCount} pixelsLength={bgra8Pixels.Length} — frame dropped");
            return false;
        }

        lock (_sinkWriterLock)
        {
            if (_sinkWriter is null) return false;
            // buffer/sample are native COM wrapper objects (IMFMediaBuffer/IMFSample). Each one
            // holds an unmanaged allocation (byteCount bytes — e.g. ~5.5 MB at 1920x1080 BGRA)
            // that is only released when the wrapper's Dispose()/finalizer runs Release() on the
            // native pointer. Relying on GC finalization here (as this code did before) means the
            // finalizer queue — a single background thread — has to keep up with up to 240
            // allocations/sec (4 cameras x 60fps), each holding several MB of native memory alive
            // until finalized; under sustained recording the allocation rate outpaces finalization,
            // so committed unmanaged memory grows without bound until the system runs out of
            // memory/GPU resources — this is the mechanism behind the "freeze after ~1 minute of
            // recording, escalating to full GPU/CPU/PC freeze" failure mode. Explicit Dispose()
            // immediately after WriteSample releases the native memory right away instead of
            // waiting on GC; this is safe because IMFSample::AddBuffer and
            // IMFSinkWriter::WriteSample both AddRef internally (standard COM ref-counting), so
            // releasing our own reference here does not free memory the sink writer still needs.
            IMFMediaBuffer? buffer = null;
            IMFSample? sample = null;
            try
            {
                buffer = MediaFactory.MFCreateMemoryBuffer(byteCount);
                buffer.Lock(out var ptr, out _, out _);
                try { Marshal.Copy(bgra8Pixels, 0, ptr, byteCount); }
                finally { buffer.Unlock(); }
                buffer.CurrentLength = byteCount;

                sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);

                if (_firstSampleTicks < 0) _firstSampleTicks = frame.PresentationTimestamp.Ticks;
                sample.SampleTime = frame.PresentationTimestamp.Ticks - _firstSampleTicks;
                // SampleDuration deliberately left unset (0) — the muxer derives each frame's actual
                // displayed duration from the delta between consecutive real SampleTime values. Setting
                // a fixed nominal duration (e.g. 1/fps) here would silently recreate the CFR-implied-
                // duration problem this migration exists to fix, just at the sample level.

                _sinkWriter.WriteSample(_videoStreamIndex, sample);

                Interlocked.Increment(ref _framesSubmitted);
                FrameEncoded?.Invoke(this, frame.FrameIndex);
                return true;
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Recording($"V2_ENC_WRITE_ERROR {ex.GetType().Name}: {ex.Message}");
                EncoderError?.Invoke(this, ex);
                // Deliberately does not stop encoding on a single frame failure — matches the existing
                // "dropped frames reported honestly, never crash the session" policy.
            }
            finally
            {
                sample?.Dispose();
                buffer?.Dispose();
            }
        }
        return false;
    }

    /// <summary>
    /// Stops recording, finalises the MP4 container (writes moov atom), and releases the sink writer.
    /// Does NOT rename the temp file — the caller (<see cref="CameraPipelineV2"/>) does that.
    /// </summary>
    public async Task FinaliseAsync(CancellationToken ct = default)
    {
        if (!_encoding) return;
        _encoding = false; // checked first (before the lock) by any concurrent SubmitFrame call

        IMFSinkWriter? writer;
        lock (_sinkWriterLock)
        {
            // Waits for any in-flight SubmitFrame call (same lock) to finish before swapping
            // _sinkWriter to null, so WriteSample/Finalize on the same underlying COM object are
            // never invoked concurrently from two threads.
            writer = _sinkWriter;
            _sinkWriter = null;
        }
        if (writer is null) return;

        var duration = DateTimeOffset.UtcNow - _recordingStartTime;
        try
        {
            // Finalize() flushes and writes the moov atom — can take up to ~100s of ms for a long
            // recording. Run on a background thread so this call (already ConfigureAwait(false) at
            // every hop up through CameraPipelineV2.StopRecordingAsync) never blocks the UI thread.
            await Task.Run(() => writer.Finalize(), ct).ConfigureAwait(false);
            AppDiagnosticLogger.Recording(
                $"V2_ENC_FINAL frames={_framesSubmitted} duration={duration.TotalSeconds:F2}s " +
                $"avgFps={(_framesSubmitted / Math.Max(duration.TotalSeconds, 0.001)):F2} " +
                $"file={Path.GetFileName(OutputPath ?? "?")}");
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Recording($"V2_ENC_ERROR_FINAL {ex.GetType().Name}: {ex.Message}");
            EncoderError?.Invoke(this, ex);
        }
        finally
        {
            writer.Dispose();
            _videoStreamIndex = -1;
            if (_mfRefHeld) { MediaFoundationRuntime.Release(); _mfRefHeld = false; }
        }

        EncodingFinalised?.Invoke(this, new V2EncoderFinalisedEventArgs
        {
            FramesWritten = _framesSubmitted,
            Duration      = duration,
        });
    }

    /// <summary>
    /// Tags the media type with BT.709 color primaries/transfer/matrix + full (pc, 0-255)
    /// nominal range, matching Windows Camera app's actual convention for HD content. Applied to
    /// both the output (H.264) and input (BGRA8) media types in <see cref="OpenAsync"/> — some
    /// color-conversion MFTs propagate the input's tags to the output only if the output doesn't
    /// already specify its own, so setting both is cheap, belt-and-suspenders insurance.
    /// </summary>
    /// <remarks>
    /// This replaces the v1.2.27-alpha attempt (reverted in v1.2.28 — see this file's git history/
    /// CHANGELOG) which tried the same GUIDs via <c>VideoEncodingProperties.Properties[guid]</c> on
    /// a <c>MediaEncodingProfile</c>; that WinRT property bag had no defined pass-through into the
    /// muxed output via <c>PrepareLowLagRecordToStorageFileAsync</c> and was confirmed via ffprobe
    /// to never take effect. Setting the identical attributes directly on the <see cref="IMFMediaType"/>
    /// object this Sink Writer actually consumes is the correct, lower layer.
    ///
    /// The four native integer values below were verified against the real Windows SDK header
    /// (um/mfobjects.h, <c>MFVideoTransferFunction</c>/<c>MFVideoPrimaries</c>/
    /// <c>MFVideoTransferMatrix</c>/<c>MFNominalRange</c> enums) rather than assumed — notably,
    /// <c>MFVideoPrimaries_BT709</c> is 2 (not 4) and <c>MFVideoTransFunc_709</c> is 5 (not 4).
    ///
    /// Nominal range was originally set to <c>MFNominalRange_16_235</c> (limited/tv, value 2) on the
    /// unverified assumption that this matched Windows Camera's own output. A v1.2.78 side-by-side
    /// ffprobe comparison against a real Windows Camera recording proved that assumption wrong —
    /// Windows Camera actually tags <c>color_range=pc</c> (full, 0-255) — so this now uses
    /// <c>MFNominalRange_Normal</c> (0-255/pc, value 1) to genuinely match it.
    /// </remarks>
    private static void ApplyColorTags(IMFMediaType type)
    {
        const uint MFVideoPrimaries_BT709      = 2;
        const uint MFVideoTransFunc_709        = 5;
        const uint MFVideoTransferMatrix_BT709 = 1;
        const uint MFNominalRange_Normal       = 1; // full/pc range (0-255) — matches Windows Camera's actual tagged output

        type.Set(MediaTypeAttributeKeys.VideoPrimaries,    MFVideoPrimaries_BT709);
        type.Set(MediaTypeAttributeKeys.TransferFunction,  MFVideoTransFunc_709);
        type.Set(MediaTypeAttributeKeys.YuvMatrix,         MFVideoTransferMatrix_BT709);
        type.Set(MediaTypeAttributeKeys.VideoNominalRange, MFNominalRange_Normal);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _encoding = false;

        IMFSinkWriter? writer;
        lock (_sinkWriterLock)
        {
            writer = _sinkWriter;
            _sinkWriter = null;
        }
        try { writer?.Finalize(); } catch { /* best-effort on abnormal teardown */ }
        writer?.Dispose();
        if (_mfRefHeld) { MediaFoundationRuntime.Release(); _mfRefHeld = false; }
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

/// <summary>Configuration for the encoder session.</summary>
public sealed class V2EncoderProfile
{
    public int Width  { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public double TargetFps { get; init; } = 30.0;
    public V2VideoCodec Codec { get; init; } = V2VideoCodec.H264;
    /// <summary>Request hardware encoder (NVENC / QuickSync). Falls back to software if unavailable.</summary>
    public bool PreferHardware { get; init; } = true;
    public int TargetBitrateKbps { get; init; } = 8_000;
}

/// <summary>Selects an encoder cadence from a stable preview measurement without large mode changes.</summary>
public static class EncoderCadencePolicy
{
    public static double ResolveTargetFps(
        double nominalFps, double measuredFps, long measuredFrames, TimeSpan measuredDuration)
    {
        if (nominalFps <= 0) nominalFps = 30.0;
        if (measuredDuration < TimeSpan.FromSeconds(2) || measuredFrames < 30 || measuredFps <= 0)
            return nominalFps;

        var relativeDifference = Math.Abs(measuredFps - nominalFps) / nominalFps;
        return relativeDifference <= 0.05
            ? Math.Round(measuredFps, 3, MidpointRounding.AwayFromZero)
            : nominalFps;
    }
}

/// <summary>Event arguments raised when the encoder session is finalised.</summary>
public sealed class V2EncoderFinalisedEventArgs : EventArgs
{
    public long FramesWritten { get; init; }
    public TimeSpan Duration  { get; init; }
}

/// <summary>Video codec options for <see cref="V2EncoderProfile"/>.</summary>
public enum V2VideoCodec
{
    H264,
    Hevc,
}
