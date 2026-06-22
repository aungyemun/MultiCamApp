# Changelog

## [1.1.0] - 2026-06-23

### Fixed
- 2-camera sessions now use the synchronized start gate (threshold changed from ≥3 to ≥2 cameras), eliminating ~190 ms inter-camera first-frame offset.
- `MaxTotalQueueDrops` now takes the max of sample-based totals and the sum of per-camera final stats, preventing a false zero when a drop occurs at or after the recording stop boundary.

### Added
- `PASS_WITH_WARNING` result for inter-camera start offset in the 50–100 ms range (previously only PASS or FAIL at 100 ms). Threshold constants `StartOffsetWarnMs` (50 ms) and `StartOffsetFailMs` (100 ms) added to `ScientificTimingAssessor`.
- Focus metadata now includes a **Final focus mode** field and annotates unreliable driver readback as `Unknown/readback unreliable` when autofocus is requested off but the driver readback reports it active.
- 6 new unit tests covering single-camera offset exemption, warn-range offset, fail-range offset, and threshold boundary cases.

## [1.0.90] - 2026-06-22

### Fixed
- Pre-release audit: removed developer stress-test bat from installer, fixed unguarded log writes in recording path, fixed Japanese translation error, removed dead handler and stale localization key.

## [1.0.89] - 2026-06-22

### Fixed
- Release build after footer copyright wording alignment and Advanced Camera Controls label polish.

## [1.0.88] - 2026-06-21

### Fixed
- Release build after exposure defaults, focus metadata wording, and camera-control UI cleanup.

## [1.0.87] - 2026-06-21

### Fixed
- Release build after UI terminology and About dialog polish.

## [1.0.84] - 2026-06-21

### Fixed
- Release maintenance build.
- Keep version metadata, installer AppVersion, and published executable versions in sync.
- Use build_release.bat to produce a verified dist bundle and installer.

## [1.0.83] - 2026-06-21

### Fixed
- Original Capture Mode scientific timing release. Added per-frame Timestamp CSV metadata, timestamp-based trimming guidance, scientific timing confidence, visual near-duplicate audit notes, and strengthened Original Capture Mode audit reporting.

## [1.0.82] - 2026-06-21

### Changed
- Clean Main Status and Hardware Diagnostics report wording while keeping recording, preview, focus, metadata, verification, audit, and timing behavior unchanged.

## [1.0.81] - 2026-06-21

### Changed
- Polish Main, Video Verification, Hardware Diagnostics, and About UI text and disabled-control appearance.

## [1.0.80] - 2026-06-21

### Fixed
- Harden privacy-safe metadata, logging, diagnostics, verification exports, and clean shutdown cleanup.

## [1.0.79] - 2026-06-21

### Changed
- Clarify recording diagnostics summaries and audit reports by separating stable scientific FPS timing from raw instantaneous diagnostic artifacts.
- Add per-camera timing verdicts, session verdict wording, stable FPS-by-frame-count and FPS-by-intervals fields, and ignored instantaneous FPS artifact notes.

## [1.0.78] - 2026-06-21

### Added
- Add diagnostic-only recording resource summaries for CPU, RAM, disk, writer queue, file growth, measured FPS, and focus status.
- Add audit report sections for Recording Resource Diagnostics, Likely Bottleneck, and Recommended Action.
- Add storage capacity warning before recording when available disk space may be insufficient for the selected duration, resolution, and camera count.

## [1.0.77] - 2026-06-20

### Fixed
- Add documentation for Original Capture Mode, status meanings, Real Capture FPS frame-count differences, and Timestamp CSV based analysis.
- Add per-frame timestamp CSV reporting and timestamp-based trimming guidance for scientific review.
- Add Scientific Timing Confidence to Verification reporting.
- Add scientific Original Capture metadata completeness validation with required field percentage, missing field list, and `scientificMetadataComplete` reporting.
- Strengthen Original Capture pass/fail rules for timestamp CSV, duplicate frames, placeholder frames, queue drops, and captured/written frame mismatches.
- Fail Original Capture verification when critical timing metadata is missing, and use `PASS_WITH_WARNING` for otherwise clean recordings with only non-critical metadata gaps.
- Persist `deviceIndex` and `recommendedAction` in new per-camera metadata so required scientific audit fields are complete.

