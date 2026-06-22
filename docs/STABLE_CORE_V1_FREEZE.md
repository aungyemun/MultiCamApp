# STABLE_CORE_V1 — Freeze Declaration

**Freeze name:** `STABLE_CORE_V1`  
**App version:** MultiCamApp **v1.0.36** (build **136**)  
**Freeze date:** 2026-06-11  
**Lock file:** [STABLE_CORE_V1.lock](../STABLE_CORE_V1.lock)  
**Compile marker:** `core/StableCoreV1.cs`

---

## Purpose

**STABLE_CORE_V1 remains frozen by default.** Do not modify stable-core systems unless a [freeze exception](STABLE_CORE_V1_EXCEPTIONS.md) is triggered.

The following systems are **validated and production-stable**. They must not be modified except under the formal [exception conditions](STABLE_CORE_V1_EXCEPTIONS.md#freeze-exception-conditions), with the smallest targeted fix, and after completing the [STABLE_CORE_V1 regression checklist](STABLE_CORE_V1_REGRESSION_CHECKLIST.md).

| System | Scope |
|--------|--------|
| **Recording Engine** | Discovery through MP4 output and session folder layout |
| **Metadata System** | Per-camera and session metadata fields and writers |
| **Video Verification Logic** | Scan, ffprobe, metadata checks, verdicts, exports |
| **Session Comparison Logic** | Intra-session dual/multi-camera sync and consistency |

Future work should focus on **installer**, **UI polish**, **reports**, **documentation**, and **OFLA analysis** unless a stable-core bug is proven.

---

## Validation summary

| Item | Value |
|------|--------|
| Tested laptop | **developer test machine** |
| Tested camera | **j5 Webcam JVU250 × 2** |
| Tested resolutions | **1920×1080**, **1280×720**, **640×480** |
| Tested sessions | **11** |
| Tested videos | **22** |
| Result | **0 FAIL**, **22 PASS_WITH_WARNING** |
| Validation folder | `Final test on other laptop` (2026-06-11) |

**PASS_WITH_WARNING is expected:** MP4 container is tagged **30 fps** while USB camera delivery is **~29 fps**. This is not a recording defect.

**Scientific timing rule:** Use **frame count + wall-clock/monotonic timing**, not ffprobe container duration alone.

> **v1.0.38 is stable baseline for 1-camera and 2-camera workflows.** A targeted post-freeze bug fix was required because cam3 preview worked but cam3 recording failed/froze on Start Recording. The fix is limited to 3-camera/4-camera recording startup and diagnostics and does not alter the validated 1-camera/2-camera workflow.

> **v1.0.39–v1.0.43 targeted fixes:** Validates **3-camera** and **4-camera** recording startup. Existing **1-camera** and **2-camera** workflow remains unchanged. **3-camera** validation should be run at **360p**, **720p**, and **1080p** using 3 external webcams. **4-camera** validation may be run at **360p** using 3 external webcams plus the built-in camera to validate the 4-slot pipeline.

> **v1.0.60 diagnostic-only update:** Hardware Diagnostics adds advisory system, graphics, camera capability, and USB topology reports. This does **not** change recording engine behavior, metadata fields, session folder layout, ffprobe verification, Video Verification thresholds, session comparison semantics, or 1/2-camera workflow.

See [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) for the current regression checklist and pass criteria.

---

## Validated behavior

- Correct resolution presets (1080p / 720p / 360p match requested settings)
- Correct session folder structure (`session_summary.txt`, `camN/`, MP4 + metadata)
- Correct metadata fields populated (see protected field list below)
- Correct capture interval statistics (`CaptureIntervalMeanMs` ~ **34.4 ms** for ~29 fps delivery)
- **0** queue drops across all 22 videos
- **0** duplicates, **0** placeholders
- Inter-camera sync **0–3 frames** per session
- Start offset approximately **6–20 ms**
- **Offline audit** (`scripts/diagnostics/audit_videos_folder.py`) and **in-app Video Verification** agreed on all 22 files

---

## Protected systems (detail)

### 1. Recording Engine

- Camera discovery, initialization, mode selection  
- Frame acquisition, queue, timestamp generation  
- Synchronized start/stop, video writer creation  
- FPS/resolution selection, MP4 output  
- Session/camera folder structure  

**Primary paths:** `capture/`, `recording/`, `experiment/FrameTimingMonitor.cs`, `utils/MonotonicClock.cs`

### 2. Metadata System

Protected outputs and fields:

- `metadata.json`, `metadata.txt`, `session_summary.txt`
- `FramesCaptured`, `FramesWritten`
- `MeasuredCameraFps`
- `CaptureIntervalMeanMs`, `CaptureIntervalMinMs`, `CaptureIntervalMaxMs`, `CaptureIntervalStdMs`
- `ScientificTimingStatus`, `ScientificTimingMessage`
- `ContainerVsWallClockDifferenceSeconds`

**Primary paths:** `metadata/`

Recording metadata files remain **English** regardless of UI language.

### 3. Video Verification Logic

- Folder scan and video discovery  
- ffprobe validation  
- Metadata validation  
- Expected vs actual resolution / FPS / duration / frame checks  
- PASS / PASS_WITH_WARNING / FAIL classification  
- Export TXT / JSON / CSV reports  

**Primary paths:** `verification/` (core services; not experiment/locomotor verification profiles)

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

---

## Source markers

Every protected file begins with:

```
////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
```

Refresh banners: `python scripts/maintenance/tag_stable_core_v1.py`

---

## Related documents

- [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md) — freeze exception policy and approved exception log  
- [STABLE_CORE_V1_REGRESSION_CHECKLIST.md](STABLE_CORE_V1_REGRESSION_CHECKLIST.md) — stable-core regression checklist
- [user_guide/video_verification.md](user_guide/video_verification.md) — interpreting verification results  

---

## CLI verification (same logic as Video Verification page)

```powershell
$env:MULTICAMAPP_ROOT = "dist"
dotnet run --project scripts/diagnostics/VerifyFolderCli/VerifyFolderCli.csproj -c Release -- "C:\path\to\sessions"
```
