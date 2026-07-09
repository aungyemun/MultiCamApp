# Changelog

All notable changes to MultiCamApp will be documented in this file.

## [v1.2.30-alpha] - 2026-07-02 (build 249)

> **Removed broken "(unknown)" device-name footer from camera preview panels; verified Video Verification and Hardware Diagnostics pages need no changes.** The small text at the bottom of each camera panel (device-name footer bar) read `CameraSlotPipeline.DeviceName`, which falls back to `"(unknown)"` when `_deviceInfo` isn't populated — this was showing even during active, successful recording. Root cause not fully isolated (would need hardware-attached debugging to confirm why `DeviceInformation.CreateFromIdAsync` doesn't end up populating it for the default V2 path), but the bar was redundant regardless — the camera's device name is already shown in the device-selector dropdown above each panel. Removed the footer bar entirely (border, text block, layout row, and the two theme brushes that only it used) rather than leave broken/redundant UI. Checked whether Video Verification or Hardware Diagnostics needed updates for today's earlier metadata/flicker-reduction changes: Video Verification's `V2MetadataReader`/`V2VerificationRunner` already parse and display every camera control's `warning` field generically via `JsonElement.EnumerateObject()` (not a hardcoded per-control list), so it automatically surfaces the flicker-reduction and metadata fixes with zero code changes. Hardware Diagnostics is a fully independent hardware-inventory tool (system/GPU/camera-capability/USB scan) unrelated to per-recording metadata — no changes needed there either. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MainWindow.xaml.cs`** — Removed the device-name footer bar (`_deviceFooters`, `footerBar`/`deviceFooter` construction, and its `UpdatePreviewOverlayStats` update) from each camera preview panel.
* **`ui/PreviewPanelTheme.cs`** — Removed `FooterBarBackground`/`FooterForeground` brushes (only used by the removed footer).
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.30-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.30-alpha`. 289/289 tests passing.

## [v1.2.29-alpha] - 2026-07-02 (build 248)

> **Flicker reduction retry-loop fix; STABLE_CORE_V1 audit (no code changes, findings reported).** Found and fixed a real bug in `SetFlickerReductionAutoAsync`: it picked one candidate value (Auto/60Hz/50Hz) by priority from the driver's `SupportedValues` capability list and attempted `SetValueAsync` exactly once — if the driver rejected that one value (a device can list a value as supported yet still decline to apply it), the method gave up immediately without ever trying the other listed candidates. Now loops through all supported candidates in priority order and only reports failure if every one is rejected. Separately audited STABLE_CORE_V1 (no protected files modified, per freeze policy) — see conversation for full findings: the freeze declaration (`docs/STABLE_CORE_V1_FREEZE.md`) was last updated for v1.1.21 (build 214) and describes the legacy OpenCV recording pipeline (`RecordingSession.cs` → `MetadataWriter.cs`, PascalCase field schema), which is confirmed still reachable only as a fallback; the primary V2/WinRT pipeline that produces virtually all current recordings writes metadata via unprotected code in `MainWindow.xaml.cs` (`V2_METADATA_WRITTEN`, camelCase schema) that predates the freeze doc's last revision. No code change made pending user decision on whether to update the freeze documentation. V2 stable recording workflow, STABLE_CORE_V1 protected files, and CSV schema unchanged.

### Changed
* **`CameraControlManagerV2.cs`** — `SetFlickerReductionAutoAsync`: retries all supported candidate values instead of giving up after the first rejection; failure warning now lists every rejected candidate.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.29-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.29-alpha`. 289/289 tests passing.

## [v1.2.28-alpha] - 2026-07-02 (build 247)

> **Full audit of C:\Users\1\Videos\ (3 test sessions); metadata completeness fixes; v1.2.27 color tagging reverted (verified non-functional).** Audited all 3 recording sessions (`test1/2/3_20260701_*`), their MP4s, and their metadata files. Found `cam1_metadata.json/txt` and `metadata.json/txt` are byte-identical duplicates in every session — **not a bug**: `VideoScanner.cs` (STABLE_CORE_V1, frozen) hardcodes the unprefixed `metadata.json`/`metadata.txt` filenames for its scan/verify logic, so the app intentionally writes both the current prefixed names and the legacy unprefixed names for backward compatibility. Left untouched. Found a real metadata gap: `flickerReduction` (and `digitalStabilization`) were the only controls missing the `warning` field that every other control (`focus`/`exposure`/`lowLightCompensation`) includes — so a "Failed" flicker-reduction result gave zero diagnostic info. Root cause traced deeper: `SetFlickerReductionAutoAsync` never populated a `WarningMessage` when the driver's `SetValueAsync` returned false without throwing — fixed to report the target mode and rejection. Verified via ffprobe on all 3 real recordings that the v1.2.27 color-primaries/matrix/range tagging (`mp4.Video.Properties[MF_MT_*]`) has **no effect** — all 3 still show `color_space`/`color_primaries=unknown` despite being built from build 246. Reverted that code (was shipping a no-op) with a detailed comment explaining why: `MediaCapture.PrepareLowLagRecordToStorageFileAsync` does not appear to propagate extra `VideoEncodingProperties.Properties` entries into the muxed H.264 output; properly fixing this would require replacing the encoder with a custom Media Foundation Sink Writer pipeline, which was not attempted. This has no visible playback impact since `color_range` already matches Windows Camera app and untagged HD H.264 is universally interpreted as BT.709 by virtually all players. No unnecessary files found to remove — the session folders are the user's own test recordings, not orphaned artifacts. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MainWindow.xaml.cs`** — `flickerReduction`/`digitalStabilization` JSON and text metadata now include the `warning` field, matching the other controls.
* **`CameraControlManagerV2.cs`** — `SetFlickerReductionAutoAsync`: populates `WarningMessage` with the target mode and rejection reason when the driver declines to apply flicker reduction.
* **`MediaFoundationEncoderService.cs`** — Reverted the v1.2.27 `MF_MT_VIDEO_PRIMARIES`/`MF_MT_YUV_MATRIX`/`MF_MT_VIDEO_NOMINAL_RANGE` property-bag tagging (confirmed non-functional via real-hardware ffprobe); left a comment explaining what was tried and why it doesn't work through this API.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.28-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.28-alpha`. 289/289 tests passing.

## [v1.2.27-alpha] - 2026-07-01 (build 246)

> **Fix: H.264 output now tagged with color primaries/matrix/range to match Windows Camera app.** Prior audit found MultiCamApp's MP4 output left `color_space`/`color_primaries` untagged ("unknown" in ffprobe) while Windows Camera app tags `color_primaries=bt709`/`color_space=bt470bg` (both apps already agreed on `color_range=pc`). `MediaFoundationEncoderService.BuildEncodingProfile` now sets `MF_MT_VIDEO_PRIMARIES` (MFVideoPrimaries_BT709=2), `MF_MT_YUV_MATRIX` (MFVideoTransferMatrix_BT601=2 — documented by Microsoft as "also used for SMPTE 170 and ITU-R BT.470-2 System B,G", which is the value expected to produce ffprobe's `bt470bg` reading like Windows Camera app), and `MF_MT_VIDEO_NOMINAL_RANGE` (MFNominalRange_0_255=1) via the WinRT `VideoEncodingProperties.Properties` attribute bag, since these aren't exposed as first-class WinRT properties. GUIDs and enum values were verified against two independent Windows SDK header mirrors plus official Microsoft Learn API docs before being hardcoded, given a wrong GUID would fail silently. This is a container-metadata tag only — it does not change encoded pixel values, only how players interpret them. **Needs verification on real hardware**: only an actual recording can confirm the Media Foundation H.264 MFT honors these attributes and ffprobe reports the expected tags. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MediaFoundationEncoderService.cs`** — `BuildEncodingProfile`: tags `mp4.Video.Properties` with `MF_MT_VIDEO_PRIMARIES`/`MF_MT_YUV_MATRIX`/`MF_MT_VIDEO_NOMINAL_RANGE`.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.27-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.27-alpha`. 289/289 tests passing (no unit test covers actual MFT attribute honoring — requires a real recording + ffprobe to verify).

## [v1.2.26-alpha] - 2026-07-01 (build 245)

> **v1.2.25 lighting fix confirmed working; second Start Recording status flash removed.** Side-by-side audit of `test1_20260701_153308/cam1.mp4` (MultiCamApp) vs `WIN_20260701_15_18_31_Pro.mp4` (Windows Camera app), same OBSBOT Meet SE hardware: extracted frames now visually match Windows Camera app's brightness (v1.2.25's LLC-gating fix confirmed active via `lowLightCompensation.result: "Not attempted"` in the recording's own metadata). Codec/resolution/fps/profile identical (H.264 Main, 1920x1080, 30fps, `yuvj420p` full-range) between both apps; MultiCamApp bitrate ~16.7 Mbps vs Windows Camera ~17.8 Mbps (close, both near the 18 Mbps target). Two minor non-blocking differences noted: Windows Camera app tags `color_space=bt470bg`/`color_primaries=bt709` while MultiCamApp leaves these unknown/untagged (cosmetic — most players default to bt709 for 1080p regardless), and Windows Camera app records an AAC audio track while MultiCamApp is video-only by design. Timestamp audit of the new recording: 1826 CSV rows, zero gaps, ~29.98fps measured, PASS. Separately, found and removed a second "environmental lock" warning: `StartV2DefaultRecordingAsync` was calling `ShowPreviewStatus(...)` with "Recording without environmental lock — auto exposure/WB may drift..." on every Start Recording click where Environmental Lock hadn't been used first (i.e. most of the time) — a different code path than the `CalibrationStatusLabel` text removed in v1.2.25, which is why it kept appearing as a brief flash. Start Recording is now fully silent on both status surfaces. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MainWindow.xaml.cs`** — `StartV2DefaultRecordingAsync()`: removed the `ShowPreviewStatus(...)` environmental-lock warning shown on every Start Recording click.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.26-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.26-alpha`. 289/289 tests passing.

## [v1.2.25-alpha] - 2026-07-01 (build 244)

> **Audit of `test1_20260701_150907` recording; lighting fix; Start Recording UI simplified.** Real-world recording audit (cam1.mp4, OBSBOT Meet SE) confirmed the frame remained dark throughout (verified visually via extracted frames at t=0.5s/8s/15s) despite the v1.2.24-alpha exposure-convergence fix, because `cam1_metadata.json` showed `ExposureControl` is **not supported** by this device/driver — auto-exposure ran uncontrolled the whole time regardless of app logic. Root cause was instead the app's unconditional low-light/backlight-compensation disable: it was darkening the picture on devices where exposure can't be locked anyway, buying zero reproducibility benefit. `ApplyResearchDefaultsAsync` now only disables low-light compensation when `ExposureControl.Supported` is true — when exposure is uncontrollable, LLC is left alone so the driver's own low-light handling can help. Also removed the "Hardware parameters hard-locked for dataset purity." / "Recording started (no environmental lock active...)" status text that appeared on `CalibrationStatusLabel` merely from clicking Start Recording — that label is for the Environmental Lock / One-Shot Calibrate buttons, not a Start Recording side-effect; clicking Start Recording is now silent on that label (still logged internally). Timestamp/clock audit of the same recording: 601 CSV rows, sequential frame indices with zero gaps, zero dropped-frame warnings, mean interval 33.39ms (≈29.95fps), one 66.5ms startup blip in the first few frames (harmless), UTC capture timestamps consistent with local wall clock — PASS. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`CameraControlManagerV2.cs`** — `ApplyResearchDefaultsInternalAsync`: low-light compensation disable is now gated on `ExposureControl.Supported`; skipped (with a logged reason) when exposure can't be controlled.
* **`MainWindow.xaml.cs`** — `StartRecordBtn_Click`: removed the `CalibrationStatusLabel.Text` update on every recording start; internal diagnostic log line kept (not user-visible).
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.25-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.25-alpha`. 289/289 tests passing.

## [v1.2.24-alpha] - 2026-07-01 (build 243)

> **Fix: CAM1-class cameras (real ExposureControl support) opening underexposed.** `CameraControlManagerV2.ApplyResearchDefaultsAsync` disables auto-exposure immediately on camera open to keep exposure stable for research recordings. For cameras without WinRT `ExposureControl` support (most cheap UVC webcams) this was always a no-op — their onboard auto-exposure kept running normally. But for cameras that *do* implement `ExposureControl` (e.g. OBSBOT Meet SE), the freeze happened at the very first readback after open, before the sensor's own auto-exposure loop had any time to converge, often locking in a dark transient value for the entire session. `DisableAutoExposureAsync` now — only when `ExposureControl.Supported` — explicitly re-enables auto-exposure and waits 500 ms before freezing, giving the driver's AE loop a short settle window first. Cameras without exposure control are unaffected (zero added delay, same as before). V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`CameraControlManagerV2.cs`** — `DisableAutoExposureAsync()`: added `ExposureConvergenceDelayMs = 500` settle window (only when `ExposureControl.Supported`) before freezing exposure at camera open and via the "Default Exposure" button.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.24-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.24-alpha`. 289/289 tests passing (no hardware-dependent tests cover this path — behavior verified by code trace, not unit test).

## [v1.2.23-alpha] - 2026-07-01 (build 242)

> **Windows Camera app audit: bitrate quality parity + dead-code cleanup.** Full comparison of MultiCamApp against the Windows built-in Camera app across camera logic, exposure, focus, lighting, GPU, and MP4 writing confirmed every user-facing control (resolution, FPS, autofocus/manual focus, auto/manual exposure, low-light compensation, manual white balance in Kelvin, environmental lock, one-shot calibrate) is correctly wired end-to-end to real WinRT camera APIs — no dead UI handlers found. Default recording bitrate raised from 7.5 Mbps to 18 Mbps (`WindowsCameraLike` preset) to match Windows Camera app output quality at 1080p30. Removed confirmed-dead code left over from the v1.2.22-alpha V3/V3B backend removal. V2 stable recording workflow, STABLE_CORE_V1 components, CSV schema, and verification verdict semantics unchanged.

