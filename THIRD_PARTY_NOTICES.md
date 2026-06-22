# MultiCamApp Third-Party Notices

MultiCamApp bundles or redistributes third-party software components. This file is the release notice reference for the published app and installer.

## .NET Runtime and Windows Desktop Runtime

MultiCamApp is published as a self-contained `win-x64` application and includes Microsoft .NET runtime and Windows Desktop runtime components.

- Source references:
  - `tools/dotnet/ThirdPartyNotices.txt`
  - `tools/dotnet/shared/Microsoft.AspNetCore.App/8.0.27/THIRD-PARTY-NOTICES.txt`
  - `tools/dotnet/shared/Microsoft.WindowsDesktop.App/8.0.27/THIRD-PARTY-NOTICES.txt`
  - `tools/nuget-packages/microsoft.netcore.app.runtime.win-x64/8.0.27/THIRD-PARTY-NOTICES.TXT`
  - `tools/nuget-packages/microsoft.net.illink.tasks/8.0.27/THIRD-PARTY-NOTICES.TXT`

## OpenCvSharp and OpenCV

MultiCamApp uses OpenCvSharp managed and native packages for live camera preview, DirectShow access, recording support, and diagnostics.

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

## FFmpeg / ffprobe

MultiCamApp bundles `ffprobe.exe` for offline video verification and video/container metadata inspection.

The bundled `ffprobe.exe` binary is from the gyan.dev Windows FFmpeg build:

> https://www.gyan.dev/ffmpeg/builds/

The bundled build reports the configuration flags `--enable-gpl --enable-version3`. MultiCamApp therefore treats the bundled `ffprobe.exe` as licensed under the GNU General Public License version 3 (GPL v3) for distribution notice purposes.

FFmpeg and ffprobe are developed by the FFmpeg project contributors.

Source code and license information:

- FFmpeg project: https://ffmpeg.org/
- FFmpeg source code: https://ffmpeg.org/download.html
- gyan.dev FFmpeg builds: https://www.gyan.dev/ffmpeg/builds/
- gyan.dev build scripts and patches: https://github.com/GyanD/codexffmpeg
- GPL v3 license text: https://www.gnu.org/licenses/gpl-3.0.txt

MultiCamApp uses `ffprobe.exe` only as an external standalone executable for post-recording video metadata inspection. No FFmpeg source code was modified. `ffprobe.exe` is not linked into the `MultiCamApp.exe` binary.

Bundled runtime files and references:

- `dist/runtime/ffmpeg/ffprobe.exe`
- `runtime/ffmpeg/README.txt`
- `dist/runtime/ffmpeg/README.txt`
- `runtime/ffmpeg/FFMPEG_LICENSE.txt`
- `dist/runtime/ffmpeg/FFMPEG_LICENSE.txt`

## Python

Python is used only as a developer/build tool and is not included in the normal installed MultiCamApp release unless a specific release package explicitly states otherwise.

- Reference files:
  - `tools/python/LICENSE.txt`
  - `tools/python/Lib/site-packages/pip-26.1.1.dist-info/licenses/LICENSE.txt`
  - `tools/python/Lib/site-packages/pip/_vendor/*/LICENSE*`

## xUnit

MultiCamApp's unit test suite (`MultiCamApp.Tests`) uses xUnit as its test framework.

- Package: `xunit` v2.9.2, `xunit.runner.visualstudio` v2.8.2, `Microsoft.NET.Test.Sdk` v17.12.0
- License: Apache 2.0 (xUnit); see `tools/nuget-packages/xunit/` for package metadata
- xUnit is a developer/build-time dependency only. It is not shipped in the installed application or installer package.

## Bundled NuGet Packages

The release process may redistribute or stage runtime assets from these package families:

**Shipped in release (`dist\`):**

- `OpenCvSharp4`
- `OpenCvSharp4.runtime.win`
- `Microsoft.NETCore.App.Runtime.win-x64`
- `Microsoft.AspNetCore.App.Runtime.win-x64`
- `Microsoft.WindowsDesktop.App.Runtime.win-x64`
- `Microsoft.NET.ILLink.Tasks`
- `System.Management`
- `System.CodeDom`
- `System.Memory`
- `System.Runtime.CompilerServices.Unsafe`
- `System.Windows.Extensions`

**Developer/build-time only (not shipped in release):**

- `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` — unit test framework and runner
- `Microsoft.Windows.SDK.Net.Ref` — Windows SDK reference assembly used at build time
- `Newtonsoft.Json`, `System.Reflection.Metadata` — transitive dependencies of test tooling

For the authoritative license and notice text, consult the package-specific `LICENSE*`, `THIRD-PARTY-NOTICES*`, and `.nuspec` metadata stored under `tools/nuget-packages/` and `tools/dotnet/`.

## Inno Setup

MultiCamApp's installer (`Setup.exe`) is built with Inno Setup, a free installer system for Windows programs.

- Copyright © 1997–2026 Jordan Russell. All rights reserved.
- Portions copyright © 2000–2026 Martijn Laan. All rights reserved.
- License: Inno Setup License. See `tools/inno/license.txt` for the full license terms.
- Homepage: https://jrsoftware.org/isinfo.php

The Inno Setup compiler and runtime files (`tools/inno/`) are used to produce `installer/Setup.exe` and are not redistributed to end users as part of the installed application. The compiled Setup.exe contains Inno Setup runtime components as is standard for Inno Setup-built installers.

## Microsoft Visual C++ Redistributable

The installer (`Setup.exe`) bundles and installs the Microsoft Visual C++ 2015–2022 Redistributable (x64) to satisfy runtime dependencies of OpenCV and related native components.

- File: `vc_redist.x64.exe` is bundled inside or alongside the installer package and may be installed during setup to satisfy native runtime dependencies.
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
