# Output Files and Metadata

Applies to: MultiCamApp v1.0.87+ (VideoEngineV2 file naming reflects v1.2.22-alpha+)

MultiCamApp writes one session folder per recording. Each active camera slot gets its own camera folder with an MP4, a privacy-safe metadata TXT summary, complete metadata JSON, and a per-frame Timestamp CSV when scientific Original Capture Mode metadata is available.

As of v1.2.22-alpha, VideoEngineV2 is the default recording engine and writes one metadata file pair per camera: `cam1_metadata.json`/`cam1_metadata.txt` (slot-prefixed). Through v1.2.104, it also wrote a byte-identical unprefixed duplicate (`metadata.json`/`metadata.txt`) solely because `VideoScanner` only recognized that exact unprefixed name. As of **v1.2.105**, `VideoScanner`/`SessionComparisonService` look for the slot-prefixed name first (falling back to the unprefixed name for older recordings or the legacy OpenCV engine), so the duplicate write was removed — new recordings only have the one prefixed pair per camera. Sessions recorded before v1.2.105 still have both copies on disk; nothing needs to be deleted or migrated.

Hardware Diagnostic reports are written under the app `logs\` folder, not inside recording session folders. They are advisory and privacy-safe. They do not change recording behavior, metadata schema, Video Verification thresholds, or session comparison.

## Session Folder Layout

Typical layout:

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
    cam2.mp4
    cam2_timestamps.csv
    cam2_metadata.json
    cam2_metadata.txt
  cam3\
    ...
  cam4\
    ...
```

Sessions recorded before v1.2.105 additionally have an unprefixed `metadata.json`/`metadata.txt` duplicate in each camera folder — harmless leftovers, not required by anything, safe to ignore or delete.

Notes:

- Only active selected camera slots are written.
- Camera folders are named by slot: `cam1`, `cam2`, `cam3`, `cam4`.
- **VideoEngineV2 metadata language** (`cam1_metadata.txt`, and human-readable status text embedded in `cam1_metadata.json`): follows the UI language selected at the moment recording starts (`_sessionLanguage`, locked for the duration of the recording — the language selector is disabled while recording). If the UI was set to Japanese when you clicked Start Recording, the `.txt` summary and embedded status strings are written in Japanese. JSON **field names** (the schema keys, e.g. `framesWritten`, `recordingEngine`) always stay in English regardless of language — only human-readable values are localized. The JSON also records which language was used in its `metadataLanguage` field.
- **Legacy pipeline metadata** (`metadata.txt`/`metadata.json` written via `MetadataWriter.cs`, OpenCV fallback only — this is the *only* engine that still writes the unprefixed names as its primary/native output): written in English regardless of UI language.
- `cam1_metadata.txt` is a human-readable privacy-safe summary.
- `cam1_metadata.json` contains the complete structured technical data used by verification.
- Timestamp CSV files are the scientific timing source for Original Capture Mode.
- Empty MP4 files from failed recording startup should be removed during rollback.

## Video Files

Video files are MP4 files named by camera slot only (no timestamp prefix).

Example:

```text
cam1.mp4
```

MultiCamApp records original captured frames only. Exact 30.000 FPS cannot be guaranteed by all consumer cameras and depends on camera hardware, USB bandwidth, PC load, driver behavior, resolution, and exposure. Stable Real Capture FPS slightly below requested FPS, such as 29.684 FPS, can be acceptable when true timing is recorded in metadata.

In Original Capture Mode, the app does not insert duplicate or placeholder frames to force equal camera frame counts. Frame counts may differ because cameras delivered real frames at different measured FPS.

**Container (since 2.1.0): fragmented MP4, H.264, keyframe every 1 s.** A plain MP4 gets its index only when recording stops normally; after a crash, forced close or power loss it was unplayable ("moov atom not found", verified by a kill test). Fragmented MP4 stays playable up to the last fragment (kill test: 0–2 frames lost per camera). The 1-second keyframe interval also gives analysis tools fast, frame-accurate seeking. Standard players, ffmpeg/ffprobe, OpenCV and browsers read fragmented MP4. Both settings are in `VideoEngineSettings` (`CrashSafeFragmentedMp4`, `KeyframeIntervalSeconds`).

## Metadata Language

`camN_metadata.txt` and `session_metadata.txt` are written in the UI language active when the recording started (English or Japanese; changing the language during a recording does not change it; the "Metadata language" line says which). Field names in `camN_metadata.json`, the CSV column names and technical identifiers (codec, API and column names) are always English, so scripts can read files from either language.

## Interrupted Recordings (crash, forced close, power loss)

While a recording is running, the session folder contains `session_in_progress.json` (settings, cameras, start time; no paths). It is deleted after the metadata is written at a normal stop. At the next start, the app looks for sessions in the output folder (last 30 days) that still have this file or a `camN.tmp.mp4`, then:

