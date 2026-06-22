# MultiCamApp Architecture

MultiCamApp is a WPF (.NET 8) desktop application for offline multi-camera recording, metadata generation, and video verification on Windows 10/11.

## Main Layers

- **UI:** WPF views and view models for recording, Video Verification, Hardware Diagnostics, About, and settings.
- **Capture backend:** camera slot pipelines, OpenCV/DirectShow preview, WinRT fallback, duplicate-device mapping, and preview lifecycle.
- **Recording backend:** recording sessions, per-camera writers, Original Capture Mode timing, metadata, and session summaries.
- **Verification backend:** bundled `ffprobe`, metadata readers, audit classification, Timestamp CSV analysis, and export.
- **Installer/runtime:** Inno Setup installer, bundled VC++ runtime, bundled FFmpeg tools, and smoke-test diagnostics.

## Camera And Recording Flow

MultiCamApp uses OpenCV/DirectShow as the preferred preview and recording path for external USB cameras. WinRT MediaCapture is available as an exact-device fallback when OpenCV cannot open a selected device.

Each active camera slot owns its own lifecycle:

```text
selected device -> open preview -> preview ready -> start recording -> write real frames -> stop recording -> write metadata -> release resources
```

For 3/4-camera layouts, cameras are opened in a staggered order to reduce USB and DirectShow driver contention. OBSBOT devices are opened first when selected, followed by the remaining cameras.

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
  session_summary.txt
  cam1\
    MCAM_YYYYMMDD_HHMMSS_cam1.mp4
    cam1_frame_timestamps.csv
    metadata.txt
    metadata.json
  cam2\
    ...
```

See [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md).

## Diagnostics

Important diagnostics include startup logs, camera open traces, recording start diagnostics, Video Verification exports, Hardware Diagnostics reports, and performance summaries. Hardware Diagnostics are advisory and privacy-safe. They do not change recording behavior.

## Related Docs

- [Output Files and Metadata](../OUTPUT_FILES_AND_METADATA.md)
- [Video Verification](../user_guide/video_verification.md)
- [Hardware Diagnostics](../user_guide/hardware_diagnostics.md)
- [Installer Logic](installer_logic.md)
