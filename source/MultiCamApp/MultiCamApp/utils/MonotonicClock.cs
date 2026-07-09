////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

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
