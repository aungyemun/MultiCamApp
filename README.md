# MultiCamApp

Windows 10/11 offline multi-camera recording, metadata capture, and video verification software for educational and research use.

**Current release: v1.1.0 Stable**

MultiCamApp v1.1.0 uses **Original Capture Mode**, per-frame **Timestamp CSV** files, scientific timing confidence, privacy-safe metadata summaries, clearer camera-control metadata wording, and scientific exposure defaults.

For multi-camera recording, active 2–4 camera sessions use a synchronized start gate to reduce inter-camera first-frame offset. In validated test sessions, inter-camera first-frame offset remained below 50 ms.

This project includes third-party components. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## Download

For end users, download the latest offline installer from the GitHub Releases page.

Use:

* `Setup.exe` for installation
* `installer.zip` if you want the installer together with important user documentation

The source repository is intended for development, review, documentation, and reproducible building.

---

## Quick Start

1. Install with `Setup.exe` from the GitHub Releases page. The installer is fully offline and includes the required runtime dependencies.
2. Launch MultiCamApp from the Desktop or Start Menu.
3. Select a 1–4 camera layout and assign devices to active camera slots.
4. Click **Start Preview** and wait for all active cameras to show live preview.
5. Click **Start Recording** only after preview is ready.
6. Click **Stop Recording**. Videos, Timestamp CSV files, metadata, and session summaries are saved automatically.
7. Open **Video Verification**, select the recorded session folder, and verify frame integrity, timing, and synchronization quality offline.

---

## Original Capture Mode

Original Capture Mode preserves real camera frames only.

It does **not** insert duplicate frames or placeholder frames to force exact nominal FPS or equal frame counts.

Frame counts may differ when cameras deliver real frames at slightly different measured frame rates. For example, an OBSBOT camera may measure around `30.000 FPS`, while some j5 cameras may measure around `29.684 FPS`.

This is acceptable when:

* Frames captured match frames written
* Duplicate frames = 0
* Placeholder frames = 0
* Writer drops = 0
* Timestamp CSV rows match frames written

For timing-sensitive analysis, use the **Timestamp CSV** files. MP4 playback FPS and container duration are playback/container metadata and are not the primary scientific timing source.

---

## Camera Handling

* MultiCamApp is camera-agnostic. OBSBOT and j5 cameras are validation examples only.
* Any Windows-recognized camera should be usable if Windows, DirectShow, and OpenCV can access it, no other application has locked it, and the PC/USB/disk pipeline can sustain the selected workload.
* Preview startup for 3-camera and 4-camera layouts is staggered to reduce USB and DirectShow contention.
* Hardware Diagnostics are advisory and privacy-safe. They do not change recording behavior.

See [Architecture Overview](docs/architecture/overview.md) and [Hardware Diagnostics](docs/user_guide/hardware_diagnostics.md).

---

## Build

```bat
installer\build_release.bat
```

| Output               | Location                              |
| -------------------- | ------------------------------------- |
| Published app bundle | `dist\MultiCamApp.exe`                |
| End-user installer   | `installer\Setup.exe`                 |
| Installer ZIP export | `installer\installer.zip`             |
| Version metadata     | `dist\config\version.json`            |
| Release summary      | `installer\build_release_summary.txt` |

---

## Developer Setup

```bat
scripts\setup_tools.bat
```

This downloads local build dependencies into `tools\`, vendor runtime files into `runtime\`, and installer prerequisites into `installer\`.

The installed app does not require the developer `tools\` folder.

---

## Source Code vs Installer

The GitHub repository contains source code, documentation, build scripts, release configuration, license information, and third-party notices.

End users should install MultiCamApp using `Setup.exe` from the GitHub Releases page.

Generated build outputs, local developer tools, and installer binaries may be excluded from the main source repository and distributed through GitHub Releases instead.

---

## Project Layout

```text
source\       C# WPF app, launcher, and tests
dist\         generated published app bundle used by Setup.exe
installer\    Inno Setup script, build script, Setup.exe, VC++ redist, installer.zip
scripts\      build, diagnostic, maintenance, packaging, and setup scripts
runtime\      staged vendor runtime helpers and FFmpeg tools
tools\        local developer toolchain
docs\         architecture, user, release, license, and changelog docs
data\         temporary scratch/download area
```

See [Directory Structure](DIRECTORY_STRUCTURE.md).

---

## Validation

Use the in-app **Video Verification** page after recording.

Simple View shows:

* Overall status
* Real Capture FPS
* Playback FPS
* FPS stability
* Frames written
* Writer drops
* Timestamp CSV status
* Timing source
* Recommended action

Detailed View keeps the full technical audit.

Scientific timing terms:

| Term                              | Meaning                                      |
| --------------------------------- | -------------------------------------------- |
| Recording mode                    | Original Capture Mode                        |
| Real camera frame rate            | Real Capture FPS                             |
| MP4/container playback frame rate | Playback FPS                                 |
| Scientific timing source          | Timestamp CSV                                |
| Frame integrity                   | Real frames only; no duplicates/placeholders |
| Queue drops                       | Writer drops                                 |

See [Video Verification](docs/user_guide/video_verification.md) and [Output Files and Metadata](docs/OUTPUT_FILES_AND_METADATA.md).

---

## Responsible Use

MultiCamApp is intended for controlled, lawful, ethical, and consent-based recording.

Users are responsible for complying with all applicable laws, institutional policies, privacy regulations, and consent requirements in their country or region.

Do not use this software for illegal surveillance, spying, harassment, unauthorized recording, privacy invasion, or any activity that violates the rights, safety, or privacy of others.

---

## Documentation

* [Installation Guide](INSTALLATION.md)
* [Directory Structure](DIRECTORY_STRUCTURE.md)
* [Third-Party Notices](THIRD_PARTY_NOTICES.md)
* [Output Files and Metadata](docs/OUTPUT_FILES_AND_METADATA.md)
* [Video Verification](docs/user_guide/video_verification.md)
* [Hardware Diagnostics](docs/user_guide/hardware_diagnostics.md)
* [Architecture Overview](docs/architecture/overview.md)
* [Installer Logic](docs/architecture/installer_logic.md)
* [Security and Antivirus](docs/user_guide/security_antivirus.md)
* [Security Policy](SECURITY.md)
* [Changelog](CHANGELOG.md)

---

## Uninstallation

Use **Uninstall MultiCamApp** from the Start Menu or Windows Control Panel.

The uninstaller removes application files and bundled runtime components while preserving recorded videos, exported reports, and user project folders.

---

## Citation

Mun AY. MultiCamApp: Offline multi-camera synchronized recording and verification platform for behavioral analysis. Version v1.1.0. 2026.

Citation metadata is also provided in [CITATION.cff](CITATION.cff).

---

## Attribution

**Original prototype workflow concept and foundational recording logic:**
Shinnosuke Koketsu

**Modern application architecture, UI/UX redesign, offline packaging, verification framework, diagnostics, metadata/reporting, localization, testing, release engineering, and maintenance:**
Aung Ye Mun

---

## License

© 2026–Present Aung Ye Mun and contributors. Non-commercial research and educational use only.

See [LICENSE.md](LICENSE.md) for details.
