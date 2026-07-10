# MultiCamApp

**Offline Windows multi-camera recording, metadata capture, timestamp logging, and video verification software for educational, laboratory, and research use.**

MultiCamApp is a Windows desktop application designed to support reproducible video-based workflows. It allows users to record from 1–4 cameras, capture metadata, generate per-frame timestamp files, and verify recorded videos after acquisition.

Current release: **v2.0.5** (Stable)

---

## Overview

MultiCamApp provides an offline workflow for users who need controlled multi-camera video recording with timing information and post-recording verification.

It is suitable for educational and research settings where users need:

* Multi-camera video recording
* Offline operation
* Metadata capture
* Per-frame timestamp CSV files
* Video integrity and timing verification
* Clear recording-quality summaries
* Privacy-safe output files

MultiCamApp was originally developed for research video recording workflows, including behavioral and locomotor activity studies, but it can also support broader video-based educational and laboratory documentation needs.

---

## Potential Use Cases

MultiCamApp is not limited to one specific experiment type. Possible use cases include:

* Behavioral and locomotor activity recording
* Multi-angle experimental observation
* Educational demonstrations and teaching recordings
* Laboratory procedure documentation
* Video-based reproducibility studies
* Camera setup, focus, exposure, and recording-quality checks
* Offline research video capture
* Synchronized-view documentation where software-level timing verification is sufficient

MultiCamApp is especially useful when users need offline recording, metadata capture, timestamp files, and post-recording video verification.

---

## Key Features

* Supports **1–4 camera layouts**
* **VideoEngineV2** recording engine using Windows MediaFoundation — works on integrated graphics, no discrete GPU required
* Compatible with any Windows Camera-compatible webcam (USB 2.0/3.0, built-in laptop camera, cheap 1080p/720p webcams)
* Offline Windows recording workflow — no internet required
* Original Capture Mode that preserves real camera frames only (no duplicates, no placeholder frames)
* Per-frame timestamp CSV files for scientific timing analysis
* Metadata output in human-readable (TXT) and machine-readable (JSON) formats
* Metadata language follows the selected UI language (English or Japanese) at recording start
* English and Japanese UI localization
* Video Verification page with Simple View and Detailed View
* Scientific timing confidence reporting
* Privacy-safe metadata summaries
* Hardware diagnostics page
* High-stability recording mode (always enabled internally)
* Coordinated software start gate with timing verification for multi-camera sessions
* Offline installer
* Third-party notices and FFmpeg/ffprobe license files included

---

## Scope and Timing Model

MultiCamApp provides **software-based recording coordination**, timestamp logging, and post-recording verification.

It does **not** provide hardware-triggered camera synchronization.

This means:

* Cameras are started through a coordinated software start process.
* Each camera may still have small hardware/driver-dependent start-time offsets.
* Real measured camera FPS may differ slightly from the requested FPS.
* MultiCamApp records timing information and reports these differences instead of hiding them.
* For timing-sensitive analysis, users should review the timestamp CSV files and verification reports.

This design is intended to preserve scientific transparency.

---

## Download

End users should download the installer from the **GitHub Releases** page.

Recommended:

* Download `Setup.exe` to install MultiCamApp directly.

Optional:

* Download `installer.zip` if you want the installer together with important documentation.

The source repository is for development, review, documentation, and reproducible building. Installer binaries such as `Setup.exe` and `installer.zip` are not committed directly to the repository.

---

## Quick Start

1. Download `Setup.exe` or `installer.zip` from GitHub Releases.
2. Run the installer.
3. Launch **MultiCamApp** from the Desktop or Start Menu.
4. Select a 1–4 camera layout.
5. Assign camera devices to active camera slots.
6. Choose resolution and frame rate.
7. Click **Start Preview** and wait for active cameras to show live preview.
8. Click **Start Recording** when preview is ready.
9. Click **Stop Recording** when finished.
10. Open **Video Verification**, select the recorded session folder, and verify frame integrity, timing, and recording quality offline.

---

## Original Capture Mode

MultiCamApp uses **Original Capture Mode** (introduced in v1.1.0, still the default in the current VideoEngineV2 pipeline).

Original Capture Mode preserves real camera frames only. It does not insert duplicate frames or placeholder frames to force exact nominal FPS or equal frame counts.

Frame counts may differ between cameras because cameras can deliver real frames at slightly different measured FPS. For example, one camera may measure close to `30.000 FPS`, while another camera may measure around `29.x FPS`.

