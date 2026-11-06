using MultiCamApp.Recording;
using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>Sudden-close recovery: folder handling with a fake ffprobe (no cameras or ffmpeg needed).</summary>
public class InterruptedRecordingRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mca_recovery_" + Guid.NewGuid().ToString("N"));
    public InterruptedRecordingRecoveryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Session(string name, bool journal, params (string cam, bool tmp, int csvRows)[] cams)
    {
        var s = Path.Combine(_root, name);
        Directory.CreateDirectory(s);
        if (journal)
            InterruptedRecordingRecovery.WriteJournal(s, new InterruptedRecordingRecovery.Journal
            {
                AppVersion = "2.1.0", StartedUtc = "2026-11-06T05:00:00Z", RequestedResolution = "1280x720", RequestedFps = 30,
                Cameras = cams.Select(c => new InterruptedRecordingRecovery.JournalCamera { Slot = c.cam, Device = "Test camera" }).ToList(),
            });
        foreach (var (cam, tmp, rows) in cams)
        {
            var d = Path.Combine(s, cam); Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, tmp ? $"{cam}.tmp.mp4" : $"{cam}.mp4"), new byte[] { 1, 2, 3 });
            File.WriteAllLines(Path.Combine(d, $"{cam}_timestamps.csv"), new[] { "header" }.Concat(Enumerable.Range(0, rows).Select(i => i.ToString())));
        }
        return s;
    }

    private static Func<string, CancellationToken, Task<VideoProbeData>> Probe(bool playable, long frames = 450) =>
        (_, _) => Task.FromResult(playable
            ? new VideoProbeData { Success = true, HasVideoStream = true, FrameCount = frames, DurationSeconds = 15, Width = 1280, Height = 720 }
            : new VideoProbeData { Success = false, Error = "moov atom not found" });

    [Fact]
    public async Task PlayableTempFile_IsRenamedAndReported()
    {
        var s = Session("a_20261106_140000", journal: true, ("cam1", true, 452));
        var res = await InterruptedRecordingRecovery.RecoverAllAsync(_root, Probe(true), TimeSpan.FromDays(30));
        var cam = Assert.Single(Assert.Single(res).Cameras);
        Assert.Equal("Recovered", cam.Status);
        Assert.True(File.Exists(Path.Combine(s, "cam1", "cam1.mp4")));
        Assert.False(File.Exists(Path.Combine(s, "cam1", "cam1.tmp.mp4")));
        Assert.Equal(452, cam.CsvRows);
        Assert.True(File.Exists(Path.Combine(s, InterruptedRecordingRecovery.ReportTextFileName)));
        Assert.True(File.Exists(Path.Combine(s, InterruptedRecordingRecovery.InterruptedJournal)));
        Assert.False(File.Exists(Path.Combine(s, InterruptedRecordingRecovery.JournalFileName)));
        Assert.DoesNotContain(_root, File.ReadAllText(Path.Combine(s, InterruptedRecordingRecovery.ReportTextFileName)));
    }

    [Fact]
    public async Task ExistingFinalFile_IsNeverOverwritten()
    {
        var s = Session("b_20261106_140000", journal: true, ("cam1", true, 10));
        File.WriteAllBytes(Path.Combine(s, "cam1", "cam1.mp4"), new byte[] { 9 });
        var res = await InterruptedRecordingRecovery.RecoverAllAsync(_root, Probe(true), TimeSpan.FromDays(30));
        Assert.Equal("cam1_recovered.mp4", res.Single().Cameras.Single().RecoveredFile);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(s, "cam1", "cam1.mp4")));
    }

    [Fact]
    public async Task UnplayableTempFile_KeptAndReported_NotRescanned()
    {
        var s = Session("c_20261106_140000", journal: false, ("cam1", true, 480));
        var first = await InterruptedRecordingRecovery.RecoverAllAsync(_root, Probe(false), TimeSpan.FromDays(30));
        Assert.Equal("NotPlayable", first.Single().Cameras.Single().Status);
        Assert.True(File.Exists(Path.Combine(s, "cam1", "cam1.tmp.mp4")));
        var second = await InterruptedRecordingRecovery.RecoverAllAsync(_root, Probe(false), TimeSpan.FromDays(30));
        Assert.Empty(second);
    }

    [Fact]
    public async Task CleanSession_IsIgnored()
    {
        Session("d_20261106_140000", journal: false, ("cam1", false, 100));
        Assert.Empty(await InterruptedRecordingRecovery.RecoverAllAsync(_root, Probe(true), TimeSpan.FromDays(30)));
    }
}
