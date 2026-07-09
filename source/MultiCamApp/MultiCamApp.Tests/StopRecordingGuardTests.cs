// v1.2.18-alpha — unit tests for StopRecordingGuard re-entrancy protection.

using MultiCamApp.Utils;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class StopRecordingGuardTests
{
    // ── TryEnter / Release ────────────────────────────────────────────────────

    [Fact]
    public void TryEnter_WhenFree_ReturnsTrue()
    {
        var guard = new StopRecordingGuard();
        Assert.True(guard.TryEnter());
    }

    [Fact]
    public void TryEnter_WhileBusy_ReturnsFalse()
    {
        var guard = new StopRecordingGuard();
        guard.TryEnter(); // first caller acquires
        Assert.False(guard.TryEnter()); // second call is rejected
    }

    [Fact]
    public void IsInProgress_FalseByDefault()
    {
        var guard = new StopRecordingGuard();
        Assert.False(guard.IsInProgress);
    }

    [Fact]
    public void IsInProgress_TrueAfterTryEnter()
    {
        var guard = new StopRecordingGuard();
        guard.TryEnter();
        Assert.True(guard.IsInProgress);
    }

    [Fact]
    public void Release_AfterTryEnter_AllowsReEnter()
    {
        var guard = new StopRecordingGuard();
        guard.TryEnter();
        guard.Release();
        Assert.True(guard.TryEnter()); // guard is free again
    }

    [Fact]
    public void Release_SetsIsInProgress_False()
    {
        var guard = new StopRecordingGuard();
        guard.TryEnter();
        guard.Release();
        Assert.False(guard.IsInProgress);
    }

    // ── Simulated click-handler pattern ──────────────────────────────────────

    [Fact]
    public async Task SecondClick_WhileFirstInProgress_IsIgnored()
    {
        var guard = new StopRecordingGuard();
        int stopCallCount = 0;

        // Simulates the pattern in StopRecordBtn_Click.
        async Task SimulateClickAsync()
        {
            if (!guard.TryEnter()) return; // second click bails here
            try
            {
                Interlocked.Increment(ref stopCallCount);
                await Task.Delay(10); // simulates async finalization
            }
            finally
            {
                guard.Release();
            }
        }

        var first  = SimulateClickAsync();
        var second = SimulateClickAsync(); // fires while first is awaiting
        await Task.WhenAll(first, second);

        Assert.Equal(1, stopCallCount); // only first click ran the body
    }

    [Fact]
    public async Task GuardResets_AfterFirstClickCompletes_AllowsNewStop()
    {
        var guard = new StopRecordingGuard();
        int stopCallCount = 0;

        async Task SimulateClickAsync()
        {
            if (!guard.TryEnter()) return;
            try { Interlocked.Increment(ref stopCallCount); await Task.Delay(5); }
            finally { guard.Release(); }
        }

        await SimulateClickAsync(); // first stop
        await SimulateClickAsync(); // new stop after guard released

        Assert.Equal(2, stopCallCount);
    }

    [Fact]
    public async Task GuardResets_EvenWhenBodyThrows()
    {
        var guard = new StopRecordingGuard();

        async Task SimulateClickWithThrowAsync()
        {
            if (!guard.TryEnter()) return;
            try
            {
                await Task.Delay(1);
                throw new InvalidOperationException("finalize failed");
            }
            finally { guard.Release(); }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(SimulateClickWithThrowAsync);
        Assert.False(guard.IsInProgress); // guard reset despite exception
        Assert.True(guard.TryEnter());    // re-enter is now possible
    }
}




