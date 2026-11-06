# MultiCamApp Third-Party Notices

MultiCamApp bundles or redistributes third-party software components. This file is the release notice reference for the published app and installer.

## .NET Runtime and Windows Desktop Runtime

MultiCamApp is published as a self-contained `win-x64` application and includes Microsoft .NET runtime and Windows Desktop runtime components.

- Source references:
  - `tools/dotnet/ThirdPartyNotices.txt`
  - `tools/dotnet/shared/Microsoft.AspNetCore.App/8.0.27/THIRD-PARTY-NOTICES.txt`
  - `tools/nuget-packages/microsoft.net.illink.tasks/8.0.27/THIRD-PARTY-NOTICES.TXT`
- Version: .NET 8 runtime **8.0.27** (bundled SDK 8.0.421, `tools/dotnet/shared/*/8.0.27`). The self-contained runtime packs (`Microsoft.NETCore.App.Runtime.win-x64`, `Microsoft.WindowsDesktop.App.Runtime.win-x64`) are restored from NuGet when `scripts/build/build_release.ps1` publishes; they are not stored in `tools/nuget-packages/`.

## OpenCvSharp and OpenCV

MultiCamApp uses OpenCvSharp (4.10.0.20240616) for the legacy (STABLE_CORE_V1) capture pipeline, DirectShow camera-index mapping and startup diagnostics. The default recording engine (VideoEngineV2) does not use OpenCV: it captures through Windows Media Capture and encodes with Windows Media Foundation (see Vortice.Windows below).

- Package metadata:
  - `tools/nuget-packages/opencvsharp4/4.10.0.20240616/opencvsharp4.nuspec`
  - `tools/nuget-packages/opencvsharp4.runtime.win/4.10.0.20240616/opencvsharp4.runtime.win.nuspec`

OpenCvSharp packages are distributed under the Apache 2.0 license according to their NuGet metadata. OpenCvSharp depends on OpenCV native binaries, which are redistributed through the OpenCvSharp runtime package.

- Relevant redistributed runtime files:
  - `dist/OpenCvSharpExtern.dll`
  - `dist/opencv_videoio_ffmpeg4100_64.dll`

The managed OpenCvSharp assembly is bundled inside the single-file `MultiCamApp.exe` release apphost. Native OpenCV runtime files remain beside the executable.

For upstream OpenCV license and attribution requirements, see the OpenCvSharp package metadata and OpenCV project license terms referenced by that package.

The `opencv_videoio_ffmpeg4100_64.dll` runtime component is included through the OpenCvSharp/OpenCV runtime package and provides FFmpeg-based video I/O support for OpenCV. Its licensing and redistribution terms should be interpreted together with the OpenCvSharp/OpenCV package notices and the FFmpeg-related notices included in this file. MultiCamApp documents this file separately because FFmpeg-related video I/O components may carry additional license obligations.

## FFmpeg / ffprobe / ffmpeg

MultiCamApp bundles `ffprobe.exe` for offline video verification, video/container metadata inspection and recovery of interrupted recordings, and `ffmpeg.exe` for the on-demand "Deep Verify" checks on the Video Verification page — the independent per-frame MD5 duplicate-frame check and the visual stability audit (light-flicker bands, brightness flashes, temporal noise). Deep Verify is opt-in only and never runs automatically.

Bundled version at the time of this release: FFmpeg 8.1.1 "essentials" build (as reported by `ffmpeg -version`).

The bundled `ffprobe.exe`/`ffmpeg.exe` binaries are from the gyan.dev Windows FFmpeg build:

> https://www.gyan.dev/ffmpeg/builds/

The bundled build reports the configuration flags `--enable-gpl --enable-version3`. MultiCamApp therefore treats the bundled `ffprobe.exe` and `ffmpeg.exe` as licensed under the GNU General Public License version 3 (GPL v3) for distribution notice purposes.

FFmpeg and ffprobe are developed by the FFmpeg project contributors.

Source code and license information:

- FFmpeg project: https://ffmpeg.org/
- FFmpeg source code: https://ffmpeg.org/download.html
- gyan.dev FFmpeg builds: https://www.gyan.dev/ffmpeg/builds/
- gyan.dev build scripts and patches: https://github.com/GyanD/codexffmpeg
- GPL v3 license text: https://www.gnu.org/licenses/gpl-3.0.txt

MultiCamApp uses `ffprobe.exe` and `ffmpeg.exe` only as external standalone executables, invoked as separate processes for post-recording video metadata inspection and (for `ffmpeg.exe`) per-frame hashing and the visual stability audit. Recording itself does not use FFmpeg. No FFmpeg source code was modified. Neither binary is linked into the `MultiCamApp.exe` binary.

Bundled runtime files and references:

- `dist/runtime/ffmpeg/ffprobe.exe`
- `dist/runtime/ffmpeg/ffmpeg.exe`
- `runtime/ffmpeg/README.txt`
- `dist/runtime/ffmpeg/README.txt`
- `runtime/ffmpeg/FFMPEG_LICENSE.txt`
- `dist/runtime/ffmpeg/FFMPEG_LICENSE.txt`

## Vortice.Windows (Direct3D11, DXGI, MediaFoundation)

MultiCamApp uses the Vortice.Windows managed bindings for GPU-accelerated live preview rendering (`Vortice.Direct3D11`, `Vortice.DXGI`) and for the raw Media Foundation H.264 recording pipeline (`Vortice.MediaFoundation`), all version 3.8.3.

