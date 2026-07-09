// v1.2.14-alpha — re-entrancy guard for the stop-recording UI flow.

namespace MultiCamApp.Utils;

/// <summary>
/// Thread-safe re-entrancy guard that prevents a second UI click from starting a concurrent
/// stop-recording operation while the first is still awaiting finalization.
/// </summary>
/// <remarks>
/// Pattern: call <see cref="TryEnter"/> at the start of the click handler.
/// If it returns false, return immediately (second click is a no-op).
/// Always call <see cref="Release"/> in a finally block so the guard resets even on error.
/// </remarks>
public sealed class StopRecordingGuard
{
    private int _inProgress; // 0 = free, 1 = busy

    /// <summary>
    /// Tries to acquire the guard. Returns true if this caller should proceed;
    /// returns false if a stop is already in progress (caller should no-op).
    /// </summary>
    public bool TryEnter() => Interlocked.CompareExchange(ref _inProgress, 1, 0) == 0;

    /// <summary>Releases the guard after the stop operation completes (or throws).</summary>
    public void Release() => Interlocked.Exchange(ref _inProgress, 0);

    /// <summary>True while a stop is currently in progress.</summary>
    public bool IsInProgress => Volatile.Read(ref _inProgress) == 1;
}
