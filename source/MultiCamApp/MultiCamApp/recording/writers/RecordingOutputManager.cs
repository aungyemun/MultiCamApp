// VideoEngineV2 — output folder and path management.
// Does not modify the existing session folder structure.

namespace MultiCamApp.Recording.Writers;

/// <summary>Status of a managed recording session's output.</summary>
public enum RecordingOutputStatus
{
    NotStarted,
    InProgress,
    Finalized,
    Failed,
    Cancelled,
}

/// <summary>
/// Owns session folder creation, safe path resolution, and output manifest writing
/// for V2 recordings. Follows the existing MultiCamApp session folder convention:
/// <code>
/// outputRoot\sessionName_YYYYMMDD_HHmmss\
///   cam1\
///     cam1.tmp.mp4            ← written during recording
///     cam1.mp4                ← renamed on finalise
///     cam1_timestamps.csv
///     cam1_metadata.json
///     cam1_metadata.txt
/// session_summary.json
/// session_summary.txt
/// </code>
/// </summary>
public sealed class RecordingOutputManager
{
    private readonly string _outputRoot;

    public RecordingOutputManager(string outputRoot)
    {
        _outputRoot = outputRoot;
    }

    /// <summary>Session folder path once <see cref="CreateSessionFolderAsync"/> has been called.</summary>
    public string? SessionFolder { get; private set; }

    /// <summary>Current recording status.</summary>
    public RecordingOutputStatus Status { get; private set; } = RecordingOutputStatus.NotStarted;

    /// <summary>
    /// Creates the session folder and returns a <see cref="RecordingFileSet"/> for the given camera slot.
    /// The session folder is named using the provided session name sanitised to be filesystem-safe,
    /// suffixed with the UTC timestamp.
    /// </summary>
    public Task<RecordingFileSet> CreateSessionFolderAsync(
        string sessionName, string slotName = "cam1", CancellationToken ct = default)
    {
        var safeName    = SanitizeSessionName(sessionName);
        var timestamp   = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var folderName  = $"{safeName}_{timestamp}";
        SessionFolder   = Path.Combine(_outputRoot, folderName);

        var cameraFolder = Path.Combine(SessionFolder, slotName);
        Directory.CreateDirectory(cameraFolder);

        var fileSet = RecordingFileSet.Create(cameraFolder, slotName);
        Status = RecordingOutputStatus.InProgress;

        WriteStatusFile("recording_in_progress.txt",
            $"Session: {sessionName}\nStarted: {DateTimeOffset.UtcNow:O}\nCamera: {slotName}");

        return Task.FromResult(fileSet);
    }

    /// <summary>Marks the session as finalised and updates the status file.</summary>
    public void MarkFinalized(RecordingFinalizeResult result)
    {
        Status = result.IsSuccess ? RecordingOutputStatus.Finalized : RecordingOutputStatus.Failed;
        WriteStatusFile("recording_status.txt",
            $"Status: {Status}\n" +
            $"FinalVideo: {result.FinalVideoPath}\n" +
            $"FramesWritten: {result.FramesWritten}\n" +
            $"Duration: {result.Duration}\n" +
            $"HardwareEncoder: {result.HardwareEncoderUsed}\n" +
            $"Encoder: {result.EncoderDescription}\n" +
            (result.FailureReason is not null ? $"FailureReason: {result.FailureReason}\n" : ""));

        // Remove in-progress marker if present
        var inProgressFile = Path.Combine(SessionFolder ?? "", "recording_in_progress.txt");
        if (File.Exists(inProgressFile))
        {
            try { File.Delete(inProgressFile); } catch { /* best effort */ }
        }
    }

    /// <summary>Marks the session as failed and cleans up temp files.</summary>
    public void MarkFailed(string reason, RecordingFileSet? fileSet = null)
    {
        Status = RecordingOutputStatus.Failed;
        WriteStatusFile("recording_failed.txt", $"Reason: {reason}\nTimestamp: {DateTimeOffset.UtcNow:O}");
        if (fileSet is not null)
        {
            try { if (File.Exists(fileSet.TempVideoPath)) File.Delete(fileSet.TempVideoPath); } catch { }
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void WriteStatusFile(string fileName, string content)
    {
        if (SessionFolder is null) return;
        try { File.WriteAllText(Path.Combine(SessionFolder, fileName), content); }
        catch { /* best effort */ }
    }

    private static string SanitizeSessionName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean   = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "session" : clean[..Math.Min(clean.Length, 64)];
    }
}
