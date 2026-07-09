# STABLE_CORE_V2 — Freeze Declaration

> **Effective 2026-07-10, MultiCamApp v2.0.0 (build 333) — first stable release.** The project owner declared v2.0.0 the second stable milestone and asked to freeze the *current, actively-used* production pipeline against accidental changes or deletion, while still allowing genuine bug fixes through the same exception process STABLE_CORE_V1 used. This document is that freeze declaration.

**Freeze name:** `STABLE_CORE_V2`
**Freeze version:** MultiCamApp **v2.0.0** (build **333**), 2026-07-10
**Compile marker:** `core/StableCoreV2.cs`
**Regression checklist:** [STABLE_CORE_V2_REGRESSION_CHECKLIST.md](STABLE_CORE_V2_REGRESSION_CHECKLIST.md)

---

## Relationship to STABLE_CORE_V1

STABLE_CORE_V1 (2026-06-11, MultiCamApp v1.0.36) froze the **legacy OpenCV/DirectShow recording pipeline** — at the time, the only recording engine that existed. That freeze was **lifted** on 2026-07-09 (v1.2.104) by explicit project-owner directive, with the stated intent to "freeze a new, updated baseline later once the VideoEngineV2-era fixes settle" (see [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md)). **This is that new baseline.**

Since STABLE_CORE_V1 was declared, the entire recording engine was rewritten (VideoEngineV2: WinRT `MediaCapture` + a raw Media Foundation `IMFSinkWriter` H.264 pipeline with real per-frame VFR timestamps and BT.709 color tagging), a GPU-accelerated preview renderer shipped (Direct3D11 via Vortice.Windows, with a confirmed software fallback), and the verification/session-comparison pipeline gained native understanding of VideoEngineV2's metadata schema (previously schema-blind, a root cause fixed in v1.2.104). None of that code existed, or worked correctly, when STABLE_CORE_V1 was declared — per the existing STABLE_CORE_V1_EXCEPTIONS.md policy ("bump to `STABLE_CORE_V2` only if core behavior intentionally changed"), this qualifies for a new freeze designation rather than reusing the old one.

**What this means in practice:**

- Files listed below now carry a `STABLE_CORE_V2` banner. Files that previously carried `STABLE_CORE_V1` and are listed below had that banner **replaced** with `STABLE_CORE_V2` (not stacked) — this document is the historical record of that lineage, so the file header doesn't need to repeat it.
- The **legacy OpenCV/DirectShow engine** (dormant — confirmed via direct code search that `RecordingSession` is never instantiated from `MainWindow.xaml.cs` on any machine audited this project) keeps its original `STABLE_CORE_V1` banners, unchanged. It remains a **dormant safety net** per the project owner's explicit 2026-07-09 decision, not part of this new freeze. See [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) for that subtree's status.
- STABLE_CORE_V1's exception log (Exceptions #1–#9) remains the accurate historical record of changes made to the *legacy* pipeline while it was frozen. It is not retroactively reinterpreted as covering VideoEngineV2.

---

## Purpose

