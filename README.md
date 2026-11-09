<div align="center">

<img src="source/MultiCamApp/MultiCamApp/assets/icons/MulticamMark.png" alt="MultiCamApp logo" width="96">

# MultiCamApp

**Offline multi-camera recording, metadata capture and video verification for Windows — built for research, laboratory and educational work.**

[![Latest release](https://img.shields.io/github/v/release/aungyemun/MultiCamApp?label=release&color=2563eb)](https://github.com/aungyemun/MultiCamApp/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20x64-0f172a)
![.NET](https://img.shields.io/badge/.NET-8-512bd4)
![Languages](https://img.shields.io/badge/UI-English%20%7C%20日本語-0f766e)
[![License](https://img.shields.io/badge/license-non--commercial%20academic-64748b)](LICENSE.md)

[**Download**](https://github.com/aungyemun/MultiCamApp/releases/latest) ·
[How to use](#how-to-use) ·
[Documentation](#documentation) ·
[Changelog](CHANGELOG.md) ·
[Cite](#citation)

<img src="docs/images/main_en.png" alt="MultiCamApp main window with a four-camera layout" width="900">

</div>

---

## Why MultiCamApp

MultiCamApp records from **1–4 cameras at once** and keeps the evidence you need to trust the footage: a per-frame timestamp file for every camera, human- and machine-readable metadata, and an offline verification tool that checks every recording after the fact. Everything runs locally — no account, no cloud, no internet connection.

It was developed for behavioural and locomotor activity studies and suits any setting where video has to be reproducible: multi-angle observation, laboratory procedure documentation, teaching recordings and camera set-up checks.

| | |
| --- | --- |
| **Record** | 1–4 cameras, any Windows-compatible webcam (USB or built-in), H.264 encoding through Windows Media Foundation (hardware-accelerated where available) — no discrete GPU required |
| **Time-stamp** | A per-frame timestamp CSV for each camera; real camera frames only, never duplicated or padded |
| **Verify** | Offline checks of frame counts, timing, duplicates and visual stability, with TXT / JSON / CSV reports |
| **Stay private** | Fully offline; output files contain no user names or hardware IDs |

---

## How it works

<p align="center">
  <img src="docs/images/guide/guide_workflow.png" alt="Five steps: install, connect cameras, set up, record, check" width="900">
</p>

---

## Screenshots

Real application pages (v2.1.0, Windows 11). Video preview is turned off, so no camera images appear; the verification pages show two demo sessions recorded for this purpose.

| Main window — 4-camera layout | Advanced camera controls |
| --- | --- |
| ![Main window](docs/images/main_en.png) | ![Advanced camera controls](docs/images/main_advanced_en.png) |
| **Video Verification — sessions found, before a run** | **Video Verification — results after Verify All and Deep Verify** |
| ![Video Verification before a run](docs/images/verify_ready_en.png) | ![Video Verification results](docs/images/verify_results_en.png) |
| **Hardware Diagnostics — system, cameras and USB topology** | **Japanese user interface** |
| ![Hardware Diagnostics](docs/images/hardware_en.png) | ![Japanese user interface](docs/images/verify_results_ja.png) |

---

## Features

### Recording
- **1–4 camera layouts** with per-slot camera, resolution and frame-rate selection.
- **VideoEngineV2** recording engine on Windows Media Foundation; works on integrated graphics.
- **Crash-safe recording** — videos are written as fragmented MP4, which stays playable if the app is closed, crashes or loses power. Interrupted sessions are recovered at the next start with a `RECOVERY_REPORT.txt`.
- **Camera-independent mode selection** — uncompressed formats are preferred; if a mode delivers no frames, the next compatible mode is used and reported.
- **Anti-flicker control** (default *Auto*) against moving horizontal bands under fluorescent or LED light.
- **Manual exposure and focus** that reach the camera (standard UVC webcams included), confirmed by camera read-back and recorded in the metadata.
- **Video quality presets** — Maximum (default, constant bitrate), High, Compact and Efficient — with the expected MB per minute shown.
- **Fast start** — the hardware encoder is prepared during preview, so recording starts in about 0.3 s.

### Timing and data integrity
- **Original Capture Mode** — real camera frames only: no duplicate or placeholder frames to reach a nominal frame rate.
- **Per-frame timestamp CSV** for every camera, one row per encoded frame.
- **Coordinated software start** with timing verification across cameras.
- **Metadata** in TXT (readable) and JSON (machine-readable), written in the UI language selected at recording start.

### Verification
- **Video Verification** page with Simple and Detailed views and per-session results.
- **Deep Verify** — an independent per-frame hash check for duplicate frames, plus a **visual stability audit** (light-flicker bands, brightness flashes, temporal noise).
- **Scientific timing confidence** per session and per camera.
- Reports exported as **TXT, JSON and CSV**.

### Diagnostics and usability
- **Hardware Diagnostics** — CPU, memory, Windows version and graphics adapters; detected cameras; and **USB topology**, showing which USB controller and hub each camera uses, with a warning when cameras share a hub.
- **English and Japanese** user interface.
- **Offline installer** with all runtime components, licences and third-party notices.

---

## Download

Get the installer from the [**latest release**](https://github.com/aungyemun/MultiCamApp/releases/latest) (currently [v2.1.0](https://github.com/aungyemun/MultiCamApp/releases/tag/v2.1.0)).

| File | Use |
| --- | --- |
| `MultiCamApp_2.1.0.339_Setup.exe` | **Recommended** — the installer |
| `installer.zip` | The same installer (as `Setup.exe`) with the main documentation |
| `SHA256SUMS.txt` | SHA-256 checksums of both files |

Verify the download in PowerShell and compare the result with `SHA256SUMS.txt`:

```powershell
Get-FileHash .\MultiCamApp_2.1.0.339_Setup.exe -Algorithm SHA256
```

> [!NOTE]
> The installer is not code-signed, so Windows SmartScreen may ask you to confirm before it runs.

### System requirements

| | |
| --- | --- |
| Operating system | Windows 10 or Windows 11, 64-bit |
| Cameras | 1–4 webcams that work with the Windows Camera app (USB 2.0/3.0 or built-in) |
| Graphics | Any; a discrete GPU is not required |
| Included | .NET runtime, FFmpeg/ffprobe, Microsoft Visual C++ runtime |
| Network | Not required |

For several cameras at 720p or higher, connect them to separate USB ports rather than one hub.

---

## How to use

A step-by-step guide with pictures from the real application. No technical knowledge is needed. Numbered yellow markers in each picture match the list below it. The pictures use demo recordings with the camera preview turned off, and the user name in the save path is shown as `<you>`.

> [!TIP]
> In short: **choose cameras → Start Preview → Start Recording → Stop Recording → Video Verification → Verify All.**

### Step 1 — Install

1. Download **`MultiCamApp_2.1.0.339_Setup.exe`** from the [latest release](https://github.com/aungyemun/MultiCamApp/releases/latest).
2. Double-click it and follow the installer. Everything the app needs is included, and no internet connection is required afterwards.
3. If Windows shows *"Windows protected your PC"*, click **More info → Run anyway** (the installer is not code-signed).
4. Open **MultiCamApp** from the Desktop or the Start Menu.

### Step 2 — Choose your cameras

Plug in your webcams first, ideally each into its own USB port.

![Main window: choosing the camera layout and the camera for each slot](docs/images/guide/guide_1_cameras.png)

### Step 3 — Choose the picture settings

Scroll down the left panel. If you are unsure, keep **720p** and **30 fps**.

![Video settings: resolution, frame rate and optional advanced controls](docs/images/guide/guide_2_quality.png)

### Step 4 — Choose where to save

Recordings go to your **Videos** folder unless you choose another one.

![Output settings: output folder, session title and the folder name that will be created](docs/images/guide/guide_3_save.png)

### Step 5 — Preview, then record

Click **Start Preview** and wait until every camera shows a picture. Then click **Start Recording**, and **Stop Recording** when you are done.

![Recording controls and status panel](docs/images/guide/guide_4_record.png)

> [!NOTE]
> If the computer shuts down or the app closes during a recording, the video stays playable. The app recovers it the next time it starts and writes a `RECOVERY_REPORT.txt`.

### Step 6 — Find your recordings

<p align="center">
  <img src="docs/images/guide/guide_folder.png" alt="Each recording has its own folder with a video, a timestamp file and a details file per camera" width="900">
</p>

Every field is described in [Output Files and Metadata](docs/OUTPUT_FILES_AND_METADATA.md).

### Step 7 — Check your recordings

Open the **Video Verification** tab, choose the folder that holds your recordings and click **Verify All**. **Deep Verify** adds an independent frame-by-frame check and takes longer.

![Video Verification: choosing a folder, Verify All, Deep Verify and export buttons](docs/images/guide/guide_5_verify.png)

### Step 8 — Read the results

The summary at the top gives the result for the whole folder:

![Verification summary: overall result, passed, warnings, failed and timing confidence](docs/images/guide/guide_6_results.png)

<p align="center">
  <img src="docs/images/guide/guide_results.png" alt="What PASS, WARNING and FAIL mean and what to do" width="900">
</p>

Further down, each recording session has its own table with one row per camera:

![Per-session results table with a result for each camera](docs/images/guide/guide_7_sessions.png)

Click **Export TXT**, **Export JSON**, **Export CSV** or **Export all to folder** to keep a copy of the report. See the [Video Verification guide](docs/user_guide/video_verification.md) for every check.

### Optional — Check your computer and cameras

The **Hardware Diagnostics** tab shows your computer, the detected cameras and which USB connection each camera uses. It warns when cameras share a USB hub, which can limit picture size or frame rate.

![Hardware Diagnostics: run a scan, read the advice and the USB connection of each camera](docs/images/guide/guide_8_hardware.png)

### Language, version and citation

Switch between **English** and **日本語** with the **Language** menu at the top right. The **About** button shows the version, licence and the citation to use in publications.

<p align="center">
  <img src="docs/images/guide/guide_9_about.png" alt="About window with version, build number and licence" width="620">
</p>

### Tips for good recordings

- Use bright, even lighting. If you see moving horizontal bands under fluorescent or LED lights, set **Anti-flicker** (under *Advanced Camera Controls*) to your mains frequency or *Auto*.
- Connect each camera to its own USB port rather than one hub, especially at 720p or higher.
- Check focus in the preview before you start recording; focus is locked when recording starts.
- Record a short test first and run **Verify All** on it before an important session.

---

## How timing works

MultiCamApp coordinates cameras **in software** and records how well that worked; it does **not** provide hardware-triggered synchronisation.

- Cameras start through a coordinated software start gate; each may still have a small driver-dependent start offset.
- A camera's real frame rate can differ slightly from the requested one (for example 29.9 fps instead of 30).
- **Original Capture Mode** keeps only the frames the camera actually delivered, so frame counts can differ between cameras.
- These differences are **measured and reported, never hidden**. For timing-sensitive analysis, use the timestamp CSV files and the verification reports as the timing reference.

A recording is in good order when frames captured equal frames written, duplicate and placeholder frames are 0, writer drops are 0, timestamp rows match the frames written, and the timing confidence suits the intended analysis.

---

## Video Verification

The offline **Video Verification** page checks, for every video in a folder:

| Area | Checks |
| --- | --- |
| Integrity | Readability, frame counts, duplicate and placeholder frames, writer drops |
| Timing | Timestamp CSV presence and row count, measured FPS, wall-clock, frame-based and container duration, inter-camera differences, scientific timing confidence |
| Visual stability (Deep Verify) | Light-flicker band strength, brightness flashes, temporal noise |

Results and exports appear after a run. A folder's overall result is **FAIL** if any video fails; the summary states how many sessions failed and why. Details: [Video Verification guide](docs/user_guide/video_verification.md).

---

## Known limitations

<details>
<summary>Show limitations</summary>

- Windows only.
- Recording performance depends on the cameras, USB bandwidth, CPU, memory, disk speed and camera drivers.
- Some webcams deliver a stable frame rate slightly below the requested one (for example 29.x fps instead of 30).
- Multi-camera recordings may have small hardware- or driver-dependent start offsets; synchronisation is software-based, not hardware-triggered.
- Preview frame rate is tuned for checking the cameras; recording quality and timing take priority.
- **Moving horizontal bands** under fluorescent or LED light come from the light flickering at twice the mains frequency. Set *Anti-flicker* to your mains frequency (Japan: east 50 Hz, west 60 Hz) or *Auto*. Some cameras accept but ignore this setting; for those, use non-flickering (DC) lighting.
- Manual exposure on standard USB webcams is limited to power-of-two shutter steps (for example 1/64 s, 1/32 s), which can produce bands; keep auto exposure with anti-flicker unless a fixed shutter is required. A shutter longer than the frame interval lowers the frame rate (the app warns about this).
- Two webcams on one USB 2.0 hub may not both stream 720p or higher.
- Focus is locked at Start Recording at the position reached during preview. Check sharpness in the preview, or set focus manually, before recording.
- The *Efficient* preset smooths sensor noise more than *Maximum*; keep *Maximum* when pixel-level intensity or subtle motion is analysed.
- After an interrupted recording, the last fraction of a second to about one second may be missing, and per-camera metadata files are replaced by the recovery report.
- Not intended for clinical diagnosis, medical decision-making or regulated medical use.

</details>

---

## Responsible use and privacy

MultiCamApp is intended for **lawful, consent-based recording only**. Do not use it for surveillance, harassment, unauthorised recording or any activity that violates the rights, safety or privacy of others. Follow your institution's policies, ethics approvals and local law.

The application works offline and saves everything locally. Output files do not contain user names or hardware IDs, but review videos, screenshots and reports before sharing them.

---

## Documentation

| Topic | Document |
| --- | --- |
| Installation, upgrade and uninstallation | [INSTALLATION.md](INSTALLATION.md) |
| Output files and metadata fields | [docs/OUTPUT_FILES_AND_METADATA.md](docs/OUTPUT_FILES_AND_METADATA.md) |
| Video Verification | [docs/user_guide/video_verification.md](docs/user_guide/video_verification.md) |
| Hardware Diagnostics | [docs/user_guide/hardware_diagnostics.md](docs/user_guide/hardware_diagnostics.md) |
| Security software and antivirus | [docs/user_guide/security_antivirus.md](docs/user_guide/security_antivirus.md) |
| Architecture | [docs/architecture/overview.md](docs/architecture/overview.md) |
| Installer logic | [docs/architecture/installer_logic.md](docs/architecture/installer_logic.md) |
| Repository layout | [DIRECTORY_STRUCTURE.md](DIRECTORY_STRUCTURE.md) |
| Release history | [CHANGELOG.md](CHANGELOG.md) |
| Frozen core and its documented exceptions | [docs/STABLE_CORE_V2_FREEZE.md](docs/STABLE_CORE_V2_FREEZE.md), [docs/STABLE_CORE_V2_EXCEPTIONS.md](docs/STABLE_CORE_V2_EXCEPTIONS.md) |
| Third-party components | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |
| Security policy | [SECURITY.md](SECURITY.md) |
| Contributing | [CONTRIBUTING.md](CONTRIBUTING.md) |

---

## Building from source

The build uses local tools inside the repository rather than global installs. Run `scripts\setup_tools.bat` once (it needs an internet connection) to install the .NET 8 SDK, Python, Inno Setup, FFmpeg and the VC++ runtime into `tools\` and `runtime\`; see [Developer Setup](INSTALLATION.md#developer-setup). Then:

```powershell
. .\env\activate.ps1          # puts the local tools on PATH for this session
dotnet build source\MultiCamApp\MultiCamApp.sln -c Release -p:Platform=x64
dotnet test  source\MultiCamApp\MultiCamApp.sln -c Release -p:Platform=x64 --no-build
installer\build_release.bat   # publishes to dist\ and builds installer\MultiCamApp_<version>_Setup.exe
```

Installer binaries are not committed to the repository; releases are published on [GitHub Releases](https://github.com/aungyemun/MultiCamApp/releases).

---

## Third-party components

The installer bundles FFmpeg/ffprobe, the .NET runtime, the Microsoft Visual C++ runtime and other libraries under their own licences. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the licence files installed with the application.

---

## Citation

If you use MultiCamApp in research, teaching or published work, please cite:

> Mun AY, Koketsu S. **MultiCamApp: Offline multi-camera recording, metadata capture, and video verification platform for research and educational workflows.** Version v2.1.0. 2026.

GitHub's **Cite this repository** button provides the same citation from [`CITATION.cff`](CITATION.cff).

---

## Attribution

- **Shinnosuke Koketsu** — original prototype workflow concept and foundational recording idea.
- **Aung Ye Mun** — application architecture, UI/UX redesign, offline packaging, verification framework, diagnostics, metadata and reporting, localisation, testing, release engineering and maintenance.

---

## License

© 2026 Aung Ye Mun and contributors.

MultiCamApp is available for **non-commercial academic, scientific, educational, evaluation and personal use**. Commercial use, redistribution, resale, sublicensing, SaaS hosting, monetised distribution or commercial incorporation requires prior written permission from the copyright holder. See [LICENSE.md](LICENSE.md) for the full terms.