## [1.0.76] - 2026-06-20

### Fixed
- Harden Original Capture Mode verification so duplicate frames, placeholders, Writer drops, missing Timestamp CSV, or captured/written frame mismatches are clear failures.
- Add Verification page summary fields for scientific timing confidence, timing mode, original-frame status, timestamp CSV status, queue drops, duplicates, placeholders, and trim source.
- Expand per-camera Verification rows with native FPS, writer/container FPS, frame counts, timestamp rows, duration comparison, start offset, and recommended action.
- Clarify metadata timestamp fields to prefer actual first/last captured frame timestamps and keep Original Capture dropped-frame metadata at zero for real-frame-only recordings.

## [1.0.75] - 2026-06-20

### Changed
- Update the UI footer license text and make the camera focus setting wording explicit that autofocus is off by default and manual/fixed focus is the main path.

## [1.0.74] - 2026-06-20

### Fixed
- Finish Original Capture Mode audit and verification: preserve real-frame-only wording, accept stable measured FPS below nominal with metadata notes, treat frame count differences as informational, and keep legacy duplicate-corrected sessions auditable as `LegacyConstantFrameCount`.

## [1.0.67] - 2026-06-19

### Fixed
- Improve 3/4-camera OpenCV recording diagnostics and synchronization. Add writer queue depth, VideoWriter timing, live queue-drop warning, strict audit handling, and recording preview throttling.

## [1.0.66] - 2026-06-19

### Fixed
- Remove artificial UI delays during recording start and stop and parallelize video probing.

## [1.0.65] - 2026-06-18

### Fixed
- Improve recording metadata accuracy by counting writer queue pressure instead of silently dropping old frames.
- Increase the OpenCV recording queue buffer for 4-camera 1080p recording.
- Keep sequential preview open for duplicate USB camera stability.

## [1.0.64] - 2026-06-18

### Fixed
- Recover selected cameras that open through OpenCV but do not deliver first preview frames by retrying the same selected device through WinRT.
- Keep camera selections fully user-changeable; no camera names or slots are hardcoded.
- Record mixed-backend preview sessions by using each ready slot's working backend.

## [1.0.63] - 2026-06-18

### Fixed
- Add startup protection against hidden stale MultiCamApp processes that can keep camera devices busy.
- Prevent launching a second visible MultiCamApp instance from the same installation path.
- Keep duplicate USB camera OpenCV opens on exact PnP device paths instead of numeric DirectShow indices.

## [1.0.62] - 2026-06-18

### Fixed
- Fix 4-camera preview regression where duplicate USB j5 cameras could time out after the cam3 recording fix.
- Prefer exact DirectShow PnP paths over numeric indices for duplicate USB OpenCV opens.
- Preserve exact PnP bindings during duplicate USB preparation instead of invalidating them after brief index probes.

## [1.0.61] - 2026-06-18

### Fixed
- Fix 4-camera recording startup where cam3 could freeze and route to WinRT instead of OpenCV.
- Keep duplicate USB webcam recording on the OpenCV preview pipeline for 3/4-camera sessions.
- Add per-slot recording startup diagnostics for precheck, writer open, pump start, first frame written, and failures.

## [1.0.60] - 2026-06-18

### Added
- Diagnostic-only Hardware Diagnostics page for advisory system, graphics, camera capability, and USB topology checks.
- `SystemProfile.latest.json`, `CameraCapability.latest.json`, and `UsbTopology.latest.json` reports under the app logs folder.
- Offline `scripts/maintenance/check_graphics_driver.ps1` for display adapter and driver checks.
- Quiet Performance Monitor logs during preview/recording for CPU, memory, preview FPS, frame counter deltas, and writer queue-drop diagnostics.

