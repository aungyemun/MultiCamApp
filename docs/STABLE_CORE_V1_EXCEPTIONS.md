# STABLE_CORE_V1 — Freeze Exception Policy

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
| **Recording Engine** | Discovery through MP4 output and session folder layout |
| **Metadata System** | Per-camera and session metadata fields and writers |
| **Video Verification Logic** | Scan, ffprobe, metadata checks, verdicts, exports |
| **Session Comparison Logic** | Intra-session dual/multi-camera sync and consistency |

**Primary paths:** `capture/` (recording pipeline), `recording/`, `metadata/`, `verification/`, `SessionComparisonService`, `experiment/FrameTimingMonitor.cs`, `utils/MonotonicClock.cs`

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
- Metadata timing is wrong
- `ScientificTimingStatus` is empty or incorrect
- Video Verification gives incorrect PASS / WARNING / FAIL
- Session Comparison compares videos from **different** sessions

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

### Exception summaries (current approved set)

1. **Device selection mapping and duplicate camera naming**  
   **Reason:** App must open the exact user-selected cameras and treat same-name physical devices as separate entries (`#1`, `#2`, …) keyed by unique device ID.

2. **Preview startup reliability and USB disconnect handling**  
   **Reason:** App must not crash when one selected camera fails or is unplugged; lost connection must be shown per slot.

3. **cam3 / cam4 startup and recording validation**  
   **Reason:** Original STABLE_CORE_V1 was validated mainly for 1-camera / 2-camera workflow; 3/4-camera paths required targeted fixes without regressing 1/2-camera behavior.

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