### Changed
* **`VideoEngineSettings.cs`** — Default `BitrateProfile`/`TargetBitrateKbps` changed from `Standard` (7,500 Kbps) to `WindowsCameraLike` (18,000 Kbps).
* **`IVideoEngineBackend.cs`**, **`RecordingFinalizeResult.cs`** — Stale comments referencing the removed V3/V3B experimental backends rewritten to describe current V2-only reality.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.23-alpha`.

### Removed
* **`BackendInitDiagnostics.Scaffold()`** — unused factory method with zero callers (leftover from V3 scaffold removal).
* **`CameraControlManagerV2.SetBrightness()`** — implemented but unreachable; no UI control and no other caller.
* **`V2CameraControl.Brightness/Contrast/Gain/Zoom`** — enum values with no working implementation or caller (Contrast/Gain/Zoom were never implemented; Brightness's only use was the removed method).

### Tests
* Removed `BackendInitDiagnostics_Scaffold_Factory` (exercised removed method). Updated `V2Settings_BitrateProfile_DefaultsStandard` → `V2Settings_BitrateProfile_DefaultsWindowsCameraLike`. Updated `BackendVersion` assertion to `1.2.23-alpha`.

## [v1.2.22-alpha] - 2026-06-29 (build 237)

> **V3/V3B experimental backends removed; app runs exclusively on VideoEngineV2 Stable (Windows Camera parity).** VideoEngineV3 (22 source files) and VideoEngineV3B (15 source files) deleted. All V3/V3B routing code removed from VideoEngineRegistry, BackendIdentifiers, and MainWindow. Default config switched to `previewEngine: winrt` and `recordingEngine: windows_camera`. BackendMetadata simplified to V2-relevant fields only (V3/V3B diagnostic fields removed). V2 stable recording workflow, STABLE_CORE_V1 components, CSV schema, and verification verdict semantics unchanged.

### Removed
* **VideoEngineV3** — 22 source files in `capture/video_engine_v3/` deleted.
* **VideoEngineV3B** — 15 source files in `capture/video_engine_v3b/` deleted.
* **V3/V3B test files** — 17 test files deleted (11 V3 + 6 V3B).
* **V3 verification/metadata files** — `V3RecordingVerifier.cs`, `V3SessionSummaryAppender.cs` deleted.
* **BackendIds** — `VideoEngineV3Experimental` and `VideoEngineV3B_SinkWriterScientific` constants removed.
* **BackendMetadata** — ~110 V3/V3B diagnostic fields removed; V2-relevant fields retained.

### Changed
* **`VideoEngineRegistry.cs`** — Rewritten to V2-only (~150 lines from 1079). All V3/V3B routing, events, and BuildMetadata wiring removed.
* **`MainWindow.xaml.cs`** — All V3/V3B event handlers, preview/recording routing, stop paths, session summary appending, and JSON metadata building removed.
* **`dist/config/appsettings.json`** — `previewEngine: winrt`, `recordingEngine: windows_camera` (was both `opencv`).
* **`source/config/appsettings.json`** — Same as above.

### Tests
* **284/284 tests passing** (754 V3/V3B tests removed with deleted backends).

## [v1.2.21-alpha] - 2026-06-29 (build 236)

> **Windows Camera parity: immediate recording start and smoother preview during recording.** Three latency sources eliminated: (1) `OpenCvPreviewController` no longer runs a 3-second blocking FPS measurement before opening the VideoWriter — it uses the live preview FPS (`_liveFps`, tracked continuously during preview) directly, so OpenCV recording starts within milliseconds of click. (2) `CameraSlotPipeline.PrepareLowLagRecordingAsync` now applies focus and exposure camera controls in **parallel** (instead of sequentially) and with a **300 ms timeout** each (was 1500 ms each, potentially 3000 ms total); for cameras that do not support these controls (the common case) the parallel tasks complete in < 50 ms. (3) `StartRecordBtn_Click` now disables the button and updates UI state **before** awaiting any pending settings reapply task, so the user sees instant response at click time regardless of background state. Preview FPS cap during recording raised from 15 fps → 20 fps (below 1080p) and 10 fps → 15 fps (1080p+), matching Windows Camera preview smoothness. V2 stable recording behavior, CSV schema, and STABLE_CORE_V1 components unchanged.

### Changed
* **`OpenCvPreviewController.cs`** — Eliminated `MeasureCaptureFpsAsync()` blocking call at recording start. Writer FPS now resolved immediately from `_liveFps` (or `requestedFps` as fallback). `MeasureCaptureFpsAsync()` method retained but no longer called in the default recording path. `DefaultRecordingPreviewFpsCap` raised 15 → 20 fps; `HighResolutionRecordingPreviewFpsCap` raised 10 → 15 fps.
* **`CameraSlotPipeline.cs`** — `PrepareLowLagRecordingAsync`: focus and exposure camera controls run in parallel via `Task.WhenAll`; per-control timeout reduced 1500 ms → 300 ms. Pre-recording camera control latency reduced from up to 3 s → up to 300 ms.
* **`MainWindow.xaml.cs`** — `StartRecordBtn_Click`: `_recordUiBusy = true` and `UpdateUiStateFromCurrentState()` now called before awaiting the pending video-settings reapply task. Early-exit on `ConfirmPreRecordStorageOrCancel` properly resets `_recordUiBusy` and action buttons.

### Tests
* Updated `OpenCvRecordingQueuePolicyTests` expected values (15 → 20 for 720p, 10 → 15 for 1080p); added `ResolveRecordingPreviewFpsCap_BelowFullHd_Returns20`.
* **1038/1038 tests passing.**

## [v1.2.20-alpha] - 2026-06-29 (build 235)

> **Honest measured-FPS policy and classification.** New `MeasuredFpsPolicy` evaluator classifies timestamp-CSV measured FPS vs. requested FPS using six status levels (Pass / PassWithInfo / ConsistentLowerRealFps / DriverVfrBehavior / PassWithWarning / Fail). Policy: the app never inserts duplicate, ghost, or placeholder frames to reach the requested FPS. If the camera/driver consistently delivers slightly lower real FPS (e.g. 29.68 fps when 30 requested), this is classified as Pass — not a failure. Padding-prohibition constants (`NoArtificialFramePadding`, `NoDuplicateFramePadding`, `NoPlaceholderFrames`) are always `true`. Seven new `BackendMetadata` fields. `RecordingSelectionContext.WithMeasuredFps()` added for post-recording update. V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `MeasuredFpsPolicy.cs` — `RealFpsStabilityStatus` enum (Pass / PassWithInfo / ConsistentLowerRealFps / DriverVfrBehavior / PassWithWarning / Fail / NotEvaluated); `MeasuredFpsEvaluationResult` record (diff, percent diff, status, accepted flag, padding-prohibition constants, reason); `MeasuredFpsPolicy.Evaluate()` pure-logic evaluator (thresholds: ±0.6 fps → Pass, ±1.5 fps → PassWithInfo, ≤10% consistently lower → ConsistentLowerRealFps, driver VFR → DriverVfrBehavior, gaps/large drift → PassWithWarning or Fail; duplicate/placeholder frames → always Fail).
* `RecordingSelectionContext.MeasuredFpsEvaluation` property; `WithMeasuredFps(eval)` method for post-recording immutable update.
* 7 new `BackendMetadata` fields: `MeasuredFpsDiffFromRequested`, `MeasuredFpsPercentDiffFromRequested`, `RealFpsStabilityStatus`, `ConsistentLowerRealFpsAccepted`, `NoArtificialFramePadding` (always `true`), `NoDuplicateFramePadding` (always `true`), `NoPlaceholderFrames` (always `true`).
* 34 new unit tests in `MeasuredFpsPolicyTests`: exact/boundary FPS cases (29.5–30.1 → Pass), real-session cam1/2/3 29.68 fps → Pass, cam4 30.00 fps → Pass, PassWithInfo range, ConsistentLowerRealFps, DriverVfrBehavior (with and without VFR flag), PassWithWarning (gaps, drift), Fail (severe gaps, >15% drift), policy violations (duplicate/placeholder → always Fail), padding constants always true, zero requestedFps → NotEvaluated, diff/percent calculation, BackendMetadata defaults, `WithMeasuredFps` copy semantics, Theory test across real 4-camera session values.

## [v1.2.19-alpha] - 2026-06-28 (build 234)

> **Resolution/FPS/backend/GPU/autofocus selection hardening.** New pure-logic selection models (`ResolutionPresets`, `ResolutionSelectionPolicy`, `FpsSelectionPolicy`, `AutofocusPolicyReport`, `RecordingSelectionContext`) added in `V2SelectionHardeningModels.cs`. BackendMetadata extended with 30 new fields covering resolution preset tracking (RequestedResolutionPreset, RequestedWidth/Height, SelectedWidth/Height, ResolutionSelectionStatus, ResolutionFallbackUsed/Reason), FPS selection audit (RequestedFps, SelectedFps, WriterFps, MeasuredCameraFps, FpsSelectionStatus, FpsFallbackUsed/Reason, DriverVfrDetected), backend consistency (RequestedBackend, RecordingEngine), GPU/encoder hardening (GpuAccelerationAvailable, EncoderBackend, EncoderFallbackUsed/Reason), and autofocus policy confirmation (AutofocusControlSupported, AutofocusOffAttempted/Succeeded, AutofocusPolicyResult, ExposureControlSupported, ManualExposureUiAvailable=false, ManualFocusUiAvailable=false). `VideoEngineRegistry.SetRecordingSelectionContext()` added for callers to supply per-recording context. V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `V2SelectionHardeningModels.cs` — `ResolutionPresetDimensions` record; `ResolutionPresets` static class (1080p→1920×1080, 720p→1280×720, 360p→640×360, `ForLabel`, `IsKnownPreset`, `ForDimensions`); `ResolutionSelectionResult` record; `ResolutionSelectionPolicy.Evaluate()` (Exact/Fallback/Unavailable); `FpsSelectionResult` record; `FpsSelectionPolicy.Evaluate()` (±0.02 fps tolerance, DriverVfrDetected); `AutofocusPolicyReport` record with factory methods `NotSupported`, `FromAttempt`, `Unknown` (ManualExposureUiAvailable and ManualFocusUiAvailable always false); `RecordingSelectionContext` record; `RecordingSelectionContext.From()` factory.
* `VideoEngineRegistry.SetRecordingSelectionContext(RecordingSelectionContext)` — stores selection context for inclusion in the next `BuildMetadata()` call; `GetRecordingSelectionContext()` for inspection.
* 30 new `BackendMetadata` fields: `RequestedResolutionPreset`, `RequestedWidth`, `RequestedHeight`, `SelectedWidth`, `SelectedHeight`, `ResolutionSelectionStatus`, `ResolutionFallbackUsed`, `ResolutionFallbackReason`, `RequestedFps`, `SelectedFps`, `WriterFps`, `ContainerFps`, `MeasuredCameraFps`, `FpsSelectionStatus`, `FpsFallbackUsed`, `FpsFallbackReason`, `DriverVfrDetected`, `RequestedBackend`, `RecordingEngine`, `GpuAccelerationAvailable`, `EncoderBackend`, `EncoderFallbackUsed`, `EncoderFallbackReason`, `AutofocusControlSupported`, `AutofocusOffAttempted`, `AutofocusOffSucceeded`, `AutofocusPolicyResult`, `ExposureControlSupported`, `ManualExposureUiAvailable`, `ManualFocusUiAvailable`.
* 44 new unit tests in `V2SelectionHardeningTests`: `ResolutionPresets` (ForLabel, ForDimensions, IsKnownPreset — all 3 presets and edge cases), `ResolutionSelectionPolicy` (exact/fallback/unavailable/explicit-reason/dimension-preservation), `FpsSelectionPolicy` (exact/within-tolerance/fallback/DriverVfr/explicit-reason/zero/propagation), `AutofocusPolicyReport` (NotSupported/FromAttempt/ManualSliderNeverAvailable/ExposureControlPropagation), `BackendMetadata` default field values, `RecordingSelectionContext.From()` (resolution/fps/autofocus/backend/encoder propagation), real-session representative test (4-camera 1080p/30fps V2 session).

### Changed
* Version assertions updated in 5 test files: `1.2.18-alpha` → `1.2.19-alpha`.

## [v1.2.18-alpha] - 2026-06-28 (build 233)

> **Stop Recording UI — immediate stopped state.** After clicking Stop Recording, the main status panel now shows "Preview Active" immediately. The elapsed timer freezes at the stop-click time. "Finalizing MP4 files…", "Analyzing recording quality…", and "Stopping recording…" are no longer shown in the main status panel. Internal MP4 finalization, CSV writing, metadata writing, ffprobe audit, and visual quality analysis all still complete safely in a silent post-stop phase. Start Recording and Stop Recording buttons remain disabled during this phase; controls restore normally after finalization. V2 Stable recording behavior and STABLE_CORE_V1 are unchanged.

### Fixed
* **Stop Recording showed "Finalizing" / "Analyzing recording quality..." / "Finalizing MP4 files..." in main status panel** — root cause: `StopRecordBtn_Click` set `StatusValue.Text = "Finalizing"` and `ShowPreviewStatus(...)` was called three times inside `StopV2DefaultRecordingAsync` with transient messages. Fix: removed all three `ShowPreviewStatus` calls from the stop path; replaced the "Finalizing" status with "Preview Active" (the actual post-stop state); status panel protected from overwrite by `_isPostStopProcessing` guard during silent finalization.
* **`GetNeutralStatusText` had dead "Stopping" branch** — `_vm.StatusDisplay.StartsWith("Stopping")` path was never set via code; removed in v1.2.18-alpha.

### Changed
* `_isRecordingFinalizing` renamed to `_isPostStopProcessing` — clearer intent: UI is visibly stopped, internal finalization is silent.
* `_finalizedElapsed` renamed to `_postStopFrozenElapsed`.

### Added
* 30 new unit tests in `StopRecordingUiBehaviorTests` — covers stop-click status freeze, elapsed freeze, button state, no-Finalizing contract, no-Analyzing-recording contract, UpdateStatusDashboard guard, finalization success/exception recovery, double-click guard, V2/V3B policy contract, elapsed formatting.

## [v1.2.17-alpha] - 2026-06-28 (build 232)

> **V3B experimental 1–4 camera support + Stop Recording finalizing UI state.** V3B layout support expanded from 1-camera-only to 1–4 cameras experimental (`Alpha_v1217` singleton, `V3BRecordingPolicy.MultiCamera`). New multi-camera coordinator architecture: `V3BMultiCameraSession` (all-or-none start, best-effort stop, shared session start tick), `V3BMultiCameraVerifier` (session-level verdict), `V3BSessionTimingModel` (per-slot offsets and inter-camera spread), `V3BSlotRuntime` (per-slot recording runtime). 16 new `BackendMetadata` fields. `VideoEngineRegistry` updated to use `Alpha_v1217`. Stop Recording UI now enters a stable "Finalizing" state immediately on click: button disabled, elapsed frozen, status fixed until finalization completes. V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `V3BMultiCameraModels.cs` — `V3BSlotRuntime` (per-slot recording runtime with `ComputeDeviceIdHash`), `V3BSessionTimingModel` (per-slot offsets, inter-camera spread, reference camera selection, `Compute()` factory), `V3BMultiCameraStartResult`, `V3BMultiCameraStopResult`, `V3BMultiCameraDiagnostics`, `V3BMultiCameraVerificationResult`.
* `V3BMultiCameraVerifier.cs` — pure-logic session-level verifier. Runs per-slot `V3BVerifier.Verify()` and aggregates to session verdict: `Fail` if any slot has a fatal issue, `PassWithExperimentalWarning` if all pass, `NotRun` if no slots provided.
* `V3BMultiCameraSession.cs` — coordinator for 1–4 simultaneous `VideoEngineV3B` instances. All-or-none start policy (rolls back started slots on any failure), best-effort stop (continues even if one slot throws), shared monotonic session start tick captured before first slot.
* `V3BRecordingPolicy.MultiCamera` singleton — `MaxActiveCameras=4`, `V3BAlphaLimitation="ExperimentalMultiCamera_1to4"`, `TimestampCsvRequiredForSuccess=true`.
* `V3BLayoutSupport.Alpha_v1217` singleton — `LayoutSupportStatus="ExperimentalMultiCamera"`, `MaxSupportedActiveCameras=4`, `CurrentSupportedLayouts="1,2,3,4"`.
* `V3BFallbackReason.V3B_MaxCamerasExceeded`, `V3B_NoActiveCameras`, `SlotPrepareFailed` — new fallback reasons for multi-camera policy failures.
* 16 new `BackendMetadata` fields: `V3BMultiCameraMode`, `V3BActiveSlotCount`, `V3BDeviceIdHash`, `V3BSessionStartMonotonicTicks`, `V3BSlotStartMonotonicTicks`, `V3BSlotStartOffsetMs`, `V3BSlotStopOffsetMs`, `V3BInterCameraStartSpreadMs`, `V3BInterCameraStopSpreadMs`, `V3BSlotFinalized`, `V3BSlotAuditStatus`, `V3BSlotAuditWarnings`, `V3BSlotFrameRowsMatchWrittenFrames`, `V3BSlotContainerReadable`, `V3BSessionVerdict`, `V3BSessionNote`.
* **Stop Recording finalizing UI state** — `_isRecordingFinalizing` flag in `MainWindow`. On Stop click: button disabled immediately, elapsed frozen at click time, status set to "Finalizing", `UpdateStatusDashboard` and `ElapsedTimer_Tick` skip all updates. Flag cleared and UI restored after finalization completes or fails.
* 52 new unit tests: `V3BLayoutSupportV1217Tests` (20) + `V3BMultiCameraModelsTests` (32).

### Changed
* `V3BLayoutSupport` — `LayoutStatusFor()` now returns `"ExperimentalSupported"` / `"NotSupported"` (was `"Supported"` / `"NotYetSupported"` in v1.2.15-alpha); `NextPlannedLayout` updated to "Validation of each layout before scientific use".
* `V3BRecordingPolicy.CheckCameraCount(0)` — now returns `(false, V3B_NoActiveCameras)` instead of `(true, None)`.
* `VideoEngineRegistry` — V3B layout info now uses `Alpha_v1217`; status supplement shows "1–4 cameras (experimental)".
* Version assertions updated in 11 test files: `1.2.16-alpha` → `1.2.17-alpha`.

## [v1.2.16-alpha] - 2026-06-28 (build 231)

> **V3B 1-camera timing audit and verification hardening.** V3B post-recording timing audit models added (`V3BTimingAuditResult`, `V3BTimestampCsvAuditResult`, `V3BContainerCsvComparison`). `V3BCsvAuditor` pure-logic CSV structure auditor checks column schema, frameIndex sequence, monotonic ticks, sinkWriter sample times/durations, and v3bFrameStatus values. `V3BVerifier` extended with 7 new issue kinds including 3 new fatal checks (non-monotonic sample times, non-positive durations, writer-not-finalized-after-claimed-success). 9 new `BackendMetadata` fields. V3B audit runs automatically in `StopRecordingAsync`. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `V3BTimingAuditModels.cs` — `V3BTimingAuditStatus`, `V3BTimingAuditWarning` (flags), `V3BTimestampCsvAuditResult`, `V3BContainerCsvComparison`, `V3BTimingAuditResult` with `Evaluate(Inputs)` factory.
* `V3BCsvAuditor.cs` — Pure-logic CSV structure auditor. Validates required columns, frameIndex sequence, monotonicTicks strictly increasing, appTimestamp non-decreasing, sinkWriterSampleTime monotonic, sinkWriterSampleDuration positive, valid v3bFrameStatus values, and row count vs expected. No file I/O — caller passes `IReadOnlyList<string>`.
* `V3BRecordingDiagnostics.CsvFilePath`, `CsvAuditResult`, `TimingAuditResult` — V3B audit results stored on diagnostics after stop.
* `VideoEngineV3B.StopRecordingAsync` — runs CSV audit after CSV close, builds full `V3BTimingAuditResult` after MF session diagnostics.
* 7 new `V3BVerificationIssueKind` values: `NonMonotonicSampleTimes` (fatal), `NonPositiveSampleDurations` (fatal), `WriterNotFinalizedAfterClaimedSuccess` (fatal), `FfprobeUnavailable` (warning), `ContainerFrameCountMismatch` (warning), `ConversionFailuresOccurred` (warning), `DroppedBeforeWriterOccurred` (warning).
* `V3BVerifier.VerifyInputs` extended: `WriterFinalized`, `SampleTimesMonotonic`, `SampleDurationsPositive`, `FfprobeAvailable`, `ContainerFrameCountAvailable`, `ContainerFrameCount`, `ConversionFailures`, `DroppedBeforeWriter`.
* 9 new `BackendMetadata` fields: `V3BTimingAuditStatus`, `V3BTimingAuditWarnings`, `V3BTimingAuditMessage`, `V3BCsvRowCount`, `V3BCsvRowsMatchWrittenFrames`, `V3BCsvSampleTimesMonotonic`, `V3BCsvSampleDurationsPositive`, `V3BContainerFrameCountMatchesCsv`, `V3BExperimentalValidationNote`.
* 41 new unit tests: `V3BCsvAuditorTests` (18) + `V3BTimingAuditTests` (23).

## [v1.2.15-alpha] - 2026-06-28 (build 230)

> **Metadata consistency fix + V3B expansion readiness model.** `TimestampCsvStatus` in BackendMetadata now correctly shows `Written` for successful V2 sessions (was incorrectly `Skipped` in v1.2.14-alpha). V3B `V3BLayoutSupport` pure-logic model added: 1-camera only, 2/3/4 camera `NotYetSupported`. `V3BStatusSupplement` is now surfaced in the UI backend label; shows concise multi-camera refusal message when V3B is selected with >1 active camera. V2 Stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Fixed
* **`TimestampCsvStatus = Skipped` when CSV was written** — root cause: `VideoEngineV2.StopSlotRecordingAsync` never set `RecordingFinalizeResult.TimestampCsvRows`, so `BuildMetadata` always saw `TimestampCsvRows = 0`. Fix: V2 stop path now sets `TimestampCsvRows = pipeline.TimestampMonitorFrameCount`. Defensive fallback: `BuildMetadata` also considers `FramesWrittenDuringRecording > 0` when deriving status.

### Added
* `V3BLayoutSupport` record (`V3BRecordingModels.cs`) — pure-logic expansion readiness model. `LayoutSupportStatus`, `MaxSupportedActiveCameras`, `CurrentSupportedLayouts`, `NextPlannedLayout`, `MultiCameraReadinessReason`, `IsLayoutSupported(int)`, `LayoutStatusFor(int)`, `GetUnsupportedMessage()`. Singleton `Alpha_v1215` used by registry and UI.
* `BackendMetadata.V3BLayoutSupportStatus`, `V3BMaxSupportedActiveCameras`, `V3BCurrentSupportedLayouts`, `V3BNextPlannedLayout`, `V3BMultiCameraReadinessReason` — five new metadata fields for V3B layout expansion status.
* `VideoEngineRegistry.V3BStatusSupplement` — now surfaced in `UpdateBackendStatusLabel`; shows `GetUnsupportedMessage()` when multi-camera violation is active, otherwise full V3B status lines.
* 19 new unit tests: `V2TimestampCsvStatusTests` (8 tests — Written/Skipped logic, defensive fallback), `V3BLayoutSupportTests` (11 tests — IsLayoutSupported, LayoutStatusFor, policy alignment, concise message).

## [v1.2.14-alpha] - 2026-06-28 (build 229)

> **Post-audit hardening.** Stop-recording double-click race fully hardened with `StopRecordingGuard`. Frame counter scope clarified: preview-inclusive `FramesWritten` separated from recording-relative `FramesSubmittedSinceRecordingStart` and `FramesWrittenDuringRecording`. Audit report now issues an informational note (not a warning) when the preview-inclusive counter differs from recording-only CSV/ffprobe frames. V2 Stable recording behavior unchanged. STABLE_CORE_V1 untouched. 796 tests pass.

### Fixed
* **Stop Recording double-click race** — `StopRecordBtn_Click` now enters `StopRecordingGuard` (Interlocked CAS) before disabling the button. A second click while finalization is in progress is silently ignored. Guard resets in `finally` so exceptions never leave the UI permanently disabled.
* **Audit PASS_WITH_WARNING false positive** — mismatch warning for `FramesWritten` vs timestamp CSV rows was triggered by a preview-inclusive counter that includes frames before recording started. The comparison now uses the recording-relative `FramesSubmittedSinceRecordingStart`. When CSV and ffprobe agree within tolerance the session result stays PASS; an informational note documents the preview-inclusive divergence.
* **Misleading metadata label** — `[Timing]` section now outputs `Frames written (preview-inclusive)` and separate `Frames submitted since recording start` fields. `Timestamp rows match frames written` renamed to `Timestamp rows match recorded frames` and compares against the recording-relative counter.
* **`TimestampCsvStatus = Skipped` when CSV was written** — `timestampCsvStatus` is set from `result.TimestampCsvRows > 0`; no logic change needed (rows now flow correctly from `FramesWrittenDuringRecording`).

### Added
* `StopRecordingGuard` (`utils/StopRecordingGuard.cs`) — thread-safe re-entrancy guard for the stop-recording UI flow. `TryEnter()` / `Release()` via `Interlocked.CompareExchange`. Fully unit-testable without WPF.
* `RecordingFinalizeResult.FramesSubmittedSinceRecordingStart` — encoder's recording-relative frame count (reset at `StartAsync`; excludes preview frames).
* `RecordingFinalizeResult.FramesWrittenDuringRecording` — timestamp CSV row count at recording stop (== `TimestampCsvRows`; agrees with ffprobe frame count).
* `RecordingFinalizeResult.FrameCounterScope` — `"PreviewInclusive"` for `FramesWritten` (V2), `"Unknown"` for V3/V3B.
* `CameraHealthSnapshot.FramesSubmittedSincePreviewStart` — alias for `FramesDelivered` with explicit scope documentation.
* `MediaFoundationEncoderService.FramesSubmittedSinceRecordingStart` — named alias for `FramesSubmitted`; both reset in `StartAsync`.
* `CameraPipelineV2.EncoderFramesSubmittedSinceRecordingStart` — surfaces encoder's recording-relative count to `VideoEngineV2`.
* `CameraPipelineV2.TimestampMonitorFrameCount` — surfaces `FrameTimestampMonitor.FrameCount` (CSV rows written during recording).
* `BackendMetadata.V2FramesSubmittedSinceRecordingStart`, `V2FrameCounterScope`, `V2FramesWrittenDuringRecording` — three new metadata fields for V2 frame counter scope documentation.
* 6 new unit tests: `StopRecordingGuardTests` (guard TryEnter/Release/re-entrancy), `V2FrameCounterScopeTests` (mismatch detection, informational note logic, tolerance boundary).

### Changed
* `VideoEngineRegistry.BuildMetadata` BackendVersion bumped to `"1.2.14-alpha"`.

## [v1.2.13-alpha] - 2026-06-26 (build 228)

> **Experimental alpha.** Real V3B 1-camera frame acquisition path. `V3BFrameAcquisitionSession` (MediaCapture SharedReadOnly + MediaFrameReader → BGRA32). `UseRealFrameAcquisition` flag. `OnRawFrameAcquired` computes sample times from Stopwatch ticks. 9 new acquisition metadata fields. `ConversionFailed` frame status. V3B BackendVersion 3B.3.0-alpha. 790 tests pass. V2 Stable unchanged. STABLE_CORE_V1 untouched.

### Added
* `V3BFrameAcquisitionSession` — MediaCapture (SharedReadOnly) + MediaFrameReader → BGRA32; scored format selection; `FrameCallback` delivers frames to engine.
* `VideoEngineV3B.UseRealFrameAcquisition` (default `true`) — wires real acquisition session; `false` = test mode (caller submits via `SubmitFrameAsync`).
* `VideoEngineV3B.DeviceId` — camera selection for real acquisition.
* `VideoEngineV3B.OnRawFrameAcquired` (private) — computes sampleTime100ns from recording-relative ticks; measured duration or FPS fallback.
* `V3BFrameStatus.ConversionFailed` — BGRA32 conversion failed; no CSV row written.
* `V3BFrameTelemetryAggregator.ConversionFailures` counter.
* 9 new `BackendMetadata` V3B acquisition fields + 9 new `V3BRecordingDiagnostics` fields.
* 34 new tests → 790 total.

### Changed
* `VideoEngineV3B.BackendVersion` → `"3B.3.0-alpha"`.
* `StopRecordingAsync` stops acquisition before SinkWriter finalize.
* Removed unused `_prevSampleTime100ns` field (CS0414 resolved).
* Registry `BackendVersion` → `"1.2.13-alpha"`. Version → `1.2.13-alpha` / build `228`.

---

## [v1.2.12-alpha] - 2026-06-26 (build 227)

> **Experimental alpha.** First real V3B MP4 output. H.264 encoding via WinRT MediaStreamSource + MediaTranscoder. CPU-copy BGRA32 frame path. 1-camera only. Disabled by default. V3B still experimental — not validated for scientific timing-critical recordings. V2 Stable unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3BMfSinkWriterSession`** — real WinRT-based SinkWriter session:
  * `MediaStreamSource` with uncompressed BGRA32 stream descriptor
  * Bounded channel (capacity 16, backpressure) bridges push (frame-arrived) to pull (SampleRequested)
  * `MediaTranscoder` with `HardwareAccelerationEnabled = true` → H.264 MP4
  * Per-sample timestamps via `MediaStreamSample.Timestamp` (100ns units → `TimeSpan.Ticks`)
  * Basic container validation post-finalize (file existence + size check)
  * `WireInto(V3BSinkWriterRecorder)` wires delegates and sets `DelegatesWiredExplicitly = true`
