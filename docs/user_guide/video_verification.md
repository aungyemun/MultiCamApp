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

Each camera folder should contain an MP4 file, a Timestamp CSV, and metadata TXT/JSON files. A `session_metadata.json`/`session_metadata.txt` at the session root provides session-level context.

Expected layout:

```text
SessionName_YYYYMMDD_HHMMSS\
  session_metadata.json
  session_metadata.txt
  cam1\
    cam1.mp4
    cam1_timestamps.csv
    cam1_metadata.json
    cam1_metadata.txt
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

### A note on VideoEngineV2 recordings and false-positive FAIL

The `ffprobe`-based checks above were originally built around the legacy recorder's metadata format. VideoEngineV2 recordings (the default pipeline since v1.1.7) write a different, richer metadata schema.

Through v1.2.103, the underlying metadata parser didn't understand that newer schema at all — it silently returned all-zero/default values for a V2 file, which meant the verification pipeline's own PASS/WARNING/FAIL classification was wrong from the start for essentially every V2 recording. From v1.2.88 through v1.2.103, the Video Verification page worked around this with a growing stack of on-screen corrections: it re-checked whether a scanned folder was entirely VideoEngineV2 recordings and, if so, overwrote the session/camera/summary status with VideoEngineV2's own already-computed verdict (which does account correctly for Original Capture Mode, e.g. real per-camera frame-count differences from independently-measured FPS) after the fact.

**As of v1.2.104**, the metadata parser (`verification/MetadataParser.cs`) natively recognizes and reads the VideoEngineV2 schema, so the verification pipeline computes the correct PASS/PASS_WITH_WARNING/FAIL verdict the first time — no on-screen correction pass needed. The old correction logic is still present as an inert safety net (it only ever activates when it finds a stale FAIL, which no longer happens for a V2 recording), but a fresh Verify All on a V2 session no longer needs it to show the right result.

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
| Color tagging (v1.2.66+, VideoEngineV2 recordings) | BT.709 primaries/transfer/matrix, full-range (`0-255`/`pc`) as of v1.2.78, matching Windows Camera's own real output |

If fewer than two capture timestamps exist, interval stats show **Unavailable** instead of silent zeros.

## Metadata Fields Used

These are the internal field names Video Verification's audit logic reads (`CameraMetadataRecord`, in `verification/MetadataParser.cs`) — they apply to both metadata schemas. For a legacy-format `metadata.json`, they're read directly under these exact names. For a VideoEngineV2 `cam1_metadata.json` (nested, camelCase), `MetadataParser` maps the equivalent V2 field into each one (e.g. V2's `timing.framesWritten` becomes `FramesWritten` here) — see [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md#per-camera-metadata) for the V2 schema's own field names.

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

## Deep Verify (independent per-frame check)

`DuplicateFrames`/`PlaceholderFrames` in the sections above are **self-reported by the recorder** — written from the app's own internal frame counters, not independently checked against the actual video file. **Deep Verify** closes that gap: it independently decodes every frame of a video and hashes it (MD5), then reports how many of those hashes are exact repeats of an earlier frame. This is the same exact-hash method used internally to audit this app's own recordings (never a perceptual/similarity check, which produces false positives on real camera footage with static scenes).

- Click **Deep Verify** on the toolbar. It scans the selected folder (running an implicit Scan if you haven't already) and hashes every frame of every video it finds.
- This is **slow** — roughly real-time relative to each recording's own duration (a 12-minute recording takes on the order of a minute per camera to hash) — and **on-demand only**. It never runs automatically as part of Scan/Verify/Verify All, and does not block or change their results.
- Progress and per-file results appear in the log at the bottom; a "Deep Verify (Independent MD5 Check)" summary card group shows aggregate status, files checked, total duplicate frames found, and the worst-offending file.
- Requires the bundled `ffmpeg.exe` (not just `ffprobe.exe`). If it's missing, a warning appears explaining how to restore it (reinstall, or check `runtime/ffmpeg`).
- Cancel at any time with the same **Cancel** button used for Scan/Verify.

## Release ZIP contents

Two different bundles exist, and their contents differ:

- **`Setup.exe`** (built by Inno Setup from `installer/MultiCamApp.iss`) installs the app itself plus `THIRD_PARTY_NOTICES.md` and `LICENSE.txt`.
- **`installer.zip`** (built by `scripts/packaging/create_release_zip.ps1`) wraps `Setup.exe` together with `README.md`, `INSTALLATION.md`, `LICENSE.md`, `THIRD_PARTY_NOTICES.md`, `DIRECTORY_STRUCTURE.md`, `CITATION.cff`, `SECURITY.md`, `CHANGELOG.md`, and the `docs/user_guide/` pages (`video_verification.md`, `hardware_diagnostics.md`, `security_antivirus.md`) — download this one if you want the documentation alongside the installer.

Verification itself runs inside the installed application; no separate download is needed.

## Related documentation

- [Installation Guide](../../INSTALLATION.md)
- [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md)
- [Changelog](../../CHANGELOG.md)