The following systems are **validated and production-stable as of v2.0.0**. Protected code may be changed **only** under the exception process below (mirroring STABLE_CORE_V1's, not a stricter or looser bar) — the project owner has pre-authorized this: *"these can be fixed and freeze lift if necessary components are truly bugs and error exist with related problems... all freeze and protected codes can be fixed"* when a fix is genuinely needed.

| System | Scope |
|--------|--------|
| **VideoEngineV2 Recording Engine** | Camera discovery/open, format negotiation, frame acquisition, GPU-accelerated preview (with WPF software fallback), raw `IMFSinkWriter` H.264 encoding, backend selection/metadata (`capture/backend/`) |
| **Native V2 Metadata** | Per-camera/session metadata written by `MainWindow.xaml.cs`'s V2 recording path; the post-finalize frame-count integrity check (`postFinalizeFrameCountMismatch`) |
| **Video Verification Logic** | Scan, ffprobe cross-checks, native V2 schema parsing, PASS/PASS_WITH_WARNING/FAIL classification, Deep Verify (independent per-frame MD5), export (TXT/JSON/CSV) |
| **Session Comparison Logic** | Intra-session multi-camera sync/consistency, inter-camera offset/frame-diff/wall-clock-diff, V2-aware reconciliation of legacy schema-blind false positives |

**Primary paths:** `capture/video_engine_v2/`, `capture/backend/`, `MainWindow.xaml.cs` (recording orchestration), `verification/`, `metadata/ScientificTimingAssessor.cs`, `ui/pages/VideoVerificationPage.xaml.cs`, `utils/MonotonicClock.cs`, `core/AppConfig.cs`.

Future work should focus on **installer, UI polish, reports, documentation, OFLA analysis** unless a stable-core bug is proven, exactly as STABLE_CORE_V1's policy read.

---

## Protected files

Every file below now carries the `STABLE_CORE_V2` banner (applied via `scripts/maintenance/tag_stable_core_v2.py`):

**VideoEngineV2 recording engine** (`capture/video_engine_v2/`):
`CameraControlManagerV2.cs`, `CameraDeviceManagerV2.cs`, `CameraDeviceWatcher.cs`, `CameraFormatSelectorV2.cs`, `CameraPipelineV2.cs`, `D3D11Interop.cs`, `D3D11PreviewPanel.cs`, `D3D11SwapChainHost.cs`, `Direct3DPreviewRenderer.cs`, `FrameTimestampMonitor.cs`, `MediaFoundationCaptureService.cs`, `MediaFoundationEncoderService.cs`, `MediaFoundationRuntime.cs`, `RecordingHealthMonitor.cs`, `VideoEngineBackendSelector.cs`, `VideoEngineDiagnostics.cs`, `VideoEngineFallbackPolicy.cs`, `VideoEngineSettings.cs`, `VideoEngineV2.cs`, `VideoEngineV2Models.cs`

**Backend abstraction / registry** (`capture/backend/`):
`VideoEngineRegistry.cs`, `BackendIdentifiers.cs`, `IVideoEngineBackend.cs`, `V2SelectionHardeningModels.cs`

**Recording orchestration:**
`MainWindow.xaml.cs`

**Video verification + session comparison** (`verification/`):
`VideoVerificationService.cs`, `SessionComparisonService.cs`, `VideoProbeService.cs`, `VideoScanner.cs`, `ExpectedSettingsResolver.cs`, `MetadataParser.cs`, `VerificationReportWriter.cs`, `VerificationReportMapper.cs`, `CameraAuditStatus.cs`, `RecordingSessionDiscovery.cs`, `VerificationCaptureProfile.cs`, `VerificationTableRow.cs`, `VerificationResult.cs`, `VerificationMatchStatus.cs`, `V2MetadataReader.cs`, `V2VerificationRunner.cs`, `V2RecordingVerifier.cs`, `MetadataCompletenessPolicy.cs`, `DeepVerifyService.cs`

**Verification UI:**
`ui/pages/VideoVerificationPage.xaml.cs`

**Cross-cutting scientific timing / shared infrastructure:**
`metadata/ScientificTimingAssessor.cs`, `utils/MonotonicClock.cs`, `core/AppConfig.cs`

48 files total.

---

## Exception process

Identical structure to STABLE_CORE_V1's — see [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md#freeze-exception-conditions) for the 5 trigger categories (recording failure, camera-slot/device-mapping bug, crash/recovery issue, scientific accuracy issue, hardware handling issue) and the "rules for exception fixes" (smallest targeted change, diagnostics before timing changes, regression pass, document the reason). A `STABLE_CORE_V2`-specific exception log is kept in [STABLE_CORE_V2_EXCEPTIONS.md](STABLE_CORE_V2_EXCEPTIONS.md) rather than reusing the V1 log, since the two now protect different code.

**Do not:**
- Refactor stable systems casually.
- Remove `STABLE_CORE_V2` file banners without a deliberate `STABLE_CORE_V3` bump (i.e. another intentional core-behavior change, not routine maintenance).

## Related documents

- [STABLE_CORE_V1_FREEZE.md](STABLE_CORE_V1_FREEZE.md) — original freeze declaration; still governs the dormant legacy OpenCV engine
- [STABLE_CORE_V1_EXCEPTIONS.md](STABLE_CORE_V1_EXCEPTIONS.md) — legacy exception policy and historical log (exceptions #1–#9)
- [STABLE_CORE_V2_EXCEPTIONS.md](STABLE_CORE_V2_EXCEPTIONS.md) — this freeze's exception policy and log
- [STABLE_CORE_V2_REGRESSION_CHECKLIST.md](STABLE_CORE_V2_REGRESSION_CHECKLIST.md) — regression checklist for changes to protected V2 code