- verifies each `camN.tmp.mp4` with ffprobe and, if playable, renames it to `camN.mp4` (or `camN_recovered.mp4` — an existing file is never overwritten);
- counts the timestamp CSV rows and reports video frames minus CSV rows;
- writes `RECOVERY_REPORT.txt` (human-readable) and `recovery_report.json`, and renames the journal to `session_interrupted.json`;
- shows a notice in the Status panel. Files that cannot be played are left untouched and listed as *NotPlayable*.

Per-camera metadata files are not written for an interrupted session; run Video Verification before using it. Because the metadata is missing, Video Verification reports these files as FAIL and the timestamp CSV as incomplete. This is intended: an interrupted recording is not a complete scientific recording. The CSV can also hold a few rows more than the video, for frames still being written when the app closed (measured: 1–10 rows). Frames that are in the video are still correctly timed by rows 0…N−1.

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
cam1_timestamps.csv
cam2_timestamps.csv
cam3_timestamps.csv
cam4_timestamps.csv
```

Columns (VideoEngineV2, current default engine). Since v2.1.0 there is **exactly one row per frame in the MP4, in the same order** (row *k* = video frame *k*); frames delivered while the encoder was still opening or already stopping are not listed.

| Col | Name | Description |
| :-- | :--- | :---------- |
| 0 | `frameIndex` | 0-based MP4 frame number |
| 1 | `captureTimestampUtc` | UTC wall-clock time at which the frame reached the app |
| 2 | `appTimestampMsFromRecStart` | App arrival time in ms, relative to the first recorded frame (monotonic) |
| 3 | `appTimestampSecondsFromRecStart` | Same, in seconds |
| 4 | `monotonicTicks` | `Stopwatch.GetTimestamp()` ticks at row creation |
| 5 | `frameIntervalMs` | Camera capture-time interval to the previous recorded frame (ms); 0 for frame 0 |
| 6 | `estimatedCaptureFps` | Recorded frames ÷ recording time so far |
| 7 | `droppedFrameWarning` | 1 if the capture interval exceeded 2× the nominal frame interval, else 0 |
| 8 | `capturePtsMsFromFirstFrame` | Camera capture timestamp (Media Foundation `SystemRelativeTime`) relative to frame 0 — identical to the MP4 sample time of that frame |

`writerQueueDepth` and `writerLatencyMs` columns existed in the legacy OpenCV writer and are not present in current VideoEngineV2 Timestamp CSV output. Readers that index columns 0–7 by position are unaffected by column 8.

Use `capturePtsMsFromFirstFrame` for per-frame timing analysis (it is the camera's own capture time and matches the video file exactly); `appTimestamp*` show when each frame reached the app and include scheduling jitter.

Since 2.1.0 there is a 10th column, `capturePtsMsSystemClock`: the same capture timestamp as an absolute value on the Windows system (QPC) clock that every camera's frames are stamped with. Subtract it between cameras to align them exactly — for example, the start offset between cam1 and cam2 is `cam2.capturePtsMsSystemClock[0] − cam1.capturePtsMsSystemClock[0]`. Measured software start offsets were 28 ms (2 cameras), 106 ms (3 cameras) and 159 ms (4 cameras); use this column, not file start times, to synchronise cameras.

## Per-Camera Metadata

Each camera folder includes one metadata file pair — see [Session Folder Layout](#session-folder-layout) above:

- `cam1_metadata.json` for structured machine-readable metadata.
- `cam1_metadata.txt` for a privacy-safe human-readable summary.

**Two metadata schemas exist in this codebase:**

1. **VideoEngineV2 schema** (current default engine, camelCase, nested objects) — written by `MainWindow.xaml.cs`, read by `verification/V2MetadataReader.cs`. Top-level sections include `recordingEngine`, `videoSettings`, `cameras`, `controls` (per-control `applied`/`result`/`readback`/`warning`), `recording`, `timing`, `timingReference`, `vfrDriverBehavior`, `startupSettling`, `bitrateProfile`, `timingModels`, `timingClassification`, `frameIntegrity`, `timestampSource`, `cameraControlReadbackLimitations`, `backendInfo`, `ffprobeAudit`, `frameQuality`, `motionBlurRisk`, `visualQuality`, `verification`, `environmentalLock`, `uiDiagnostics`.
   - `videoSettings.frameTimingMode` — as of v1.2.65 (schema 1.3.0), recordings use a raw `IMFSinkWriter` pipeline with real per-frame VFR timestamps; this field reads `"Variable"` (real per-frame timestamps, not a fabricated constant rate). It replaced the older `constantFrameRateTarget` boolean.
   - `backendInfo.colorTaggingApplied`/`colorPrimaries`/`colorTransferFunction`/`colorMatrix`/`colorRange` — every recorded MP4 is tagged BT.709 primaries/transfer/matrix. `colorRange` was full-range (`0-255`/`pc`) matching Windows Camera through v1.2.77, briefly retagged limited-range (`16-235`/`tv`) in v1.2.66/v1.2.67 based on an unverified assumption about Windows Camera's own output, then corrected back to full-range (`0-255`/`pc`) in v1.2.78 after a side-by-side ffprobe comparison proved Windows Camera's real output is full-range, not limited. These fields document that tagging in the exported metadata (added in v1.2.67; the tagging itself shipped in v1.2.66's encoder but was not previously visible in metadata).
   - `ffprobeAudit.available` is always `false` and every field in that section is `null` — the automatic per-recording ffprobe container audit was intentionally removed in v1.2.22-alpha for performance; ffprobe now only runs on demand from the Video Verification page, which is where `nb_frames`/`color_*`/codec fields are actually cross-checked, not here.
   - `frameQuality`, `motionBlurRisk`, and `visualQuality` are reserved sections for duplicate/near-duplicate frame detection, motion-blur risk, and visual quality scoring (blur, exposure, brightness). **None of these are implemented yet** — every field is `null`/`"Unknown"`/`"NotAvailable"` and `visualQuality.implemented` is explicitly `false`. Real duplicate-frame verification currently happens only via exact MD5 frame hashing external to the app (`ffmpeg -f framemd5 -fps_mode passthrough`, not the app itself); there is no in-app blur or exposure scoring today.
2. **Legacy schema** (PascalCase, flat) — written by `metadata/MetadataWriter.cs`, only reachable via the OpenCV fallback path (dormant on any machine with working Media Foundation, which is effectively all Windows 10/11 hardware). Fields: `AppVersion`, `BuildNumber`, `CameraSlot`, `CameraName`, `RequestedResolution`, `ActualResolution`, `RequestedFps`, `RecordingTimingMode`, `OriginalCaptureMode`, `MeasuredCameraFps`, `WriterFps`, `ContainerFps`, `EffectiveRecordedFps`, `FramesCaptured`, `FramesWritten`, `WriterQueueDrops`, `DuplicateFrames`/`DuplicatedFrames`, `PlaceholderFrames`, `CaptureIntervalMeanMs`, `CaptureIntervalMinMs`, `CaptureIntervalMaxMs`, `CaptureIntervalStdMs`, `ScientificTimingStatus`, `ScientificTimingMessage`, `FrameTimestampCsvPath`, `FrameTimestampCsvWritten`, `FrameTimestampCsvRowCount`.

**As of v1.2.104**, `verification/MetadataParser.cs` (the component Video Verification actually reads metadata through) natively detects and parses the VideoEngineV2 schema directly — it no longer silently returns all-zero/default values for a V2 file the way it did through v1.2.103. Video Verification's own PASS/PASS_WITH_WARNING/FAIL classification for a V2 recording is now computed correctly the first time, using VideoEngineV2's own already-computed verdict, rather than needing a later on-screen correction pass. If you're inspecting a recording made with the default engine (check `recordingEngine.engine == "VideoEngineV2"` in the JSON), you're looking at schema 1, and the verification pipeline understands it natively. `V2MetadataReader` also reads the `controls` object generically (any key with `applied`/`result`/`readback`/`warning`), so it automatically picks up new controls without code changes.

User-facing timing terms:

| User-facing term | Meaning |
| :--- | :--- |
| Recording mode | Original Capture Mode |
| Real Capture FPS | Real measured camera FPS from capture timestamps |
| Playback FPS | MP4 container/writer FPS tag |
| Timestamp CSV | Scientific timing source |
| Writer drops | Frames dropped by the writer queue |
| Frame integrity | Real frames only; no duplicates/placeholders |

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

`session_metadata.json`/`session_metadata.txt` (VideoEngineV2) — or `session_summary.txt` on the legacy OpenCV path — is written at the session root.

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

## New metadata fields (2.1.0)

- `Camera input subtype` (txt) / `cameraInputSubtype` (json): the camera-side format actually used (`NV12`, `YUY2`, `MJPG`, …).
- `Fallback reason` / `fallbackReason`: now also states when the camera mode was switched automatically because the first mode delivered no frames.
- `Auto exposure during recording` (txt) / `autoExposureActive` (json): read back from the camera at recording start (`On (camera auto exposure kept)` / `Off` / `Unknown`), replacing fixed text. `Manual exposure`, `Manual exposure value` and `Manual exposure readback` show the shutter the camera confirmed when exposure is manual.

## Data Preservation

The installer and uninstaller are designed to preserve recorded videos, exported reports, and user project folders. Build cleanup scripts should not be run on user output folders.
