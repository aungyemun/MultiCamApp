// VideoEngineV2 — recording output file set.
// Part of the V2 recording path; does not affect the legacy OpenCV pipeline.

namespace MultiCamApp.Recording.Writers;

/// <summary>
/// Describes all output files for one camera's V2 recording session.
/// Follows the existing MultiCamApp output structure:
/// <code>
/// sessionName_YYYYMMDD_HHMMSS\
///   cam1\
///     cam1.mp4              ← final video (renamed from temp after finalisation)
///     cam1_metadata.json
///     cam1_metadata.txt
///     cam1_timestamps.csv
/// </code>
/// </summary>
public sealed class RecordingFileSet
{
    /// <summary>Absolute path to the camera slot folder (<c>…\cam1\</c>).</summary>
    public string CameraFolder { get; init; } = "";

    /// <summary>Temporary video file written during recording.</summary>
    public string TempVideoPath { get; init; } = "";

    /// <summary>Final video path — exists only after successful finalisation.</summary>
    public string FinalVideoPath { get; init; } = "";

    /// <summary>Per-frame timestamp CSV path.</summary>
    public string TimestampCsvPath { get; init; } = "";

    /// <summary>JSON metadata path.</summary>
    public string MetadataJsonPath { get; init; } = "";

    /// <summary>Human-readable TXT metadata path.</summary>
    public string MetadataTxtPath { get; init; } = "";

    /// <summary>Canonical JSON filename used by session discovery and offline audit tools.</summary>
    public string CanonicalMetadataJsonPath => Path.Combine(CameraFolder, "metadata.json");

    /// <summary>Canonical text filename used by session discovery and offline audit tools.</summary>
    public string CanonicalMetadataTxtPath => Path.Combine(CameraFolder, "metadata.txt");

    /// <summary>True after the temp video has been renamed to the final path.</summary>
    public bool IsFinalized { get; private set; }

    /// <summary>Marks the file set as finalized (temp renamed to final). Call once after successful rename.</summary>
    public void MarkFinalized() => IsFinalized = true;

    /// <summary>Creates a <see cref="RecordingFileSet"/> from a camera folder path and slot name.</summary>
    public static RecordingFileSet Create(string cameraFolder, string slotName)
    {
        return new RecordingFileSet
        {
            CameraFolder      = cameraFolder,
            TempVideoPath     = Path.Combine(cameraFolder, $"{slotName}.tmp.mp4"),
            FinalVideoPath    = Path.Combine(cameraFolder, $"{slotName}.mp4"),
            TimestampCsvPath  = Path.Combine(cameraFolder, $"{slotName}_timestamps.csv"),
            MetadataJsonPath  = Path.Combine(cameraFolder, $"{slotName}_metadata.json"),
            MetadataTxtPath   = Path.Combine(cameraFolder, $"{slotName}_metadata.txt"),
        };
    }
}
