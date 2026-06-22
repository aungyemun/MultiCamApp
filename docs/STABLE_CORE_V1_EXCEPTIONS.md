# STABLE_CORE_V1 — Freeze Exception Policy

> **Historical note:** This document was originally created during v1.0.x stabilization and has been reviewed and updated for v1.1.0 Stable (2026-06-23). The Approved Freeze Exception Log includes all exceptions through v1.1.0.

**Freeze name:** `STABLE_CORE_V1`  
**Status:** **ACTIVE** (frozen by default)  
**Parent document:** [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md)  
**Regression checklist:** [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md)

---

## Policy summary

**STABLE_CORE_V1 remains frozen by default.**

Do **not** modify stable-core systems unless a **freeze exception** is triggered by a real reliability, safety, or scientific-validity issue. Cosmetic changes, drive-by refactors, and speculative improvements do **not** qualify.

When an exception applies:

1. Make the **smallest targeted change** that fixes the trigger.
2. Add **diagnostic logs** before changing timing-sensitive code.
3. Run the [regression checklist](STABLE_CORE_V1_REGRESSION_CHECKLIST.md).
4. Record the exception in **Approved Freeze Exception Log** (below).
5. Document the reason in changelog / PR.

Do **not**:

- Refactor stable systems casually.
- Change validated **1-camera / 2-camera** behavior unless the bug affects them.
- Remove `STABLE_CORE_V1` file banners without a deliberate core version bump (`STABLE_CORE_V2`).

---

## Protected systems

| System | Scope |
|--------|--------|
| **Recording Engine** | Discovery through MP4 output and session folder layout; Original Capture Mode (real frames only); synchronized start gate for active 2–4 camera sessions |
| **Metadata System** | Per-camera and session metadata fields and writers; Timestamp CSV generation; privacy-safe output |
| **Video Verification Logic** | Scan, ffprobe, metadata checks, verdicts, exports; PASS / PASS_WITH_WARNING / FAIL classification |
| **Session Comparison Logic** | Intra-session multi-camera sync and consistency; inter-camera offset, frame-diff, wall-clock diff |

**Primary paths:** `capture/` (recording pipeline), `recording/`, `metadata/`, `verification/`, `metadata/ScientificTimingAssessor.cs`, `SessionComparisonService`, `experiment/FrameTimingMonitor.cs`, `utils/MonotonicClock.cs`

> **Note:** Camera **UI selection**, **display naming**, **refresh**, and **preview orchestration** are generally **outside** the frozen core unless a bug causes incorrect recording input (wrong device opened, merged duplicates, etc.). See approved exceptions below.

---

## Freeze exception conditions

A change to protected code is permitted when **at least one** of the following is **reproducible** and **documented**.

### 1. Recording failure

- Empty MP4 created
- Selected camera freezes after **Start Recording**
- `FramesWritten = 0` for an active selected camera
- Video file corrupt or unreadable
- Recording cannot stop safely

### 2. Camera-slot or device-mapping bug

- App opens a camera **not** selected in the UI
- Selected cam1 / cam2 / cam3 / cam4 device does **not** match the actual opened device
- Same physical camera is duplicated or merged incorrectly
- Duplicate-name cameras are treated as one device
- cam3 / cam4 preview works but recording fails

### 3. Crash or recovery issue

- App crashes during **Start Preview**
- App crashes during **Start Recording**
- App crashes when a USB camera is unplugged
- **Stop Preview** or **Stop Recording** cannot recover safely
- One camera failure stops the whole app unexpectedly

### 4. Scientific accuracy issue

- Frame count is wrong
- `FramesCaptured` or `FramesWritten` is wrong
- Metadata timing is wrong or missing
- `ScientificTimingStatus` is empty or incorrect
- Video Verification gives incorrect PASS / PASS_WITH_WARNING / FAIL
- Session Comparison compares videos from **different** sessions
- Inter-camera start offset classification is wrong (wrong threshold used)
- `MaxTotalQueueDrops` is reported as zero when drops occurred at stop boundary
- Timestamp CSV row count does not match frames written

### 5. Hardware handling issue

- Unsupported resolution causes crash
- Failed camera open is not reported clearly
- Lost connection is not shown per camera slot
- USB bandwidth / camera FPS issue is not logged or explained

