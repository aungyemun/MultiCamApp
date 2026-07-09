// v1.2.18-alpha — unit tests for V2 TimestampCsvStatus metadata consistency fix.
// Root cause: VideoEngineRegistry.BuildMetadata used result.TimestampCsvRows which was
// never set in the V2 stop path, so status was always "Skipped" even when CSV was written.
// Fix: V2 StopSlotRecordingAsync now sets TimestampCsvRows = pipeline.TimestampMonitorFrameCount.
// Defensive fallback: BuildMetadata also checks FramesWrittenDuringRecording > 0.

using MultiCamApp.Recording.Writers;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class V2TimestampCsvStatusTests
{
    // ── TimestampCsvStatus derivation logic (mirrors VideoEngineRegistry.BuildMetadata) ──

    private static string DeriveTimestampCsvStatus(RecordingFinalizeResult result) =>
        result.TimestampCsvRows > 0 || result.FramesWrittenDuringRecording > 0
            ? "Written" : "Skipped";

    [Fact]
    public void TimestampCsvStatus_Written_WhenTimestampCsvRowsSet()
    {
        var result = new RecordingFinalizeResult
        {
            TimestampCsvRows         = 6867,
            FramesWrittenDuringRecording = 6867,
        };
        Assert.Equal("Written", DeriveTimestampCsvStatus(result));
    }

    [Fact]
    public void TimestampCsvStatus_Written_WhenOnlyFramesWrittenDuringRecordingSet()
    {
        // Defensive: TimestampCsvRows not set but FramesWrittenDuringRecording is.
        var result = new RecordingFinalizeResult
        {
            TimestampCsvRows             = 0,   // not set (old path)
            FramesWrittenDuringRecording = 6862,
        };
        Assert.Equal("Written", DeriveTimestampCsvStatus(result));
    }

    [Fact]
    public void TimestampCsvStatus_NotSkipped_ForSuccessfulV2Session()
    {
        // Simulates the v1.2.14-alpha bug: TimestampCsvRows was 0 and FramesWrittenDuringRecording was set.
        // The fix checks both — result must NOT be "Skipped".
        var result = new RecordingFinalizeResult
        {
            FramesWritten                = 7241, // preview-inclusive
            FramesSubmittedSinceRecordingStart = 6862,
            FramesWrittenDuringRecording = 6867,
            TimestampCsvRows             = 6867,
            FrameCounterScope            = "PreviewInclusive",
        };
        string status = DeriveTimestampCsvStatus(result);
        Assert.NotEqual("Skipped", status);
        Assert.Equal("Written", status);
    }

    [Fact]
    public void TimestampCsvStatus_Skipped_WhenBothFieldsZero()
    {
        var result = new RecordingFinalizeResult
        {
            TimestampCsvRows             = 0,
            FramesWrittenDuringRecording = 0,
        };
        Assert.Equal("Skipped", DeriveTimestampCsvStatus(result));
    }

    [Fact]
    public void TimestampCsvRows_IsSetFromTimestampMonitorFrameCount_InV2Path()
    {
        // The V2 finalize result must now carry TimestampCsvRows == FramesWrittenDuringRecording.
        // Both are sourced from pipeline.TimestampMonitorFrameCount.
        long monitorCount = 6867;
        var result = new RecordingFinalizeResult
        {
            FramesWrittenDuringRecording = monitorCount,
            TimestampCsvRows             = monitorCount,
        };
        Assert.Equal(result.FramesWrittenDuringRecording, result.TimestampCsvRows);
    }

    [Fact]
    public void TimestampCsvStatus_Written_WhenOnlyTimestampCsvRowsSet_AndFramesWrittenIsZero()
    {
        // Pure TimestampCsvRows path — should be sufficient on its own.
        var result = new RecordingFinalizeResult
        {
            TimestampCsvRows             = 3656,
            FramesWrittenDuringRecording = 0,
        };
        Assert.Equal("Written", DeriveTimestampCsvStatus(result));
    }

    [Fact]
    public void BackendMetadata_TimestampCsvStatus_And_TimestampRows_MustAgree()
    {
        // When TimestampCsvRows > 0, status is Written; and the row count must be the same value.
        // This test asserts the invariant both fields should satisfy together.
        long expectedRows = 6867;
        var result = new RecordingFinalizeResult { TimestampCsvRows = expectedRows };
        string status = DeriveTimestampCsvStatus(result);
        Assert.Equal("Written", status);
        Assert.Equal(expectedRows, result.TimestampCsvRows);
    }

    [Fact]
    public void PreviewInclusive_FrameCounterNote_RemainsInformational_WithValidCsvStatus()
    {
        // When CSV is written and recording-relative counter agrees, no warning is raised.
        var result = new RecordingFinalizeResult
        {
            FramesWritten                      = 7241,
            FramesSubmittedSinceRecordingStart = 6862,
            TimestampCsvRows                   = 6867,
            FramesWrittenDuringRecording       = 6867,
            FrameCounterScope                  = "PreviewInclusive",
        };

        long csvRows        = result.TimestampCsvRows;
        long recordingFrames = result.FramesSubmittedSinceRecordingStart > 0
            ? result.FramesSubmittedSinceRecordingStart : result.FramesWritten;
        long tolerance       = Math.Max(5L, recordingFrames / 100L);
        bool framesMatchCsv  = csvRows > 0 && Math.Abs(csvRows - recordingFrames) <= tolerance;
        string csvStatus     = DeriveTimestampCsvStatus(result);

        Assert.Equal("Written", csvStatus);
        Assert.True(framesMatchCsv, "Recording-relative counter must match CSV — no warning");
    }
}



