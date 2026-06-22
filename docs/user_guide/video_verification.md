# Video Verification

MultiCamApp includes an offline **Video Verification** page that checks recorded session folders using bundled `ffprobe`, per-camera metadata, and Timestamp CSV files. No internet connection is required.

## When to use it

- After a recording session, before analysis or archival
- When moving sessions between machines (confirm files copied completely)
- When reviewing timing quality for scientific use

## How to run a check

1. Open **Video Verification** in the app.
2. Click **Browse** and select a session folder (the folder that contains `cam1`, `cam2`, etc.).
3. Click **Scan** to list videos, or **Verify** / **Verify All** to run checks.
4. Select a row in the results table to read the detail panel on the right.

Each camera folder should contain an MP4 file, a Timestamp CSV, `metadata.txt`, and `metadata.json`. A `session_summary.txt` at the session root provides session-level context.

Expected layout:

```text
SessionName_YYYYMMDD_HHMMSS\
  session_summary.txt
  cam1\
    MCAM_YYYYMMDD_HHMMSS_cam1.mp4
    cam1_frame_timestamps.csv
    metadata.txt
    metadata.json
  cam2\
    ...
```

## Original Capture Mode

Original Capture Mode preserves real camera frames only. MultiCamApp does not insert duplicate frames and does not insert placeholder frames to force a nominal FPS or equal frame counts between cameras.

Frame counts may differ because cameras delivered real frames at different measured FPS. This is expected and scientifically preferable to synthesizing frames. Stable lower Real Capture FPS is acceptable when metadata is complete, frames captured match frames written, duplicate frames are 0, placeholder frames are 0, Writer drops are 0, and Timestamp CSV rows match frames written.

Use timestamp CSV for timing-sensitive analysis. Timestamp CSV files record capture timing, and metadata records Real Capture FPS, capture interval statistics, wall-clock duration, frame-based duration, and container duration. Container duration differs from wall-clock time. Use timestamp CSV for scientific trimming and analysis.

Example: an OBSBOT camera may measure **30.000 FPS** while j5 cameras measure about **29.684 FPS**. Over the same wall-clock recording, this creates different frame counts. That is not dropped frames when each camera reports `framesCaptured == framesWritten` and Writer drops are 0; it means each camera preserved its real captured frames.

## Understanding Results

### PASS_ORIGINAL_TIMING

Original Capture Mode metadata is present and complete, the video is readable, frames captured match frames written, no duplicate or placeholder frames were inserted, no Writer drops occurred, Timestamp CSV rows match frames written, and Real Capture FPS is stable at the requested/native rate.

### PASS_ORIGINAL_TIMING_WITH_NOTE

Original Capture Mode preserved only real frames and timing is stable, but Real Capture FPS differs slightly from requested FPS or Playback FPS. This is acceptable for scientific use when metadata and Timestamp CSV files are present. Use timestamp CSV for timing-sensitive analysis.

### PASS_WITH_WARNING

The video is usually usable, but timing or metadata needs attention. Examples include capture timing jitter without Writer drops, non-critical metadata completeness gaps, or legacy sessions whose timing cannot be classified as a clean Original Capture pass. Use timestamp CSV for timing-sensitive analysis.

### FAIL

The recording should not be used for scientific analysis without re-recording or investigation. Failures include Writer drops, duplicate frames in Original Capture Mode, placeholder frames in Original Capture Mode, missing critical timing metadata, Timestamp CSV row mismatch, missing/corrupt video, or `framesCaptured != framesWritten`.

For scientific timing, prefer:

- **Timestamp CSV** as the primary scientific timing source.
- **Real Capture FPS** from metadata.
- **Monotonic wall-clock duration** from metadata.
- **Capture interval statistics** such as mean, min, max, standard deviation, P95, and P99.

Do not rely on MP4 Playback FPS or container duration alone when Timestamp CSV data explains the true recording timing.

## Verification Flow

Scan:

- Finds camera folders and videos.
- Reads available metadata.
- Lists each video in the table.

Verify:

- Runs bundled `ffprobe`.
- Reads `metadata.txt` and `metadata.json`.
- Compares Playback FPS, Real Capture FPS, frame count, wall-clock timing, Timestamp CSV rows, and metadata timing status.

Verify All:

- Verifies every listed video.
- Groups videos by session folder for inter-camera comparison.
- Reports frame-count difference, start offset, stop offset, and session-level sync status.

Export:

- Exports verification results for later review.
- Use exported files together with the original session folder when archiving validation evidence.

## Detail Panel Fields

| Field | Description |
| :--- | :--- |
| Real Capture FPS | FPS derived from capture timestamps during recording |
| Playback FPS | FPS tag reported by the MP4 container (ffprobe) |
| Timestamp CSV | Scientific timing source |
| Wall-clock duration | Elapsed recording time from session metadata |
| Container vs wall-clock | Difference between container-derived duration and wall-clock; informational when PASS_WITH_WARNING |
| Capture interval count / mean / min / max / std | Spacing between consecutive frame captures in milliseconds |
| Scientific timing message | Human-readable assessment written at end of recording |

If fewer than two capture timestamps exist, interval stats show **Unavailable** instead of silent zeros.

## Metadata Fields Used

Video Verification reads and reports JSON fields such as:

- `AppVersion`
- `BuildNumber`
- `RequestedResolution`
- `ActualResolution`
- `RequestedFps`
- `RecordingTimingMode`
- `MeasuredCameraFps`
- `WriterFps`
- `ContainerFps`
- `EffectiveRecordedFps`
- `FramesCaptured`
- `FramesWritten`
- `WriterQueueDrops`
- `DuplicateFrames` / `DuplicatedFrames`
- `PlaceholderFrames`
- `CaptureIntervalMeanMs`
- `ScientificTimingStatus`
- `ScientificTimingMessage`
- `FrameTimestampCsvWritten`
- `FrameTimestampCsvRowCount`

See [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md).

## User-Facing Terms

| Standard term | Meaning |
| :--- | :--- |
| Original Capture Mode | Recording mode that preserves real camera frames only |
| Real Capture FPS | Real measured camera FPS |
| Playback FPS | MP4/container playback frame rate |
| Timestamp CSV | Scientific timing source |
| Real frames only; no duplicates/placeholders | Frame integrity summary |
| Writer drops | Writer queue drops |

Standard warning text:

```text
Use timestamp CSV for timing-sensitive analysis.
Container duration differs from wall-clock time. Use timestamp CSV for scientific trimming and analysis.
One final frame occurred at the stop boundary and was not written. This is accepted and does not indicate frame loss during recording.
Frame counts may differ because cameras delivered real frames at different measured FPS.
```

## Release ZIP contents

Offline release packages (`installer.zip`) include `Setup.exe`, `README.md`, `INSTALLATION.md`, `DIRECTORY_STRUCTURE.md`, `LICENSE.md`, `THIRD_PARTY_NOTICES.md`, `SECURITY.md`, `CITATION.cff`, `CHANGELOG.md`, and key user-guide docs. Verification itself runs inside the installed application; no separate download is needed.

## Related documentation

- [Installation Guide](../../INSTALLATION.md)
- [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md)
- [Changelog](../changelogs/CHANGELOG.md)