* **`VideoEngineV3B.UseRealMediaFoundation`** property (default `true`):
  * When `true`: `StartRecordingAsync` creates `V3BMfSinkWriterSession` and wires it, unless delegates were explicitly injected (test mode)
  * When `false`: scaffold delegates remain (backward-compatible test behavior)
* **`V3BSinkWriterRecorder.MarkDelegatesWired()`** — marks delegates as explicitly set; prevents real MF wiring in `StartRecordingAsync`
* **`V3BRecordingStatus.SinkWriterUnavailable`** — real MF init attempted but failed (distinct from `SinkWriterScaffold` = delegates not wired)
* **13 new `BackendMetadata` V3B fields:**
  * `V3BSinkWriterAvailable`, `V3BSinkWriterInitialized`, `V3BSinkWriterFailureReason`
  * `V3BInputPixelFormat` ("BGRA32"), `V3BWriterPixelFormat` ("H264_MP4"), `V3BFrameConversionMode` ("CPU_COPY_BGRA32_TO_H264"), `V3BFrameConversionWarning`
  * `V3BContainerReadable`, `V3BContainerDurationSeconds`, `V3BContainerFrameCount`, `V3BContainerFps`, `V3BOutputFileSizeBytes`, `V3BCodecName`
* **13 new `V3BRecordingDiagnostics` fields** (mirrors BackendMetadata additions)
* **25 new tests** → 756 total

### Changed

* `VideoEngineV3B.BackendVersion` → `"3B.2.0-alpha"` (first real output path)
* `VideoEngineV3B.StartRecordingAsync`: wires real `V3BMfSinkWriterSession` by default; `SinkWriterUnavailable` status when MF fails; explicit-delegate check prevents clobbering test mocks
* `VideoEngineV3B.StopRecordingAsync`: populates container validation diagnostics from `V3BMfSinkWriterSession` after finalize
* `VideoEngineV3B.DisposeAsync`: disposes `_mfSession`
* `V3BSinkWriterRecorder` comment and state updated
* `VideoEngineRegistry.BuildMetadata`: populates 13 new V3B fields
* `docs/V3B_SINKWRITER_SCIENTIFIC_BACKEND.md`: updated with real implementation details
* Version → `1.2.12-alpha` / build `227`

### Unchanged

* VideoEngineV2 stable recording behavior
* V2 timestamp CSV schema
* V2 Video Verification verdict semantics
* Session output folder layout
* VideoEngineV3 LowLag
* `TimestampCsvStatus` for V3 (`NotAvailableForV3Alpha`)
* STABLE_CORE_V1 components

---

## [v1.2.11-alpha] - 2026-06-26 (build 226)

> **Experimental alpha.** VideoEngineV3B SinkWriter Scientific backend scaffold.
> 1-camera only. SinkWriter delegate-injected (scaffold — no real MP4/CSV in this alpha).
> V3B disabled by default. Full model, policy, telemetry, CSV, verifier, and diagnostics layers implemented.
> V2 Stable remains recommended for timing-critical scientific recordings. V2 behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **New namespace `MultiCamApp.Capture.VideoEngineV3B`** — 7 new files:
  * **`V3BRecordingModels.cs`** — `V3BRecordingStatus` (11 values), `V3BReadinessStatus` (9), `V3BFrameStatus` (7), `V3BFallbackReason` (10)
  * **`V3BRecordingPolicy.cs`** — immutable record; `EnabledByDefault = false`, `MaxActiveCameras = 1`, `NoDuplicates/NoPlaceholders/NoArtificialCfrPadding = true`; `CheckCameraCount()` pure check (before opening any resources); `Default` and `Developer` singletons
  * **`V3BFrameTelemetry.cs`** — `V3BFrameTelemetryRow`, `V3BFrameTelemetryAggregator` (frame/submit/write/drop/dup counts, interval stats, `RowCountMatchesWrittenFrames`, `IsDuplicateSampleTime()`), `V3BSampleTimeConverter` (ticks→100ns, fps→default duration)
  * **`V3BTimestampCsvWriter.cs`** — 13-column schema; `OpenAsync`/`WriteRowAsync`/`DisposeAsync`; row count tracked; `TimestampCsvStatus` = `WrittenForV3BAlpha` / `FailedForV3BAlpha` / `NotAvailable`; `TimestampSource = MediaFrameReaderAppTimestamp`
  * **`V3BVerificationModels.cs`** — `V3BVerificationVerdict` (4), `V3BVerificationIssueKind` (9), `V3BVerificationIssue` (IsFatal), `V3BVerificationResult` (FromIssues), `V3BVerifier` pure-logic static verifier (MP4/CSV/row-count/dup/placeholder checks; scaffold → PassWithExperimentalWarning; fatal issues → Fail)
  * **`V3BRecordingDiagnostics.cs`** — mutable diagnostics snapshot; `ApplyTelemetry()` populates from aggregator
  * **`V3BSinkWriterRecorder.cs`** — delegate-injected scaffold; `InitDelegate/SubmitSampleDelegate/FinalizeDelegate` default to `false`; explains real MediaFoundation COM path in comments
  * **`VideoEngineV3B.cs`** — orchestrator; one-camera check before opening resources; CSV open → SinkWriter init → frame submission loop → stop → telemetry; scaffold returns false + cleans up partial CSV; `SubmitFrameAsync` enforces no-duplicate policy; `StopRecordingAsync` idempotent; `DisposeAsync` safe