---

## Rules for exception fixes

| Rule | Requirement |
|------|-------------|
| **Minimal scope** | Smallest change that fixes the trigger; no unrelated refactors |
| **Preserve 1/2-cam baseline** | Do not alter validated 1-camera / 2-camera workflow unless the bug affects it |
| **Diagnostics first** | Add logs (e.g. `recording_runtime_*.log`, `preview_start_trace_*`, `camera_refresh_*.log`) before timing changes |
| **Regression** | Complete [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) |
| **Documentation** | Log exception below; note reason in changelog |
| **Banners** | Keep `STABLE_CORE_V1` markers; bump to `STABLE_CORE_V2` only for intentional core behavior change |

---

## Approved Freeze Exception Log

Record new exceptions here **before** or **when** merging the fix.

| Date | App version | Trigger condition | Affected component | Reason | Files changed | Regression tests run | Result | New stable version |
|------|-------------|-------------------|--------------------|--------|---------------|----------------------|--------|-------------------|
| 2026-06-11 | v1.0.38 | **2.** Camera-slot / device-mapping — cam3 preview OK, recording failed | Recording startup (3/4-cam path) | Original STABLE_CORE_V1 validated mainly for 1/2-camera; cam3 slot needed targeted recording-start fix without changing 1/2-cam path | `recording/`, `capture/CameraSlotPipeline.cs`, `ui/MainViewModel.cs` (orchestration only) | 1-cam / 2-cam regression; 3-cam record @ 1080p/720p/360p | **Accepted** — 1/2-cam unchanged | v1.0.38 baseline |
| 2026-06-12 – 2026-06-15 | v1.0.39 – v1.0.43 | **1.** Recording failure / **2.** cam3–cam4 mapping — 3/4-cam startup & record validation | Recording coordinator, multi-slot open | Validate 3-camera and 4-camera recording startup; 1/2-camera workflow unchanged | `recording/MultiCameraRecordingCoordinator.cs`, `capture/`, `ui/MainViewModel.cs` | 3-cam @ 360p/720p/1080p; 4-cam @ 360p; 1/2-cam smoke | **Accepted** | v1.0.43 |
| 2026-06-16 – 2026-06-17 | v1.0.46 – v1.0.47 | **3.** Crash / recovery — preview crash, USB unplug; **5.** Hardware — lost connection per slot, preview slowness | Preview orchestration, disconnect handling (non-recording paths) | App must not crash when one selected camera fails or is unplugged; per-slot lost-connection UI | `capture/OpenCvPreviewController.cs`, `capture/CameraSlotPipeline.cs`, `ui/MainViewModel.cs`, `utils/PreviewStartTrace.cs`, `diagnostics/` | Start Preview 2/3/4-cam; unplug during preview; 1/2-cam record smoke | **Accepted** | v1.0.47 |
| 2026-06-17 | v1.0.47 | **2.** Device-mapping — same-name USB cameras merged; wrong device opened; refresh cleared selections | Camera discovery, UI selection mapping (not recording engine) | App must open exact user-selected cameras; duplicate generic names (`USB Live camera`, etc.) numbered by unique device ID | `capture/CameraDeviceDisplayNamer.cs`, `capture/CameraDeviceDiscovery.cs`, `ui/MainViewModel.cs`, `MainWindow.xaml.cs`, `utils/CameraRefreshLog.cs` | `CameraDeviceDisplayNamerTests`; 2× same-name USB refresh/select; Start Preview device match; 1/2-cam record smoke | **Accepted** | v1.0.47 |
| 2026-06-22 – 2026-06-23 | v1.1.0 | **4.** Scientific accuracy — 2-camera sessions not using synchronized start gate; ~190 ms inter-camera first-frame offset | `recording/RecordingSession.cs` | `openCvSlots.Count >= 3` guard excluded 2-camera from the shared start barrier; fix: `>= 2`. All active 2–4 camera sessions now use the synchronized start gate. Validated tests kept first-frame offset < 50 ms. | `recording/RecordingSession.cs` (one line: threshold `>= 3` → `>= 2`) | 2-cam record @ 1080p/720p/360p; start offset < 50 ms confirmed; 1-cam smoke | **Accepted** | v1.1.0 |
| 2026-06-22 – 2026-06-23 | v1.1.0 | **4.** Scientific accuracy — `MaxTotalQueueDrops` reported as 0 when drops occurred at or after recording stop boundary | `recording/RecordingDiagnosticsMonitor.cs` | Sample-based aggregation missed drops captured in per-camera final stats at stop boundary; fix: `Math.Max(sample-based, cameraSummaries.Sum(drops))` | `recording/RecordingDiagnosticsMonitor.cs` | 2-cam record with forced stop-boundary drop; verified non-zero result | **Accepted** | v1.1.0 |
| 2026-06-22 – 2026-06-23 | v1.1.0 | **4.** Scientific accuracy — no `PASS_WITH_WARNING` result for 50–100 ms inter-camera start offset; only PASS or FAIL existed | `metadata/ScientificTimingAssessor.cs` | Added `StartOffsetWarnMs = 50.0` and `StartOffsetFailMs = 100.0` threshold constants; 50–100 ms range now returns `PASS_WITH_WARNING` instead of PASS; > 100 ms returns FAIL. Added 6 unit tests covering boundary cases. | `metadata/ScientificTimingAssessor.cs` | Unit tests (6 new); 2-cam session result reviewed | **Accepted** | v1.1.0 |
| 2026-06-22 – 2026-06-23 | v1.1.0 | **4.** Scientific accuracy — focus metadata wording ambiguous when autofocus-off driver readback is unreliable | `metadata/MetadataWriter.cs` | Added `BuildFinalFocusMode` helper; autofocus-off sessions where driver readback incorrectly reports autofocus active are now annotated `Unknown/readback unreliable` in `FinalFocusMode` field | `metadata/MetadataWriter.cs` | Metadata field review; 1/2-cam smoke | **Accepted** | v1.1.0 |

