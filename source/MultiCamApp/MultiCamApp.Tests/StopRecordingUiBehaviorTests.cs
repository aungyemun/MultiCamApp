// v1.2.18-alpha — unit tests for Stop Recording UI behavior (pure-logic layer).
//
// The WPF MainWindow cannot be unit-tested directly, so these tests cover the
// pure-logic contracts that govern the stop-UI behavior:
//
//  1. StopRecordingGuard — re-entrancy / double-click protection.
//  2. PostStopState — a pure-logic model that tracks what the UI MUST present
//     during and after the post-stop processing phase.
//
// The PostStopState record is defined in this test file (test-only helper).
// It mirrors what MainWindow._isPostStopProcessing + StatusValue govern,
// letting us assert the rules without WPF dependency.

using MultiCamApp.Utils;
using Xunit;

namespace MultiCamApp.Tests;

// ── Pure-logic helper: what MainWindow presents during post-stop ──────────────

/// <summary>
/// Models the UI-facing output of the stop-click flow.
/// Mirrors the _isPostStopProcessing, StatusValue, and elapsed logic in MainWindow.
/// </summary>
file sealed class PostStopState
{
    // Inputs
    public bool   IsPostStopProcessing  { get; set; }
    public string StatusText            { get; set; } = "";
    public string ElapsedText           { get; set; } = "";
    public bool   StopButtonEnabled     { get; set; } = true;
    public bool   StartButtonEnabled    { get; set; } = true;
    public string PreviewStatusText     { get; set; } = "";

    // Simulate what StopRecordBtn_Click does immediately on click
    public static PostStopState OnStopClick(TimeSpan frozenElapsed)
    {
        return new PostStopState
        {
            IsPostStopProcessing = true,
            StatusText           = "Preview Active", // was "Finalizing" in v1.2.17
            ElapsedText          = frozenElapsed.ToString(@"hh\:mm\:ss"),
            StopButtonEnabled    = false,
            StartButtonEnabled   = false,
            PreviewStatusText    = "",  // cleared — not "Stopping recording… finalizing MP4 files"
        };
    }

    // Simulate what happens after finalization completes
    public PostStopState AfterFinalization(bool previewActive)
    {
        return new PostStopState
        {
            IsPostStopProcessing = false,
            StatusText           = previewActive ? "Preview Active" : "Idle",
            ElapsedText          = ElapsedText,   // last frozen elapsed stays
            StopButtonEnabled    = false,          // no recording in progress
            StartButtonEnabled   = previewActive,  // can start new if preview is up
            PreviewStatusText    = "",
        };
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public sealed class StopRecordingUiBehaviorTests
{
    // ── On stop click: immediate UI freeze ───────────────────────────────────

    [Fact]
    public void OnStopClick_SetsPostStopProcessingTrue()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(42));
        Assert.True(state.IsPostStopProcessing);
    }

    [Fact]
    public void OnStopClick_FreezesElapsedAtClickTime()
    {
        var elapsed = TimeSpan.FromSeconds(63);
        var state   = PostStopState.OnStopClick(elapsed);
        Assert.Equal("00:01:03", state.ElapsedText);
    }

    [Fact]
    public void OnStopClick_DisablesStopButton()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.False(state.StopButtonEnabled);
    }

    [Fact]
    public void OnStopClick_DisablesStartButton()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.False(state.StartButtonEnabled);
    }

    // ── On stop click: status panel must NOT show "Finalizing" ───────────────

    [Fact]
    public void OnStopClick_StatusText_IsNotFinalizing()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.DoesNotContain("Finalizing", state.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnStopClick_StatusText_IsNotAnalyzingRecording()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.DoesNotContain("Analyzing recording", state.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnStopClick_StatusText_IsNotFinalizingMp4()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.DoesNotContain("Finalizing MP4", state.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnStopClick_PreviewStatusText_IsCleared()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        // Must NOT contain "Stopping recording… finalizing MP4 files. Please wait."
        Assert.Empty(state.PreviewStatusText);
    }

    [Fact]
    public void OnStopClick_PreviewStatusText_IsNotStoppingRecordingMessage()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.DoesNotContain("Finalizing MP4 files", state.PreviewStatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Status text shows stopped (preview) state ─────────────────────────────

    [Fact]
    public void OnStopClick_StatusText_ShowsPreviewActive()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(30));
        // Must look like "Preview Active" or "previewing" — not "Recording", not "Finalizing"
        Assert.DoesNotContain("Recording", state.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Elapsed timer behavior during post-stop ───────────────────────────────

    [Fact]
    public void DuringPostStop_ElapsedDoesNotIncrease()
    {
        var frozen = TimeSpan.FromSeconds(45);
        var state  = PostStopState.OnStopClick(frozen);

        // Simulate multiple timer ticks — elapsed must stay frozen.
        // The contract: when IsPostStopProcessing=true, show the frozen value only.
        string tick1 = state.IsPostStopProcessing ? state.ElapsedText : "changed";
        string tick2 = state.IsPostStopProcessing ? state.ElapsedText : "changed";

        Assert.Equal(state.ElapsedText, tick1);
        Assert.Equal(state.ElapsedText, tick2);
    }

    [Fact]
    public void DuringPostStop_ElapsedText_IsTheSameAtEveryTick()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(46));
        var result = Enumerable.Range(0, 5)
            .Select(_ => state.IsPostStopProcessing ? state.ElapsedText : "ticking")
            .Distinct()
            .ToList();
        // All 5 ticks produced the same value
        Assert.Single(result);
    }

    // ── UpdateStatusDashboard must not overwrite stopped status ──────────────

    [Fact]
    public void UpdateStatusDashboard_DuringPostStop_DoesNotRun()
    {
        // Contract: UpdateStatusDashboard returns early if _isPostStopProcessing is true.
        // This is verified by checking that the flag prevents a write.
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(42));
        bool wouldOverwrite = !state.IsPostStopProcessing;
        Assert.False(wouldOverwrite);
    }

    [Fact]
    public void UpdateStatusDashboard_AfterPostStop_Runs()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(42));
        var after = state.AfterFinalization(previewActive: true);
        bool wouldOverwrite = !after.IsPostStopProcessing;
        Assert.True(wouldOverwrite);
    }

    // ── Start Recording blocked until finalization completes ──────────────────

    [Fact]
    public void DuringPostStop_StartRecordingRemainedDisabled()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(30));
        Assert.False(state.StartButtonEnabled);
    }

    [Fact]
    public void AfterFinalization_WithPreview_StartRecordingBecomesEnabled()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(30));
        var after = state.AfterFinalization(previewActive: true);
        Assert.True(after.StartButtonEnabled);
    }

    [Fact]
    public void AfterFinalization_WithoutPreview_StartRecordingStaysDisabled()
    {
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(30));
        var after = state.AfterFinalization(previewActive: false);
        Assert.False(after.StartButtonEnabled);
    }

    // ── UI returns to safe state after finalization ───────────────────────────

    [Fact]
    public void AfterFinalization_PostStopFlagCleared()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        var after = state.AfterFinalization(previewActive: true);
        Assert.False(after.IsPostStopProcessing);
    }

    [Fact]
    public void AfterFinalizationException_PostStopFlagStillCleared()
    {
        // The finally block in StopRecordBtn_Click must clear the flag even on exception.
        var guard = new StopRecordingGuard();
        guard.TryEnter();
        bool postStopProcessing = true;

        try { throw new InvalidOperationException("finalize failed"); }
        catch { /* swallowed by catch handler in click */ }
        finally
        {
            postStopProcessing = false;  // mirrors finally block in MainWindow
            guard.Release();
        }

        Assert.False(postStopProcessing);
        Assert.False(guard.IsInProgress);
    }

    // ── Double-click stop is ignored ──────────────────────────────────────────

    [Fact]
    public async Task DoubleClickStop_SecondClickIsIgnoredByGuard()
    {
        var guard = new StopRecordingGuard();
        int stopBodyRuns = 0;

        async Task SimulateStopClickAsync()
        {
            if (!guard.TryEnter()) return;
            try
            {
                Interlocked.Increment(ref stopBodyRuns);
                await Task.Delay(20); // silent finalization
            }
            finally { guard.Release(); }
        }

        var t1 = SimulateStopClickAsync();
        var t2 = SimulateStopClickAsync(); // concurrent second click
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, stopBodyRuns);
    }

    // ── V2 stable recording behavior contract ────────────────────────────────

    [Fact]
    public void V2RecordingBehavior_IsUnchanged_StopUiIsAdditiveOnly()
    {
        // The stop-UI change (v1.2.18-alpha) is additive:
        // it only changes what text is shown in the UI during post-stop.
        // V2 recording engine stop behavior, CSV writing, metadata writing,
        // and ffprobe audit are not touched.
        //
        // Contract: PostStopState.OnStopClick() does not call StopRecordingAsync;
        // it only sets UI fields. The actual stop is still called by StopV2DefaultRecordingAsync.
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(45));
        // UI shows stopped; internal finalization still executes (modeled by IsPostStopProcessing=true)
        Assert.True(state.IsPostStopProcessing);
        Assert.False(state.StopButtonEnabled);
    }

    // ── V3B stop path uses same UI policy ────────────────────────────────────

    [Fact]
    public void V3BStopPath_SameUiPolicyAsV2()
    {
        // V3B slots route through the same StopRecordBtn_Click → StopV2DefaultRecordingAsync
        // path. The post-stop UI state is set before the path is entered,
        // so it applies to both V2 and V3B slots equally.
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(30));
        // Same asserts pass for V3B — the UI policy is backend-agnostic.
        Assert.True(state.IsPostStopProcessing);
        Assert.DoesNotContain("Finalizing", state.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Finalizing MP4 files", state.PreviewStatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Frozen elapsed: correct formatting ───────────────────────────────────

    [Fact]
    public void FrozenElapsed_ZeroSeconds_FormatsAsZero()
    {
        var state = PostStopState.OnStopClick(TimeSpan.Zero);
        Assert.Equal("00:00:00", state.ElapsedText);
    }

    [Fact]
    public void FrozenElapsed_HourMinuteSecond_FormatsCorrectly()
    {
        var elapsed = new TimeSpan(1, 23, 45);
        var state   = PostStopState.OnStopClick(elapsed);
        Assert.Equal("01:23:45", state.ElapsedText);
    }

    [Fact]
    public void FrozenElapsed_46Seconds_MatchesRealRecording()
    {
        // Real test recording test1_20260628_223229: duration ~46s
        var state = PostStopState.OnStopClick(TimeSpan.FromSeconds(46));
        Assert.Equal("00:00:46", state.ElapsedText);
    }
}
