# MultiCamApp Third-Party Notices

MultiCamApp may bundle, redistribute, or depend on third-party software components as part of the published Windows app and installer.

This file is intended to provide release notice information for third-party components used by MultiCamApp. Third-party software remains the property of its respective authors and copyright holders.

## .NET Runtime and Windows Desktop Runtime

MultiCamApp may be published as a self-contained `win-x64` application and may include Microsoft .NET runtime and Windows Desktop runtime components.

Relevant notice and metadata files may include:

* `tools/dotnet/ThirdPartyNotices.txt`
* `tools/dotnet/shared/Microsoft.AspNetCore.App/8.0.27/THIRD-PARTY-NOTICES.txt`
* `tools/dotnet/shared/Microsoft.WindowsDesktop.App/8.0.27/THIRD-PARTY-NOTICES.txt`
* `tools/nuget-packages/microsoft.netcore.app.runtime.win-x64/8.0.27/THIRD-PARTY-NOTICES.TXT`
* `tools/nuget-packages/microsoft.net.illink.tasks/8.0.27/THIRD-PARTY-NOTICES.TXT`

## OpenCvSharp and OpenCV

MultiCamApp uses OpenCvSharp managed and native packages for live camera preview, camera access, recording support, and diagnostics.

Package metadata may include:

* `tools/nuget-packages/opencvsharp4/4.10.0.20240616/opencvsharp4.nuspec`
* `tools/nuget-packages/opencvsharp4.runtime.win/4.10.0.20240616/opencvsharp4.runtime.win.nuspec`

OpenCvSharp packages are distributed under the Apache License 2.0 according to their NuGet metadata. OpenCvSharp may include or depend on OpenCV native binaries redistributed through the OpenCvSharp runtime package.

Relevant redistributed runtime files may include:

* `dist/OpenCvSharp.dll`
* `dist/OpenCvSharpExtern.dll`
* `dist/opencv_videoio_ffmpeg4100_64.dll`

For upstream OpenCV license and attribution requirements, see the OpenCvSharp package metadata and the OpenCV project license terms referenced by that package.

## FFmpeg / ffprobe

MultiCamApp may bundle `ffprobe.exe` for offline video verification.

Bundled runtime files may include:

* `dist/runtime/ffmpeg/ffprobe.exe`

Reference material may include:

* `runtime/ffmpeg/README.txt`
* `dist/runtime/ffmpeg/README.txt`

FFmpeg and ffprobe are third-party tools. Their licensing may depend on the specific build and configuration used. Before public release, confirm that the bundled `ffprobe.exe` build, license terms, and required notices are included with the release package.

## Python

The repository may include a local Python toolchain for build, validation, and release scripts. Python is a developer/build tool and is not required for normal installed-app use unless explicitly bundled in a release workflow.

Reference files may include:

* `tools/python/LICENSE.txt`
* `tools/python/Lib/site-packages/pip-26.1.1.dist-info/licenses/LICENSE.txt`
* `tools/python/Lib/site-packages/pip/_vendor/*/LICENSE*`

## Bundled NuGet Packages

The release process may redistribute or stage runtime assets from these package families:

* `OpenCvSharp4`
* `OpenCvSharp4.runtime.win`
* `Microsoft.NETCore.App.Runtime.win-x64`
* `Microsoft.AspNetCore.App.Runtime.win-x64`
* `Microsoft.WindowsDesktop.App.Runtime.win-x64`
* `Microsoft.NET.ILLink.Tasks`
* `System.Management`
* `System.CodeDom`
* `System.Memory`
* `System.Runtime.CompilerServices.Unsafe`
* `System.Windows.Extensions`

For authoritative license and notice text, consult the package-specific `LICENSE*`, `THIRD-PARTY-NOTICES*`, and `.nuspec` metadata stored under `tools/nuget-packages/` and `tools/dotnet/`.

## Release Packaging Note

Published release artifacts should include, where applicable:

* `LICENSE.txt`
* `THIRD_PARTY_NOTICES.md`
* `README.md`
* `INSTALLATION.md`
* Required third-party license and notice files

If any bundled dependency is updated, its license and notice references should be reviewed before release.

The generated `dist/THIRD_PARTY_NOTICES.md` should match this root notice file at release time.