This is acceptable when:

* Frames captured match frames written
* Duplicate frames are 0
* Placeholder frames are 0
* Writer drops are 0
* Timestamp CSV rows match frames written
* Timing confidence is acceptable for the intended analysis

For timing-sensitive analysis, use the timestamp CSV files and verification reports as the primary scientific timing references.

---

## Output Files

Each recording session saves videos and metadata automatically.

Typical outputs may include:

* Recorded video files
* Per-frame timestamp CSV files
* Metadata JSON files
* Human-readable metadata TXT summaries
* Session summary files
* Video verification reports

The exact output structure depends on the selected camera layout and recording session.

---

## Video Verification

MultiCamApp includes an offline **Video Verification** page.

The verification system can help users review:

* Video readability
* Frame counts
* Timestamp CSV availability
* Timestamp row count consistency
* Duplicate frame count
* Placeholder frame count
* Writer queue drops
* Wall-clock duration
* Frame-based duration
* Container duration
* Measured camera FPS
* Inter-camera timing differences
* Scientific timing confidence

Verification results are intended to help users decide whether a recording session is suitable for downstream analysis.

---

## Hardware Diagnostics

MultiCamApp includes hardware diagnostics to help users evaluate recording stability.

Diagnostics may include information related to:

* Camera availability
* Selected camera settings
* Recording performance
* Timing stability
* Writer queue behavior
* System-level recording risks

Recording stability can depend on camera hardware, USB bandwidth, CPU, RAM, disk speed, and camera drivers.

---

## Known Limitations

* Windows only.
* Recording performance depends on camera hardware, USB bandwidth, CPU, RAM, disk speed, and camera drivers.
* Some webcams may capture at a stable real FPS slightly below the requested FPS, such as 29.x FPS instead of exactly 30 FPS.
* Multi-camera recordings may have small hardware/driver-dependent start-time offsets.
* MultiCamApp provides software-based recording coordination and verification, but not hardware-triggered camera synchronization.
* Preview FPS is optimized for camera checking and usability; recorded video quality and timing information are the priority.
* Not intended for clinical diagnosis, medical decision-making, or regulated medical use.

---

## Responsible Use

MultiCamApp is intended for lawful, consent-based recording only.

Do not use this software for illegal surveillance, spying, harassment, unauthorized recording, privacy invasion, or any activity that violates the rights, safety, or privacy of others.

Users are responsible for following applicable institutional policies, ethics approvals, local laws, and privacy regulations.

---

## Privacy

MultiCamApp is designed as an offline application.

The app does not require cloud services for normal recording and verification workflows. Output files are saved locally on the user's computer.

Users should still review exported files before sharing them publicly, especially videos, screenshots, reports, and metadata.

---

## Third-Party Components

MultiCamApp includes bundled third-party components, including FFmpeg/ffprobe and other runtime dependencies.

See:

* `THIRD_PARTY_NOTICES.md`
* `FFMPEG_LICENSE.txt`
* License files included with the installer or installation folder

---

## Documentation

Additional documentation:

* Installation Guide
* Directory Structure
* Third-Party Notices
* Output Files and Metadata
* Video Verification
* Hardware Diagnostics
* Architecture Overview
* Installer Logic
* Security and Antivirus
* Changelog

---

## Uninstallation

Use **Uninstall MultiCamApp** from the Start Menu or Windows Control Panel.

The uninstaller removes application files and bundled runtime components while preserving recorded videos, exported reports, and user project folders.

---

## Citation

If you use MultiCamApp in research, education, or published work, please cite:

Mun AY, Koketsu S. **MultiCamApp: Offline multi-camera recording, metadata capture, and video verification platform for research and educational workflows.** Version v2.0.5. 2026.

---

## Attribution

Original prototype workflow concept and foundational recording idea:

**Shinnosuke Koketsu**

Modern application architecture, UI/UX redesign, offline packaging, verification framework, diagnostics, metadata/reporting, localization, testing, release engineering, and maintenance:

**Aung Ye Mun**

---

## License

© 2026–Present Aung Ye Mun and contributors.

MultiCamApp is available for non-commercial academic, scientific, educational, evaluation, and personal use.

Commercial use, redistribution, resale, sublicensing, SaaS hosting, monetized distribution, or commercial incorporation is prohibited without prior written permission from the copyright holder.

See `LICENSE.md` for full details.