- Author: Amer Koleci
- License: MIT (`https://licenses.nuget.org/MIT`)
- Project: https://github.com/amerkoleci/Vortice.Windows
- Package metadata: `tools/nuget-packages/vortice.direct3d11/3.8.3/vortice.direct3d11.nuspec`, `tools/nuget-packages/vortice.dxgi/3.8.3/vortice.dxgi.nuspec`, `tools/nuget-packages/vortice.mediafoundation/3.8.3/vortice.mediafoundation.nuspec`, `tools/nuget-packages/vortice.directx/3.8.3/vortice.directx.nuspec` (transitive dependency)

The managed Vortice assemblies are bundled inside the single-file `MultiCamApp.exe` release apphost, same as OpenCvSharp above.

## Python

Python is used only as a developer/build tool and is not included in the normal installed MultiCamApp release unless a specific release package explicitly states otherwise.

- Bundled interpreter: Python 3.12.10 (`tools/python/`), with pip 26.1.2 and Pillow 12.2.0 (Pillow is used only by `scripts/build/build_app_icon.py`). The real-camera test harness (`scripts/autotest/`) uses the standard library only.
- Reference files:
  - `tools/python/LICENSE.txt`
  - `tools/python/Lib/site-packages/pip-26.1.2.dist-info/licenses/LICENSE.txt`
  - `tools/python/Lib/site-packages/pip/_vendor/*/LICENSE*`
  - `tools/python/Lib/site-packages/pillow-12.2.0.dist-info/licenses/LICENSE` (MIT-CMU)

## xUnit

MultiCamApp's unit test suite (`MultiCamApp.Tests`) uses xUnit as its test framework.

- Package: `xunit` v2.9.3, `xunit.runner.visualstudio` v2.8.2, `Microsoft.NET.Test.Sdk` v17.14.1
- License: Apache 2.0 (xUnit); see `tools/nuget-packages/xunit/` for package metadata
- xUnit is a developer/build-time dependency only. It is not shipped in the installed application or installer package.

## Bundled NuGet Packages

The release process may redistribute or stage runtime assets from these package families:

**Shipped in release (`dist\`)** — versions as resolved in the app's `MultiCamApp.deps.json`:

- `OpenCvSharp4` 4.10.0.20240616, `OpenCvSharp4.runtime.win` 4.10.0.20240616
- `Vortice.Direct3D11` 3.8.3, `Vortice.DXGI` 3.8.3, `Vortice.MediaFoundation` 3.8.3
- `Vortice.DirectX` 3.8.3, `Vortice.Mathematics` 2.1.0, `SharpGen.Runtime` 2.4.2-beta, `SharpGen.Runtime.COM` 2.4.2-beta (dependencies of the above)
- `System.Management` 10.0.14, `System.CodeDom` 10.0.14
- `System.Text.Json` 9.0.1, `System.Text.Encodings.Web` 9.0.1, `System.IO.Pipelines` 9.0.1
- `System.Memory` 4.5.5, `System.Runtime.CompilerServices.Unsafe` 6.0.0
- .NET 8 runtime packs `Microsoft.NETCore.App.Runtime.win-x64` and `Microsoft.WindowsDesktop.App.Runtime.win-x64` (8.0.27, self-contained publish)

**Build-time only:** `Microsoft.NET.ILLink.Tasks` 8.0.27 (publish trimming/analysis tasks).

**Developer/build-time only (not shipped in release):**

- `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` — unit test framework and runner
- `Microsoft.Windows.SDK.Net.Ref` — Windows SDK reference assembly used at build time
- `Newtonsoft.Json`, `System.Reflection.Metadata` — transitive dependencies of test tooling

For the authoritative license and notice text, consult the package-specific `LICENSE*`, `THIRD-PARTY-NOTICES*`, and `.nuspec` metadata stored under `tools/nuget-packages/` and `tools/dotnet/`.

## Inno Setup

MultiCamApp's installer (`Setup.exe`) is built with Inno Setup, a free installer system for Windows programs.

- Copyright © 1997–2026 Jordan Russell. All rights reserved.
- Portions copyright © 2000–2026 Martijn Laan. All rights reserved.
- License: Inno Setup License. `installer/build_release.bat` uses an installed Inno Setup 7 or 6 (Inno Setup 6 on the build machine at this release); the full license text (`license.txt`) is in that installation folder. `tools/inno/` holds only a copy of `ISCC.exe` (copied by `scripts/maintenance/audit_environment.ps1`), used as a last fallback.
- Homepage: https://jrsoftware.org/isinfo.php

The Inno Setup compiler is used to produce `installer/MultiCamApp_<version>_Setup.exe` and is not redistributed to end users as part of the installed application. The compiled Setup.exe contains Inno Setup runtime components as is standard for Inno Setup-built installers.

## Microsoft Visual C++ Redistributable

The installer (`Setup.exe`) bundles and installs the Microsoft Visual C++ 2015–2022 Redistributable (x64) to satisfy runtime dependencies of OpenCV and related native components.

- File: `runtime/vc_runtime/vc_redist.x64.exe`, version 14.44.35211.0, bundled with the installer and installed during setup if needed.
- License: Microsoft Software License Terms for Visual C++ Redistributable Packages
- Reference: https://docs.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files

## Release Packaging Note

This project’s release artifacts should include:

- `LICENSE.md`
- `THIRD_PARTY_NOTICES.md`
- `README.md`
- `INSTALLATION.md`
- `runtime/ffmpeg/FFMPEG_LICENSE.txt`

If any bundled dependency is updated, its license and notice references should be reviewed before release.

The generated `dist\THIRD_PARTY_NOTICES.md` should match this root notice file at release time.
