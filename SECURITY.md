# Security Policy

## Supported Versions

| Version | Status        |
|---------|---------------|
| 2.0.x   | Active        |
| < 2.0.0 | Not supported |

## Reporting a Vulnerability

To report a security vulnerability in MultiCamApp, contact the maintainer directly at **aungyemun25@gmail.com**.

Do not open a public GitHub issue for security vulnerabilities. Include a description of the issue, steps to reproduce, and any relevant log files (with personal paths redacted).

## Responsible Use

MultiCamApp is intended for lawful, consent-based recording only. Unauthorized or covert recording is outside the intended and permitted use.

## Scope

MultiCamApp is a fully offline desktop application for Windows. It does not connect to the internet, does not transmit user data, and does not expose network services. Relevant security areas are:

- **Installation integrity** — Setup.exe is produced by Inno Setup from verified source. Code signing is recommended but not currently applied.
- **Privacy-safe metadata output** — exported metadata.txt and metadata.json files do not contain absolute paths, hardware identifiers, or computer names. See `THIRD_PARTY_NOTICES.md` and `docs\OUTPUT_FILES_AND_METADATA.md`.
- **Bundled third-party components** — ffprobe, OpenCV, and the .NET runtime are bundled. See below.

## Bundled Third-Party Components

| Component | License | Notes |
|-----------|---------|-------|
| ffprobe.exe | GPL v3 | Standalone exe; not linked into MultiCamApp.exe |
| OpenCvSharp / OpenCV | Apache 2.0 | Native DLLs beside the app |
| Vortice.Windows (Direct3D11, DXGI, MediaFoundation) | MIT | Managed bindings bundled inside the single-file apphost |
| .NET 8 Runtime | MIT | Self-contained bundle |
| Microsoft VC++ Redistributable | Microsoft License | Installed by Setup.exe |

See `THIRD_PARTY_NOTICES.md` and `dist\runtime\ffmpeg\FFMPEG_LICENSE.txt` for full details.

## Out of Scope

- Vulnerabilities in cameras or drivers connected by the user
- Issues introduced by running the app in a non-standard environment
- Hardware Diagnostics results (advisory only, do not affect recording behavior)
