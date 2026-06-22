# MultiCamApp

Windows 10/11 offline multi-camera recording, metadata capture, and video verification for educational and research use.

Current release: **v1.1.0 stable**. This release uses Original Capture Mode, per-frame Timestamp CSV files, scientific timing confidence, privacy-safe metadata summaries, clearer camera-control metadata wording, and scientific exposure defaults. All multi-camera sessions (2–4 cameras) now use a synchronized start gate, keeping inter-camera first-frame offset below 50 ms.

This project includes third-party components. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Quick Start

1. Install with `installer\Setup.exe`. The installer is fully offline and includes required runtime dependencies.
2. Launch MultiCamApp from the Desktop or Start Menu.
3. Select a 1-4 camera layout and assign devices to slots.
4. Click **Start Preview** and wait for active cameras to show live preview.
5. Click **Start Recording** only after preview is ready.
6. Click **Stop Recording**. Videos, Timestamp CSV files, metadata, and session summaries are saved automatically.
7. Open **Video Verification**, select the recorded session folder, and verify frame integrity, timing, and sync quality offline.

## Original Capture Mode

Original Capture Mode preserves real camera frames only. It does not insert duplicate frames or placeholder frames to force exact nominal FPS or equal frame counts.

Frame counts may differ because cameras delivered real frames at different measured FPS. For example, an OBSBOT camera may measure about `30.000 FPS` while j5 cameras measure about `29.684 FPS`. This is acceptable when frames captured match frames written, duplicate frames are 0, placeholder frames are 0, Writer drops are 0, and Timestamp CSV rows match frames written.

Use Timestamp CSV for timing-sensitive analysis. MP4 Playback FPS and container duration are playback/container metadata, not the primary scientific timing source.

## Camera Handling

- MultiCamApp is camera-agnostic. OBSBOT and j5 cameras are validation examples only.
- Any Windows-recognized camera should be usable if Windows, DirectShow, and OpenCV can see it, another app has not locked it, and the PC/USB/disk pipeline can sustain the workload.
- 3/4-camera preview startup is staggered to reduce USB and DirectShow contention.
- Hardware Diagnostics are advisory and privacy-safe. They do not change recording behavior.

See [Architecture Overview](docs/architecture/overview.md) and [Hardware Diagnostics](docs/user_guide/hardware_diagnostics.md).

## Build

```bat
installer\build_release.bat
```

| Output | Location |
|--------|----------|
| Published app bundle | `dist\MultiCamApp.exe` |
| End-user installer | `installer\Setup.exe` |
| Installer ZIP export | `installer\installer.zip` |
| Version metadata | `dist\config\version.json` |
| Release summary | `installer\build_release_summary.txt` |

## Developer Setup

```bat
scripts\setup_tools.bat
```

This downloads local build dependencies into `tools\`, vendor runtime files into `runtime\`, and installer prerequisites into `installer\`. The installed app does not require the developer `tools\` folder.

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

## Validation

Use the in-app **Video Verification** page after recording. Simple View shows status, Real Capture FPS, Playback FPS, FPS stability, frames written, Writer drops, Timestamp CSV status, timing source, and recommended action. Detailed View keeps the full technical audit.

Scientific timing terms:

- **Recording mode:** Original Capture Mode
- **Real camera frame rate:** Real Capture FPS
- **MP4/container playback frame rate:** Playback FPS
- **Scientific timing source:** Timestamp CSV
- **Frame integrity:** Real frames only; no duplicates/placeholders
- **Queue drops:** Writer drops

See [Video Verification](docs/user_guide/video_verification.md) and [Output Files and Metadata](docs/OUTPUT_FILES_AND_METADATA.md).

## Documentation

- [Installation Guide](INSTALLATION.md)
- [Directory Structure](DIRECTORY_STRUCTURE.md)
- [Third-Party Notices](THIRD_PARTY_NOTICES.md)
- [Output Files and Metadata](docs/OUTPUT_FILES_AND_METADATA.md)
- [Video Verification](docs/user_guide/video_verification.md)
- [Hardware Diagnostics](docs/user_guide/hardware_diagnostics.md)
- [Architecture Overview](docs/architecture/overview.md)
- [Installer Logic](docs/architecture/installer_logic.md)
- [Security and Antivirus](docs/user_guide/security_antivirus.md)
- [Changelog](docs/changelogs/CHANGELOG.md)

## Uninstallation

Use **Uninstall MultiCamApp** from the Start Menu or Windows Control Panel. The uninstaller removes application files and bundled runtime components while preserving recorded videos, exported reports, and user project folders.

## Citation

Mun AY. MultiCamApp: Offline multi-camera synchronized recording and verification platform for behavioral analysis. Version v1.1.0. 2026.

## Attribution

**Original prototype workflow concept and foundational recording logic:**
Shinnosuke Koketsu

**Modern application architecture, UI/UX redesign, offline packaging, verification framework, diagnostics, metadata/reporting, localization, testing, release engineering, and maintenance:**
Aung Ye Mun

## License

© 2026–Present Aung Ye Mun and contributors. Non-commercial research and educational use only.
See [LICENSE.txt](docs/license/LICENSE.txt) for details.