* **`BackendIds.VideoEngineV3B_SinkWriterScientific`** — new backend identifier constant
* **30 new `BackendMetadata` V3B fields** — all diagnostic/policy/timing/CSV fields listed in docs
* **`VideoEngineRegistry` V3B additions:**
  * `IsV3BSelected` property
  * `V3BStatusSupplement` property (UI status lines when V3B active)
  * `SelectBackend` handles `VideoEngineV3B_SinkWriterScientific` — creates `VideoEngineV3B` instance; no WinRT dependency at selection time
  * `TryStartV3BRecordingAsync` — routing method; returns false if V3B not active or policy refuses
  * `StopV3BRecordingAsync` — idempotent stop; returns `RecordingFinalizeResult`
  * `GetV3BEngine` — exposes engine for delegate injection before recording starts
  * `BuildMetadata` populates all 30 V3B fields from `_v3b.Diagnostics`
  * `FormatId` updated for V3B display name
  * `Dispose` releases V3B engine
* **`docs/V3B_SINKWRITER_SCIENTIFIC_BACKEND.md`** — architecture, why V3 LowLag cannot provide CSV, why V3B uses MFR+SinkWriter, 1-camera alpha limitation, timestamp CSV policy, scaffold wiring, V2 recommendation, metadata fields, verification, future plan
* **~95 new tests** in `VideoEngineV3BTests.cs` covering all classes, policies, lifecycle paths, CSV schema, verifier, metadata fields, supplement, routing, V2 stability

### Changed

* `BackendIdentifiers.cs` — `BackendIds` + `BackendMetadata` (30 new V3B fields)
* `VideoEngineRegistry.cs` — version → `"1.2.11-alpha"`; V3B routing + supplement + BuildMetadata
* Version files → `1.2.11-alpha` / build `226`
* `V3ExperimentalReadinessInputs` changed from `sealed class` to `sealed record` (enables `with` in tests — no behavior change)

### Tests

* **Prior 636 tests unchanged and passing**
* **95 new tests → 731 total**
* Version assertions updated in all prior test files → `"1.2.11-alpha"`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* VideoEngineV3 LowLag — kept, unchanged
* `TimestampCsvStatus` for V3 — remains `"NotAvailableForV3Alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.10-alpha] - 2026-06-26 (build 225)

> **Experimental alpha.** V3 experimental readiness layer. V3 is clearly labeled experimental.
> `TimestampCsvStatus` and `TimestampSource` remain `NotAvailableForV3Alpha`. Maximum achievable
> readiness level is `NotForScientificTiming` while timestamp CSV is not implemented.
> V2 Stable is recommended for timing-critical scientific recordings. V2 stable behavior unchanged.
> STABLE_CORE_V1 untouched.

### Added

* **`V3ExperimentalReadinessModels.cs`** — pure-logic readiness types:
  * `V3ExperimentalReadinessLevel` (6 values: NotAvailable / Unsafe / ExperimentalOnly / TestableWithWarning / ReadyForNonCriticalTesting / NotForScientificTiming)
  * `V3ExperimentalReadinessStatus` (14 values: Unknown / NotEvaluated / V3NotSelected / EnumerationFailed / NoDevicesFound / PreviewUnavailable / RecordingUnavailable / MappingLowConfidence / TimestampCsvUnavailable / SidecarConflict / SidecarNotProven / SidecarProbeSucceeded / FallbackActive / Evaluated)
  * `V3ExperimentalReadinessReason` (15 values: None / V3BackendNotSelected / NoDevicesEnumerated / EnumerationFailed / PreviewPipelineNotAvailable / RecordingPipelineNotAvailable / MappingConfidenceLow / MappingConfidenceUnknown / TimestampCsvNotImplemented / SidecarExclusiveControlConflict / SidecarNotProvenSafe / SidecarDisabledForAlphaSafety / SidecarProbeSuccess / FallbackToV2Active / AllBasicCapabilitiesAvailable)
  * `V3ExperimentalReadinessInputs` — plain-data inputs (IsV3Selected, BackendVersion, DetectedCameraCount, MappingConfidence, PreviewAvailable, RecordingCapable, ContainerValidationAvailable, TimestampCsvStatus, TimestampSource, TimestampSidecarSupportLevel, FallbackActive, EnumerationFailed)
  * `V3ExperimentalReadinessReport` — immutable report (ReadinessLevel, ReadinessStatus, PrimaryReason, AllReasons, + inputs echoed, RecommendedUse, NotRecommendedUse, ReadinessMessage, IsNotForScientificTiming)

* **`V3ExperimentalReadinessEvaluator.cs`** — pure-logic stateless evaluator:
  * `Evaluate(inputs)` — covers: V3 not selected → NotAvailable; no devices → Unsafe; no preview/recording → ExperimentalOnly; low mapping confidence → TestableWithWarning; all basic capabilities + CSV unavailable → NotForScientificTiming; all capabilities + CSV available → ReadyForNonCriticalTesting
  * Always sets `RecommendedUse` ("VideoEngineV3 is experimental and may be used for backend testing and non-critical recordings.")
  * Always sets `NotRecommendedUse` ("Use VideoEngineV2 Stable for timing-critical scientific recordings until V3 timestamp CSV support is implemented.")
  * Populates `AllReasons` with all applicable reasons including sidecar and CSV reason
  * Message includes Unknown mapping confidence note when relevant

* **6 new `BackendMetadata` fields** (v1.2.10-alpha):
  * `V3ExperimentalReadinessLevel` — computed by `V3ExperimentalReadinessEvaluator`
  * `V3ExperimentalReadinessStatus`
  * `V3ExperimentalReadinessReason`
  * `V3RecommendedUse`
  * `V3NotRecommendedUse`
  * `V3ReadinessMessage`

* **Session summary readiness section** in `V3SessionSummaryAppender.AppendAsync`:
  * Optional `V3ExperimentalReadinessReport? readiness` parameter (backward-compatible)
  * When provided: writes `V3 Experimental Readiness:` section with level/status/reason/message/recommended-use/not-recommended-use

* **`V3SlotSummaryEntry`** — 6 new readiness fields (ReadinessLevel, ReadinessStatus, ReadinessReason, RecommendedUse, NotRecommendedUse, ReadinessMessage); `FromValidation` extended with optional params (backward-compatible)

* **V3StatusSupplement timing advisory** — always shown when V3 is active:
  * `Timing: No V3 timestamp CSV in alpha`
  * `Rec: Use V2 Stable for scientific timing`

* **`docs/V3_EXPERIMENTAL_NOTES.md`** — developer/user documentation covering V3 capabilities, why timing is unavailable, recommended use, feature flags, readiness diagnostics, version history

* **~60 new tests** in `VideoEngineV3ReadinessTests.cs`:
  * Enum value completeness (ReadinessLevel/Status/Reason)
  * ReadinessReport defaults, IsNotForScientificTiming
  * Evaluator: V3 not selected, no devices, enumeration failed, no preview, no recording, low mapping confidence, failed mapping, CSV unavailable → NotForScientificTiming, CSV available → ReadyForNonCritical, recommended/not-recommended text, sidecar reasons, AllReasons always has TimestampCsvNotImplemented, report carries inputs, Unknown mapping note
  * BackendMetadata: default fields, V2 active → V3NotSelected/NotAvailable, V3 active → level not NotAvailable/recommendations set/readiness message set, NotRecommendedUse mentions V2, version = 1.2.10-alpha
  * V3StatusSupplement: V3 active → timing + V2 warning lines, V2 active → empty
  * Session summary: AppendAsync with readiness → writes section, without readiness → no section, always has timing note
  * V3SlotSummaryEntry: without readiness params → defaults, with params → fields set, readiness does not affect TimestampCsvStatus
  * V2 stable: default backend, fallback not used, evaluator V2 backend → NotAvailable

### Changed

* **`VideoEngineRegistry`** — version → `"1.2.10-alpha"`; `BuildMetadata` computes and populates 6 readiness fields; `V3StatusSupplement` adds timing advisory lines when V3 active

* **`BackendIdentifiers.cs`** — 6 new readiness fields on `BackendMetadata`

* **`V3SessionSummaryAppender`** — `AppendAsync` accepts optional `V3ExperimentalReadinessReport?`; readiness section written when provided; 6 new fields on `V3SlotSummaryEntry`; `FromValidation` extended

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.10-alpha / build 225

### Tests

* **Prior 586 tests unchanged and passing**
* **~60 new tests → ~646 total**
* Version assertions updated in `VideoEngineBackendTests`, `VideoEngineV3PreviewTests`, `VideoEngineV3TimestampSidecarTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* `V3RecordingVerifier` — unchanged
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"`
* `TimestampSource` — remains `"NotAvailableForV3Alpha"`
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.9-alpha] - 2026-06-26 (build 224)

> **Experimental alpha.** V3 timestamp sidecar feasibility probe. Probe is disabled by default
> (`EnableV3TimestampSidecarExperimental = false`). Probe enabled only under explicit developer
> opt-in. Does not write timestamp CSV. `TimestampCsvStatus` and `TimestampSource` remain
> `NotAvailableForV3Alpha`. Probe failure cannot corrupt recording. V3 is not scientifically
> timing-equivalent to V2. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3TimestampSidecarProbeModels.cs`** — pure-logic probe types:
  * `V3TimestampSidecarProbeStatus` (9 values: Disabled / NotAttempted / Attempted / Started / SamplesReceived / ConflictDetected / Failed / Cancelled / Timeout)
  * `V3TimestampSidecarProbePhase` (7 values: NotStarted / PolicyCheck / DeviceOpen / FrameReaderStart / SampleCollection / Cleanup / Completed)
  * `V3TimestampSidecarProbeFailureReason` (10 values: None / DisabledByPolicy / ConflictWithRecording / DeviceOpenFailed / FrameReaderStartFailed / NoSamplesReceived / ProbeTimeout / ExceptionDuringProbe / Cancelled / Unknown)
  * `V3TimestampSidecarProbeResult` — immutable result with static factories: `Disabled()`, `Conflict(reason)`, `Cancelled(durationMs)`. Fields: Enabled, Attempted, Started, SamplesReceived, SampleCount, FirstSampleUtc, LastSampleUtc, LongestGapMs, ProbeDurationMs, ConflictDetected, ConflictReason, ProbeStatus, ProbePhase, FailureReason, SupportLevel, SanitizedError, Note.

* **`V3TimestampSidecarProbe.cs`** — developer-only feasibility probe class:
  * `ProbeDurationMs` — bounded probe window (default 1500 ms)
  * `FrameSampleDelegate` — delegate injection (`Func<string, CancellationToken, Task<IReadOnlyList<long>>>`) for full unit-testability without WinRT
  * `RunAsync(deviceId, policy, ct)` — never throws; handles all cases:
    * `policy.Enabled = false` → `Disabled` (DisabledByPolicy; DisabledForAlphaSafety)
    * cancellation before probe → `Cancelled`
    * `AllowConcurrentWithRecording = false` → `ConflictDetected` (ConflictWithRecording; ConflictWithExclusiveRecording)
    * `OperationCanceledException` in delegate → `Cancelled` (caller CT) or `Timeout` (probe CT)
    * exception in delegate → `Failed` (ExceptionDuringProbe; FailedSafely); error sanitized
    * no samples returned → `Started` (NoSamplesReceived; Unknown)
    * samples returned → `SamplesReceived` (None; SupportedWithWarning); longest gap and UTC bounds computed

* **`VideoEngineRegistry.SetLastProbeResult(result)`** — stores probe result; null-safe (falls back to `Disabled()`)
* **`VideoEngineRegistry.GetLastProbeResult()`** — retrieves last stored probe result

* **9 new `BackendMetadata` fields** (v1.2.9-alpha):
  * `V3TimestampSidecarProbeStatus` — "Disabled" when V3 + default; "NotAvailable" when V2
  * `V3TimestampSidecarProbeAttempted` (bool)
  * `V3TimestampSidecarProbeSamplesReceived` (bool)
  * `V3TimestampSidecarProbeSampleCount` (int)
  * `V3TimestampSidecarProbeDurationMs` (long)
  * `V3TimestampSidecarProbeConflictDetected` (bool)
  * `V3TimestampSidecarProbeFailureReason` — "None" by default
  * `V3TimestampSidecarProbeSupportLevel` — "DisabledForAlphaSafety" by default
  * `V3TimestampSidecarProbeNote` — advisory message

* **`V3SlotSummaryEntry`** — 9 new probe fields; `FromValidation` extended with optional probe params (backward-compatible)

* **Session summary probe lines** in `V3SessionSummaryAppender.AppendAsync`:
  * `V3 Sidecar Probe: {status} (Attempted: {bool})`
  * `V3 Sidecar Probe Support Level: {level}`
  * `V3 Sidecar Probe Samples: {count} in {ms} ms` (when samples received)
  * `V3 Sidecar Probe: ExclusiveControl conflict detected. Recording unaffected.` (when conflict)
  * `V3 Sidecar Probe Note: {note}` (when attempted + note set)

* **~60 new tests** in `VideoEngineV3TimestampSidecarProbeTests.cs`:
  * Enum value completeness (ProbeStatus/ProbePhase/ProbeFailureReason)
  * ProbeResult static factories (Disabled, Conflict, Cancelled) and default constructor
  * `V3TimestampSidecarProbe.RunAsync`: disabled, not-attempted, conflict, cancelled-before-start, timeout, exception (sanitized error), no-samples, samples-received (count/duration/UTC bounds/longest gap/one-sample edge case)
  * TimestampCsvStatus remains NotAvailableForV3Alpha after samples received
  * Probe failure/conflict does not mark recording slot failed
  * Support level mapping for all outcomes
  * BackendMetadata: default probe fields, V3 active + default → Disabled, V2 active → NotAvailable, conflict result, samples-received result, SetNull fallback, GetLastProbeResult default + after set
  * V3SlotSummaryEntry: probe defaults, conflict params, samples params, probe fields do not affect TimestampCsvStatus
  * V3Verifier: missing CSV still ExperimentalWarning after probe with samples, after probe conflict, missing MP4 still Fail

### Changed

* **`VideoEngineRegistry`** — version bumped to `"1.2.9-alpha"`; `BuildMetadata` populates 9 new probe fields from `_lastProbeResult`; added `SetLastProbeResult`/`GetLastProbeResult`

* **`V3SessionSummaryAppender`** — 9 new probe fields on `V3SlotSummaryEntry`; probe lines in `AppendAsync`

* **`BackendIdentifiers.cs`** — 9 new probe fields on `BackendMetadata`

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.9-alpha / build 224

### Tests

* **Prior 541 tests unchanged and passing**
* **~60 new tests → ~601 total**
* Version assertions updated in `VideoEngineBackendTests`, `VideoEngineV3PreviewTests`, `VideoEngineV3TimestampSidecarTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* `V3RecordingVerifier` — missing CSV remains ExperimentalWarning; MP4 missing remains Fail
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"` regardless of probe result
* `TimestampSource` — remains `"NotAvailableForV3Alpha"` regardless of probe result
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.8-alpha] - 2026-06-26 (build 223)

