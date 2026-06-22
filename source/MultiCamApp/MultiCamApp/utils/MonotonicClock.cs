////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Diagnostics;

namespace MultiCamApp.Utils;

/// <summary>High-precision monotonic timing (not UI timers).</summary>
public sealed class MonotonicClock
{
    private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;
    private long _originTicks = Stopwatch.GetTimestamp();

    public void Reset() => _originTicks = Stopwatch.GetTimestamp();

    public double ElapsedMilliseconds =>
        (Stopwatch.GetTimestamp() - _originTicks) * TickToMs;

    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

    public static long NowTicks() => Stopwatch.GetTimestamp();

    public static string PrecisionLabel => "Stopwatch (QueryPerformanceCounter)";
}
