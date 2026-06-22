# Hardware Diagnostics

Hardware Diagnostics is an advisory-only page for understanding whether the current PC, camera setup, USB / Camera Connection, and graphics driver are likely to sustain 3/4-camera preview and recording.

Diagnostics are advisory and privacy-safe. They do not change recording behavior.

The page does not block recording, disable presets, change selected cameras, change camera order, change metadata, or modify Video Verification thresholds.

## Camera Support Principle

MultiCamApp is camera-agnostic. OBSBOT and j5 cameras are validation examples only.

Any Windows-recognized camera should be usable when:

- Windows, DirectShow, and OpenCV can see it.
- Another app has not locked it.
- The camera truly supports the selected resolution and FPS.
- USB bandwidth, drivers, CPU/GPU, and disk can sustain the workload.

Known-device compatibility rules must remain isolated and must not affect generic cameras.

## What the Scan Collects

System summary:

- App version and build number.
- Scan time.
- OS version.
- CPU and RAM summary when available.
- Display adapter and driver summary when available.
- Intel/NVIDIA/AMD detection.
- Microsoft Basic Display Adapter warning.
- Safe encoder/driver hints.

Camera capability report:

- Detected camera display name.
- External/built-in/virtual classification when detectable.
- Advisory status for 640x480/30, 1280x720/30, and 1920x1080/30.

By default the camera scanner avoids opening cameras so it does not disturb preview, recording, or DirectShow mappings. It may report `Unknown`; that does not mean unsupported.

USB / Camera Connection report:

- Selected cameras.
- USB controller/hub mapping if safely available without privacy-sensitive identifiers.
- `Unknown` when mapping cannot be confirmed.

If USB topology is unavailable, the UI uses advisory wording:

```text
USB topology unavailable. For unstable 3-4 camera recording, try separate USB ports, a powered USB hub, or lower resolution.
```

## Output Logs

Hardware scan writes reports under the app logs folder:

```text
logs\system_profile_YYYYMMDD_HHMMSS.json
logs\SystemProfile.latest.json
logs\camera_capability_YYYYMMDD_HHMMSS.json
logs\CameraCapability.latest.json
logs\usb_topology_YYYYMMDD_HHMMSS.json
logs\UsbTopology.latest.json
```

## UI Layout

Only summary cards are expanded by default:

- System
- Camera Devices
- USB / Camera Connection

Detailed reports are collapsed by default:

- Detailed System Report
- Detailed Camera Capability Report
- Detailed USB Topology Report

Unavailable USB topology should use neutral/advisory styling unless an actual recording problem is detected.

## UI Actions

- **Run Hardware Scan:** runs the advisory scan offline.
- **Open Logs Folder:** opens the folder containing diagnostic JSON files.
- **Copy Diagnostic Summary:** copies a text summary for support or lab notes.

A modal warning is shown only when Microsoft Basic Display Adapter is detected. USB topology availability is advisory unless paired with a real recording problem such as Writer drops or failed camera access.

## Privacy

Hardware Diagnostics must not store:

- Hardware IDs
- Serial numbers
- MAC addresses
- Computer name
- Windows username
- Full user profile paths

## Preset Guidance

- 1/2-camera: normal presets are allowed when cameras support the chosen resolution/FPS.
- 3-camera: start with 360p, use 720p if stable, and use 1080p with bandwidth and driver warnings.
- 4-camera: start with 360p.
- Built-in fallback: use 360p first, 720p if stable, and do not use built-in fallback for 1080p stress.
- Generic external cameras: 720p/1080p is allowed if capability, USB bandwidth, drivers, CPU/GPU, and disk can sustain it.

## Deferred Items

The following remain intentionally outside the Hardware Diagnostics page:

- WPF preview renderer replacement.
- Zero-copy or BackBuffer preview.
- Full watchdog recovery.
- Placeholder frame salvage mode.
- Automatic per-camera stop/recovery.
- GPU usage or hardware encoder switching.

Existing startup, first-frame, stop summary, frame count, queue-drop, and scientific timing diagnostics remain the primary recording health evidence.

## Performance Diagnostics

Performance Monitor is diagnostic-only and runs quietly during preview and recording. It adds no UI controls, buttons, panels, icons, popups, status text, or live performance labels.

Logs are saved only in the app logs folder:

```text
logs\performance_monitor_YYYYMMDD_HHMMSS.csv
logs\performance_summary_YYYYMMDD_HHMMSS.txt
logs\PerformanceSummary.latest.txt
```

The logs are intentionally small:

- sampled once per second,
- CSV text only,
- summary text only,
- no screenshots,
- no raw frames,
- no per-frame logs,
- no video data.

Performance logs are support/developer diagnostics, not scientific output videos. They are not written inside recording session folders.

Slow preview does not always mean bad recording. Use Video Verification and recording metadata to judge recording validity. The performance summary can help distinguish CPU load, preview rendering pressure, USB/camera driver delays, disk/write pressure, and recording queue drops before any GPU usage or GPU encoder switching is considered.
