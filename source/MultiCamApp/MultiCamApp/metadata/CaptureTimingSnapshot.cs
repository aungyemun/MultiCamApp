////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Metadata;

/// <summary>Immutable per-session capture interval statistics, frozen before recorder cleanup.</summary>
public sealed record CaptureTimingSnapshot
{
    public const string UnavailableMessage = "Insufficient capture timestamps for interval statistics.";

    public long FramesCaptured { get; init; }
    public long CaptureIntervalCount { get; init; }
    public double MeanMs { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public double StdMs { get; init; }
    public long MaxConsecutiveLateFrames { get; init; }
    public long MaxConsecutiveNoFrame { get; init; }
    public string AvailabilityMessage { get; init; } = "";

    public bool IsAvailable => CaptureIntervalCount >= 1;

    public static CaptureTimingSnapshot Unavailable(long framesCaptured = 0) => new()
    {
        FramesCaptured = framesCaptured,
        AvailabilityMessage = UnavailableMessage
    };

    public static CaptureTimingSnapshot FromIntervals(
        long framesCaptured,
        IReadOnlyList<double> captureIntervalsMs,
        long maxConsecutiveLateFrames,
        long maxConsecutiveNoFrame)
    {
        if (captureIntervalsMs.Count < 1)
            return Unavailable(framesCaptured);

        var avg = captureIntervalsMs.Average();
        var min = captureIntervalsMs.Min();
        var max = captureIntervalsMs.Max();
        var variance = captureIntervalsMs.Sum(x => (x - avg) * (x - avg)) / captureIntervalsMs.Count;
        var std = Math.Sqrt(variance);

        return new CaptureTimingSnapshot
        {
            FramesCaptured = framesCaptured,
            CaptureIntervalCount = captureIntervalsMs.Count,
            MeanMs = avg,
            MinMs = min,
            MaxMs = max,
            StdMs = std,
            MaxConsecutiveLateFrames = maxConsecutiveLateFrames,
            MaxConsecutiveNoFrame = maxConsecutiveNoFrame
        };
    }
}
