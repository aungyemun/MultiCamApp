using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

public sealed class TimingMonitor
{
    private readonly MonotonicClock _clock = new();

    public void StartSession() => _clock.Reset();
    public TimeSpan Elapsed => _clock.Elapsed;
    public string AccuracyLabel => MonotonicClock.PrecisionLabel;
}