### Notes
- Camera support remains camera-agnostic: OBSBOT and j5 are validation examples, not required brands.
- Diagnostics are advisory only and do not block 1080p or change selected cameras, camera order, recording output, metadata, Video Verification thresholds, or session comparison.
- Performance Monitor adds no UI controls, buttons, panels, status text, popups, or GPU/encoder switching; logs are small app-log-folder support diagnostics only.
- WPF preview optimization, full watchdog recovery, and placeholder salvage mode remain deferred.

## [1.0.59] - 2026-06-18

### Fixed
- Stabilized 3/4-camera preview startup for duplicate j5 webcams plus OBSBOT by opening high-friction devices first with staggered recovery.
- Added built-in webcam fallback workflow when `j5 Webcam JVU250 #3` fails preflight, constrained to 360p/720p validation.
- Added targeted final 3/4-camera stress diagnostics and 30-second post-preflight driver recovery cooldown.

## [1.0.58] - 2026-06-18

### Fixed
- Restore v1.0.38-style OpenCV preview color path for duplicate USB cameras.
- Avoid successful OpenCV-to-WinRT preview fallback that changed color/quality and backend lifecycle.
- Wait for OpenCV capture loop shutdown before release/reopen and add one-shot preview quality diagnostics.

## [1.0.57] - 2026-06-18

### Fixed
- Fix preview lifecycle crash risk after repeated Start/Stop and layout or camera selection changes.
- Block layout and camera selection changes while preview pipelines are active.
- Add preview lifecycle locks, stale callback guards, event unsubscribe logs, and stop cleanup timeouts.

## [1.0.56] - 2026-06-18

### Fixed
- Restore parallel Start Preview open tasks for multi-camera 1080p to reduce startup time.
- Prevent stale preview frames after repeated Start/Stop by using preview session generation checks.
- Discard initial warm-up frames and add preview lifecycle timing markers for troubleshooting.

## [1.0.55] - 2026-06-18

### Fixed
- Fix duplicate j5 webcam pair mapping by preserving unique DirectShow URI/path identity.
- Avoid false ownership conflicts from duplicate friendly names; compare URI/index before name fallback.
- Add CAMERA_SELECTED/RESOLVED/OWNERSHIP/OPEN_TARGET mapping diagnostics before OpenCV open.

## [1.0.54] - 2026-06-17

### Fixed
- App-folder debug logs: preview_session, recording_session, app_runtime, crash_or_failure (rotating, non-blocking).
- Centralized UI button/status rules via UpdateUiStateFromCurrentState.
- 1080p multi-cam sequential open and serialized OpenCV release to prevent preview crashes.

## [1.0.53] - 2026-06-17

### Fixed
- App-folder debug logs: preview_session, recording_session, app_runtime, crash_or_failure (rotating, non-blocking).
- Centralized UI button/status rules via UpdateUiStateFromCurrentState.
- 1080p multi-cam sequential open and serialized OpenCV release to prevent preview crashes.

## [1.0.51] - 2026-06-17

### Fixed
- App-folder debug logs: preview_session, recording_session, app_runtime, crash_or_failure (rotating, non-blocking).
- Centralized UI button/status rules via UpdateUiStateFromCurrentState.
- 1080p multi-cam sequential open and serialized OpenCV release to prevent preview crashes.

## [1.0.52-dev] - 2026-06-17

### Fixed
- Fixed possible stale OpenCvDeviceSession mapping state during Start Preview.
- Reset used DirectShow indices/names/URIs before rebuilding selected camera mapping.
- Added lightweight diagnostics for OpenCV device binding conflicts.
- No changes to recording writer, metadata, video verification, or session comparison.

## [1.0.50] - 2026-06-17

