# STABLE_CORE_V1 Regression Checklist

> **Historical note:** This document was originally created during v1.0.36 stabilization and has been reviewed and updated for v1.1.0 Stable (2026-06-23). Pass criteria reflect current v1.1.0 Stable thresholds including synchronized start gate, PASS_WITH_WARNING offset range, and Timestamp CSV requirements.

> **🔓 FREEZE LIFTED (2026-07-09, v1.2.104) — project owner directive.** See [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md#freeze-declaration). This checklist is no longer a mandatory pre-merge gate, but running it (or the parts of it that apply — a recording-engine change should still get a real recording test, a metadata-schema change should still get a real audit) remains the best available way to catch a regression in this code before shipping. The "Required before" line below described the pre-lift mandatory process.

**Applies to:** `STABLE_CORE_V1` (original freeze: MultiCamApp v1.0.36 build 136; current: v1.1.0 build 193)  
**Required before (while frozen):** Any change to protected recording, metadata, verification, or session-comparison code — see FREEZE LIFTED note above for current status  
**Reference:** [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) · [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md)

---

## Pre-change

- [ ] Change is justified by a [freeze exception trigger](STABLE_CORE_V1_EXCEPTIONS.md#freeze-exception-conditions) (reproducible bug, corruption, frame drops, sync failure, scientific accuracy, or hardware-handling issue) — good practice, no longer a mandatory gate while the freeze is lifted
- [ ] Exception logged in [Approved Freeze Exception Log](STABLE_CORE_V1_EXCEPTIONS.md#approved-freeze-exception-log) (optional now; still useful as a changelog of *why*)
- [ ] Scope limited to stable core; no bundled UI/installer drive-by changes
- [ ] `STABLE_CORE_V1.lock` — referenced historically as a protected-systems marker file; it does not exist in this repo (nothing to review) and is not required
- [ ] `STABLE_CORE_V1` file banners preserved (run `python scripts/maintenance/tag_stable_core_v1.py` if adding new protected files)

---

## Build

- [ ] `dotnet build` Release — **0 errors**
- [ ] Expected `#warning STABLE_CORE_V1` from `core/StableCoreV1.cs` (do not silence without core version bump)
- [ ] `dotnet test` — all tests pass

---

## Recording tests (dual USB cameras)

Use j5 Webcam JVU250 × 2 or equivalent. Record **≥ 10 minutes** per preset unless testing a short bug repro.

### 1080p dual-camera test

- [ ] Resolution **1920×1080**, FPS **30**
- [ ] Both MP4 files play; no corruption
- [ ] `RequestedResolution` = `1920x1080` in metadata
- [ ] Writer drops = **0** (`WriterQueueDrops`)
- [ ] `RecordingTimingMode` = `OriginalCapture` in metadata

### 720p dual-camera test

- [ ] Resolution **1280×720**, FPS **30**
- [ ] Actual resolution matches in metadata
- [ ] Writer drops = **0** (`WriterQueueDrops`)

### 360p dual-camera test

- [ ] Resolution **640×480**, FPS **30**
- [ ] Actual resolution matches in metadata
- [ ] Writer drops = **0** (`WriterQueueDrops`)

---

## Integrity checks (all test sessions)

- [ ] **0** corrupt / unreadable files (ffprobe succeeds)
- [ ] **0** Writer drops (`WriterQueueDrops`)
- [ ] **0** duplicates (`DuplicatedFrames` / `DuplicateFrames`) — Original Capture Mode must not insert duplicate frames
- [ ] **0** placeholders (`PlaceholderFrames`) — Original Capture Mode must not insert placeholder frames
- [ ] `FramesCaptured` and `FramesWritten` match (≤ 1 frame stop-edge allowed)
- [ ] `CaptureIntervalMeanMs` is nonzero and ~ **34.4 ms** (±0.5 ms) for ~29 fps delivery
- [ ] `ScientificTimingStatus` populated — **PASS** or **PASS_WITH_WARNING** (not FAIL, not empty)
- [ ] `metadata.json`, `metadata.txt`, `session_summary.txt` present per session
- [ ] `camN_frame_timestamps.csv` present per camera — **Timestamp CSV** is the primary scientific timing source
- [ ] Timestamp CSV row count = `FramesWritten` for each camera (no row-count mismatch)
- [ ] `MaxTotalQueueDrops` is **0** (or nonzero and matches actual drops — not silently zero when drops occurred)
- [ ] Metadata output is **privacy-safe**: no absolute paths, hardware IDs, computer names, or usernames

---

## Synchronized start gate (active 2–4 camera sessions)

- [ ] Active 2-camera session: inter-camera first-frame start offset **< 50 ms** (synchronized start gate active for ≥ 2 active slots)
- [ ] Start offset **50–100 ms** → `PASS_WITH_WARNING` (expected for borderline timing; not FAIL)
- [ ] Start offset **> 100 ms** → `FAIL` (synchronized start gate may not have fired)
- [ ] 1-camera session: start offset check not applicable (single slot; no inter-camera comparison)

---

## Video Verification (in-app)

Open **Video Verification** page on regression folder:

- [ ] Scan finds all sessions and videos
- [ ] Verify All completes without crash
- [ ] **0 FAIL** overall (PASS_WITH_WARNING acceptable for container vs camera FPS mismatch or 50–100 ms offset)
- [ ] Expected resolution/FPS from session metadata (not global appsettings)
- [ ] Session groups show per-session comparison only
- [ ] PASS_WITH_WARNING verdict shown correctly for sessions with moderate offset or FPS mismatch
- [ ] FAIL verdict shown correctly for sessions with Writer drops, duplicate/placeholder frames, or missing Timestamp CSV

---

## Session comparison logic

- [ ] Each session audited separately
- [ ] Inter-camera metrics only within same session folder
- [ ] Videos from **different sessions are not compared** with each other
- [ ] cam1/cam2 (and cam3/cam4 if used) listed in session audit
- [ ] Frame diff, start offset, wall duration diff shown in session summary
- [ ] Start offset classification matches thresholds: < 50 ms → PASS, 50–100 ms → PASS_WITH_WARNING, > 100 ms → FAIL

---

## Offline audit vs app agreement

```powershell
$env:MULTICAMAPP_ROOT = "dist"
python scripts/diagnostics/audit_videos_folder.py "C:\path\to\regression\parent"
dotnet run --project scripts/diagnostics/VerifyFolderCli/VerifyFolderCli.csproj -c Release -- "C:\path\to\regression\parent"
```

- [ ] Offline audit: **0 FAIL**
- [ ] App verification CLI/page: **0 FAIL**
- [ ] Per-video verdicts match between offline audit and app verification
- [ ] Frame counts and resolutions agree

---

## Export checks

- [ ] Export TXT from Video Verification page opens and lists all rows
- [ ] Export JSON valid and includes session audits
- [ ] Export CSV includes expected columns

---

## Post-change

- [ ] Update [CHANGELOG.md](../CHANGELOG.md) with regression note
- [ ] If core behavior changed intentionally, update [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) or plan `STABLE_CORE_V2`
- [ ] Do **not** change verification thresholds or metadata calculations without explicit scientific justification

---

## Quick pass/fail

| Check | Pass criteria |
|-------|---------------|
| Audit / verification FAIL count | 0 |
| Writer drops | 0 |
| Duplicates / placeholders | 0 — Original Capture Mode only |
| Corrupt MP4 | 0 |
| Timestamp CSV row count | = `FramesWritten` per camera |
| Inter-camera frame diff | Explained by Real Capture FPS differences in Original Capture Mode |
| Start offset (2–4 cam) | < 50 ms → PASS; 50–100 ms → PASS_WITH_WARNING; > 100 ms → FAIL |
| Offline vs in-app verdicts | Match |
| Privacy-safe metadata | No paths, hardware IDs, hostnames, or usernames in output |
