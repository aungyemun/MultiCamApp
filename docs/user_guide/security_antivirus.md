# MultiCamApp Security & Antivirus Guide

MultiCamApp is designed to be a transparent, secure, and offline-capable Windows application. It follows standard desktop application practices and does not require weakening your system's security.

## For End Users

- **No Exclusions Required**: You do **not** need to disable your antivirus or add manual exclusions for MultiCamApp.
- **Explicit User Action**: Camera access and recording only start when you explicitly click the **Start Preview** or **Start Recording** buttons.
- **Privacy First**: The app uses official Windows Media Capture APIs. If access is blocked, the app provides a direct link to Windows Privacy Settings.
- **Offline by Default**: MultiCamApp does not require an internet connection to record or verify videos. No data is uploaded to the cloud.

## Security Policy

### What MultiCamApp Does NOT Do
- Bypass or disable Windows Defender or third-party security software.
- Hide processes or inject code into other applications.
- Access the camera or microphone without visible indicators in the UI.
- Auto-start at Windows logon or run hidden background tasks.
- Send telemetry or usage data by default.

### What MultiCamApp DOES
- Use **Inno Setup** for a standard installation and uninstallation experience.
- Bundle all dependencies locally to avoid system-wide environment changes.
- Provide a clear **Apps & Features** entry for easy removal.
- Enforce security policies via a dedicated `SecurityPolicyService` at startup.

## Technical Configuration

The application's security posture is controlled via the `security` section in `config/appsettings.json`:

| Setting | Default | Meaning |
| :--- | :--- | :--- |
| `disableAntivirusBypass` | `true` | Prevents any behavior that might be flagged as a bypass. |
| `cameraAccessOnlyAfterUserAction` | `true` | Ensures camera access is strictly user-initiated. |
| `telemetryEnabled` | `false` | No data is sent to external servers. |
| `networkRequired` | `false` | Operates fully in offline environments. |

## SmartScreen & Signing

Unsigned builds may trigger a Windows SmartScreen warning ("Windows protected your PC"). This is normal for development or test builds. For production environments, we recommend using **signed** builds which verify the publisher's identity.

For more details on the release process, see the [Release Checklist](../developer_notes/release_checklist.md).