> **Experimental alpha.** V3 timestamp sidecar feasibility layer. Sidecar is disabled by default
> (`EnableV3TimestampSidecarExperimental = false`). `TimestampCsvStatus` and `TimestampSource`
> remain `NotAvailableForV3Alpha`. Sidecar failure cannot corrupt recording. V3 is not
> scientifically timing-equivalent to V2. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3TimestampSidecarModels.cs`** — pure-logic timestamp sidecar types:
  * `V3TimestampSidecarStatus` (9 values: Disabled / NotAttempted / Probing / Running / Completed / ConflictDetected / Unsupported / Failed / Cancelled)
  * `V3TimestampSidecarSupportLevel` (7 values: Unknown / DisabledForAlphaSafety / Supported / SupportedWithWarning / ConflictWithExclusiveRecording / UnsupportedByDriver / FailedSafely)
  * `V3TimestampSidecarFailureReason` (9 values: None / DisabledByPolicy / ProbeConflict / DeviceOpenFailed / FrameReaderStartFailed / FrameReaderFailed / ExclusiveControlConflict / Cancelled / Unknown)
  * `V3TimestampSidecarPolicy` — immutable policy (Enabled=false default; AllowConcurrentWithRecording=false; FailRecordingIfSidecarFails always false; `Disabled` and `DevTest` static factories)
  * `V3TimestampSidecarDiagnostics` — mutable per-slot diagnostics with `RecordSample()` (tracks samples + longest gap), `RecordError()` (sanitised), `Clone()`
  * `V3TimestampSidecarFrameSample` — per-frame timestamp sample (SlotIndex, TimestampMs, FrameIndex, Note)

* **`V3TimestampSidecarSettings.cs`** — developer-only feature flags:
  * `EnableV3TimestampSidecarExperimental` = **false** (default; must remain false in release builds)
  * `GetEffectivePolicy()` — returns `Disabled` policy when flag is false; `DevTest` when true

* **`V3TimestampSidecar.cs`** — sidecar skeleton class:
  * `StartAsync(deviceId, policy, ct)` — disabled policy → Disabled status; cancelled → Cancelled; ExclusiveControl conflict → ConflictDetected; probe scaffold → NotAttempted; never throws
  * `StopAsync(ct)` — no-op when not Running; transitions Running → Completed
  * `RecordFrameSample(timestampMs)` — no-op when not Running; no CSV written in this alpha patch
  * `GetDiagnostics()` — returns diagnostics clone
  * `DisposeAsync()` — safe/idempotent; no WinRT resources in this scaffold
  * Recording is never failed due to sidecar outcome (`FailRecordingIfSidecarFails` always false)

* **7 new `BackendMetadata` fields** (v1.2.8-alpha):
  * `V3TimestampSidecarEnabled` (bool)
  * `V3TimestampSidecarStatus` — "Disabled" when V3 + sidecar off; "NotAvailable" when V2
  * `V3TimestampSidecarSupportLevel` — "DisabledForAlphaSafety" when disabled; "NotAvailable" when V2
  * `V3TimestampSidecarFailureReason` — "DisabledByPolicy" when disabled; "NotAvailable" when V2
  * `V3TimestampSidecarSamplesCaptured` (int)
  * `V3TimestampSidecarLongestGapMs` (long)
  * `V3TimestampSidecarNote` — advisory message; set for V3, empty for V2

* **`V3SlotSummaryEntry`** — 6 new timestamp sidecar fields; `FromValidation` extended with optional sidecar params (backward-compatible)

* **Session summary sidecar lines** in `V3SessionSummaryAppender.AppendAsync`:
  * `V3 Timestamp Sidecar: Disabled [Disabled]` (or status when active)
  * `V3 Timestamp Sidecar Support Level: DisabledForAlphaSafety`
  * `V3 Timestamp Sidecar Samples: {n}` (when > 0)
  * `V3 Timestamp Sidecar Note: {note}` (when set)

* **~55 new tests** in `VideoEngineV3TimestampSidecarTests.cs`:
  * Enum value completeness (Status/SupportLevel/FailureReason)
  * Policy defaults and static factories
  * Settings feature flag default false, GetEffectivePolicy
  * Diagnostics: defaults, RecordSample count/gap tracking, RecordError sanitization, Clone independence
  * FrameSample field storage
  * Skeleton: StartAsync disabled/conflict/cancelled/not-attempted, StopAsync transitions, RecordFrameSample guards, GetDiagnostics clone, DisposeAsync idempotent, StartAsync after dispose
  * Failure isolation: sidecar conflict/failure does not set recording state to failed
  * TimestampCsvStatus remains NotAvailableForV3Alpha when sidecar disabled (V3 active)
  * BackendMetadata: default safe values, V2 active shows NotAvailable, V3 note is set, version = "1.2.8-alpha"
  * V3SlotSummaryEntry: without sidecar params → defaults, with conflict fields, samples populated
  * V3Verifier: missing CSV still ExperimentalWarning (unchanged), WarnOnMissingTimestampCsv=false suppresses, missing MP4 still Fail

### Changed

* **`VideoEngineRegistry`** — version bumped to `"1.2.8-alpha"`; `BuildMetadata` populates 7 new sidecar fields; reads `V3TimestampSidecarSettings.EnableV3TimestampSidecarExperimental`

* **`V3SessionSummaryAppender`** — 6 new sidecar fields on `V3SlotSummaryEntry`; sidecar lines in `AppendAsync`

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.8-alpha / build 223

### Tests

* **Prior 494 tests unchanged and passing**
* **~55 new tests → ~549 total**
* Version assertion updated in `VideoEngineBackendTests` and `VideoEngineV3PreviewTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* `V3RecordingVerifier` — missing CSV remains ExperimentalWarning; MP4 missing remains Fail
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"` for V3 when sidecar disabled
* `TimestampSource` — remains `"NotAvailableForV3Alpha"` for V3 when sidecar disabled
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.7-alpha] - 2026-06-26 (build 222)

> **Experimental alpha.** V3 recording lifecycle hardening. Idempotent start/stop,
> safe multi-slot cleanup, per-slot output integrity tracking, stress cycler with
> delegate injection, 4 new BackendMetadata output integrity fields, output integrity
> lines in session_summary.txt. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3RecordingStressModels.cs`** — pure-logic stress model types:
  * `V3RecordingStressFailureKind` (10 values: None / RecordingStartFailed / RecordingStopFailed / OutputFileMissing / OutputFileEmpty / FfprobeReadFailed / ZeroDuration / PreviewSuspendFailed / PreviewResumeFailed / Unknown)
  * `V3RecordingStressCycleResult` — per-cycle result tracking all start/stop/output/preview suspension fields; `IsSuccess` = start + stop + non-empty MP4
  * `V3RecordingStressSummary` — aggregated summary (TotalCycles, SuccessfulCycles, FailedCycles, MissingMp4Count, EmptyMp4Count, FfprobeFailureCount, ZeroDurationCount, PreviewSuspendCount, PreviewResumeSuccessCount); `Aggregate()` static factory; `SuccessRate`; `AllCyclesPassed`

* **`V3RecordingStressCycler.cs`** — developer-only stress cycler with delegate injection:
  * All I/O via injected delegates: `StartRecordingDelegate`, `StopRecordingDelegate`, `AuditDelegate`, `SuspendPreviewDelegate`, `ResumePreviewDelegate`
  * Testable without real cameras; pattern mirrors `V3PreviewStressCycler` (v1.2.3-alpha)
  * `RunCycleAsync` — single-slot recording cycle: suspend preview → start → stop → resume preview → audit
  * `RunMultipleCyclesAsync` — repeated cycles with cancellation support; returns `V3RecordingStressSummary`
  * Preview resume always attempted after stop when suspended, regardless of stop outcome
  * NOT staged to public installer

* **Per-slot output integrity fields** on `V3RecordingSlotState`:
  * `LastOutputValidationStatus` (string) — "NotRun" / "FileSizeOk" / "FileEmpty" / "FileMissing" / "StopFailed" / "Unknown"
  * `LastOutputFileSizeBytes` (long)
  * `LastOutputDurationSeconds` (double)
  * Populated in `StopSlotRecordingAsync` after each successful stop

* **4 new `BackendMetadata` fields** (v1.2.7-alpha):
  * `V3LastOutputValidationStatus` — mirrors slot state; "NotRun" when V2 active or no stop yet
  * `V3LastOutputFileSizeBytes`
  * `V3LastOutputDurationSeconds`
  * `V3StressValidationNote` — optional developer note from stress run

* **`V3SlotSummaryEntry`** — 4 new output integrity fields: `OutputValidationStatus`, `OutputFileSizeBytes`, `OutputDurationSeconds`, `StressValidationNote`; `FromValidation` signature extended (backward-compatible optional params)

* **Session summary output integrity lines** in `V3SessionSummaryAppender.AppendAsync`:
  * `V3 Output Validation Status: {status}` when not "NotRun"
  * `V3 Output File Size: {KB}` when > 0
  * `V3 Output Duration (clock): {s}` when > 0
  * `V3 Stress Validation: {note}` when set

* **46 new tests** in `VideoEngineV3RecordingHardeningTests.cs`:
  * `V3RecordingStressFailureKindTests` — all 10 values verified
  * `V3RecordingStressCycleResultTests` — IsSuccess logic (start/stop/mp4 required)
  * `V3RecordingStressSummaryTests` — Aggregate empty/all-success/mixed, preview tracking, zero-duration counting
  * `V3RecordingStressCyclerTests` — start fail, stop fail, success recording-only, preview suspension/resume, resume failure isolates from recording, file missing, file empty, ffprobe unavailable, start throws, multi-cycle aggregation, cancellation
  * `V3RecordingSlotStateOutputIntegrityTests` — default values, field mutation
  * `VideoEngineV3HardeningTests` — StopAll no-op, double stop, dispose state, empty filesets
  * `BackendMetadataOutputIntegrityTests` — defaults "NotRun", V2 active defaults
  * `V3SlotSummaryEntryOutputIntegrityTests` — FromValidation without/with integrity params
  * `V3RecordingPerSlotFailureIsolationTests` — slot state isolation, diagnostics clone independence, combined state isolation

### Changed

* **`VideoEngineV3.StartSlotRecordingAsync`** — idempotent: returns immediately (no-op) if slot is already in `Recording` state; prevents duplicate recorder creation on the same slot

* **`VideoEngineV3.StopSlotRecordingAsync`** — populates `LastOutputValidationStatus`, `LastOutputFileSizeBytes`, `LastOutputDurationSeconds` on slot state after each stop; file-size check is best-effort (errors → "Unknown")

* **`VideoEngineV3.StopAllSlotsRecordingAsync`** — each per-slot task now runs under `CancellationToken.None`; inner exceptions are caught and returned as failure results, not propagated to `Task.WhenAll`; app-close cleanup is safe even when `ct` is already cancelled

* **`VideoEngineRegistry`** — version bumped to `"1.2.7-alpha"`; `BuildMetadata` populates 4 new output integrity fields from first stopped V3 slot

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.7-alpha / build 222

### Tests

* **Prior 449 tests unchanged and passing**
* **46 new tests → 495 total**
* Version assertion updated in `VideoEngineBackendTests` and `VideoEngineV3PreviewTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"` for V3

---

## [v1.2.6-alpha] - 2026-06-26 (build 221)

> **Experimental alpha.** V3 combined preview + recording lifecycle coordination.
> V3 preview is suspended before recording starts (ExclusiveControl safety policy).
> Preview resume attempted after recording stops. Resume failure does not affect recording result.
> Recording priority is always higher than preview priority.
> V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3CombinedLifecycleModels.cs`** — pure-logic combined lifecycle types:
  * `V3CombinedModeStatus` — NotActive / PreviewOnly / RecordingOnly / PreviewAndRecording / PreviewSuspendedForRecording / Failed / Fallback.
  * `V3PreviewRecordingConflictReason` — ExclusiveControlRequired / SafetyDefault / PreviewNotActive / Unknown.
  * `V3CombinedSlotState` — per-slot tracking: PreviewWasRunningBeforeRecording, PreviewSuspendedForRecording, PreviewResumeAttempted/Succeeded, ConflictReason, LastCombinedError.
  * `V3CombinedSession` — session-level snapshot: SuspendedSlots, ResumedSlots, FailedSlots.
  * `V3CombinedDiagnostics` — counters: suspension count, resume attempt/success/failure counts.
* **`VideoEngineV3`** — new combined lifecycle methods:
  * `SuspendPreviewForRecordingAsync` — stops preview if running before recording (ExclusiveControl policy); marks PreviewSuspendedForRecording.
  * `OnRecordingStartedAfterSuspension` — updates combined status after recording starts.
  * `ResumePreviewAfterRecordingAsync` — tries to restart preview after recording; failure isolated from recording result.
  * `GetCombinedSlotState`, `GetCombinedSession`, `GetCombinedDiagnostics`, `ResetCombinedSlotState`.
* **`VideoEngineRegistry`** — new combined lifecycle routing:
  * `TryStartV3SlotRecordingWithPreviewCoordinationAsync` — replaces `TryStartV3SlotRecordingAsync` at call sites; detects and suspends V3 preview before recording.
  * `StopV3SlotRecordingWithPreviewRecoveryAsync` — replaces `StopV3SlotRecordingAsync`; attempts preview resume after recording stops.
  * `SetUiDispatcher` — stores dispatcher for preview resume.
  * `GetV3CombinedSlotState`, `GetV3CombinedSession`, `GetV3CombinedDiagnostics`.
* **7 new `BackendMetadata` fields** (v1.2.6-alpha): V3CombinedStatus, V3PreviewWasRunningBeforeRecording, V3PreviewSuspendedForRecording, V3PreviewResumeAttemptedAfterRecording, V3PreviewResumeSucceededAfterRecording, V3PreviewRecordingConflictReason, V3CombinedLifecycleWarning.
* **`V3SlotSummaryEntry`** — 7 new combined lifecycle fields; `FromValidation` accepts optional `V3CombinedSlotState`.
* **`V3SessionSummaryAppender`** — outputs combined lifecycle section per slot when preview was suspended.

### Changed

* `MainWindow` — `StartOneSlotRecordingAsync` calls `TryStartV3SlotRecordingWithPreviewCoordinationAsync`; `StopV2AllSlotsRecordingAsync` calls `StopV3SlotRecordingWithPreviewRecoveryAsync`.
* `MainWindow` — TXT metadata includes V3 combined lifecycle section; JSON metadata includes `v3CombinedLifecycle` block.
* `VideoEngineRegistry.V3StatusSupplement` — shows `[preview suspended]` tag and preview resume failure note.
* `VideoEngineRegistry.BuildMetadata` — version bumped to `"1.2.6-alpha"`.
* `VideoEngineV3.GetSlotPipelineState` — returns `Recording` when slot is in PreviewSuspendedForRecording state.
* Tests — `VideoEngineBackendTests` and `VideoEngineV3PreviewTests` version assertions updated to `"1.2.6-alpha"`.

### Tests

* Added `VideoEngineV3CombinedLifecycleTests.cs` — 40 new pure-logic tests.

### Unchanged

* VideoEngineV2 stable recording behavior: unchanged.
* V2 timestamp CSV schema: unchanged.
* V2 Video Verification verdict semantics: unchanged.
* Session output folder layout: unchanged.
* STABLE_CORE_V1 (VideoVerificationService, MetadataParser, VideoScanner, RecordingTimingMetrics, SessionComparisonService, MetadataWriter, RecordingSession, RecordingDiagnosticsMonitor, ScientificTimingAssessor, AppConfig, SessionSummaryWriter, MetadataCompletenessPolicy): untouched.
* TimestampCsvStatus = NotAvailableForV3Alpha: unchanged.

## [v1.2.5-alpha] - 2026-06-26 (build 220)

> **Experimental alpha.** V3 recording audibility pass.
> Adds post-recording container validation, V3RecordingVerifier, V3 session summary appendix.
> Missing timestamp CSV = ExperimentalWarning (not FAIL) for V3 alpha recordings.
> V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3ContainerValidation.cs`** — pure-logic result model: `V3ContainerValidationStatus` (NotRun/Valid/ValidWithWarning/Invalid), `V3ContainerValidation` record (FileExists, FileSizeBytes, FfprobeReadable, DurationSeconds, FrameCount, FpsTag, CodecName, Width, Height, PixelFormat, ValidationStatus). Factory helpers: FileMissing, FileEmpty, FfprobeUnavailable, FfprobeFailed, NotRun.
* **`V3PostRecordingAudit.cs`** — stateless async runner: wraps `V2PostRecordingAudit.RunFfprobeAsync`, produces `V3ContainerValidation`. Never throws; all failures captured in result.
* **`V3RecordingVerifier.cs`** — independent V3 verifier (NOT modifying STABLE_CORE_V1):
  * `V3VerificationVerdict` — Pass / PassWithExperimentalWarning / Fail.
  * `V3VerificationIssue` — Severity: Info / ExperimentalWarning / Fail.
  * Missing timestamp CSV → `ExperimentalWarning` (not `Fail`); corrupt/missing MP4 → `Fail`.
  * Includes explicit wording: "Per-frame timestamp CSV is not available in V3 alpha backend."
  * `V3VerificationOptions.Default` — RequireMp4Exists=true, WarnOnMissingTimestampCsv=true.
