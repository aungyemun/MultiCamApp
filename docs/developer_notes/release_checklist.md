# Release Checklist

Use before distributing MultiCamApp to end users.

## Build

- [ ] Run `scripts\setup_tools.bat` on a clean dev machine (once)
- [ ] Run `installer\build_release.bat` (publishes `dist\`, builds `installer\MultiCamApp_{version}_Setup.exe`, and validates the release)
- [ ] Confirm `dist\MultiCamApp.exe`, `dist\runtime\ffmpeg\ffprobe.exe`, and `installer\MultiCamApp_{version}_Setup.exe` exist
- [ ] Confirm `dist\config\version.json` contains the intended app version and build number

## Metadata and policy

- [ ] `config/version.json` matches installer `#define AppVersion`
- [ ] Executable metadata: ProductName, Company, Description, Copyright (see `MultiCamApp.csproj`)
- [ ] `config/appsettings.json` `security` section matches policy defaults
- [ ] Workspace is clean: `powershell -File scripts\check_release_clean.ps1` passes
- [ ] `python scripts\packaging\validate_installer_security.py` passes

## Signing (public release)

- [ ] Set `MULTICAMAPP_SIGN_PFX` or `MULTICAMAPP_SIGN_THUMBPRINT`
- [ ] `scripts\packaging\sign_release.ps1` signs `dist\MultiCamApp.exe`, key DLLs, and `installer\MultiCamApp_{version}_Setup.exe`
- [ ] Signature verifies as **Valid** on release machine

## Antivirus (build machine)

- [ ] Optional: `scripts\packaging\scan_defender.ps1` on `dist\` and installer
- [ ] No Defender exclusion required on build machine

## Clean Windows test

- [ ] Install via `installer\MultiCamApp_{version}_Setup.exe` (not raw folder copy)
- [ ] Installer final verification reports PASS or WARNING, not false failure
- [ ] Installer UX reviewed against general mature Windows installer behavior
- [ ] Start Menu folder exists after install (MultiCamApp group)
- [ ] Desktop shortcut target = installed MultiCamApp.exe
- [ ] Desktop shortcut Start in = installed app folder
- [ ] Launch-after-install works (app opens correctly from installer)
- [ ] Second launch after closing app works (Desktop/Start Menu)
- [ ] App does not auto-open camera on startup
- [ ] Corrupted settings fallback works (rename appsettings.json to invalid JSON and launch)
- [ ] Desktop shortcut icon visible
- [ ] Start Menu shortcut icon visible
- [ ] Installed Apps icon visible (Settings -> Apps)
- [ ] Uninstaller icon visible
- [ ] About window logo visible
- [ ] Setup.exe icon visible
- [ ] Video Verification page runs ffprobe on a recorded session folder
- [ ] Video Verification detail panel shows Real Capture FPS, Playback FPS, Timestamp CSV status, capture intervals, wall-clock vs container timing, and scientific timing message
- [ ] Stable 29.684 FPS camera / 30 FPS Playback FPS sessions show `PASS_ORIGINAL_TIMING_WITH_NOTE`, not FAIL
- [ ] Original Capture Mode sessions show: Real frames only; no duplicates/placeholders
- [ ] Timestamp CSV row count matches frames written for clean Original Capture Mode sessions
- [ ] License page and publisher shown in installer
- [ ] App appears in **Settings → Apps → Installed apps**
- [ ] Uninstaller verified (removes app files and shortcuts)
- [ ] User data preservation verified (videos/logs remain after uninstall)
- [ ] App runs **without admin** when installed to a user profile folder
- [ ] Offline test: Install on a machine with NO internet; app launches and records successfully
- [ ] `{app}\logs\startup_smoketest.log` is written during install validation (app folder for user installs: `%LOCALAPPDATA%\Programs\MultiCamApp`)
- [ ] `{app}\logs\smoke_test_result.txt` contains PASS / WARNING / FAIL
- [ ] **Refresh cameras** lists devices
- [ ] **Start Preview** opens camera only after click; status shows Previewing
- [ ] **Start Recording** only after preview; status shows Recording
- [ ] Closing the app releases the camera (no LED stuck on)
- [ ] No startup entries, scheduled tasks, or services created
- [ ] Airplane mode / offline: preview and record still work locally
- [ ] Camera blocked in Windows Settings shows privacy message and **Open Camera Settings**

## Distribution

- [ ] Ship `installer\MultiCamApp_{version}_Setup.exe` or `installer\installer.zip` (signed for production)
- [ ] Do **not** ask users to disable antivirus or add exclusions
- [ ] Include `LICENSE.md` and version in About tab
