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

Record new exceptions here **before** or **when** merging the fix. Empty as of the freeze declaration (2026-07-10, v2.0.0) — this is the first entry point going forward.

| Date | App version | Trigger condition | Affected component | Reason | Files changed | Regression tests run | Result | New stable version |
|------|-------------|-------------------|--------------------|--------|---------------|----------------------|--------|-------------------|
| _(none yet)_ | | | | | | | | |

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
