# Output Files and Metadata

Applies to: MultiCamApp v1.0.87+

MultiCamApp writes one session folder per recording. Each active camera slot gets its own camera folder with an MP4, a privacy-safe `metadata.txt` summary, complete `metadata.json`, and a per-frame Timestamp CSV when scientific Original Capture Mode metadata is available.

Hardware Diagnostic reports are written under the app `logs\` folder, not inside recording session folders. They are advisory and privacy-safe. They do not change recording behavior, metadata schema, Video Verification thresholds, or session comparison.

## Session Folder Layout

Typical layout:

```text
SessionName_YYYYMMDD_HHMMSS\
  session_summary.txt
  cam1\
    MCAM_YYYYMMDD_HHMMSS_cam1.mp4
    cam1_frame_timestamps.csv
    metadata.txt
    metadata.json
  cam2\
    MCAM_YYYYMMDD_HHMMSS_cam2.mp4
    cam2_frame_timestamps.csv
    metadata.txt
    metadata.json
  cam3\
    ...
  cam4\
    ...
```

Notes:

- Only active selected camera slots are written.
- Camera folders are named by slot: `cam1`, `cam2`, `cam3`, `cam4`.
- Metadata files are written in English regardless of UI language.
- `metadata.txt` is a human-readable privacy-safe summary.
- `metadata.json` contains the complete structured technical data used by verification.
- Timestamp CSV files are the scientific timing source for Original Capture Mode.
- Empty MP4 files from failed recording startup should be removed during rollback.

## Video Files

Video files are MP4 files named with the `MCAM_` prefix, timestamp, and camera slot.

Example:

```text
MCAM_20260618_155545_cam1.mp4
```

MultiCamApp records original captured frames only. Exact 30.000 FPS cannot be guaranteed by all consumer cameras and depends on camera hardware, USB bandwidth, PC load, driver behavior, resolution, and exposure. Stable Real Capture FPS slightly below requested FPS, such as 29.684 FPS, can be acceptable when true timing is recorded in metadata.

In Original Capture Mode, the app does not insert duplicate or placeholder frames to force equal camera frame counts. Frame counts may differ because cameras delivered real frames at different measured FPS.

## Original Capture Mode

Original Capture Mode means:

- Only real camera frames are written.
- Duplicate frames are not inserted.
- Placeholder frames are not inserted.
- Frame counts may differ between cameras when native FPS differs.
- Stable lower native FPS is acceptable when recorded in metadata.
- Timestamp CSV files should be used for trimming and timing-sensitive analysis.
- MP4 container duration can differ from wall-clock duration when the writer FPS tag differs from measured native FPS.

Container duration differs from wall-clock time. Use timestamp CSV for scientific trimming and analysis.

Example: an OBSBOT camera measured at `30.000 FPS` and j5 cameras measured around `29.684 FPS` will produce different frame counts over the same recording time. This is not dropped frames when `FramesCaptured == FramesWritten`, duplicate frames are 0, placeholder frames are 0, and Writer drops are 0.

## Timestamp CSV

Each camera writes a Timestamp CSV beside its MP4:

```text
cam1_frame_timestamps.csv
cam2_frame_timestamps.csv
cam3_frame_timestamps.csv
cam4_frame_timestamps.csv
```

Important columns include:

- `frameIndex`
- `captureUtcTime`
- `captureLocalTime`
- `captureMonotonicSec`
- `writeMonotonicSec`
- `deltaFromPreviousCaptureMs`
- `expectedIntervalMs`
- `intervalErrorMs`
- `isOriginalFrame`
- `isDuplicateFrame`
- `isPlaceholderFrame`
- `writerQueueDepthAtEnqueue`
- `writerQueueDepthAtWrite`
- `sourceCameraName`
- `recordingTimingMode`

For Original Capture Mode, `isOriginalFrame` is true, `isDuplicateFrame` is false, and `isPlaceholderFrame` is false.

## Per-Camera Metadata

Each camera folder includes:

- `metadata.json` for structured machine-readable metadata.
- `metadata.txt` for a privacy-safe human-readable summary.

User-facing timing terms:

| User-facing term | Meaning |
| :--- | :--- |
| Recording mode | Original Capture Mode |
| Real Capture FPS | Real measured camera FPS from capture timestamps |
| Playback FPS | MP4 container/writer FPS tag |
| Timestamp CSV | Scientific timing source |
| Writer drops | Frames dropped by the writer queue |
| Frame integrity | Real frames only; no duplicates/placeholders |

Important JSON fields include:

- `AppVersion`
- `BuildNumber`
- `CameraSlot`
- `CameraName`
- `RequestedResolution`
- `ActualResolution`
- `RequestedFps`
- `RecordingTimingMode`
- `OriginalCaptureMode`
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
- `CaptureIntervalMinMs`
- `CaptureIntervalMaxMs`
- `CaptureIntervalStdMs`
- `ScientificTimingStatus`
- `ScientificTimingMessage`
- `FrameTimestampCsvPath`
- `FrameTimestampCsvWritten`
- `FrameTimestampCsvRowCount`

Original Capture pass expectations:

- `FramesWritten` is greater than 0.
- `FramesCaptured` and `FramesWritten` match unless there was a real writer error.
- Writer drops are 0.
- Duplicate and placeholder frames are 0.
- Timestamp CSV is present.
- Timestamp CSV row count matches frames written.
- Stable near-target FPS records `PASS_ORIGINAL_TIMING`.
- Stable measured FPS slightly below requested FPS records `PASS_ORIGINAL_TIMING_WITH_NOTE`.
- `ScientificTimingStatus` is not `FAIL`.

Legacy sessions recorded before Original Capture Mode may contain duplicate-frame correction metadata. Verification treats those as `LegacyConstantFrameCount` and audits them with legacy duplicate-frame logic.

## Session Summary

`session_summary.txt` is written at the session root.

It summarizes:

- Active camera slots.
- Session timing.
- Per-camera output paths.
- Inter-camera frame differences.
- Inter-camera start and stop offsets.
- Session-level timing status.

Video Verification uses these session-level details together with per-camera metadata, Timestamp CSV data, and `ffprobe` results.

## Output Validation

Use the in-app **Video Verification** page after recording:

1. Select the session folder that contains `cam1`, `cam2`, etc.
2. Click **Scan** to list videos.
3. Click **Verify** or **Verify All**.
4. Select a row to inspect metadata and timing details.

Command-line audit helper:

```powershell
.\scripts\diagnostics\run_session_validation_audit.ps1 -Folder "C:\Users\YOU\Videos\your_session_folder"
```

See [Video Verification](user_guide/video_verification.md) for result interpretation.

## Data Preservation

The installer and uninstaller are designed to preserve recorded videos, exported reports, and user project folders. Build cleanup scripts should not be run on user output folders.
