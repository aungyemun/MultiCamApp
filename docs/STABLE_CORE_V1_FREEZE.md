# STABLE_CORE_V1 — Freeze Declaration

> **Historical note:** This document was originally created during v1.0.36 stabilization and has been reviewed and updated for v1.1.0 Stable (2026-06-23). Post-freeze approved exceptions are logged in [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md) and summarised in the sections below.

> **🔓 FREEZE LIFTED (2026-07-09, v1.2.104) — project owner directive.** The project owner explicitly asked to lift the freeze across **all** STABLE_CORE_V1 components (not just Video Verification Logic, per the v1.2.104 exception), with the intent of freezing a new, updated baseline later once the VideoEngineV2-era fixes settle. All 44 files carrying the `STABLE_CORE_V1` banner are open for modification without going through the exception-trigger process below until that new freeze is declared. Banners are left in place per policy (do not remove without a deliberate version bump) so the pre-lift baseline stays identifiable; **do not treat "Do not modify without documented regression testing" in a banner as still binding** — regression-test changes on their merits instead, same as any other code. This note supersedes the "still ACTIVE" line below until removed.
>
> **🔒 NEW FREEZE DECLARED — [STABLE_CORE_V2](STABLE_CORE_V2_FREEZE.md) (2026-07-10, v2.0.0, first stable release).** This is the new baseline the note above anticipated. It protects the *actively-used* production pipeline — VideoEngineV2 recording engine, native V2 metadata, video verification, session comparison — which is a different (and mostly non-overlapping) file set from what STABLE_CORE_V1 originally protected. **STABLE_CORE_V1 itself remains lifted** and now applies only to the dormant legacy OpenCV/DirectShow engine (see engine scope note below) — that subtree is not part of the new freeze, kept only as a dormant safety net per the project owner's 2026-07-09 decision. Any file promoted to `STABLE_CORE_V2` had its `STABLE_CORE_V1` banner replaced (not stacked); this document remains the historical record for those files up to 2026-07-10.

> **⚠️ IMPORTANT — engine scope note (added 2026-07-02, v1.2.30-alpha):** This freeze was validated against the **legacy OpenCV/DirectShow recording pipeline** (`recording/RecordingSession.cs` → `metadata/MetadataWriter.cs`, PascalCase field schema, e.g. `FramesCaptured`/`MeasuredCameraFps`/`CaptureIntervalMeanMs`). As of **v1.2.22-alpha**, **VideoEngineV2** (`capture/video_engine_v2/`, WinRT `MediaCapture` + Media Foundation H.264) is the **default and primary** recording engine for all camera types; the legacy OpenCV path described by this freeze is now reachable only as a fallback (e.g. when WinRT cannot open a selected device, or via explicit `previewEngine`/`recordingEngine` config). VideoEngineV2's recording/metadata code (`MainWindow.xaml.cs` V2 metadata writer, `capture/video_engine_v2/*`, `verification/V2MetadataReader.cs`/`V2VerificationRunner.cs`) was already outside this freeze even before the 2026-07-09 lift. Confirm which engine produced a given recording via `recordingEngine.engine` in its metadata JSON before assuming legacy-pipeline behavior applies to it.
>
> **Practical note on the legacy OpenCV engine specifically (2026-07-09 audit):** `RecordingSession` (the legacy recording orchestrator) is not instantiated anywhere in the current `MainWindow.xaml.cs` — confirmed by a direct code search, not an assumption — and `VideoEngineFallbackPolicy` only drops to `Tier4_LegacyOpenCv` when Media Foundation is entirely unavailable on the host machine, which every real recording audited this cycle contradicts (`MediaFoundationH264` hardware encoder, every session). A structural scan of `capture/`, `recording/`, and the legacy `metadata/MetadataWriter.cs` found no TODO/FIXME markers or obvious defects, but no code changes were made there this cycle: with the legacy engine unreachable on this machine, there is no real recording to regression-test a change against, so any edit would be unverifiable by construction. Treat that subtree as lower priority for the next audit pass unless a specific legacy-engine bug is reported.