* **`V3SessionSummaryAppender.cs`** — appends V3 experimental section to `session_summary.txt` after V2 writer finishes. Includes: per-slot recording status, timestamp unavailable policy note, MP4 readability, codec, duration, verification verdict. Does NOT modify `SessionSummaryWriter` (STABLE_CORE_V1).
* **6 new `BackendMetadata` fields** (v1.2.5-alpha): V3ContainerReadable, V3ContainerDurationSeconds, V3ContainerFrameCount, V3ContainerFps, V3CodecName, V3PostRecordingValidationStatus.

### Changed

* `VideoEngineRegistry.BuildMetadata` — new optional `V3ContainerValidation? v3Container` parameter; populates 6 new container fields; `BackendVersion = "1.2.5-alpha"`.
* `MainWindow.StopV2AllSlotsRecordingAsync` — Phase 3b runs `V3PostRecordingAudit.RunAsync` for V3 slots; Phase 3c runs `V3RecordingVerifier`; Phase 7 appends V3 session summary section; V3 slots skip duplicate/VQ detection.
* `MainWindow.WriteV2SlotMetadataAsync` — accepts `V3ContainerValidation?`; passes to `BuildMetadata` and `BuildBackendMetadataJson`; writes V3 container validation lines to TXT metadata.
* `MainWindow.BuildBackendMetadataJson` — accepts `V3ContainerValidation?`; adds `v3ContainerValidation` block to JSON output.
* Metadata TXT — V3 container validation section (readable, duration, codec, FPS, frame count).
* Metadata JSON — `backendInfo.v3ContainerValidation` object.

### Verification policy clarification

* V3 alpha missing timestamp CSV: `ExperimentalWarning` — not `Fail`. Explicitly documented in issues list with recommendation to use V2 stable for timing-critical recordings.
* V2 missing timestamp CSV: unchanged — still `Fail` per existing `V2RecordingVerifier` rules.
* Corrupt/missing V3 MP4: `Fail` — this is always a recording failure.

---

## [v1.2.4-alpha] - 2026-06-25 (build 219)

> **Experimental alpha.** First VideoEngineV3 recording pipeline.
> V3 recording uses MediaCapture + LowLagMediaRecording for H.264 MP4 output.
> Per-frame timestamp CSV not available from this path.
> V3 recording failure falls back to V2 or reports ExperimentalWarning.
> V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **V3RecordingModels.cs** — pure-logic types (no WinRT):
  * `V3RecordingState` enum — 7 states (NotStarted/Preparing/Starting/Recording/Stopping/Stopped/Failed).
  * `V3RecordingStatus` enum — session-level (NotStarted/Recording/PartiallyRecording/Stopped/Failed).
  * `V3RecordingFailureReason` enum — 12 values classifying recording failures by cause.
  * `V3RecordingStateValidator` — 12 valid transitions, `Describe()`, `SanitiseError()`.
  * `V3RecordingSlotState` — mutable per-slot state (DeviceKey, OutputMp4Path, FramesCaptured, etc.).
  * `V3RecordingSession` — session snapshot across all slots.
  * `V3RecordingDiagnostics` — session counters (StartAttempts, SuccessfulStarts, FailureCount, etc.) + `Clone()`.
* **V3SlotRecorder.cs** — per-slot V3 recording pipeline (WinRT-dependent):
  * `InitialiseAsync(deviceId, tempOutputPath)` — opens MediaCapture (ExclusiveControl, Video-only), creates StorageFile.
  * `StartAsync()` — builds `MediaEncodingProfile.CreateMp4(Auto)` (no audio), calls `PrepareLowLagRecordToStorageFileAsync`, starts recording.
  * `StopAsync(finalOutputPath)` — calls `FinishAsync()`, renames temp → final, returns `RecordingFinalizeResult`. Never throws.
  * `RecordingFailed` event — fired on `MediaCapture.Failed` during active recording.
  * `DisposeAsync()` — safe cleanup; deletes corrupt temp file if present.
* **11 new BackendMetadata fields** (v1.2.4-alpha): V3RecordingStatus, V3RecordingFailureReason, V3RecordingFallbackRecommended, V3RecordingFallbackReason, V3RecordingStartAttempts, V3RecordingSuccessfulStarts, V3RecordingStopAttempts, V3RecordingSuccessfulStops, V3RecordingFailureCount, TimestampSource, TimestampCsvStatus.

### Changed

* `VideoEngineV3` — recording stubs replaced with real pipeline:
  * `IsRecordingCapable = true` (was false).
  * `BackendVersion = "3.4.0-alpha"`.
  * `StartSlotRecordingAsync` — initialises `V3SlotRecorder`, starts recording, fires `SlotRecordingFailed` on failure.
  * `StopSlotRecordingAsync` — finishes recording, returns `RecordingFinalizeResult`.
  * `StopAllSlotsRecordingAsync` — stops all active slots in parallel; per-slot failures isolated.
  * `GetSlotPipelineState` — now returns `Recording` when V3 recording active, `Previewing` when V3 preview active.
  * `GetRecordingSession()`, `GetRecordingDiagnostics()` — new public accessors.
  * `SlotRecordingFailed` event added.
* `VideoEngineRegistry` — V3 recording routing:
  * `IsV3RecordingPreferred` — true when V3 active + confidence High/Medium.
  * `IsV3SlotPreviewActive(slot)` — exposes per-slot V3 preview active flag.
  * `IsV3SlotRecordingActive(slot)` — per-slot V3 recording active flag.
  * `TryStartV3SlotRecordingAsync(slot, deviceId, fileSet)` — sets device ID, starts V3 recording, returns bool.
  * `StopV3SlotRecordingAsync(slot, fileSet)` — stops V3 recording, returns `RecordingFinalizeResult`.
  * `GetV3RecordingSession()`, `GetV3RecordingDiagnostics()` — session and diagnostic snapshots.
  * `SlotRecordingFailed` event relay.
  * `BuildMetadata` → `BackendVersion = "1.2.4-alpha"` + 11 new recording/timestamp fields.
  * `V3StatusSupplement` — now shows V3 recording status line.
* `MainWindow` — V3 recording integration:
  * `_v3SlotRecordingActive[]` field tracks which slots are using V3 recording.
  * Active slot detection now includes `IsV3SlotPreviewActive(i)` so V3-preview-only slots are included.
  * `StartOneSlotRecordingAsync` — tries V3 first via `TryStartV3SlotRecordingAsync`; falls back to V2 if failed.
  * `StopV2AllSlotsRecordingAsync` — routes V3 slots to `StopV3SlotRecordingAsync`; V2 slots unchanged.
  * `V3SlotRecordingFailed` subscription + `OnV3SlotRecordingFailed` handler → `UpdateBackendStatusLabel`.
  * `BuildBackendMetadataJson` — includes `v3Recording` block and `timestampSource`/`timestampCsvStatus` fields.
  * TXT metadata — V3 recording fields and timestamp source written to `[Recording Backend — camN]` section.

### Timestamp / verification policy

* V3 alpha recording: `TimestampSource = NotAvailableForV3Alpha`, `TimestampCsvStatus = NotAvailableForV3Alpha`.
* No per-frame CSV rows are written for V3 recording. No fake rows.
* Verification for V3 recordings: ffprobe still runs for MP4 readability; timestamp CSV check is skipped with explicit warning in metadata.
* V2 recording: `TimestampSource = V2_FrameTimestampMonitor`; existing timestamp behavior unchanged.

---

## [v1.2.3-alpha] - 2026-06-25 (build 218)

> **Experimental alpha.** VideoEngineV3 preview lifecycle hardening.
> Idempotent start/stop, startup timeout watchdog, per-slot failure isolation, session diagnostics.
> V3 recording still NOT implemented. STABLE_CORE_V1 untouched. VideoEngineV2 recording unchanged.

### Added

* **V3PreviewDiagnostics** — new file `V3PreviewDiagnostics.cs` (pure-logic, no WinRT):
  * `V3PreviewFailureReason` enum — 10 values classifying preview failures by cause.
  * `V3PreviewStateValidator` — pure-logic HashSet of 10 valid (From, To) state transitions; `IsValid()`, `Describe()`, `SanitiseError()` (strips 20+ char hex IDs for privacy).
  * `V3PreviewSessionDiagnostics` — session-level counters: StartAttempts, SuccessfulStarts, StopAttempts, SuccessfulStops, FailureCount, FallbackCount, StartupTimeoutCount, LastError, LastFallbackReason, LongestFrameGapMs, ConsecutiveFrameErrorCount. `Clone()` for snapshot.
  * `V3PreviewStressCycler` — developer utility, delegate-injected, no WinRT. Runs N start→stop cycles, returns `StressCycleResult` (TotalCycles, SuccessStarts, FailedStarts, CleanStops, ErrorStops, FullySuccessful).
* **Startup timeout watchdog** — `V3SlotPreview` launches `RunStartupWatchdogAsync` (3000ms) after FrameReader.StartAsync. Cancelled on first frame arrival. Timeout → `RecordStartupTimeout()` + `MarkFailed(StartupTimeout)` + `PreviewFailed` event.
* **`PreviewFailed` event on V3SlotPreview** — raised after MarkFailed. Relay: `VideoEngineV3.SlotPreviewFailed` → `VideoEngineRegistry.V3SlotPreviewFailed` → `MainWindow.OnV3SlotPreviewFailed` → `UpdateBackendStatusLabel()`.
* **11 new BackendMetadata fields** (v1.2.3-alpha lifecycle diagnostics): V3PreviewStartAttempts, V3PreviewSuccessfulStarts, V3PreviewStopAttempts, V3PreviewSuccessfulStops, V3PreviewFailureCount, V3PreviewFallbackCount, V3PreviewStartupTimeoutCount, V3PreviewLastError, V3PreviewLastFallbackReason, V3PreviewLongestFrameGapMs, V3PreviewConsecutiveFrameErrors. All written to JSON and TXT metadata.

### Changed

* `V3SlotPreview.StartAsync` — idempotent: Running/Starting → log + no-op; Failed → log "call StopAsync first" + no-op. Resets frame counters on each new start cycle. Increments `V3PreviewSessionDiagnostics.StartAttempts` before init; `SuccessfulStarts` after FrameReader starts.
* `V3SlotPreview.StopAsync` — idempotent: Stopped/Stopping → log + no-op. Cancels watchdog CTS. Increments StopAttempts/SuccessfulStops.
* `V3SlotPreview.MarkFailed` — now takes `V3PreviewFailureReason`; sanitises error string via `SanitiseError()`.
* `V3SlotPreview` constructor — now requires `V3PreviewSessionDiagnostics sessionDiag` parameter.
* `V3PreviewSlotState` — new fields: `FailureReason`, `StartAttempts`, `StopAttempts`.
* `V3PreviewFrameStats` — new fields: `LongestFrameGapMs`, `ConsecutiveFrameErrorCount`, `LastFrameAgeMs`.
* `VideoEngineV3` — `_sessionDiag` field, `SlotPreviewFailed` event, `GetPreviewDiagnostics()`. `PrepareSlotPreviewAsync` resets slot state on each call.
* `VideoEngineRegistry` — `V3SlotPreviewFailed` event relay; `GetV3PreviewDiagnostics()`; `BuildMetadata` → `BackendVersion = "1.2.3-alpha"` + 11 new diagnostic fields; improved `V3StatusSupplement` (Running/Partial/Failed/Fallback format with slot count and FPS).
* `MainWindow` — subscribes/unsubscribes `V3SlotPreviewFailed` alongside `V3SlotFrameRendered`; `OnV3SlotPreviewFailed` handler calls `UpdateBackendStatusLabel()`.

---

## [v1.2.2-alpha] - 2026-06-25 (build 217)

> **Experimental alpha.** First functional VideoEngineV3 preview pipeline.
> V3 preview replaces V2 preview when mapping confidence is High or Medium.
> V3 recording still NOT implemented. STABLE_CORE_V1 untouched. VideoEngineV2 recording unchanged.

### Added

* **V3 preview pipeline** — `V3SlotPreview` per-slot MediaCapture + MediaFrameReader preview. Uses `StreamingCaptureMode.Video`, `SharedReadOnly`, CPU memory for SoftwareBitmap delivery. Same WinRT API family as V2's `MediaFoundationCaptureService`. No audio, no recording, no MP4.
* **Preview throttle** — `V3PreviewThrottle` pure-logic class: Eco ~4 FPS (250 ms gate), Balanced ~9 FPS (110 ms gate, default), Smooth ~15 FPS (66 ms gate). Tracks frames received/displayed/skipped. FPS measured against `Stopwatch` session elapsed time.
* **Preview models** — `V3PreviewState` (NotStarted/Starting/Running/Stopping/Stopped/Failed), `V3PreviewStatus`, `V3PreviewThrottleMode`, `V3PreviewFrameStats`, `V3PreviewSlotState`, `V3PreviewSession`.
* **Registry preview routing** — `VideoEngineRegistry.TryStartV3SlotPreviewAsync` starts V3 preview when `IsV3PreviewPreferred`; returns false and logs fallback on failure. `StopV3AllSlotsPreviewAsync`, `GetV3SlotPreviewBitmap`, `V3SlotFrameRendered` event added.
* **V3 preview fallback** — Low mapping confidence → falls back to V2 preview. V3 `StartAsync` failure → caught, state → Failed, falls back to V2. Partial slot failure does not affect other slots.
* **UI integration** — `BackendStatusText` now shows active V3 slot count + average FPS. `OpenV2SlotAsync` tries V3 first when `IsV3PreviewPreferred`; falls back to V2 on failure.
* **V3 preview metadata** — 9 new fields in `BackendMetadata` (V3PreviewAvailable, V3PreviewStatus, V3PreviewFallbackUsed, V3PreviewFallbackReason, V3PreviewTargetFps, V3PreviewMeasuredFps, V3PreviewFramesReceived, V3PreviewFramesDisplayed, V3PreviewFramesSkippedByThrottle). Written to both JSON `v3Preview` block and TXT `[Recording Backend — camN]` section.

### Changed

* `VideoEngineV3` — preview stubs replaced with real `PrepareSlotPreviewAsync`/`StartSlotPreviewAsync`/`StopSlotPreviewAsync`/`StopAllSlotsPreviewAsync`. `GetSlotPreviewBitmap` returns live `WriteableBitmap`. `GetSlotOverlayData` shows V3 state.
* `BackendMetadata` — 9 new V3 preview diagnostic fields (all default-safe).
* `VideoEngineRegistry.BuildMetadata` → `BackendVersion` = `"1.2.2-alpha"`.
* `VideoEngineV3.BackendVersion` → `"3.1.0-alpha"` (unchanged).

---

## [v1.2.1-alpha] - 2026-06-25 (build 216)

> **Experimental alpha.** First real V3 capability layer: native camera enumeration and capability probing.
> Recording remains on VideoEngineV2_Stable. STABLE_CORE_V1 untouched.

