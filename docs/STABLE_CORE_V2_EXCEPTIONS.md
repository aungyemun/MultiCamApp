# STABLE_CORE_V2 — Freeze Exception Policy

**Freeze name:** `STABLE_CORE_V2`
**Status:** **ACTIVE** (frozen by default as of 2026-07-10, v2.0.0)
**Parent document:** [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md)
**Regression checklist:** [STABLE_CORE_V2_REGRESSION_CHECKLIST.md](STABLE_CORE_V2_REGRESSION_CHECKLIST.md)

---

## Policy summary

**STABLE_CORE_V2 is frozen by default**, protecting the VideoEngineV2 recording engine and the native V2-aware video verification / session comparison pipeline (see the full file list in [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md#protected-files)).

Do **not** modify protected code unless a **freeze exception** is triggered by a real reliability, safety, or scientific-validity issue. Cosmetic changes, drive-by refactors, and speculative improvements do **not** qualify — same bar STABLE_CORE_V1 used.

**The project owner has explicitly pre-authorized fixing genuine bugs found in protected code** ("these can be fixed and freeze lift if necessary components are truly bugs and error exist with related problems... all freeze and protected codes can be fixed") — this is not a harder freeze than STABLE_CORE_V1's; it's the same targeted-exception model, applied to the code that's actually in production use now.

When an exception applies:

1. Make the **smallest targeted change** that fixes the trigger.
2. Add **diagnostic logs** before changing timing-sensitive code.
3. Run the [regression checklist](STABLE_CORE_V2_REGRESSION_CHECKLIST.md).
4. Record the exception in **Approved Freeze Exception Log** (below) — before or when merging the fix.
5. Document the reason in changelog / PR.

Do **not**:

- Refactor stable systems casually.
- Remove `STABLE_CORE_V2` file banners without a deliberate `STABLE_CORE_V3` bump (an intentional core-behavior change, not routine maintenance).

---

## Protected systems

See [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md#purpose) for the full protected-systems table and file list (VideoEngineV2 recording engine, native V2 metadata, video verification logic, session comparison logic — 48 files).

---

## Freeze exception conditions

A change to protected code is permitted when **at least one** of the following is **reproducible** and **documented** — identical categories to STABLE_CORE_V1, since the underlying risk classes (a recording pipeline can fail, drop frames, misreport timing, or mishandle hardware) didn't change just because the implementation did:

### 1. Recording failure

- Empty MP4 created, selected camera freezes after Start Recording, `FramesWritten = 0` for an active camera, video file corrupt or unreadable, recording cannot stop safely

### 2. Camera-slot or device-mapping bug

- App opens a camera not selected in the UI, selected cam1–cam4 device does not match the actual opened device, duplicate-name cameras treated as one device

### 3. Crash or recovery issue

- Crash during Start Preview/Start Recording, crash on USB unplug, Stop Preview/Stop Recording cannot recover safely, GPU device loss doesn't fall back to software rendering cleanly

### 4. Scientific accuracy issue

- Frame count is wrong (including a real `postFinalizeFrameCountMismatch` the app fails to surface), metadata timing wrong or missing, Video Verification gives an incorrect PASS/PASS_WITH_WARNING/FAIL, Session Comparison compares videos from different sessions, Timestamp CSV row count doesn't match frames written, a verdict/status field uses inconsistent vocabulary between display and export

### 5. Hardware handling issue

- Unsupported resolution/FPS causes a crash instead of a graceful fallback, failed camera open not reported clearly, lost connection not shown per camera slot, GPU preview fails without falling back to WPF WriteableBitmap

---

## Rules for exception fixes

| Rule | Requirement |
|------|-------------|
| **Minimal scope** | Smallest change that fixes the trigger; no unrelated refactors |
| **Diagnostics first** | Add logs before changing timing-sensitive code |
| **Regression** | Complete [STABLE_CORE_V2_REGRESSION_CHECKLIST.md](STABLE_CORE_V2_REGRESSION_CHECKLIST.md) |
| **Documentation** | Log exception below; note reason in CHANGELOG.md |
| **Banners** | Keep `STABLE_CORE_V2` markers; bump to `STABLE_CORE_V3` only for an intentional core behavior change |

---

## Approved Freeze Exception Log

Record new exceptions here **before** or **when** merging the fix. Empty as of the freeze declaration (2026-07-10, v2.0.0). First entries added 2026-11-06 (exceptions #1–#21). Exception #8 changes the timestamp CSV contents (one row per encoded frame, indexed from 0, plus `capturePtsMsFromFirstFrame` and, in a follow-up for exact cross-camera alignment, `capturePtsMsSystemClock` columns appended at the end); whether that warrants a `STABLE_CORE_V3` declaration is an owner decision and has not been made.

| Date | App version | Trigger condition | Affected component | Reason | Files changed | Regression tests run | Result | New stable version |
|------|-------------|-------------------|--------------------|--------|---------------|----------------------|--------|-------------------|
| 2026-11-06 | 2.0.5 → 2.1.0 | 3 Crash or recovery issue | Recording orchestration / shutdown | Exception #1: A faulted post-recording metadata task was re-awaited by later Stop Preview and app close, aborting cleanup (cameras/encoders never released) and making the window refuse to close | `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 314/314; app launch + `--smoke-test` PASS. **Real-camera checklist sections pending** | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 1 Recording failure; 5 Hardware handling issue | Pipeline / encoder start, camera open | Exception #2: Start Recording/open failure paths left a slot stuck in StartingRecording, leaked the CSV writer and IMFSinkWriter (temp MP4 held open), stacked MF refs on retry, and left MediaCapture alive after releasing the MF ref | `CameraPipelineV2.cs`, `MediaFoundationEncoderService.cs` | Release build 0 errors; `dotnet test` 314/314; app launch + `--smoke-test` PASS. **Real-camera checklist sections pending** | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 3 Crash or recovery issue | GPU preview | Exception #3: `_gpuHost` double read raced with GPU→WPF fallback (NRE on capture thread); D3D11 device released while render thread might still be running after a 2 s join timeout | `Direct3DPreviewRenderer.cs`, `D3D11SwapChainHost.cs` | Release build 0 errors; `dotnet test` 314/314; app launch + `--smoke-test` PASS. **Real-camera checklist sections pending** | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 1 Recording failure; 3 Crash or recovery issue | Post-finalize ffprobe check | Exception #4: ffprobe ran with no timeout inside Stop Recording; a hung probe hung Stop Recording and app close | `VideoProbeService.cs` | Release build 0 errors; `dotnet test` 314/314; app launch + `--smoke-test` PASS. **Real-camera checklist sections pending** | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 4 Scientific accuracy issue | Video verification reconciliation | Exception #5: V2 verdict reconciliation replaced *real* per-file FAILs (ffprobe could not read the file) with the recording-time session verdict, showing an unreadable MP4 as PASS | `VerificationResult.cs`, `VideoProbeService.cs`, `V2VerificationRunner.cs`, `ui/pages/VideoVerificationPage.xaml.cs` | Release build 0 errors; `dotnet test` 314/314; app launch + `--smoke-test` PASS. **Real-camera checklist sections pending** | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 1 Recording failure; 5 Hardware handling issue | Recording stop after capture error | Exception #6: A camera lost mid-recording (MediaCapture.Failed → Error state) was skipped by Stop Recording: MP4 not finalized (no moov), not renamed, no metadata, invisible to verification | `CameraPipelineV2.cs`, `VideoEngineV2.cs`, `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 314/314; app launch + `--smoke-test` PASS. **Real-camera checklist sections pending** | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 5 Hardware handling issue | Camera-slot error reporting | Exception #7: Camera failing after open (e.g. 0x800718CF USB bandwidth) showed a silent black panel with green status; header stats always blank under V2 | `VideoEngineV2.cs`, `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 332/332; real-camera matrix (1–4 camera layouts, 480p/720p/1080p, 15/30/60 fps requested, external+built-in) through the UI with independent ffprobe/framemd5/CSV verification — all PASS | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 4 Scientific accuracy issue | Timestamp CSV | Exception #8: CSV rows written for frames not in the MP4 (+71 rows measured); frameIndex preview-inclusive; row k != frame k | `FrameTimestampMonitor.cs`, `CameraPipelineV2.cs`, `MediaFoundationEncoderService.cs` | Release build 0 errors; `dotnet test` 332/332; real-camera matrix (1–4 camera layouts, 480p/720p/1080p, 15/30/60 fps requested, external+built-in) through the UI with independent ffprobe/framemd5/CSV verification — all PASS | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 5 Hardware handling issue; 4 Scientific accuracy issue | Camera controls | Exception #9: UVC exposure/focus/WB/power-line controls never used; metadata reported "Not supported" for supported controls; anti-flicker never applied | `CameraControlManagerV2.cs`, `VideoEngineSettings.cs`, `CameraPipelineV2.cs`, `core/AppConfig.cs` | Release build 0 errors; `dotnet test` 332/332; real-camera matrix (1–4 camera layouts, 480p/720p/1080p, 15/30/60 fps requested, external+built-in) through the UI with independent ffprobe/framemd5/CSV verification — all PASS | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 1 Recording failure (storage) | Encoder bitrate | Exception #10: 1080p bitrate applied to all resolutions (140 MB/min at 480p) | `VideoEngineSettings.cs`, `CameraPipelineV2.cs`, `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 332/332; real-camera matrix (1–4 camera layouts, 480p/720p/1080p, 15/30/60 fps requested, external+built-in) through the UI with independent ffprobe/framemd5/CSV verification — all PASS | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 4 Scientific accuracy issue | Metadata ffprobe audit | Exception #11: ffprobe results not passed to metadata; "ffprobe not available" reported falsely | `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 332/332; real-camera matrix (1–4 camera layouts, 480p/720p/1080p, 15/30/60 fps requested, external+built-in) through the UI with independent ffprobe/framemd5/CSV verification — all PASS | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 4 Scientific accuracy issue (privacy) | Metadata | Exception #12: Absolute output path with Windows user name in camN_metadata.json | `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 332/332; real-camera matrix (1–4 camera layouts, 480p/720p/1080p, 15/30/60 fps requested, external+built-in) through the UI with independent ffprobe/framemd5/CSV verification — all PASS | Fixed (minimal targeted change; banners kept) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 3 Functional bug + 4 Scientific accuracy issue | Camera controls (exposure) / metadata | Exception #13: Manual exposure UI had no effect under VideoEngineV2 (legacy SharedReadOnly path) and reported "Not supported" for UVC-only cameras; capability probe ignored UVC exposure/focus; metadata exposure lines were fixed text | `CameraControlManagerV2.cs`, `CameraPipelineV2.cs`, `VideoEngineV2.cs`, `MainWindow.xaml.cs` | Release build 0 errors; `dotnet test` 337/337; C922 720p60 through the UI: slider 142 → 15.6 ms confirmed, 59.44 fps; slider 255 → 250 ms confirmed + warning, 4.99 fps; auto exposure restored after close; metadata auto/manual lines verified for both | Fixed (additive; default auto-exposure behaviour unchanged) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 3 Functional bug (any-camera compatibility) | Capture format negotiation | Exception #14: camera mode chosen by resolution/FPS only and driver order of subtypes; CPU frame reader delivers no frames from MJPG (`OutputFormatNotSupported`), so cameras listing MJPG first showed a black "Previewing" panel; infrared streams could be selected on RGB+IR devices; no first-frame check | `MediaFoundationCaptureService.cs`, `CameraPipelineV2.cs`, `VideoEngineV2.cs`, `MainWindow.xaml.cs` (metadata: camera input subtype, mode-switch note) | `dotnet test` 364/364 incl. `CameraModeRankingTests` (MJPG-first, YUY2-only, 29.97 fps, MJPG-only exact mode, unusual resolution); hardware: MJPG forced first on C922 → refused, switched to NV12 in 0.2 s, recording CSV=frames | Fixed (uncompressed subtypes preferred; first-frame watchdog with up to 4 ranked modes; Color-kind source only; documented test hook `MULTICAMAPP_TEST_PREFER_SUBTYPE`) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 2 Data-loss risk | Recording container / probe | Exception #15: forced close, crash or power loss left `camN.tmp.mp4` unplayable ("moov atom not found", verified by a kill test: 15–17 MB per camera lost) | `MediaFoundationEncoderService.cs`, `VideoEngineSettings.cs`, `VideoProbeService.cs` (packet count when `nb_frames` is absent), `MainWindow.xaml.cs` (session journal); new `recording/InterruptedRecordingRecovery.cs` | Kill test after fix: 448/450 and 470/470 frames playable; next start renamed files and wrote RECOVERY_REPORT; normal recording: ffprobe frame count present, post-recording check Match, CSV=frames, 1 s keyframe interval (44 keyframes / 45 s), bitrate unchanged; `InterruptedRecordingRecoveryTests` | Fixed (fragmented MP4 + 1 s GOP, both settings; journal + startup recovery) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 4 Scientific accuracy / visual quality | Anti-flicker, status, verification | Exception #16: anti-flicker default kept each camera's factory value (C922: 50 Hz) → 1.30 % drifting bands under 120 Hz light; Auto reported as "readback mismatch" when the camera resolved it to 60 Hz; status showed "Previewing" while recording was starting / files were being saved; no visual-quality audit | `VideoEngineSettings.cs`, `CameraControlManagerV2.cs`, `MainWindow.xaml.cs`, `ui/pages/VideoVerificationPage.xaml(.cs)`, `DeepVerifyService.cs` (exposes resolved ffmpeg path); new `verification/VisualStabilityAuditService.cs` | Flicker experiment (3 cameras × 50/60/Auto): C922 1.30/0.095/0.076 %, built-in 0.60/0.038/0.16 %; C# audit equals independent Python audit to 3 decimals on 6 files; `VisualStabilityAuditTests` | Fixed (default Auto; Auto resolution reported; transitional status; Deep Verify visual stability cards) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 3 Functional bug + 4 Scientific accuracy issue | Camera controls (focus) / metadata | Exception #17: manual focus Apply had the same legacy-pipeline defect as exposure (#13) and never reached the camera; focus metadata lines were fixed text; restoring only the AF flag left a C922 lens at the user position (its AF did not search again: position stayed 250 for 6 s), so the next session recorded defocused (blur 18 vs 4) | `CameraControlManagerV2.cs`, `CameraPipelineV2.cs`, `VideoEngineV2.cs`, `MainWindow.xaml.cs` | Hardware: C922 position 0 → blur 4.07, 250 → 18.2, UI "confirmed by camera"; `FocusValue_SnapsToControlStep` | Fixed (V2 focus path; original lens position + AF restored on close; metadata shows focus mode and locked position) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 5 Performance (start lag) / additive option | Encoder | Exception #18: first Start Recording per process took 2.28 s (hardware H.264 encoder initialisation, one-time; 2nd/3rd starts 0.31 s); quality-based rate control option added (`V2RateControl.QualityVbr`, "Efficient" preset, quality 80) after hardware measurement — CBR stays default | `MediaFoundationEncoderService.cs`, `VideoEngineSettings.cs`, `MainWindow.xaml.cs`, `ui/VideoQualitySelector.*` | Restart test (3 starts, one session); rate-control matrix CBR / q70 / q78 / q80 / q85 on C922 + built-in (size, blur, blockdetect, temporal noise) | Fixed (background encoder warm-up after preview start; Efficient preset optional) | — (pending release) |
| 2026-11-06 | 2.0.5 → 2.1.0 | 5 Performance (UI stall) — investigated, reverted; diagnostics only | Capture open | Exception #19: one Start Preview froze the UI for 2.7 s while `MediaCapture.InitializeAsync` ran on the UI thread. Moving initialisation to a thread-pool thread cut it to 0.2 s, but the first close afterwards hung in `MediaCapture.Dispose()` (process left unterminable). Change reverted; kept: `InitializeMediaCaptureAsync` helper (same UI-thread behaviour) and `V2_CAPTURE_OPEN_TIMING` log line | `MediaFoundationCaptureService.cs` | Release build 0 errors; `dotnet test` 369/369; UI tour EN/JA with the watchdog (`maxFreezeMs=0`, init 0.2–0.3 s); 4 real 2-camera recordings after the revert, all PASS | Reverted to original behaviour (diagnostic log only); 2.7 s cold-start stall not reproduced, open | — (pending release) |
| 2026-11-06 | 2.1.0 | 2 Data integrity (invalid export) + 5 Performance (UI stall) + 3 Misleading output | Verification UI and report export | Exception #20: (a) the JSON report was invalid because the output privacy filter rewrote decimal numbers inside the serialized JSON; the writer now filters string values only. (b) Verify All froze the UI for 54 s on 115 files; verification work moved off the UI thread and the session list updates incrementally. (c) Before any run, cards showed placeholder FAIL/MISSING values; results and exports are now shown only after a run. (d) Partial-metadata sessions showed legacy texts; session headers lost underscores; some Japanese card values were English. Verification logic and pass/fail rules unchanged | `VerificationReportWriter.cs`, `ui/pages/VideoVerificationPage.xaml(.cs)` | Release build 0 errors; `dotnet test` 386/386 (exported JSON parses); `pages_func.ps1` on 3 known sessions: Verify All 3 s, Deep Verify 22 s, all exports written, no watchdog freeze; results match the session contents (interrupted session FAIL, complete EN/JA sessions PASS) | Fixed; 5.9–7 s freeze remains at the end of very large runs (115 files), open | — (pending release) |
| 2026-11-06 | 2.1.0 | 2 Data integrity (independent check counted the wrong frames) | Deep Verify | Exception #21: Deep Verify ran `ffmpeg -f framemd5` without `-fps_mode passthrough`, so ffmpeg converted the variable-frame-rate recordings to a constant rate before hashing. On a real 720p30 file it hashed 535 of 536 frames; the same conversion can also repeat frames, which would be reported as duplicates that are not in the file. Now every decoded frame is hashed exactly once | `DeepVerifyService.cs` | `dotnet test` 387/387 (new test pins the argument); 6 real files: frames hashed = ffprobe packet count = CSV rows = metadata frames (536, 534, 633, 614, 611, 630); EN and JA | Fixed | — (pending release) |

---

## Requesting a new exception

1. Reproduce the issue; capture logs and steps.
2. Map the issue to a **trigger condition** (sections 1–5 above).
3. Propose the **smallest** fix.
4. Run the [regression checklist](STABLE_CORE_V2_REGRESSION_CHECKLIST.md).
5. Add a row to **Approved Freeze Exception Log**.
6. If the fix **intentionally** changes validated core semantics, plan `STABLE_CORE_V3` instead of silent drift.

---

## Related documents

- [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md) — freeze declaration, file list, and STABLE_CORE_V1 lineage
- [STABLE_CORE_V2_REGRESSION_CHECKLIST.md](STABLE_CORE_V2_REGRESSION_CHECKLIST.md) — regression checklist
- [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md) — legacy (pre-V2) exception policy and historical log, still governing the dormant OpenCV engine
