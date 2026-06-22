////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Recording;

/// <summary>
/// Combines UTC wall clock (metadata/file naming) with monotonic elapsed (duration correction).
/// Same pattern as Windows Camera: filename from local time, MP4 PTS from encoder, app duration from QPC.
/// </summary>
public readonly struct RecordingTimingSnapshot
{
    public DateTime StartUtc { get; init; }
    public DateTime StartLocal { get; init; }
    public long StartMonotonicTicks { get; init; }
    public double StartMonotonicSeconds { get; init; }
    public DateTime StopUtc { get; init; }
    public DateTime StopLocal { get; init; }
    public long StopMonotonicTicks { get; init; }
    public double StopMonotonicSeconds { get; init; }
    public double WallClockDurationSeconds { get; init; }
    public double MonotonicDurationSeconds { get; init; }
    public TimeSpan MonotonicElapsed { get; init; }

    public DateTime StopUtcFromMonotonic => StopUtc == default ? StartUtc.Add(MonotonicElapsed) : StopUtc;
    public DateTime StopLocalFromMonotonic => StopLocal == default ? StartLocal.Add(MonotonicElapsed) : StopLocal;
}