### Fixed
- Preview crash fix: safe OpenCV release on slot failure (no double-dispose crash).
- Stop Preview clears failure overlay text and resets button states.
- Partial preview status/buttons fixed; camera_open_failure_*.log per slot failure.

## [1.0.49] - 2026-06-17

### Fixed
- Preview: per-slot failure handling when a camera cannot open at 1080p/720p/360p — working cameras keep previewing.
- Preset-only overlay messages with 8s open / 5s first-frame timeouts; Start Recording blocked until all slots ready.
- Logging: SLOT_FAILED in preview_start_trace, preview_problem_summary.txt, and crash_or_failure logs.

## [1.0.48] - 2026-06-17

### Fixed
- Camera display names: numbered labels for duplicate/generic USB cameras by unique device ID.
- Refresh Cameras: preserve slot selections, mark unavailable devices, write camera_refresh_*.log.
- Footer: remove accent line above copyright.
- Docs: formal STABLE_CORE_V1 freeze exception policy.

## [1.0.47] - 2026-06-17

### Fixed
- Start Preview: parallel per-slot open for all layouts, faster driver startup, and preview_start_trace / preview_start_diagnosis logs.
- Runtime setup: fix duplicate console on install (single setup_runtime.bat run), runtime_initialized.flag, and silent app bootstrap.
- Installer: verify runtime_initialized.flag and runtime_paths.env after install.

## [1.0.46] - 2026-06-17

### Fixed
- Resolution UI and metadata use 360p / 720p / 1080p preset labels (not raw pixel strings).
- Preview reliability: open only selected cameras, async per-slot startup with partial success, duplicate-device blocking, USB stream-loss detection, and lightweight diagnostic logging.
- Video Verification: expected/actual resolution columns and detail panel show preset labels; legacy WxH metadata still parses.

## [1.0.45] - 2026-06-17

### Fixed
- Multi-camera: prevent alternate DirectShow index fallback from stealing indices reserved for other selected cameras (fixes cam1 Could not open when cam3 grabbed index 0).
- Multi-camera diagnostics: auto-write logs/cam3_diagnostics.txt after 3+ camera recordings.

## [1.0.44] - 2026-06-16

### Fixed
- Video Verification UI: fix invisible session summary and grid text on dark theme (readable light foreground colors).
- Video Verification: propagate ScientificTimingStatus FAIL to per-video audit status and session summaries (queue-drop failures now show as FAIL).

## [1.0.43] - 2026-06-16

### Fixed
- 3/4-camera recording: per-camera first-frame verify during sequential startup (not after all cameras), dedicated `recording_start_diagnostics_*.log`, fail-fast with empty MP4 cleanup.
- 3/4-camera preview: incremental per-slot preview start with Opening/Ready progress; USB bandwidth warning for 3+ layouts.
- Preserves v1.0.38 parallel 2-camera recording and preview workflow.

## [1.0.42] - 2026-06-16

### Fixed
- Fix 3/4-camera preview start: probe-bind DirectShow once before sequential opens; skip WinRT focus when autofocus off; avoid re-probing already-open cameras.

## [1.0.40] - 2026-06-16

### Fixed
- 3/4-camera automated tests (coordinator, session comparison, capture stagger). Validation build for multi-camera recording.

## [1.0.39] - 2026-06-16

### Fixed
- 3/4-camera recording startup fix: sequential start for 3+ cameras, extend-preview stagger for cam3/cam4, first-frame timeout, fail-fast rollback, startup/stop logging. 1/2-camera workflow unchanged.

## [1.0.38] - 2026-06-11

### Fixed
- Video Verification UI: fix invisible session summary and grid text on dark theme (readable light foreground colors).
- Video Verification: propagate ScientificTimingStatus FAIL to per-video audit status and session summaries (queue-drop failures now show as FAIL).

## [1.0.37] - 2026-06-11

### Fixed
- STABLE_CORE_V1 freeze (recording, metadata, verification, session comparison). Video Verification startup fix, installer VC++ 1638 handling, desktop shortcut replace-on-upgrade.