### Exception summaries (current approved set)

1. **Device selection mapping and duplicate camera naming**  
   **Reason:** App must open the exact user-selected cameras and treat same-name physical devices as separate entries (`#1`, `#2`, …) keyed by unique device ID.

2. **Preview startup reliability and USB disconnect handling**  
   **Reason:** App must not crash when one selected camera fails or is unplugged; lost connection must be shown per slot.

3. **cam3 / cam4 startup and recording validation**  
   **Reason:** Original STABLE_CORE_V1 was validated mainly for 1-camera / 2-camera workflow; 3/4-camera paths required targeted fixes without regressing 1/2-camera behavior.

4. **2-camera synchronized start gate correction** (v1.1.0)  
   **Reason:** Scientific accuracy issue — 2-camera sessions were excluded from the synchronized start gate by an off-by-one threshold, causing ~190 ms inter-camera first-frame offsets. Fix is a single-line threshold change; 1-camera behavior unchanged.

5. **MaxTotalQueueDrops aggregation fix** (v1.1.0)  
   **Reason:** Scientific accuracy issue — drops occurring at or after recording stop were not captured in sample-based aggregation, causing false-zero drop counts in diagnostics.

6. **PASS_WITH_WARNING inter-camera start offset range** (v1.1.0)  
   **Reason:** Scientific accuracy — introduced explicit warn/fail thresholds (50 ms / 100 ms) so sessions with moderate offset are clearly distinguished from clean sessions and from clearly failing sessions.

7. **Focus metadata FinalFocusMode wording** (v1.1.0)  
   **Reason:** Metadata accuracy — driver readback for autofocus state can be unreliable; unreliable cases now annotated explicitly in metadata output.

---

## Requesting a new exception

1. Reproduce the issue; capture logs and steps.
2. Map the issue to a **trigger condition** (sections 1–5 above).
3. Propose the **smallest** fix; prefer non-core layers when the bug is selection/UI/mapping.
4. Run the [regression checklist](STABLE_CORE_V1_REGRESSION_CHECKLIST.md).
5. Add a row to **Approved Freeze Exception Log**.
6. If the fix **intentionally** changes validated core semantics, plan `STABLE_CORE_V2` instead of silent drift.

---

## Related documents

- [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) — freeze declaration and validation summary
- [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) — stable-core regression checklist
