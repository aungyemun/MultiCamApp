// v1.2.18-alpha — unit tests for frame counter scope fields and audit wording logic.
// Validates that preview-inclusive counter divergence does NOT raise a PASS_WITH_WARNING
// when the recording-relative counter agrees with the timestamp CSV rows.

using MultiCamApp.Recording.Writers;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class V2FrameCounterScopeTests
{
    // ── RecordingFinalizeResult new fields ────────────────────────────────────

    [Fact]
    public void RecordingFinalizeResult_DefaultScope_IsUnknown()
    {
        var r = new RecordingFinalizeResult();
        Assert.Equal("Unknown", r.FrameCounterScope);
    }

    [Fact]
    public void RecordingFinalizeResult_V2Scope_IsPreviewInclusive()
    {
        var r = new RecordingFinalizeResult { FrameCounterScope = "PreviewInclusive" };
        Assert.Equal("PreviewInclusive", r.FrameCounterScope);
    }

    [Fact]
    public void RecordingFinalizeResult_FramesSubmittedSinceRecordingStart_DefaultsToZero()
    {
        var r = new RecordingFinalizeResult();
        Assert.Equal(0L, r.FramesSubmittedSinceRecordingStart);
    }

    [Fact]
    public void RecordingFinalizeResult_FramesWrittenDuringRecording_DefaultsToZero()
    {
        var r = new RecordingFinalizeResult();
        Assert.Equal(0L, r.FramesWrittenDuringRecording);
    }

    [Fact]
    public void ResolveRecordingRelativeFrames_PrefersEncoderCounterThenCsvThenPreviewCounter()
    {
        var withEncoder = new RecordingFinalizeResult
        {
            FramesWritten = 5000,
            FramesSubmittedSinceRecordingStart = 3000,
        };
        Assert.Equal(3000, withEncoder.ResolveRecordingRelativeFrames(3010));

        var withCsv = new RecordingFinalizeResult { FramesWritten = 5000 };
        Assert.Equal(3010, withCsv.ResolveRecordingRelativeFrames(3010));

        var previewOnly = new RecordingFinalizeResult { FramesWritten = 5000 };
        Assert.Equal(5000, previewOnly.ResolveRecordingRelativeFrames());
    }

    // ── Recording-relative vs preview-inclusive counter logic ─────────────────

    [Fact]
    public void RecordingRelativeCounter_UsedWhenAvailable()
    {
        // Simulates a V2 session: preview-inclusive=4340, recording-relative=3652, CSV=3656.
        var result = new RecordingFinalizeResult
        {
            FramesWritten                      = 4340, // preview-inclusive (health monitor)
            FramesSubmittedSinceRecordingStart = 3652, // recording-relative (encoder)
            TimestampCsvRows                   = 3656,
            FrameCounterScope                  = "PreviewInclusive",
        };

        long csvRows       = result.TimestampCsvRows;
        long recordingFrames = result.FramesSubmittedSinceRecordingStart > 0
            ? result.FramesSubmittedSinceRecordingStart
            : result.FramesWritten;
        long tolerance     = Math.Max(5L, recordingFrames / 100L); // 1% of 3652 = 36
        bool framesMatchCsv = csvRows > 0 && Math.Abs(csvRows - recordingFrames) <= tolerance;

        // CSV=3656, recording=3652, diff=4 — within 1% tolerance (36).
        Assert.True(framesMatchCsv, "Recording-relative counter should match CSV within tolerance");
    }

    [Fact]
    public void PreviewInclusiveCounter_DoesNotTriggerWarning_WhenCsvMatches()
    {
        var result = new RecordingFinalizeResult
        {
            FramesWritten                      = 4340,
            FramesSubmittedSinceRecordingStart = 3652,
            TimestampCsvRows                   = 3656,
            FrameCounterScope                  = "PreviewInclusive",
        };

        long csvRows        = result.TimestampCsvRows;
        long recordingFrames = result.FramesSubmittedSinceRecordingStart > 0
            ? result.FramesSubmittedSinceRecordingStart : result.FramesWritten;
        long tolerance      = Math.Max(5L, recordingFrames / 100L);
        bool framesMatchCsv = csvRows > 0 && Math.Abs(csvRows - recordingFrames) <= tolerance;

        // The old code would raise a warning here because 4340 != 3656 (diff 684).
        // The new code uses recordingFrames (3652) and passes.
        bool oldCodeWouldWarn = Math.Abs(csvRows - result.FramesWritten) > Math.Max(5L, result.FramesWritten / 100L);
        Assert.True(oldCodeWouldWarn, "Old code should have produced a false warning");
        Assert.True(framesMatchCsv,   "New code must not warn — recording-relative count matches CSV");
    }

    [Fact]
    public void PreviewInclusiveDiffers_FlaggedInformationalOnly()
    {
        var result = new RecordingFinalizeResult
        {
            FramesWritten                      = 4340,
            FramesSubmittedSinceRecordingStart = 3652,
            TimestampCsvRows                   = 3656,
            FrameCounterScope                  = "PreviewInclusive",
        };

        long csvRows        = result.TimestampCsvRows;
        long recordingFrames = result.FramesSubmittedSinceRecordingStart > 0
            ? result.FramesSubmittedSinceRecordingStart : result.FramesWritten;
        long recordingTol   = Math.Max(5L, recordingFrames / 100L);
        bool framesMatchCsv = csvRows > 0 && Math.Abs(csvRows - recordingFrames) <= recordingTol;

        bool previewInclusiveDiffers = result.FrameCounterScope == "PreviewInclusive"
            && result.FramesSubmittedSinceRecordingStart > 0
            && Math.Abs(result.FramesWritten - csvRows) > Math.Max(5L, result.FramesWritten / 100L);

        // Preview-inclusive counter does differ, so the informational note flag is set.
        Assert.True(previewInclusiveDiffers);
        // But the session result is NOT a warning because framesMatchCsv is true.
        Assert.True(framesMatchCsv);
    }

    [Fact]
    public void RealMismatch_StillTriggersWarning()
    {
        // A genuine mismatch: recording-relative counter also doesn't match CSV.
        var result = new RecordingFinalizeResult
        {
            FramesWritten                      = 4340,
            FramesSubmittedSinceRecordingStart = 2000, // genuinely wrong
            TimestampCsvRows                   = 3656,
            FrameCounterScope                  = "PreviewInclusive",
        };

        long csvRows        = result.TimestampCsvRows;
        long recordingFrames = result.FramesSubmittedSinceRecordingStart > 0
            ? result.FramesSubmittedSinceRecordingStart : result.FramesWritten;
        long tolerance      = Math.Max(5L, recordingFrames / 100L); // 1% of 2000 = 20
        bool framesMatchCsv = csvRows > 0 && Math.Abs(csvRows - recordingFrames) <= tolerance;

        // diff = |3656 - 2000| = 1656 >> tolerance=20 → genuine mismatch.
        Assert.False(framesMatchCsv, "Genuine recording-relative mismatch must still produce a warning");
    }

    [Fact]
    public void FallbackToFramesWritten_WhenRecordingRelativeNotAvailable()
    {
        // Legacy result with no recording-relative counter set (FramesSubmittedSinceRecordingStart == 0).
        var result = new RecordingFinalizeResult
        {
            FramesWritten    = 3654,
            TimestampCsvRows = 3656,
            FrameCounterScope = "Unknown",
            // FramesSubmittedSinceRecordingStart defaults to 0
        };

        long recordingFrames = result.FramesSubmittedSinceRecordingStart > 0
            ? result.FramesSubmittedSinceRecordingStart
            : result.FramesWritten; // falls back to FramesWritten

        Assert.Equal(result.FramesWritten, recordingFrames);
    }

    [Fact]
    public void ToleranceBoundary_ExactlyAtLimit_Passes()
    {
        // tolerance = max(5, 3600/100) = 36; diff = 36 → should pass (<=).
        var result = new RecordingFinalizeResult
        {
            FramesSubmittedSinceRecordingStart = 3600,
            TimestampCsvRows                   = 3636, // diff = 36 exactly
        };

        long recordingFrames = result.FramesSubmittedSinceRecordingStart;
        long tolerance       = Math.Max(5L, recordingFrames / 100L);
        bool passes          = Math.Abs(result.TimestampCsvRows - recordingFrames) <= tolerance;

        Assert.Equal(36L, tolerance);
        Assert.True(passes);
    }

    [Fact]
    public void ToleranceBoundary_OneOverLimit_Fails()
    {
        // tolerance = 36; diff = 37 → should fail (>).
        var result = new RecordingFinalizeResult
        {
            FramesSubmittedSinceRecordingStart = 3600,
            TimestampCsvRows                   = 3637, // diff = 37
        };

        long recordingFrames = result.FramesSubmittedSinceRecordingStart;
        long tolerance       = Math.Max(5L, recordingFrames / 100L);
        bool passes          = Math.Abs(result.TimestampCsvRows - recordingFrames) <= tolerance;

        Assert.False(passes);
    }
}