### Added

* **V3 native camera enumeration** — `V3CameraEnumerator` enumerates cameras via `MediaFrameSourceGroup.FindAllAsync()` (with `DeviceInformation` fallback); populates privacy-safe `V3CameraDeviceInfo` records (display name, device index, SHA-256 hash of symbolic link, device source kind USB/BuiltIn/Unknown, availability).
* **V3 capability probing** — `V3CameraEnumerator.ProbeCapabilitiesAsync()` collects supported resolutions/FPS from `MediaFrameSourceGroup` format descriptions without opening any camera.
* **Duplicate-name resolver** — `V3DuplicateNameResolver` groups cameras by display name and classifies `V3DeviceMappingConfidence`: High (all unique), Medium (duplicates but unique keys), Low (duplicates + no key disambiguation). Low → auto fallback to V2.
* **V3 enumeration models** — `V3CameraDeviceInfo`, `V3CameraCapability`, `V3CapabilityProbeResult`, `V3EnumerationResult` (with `Empty()` and `Failed()` factories).
* **V3 enumeration in metadata** — `backendInfo.v3Enumeration` JSON object and V3 TXT fields in `[Recording Backend — camN]` section: `V3CameraEnumerationAvailable`, `V3DetectedCameraCount`, `V3CapabilityProbeStatus`, `V3DeviceMappingConfidence`, `V3FallbackRecommended`, `V3FallbackReason`, `duplicateNameGroups`, `sameNameDeviceCount`.
* **UI supplement** — `BackendStatusText` now shows V3 camera count + mapping confidence when V3 is active.
* **`VideoEngineRegistry.RunV3EnumerationAsync()`** — called after backend selection in `InitV2EngineAsync`; no-op when V2 is active.
* **44 new tests** — `VideoEngineV3EnumerationTests.cs`: model round-trips, factory methods, duplicate-name resolver logic (all confidence levels, case-insensitive, multi-group), hash/source-kind pure helpers, BackendMetadata V3 fields, registry V3 status supplement.

### Changed

* `BackendMetadata` — 7 new V3 diagnostic fields (all default-safe).
* `VideoEngineV3.BackendVersion` → `"3.1.0-alpha"`.
* `VideoEngineRegistry.BuildMetadata` → populates V3 fields from live enumeration result; `BackendVersion` field = `"1.2.1-alpha"`.
* Schema version remains `"1.2.0"` (JSON structure unchanged; new fields are additions only).

### Unchanged

* All STABLE_CORE_V1 systems — not modified.
* V2 recording pipeline — identical behavior to v1.2.0-alpha.
* Timestamp CSV schema — 10-column format unchanged.
* All existing JSON/TXT metadata fields.

## [v1.2.0-alpha] - 2026-06-25 (build 215)

> **Experimental alpha.** Architecture patch only — no user-visible recording behavior changes.
> Default backend remains VideoEngineV2_Stable. STABLE_CORE_V1 systems are untouched.

### Added

* **Backend abstraction layer** — new `IVideoEngineBackend` interface in `MultiCamApp.Capture.Backend`; defines the contract for all future video engine backends.
* **VideoEngineRegistry** — selects active backend at startup, manages V2/V3 switching, handles fallback, and exposes `BackendStatusDisplay` for the UI label.
* **VideoEngineV3_Experimental scaffold** — compiles and integrates; all recording/preview methods are safe stubs returning failure results; `Activate()` runs a capability probe and sets state to `ScaffoldReady`.
* **Backend metadata in output files** — `backendInfo` JSON object and `[Recording Backend — camN]` TXT section added to per-camera metadata output; 14 fields: `RecordingBackend`, `BackendVersion`, `BackendMode`, `BackendFallbackUsed`, `BackendFallbackReason`, `CaptureApi`, `PreviewApi`, `EncoderApi`, `HardwareEncoderUsed`, `HardwareEncoderEvidence`, `PreviewIndependentFromRecording`, `PreviewTargetFps`, `PreviewMeasuredFps`, `RecordingMeasuredRealFps`.
* **Backend init diagnostics** — `BackendInitDiagnostics` container with `Success()`, `Failure()`, `Scaffold()` factories; surfaces MediaFoundation/D3D11/encoder/preview/watchdog/storage sub-statuses.
* **UI backend status label** — `BackendStatusText` TextBlock shows "Backend: VideoEngineV2 Stable" (or V3 Experimental with fallback note when applicable).
* **`VideoEngineSettings.RequestedBackendId`** — static opt-in property for backend selection; defaults to `"VideoEngineV2_Stable"`.
* **Schema version bump** — metadata JSON `schemaVersion` updated to `"1.2.0"`.
* **Backend tests** — `MultiCamApp.Tests/VideoEngineBackendTests.cs` with 30 unit tests covering registry selection, V3 scaffold identity/lifecycle, metadata fields, diagnostics factories, and V2 settings backward-compatibility.

### Unchanged

* All STABLE_CORE_V1 systems (VideoVerificationService, MetadataParser, VideoScanner, RecordingTimingMetrics, SessionComparisonService, MetadataWriter, RecordingSession, RecordingDiagnosticsMonitor, ScientificTimingAssessor, AppConfig) — not modified.
* Existing recording behavior — MainWindow still calls `_v2Engine` directly; V2 pipeline behavior is identical to v1.1.21.
* Timestamp CSV schema — 10-column format unchanged.
* All existing JSON/TXT metadata fields — no fields removed or renamed.

## [v1.1.21] - 2026-06-25 (build 214)

### Added

* **Timestamp source metadata** — new `[Timestamp Source — camN]` TXT section and `timestampSource` JSON object explicitly document that CSV timestamps are app-side monotonic (`Stopwatch.GetTimestamp / Stopwatch.Frequency`), relative to recording start, captured at CSV write time on the background frame thread, and are not hardware camera presentation timestamps.
* **Camera control readback limitations** — new `cameraControlReadbackLimitations` JSON object and TXT note in `[Camera Controls]` section clarify that `Unknown/Unavailable` readback values mean the Windows camera API did not expose the value, not that the setting failed or that recording is affected.
* **Timing model clarification** — `[Timing Models]` TXT labels updated to make purpose of each timing source explicit: nominal (frame-count), player/container interpretation (ffprobe), primary audit (app timestamps), recording lifecycle (stopwatch). `timingModels` JSON `basis` strings updated to match. Added `hardwarePresentationTimestampUsed: false` flag.

### Changed

* No recording behavior changes. No frame writing changes. No exposure/shutter control changes.
* Metadata schema version → `1.1.21`.

## [v1.1.20] - 2026-06-24 (build 213)

### Added

* **Real frame timing policy** — `FrameTimestampMonitor` now writes recording-relative timestamps (`appTimestampMsFromRecStart`, `appTimestampSecondsFromRecStart`) and high-resolution monotonic ticks (`monotonicTicks = Stopwatch.GetTimestamp()`) to the CSV. Timestamps are now relative to recording start, not preview start.
* **Timing Models section** — new `[Timing Models — camN]` TXT section and `timingModels` JSON object separates four timing interpretations: frame-count timing, container timing (ffprobe), app timestamp timing (CSV), and internal clock timing (stopwatch). Shows diffs between models.
* **Timing Classification** — new `ClassifyTimingBehavior` method classifies each camera's timing into: `CFR_LIKE`, `STARTUP_SETTLING_ONLY`, `VFR_DRIVER_BEHAVIOR`, `MID_SESSION_GAPS_DETECTED`, `FPS_MISMATCH_DETECTED`, `POSSIBLE_EXPOSURE_LIMITED_FPS`. Multiple labels can apply. Reported in `[Timing Classification]` TXT section and `timingClassification` JSON object.
* **Frame integrity check** — new `frameIntegrity` JSON object reports: framesWritten, timestampCsvRows, exact match flag, diff, duplicateFramesDetected, integrityVerdict (PASS/WARN_CSV_MISMATCH/CSV_UNAVAILABLE). TXT timestamps-rows line now shows exact match with count diff.
* **Improved VFR classification** — added interval StdDev (`IntervalStdMs`), coefficient of variation (`CvPercent`), and CSV-based FPS mismatch detection on top of the existing r_frame_rate heuristic.
* **Timing reference camera** — `ComputeTimingReferenceCamera` now scores cameras by mid-session gap count, interval jitter, and measured FPS (not just CFR flag). Selection reason includes jitter σ value.
* **Exposure / FPS safety warning** — if measured FPS < 90% of requested FPS, a warning is added to `[Timing Models]` TXT and `timingModels.fpsSafetyWarning` JSON describing possible causes (exposure, LLC, driver, USB bandwidth).

### Changed

* `FrameTimestampMonitor` CSV columns 2–4 updated: `captureTimestampMonotonicMs` → `appTimestampMsFromRecStart` (now recording-relative, not preview-relative); `writerInputTimestampMs` → `appTimestampSecondsFromRecStart`; `writtenTimestampMonotonicMs` → `monotonicTicks`. Column 7 (gap flag) unchanged.
* `TimestampCsvStats` — added `IntervalStdMs` and `CvPercent` fields.
* `WriteV2SlotMetadataAsync` — added `allTsStats` parameter for session-wide timing reference scoring.
* Metadata schema version → `1.1.20`.

## [v1.1.19] - 2026-06-24 (build 212)

### Added

* **Timing reference camera selection** — `ComputeTimingReferenceCamera` selects the CFR camera closest to 30 fps as the timing reference. New `[Timing Reference]` TXT section and `timingReference` JSON field: referenceCamera, reason, authoritativeSource, per-camera CFR status.
* **VFR driver behavior reporting** — detects driver-level VFR (`r_frame_rate=60/1` but avg≈29.68 fps) and reports it as `DriverVfr` behavior, not a recording failure. New `[VFR Driver Behavior]` TXT section and `vfrDriverBehavior` JSON field with note that timestamp CSV is the authoritative timing source.
* **Startup settling analysis** — `ReadTimestampCsvStatsAsync` now classifies gaps into startup (first 10 s) and mid-session. New `[Startup Settling]` TXT section and `startupSettling` JSON field: startAfterWarmupSeconds, startupGapCount, midSessionGapCount, stableAfterWarmup, analysisSafeInterval.
* **Bitrate quality profiles** — `V2BitrateProfile` enum (Standard=7500, High=12000, WindowsCameraLike=18000) added to `VideoEngineSettings`. Default changed from 8000 → 7500 kbps (Standard). New `[Bitrate Profile]` TXT section and `bitrateProfile` JSON field.
* **Metadata schema version** bumped to `1.1.19`.
* **Windows Camera patterns** — `MediaCapture.RecordLimitationExceeded` handler (WinRT 3-hour recording limit auto-stop) and `DisplayRequest.RequestActive()` / `RequestRelease()` (prevents system sleep during recording) added to the V2 engine.

### Changed

* `WriteV2SlotMetadataAsync` — added `allFfprobe` parameter for session-wide timing reference computation.
* `TimestampCsvStats` — added `StartupGapCount` and `MidSessionGapCount` fields.
* `VideoEngineSettings.TargetBitrateKbps` default changed from 8000 to 7500 (Standard profile).

## [v1.1.17] - 2026-06-24 (build 210)

### Added

* **Visual quality analysis** (`V2VisualQualityAnalyzer`) — post-recording analysis using OpenCvSharp4. Samples 40 frames per camera. Computes: Laplacian variance (blur score), overexposed pixel % (luma ≥ 245), underexposed pixel % (luma ≤ 15), brightness mean/std across frames (instability), 3×3 regional brightness imbalance (uneven lighting/shadow). Verdict: PASS / PASS_WITH_WARNING / FAIL_VISUAL_QUALITY. Results in new `[Visual Quality — camN]` TXT section and `visualQuality` JSON field.
* **Camera capability probe** (`V2CameraCapabilitySnapshot`, `CameraControlManagerV2.ProbeCapabilities()`) — reads driver-reported ranges from open VideoDeviceController: ExposureControl.Min/Max/Step/Value (seconds), FocusControl.Min/Max/Step/Value (driver steps), BacklightCompensation support. Accessible via `VideoEngineV2.GetSlotCapabilities(slot)`.
* **Capability probe on camera target change** — when the user selects a camera in the Focus/Exposure panel, the driver range is probed and shown in the status labels: "Exposure range: Xms – Yms | Current: Zms" and "Focus: Supported | Range: N–M steps | Current: K". If unsupported, "Not supported by this device/driver."
* **Per-Apply audit log** — `ApplyFocusSettingButton_Click` and `ApplyExposureSettingButton_Click` now log resolved slot, device name, requested mode (auto/manual), requested value, and full apply result (applied, readback status, readback value, warning) to `AppDiagnosticLogger.Runtime`.
* **Start/stop state labels** — `ShowPreviewStatus` calls during recording lifecycle: "Preparing cameras..." → "Starting encoders..." → "Recording..." (on start); "Stopping cameras..." → "Finalizing MP4 files..." → "Writing metadata..." → "Complete." (on stop).
* **Metadata schema version** bumped to `1.1.17` in both per-camera and session JSON.

### Changed

* `StopV2AllSlotsRecordingAsync` — duplicate-frame detection and visual quality analysis now run sequentially per camera (dup then VQ, to avoid file contention), with cameras running in parallel via `Task.WhenAll`. Previously dup ran in a separate parallel pass.
* `WriteV2SlotMetadataAsync` — added `Diagnostics.VisualQualityResult? vq` parameter.

## [v1.1.16] - 2026-06-24 (build 209)

### Changed

* Maintenance version bump only. No code changes from v1.1.15.

## [v1.1.15] - 2026-06-24

### Fixed

* **Timestamp CSV path bug** — `CameraPipelineV2.StartRecordingAsync` was computing the CSV path from `tempOutputPath` ("cam1.tmp.mp4") by stripping extension, producing "cam1.tmp_timestamps.csv" instead of "cam1_timestamps.csv". Fixed by adding a `timestampCsvPath` parameter and passing `fileSet.TimestampCsvPath` from `VideoEngineV2.StartSlotRecordingAsync`. `VideoEngineSettings.WriteTimestampCsv = true` was already set; the path bug was the only cause of missing CSVs.
* **One-camera frame count range** — was still written as per-camera min/max only. Now uses global session-level min/max across all active cameras.
* **Timing confidence** — was PASS even when CSV and ffprobe both unavailable (stopwatch alone). Now PASS_WITH_WARNING when neither CSV data nor ffprobe container duration is available to corroborate the monotonic stopwatch.
* **Inter-camera comparison** — was "Not yet implemented". Now computes global frame range, duration range, and FPS spread across all active cameras with PASS/PASS_WITH_WARNING/FAIL result.
* **Duplicate-frame detection** — was "Not available in this build". Now implemented using OpenCvSharp4: samples 30 frames per camera, computes mean grayscale absolute difference, classifies near-identical frame evidence.

### Added

