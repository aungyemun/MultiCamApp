# STABLE_CORE_V2 Regression Checklist

**Applies to:** `STABLE_CORE_V2` (freeze declared MultiCamApp v2.0.0 build 333, 2026-07-10)
**Required before:** Any change to protected VideoEngineV2 recording, native V2 metadata, video verification, or session-comparison code (see [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md#protected-files) for the file list)
**Reference:** [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md) · [STABLE_CORE_V2_EXCEPTIONS.md](STABLE_CORE_V2_EXCEPTIONS.md)

---

## Pre-change

- [ ] Change is justified by a [freeze exception trigger](STABLE_CORE_V2_EXCEPTIONS.md#freeze-exception-conditions)
- [ ] Exception logged in [Approved Freeze Exception Log](STABLE_CORE_V2_EXCEPTIONS.md#approved-freeze-exception-log) (or row prepared before merge)
- [ ] Scope limited to stable core; no bundled UI/installer drive-by changes
- [ ] `STABLE_CORE_V2` file banners preserved (run `tools/python/python.exe scripts/maintenance/tag_stable_core_v2.py` if adding new protected files)

---

## Build

- [ ] `dotnet build` Release — **0 errors**
- [ ] Expected `#warning STABLE_CORE_V2` from `core/StableCoreV2.cs` (do not silence without a core version bump)
- [ ] `dotnet test` — all tests pass (295 baseline as of v2.0.0)

---

## Recording tests (real cameras, ≥ 1 minute per case unless testing a short bug repro)

- [ ] 1080p @ 30fps, 1–4 cameras: MP4 plays, no corruption, `RequestedResolution`/`selectedFormatFps` match metadata
- [ ] 720p @ 30fps, 1–4 cameras: same checks
- [ ] Mixed-capability session (e.g. one 60fps-capable camera + cameras that can only deliver ~30fps): confirm the app reports the real per-camera measured FPS honestly rather than hiding or averaging over the mismatch (see the 2026-07-10 audit of `test5_20260710_010224`/`test4_20260710_010127` for the expected reporting shape)
- [ ] Writer drops = 0 for a clean recording; if a real drop occurs, it must be reported (not silently zero — this is the same `MaxTotalQueueDrops` risk class STABLE_CORE_V1 fixed once already)

---

## Frame integrity checks (all test sessions)

- [ ] **0** corrupt/unreadable files (ffprobe succeeds)
- [ ] Metadata's `framesWritten` matches the real file's frame count — cross-check independently via `ffprobe -count_frames` or `-show_entries stream=nb_frames`, not just the app's own self-report
- [ ] `postFinalizeFrameCountMismatch` is `false` for a clean recording; if `true`, confirm `frameIntegrity.integrityVerdict` reports `WARN_POST_FINALIZE_MISMATCH` and the per-camera `verification.notes` explains the actual vs. expected counts (this is the exact mechanism fixed in v1.2.109/v1.2.112 — a regression here would silently re-hide a real frame-count problem)
- [ ] **0** duplicate frames — verify with exact MD5 frame hashing (`ffmpeg -f framemd5 -fps_mode passthrough`, **not** `mpdecimate`, which gives false positives on real camera footage) if duplicate-frame logic is touched
- [ ] Color tags: `color_range=pc`, `color_space`/`color_primaries=bt709` on the output MP4
- [ ] Timestamp CSV row count and interval statistics (mean/min/max) independently recomputed from the raw CSV match the app's own reported values

---

## GPU preview / hardware fallback

- [ ] GPU-accelerated preview (`Direct3DPreviewRenderer`) initializes and renders on real hardware
- [ ] Forcing (or simulating) a D3D11 device-lost condition triggers `FallBackToWpf()` and preview continues via `WriteableBitmap` without a crash
- [ ] `DriverType.Hardware` device creation path is vendor-agnostic — do not introduce a check that only works on one GPU vendor

---

## Video Verification (in-app)

- [ ] Scan finds all sessions and videos
- [ ] Verify All completes without crash
- [ ] A V2 session's raw (schema-blind legacy) FAIL is correctly reconciled to the session's own native V2 verdict — check the reconciled status uses the **full** `CameraAuditStatus` vocabulary (`"PASS_WITH_WARNING"`, not a shortened `"WARNING"` token) consistently across the summary cards, per-row status, and export fields (the exact bug fixed in v1.2.112 — a regression would reintroduce inconsistent status text between reconciled and untouched rows of the same severity)
- [ ] Exported `video_audit_report.txt`/JSON/CSV agree with the on-screen corrected result, not the raw pre-reconciliation one
- [ ] "Clear results" actually clears the Verification Log, not just the table

---

## Session comparison logic

- [ ] Each session audited separately; videos from different sessions never compared
- [ ] Inter-camera frame/FPS/duration differences are explained by real measured-FPS differences in Original Capture Mode, not flagged as false failures
- [ ] No duplicate/redundant interpretation notes for the same underlying condition (e.g. two near-identical "frame counts may differ" notes firing for the same reason — the pattern fixed in v1.2.112)

---

## Post-change

- [ ] Update [CHANGELOG.md](../CHANGELOG.md) with a regression note
- [ ] If core behavior changed intentionally, update [STABLE_CORE_V2_FREEZE.md](STABLE_CORE_V2_FREEZE.md) or plan `STABLE_CORE_V3`
- [ ] Do **not** change verification thresholds or metadata calculations without explicit scientific justification

---

## Quick pass/fail

| Check | Pass criteria |
|-------|---------------|
| Build/test | 0 errors, all tests pass |
| Metadata vs. real file frame count | Match (or a genuine mismatch is surfaced, not hidden) |
| Duplicate frames | 0 (verified via exact MD5 hash, not perceptual dedup) |
| Color tags | `bt709`/`pc` on every recorded MP4 |
| GPU preview fallback | Falls back to WPF cleanly on device loss |
| Verdict vocabulary | Consistent (`PASS`/`PASS_WITH_WARNING`/`FAIL`) across UI, cards, and export |
| Offline vs. in-app verdicts | Match |
| Privacy-safe metadata | No paths, hardware IDs, hostnames, or usernames in output |
