# MultiCamApp Architecture

MultiCamApp is a WPF (.NET 8) desktop application for offline multi-camera recording, metadata generation, and video verification on Windows 10/11.

## Main Layers

- **UI:** WPF views and view models for recording, Video Verification, Hardware Diagnostics, About, and settings.
- **Capture backend:** camera slot pipelines (`capture/CameraSlotPipeline.cs`), the VideoEngineV2 engine (`capture/video_engine_v2/`), OpenCV/DirectShow fallback preview, duplicate-device mapping, and preview lifecycle.
- **Recording backend:** VideoEngineV2 recording sessions (`MediaFoundationEncoderService` — a raw `IMFSinkWriter` H.264 encoder via `Vortice.MediaFoundation`, `CameraPipelineV2`), the legacy `recording/` orchestration shared across both engines, Original Capture Mode timing, metadata, and session summaries.
- **Verification backend:** bundled `ffprobe`, `verification/MetadataParser.cs` (natively parses both the legacy flat schema and the VideoEngineV2 nested schema as of v1.2.104 — see [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md#per-camera-metadata)), a separate V2 metadata reader/runner (`verification/V2MetadataReader.cs`, `V2VerificationRunner.cs`) used for the per-camera Deep Verify / V2-specific detail sections, audit classification, Timestamp CSV analysis, and export.
- **Installer/runtime:** Inno Setup installer, bundled VC++ runtime, bundled FFmpeg tools, and smoke-test diagnostics.

## Camera And Recording Flow

As of v1.2.22-alpha, **VideoEngineV2 (WinRT `MediaCapture` for capture + a raw Media Foundation `IMFSinkWriter` H.264 encoder) is the default and primary preview and recording path** for all camera types, including external USB cameras. The experimental V3/V3B backends that previously existed alongside V2 were removed entirely in v1.2.22-alpha. OpenCV/DirectShow remains in the codebase as a fallback path (`previewEngine`/`recordingEngine` config can select it, and it's used automatically if the WinRT path cannot open a selected device), but it is no longer the preferred default.

As of v1.2.65, the encoder (`MediaFoundationEncoderService`) writes samples directly via `Vortice.MediaFoundation`'s `IMFSinkWriter` instead of the higher-level WinRT `LowLagMediaRecording` API, so recorded MP4s carry real per-frame VFR timestamps rather than a forced constant frame rate. As of v1.2.66, every recorded MP4 is also tagged with BT.709 color primaries/transfer/matrix, and this tagging is surfaced in exported metadata and the Video Verification page as of v1.2.67. The nominal range was initially tagged limited-range (`tv`) in v1.2.66/v1.2.67 based on an unverified assumption, then corrected to full-range (`0-255`/`pc`) in v1.2.78 after a side-by-side ffprobe comparison proved Windows Camera's real output is full-range, not limited.

Each active camera slot owns its own lifecycle:

```text
selected device -> open camera (VideoEngineV2) -> negotiate format -> apply research-safe camera-control defaults
  -> preview ready -> start recording -> write real frames (Media Foundation H.264) -> stop recording
  -> finalize MP4 -> write metadata (JSON + TXT, prefixed and legacy-compatible names) -> release resources
```

For 3/4-camera layouts, cameras are opened in a staggered order (see `CaptureResolutionHelper.MultiCameraStaggerMs`) to reduce USB and driver contention.

## Original Capture Mode

Original Capture Mode preserves real camera frames only. It does not insert duplicate frames or placeholder frames to force equal frame counts or a nominal FPS.

Scientific timing is based on monotonic capture timestamps and per-frame Timestamp CSV files. MP4 Playback FPS is the container/writer tag and must not be treated as the scientific timing source when Real Capture FPS differs from Playback FPS.

Expected scientific frame integrity text:

```text
Real frames only; no duplicates/placeholders
```

Frame counts may differ because cameras delivered real frames at different measured FPS. This is expected when `FramesCaptured == FramesWritten`, duplicate frames are 0, placeholder frames are 0, and Writer drops are 0.

## Threading

| Concern | Thread / owner |
|---------|----------------|
| UI state | WPF UI thread |
| Preview frames | OpenCV/WinRT callbacks, throttled onto UI dispatcher |
| Recording | Per-camera capture/write workers |
| Metadata | Async file I/O after stop |
| Diagnostics | Background log and FPS monitors |

Recording durations use monotonic timing (`Stopwatch` / QueryPerformanceCounter), not UI timers or MP4 container duration.

## Output Layout

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

`cam1_metadata.json`/`cam1_metadata.txt` are the VideoEngineV2 per-camera metadata files. Through v1.2.104, `MainWindow.xaml.cs` also wrote a byte-identical unprefixed `metadata.json`/`metadata.txt` duplicate, solely because `VideoScanner` only recognized that exact unprefixed name. As of v1.2.105, `VideoScanner`/`SessionComparisonService` resolve the slot-prefixed name directly (`RecordingSessionDiscovery.FindCameraMetadataFile`, falling back to the unprefixed name for older recordings or the legacy OpenCV engine), so the duplicate write was removed.

See [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md).

## Diagnostics

Important diagnostics include startup logs, camera open traces, recording start diagnostics, Video Verification exports, Hardware Diagnostics reports, and performance summaries. Hardware Diagnostics are advisory and privacy-safe. They do not change recording behavior.

## Related Docs

- [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md)
- [Video Verification](../user_guide/video_verification.md)
- [Hardware Diagnostics](../user_guide/hardware_diagnostics.md)
- [Installer Logic](installer_logic.md)