## [1.0.36] - 2026-06-11

### Fixed
- Installer: treat VC++ exit 1638 (already installed) as success; create desktop shortcut on user desktop instead of Public Desktop to avoid access denied.

## [1.0.35] - 2026-06-11

### Fixed
- Fix startup crash on Video Verification page: DataGrid columns are now added via DataGrid.Columns instead of invalid FrameworkElementFactory visual children.

## [1.0.34] - 2026-06-11

### Fixed
- Installer supports automatic in-place upgrade: backs up settings, removes old app files, and installs the new version without manual uninstall.
- App logs use %LOCALAPPDATA%\MultiCamApp\logs when Program Files is not writable.
- Video Verification uses UI-requested resolution and FPS from session metadata.

## [1.0.33] - 2026-06-11

### Fixed
- Fix startup crash when installed under Program Files: app logs use %LOCALAPPDATA%\MultiCamApp\logs when the install folder is not writable.

## [1.0.32] - 2026-06-11

### Fixed
- Localized metadata and verification detail labels in English and Japanese; recording metadata files remain English.
- Video Verification compares against UI-requested resolution (640x480, 720p, 1080p) and FPS (15/24/30/60) from session metadata.
- Installer: no welcome or post-install success popups; license agreement without pre-welcome message.

## [1.0.31] - 2026-06-10

### Fixed
- Align Video Verification with v1.0.30 session metadata: capture interval stats, scientific timing message, and container vs wall-clock difference.
- Clarify PASS_WITH_WARNING guidance when cameras deliver ~29 fps but MP4 is tagged 30 fps; de-emphasize misleading timestamp drift wording.

## [1.0.30] - 2026-06-10

### Fixed
- Fix capture interval metadata: freeze CaptureTimingSnapshot before recorder cleanup so mean/min/max/std are preserved per session.
- Add CaptureIntervalCount and unavailable messaging; show Unavailable instead of silent 0 in metadata and audit reports.

## [1.0.29] - 2026-06-10

### Fixed
- Fix per-session metadata: FramesCaptured, MeasuredCameraFps, and capture interval stats reset each recording.
- Add clearer timing fields (wall-clock vs container vs frame-based) and ScientificTimingStatus/Message.
- De-emphasize misleading TimestampDriftSeconds; improve session summary and video audit wording.

## [1.0.28] - 2026-05-29

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.27] - 2026-05-29

### Changed
- Version bump for the current release packaging and release compliance updates.

## [1.0.26] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.25] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.24] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.23] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.22] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.21] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.20] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.19] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.18] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.17] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.16] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.15] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.14] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.13] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.12] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.11] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.10] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.9] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.8] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.7] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.6] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.5] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.4] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.3] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.2] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.

## [1.0.1] - 2026-05-28

### Fixed
- Updated About window citation and attribution
- Improved professional licensing and contribution structure
- Added Copy Citation and Copy Version Info features
- Refined documentation and license text

## [1.0.0] - 2026-05-28

### Fixed
- First stable release: fully self-contained offline installer, automated runtime setup, and startup diagnostics.
- Professional citation and attribution system integrated into app and documentation.
- Modern application architecture with dual capture engines (OpenCV/WinRT).

## [0.0.85] - 2026-05-28

### Fixed
- Installer/Uninstaller system overhaul: integrated runtime setup script, automated final checks, and enhanced data preservation during uninstall.

## [0.0.84] - 2026-05-28

### Fixed
- Installer/Uninstaller system overhaul: integrated runtime setup script, automated final checks, and enhanced data preservation during uninstall.

## [0.0.83] - 2026-05-28

### Fixed
- Installer/Uninstaller system overhaul: integrated runtime setup script, automated final checks, and enhanced data preservation during uninstall.

## [0.0.82] - 2026-05-28

