# MultiCamApp Installation And Development Guide

MultiCamApp is a self-contained, offline-capable Windows application. This guide covers end-user installation, offline install testing, developer setup, dependencies, and troubleshooting.

## End-User Installation

Download `Setup.exe` or `installer.zip` from the [GitHub Releases](../../releases) page. Installer binaries are not committed to the source repository.

The installer is the recommended release artifact. It includes the app bundle, native camera/video dependencies, FFmpeg `ffprobe`, config/localization files, third-party notices, license text, runtime setup scripts, and the Microsoft Visual C++ Redistributable.

Setup.exe does the following:

1. Installs or verifies the VC++ Runtime from the bundled `vc_redist.x64.exe`.
2. Copies the self-contained `MultiCamApp.exe` app bundle and required native DLLs.
3. Copies `runtime\ffmpeg\ffprobe.exe` for offline Video Verification.
4. Copies config, localization, icons, license, and third-party notices.
5. Runs `runtime\setup_runtime.bat`.
6. Runs `MultiCamApp.exe --smoke-test` for offline compatibility validation.
7. Creates Desktop and Start Menu shortcuts.

No internet connection is required during installation or normal app use.

## Uninstallation

Use **Uninstall MultiCamApp** from the Start Menu or Windows Control Panel.

Removed:

- Application executable and native DLLs
- Bundled runtime tools
- App shortcuts
- App-local logs

Preserved:

- Recorded videos
- Recording session folders
- Exported CSV/JSON/TXT reports
- User project folders

## Video Verification

After recording, open **Video Verification** and select the session folder that contains `cam1`, `cam2`, etc. Verification runs offline using bundled `ffprobe`, per-camera metadata, and Timestamp CSV files.

Important terms:

| Term | Meaning |
| :--- | :--- |
| Original Capture Mode | Preserves real camera frames only |
| Real Capture FPS | Real measured camera FPS |
| Playback FPS | MP4/container playback frame rate |
| Timestamp CSV | Scientific timing source |
| Writer drops | Writer queue drops |
| Frame integrity | Real frames only; no duplicates/placeholders |

Use timestamp CSV for timing-sensitive analysis. Container duration differs from wall-clock time. Use timestamp CSV for scientific trimming and analysis.

See [Video Verification](docs/user_guide/video_verification.md).

## Hardware Diagnostics

Hardware Diagnostics are advisory and privacy-safe. They help assess camera visibility, USB / Camera Connection, graphics driver status, and 3/4-camera recording risk. They do not change recording behavior.

If USB topology is unavailable, the app uses advisory wording:

```text
USB topology unavailable. For unstable 3-4 camera recording, try separate USB ports, a powered USB hub, or lower resolution.
```

See [Hardware Diagnostics](docs/user_guide/hardware_diagnostics.md).

## Offline Installation Test

Use this procedure before sending a release to another machine.

Test machine requirements:

- Windows 10/11 x64
- No internet connection
- No required developer tools pre-installed
- Do not rely on system FFmpeg, Python, .NET SDK, or Visual Studio

Procedure:

1. Download `Setup.exe` or `installer.zip` from GitHub Releases and copy to the test machine.
2. Run `Setup.exe`.
3. Confirm VC++ Runtime setup completes.
4. Confirm bundled runtime setup completes.
5. Confirm installer smoke test passes or only reports a non-critical warning.
6. Launch MultiCamApp from Desktop or Start Menu.
7. Open **Video Verification** and verify a known recorded session folder.
8. Optional: open **Hardware Diagnostics** and run **Run Hardware Scan**.

Expected result:

- The app launches offline.
- Video Verification runs using bundled `ffprobe`.
- Hardware Diagnostics can write advisory JSON reports offline.
- Stable 29.684 FPS cameras with clean metadata are reported as `PASS_ORIGINAL_TIMING_WITH_NOTE`, not FAIL.

## Developer Setup

MultiCamApp uses local tools inside this repository so release builds do not depend on global PATH configuration.

Run once:

```bat
scripts\setup_tools.bat
```

This installs or refreshes:

- `tools\dotnet\` - local .NET SDK
- `tools\python\` - Python for build and audit scripts
- `tools\inno\` - Inno Setup compiler
- `tools\nuget-packages\` - NuGet package cache
- `runtime\ffmpeg\` - FFmpeg tools
- `runtime\vc_runtime\` and `installer\vc_redist.x64.exe` - VC++ Redistributable

## Build Release

Use:

```bat
installer\build_release.bat
```

Expected outputs:

```text
dist\MultiCamApp.exe
dist\OpenCvSharpExtern.dll
dist\opencv_videoio_ffmpeg4100_64.dll
dist\runtime\ffmpeg\ffprobe.exe
installer\Setup.exe
installer\build_release_summary.txt
```

Export the shareable ZIP:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\packaging\create_release_zip.ps1 -DestinationZip installer\installer.zip
```

The ZIP contains `Setup.exe`, `README.md`, `INSTALLATION.md`, `DIRECTORY_STRUCTURE.md`, `THIRD_PARTY_NOTICES.md`, `LICENSE.md`, and key user-guide docs.

## Shipped Runtime Dependencies

| Component | Included where | Purpose |
| :--- | :--- | :--- |
| .NET 8 runtime | bundled into self-contained app | Runs the WPF app without system .NET |
| OpenCvSharp / OpenCV native runtime | `dist\OpenCvSharpExtern.dll` and related native files | Camera preview/capture/video I/O |
| FFmpeg ffprobe | `dist\runtime\ffmpeg\ffprobe.exe` | Offline Video Verification |
| VC++ Redistributable | `installer\vc_redist.x64.exe` inside Setup.exe | Required by native components |
| Config/localization | `dist\config\`, `dist\localization\` | App settings and UI language files |

Developer-only tools in `tools\` are not installed with the app.

## Troubleshooting

If MultiCamApp fails to start after installation:

1. Run **MultiCamApp Diagnostic Launch** from the Start Menu.
2. Check `{app}\logs\startup_diagnostics.json`.
3. Check `{app}\logs\crash.log` if a startup error dialog appeared.
4. Re-run `Setup.exe` if files are missing.
5. Check antivirus quarantine for `MultiCamApp.exe`, `OpenCvSharpExtern.dll`, or FFmpeg/OpenCV native files.
6. Confirm the shortcut target is `{app}\MultiCamApp.exe` and the working directory is `{app}`.

Do not ask users to disable antivirus. If a tool flags the app, verify signatures/notices, reinstall from the official package, and review logs.

## Citation

Mun AY. MultiCamApp: Offline multi-camera synchronized recording and verification platform for behavioral analysis. Version v1.1.0. 2026.

## Attribution

Original prototype workflow concept and foundational recording logic:
Shinnosuke Koketsu

Modern application architecture, UI/UX redesign, offline packaging, verification framework, diagnostics, metadata/reporting, localization, testing, release engineering, and maintenance:
Aung Ye Mun

## License

© 2026–Present Aung Ye Mun and contributors. Non-commercial research and educational use only.
See [LICENSE.md](LICENSE.md).
