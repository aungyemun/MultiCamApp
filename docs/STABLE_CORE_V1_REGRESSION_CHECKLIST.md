# STABLE_CORE_V1 Regression Checklist

**Applies to:** `STABLE_CORE_V1` (MultiCamApp v1.0.36 build 136)  
**Required before:** Any change to protected recording, metadata, verification, or session-comparison code  
**Reference:** [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) · [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md)

---

## Pre-change

- [ ] Change is justified by a [freeze exception trigger](STABLE_CORE_V1_EXCEPTIONS.md#freeze-exception-conditions) (reproducible bug, corruption, frame drops, sync failure, scientific accuracy, or hardware-handling issue)
- [ ] Exception logged in [Approved Freeze Exception Log](STABLE_CORE_V1_EXCEPTIONS.md#approved-freeze-exception-log) (or row prepared before merge)
- [ ] Scope limited to stable core; no bundled UI/installer drive-by changes
- [ ] [STABLE_CORE_V1.lock](../STABLE_CORE_V1.lock) reviewed — protected systems understood
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
- [ ] **0** duplicates (`DuplicatedFrames` / `DuplicateFrames`)
- [ ] **0** placeholders (`PlaceholderFrames`)
- [ ] Frame difference **≤ 5** frames per session (cam1 vs cam2)
- [ ] Start offset **≤ 100 ms**
- [ ] `FramesCaptured` and `FramesWritten` match (≤ 1 frame stop-edge allowed)
- [ ] `CaptureIntervalMeanMs` is nonzero and ~ **34.4 ms** (±0.5 ms) for ~29 fps delivery
- [ ] `ScientificTimingStatus` populated (PASS or PASS_WITH_WARNING, not FAIL)
- [ ] `metadata.json`, `metadata.txt`, `session_summary.txt` present per session

---

## Video Verification (in-app)

Open **Video Verification** page on regression folder:

- [ ] Scan finds all sessions and videos
- [ ] Verify All completes without crash
- [ ] **0 FAIL** overall (Warning acceptable for container vs camera FPS)
- [ ] Expected resolution/FPS from session metadata (not global appsettings)
- [ ] Session groups show per-session comparison only

---

## Session comparison logic

- [ ] Each session audited separately (11 sessions → 11 session groups when testing final matrix)
- [ ] Inter-camera metrics only within same session folder
- [ ] Videos from **different sessions are not compared** with each other
- [ ] cam1/cam2 (and cam3/cam4 if used) listed in session audit
- [ ] Frame diff, start offset, wall duration diff shown in session summary

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

- [ ] Update [CHANGELOG.md](changelogs/CHANGELOG.md) with regression note
- [ ] If core behavior changed intentionally, update [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) or plan `STABLE_CORE_V2`
- [ ] Do **not** change verification thresholds or metadata calculations without explicit scientific justification

---

## Quick pass/fail

| Check | Pass |
|-------|------|
| Audit / verification FAIL count | 0 |
| Writer drops | 0 |
| Duplicates / placeholders | 0 |
| Corrupt MP4 | 0 |
| Inter-camera frame diff | Explained by Real Capture FPS in Original Capture Mode |
| Start offset | ≤ 100 ms |
| Offline vs in-app verdicts | Match |