**Freeze name:** `STABLE_CORE_V1`  
**Original freeze version:** MultiCamApp **v1.0.36** (build **136**), 2026-06-11  
**Current app release:** MultiCamApp **v2.0.4** (build **337**, Stable), 2026-07-10 — see FREEZE LIFTED note above; post-freeze exceptions below remain the historical record of changes made while still frozen  
**Lock file:** `STABLE_CORE_V1.lock` — referenced historically; does not exist in this repo and is not required (gitignored, machine-local marker only)  
**Compile marker:** `core/StableCoreV1.cs`

---

## Purpose

**STABLE_CORE_V1 was frozen by default** for the legacy OpenCV recording/metadata/verification pipeline it was validated against (see engine scope note above) — **lifted 2026-07-09 by explicit project-owner directive, pending a new freeze declaration.** Historically, modification required a [freeze exception](STABLE_CORE_V1_EXCEPTIONS.md); that process is suspended for now but the exception log is still maintained as a historical record and good practice for documenting *why* a change was made.

The following systems are **validated and production-stable**. They must not be modified except under the formal [exception conditions](STABLE_CORE_V1_EXCEPTIONS.md#freeze-exception-conditions), with the smallest targeted fix, and after completing the [STABLE_CORE_V1 regression checklist](STABLE_CORE_V1_REGRESSION_CHECKLIST.md).

| System | Scope |
|--------|--------|
| **Recording Engine** | Discovery through MP4 output and session folder layout; Original Capture Mode (real frames only); synchronized start gate for active 2–4 camera sessions |
| **Metadata System** | Per-camera and session metadata fields and writers; Timestamp CSV generation; privacy-safe output |
| **Video Verification Logic** | Scan, ffprobe, metadata checks, verdicts, exports; PASS / PASS_WITH_WARNING / FAIL classification |
| **Session Comparison Logic** | Intra-session multi-camera sync and consistency; inter-camera offset, frame-diff, wall-clock diff |

Future work should focus on **installer**, **UI polish**, **reports**, **documentation**, **OFLA analysis**, and **VideoEngineV2** (which sits outside this freeze) unless a legacy-pipeline stable-core bug is proven.

---

## Validation summary

| Item | Value |
|------|--------|
| Original freeze laptop | **developer test machine** |
| Tested camera | **j5 Webcam JVU250 × 2** |
| Tested resolutions | **1920×1080**, **1280×720**, **640×480** |
| Tested sessions | **11** |
| Tested videos | **22** |
| Result at v1.0.36 | **0 FAIL**, **22 PASS_WITH_WARNING** |
| Validation folder | `Final test on other laptop` (2026-06-11) |

**PASS_WITH_WARNING is expected:** MP4 container is tagged **30 fps** while USB camera delivery is **~29 fps**. This is not a recording defect.

**Scientific timing rule:** Use **Timestamp CSV** and **frame count + wall-clock/monotonic timing**, not ffprobe container duration alone.

> **v1.0.38 baseline for 1-camera and 2-camera workflows.** A targeted post-freeze bug fix was required because cam3 preview worked but cam3 recording failed/froze on Start Recording. The fix is limited to 3-camera/4-camera recording startup and diagnostics and does not alter the validated 1-camera/2-camera workflow.

> **v1.0.39–v1.0.43 targeted fixes:** Validates **3-camera** and **4-camera** recording startup. Existing **1-camera** and **2-camera** workflow remains unchanged.

> **v1.0.60 diagnostic-only update:** Hardware Diagnostics adds advisory system, graphics, camera capability, and USB topology reports. This does **not** change recording engine behavior, metadata fields, session folder layout, ffprobe verification, Video Verification thresholds, session comparison semantics, or 1/2-camera workflow.

> **v1.1.0 targeted exceptions (scientific accuracy):** Four targeted fixes applied to protected code as formal freeze exceptions. See [Approved Freeze Exception Log](STABLE_CORE_V1_EXCEPTIONS.md#approved-freeze-exception-log) for full details.
> - **2-camera synchronized start gate:** `RecordingSession.cs` — threshold changed from `openCvSlots.Count >= 3` to `>= 2`. 2-camera sessions now use the shared synchronized start gate. Validated tests kept first-frame inter-camera offset below 50 ms.
> - **MaxTotalQueueDrops aggregation:** `RecordingDiagnosticsMonitor.cs` — `MaxTotalQueueDrops` now takes the max of sample-based total and the sum of per-camera final stats, preventing a false zero when a drop occurs at or after the recording stop boundary.
> - **PASS_WITH_WARNING offset range:** `ScientificTimingAssessor.cs` — added `StartOffsetWarnMs = 50.0` and `StartOffsetFailMs = 100.0` constants; sessions with 50–100 ms inter-camera start offset now report `PASS_WITH_WARNING` instead of either PASS or FAIL.
> - **Focus metadata wording:** `MetadataWriter.cs` — added `BuildFinalFocusMode` method; unreliable autofocus-off driver readback annotated as `Unknown/readback unreliable`.

See [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) for the current regression checklist and pass criteria.

---

## Validated behavior (v1.1.0 Stable)

- **Original Capture Mode** — preserves real camera frames only; no duplicate frames inserted; no placeholder frames inserted
- **Timestamp CSV** — generated per camera per session; row count must match frames written; primary scientific timing source
- Correct resolution presets (1080p / 720p / 360p match requested settings)
- Correct session folder structure (`session_summary.txt`, `camN/`, MP4 + metadata + Timestamp CSV)
- Correct metadata fields populated (see protected field list below)
- Correct capture interval statistics (`CaptureIntervalMeanMs` ~ **34.4 ms** for ~29 fps delivery)
- **0** queue drops across all validated 2-camera 22-video baseline sessions
- **0** duplicates, **0** placeholders
- Inter-camera frame difference explained by Real Capture FPS differences in Original Capture Mode
- Start offset **< 50 ms** for active 2–4 camera sessions using synchronized start gate (v1.1.0 validated)
- `PASS_WITH_WARNING` for start offset 50–100 ms; `FAIL` above 100 ms
- **Offline audit** and **in-app Video Verification** agreed on all validated baseline files
- **Privacy-safe metadata** — exported files do not contain absolute paths, hardware IDs, computer names, or usernames
- **Hardware Diagnostics** are advisory only — do not change recording behavior or verification thresholds

---

## Protected systems (detail)

### 1. Recording Engine

- Camera discovery, initialization, mode selection
- Frame acquisition, queue, timestamp generation
- **Synchronized start gate** for active 2–4 camera sessions (`openCvSlots.Count >= 2`)
- **Original Capture Mode** — real frames only; no duplicate or placeholder frame insertion
- FPS/resolution selection, MP4 output
- Session/camera folder structure

**Primary paths:** `capture/`, `recording/`, `experiment/FrameTimingMonitor.cs`, `utils/MonotonicClock.cs`

### 2. Metadata System

Protected outputs and fields:

- `metadata.json`, `metadata.txt`, `session_summary.txt`
- `camN_frame_timestamps.csv` — **Timestamp CSV**; row count must equal `FramesWritten`
- `FramesCaptured`, `FramesWritten`
- `MeasuredCameraFps`
- `CaptureIntervalMeanMs`, `CaptureIntervalMinMs`, `CaptureIntervalMaxMs`, `CaptureIntervalStdMs`
- `ScientificTimingStatus`, `ScientificTimingMessage`
- `ContainerVsWallClockDifferenceSeconds`
- `WriterQueueDrops`, `MaxTotalQueueDrops` (aggregated correctly across stop boundary)
- `DuplicateFrames`, `PlaceholderFrames` (must remain 0 in Original Capture Mode)
- `FocusMode`, `FinalFocusMode` (including unreliable-readback annotation)

**Primary paths:** `metadata/`

Recording metadata files remain **English** regardless of UI language.  
Exported files are **privacy-safe**: no absolute paths, hardware identifiers, computer names, or usernames.

### 3. Video Verification Logic

- Folder scan and video discovery
- ffprobe validation
- Metadata validation
- Expected vs actual resolution / FPS / duration / frame checks
- PASS / PASS_WITH_WARNING / FAIL classification
- Inter-camera start offset thresholds: warn at **50 ms**, fail at **100 ms** (`ScientificTimingAssessor`)
- Timestamp CSV row-count validation
- Export TXT / JSON / CSV reports

**Primary paths:** `verification/`, `metadata/ScientificTimingAssessor.cs`

### 4. Session Comparison Logic

- Group videos by recording session folder
- Compare **only** cameras inside the same session
- cam1 / cam2 / cam3 / cam4 handling
- Inter-camera frame difference, start/stop offset
- Wall-clock duration difference, measured FPS difference
- Resolution / codec / pixel-format consistency
- Drops / duplicates / placeholders comparison

**Primary class:** `SessionComparisonService`

Videos from **different sessions are never compared** with each other.

---

## Modification rules

Protected code may be changed **only** when a [freeze exception](STABLE_CORE_V1_EXCEPTIONS.md) is triggered. See [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md) for the full policy, trigger categories, and **Approved Freeze Exception Log**.

**Exception trigger categories (summary):**

1. **Recording failure** — empty/corrupt MP4, freeze on record, `FramesWritten = 0`, unsafe stop
2. **Camera-slot / device-mapping bug** — wrong device opened, duplicate merge, same-name treated as one device
3. **Crash or recovery issue** — preview/record crash, USB unplug, unsafe stop/recover
4. **Scientific accuracy issue** — wrong frame counts, metadata timing, verification or session-comparison errors
5. **Hardware handling issue** — resolution crash, unclear open failure, missing per-slot lost connection

**Rules for exception fixes:**

- Make the **smallest targeted change**; do not refactor stable systems casually
- Do **not** change validated 1-camera / 2-camera behavior unless the bug affects them
- Add **diagnostic logs** before changing timing-sensitive code
- Run the [regression checklist](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) after the change
- **Document** the reason and log the exception in [Approved Freeze Exception Log](STABLE_CORE_V1_EXCEPTIONS.md#approved-freeze-exception-log)

Before merging changes:

1. Confirm a trigger in [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md)
2. Complete [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md)
3. Add or update a row in the **Approved Freeze Exception Log**
4. Document justification in changelog / PR
5. Preserve `STABLE_CORE_V1` banners; bump to `STABLE_CORE_V2` only if core behavior intentionally changes

**Current approved targeted exceptions** (STABLE_CORE_V1 still active):

- Device selection mapping and duplicate camera naming (v1.0.47)
- Preview startup reliability and USB disconnect handling (v1.0.46–v1.0.47)
- cam3 / cam4 startup and recording validation (v1.0.38–v1.0.43)
- 2-camera synchronized start gate correction (v1.1.0)
- MaxTotalQueueDrops aggregation fix (v1.1.0)
- PASS_WITH_WARNING offset range addition (v1.1.0)
- Focus metadata wording / FinalFocusMode (v1.1.0)
- Japanese localization of Video Verification log/report text — explicit owner override, not a standard trigger (v1.2.92)
- Native VideoEngineV2 metadata schema support in MetadataParser/VideoVerificationService (v1.2.104)

---

## Source markers

Every protected file begins with:

```
////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Post-freeze exceptions approved through v1.1.21 build 214.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
```

Refresh banners: `python scripts/maintenance/tag_stable_core_v1.py`

---

## Related documents

- [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md) — the current freeze, covering the actively-used VideoEngineV2 + verification pipeline
- [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md) — freeze exception policy and approved exception log (legacy engine, historical)
- [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) — stable-core regression checklist (legacy engine)
- [user_guide/video_verification.md](user_guide/video_verification.md) — interpreting verification results

---

## CLI verification (same logic as Video Verification page)

```powershell
$env:MULTICAMAPP_ROOT = "dist"
dotnet run --project scripts/diagnostics/VerifyFolderCli/VerifyFolderCli.csproj -c Release -- "C:\path\to\sessions"
```
