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

1. **Wizard initialization:** Shows the license flow and install location. If an existing install is detected at the target folder, the wizard shows an upgrade notice on the Ready page.
2. **Upgrade cleanup (upgrade only):** Backs up `config\version.json`, `appsettings.json`, and installer logs to `{app}\backup_before_update\<timestamp>\`, then removes the previous version's managed folders/files (`[InstallDelete]`) before new files are copied. Logs, `backup_before_update\`, `user_settings\`, `config_user\`, and any video files are preserved.
3. **VC++ runtime:** Checks the registry (`HKLM64\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64`, `Installed`) for an existing VC++ 2015-2022 x64 runtime first. If already present, the bundled `vc_redist.x64.exe` is **not launched at all** (no duplicate install). Otherwise it runs `vc_redist.x64.exe /quiet /norestart` and logs the result (exit 1638 = already installed, also treated as success).
4. **File extraction:** Installs the app executable, native DLLs, config, localization, and runtime tools.
5. **Shortcuts:** Any existing Desktop shortcut is always removed first, then recreated only if the `desktopicon` task is selected. If the `startmenuicon` task is deselected, any pre-existing Start Menu group is removed; otherwise its shortcuts (app, uninstaller, diagnostic launchers) are (re)created — so upgrading always leaves shortcuts consistent with the currently selected tasks, never a stale/broken leftover from a prior version.
6. **Runtime setup:** Runs `setup_runtime.bat` to verify OpenCV and FFmpeg files.
7. **Final validation:** Confirms `MultiCamApp.exe`, critical native DLLs, `ffprobe.exe`/`ffmpeg.exe`, and runtime logs. Then runs `MultiCamApp.exe --smoke-test`.

The installer should wait for runtime setup and validation before showing success. Shortcuts should target `{app}\MultiCamApp.exe` and use `{app}` as the working directory.

No other system-level installer runs alongside Setup.exe — the .NET 8 runtime is bundled into the self-contained app (no separate .NET install), and the VC++ Redistributable above is the only external runtime dependency.

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

Uninstallation is user-data safe: the entire `{app}` install folder is removed recursively (`[UninstallDelete]`: `Type: filesandordirs; Name: "{app}"`), including the folder itself, plus the Start Menu group and Desktop shortcut.

Removed:

- Every file and subfolder under the install folder — app binaries, native DLLs, config, localization, bundled runtime tools, logs, and the install folder itself
- App shortcuts (Desktop and Start Menu)

Preserved:

- Recorded videos and recording session folders (default `%USERPROFILE%\Videos`, or a user-chosen folder — see `OutputFolderManager.ResolveBaseFolder`/`PathHelper.DefaultVideosFolder`, never under `{app}`)
- Exported CSV/JSON/TXT reports
- User project folders

The VC++ Runtime is left installed because other applications may use it.