* **ffprobe post-finalization audit** — runs `runtime/ffmpeg/ffprobe.exe` against each finalized MP4 after recording stops. Extracts codec, pixel format, resolution, avg_frame_rate, r_frame_rate, container duration, nb_frames, bitrate, constant-frame-rate flag. Results written to both TXT and JSON. New `[ffprobe Audit — camN]` section in TXT metadata; `ffprobeAudit` object in JSON. Per-slot ffprobe tasks run in parallel.
* **Duplicate-frame detection** (`V2PostRecordingAudit.RunDuplicateDetectionAsync`) — 30-frame sample, grayscale mean absolute diff per frame pair. Distinguishes static-scene near-identical from software duplication. Evidence level: None / Suspected. Results in `[Frame Quality — camN]` section.
* **`session_metadata.txt` and `session_metadata.json`** — written to session root folder after all cameras stop. Contains: session summary, per-camera table (frames, duration, FPS, CSV rows, ffprobe duration, dup evidence), global inter-camera comparison stats, global session verification result and notes.
* **`SessionVerificationStats` record** and `ComputeSessionVerificationStats` method — computes global frame min/max/spread, duration min/max/spread, inter-camera timing confidence, global PASS/PASS_WITH_WARNING/FAIL verdict.
* **`WriteV2SessionMetadataAsync`** — writes `session_metadata.txt` and `session_metadata.json`.
* **`V2PostRecordingAudit` class** (`diagnostics/V2PostRecordingAudit.cs`) — static helpers for `RunFfprobeAsync` and `RunDuplicateDetectionAsync`. `FfprobeResult` and `DuplicateDetectionResult` data classes.
* **Writer queue diagnostics** — reports "Not tracked (LowLagMediaRecording handles encoding internally)" for queue drops/depth/latency; reports preview dropped frames from `RecordingHealthMonitor`.
* **Refactored `StopV2AllSlotsRecordingAsync`** — 7-phase pipeline: stop slots → read CSV stats → ffprobe (parallel) → dup detection (parallel) → compute session stats → write per-camera metadata → write session metadata.
* **Japanese support** for all new fields: ffprobe audit section, frame quality updates, inter-camera comparison, session verification, session metadata files.
* **`container duration (ffprobe)`** now shown in `[Timing]` section and JSON `timing.containerDurationS`.
* **JSON schema v1.1.15** — new `ffprobeAudit` top-level object, updated `frameQuality` (implemented detection, near-identical stats, writer queue note), updated `verification` (globalSessionResult, interCameraTimingConfidence, frameCountSpread, globalVerificationNotes), updated `timing.timingConfidence`.

### Changed

* `WriteV2SlotMetadataAsync` now accepts pre-computed `TimestampCsvStats?`, `FfprobeResult?`, `DuplicateDetectionResult?`, and `SessionVerificationStats` instead of computing them internally.
* Version bumped to v1.1.15, build 208.

## [v1.1.14] - 2026-06-24

### Fixed

* **App version in metadata** — was printing `MultiCamApp.Core.VersionInfo` (the C# class name) instead of the version string. Fixed by using `_vm.Version.Version`, `.Build`, `.Stage` individually.
* **Duration always 0.00s** — `RecordingFinalizeResult` was constructed without the `Duration` field in `VideoEngineV2.StopSlotRecordingAsync`. Added `RecordingElapsed` public property to `CameraPipelineV2` (reads `_recordingClock.Elapsed` which is valid after `Stopwatch.Stop()`). Duration is now populated before the result is returned.
* **Timestamp CSV status mismatch** — metadata reported "Not written" when the CSV file existed. Fixed by checking `File.Exists` directly.
* **Duplicate-frame detection claim** — removed "Not implemented (planned)" which allowed clean PASS. Now "Not available in this build" with `PASS_WITH_WARNING`.
* **Motion blur risk config-based** — was reading `AutoExposureEnabledPerCamera` / `DisableLowLightCompensationPerCamera` config flags (UI intent, not driver state). Now reads actual `V2ControlApplyResult` for Exposure and LowLightCompensation controls. Risk is `Unknown` unless exposure is driver-confirmed fixed.
* **One-camera frame count range** — was always `0 - 0`. Now writes actual `framesWritten - framesWritten`.
* **UI diagnostics in Japanese metadata** — was silently dropped (was inside English-only else branch). Both paths now include `[UI 診断]` section.

### Added

* **Timestamp interval analysis** — CSV is read post-recording to compute: first/last frame timestamps, total duration, mean/median/min/max/P95/P99 frame intervals, gap count, estimated FPS. Written to both TXT and JSON.
* **Detailed camera control sections** — Focus, Exposure, LLC, Digital stabilization, Flicker reduction each get a subsection with: support status, request made, apply result, readback value, confirmed state, warning.
* **Finalization reporting** — `[Recording — camN]` adds: Recording finalized, Temporary file used, Temporary file renamed, File size.
* **Session Verification** — `PASS` / `PASS_WITH_WARNING` / `FAIL` with documented rules for each condition. Notes list specific evidence.
* **Duration fallback chain** — monotonic stopwatch → CSV first/last delta → health session elapsed → Unknown. Duration is no longer zero when frames were written unless all three sources fail.
* **`TimestampCsvStats` class** and `ReadTimestampCsvStatsAsync` in MainWindow.
* **`RecordingElapsed` property** on `CameraPipelineV2`.
* **metadata.json schema v1.1.14** — restructured top-level: `schemaVersion`, `appVersion` (object), `session`, `recordingEngine`, `videoSettings`, `cameras` (array), `controls` (per-type objects), `recording`, `timing` (full stats), `frameQuality`, `motionBlurRisk`, `verification`, `uiDiagnostics`.
* **Japanese metadata** uses same redesigned structure with Japanese labels and section names throughout.

### Changed

* Version bumped to v1.1.14, build 207.

## [v1.1.13] - 2026-06-24

### Fixed

* **3-camera UI freeze** — root cause identified and fixed: `Direct3DPreviewRenderer.PresentFrame` was calling `_dispatcher.BeginInvoke(DispatcherPriority.Render, ...)` on every camera frame. Since WPF `Render` priority (7) is higher than `Input` priority (5), 3 cameras × 30 fps = 90 Render-priority operations per second completely starved mouse/keyboard input processing.
* Changed preview dispatch priority from `Render` to `Background` (priority 4, lower than `Input` 5). Mouse clicks and button presses now always preempt preview frame rendering.
* Added frame-drop gate: at most one `BeginInvoke` is queued per camera renderer at any time. Incoming frames are dropped if the previous render has not been processed yet. This prevents unbounded dispatcher queue growth.
* Pre-allocated reusable pixel buffer per renderer (`byte[] _pixelBuffer`). Previously allocated 8 MB per frame per camera: 3 cameras × 30 fps × 8 MB = 720 MB/s of managed heap allocation causing GC pauses. Buffer is now reused across frames; allocation only occurs on first frame or resolution change.
* Adaptive preview FPS throttle applied at recording start: 20 fps (1 camera), 15 fps (2 cameras), 10 fps (3 cameras), 8 fps (4 cameras). Preview FPS is restored to unlimited on Stop Recording.
* Parallel recording startup: all camera slots now start `LowLagMediaRecording` concurrently via `Task.WhenAll` instead of sequentially. Eliminates 1–2 second stall per camera during startup (3 cameras previously took 3–6 s sequentially).
* Folder creation moved to `Task.Run` to avoid synchronous I/O on the UI thread at recording start.
* Stop Recording button now becomes enabled as soon as any slot enters Recording state, even while other slots are still initializing. Previous behavior: Stop was disabled during the entire `_recordUiBusy` window.
* Preview FPS limit restored to unlimited on `StopV2DefaultRecordingAsync`.

### Added

* `UiFreezeWatchdog`: background timer posts heartbeats to the WPF dispatcher at 100 ms intervals; measures response latency. Logs stalls > 250 ms (minor), 1000 ms (warning), 3000 ms (critical freeze). Freeze count and max duration tracked across the session. Critical freeze counted once per pending cycle (not once per 100 ms tick during a prolonged freeze).
* `CameraPipelineV2.SetPreviewFpsLimit(int maxFps)`: per-slot preview throttle method forwarded to `Direct3DPreviewRenderer.SetPreviewFpsLimit`.
* `VideoEngineV2.SetAllSlotsPreviewFpsLimit(int maxFps)`: sets throttle on all active pipeline slots.
* `VideoEngineV2.RecommendedPreviewFpsForRecordingCameras(int count)`: returns the recommended limit based on active camera count.
* Session metadata (`metadata.json` and `metadata.txt`) now includes `[UI Diagnostics]` / `uiDiagnostics` section: `uiFreezeDetected`, `uiFreezeCount`, `maxUiFreezeMs`, `uiFreezeDuringState`, `previewFpsThrottled`, `activeCameraCount`, `suspectedFreezeCause`.
* Watchdog state labels in preview start (`PreviewStarting` / `Previewing`), preview stop (`Idle`), recording start (`RecordingStarting` / `Recording`), and recording stop (`RecordingStopping` / `Previewing`).

### Changed

* `StartV2DefaultRecordingAsync`: folded sequential slot loop into parallel `Task.WhenAll`; folder I/O moved off UI thread; file-set array population logic corrected (previously had incorrect index mapping bug).
* Version bumped to v1.1.13, build 206.

## [v1.1.11] - 2026-06-23

### Added

* **Default Focus button**: Restores the selected camera's research-safe focus default (autofocus disabled, fixed/manual mode) via `VideoEngineV2 → CameraControlManagerV2`. Clears the per-camera manual focus override stored in `AppConfig.ManualFocusValuesPerCamera`. The per-camera `_focusDefaultActivePerCamera` flag prevents old manual values from being re-applied after a default restore.
* **Default Exposure button**: Restores the selected camera's research-safe exposure default (auto exposure disabled) via the same control chain. Clears the per-camera manual exposure override and sets `_exposureDefaultActivePerCamera` flag.
* Per-camera restore methods added to `CameraPipelineV2` (`RestoreFocusDefaultAsync`, `RestoreExposureDefaultAsync`) and exposed as public pass-throughs on `VideoEngineV2` (`RestoreSlotFocusDefaultAsync`, `RestoreSlotExposureDefaultAsync`).
* Focus and exposure restore status messages distinguish confirmed success, unsupported device, failed, and camera-not-yet-open cases.
* Japanese localization keys `defaultFocusButton` and `defaultExposureButton` added to `ja.json` and `en.json`.
* Recording metadata (`metadata.txt` and `metadata.json`) now includes FPS and timing detail section: requested FPS, selected format FPS, measured capture FPS, note that recording FPS is independent from preview throttle.
* Recording metadata now includes motion blur risk assessment: `"low"` when fixed exposure and low-light compensation are both off; `"medium"` when either is active, with a note explaining why.
* Exposure status messages after "Apply Exposure" are now honest: distinguish between request-accepted-with-readback-unavailable and confirmed states instead of falsely claiming "applied".

### Changed

* `ApplyFocusSettingButton_Click`: clears `_focusDefaultActivePerCamera` flag when the user explicitly applies manual focus settings.
* `ApplyExposureSettingButton_Click`: uses `BuildExposureStatusText` for honest result messaging; clears `_exposureDefaultActivePerCamera` flag.
* `UpdateCameraControlButtonLabels` now updates Default Focus and Default Exposure button labels in addition to Apply buttons; labels read from `en.json`/`ja.json` language keys.
* Version bumped to v1.1.11, build 204.

## [v1.1.10] - 2026-06-23

### Changed

* OpenCV DLL is no longer a hard startup requirement. VideoEngineV2 uses Windows MediaFoundation for all preview and recording; the OpenCV startup check now logs an advisory warning instead of blocking startup. This fixes startup failures on systems where `OpenCvSharpExtern.dll` is absent or VC++ redist is missing.
* Language selection is locked while recording is active. The language dropdown is disabled when recording starts and re-enabled when recording fully stops, preventing mid-session language changes.
* Recording metadata language now follows the UI language selected at recording start. If Japanese is selected before clicking Start Recording, `metadata.txt` is written in Japanese for that session. If English is selected, `metadata.txt` is written in English. The locked session language is recorded in `metadata.json` as `metadataLanguage`.
* Metadata text files are now written with explicit UTF-8 encoding to ensure correct Japanese character output.
* Japanese metadata sections now use Japanese section headers (`[セッション]`, `[録画エンジン]`, `[カメラ]`, `[カメラ制御]`, `[録画]`) and localized field labels and values.
* Fixed remaining English-only strings in `ja.json`: experiment mode timing labels, About page section titles, license/commercial use body text, attribution titles, third-party notices title and button, and the startup critical DLL error message.
* About page: "Released:" label, third-party notices body text, and FFmpeg license button are now localized via language keys instead of hardcoded English strings.
* `AboutPage.xaml.cs` now uses `lang["releasedLabel"]`, `lang["thirdPartyNoticesBody"]`, and `lang["viewFfmpegLicense"]` keys from both `en.json` and `ja.json`.
* `startupCriticalDllMissing` error message in both localization files no longer mentions OpenCV by name, as the V2 pipeline does not require it.
* Added `thirdPartyNoticesBody`, `viewFfmpegLicense`, and `releasedLabel` keys to both `en.json` and `ja.json`.
* Version bumped to v1.1.10, build 203.

## [v1.1.9] - 2026-06-23

### Fixed

* Recording elapsed and session timers now reset correctly at each recording session start. Added `_v2RecordingStopwatch` (Stopwatch) owned by MainWindow and restarted in `StartElapsedTimer`. Timer display returns to `00:00:00` when not recording.
* Camera readiness count in status panel now uses `CountV2ReadySlots()`, which queries V2 slot state directly, fixing the `0/N ready` display during active preview.
* Camera target dropdown in Advanced Camera Controls now shows only cameras assigned in the current active layout, filtering out unassigned or out-of-layout slots.
* `StartV2DefaultRecordingAsync` clears `_v2RecordingFileSets` and `_v2SessionFolderPath` before each recording, preventing stale data from a prior session from affecting the new one.

### Removed

* `HighStabilityRecordingModeCheckBox` — High-Stability Recording Mode is always enabled internally; the checkbox was redundant and removed from the UI.
* `ReapplyFocusBeforeRecordingCheckBox` — Re-apply focus before recording is always active internally; checkbox removed.
* `ReapplyExposureBeforeRecordingCheckBox` — Re-apply exposure before recording is always active internally; checkbox removed.
* Associated click handlers (`HighStabilityRecordingModeCheckBox_Click`, `ReapplyFocusBeforeRecordingCheckBox_Click`, `ReapplyExposureBeforeRecordingCheckBox_Click`) removed from code.

## [v1.1.8] - 2026-06-23

### Changed

* VideoEngineV2 is now the default and sole active recording engine for all normal recording sessions. Legacy pipeline is isolated from the recording flow.
* Removed `V2TestPanel` Expander from the main UI. Preview status is now shown via `PreviewStartupStatusText` using the `ShowPreviewStatus(message, isError)` helper.
* Removed legacy-only methods: `ApplyV2ButtonOverride`, `StartV2PreviewAsync`, `StopV2PreviewAsync`, `StartV2RecordingAsync`, `StopV2RecordingAsync`.
* `OnV2SlotFrameRendered` cleaned to remove `V2TestPanel.SetPreviewBitmap` call.
* `StartV2DefaultPipelineAsync` cleaned to remove `V2TestPanel.ClearError` and `IsActive` guard.
* All `V2TestPanel.ShowError` calls replaced with `ShowPreviewStatus` or `AppDiagnosticLogger.Runtime`.
* `WriteV2SlotMetadataAsync` updated with full `[Session]` block in both TXT and JSON output, including session name, folder, recording date/time, active cameras, active slots, and engine identifier.
* Fixed `CS1061` build error: `result.Duration` is `TimeSpan` (value type), not `TimeSpan?`. Removed `.HasValue`/`.Value` usage.
* `StopV2AllSlotsRecordingAsync` cleaned to remove stale backward-compatibility line.

## [v1.1.0] - 2026-06-23

### Added

* Original Capture Mode for preserving real camera frames only
* Per-frame Timestamp CSV files for timing-sensitive analysis
* Scientific timing confidence reporting
* Privacy-safe metadata summaries
* Video Verification Simple View and Detailed View
* Offline video/container metadata inspection using bundled `ffprobe.exe`
* Synchronized start gate for active 2–4 camera sessions
* Scientific exposure defaults
* Clearer camera-control metadata wording
* Offline Windows installer for Windows 10/11
* Non-commercial academic and research license
* Third-party notices for bundled runtime and build components
* Citation metadata through `CITATION.cff`

### Changed

* Updated recording logic to avoid duplicate-frame and placeholder-frame insertion
* Updated verification wording to distinguish Real Capture FPS from MP4/container Playback FPS
* Updated metadata and report wording for research readability
* Updated documentation for installation, verification, hardware diagnostics, security, licensing, and release packaging
* Updated GitHub repository structure for public source release preparation

### Notes

* Timestamp CSV is the recommended timing source for timing-sensitive analysis.
* MP4 playback FPS and container duration are playback/container metadata, not the primary scientific timing source.
* Frame counts may differ between cameras when devices deliver real frames at slightly different measured FPS.
* Recordings should be verified using the in-app Video Verification page after capture.
