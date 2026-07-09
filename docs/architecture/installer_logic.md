# MultiCamApp Installer Architecture

This document describes the offline installer, release folder, and troubleshooting flow for MultiCamApp.

## Overview

MultiCamApp uses a self-contained, offline-first Windows installation strategy. The app is published as a self-contained .NET 8 single-file executable with the native files it needs beside it.

Key components:

- **Inno Setup 7:** Builds `Setup.exe`.
- **VC++ Redistributable:** Bundled and installed silently because native camera/video components need it.
- **OpenCV native runtime:** `OpenCvSharpExtern.dll` and related native video libraries.
- **FFmpeg tools:** Bundled under `runtime\ffmpeg`; `ffprobe.exe` is used by Video Verification.
- **Smoke tests:** Used during release build and installer final validation.

## Release Build

Use:

```powershell
installer\build_release.bat
```

The release build populates `dist\`, runs .NET tests, publishes the app, validates required runtime files, builds `Setup.exe`, and writes `installer\build_release_summary.txt`.

Expected key outputs:

```text
dist\
  MultiCamApp.exe
  OpenCvSharpExtern.dll
  config\
  runtime\
    ffmpeg\
      ffprobe.exe

installer\
  MultiCamApp_{version}_Setup.exe
  installer.zip
  build_release_summary.txt
```

`OutputDir` in `MultiCamApp.iss` is `.` — the compiled installer is written directly to `installer\`, not to an `Output\` subfolder, and is named `MultiCamApp_{version}_Setup.exe` (e.g. `MultiCamApp_2.0.1.334_Setup.exe`), not a bare `Setup.exe`.

## Installation Flow

1. **Wizard initialization:** Shows the license flow and install location.
2. **VC++ runtime:** Extracts and runs `vc_redist.x64.exe /quiet /norestart`, then logs the result.
3. **File extraction:** Installs the app executable, native DLLs, config, localization, and runtime tools.
4. **Runtime setup:** Runs `setup_runtime.bat` to verify OpenCV and FFmpeg files.
5. **Final validation:** Confirms `MultiCamApp.exe`, critical native DLLs, `ffprobe.exe`, and runtime logs. Then runs `MultiCamApp.exe --smoke-test`.

The installer should wait for runtime setup and validation before showing success. Shortcuts should target `{app}\MultiCamApp.exe` and use `{app}` as the working directory.

## Installed Components

| Component | Destination | Purpose |
| :--- | :--- | :--- |
| `MultiCamApp.exe` | `{app}\` | Main app entry point |
| OpenCV native DLLs | `{app}\` | Camera capture, preview, and video I/O |
| `ffprobe.exe` | `{app}\runtime\ffmpeg\` | Video Verification metadata extraction |
| Config/localization files | `{app}\config\` | App settings and language resources |
| VC++ Runtime | System runtime | Required by native components |

## First-Launch Protection

At startup, the app runs `StartupDiagnostics` before opening the main window. It verifies critical files and writes `logs\startup_diagnostics.json`. If a required file was deleted, quarantined, or blocked, the app shows a clear startup error with the relevant log path.

The diagnostics must stay privacy-safe. Do not write Windows usernames, computer names, hardware serial numbers, MAC addresses, hardware IDs, or full user profile paths.

## Offline Troubleshooting

If the app fails to launch on an offline machine:

1. Re-run `Setup.exe` to repair missing files and VC++ runtime registration.
2. Check `{app}\logs\startup_diagnostics.json`.
3. Check `{app}\logs\crash.log` if the app produced a startup error dialog.
4. Confirm antivirus did not quarantine `OpenCvSharpExtern.dll` or FFmpeg/OpenCV native files.
5. Confirm shortcuts use `{app}` as the working directory.

Common causes:

- VC++ Redistributable failed or another installer was already running.
- Antivirus blocked native video DLLs.
- Install folder was partially copied by hand instead of installed.
- Shortcut target or working directory points to the wrong folder.

## Uninstallation

Uninstallation is user-data safe.

Removed:

- App binaries and native DLLs
- Bundled runtime tools
- App shortcuts
- App-local logs

Preserved:

- Recorded videos
- Recording session folders
- Exported CSV/JSON/TXT reports
- User project folders

The VC++ Runtime is left installed because other applications may use it.
