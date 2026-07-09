// VideoEngineV2 — recording finalization result.

namespace MultiCamApp.Recording.Writers;

/// <summary>Outcome of a recording finalisation attempt.</summary>
public enum RecordingFinalizeStatus
{
    /// <summary>MP4 file closed and renamed successfully.</summary>
    Success,
    /// <summary>File was written but timestamp CSV or metadata is incomplete.</summary>
    SuccessWithWarnings,
    /// <summary>Encoder failed to finalise — partial or unplayable file.</summary>
    EncoderFailed,
    /// <summary>File rename from temp to final path failed.</summary>
    RenameFailed,
    /// <summary>Recording was cancelled before any frames were written.</summary>
    Cancelled,
}

/// <summary>Returned by <see cref="IVideoFileWriter.FinalizeAsync"/> describing the outcome.</summary>
public sealed class RecordingFinalizeResult
{
    public RecordingFinalizeStatus Status { get; init; }
    public string FinalVideoPath   { get; init; } = "";
    public long FramesWritten      { get; init; }
    public long TimestampCsvRows   { get; init; }
    public bool HardwareEncoderUsed { get; init; }
    public string EncoderDescription { get; init; } = "";
    public string? FailureReason   { get; init; }
    public TimeSpan Duration       { get; init; }

    // ── Frame counter scope (v1.2.14-alpha) ──────────────────────────────────
    /// <summary>
    /// Frames submitted to the H.264 encoder during recording only (recording-relative).
    /// Reset at recording start; excludes preview frames delivered before record began.
    /// 0 when not available (older result objects predating this field).
    /// </summary>
    public long FramesSubmittedSinceRecordingStart { get; init; }

    /// <summary>
    /// Scope of the <see cref="FramesWritten"/> counter.
    /// "PreviewInclusive" = counter includes frames delivered since preview start (health monitor).
    /// "RecordingOnly"    = counter covers only frames written after recording started.
    /// "Unknown"          = scope not determined (legacy result objects).
    /// </summary>
    public string FrameCounterScope { get; init; } = "Unknown";

    /// <summary>
    /// Frames confirmed written by the timestamp CSV during recording (recording-relative).
    /// Sourced from <c>FrameTimestampMonitor.FrameCount</c>; equals <see cref="TimestampCsvRows"/>
    /// and agrees closely with the ffprobe frame count. 0 when not available.
    /// </summary>
    public long FramesWrittenDuringRecording { get; init; }

    /// <summary>
    /// Returns the best recording-relative frame count without confusing it with the
    /// preview-inclusive health counter. Timestamp rows are the final fallback when the
    /// encoder-side counter is unavailable.
    /// </summary>
    public long ResolveRecordingRelativeFrames(long timestampCsvRows = 0) =>
        FramesSubmittedSinceRecordingStart > 0
            ? FramesSubmittedSinceRecordingStart
            : timestampCsvRows > 0 ? timestampCsvRows : FramesWritten;

    // ── Post-finalization frame verification (v1.2.101) ──────────────────────
    /// <summary>
    /// IMFSinkWriter.WriteSample only throws when the native call returns a failure HRESULT — a
    /// return of S_OK only confirms the sample was accepted into the sink writer's internal queue,
    /// not that it was actually encoded and muxed into the final container. Media Foundation can
    /// silently drop a queued sample later in its own pipeline (observed: a session where the
    /// encoder's own counter said 13596 frames submitted, zero errors logged, but the finalized MP4
    /// only contained 13155 actual frames). This runs an independent ffprobe frame-count check on
    /// the just-renamed final file immediately after finalization, so that class of silent loss is
    /// caught and logged instead of staying invisible until someone runs ffprobe manually.
    /// Null when the check could not run (ffprobe unavailable, probe failed, etc.) — absence does
    /// NOT imply frames matched; it means the check was not performed.
    /// </summary>
    public long? PostFinalizeProbedFrameCount { get; init; }

    /// <summary>
    /// True when <see cref="PostFinalizeProbedFrameCount"/> is available and differs from the
    /// encoder's own <see cref="FramesSubmittedSinceRecordingStart"/> count — i.e. frames were
    /// silently dropped somewhere between WriteSample and the finalized container.
    /// </summary>
    public bool PostFinalizeFrameCountMismatch { get; init; }

    public bool IsSuccess =>
        Status is RecordingFinalizeStatus.Success or RecordingFinalizeStatus.SuccessWithWarnings;

    public static RecordingFinalizeResult Failure(string reason) =>
        new() { Status = RecordingFinalizeStatus.EncoderFailed, FailureReason = reason };
}