### Fixed
- Installer/Uninstaller system overhaul: integrated runtime setup script, automated final checks, and enhanced data preservation during uninstall.

## [0.0.81] - 2026-05-28

### Fixed
- Installer/Uninstaller system overhaul: integrated runtime setup script, automated final checks, and enhanced data preservation during uninstall.

## [0.0.80] - 2026-05-28

### Fixed
- Installer overhaul: fully self-contained offline bundle with silent VC++ redist, startup diagnostics, and isolated runtimes.

## [0.0.79] - 2026-05-28

### Fixed
- Installer overhaul: fully self-contained offline bundle with silent VC++ redist, startup diagnostics, and isolated runtimes.

## [0.0.78] - 2026-05-28

### Fixed
- Autofocus refinement: forced off as default for multi-camera layouts; added best-effort focus control for OpenCV cameras using WinRT backend.

## [0.0.77] - 2026-05-28

### Fixed
- Verification profile consolidation: merged behavioral analysis into the Standard profile and removed all other profile options for a simplified workflow.

## [0.0.76] - 2026-05-28

### Fixed
- Verification refinement: relaxed FPS/duration tolerances in appsettings.json to prevent false warnings; downgraded session-level duration spread to WARNING unless extremely high.

## [0.0.75] - 2026-05-28

### Fixed
- Video Verification UI refinement: compacted summary cards, increased detail panel height, and optimized spacing for better readability.

## [0.0.74] - 2026-05-27

### Fixed
- UI Fix: DataGrid column headers visibility improved. Verification: Relaxed FPS and duration tolerances for behavioral analysis; PASS if frames match despite container duration mismatch.

## [0.0.73] - 2026-05-27

### Fixed
- Added Behavior_Stable_Time_Profile for behavior analysis: prioritizes stable actual FPS and minimal inter-camera drift over exact nominal FPS.

## [0.0.72] - 2026-05-27

### Fixed
- Video Verification UI overhaul: merged session results into details panel, increased table row size and font, improved layout visibility, and added Locomotor profile to selection

## [0.0.71] - 2026-05-27

### Fixed
- Video Verification UI overhaul: merged session results into details panel, increased table row size and font, improved layout visibility, and added Locomotor profile to selection

## [0.0.70] - 2026-05-27

### Fixed
- Fixed cross-thread access error in Video Verification page; added busy state protection for scan/verify operations

## [0.0.69] - 2026-05-27

### Fixed
- Video Verification page overhaul: added Verify All, auto-scan, search filter, and result clearing; improved UI responsiveness

## [0.0.68] - 2026-05-27

### Fixed
- AutoFocus control added (off by default); stable OpenCV-first backend; built-in 720p verification fix

## [0.0.67] - 2026-05-27

### Fixed
- AutoFocus control added (off by default); stable OpenCV-first backend; built-in 720p verification fix

## [0.0.66] - 2026-05-27

### Fixed
- AutoFocus control added (off by default); stable OpenCV-first backend; built-in 720p verification fix

## [0.0.65] - 2026-05-27

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.63] - 2026-05-27

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.62] - 2026-05-27

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.61] - 2026-05-27

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.51] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.50] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.49] - 2026-05-22

### Fixed
- Dual USB webcam DirectShow mapping, distinct device labels, preview open timeout

## [0.0.47] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.46] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.45] - 2026-05-22

### Fixed
- Exclusive slot device ownership; camera opens only on selected cam slot

## [0.0.44] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.43] - 2026-05-22

### Fixed
- Cam4 1080p/720p USB negotiation, multi-camera stagger, preflight capture FPS, startup fix

## [0.0.42] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.41] - 2026-05-22

### Fixed
- Fix startup crash and experiment preflight (capture FPS, reapply settings before test)

## [0.0.40] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.39] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change

## [0.0.38] - 2026-05-22

### Fixed
- 3/4-camera layout: distinct devices per slot, duplicate protection, extended preview on layout change, OpenCV multi-camera mapping

## [0.0.37] - 2026-05-22

### Fixed
- OpenCV metadata accuracy, Video Verification UI, ffprobe bundled in installer

## [0.0.36] - 2026-05-22

### Fixed
- OpenCV metadata accuracy, Video Verification UI, ffprobe bundled in installer

## [0.0.35] - 2026-05-22

### Fixed
- Video Verification results table and detail panel; pre-record settings mismatch warning

## [0.0.34] - 2026-05-22

### Fixed
- OpenCV metadata accuracy, Video Verification UI, ffprobe bundled in installer

## [0.0.33] - 2026-05-22

### Fixed
- Release build

## [0.0.32] - 2026-05-22

### Fixed
- Release build

## [0.0.31] - 2026-05-22

### Fixed
- Release build

## [0.0.30] - 2026-05-22

### Fixed
- Release build

## [0.0.29] - 2026-05-22

### Fixed
- Release build

## [0.0.27] - 2026-05-22

### Fixed
- Release build

## [0.0.26] - 2026-05-22

### Fixed
- Release build

## [0.0.24] - 2026-05-22

### Fixed
- Release build

## [0.0.23] - 2026-05-22

### Fixed
- Release build

## [0.0.20] - 2026-05-22

### Fixed
- Security policy, MultiCamApp_Setup installer, signing and validation

## [0.0.19] - 2026-05-22

### Fixed
- Fix preview freeze: About tab UserControl, throttled frames, camera validation

## [0.0.18] - 2026-05-22

### Fixed
- AV-safe root launcher; multi-file dist; release verify and security docs

## [0.0.17] - 2026-05-22

### Fixed
- Release build

## [0.0.16] - 2026-05-22

### Fixed
- Release build

## [0.0.15] - 2026-05-22

### Fixed
- Release build

## [0.0.14] - 2026-05-22

### Fixed
- Release build

## [0.0.13] - 2026-05-22

### Added
- Formal semantic versioning (`config/version.json`, stages `experimental` → `stable`)
- `VersionService` and enhanced `bump_version.py` (patch/minor/major, changelog, installer, csproj sync)
- Metadata fields: App Version, Build Number, Release Stage

### Changed
- Version renumbered to `0.0.13` (experimental) per early-development policy

## [0.2.0] - 2026-05-22

### Added
- `MultiCamApp.exe` self-contained publish (`releases/MultiCamApp/`)
- App logo from `Multicam.png` (window icon + header)
- `CameraModeSelector` — auto picks native/recommended resolution and ~30 FPS (no hard 1080p cap)
- `ResponsiveLayoutManager` — adaptive 1–4 camera grid on window resize
- `AppLifecycleService`, `PrivacyGuardService`, `ResourceManager`
- Privacy metadata fields; cameras released on stop preview / app close
- Preview throttling for UI performance; recording mode independent of UI size

### Changed
- `appsettings.json` expanded with resolution, privacy, and responsive UI flags

## [0.1.1] - 2026-05-22

### Added
- USB camera hot-plug watcher and safe disconnect handling
- Auto-reconnect when a disconnected camera returns during preview
- **Refresh cameras** button in the main UI
- Inno Setup template (`installer/setup_builder/MultiCamApp.iss`)

### Changed
- UI shell documented as WPF + MediaCapture (portable `dotnet build`)

## [0.1.0] - 2026-05-22

### Added
- WinUI 3 / .NET 8 desktop application skeleton
- JSON configuration (appsettings, recording presets, version, camera profiles)
- English and Japanese localization with runtime language switch
- Camera detection and 1–4 camera layout preview grid
- Media Foundation recording pipeline (H.264 MP4 via MediaCapture)
- Separate preview and recording control paths per camera slot
- Session folder output with per-camera metadata and session summary
- Diagnostics utilities (FPS monitor, timing monitor, file verifier)
- Python and PowerShell helper scripts for build, validation, and verification
- Architecture and user guide documentation
