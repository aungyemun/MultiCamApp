# Changelog

All notable changes to MultiCamApp will be documented in this file.

## [v2.0.3] - 2026-07-10 (build 336)

> **User asked for a full audit of `installer/MultiCamApp.iss` against seven specific installer-behavior requirements**: skip the VC++ Redistributable install if already present (no duplicate install) and keep it updated; check whether any other runtime `.exe` is needed; clean-replace old files/folders on upgrade; replace Desktop/Start Menu shortcuts on upgrade; only create the Desktop shortcut if its task is selected; default the Start Menu task to checked but keep it deselectable; ship an uninstaller in the Start Menu that fully removes the app (including its own folder) without touching user video folders.
>
> Read the full 673-line `.iss` script end-to-end against each requirement. Most of the upgrade-cleanup/backup logic already existed and was sound. Found and fixed four real gaps:
>
> 1. **VC++ Redistributable launched unconditionally.** It relied on the bootstrapper's own exit code (1638) to detect "already installed" — functionally correct but always spent a few seconds launching the ~25 MB bootstrapper even when unnecessary. Added an explicit registry check (`HKLM64\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64`, `Installed`) so `RunVCRedistBeforeRuntime` now skips launching it at all when an equal/newer runtime is already present. Also downloaded the current `aka.ms/vs/17/release/vc_redist.x64.exe` and diffed it against the bundled copy — byte-identical (same SHA256), so no binary refresh was needed.
> 2. **Start Menu shortcuts were never gated by their own task.** The `[Icons]` entries for the app, uninstaller, and diagnostic-launcher shortcuts had no `Tasks:` filter at all, so deselecting "Create Start Menu shortcuts" in the wizard did nothing — they were created every time regardless. Added `Tasks: startmenuicon` to all four group entries.
> 3. **Orphaned shortcuts on a deselected upgrade.** If a shortcut task was deselected on an upgrade, the old shortcut from the prior install was never removed (it was only ever removed as part of recreating it, which is skipped when the task is off). Added an unconditional `RemoveExistingDesktopShortcuts` call plus a new `RemoveExistingStartMenuGroup` (removes `{group}` when `startmenuicon` is deselected) in `CurStepChanged(ssInstall)`, so shortcuts always end up consistent with the currently selected tasks.
> 4. **Uninstall left most of the app folder behind.** `[UninstallDelete]` only removed top-level `*.dll`/`*.exe`/`*.json` plus `runtime\` and `logs\`, then tried `dirifempty` on `{app}` — but config, localization, assets, `lib`, `platform`, `resources`, the .NET satellite-resource locale folders, `tools`, `scripts`, `_internal`, and the upgrade-backup folders were never listed, so the install folder was never actually empty and never got removed. Replaced the whole section with a single recursive `Type: filesandordirs; Name: "{app}"`, which fully removes the folder and everything in it. Confirmed safe: recordings default to `%USERPROFILE%\Videos` (or wherever the user pointed the output folder) via `OutputFolderManager`/`PathHelper.DefaultVideosFolder` — never under `{app}` — so this cannot delete user recordings.
>
> Also confirmed: no other system-level installer is needed alongside `vc_redist.x64.exe` — the app is a self-contained .NET 8 single-file publish, so no separate .NET runtime install is required.
>
> Updated `docs/architecture/installer_logic.md`'s Installation Flow and Uninstallation sections and `INSTALLATION.md`'s End-User Installation/Uninstallation sections to describe the corrected behavior accurately, and fixed a stale `installer\Setup.exe` example path in `INSTALLATION.md` (should be `installer\MultiCamApp_{version}_Setup.exe`, matching the same fix already made elsewhere this session).

### Fixed
* **`installer/MultiCamApp.iss`** — VC++ Redistributable now skipped via registry check when already installed; Start Menu shortcuts now correctly gated by the `startmenuicon` task; Desktop/Start Menu shortcuts are cleared before reinstall so a deselected task never leaves an orphaned shortcut; uninstall now fully removes the entire app folder instead of leaving several subfolders behind.

### Changed
* **`docs/architecture/installer_logic.md`, `INSTALLATION.md`** — Installation Flow / Uninstallation sections rewritten to match the corrected installer behavior; stale `installer\Setup.exe` path fixed in `INSTALLATION.md`.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `2.0.3`. 295 tests passing (unchanged baseline). Installer changes verified via a full ISCC compile (`MultiCamApp_2.0.3.336_Setup.exe`, successful).

## [v2.0.2] - 2026-07-10 (build 335)

> **User was running the actual v2.0.1.334 Setup.exe and asked me to check the installer's License Agreement page for staleness.** Traced `docs/license/LICENSE.txt` (fixed for its stale v1.1.0 citation in an earlier pass this session) and discovered it is not just a repo documentation file — `MultiCamApp.iss` uses it directly as `LicenseFile` (compiled into the installer's License Agreement wizard page) and also copies it into the installed app folder as `LICENSE.txt`. That earlier fix was treated as "documentation-only, no rebuild needed" at the time, which was wrong for this specific file: the already-built and already-shared v2.0.1.334 `Setup.exe` still had the old, stale citation baked into its License Agreement page, since it was compiled before the fix.
>
> This release has no functional/behavior changes — it exists solely to rebuild the installer with the corrected `docs/license/LICENSE.txt` actually compiled in, and to bump the citation strings that reference the version number to stay consistent with the new build.

### Fixed
* **Installer License Agreement page** — now shows the corrected citation (previously stuck at the stale "v1.1.0... behavioral analysis" wording even after the source file was fixed, because the installer hadn't been rebuilt since).

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `2.0.2`. 295 tests passing (unchanged baseline).

## [v2.0.1] - 2026-07-10 (build 334) — STABLE_CORE_V2 Freeze Declared

> **User asked to freeze the current camera recording components, logic, and other important app components** now that v2.0.0 is the second stable release — protecting them from accidental changes or deletion, while explicitly pre-authorizing genuine bug fixes through an exception process (mirroring how STABLE_CORE_V1 worked before it was lifted).
>
> **Declared `STABLE_CORE_V2`** — this is the "new, updated baseline" that `STABLE_CORE_V1_FREEZE.md`'s 2026-07-09 lift note said would come later, now that the VideoEngineV2-era work has settled into a stable release. Surveyed the actual current, actively-used production code (not the dormant legacy engine) and applied the freeze to **48 files**: the entire VideoEngineV2 recording engine (`capture/video_engine_v2/*`, 20 files — every one of which had **zero** freeze protection until now, despite being the app's only active camera recording engine), the backend abstraction/registry (`capture/backend/*`, 4 files), recording orchestration (`MainWindow.xaml.cs`), the native V2-aware verification/session-comparison pipeline (`verification/*`, 19 files), the verification UI (`ui/pages/VideoVerificationPage.xaml.cs`), and shared cross-cutting infrastructure (`metadata/ScientificTimingAssessor.cs`, `utils/MonotonicClock.cs`, `core/AppConfig.cs`).
>
> Added a new compile-time marker (`core/StableCoreV2.cs`, mirroring `core/StableCoreV1.cs`) that emits its own `#warning STABLE_CORE_V2` on every build, alongside the existing V1 warning. Wrote `scripts/maintenance/tag_stable_core_v2.py` to apply the banner (and used it — don't hand-edit 48 files when a script can do it consistently and repeatably for the next audit).
>
> **The dormant legacy OpenCV/DirectShow engine is explicitly *not* part of this new freeze** — it keeps its original `STABLE_CORE_V1` banners unchanged, per the project owner's prior (2026-07-09) decision to keep it only as a dormant safety net. For files being promoted to `STABLE_CORE_V2` that previously carried `STABLE_CORE_V1`, the old banner was replaced (not stacked) — the lineage is documented once, in `STABLE_CORE_V2_FREEZE.md`, rather than repeated in every file header.
>
> **New docs**: `docs/STABLE_CORE_V2_FREEZE.md` (declaration, protected-systems table, full file list, V1 lineage), `docs/STABLE_CORE_V2_EXCEPTIONS.md` (exception policy — same 5 trigger categories as V1, empty log ready for future entries), `docs/STABLE_CORE_V2_REGRESSION_CHECKLIST.md` (adapted for what V2 actually needs checked — GPU preview fallback, native metadata cross-checks against real ffprobe data, the `postFinalizeFrameCountMismatch` mechanism, verdict-vocabulary consistency — reflecting the real issues found and fixed in this pipeline over the last several releases, not a generic template). Updated `STABLE_CORE_V1_FREEZE.md` and `STABLE_CORE_V1_EXCEPTIONS.md` with pointers to the new freeze so a reader following either trail lands in the right place.
>
> This is a policy/documentation change plus banner comments — no recording, verification, or metadata **logic** was touched. 295 tests passing (unchanged baseline).

### Added
* **`docs/STABLE_CORE_V2_FREEZE.md`, `docs/STABLE_CORE_V2_EXCEPTIONS.md`, `docs/STABLE_CORE_V2_REGRESSION_CHECKLIST.md`** — new freeze declaration, exception policy, and regression checklist for the actively-used VideoEngineV2 + verification pipeline.
* **`source/MultiCamApp/MultiCamApp/core/StableCoreV2.cs`** — compile-time freeze marker, emits `#warning STABLE_CORE_V2` on every build.
* **`scripts/maintenance/tag_stable_core_v2.py`** — applies/refreshes the `STABLE_CORE_V2` banner across the 48 protected files.

### Changed
* **48 files** now carry the `STABLE_CORE_V2` banner (see `docs/STABLE_CORE_V2_FREEZE.md#protected-files` for the full list) — the entire `capture/video_engine_v2/` engine, `capture/backend/*`, `MainWindow.xaml.cs`, `verification/*`, `ui/pages/VideoVerificationPage.xaml.cs`, `metadata/ScientificTimingAssessor.cs`, `utils/MonotonicClock.cs`, `core/AppConfig.cs`.
* **`docs/STABLE_CORE_V1_FREEZE.md`, `docs/STABLE_CORE_V1_EXCEPTIONS.md`** — added pointers to the new `STABLE_CORE_V2` freeze; clarified that STABLE_CORE_V1 now governs only the dormant legacy engine.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `2.0.1`. 295 tests passing (unchanged baseline).

## [v2.0.0] - 2026-07-10 (build 333) — First Stable Release

> **User asked to declare the app stable at v2.0.0**, update the About page and every version reference, and sweep all markdown docs, the license, security policy, citation metadata, installer, and tooling for consistency with the new stable milestone.
>
> **This is a stage change, not a code change.** No recording, verification, or metadata logic was touched in this release — v2.0.0 formally closes out the `alpha` stage the project intentionally stayed in from v1.0.36 (2026-06-11) through v1.2.112 (2026-07-10) while the VideoEngineV2 rewrite, GPU-accelerated preview (Vortice.Windows), raw `IMFSinkWriter` encoding with real per-frame VFR timestamps, native V2 metadata/verification support, and the STABLE_CORE_V1 freeze-lift all shipped and settled. `config/version.json`'s `stage` field moves from `"alpha"` to `"stable"` — the About page, recording metadata, and diagnostic logs all read this field dynamically (`VersionService`/`VersionInfo`), so no UI code changes were needed for the About page to correctly show "Release Stage: stable"; confirmed by reading `AboutPage.xaml.cs` and `VersionService.cs` directly rather than assuming.
>
> **Version bumped everywhere in lockstep**: `config/version.json`, `MultiCamApp.csproj` (`Version`/`FileVersion`/`InformationalVersion`), `capture/backend/VideoEngineRegistry.cs` (`BackendVersion`), `MultiCamApp.Tests/VideoEngineBackendTests.cs` (matching assertion), `installer/MultiCamApp.iss` (`AppVersion`/`AppVersionNumeric`).
>
> **Docs and metadata swept for consistency with the new stable status:**
> - `README.md`, `INSTALLATION.md` — "Current release" banner and citation strings updated to v2.0.0.
> - `CITATION.cff` — `version`/`date-released` updated.
> - `LICENSE.md` — found and fixed a citation string that had drifted all the way back to **v1.1.0** (stale since long before this session) and used different wording than every other citation instance in the app (README/INSTALLATION/About page); now reads v2.0.0 with the same wording everywhere.
> - `SECURITY.md` — "Supported Versions" table updated from `1.2.x`/`< 1.2.0` to `2.0.x`/`< 2.0.0`.
> - `docs/STABLE_CORE_V1_FREEZE.md` — "Current app release" line updated.
> - `docs/developer_notes/versioning.md` — the note explaining "the project stayed in `alpha` past `1.0.0`" is now framed as historical (with an end date), a new note marks the move to `stable` as of v2.0.0, the stale `version.json` example (frozen at v1.2.67 since a much earlier session) refreshed to the current shape, and the "do not rush to 1.0.0" recommendation replaced with real guidance for the post-2.0.0 increment path (reserve the next major bump for an actual breaking-change milestone, not routine fixes).
>
> **Checked but found no changes needed**: `THIRD_PARTY_NOTICES.md` (only references package versions, not app version — accurate as-is), `.gitignore` (installer ignore patterns are already version-agnostic wildcards, e.g. `installer/*_Setup.exe`), `global.json` (.NET SDK pin, unrelated to app version), `CONTRIBUTING.md` and `DIRECTORY_STRUCTURE.md` (no app-version-specific content), `CITATION.cff`'s `cff-version: 1.2.0` (this is the *Citation File Format spec version*, unrelated to MultiCamApp's own version — verified before assuming it needed changing), installer helper scripts (`extract_version_json.ps1`/`extract_file_productversion.ps1` only contain generic illustrative comments, no hardcoded version dependency), vendored `tools/`/NuGet package versions (independent of app version by design).

### Changed
* **`source/MultiCamApp/MultiCamApp/config/version.json`** — `stage` moved from `"alpha"` to `"stable"`; version `1.2.112` → `2.0.0`.
* Version bumped in lockstep across `MultiCamApp.csproj`, `VideoEngineRegistry.cs`, `VideoEngineBackendTests.cs`, `installer/MultiCamApp.iss`.

### Fixed
* **`LICENSE.md`** — citation string was stuck at v1.1.0 with inconsistent wording versus every other citation instance in the app; now matches and is current.
* **`SECURITY.md`** — "Supported Versions" table updated to the current release line.

### Docs
* `README.md`, `INSTALLATION.md`, `CITATION.cff`, `docs/STABLE_CORE_V1_FREEZE.md`, `docs/developer_notes/versioning.md` updated for the v2.0.0 stable milestone.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `2.0.0`. 295 tests passing (unchanged baseline — this release is a stage/version change only, no logic touched).

## [v1.2.112] - 2026-07-10 (build 332)

> **User asked for a full audit of two new recordings, a cleanup pass on the Video Verification page's duplicate/unnecessary text, and a GitHub-upload readiness + cross-hardware compatibility check** of the whole app.
>
> **New recordings (`test1_20260709_234329`, `test2_20260709_234741`) confirmed the v1.2.109 frame-count-mismatch fix is now live and working**: `cam4`'s metadata showed `integrityVerdict: "WARN_POST_FINALIZE_MISMATCH"` with `postFinalizeProbedFrameCount: 7015` vs. a claimed `framesWritten: 7225` — independently confirmed via `ffprobe -count_frames` that the real file genuinely has 7015 frames (210 fewer than the app's own submission counter tracked, the *opposite* direction from the earlier v1.2.109 case: this time frames were seemingly accepted by `WriteSample` but never survived into the final container — exactly the failure mode `postFinalizeFrameCountMismatch` was originally built to catch). All other 7 cameras across both sessions matched ffprobe exactly with `postFinalizeFrameCountMismatch: false`.
>
> **Found and fixed a real vocabulary-mismatch bug while tracing why `cam4`'s status cell showed bare `"WARNING"` while its siblings showed `"PASS_WITH_WARNING"` for the identical severity level.** `ReconcileV2SessionVerdict`/`ReconcileV2Summary` (`VideoVerificationPage.xaml.cs`) write `SessionGroupViewModel.NormalizeSessionResult`'s short internal token ("WARNING") directly into display/export fields (`audit.SessionStatus`, `row.AuditStatus`, `CardTimingConfidenceValue.Text`, `SessionResult.ScientificTimingStatus`) that use the full `CameraAuditStatus` vocabulary ("PASS_WITH_WARNING") everywhere else in the app. Confirmed via `VerificationUiBrushes.ForStatus`/`CountV2RowVerdicts` that colors and counts were unaffected (both already re-normalize before comparing) — this was a text-only inconsistency, not a functional bug. Fixed by mapping the short token back to the full vocabulary before writing to any of these fields, while correctly respecting the existing Simple/Detailed view-mode-dependent short-form display for the Duration/FPS Spread cards.
>
> **Removed genuinely unnecessary/redundant text**, not just cosmetic duplication across repeated user actions (already explained as by-design in v1.2.109):
> - `ClearResults()` never actually cleared the Verification Log (`LogBox`) — the button named "Clear results" cleared everything *except* the one place all the repeated text accumulates. Fixed.
> - The exported `video_audit_report.txt`'s "Recording Resource Diagnostics" / "Likely Bottleneck" / "Recommended Action" sections are driven by a legacy-only `CameraMetadataRecord.RecordingDiagnostics` field that `MetadataParser.BuildRecordFromV2Metadata` never populates — confirmed every current (V2) recording's export prints ~10 lines of "unavailable" / "diagnostics inconclusive" / "review diagnostics" boilerplate with zero real signal, on every single session. Now prints one honest line ("not applicable — VideoEngineV2 does not track this legacy-only diagnostic") for fully-V2 sessions instead; legacy recordings keep the original verbose fallback since a real gap there might mean something.
> - `SessionComparisonService.ApplyInterCameraComparison` had two independent conditions (frame-count difference, FPS difference) that, in every real Original-Capture session, both fire simultaneously and each add their own near-identical "frame counts may differ..." note — one with the exact frame-count diff, one without. Now skips the less-informative bare note when the more specific one already fired.
>
> **GitHub-upload readiness and cross-hardware compatibility audit — clean, no changes needed:**
> - Privacy: `Environment.MachineName`/`UserName` are collected by the (dormant) legacy engine but always redacted via `PrivacySanitizer`/`MetadataWriter.cs` before disk write, confirmed by reading the actual redaction code; VideoEngineV2 never collects these fields at all. No hardcoded personal paths or secrets found in source; the one `password`/`token`-adjacent hit was a test fixture using a fake "SECRET-PC" placeholder.
> - `THIRD_PARTY_NOTICES.md` cross-checked against every actual `<PackageReference>` in all three `.csproj` files — exact match, nothing missing or stale.
> - No duplicate/leftover files (`*.bak`, `*_old.cs`, `*_copy.cs`) found anywhere in `source/`.
> - No hardcoded camera-model or GPU-vendor gating found in `capture/` — device names like "j5 Webcam"/"OBSBOT" only appear in comments/diagnostics, never as functional gates. GPU device creation uses `DriverType.Hardware` (vendor-agnostic), and `Direct3DPreviewRenderer.OnGpuDeviceLost` → `FallBackToWpf()` is a real, confirmed software-rendering fallback (matches the v1.2.60 real-hardware validation already on record).
> - `Environment.ProcessorCount` is only used for CPU-percentage normalization (with a `Math.Max(1, ...)` guard), never as a hard minimum-core requirement.
>
> **Cleaned the installer folder**: 5 superseded versioned `Setup.exe` files (v1.2.106 through v1.2.110) had piled up since the last cleanup; removed, keeping only the current version's installer.

### Fixed
* **`ui/pages/VideoVerificationPage.xaml.cs`** — `ReconcileV2SessionVerdict`/`ReconcileV2Summary` now write the full `CameraAuditStatus` vocabulary ("PASS_WITH_WARNING") instead of a short internal token ("WARNING") into `SessionStatus`/`AuditStatus`/summary cards/export fields, fixing a real display inconsistency between reconciled and untouched rows of the same severity.
* **`ui/pages/VideoVerificationPage.xaml.cs`** (`ClearResults`) — "Clear results" now actually clears the Verification Log, not just the table/detail panel.
* **`verification/VerificationReportWriter.cs`** — suppressed the always-empty "Recording Resource Diagnostics"/"Likely Bottleneck"/"Recommended Action" boilerplate for fully-V2 sessions (that legacy-only field is never populated by V2 metadata).
* **`verification/SessionComparisonService.cs`** — no longer adds a redundant bare "frame counts may differ" note when the more specific, numbered version already fired for the same session.

### Verified (no changes needed)
* v1.2.109's `postFinalizeFrameCountMismatch` fix confirmed working on two fresh real recordings — correctly caught a genuine 210-frame discrepancy on one camera, left the other 7 (correctly matching) cameras alone.
* Full privacy, license/third-party-notices, duplicate-file, and cross-hardware-compatibility audit — no issues found.

### Removed
* 5 superseded installer `Setup.exe` files (v1.2.106–v1.2.110) from `installer/`.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.112`. 295 tests passing (unchanged baseline).

## [v1.2.111] - 2026-07-09 (build 331)

> **User asked to trace the exact origin of specific Verification Log lines from a real scan** (`Frame count mismatch vs metadata: 99`, `FAIL: cam2: individual camera audit failed`, `[V2] Corrected false-positive FAIL for session "test1_20260709_222235" -> WARNING`), confirm whether each is true or needs fixing, and fix anything found — including in freeze-related docs/code.
>
> **Traced all three to their source and confirmed the underlying facts are true, but found a real wording inaccuracy in how the app explains them.**
> - `"Frame count mismatch vs metadata: 99"` originates in `VideoVerificationService.cs:456` (STABLE_CORE_V1) — it compares ffprobe's real, decoded frame count against `expected.FrameCount`, which since v1.2.104 is populated by the schema-aware `MetadataParser` reading VideoEngineV2's own claimed `framesWritten`. For this specific video this is **not a schema artifact at all** — it's the same real 99-frame gap identified and fixed in v1.2.109 (`postFinalizeFrameCountMismatch`): `cam2.mp4` genuinely contains 9377 frames while its own metadata claimed 9278. The message is accurate.
> - `"FAIL: cam2: individual camera audit failed"` (`SessionComparisonService.cs:189`) is a generic per-camera rollup line with no room for detail — expected design, the specific reason is logged separately right above it.
> - `"[V2] Corrected false-positive FAIL... -> WARNING"` is where the actual problem was: this message (and its 3 sibling strings — `verifyV2PreScanDisclaimer`, `verifyV2CorrectedNote`, `verifyV2RowCorrectedNote`) unconditionally attributed **every** legacy-scan FAIL on a V2 session to "legacy metadata schema mismatch" / "does not understand VideoEngineV2's metadata schema." That was accurate before v1.2.104 (every V2 field really did read as zero/default), but the frame-count check that fired here is completely unconditional — it runs identically for V2 and legacy sources using schema-correctly-parsed data on both sides — so blaming "schema mismatch" for *this* FAIL is simply false. A user reading the old wording would reasonably (but wrongly) conclude "nothing is actually wrong here," when in fact a real, independently-confirmed 99-frame accounting gap exists.
>
> Verified the correction mechanism doesn't erase the evidence, even with the old wording: `ReconcileV2SessionVerdict` only clears the FAIL-level `Failures`/`ErrorMessages` collections, not `WarningMessages`, and the exported `video_audit_report.txt` still shows `error: Frame count mismatch vs metadata: 99` for cam2 under the (now-WARNING) session — so the real signal was never actually hidden, just mis-explained.
>
> **Fixed the wording** of all 4 messages (`en.json`/`ja.json`) to state both real possibilities — a benign semantic/schema difference, or a genuine independently-confirmed issue — and to direct the reader to check each camera's specific message rather than assuming every reconciled FAIL is automatically benign. Verified placeholder-token counts still match between `en.json`/`ja.json` for all 4 keys and both files remain valid JSON with unchanged key counts (795 each).
>
> Also re-confirmed the previously-fixed STABLE_CORE_V1 freeze-status docs (`STABLE_CORE_V1_FREEZE.md`, `STABLE_CORE_V1_EXCEPTIONS.md`, `STABLE_CORE_V1_REGRESSION_CHECKLIST.md`, from v1.2.110) are consistent with this session's findings — no further freeze-doc changes needed; this turn's fix was to UI explanatory text, not to any STABLE_CORE_V1-protected file (`VideoVerificationPage.xaml.cs` and the localization JSON files carry no `STABLE_CORE_V1` banner).

### Fixed
* **`localization/en.json`, `localization/ja.json`** — `verifyV2PreScanDisclaimer`, `verifyV2CorrectedFalsePositive`, `verifyV2CorrectedNote`, `verifyV2RowCorrectedNote` no longer claim every V2-session FAIL from the legacy scan is a harmless "schema mismatch" — they now state it can also be a real, independently-confirmed issue, and point the reader to each camera's specific message.

### Verified (no changes needed)
* `"Frame count mismatch vs metadata: {diff}"` (`VideoVerificationService.cs`) — accurate, schema-aware, unconditional check; correctly caught the real v1.2.109 frame-count discrepancy on `test1_20260709_222235/cam2`.
* `"{slot}: individual camera audit failed"` (`SessionComparisonService.cs`) — a generic rollup line by design; the specific reason is logged separately.
* `ReconcileV2SessionVerdict` — confirmed it does not erase the genuine warning signal; `video_audit_report.txt` still shows the real per-camera reason after correction.
* STABLE_CORE_V1 freeze-status docs (fixed in v1.2.110) — still consistent with this turn's findings, no further changes needed.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.111`. 295 tests passing (unchanged baseline).

## [v1.2.110] - 2026-07-09 (build 330)

> **User asked to re-check the STABLE_CORE_V1 freeze-lift status** ("citation and current release defreeze and fix all errors" — clarified via follow-up question to mean: confirm the freeze lifted in v1.2.104 is still properly reflected everywhere and fix any stale "still frozen" language).
>
> **Found a real, significant inconsistency**: `docs/STABLE_CORE_V1_EXCEPTIONS.md` was never updated when the freeze was lifted in v1.2.104 — it still stated `**Status:** **ACTIVE** (frozen by default)` and `**STABLE_CORE_V1 remains frozen by default.**`, directly contradicting `STABLE_CORE_V1_FREEZE.md`'s lift note (which even anticipated this: "supersedes the 'still ACTIVE' line below until removed" — but that referred to FREEZE.md's own now-superseded status line, and nobody had carried the same fix over to EXCEPTIONS.md across 5 version bumps since). Added a matching FREEZE LIFTED note, changed `Status` to `LIFTED`, and reworded the Policy Summary section to present the exception-trigger process as good-practice guidance rather than a mandatory gate — while explicitly preserving the Approved Freeze Exception Log (exceptions #1–#9) as the accurate historical record of changes made while the freeze was still active.
>
> Also found and fixed a smaller, related staleness issue: both `STABLE_CORE_V1_FREEZE.md` and `STABLE_CORE_V1_REGRESSION_CHECKLIST.md` reference a `STABLE_CORE_V1.lock` file (as a markdown link in FREEZE.md, and as a checklist item to "review" in the checklist) that **does not exist anywhere in this repo** — confirmed by direct filesystem check. It's `.gitignore`d as a machine-local marker, but nothing ever created it. Both references now note plainly that the file doesn't exist and isn't required, instead of pointing at a dead link / an unreviewable checklist item.
>
> Swept the rest of the repo for the same "still frozen"/"Status: ACTIVE" pattern (`grep` across every `.md` file) — no other hits; `docs/STABLE_CORE_V1_FREEZE.md` itself was already correct (fixed in the v1.2.104 change), and no other doc makes a competing claim about freeze status.

### Fixed
* **`docs/STABLE_CORE_V1_EXCEPTIONS.md`** — added a FREEZE LIFTED note matching `STABLE_CORE_V1_FREEZE.md`'s; changed `Status: ACTIVE (frozen by default)` to `Status: LIFTED`; reworded the Policy Summary to present exception rules as guidance, not a mandatory gate. Approved Freeze Exception Log (historical record) left untouched.
* **`docs/STABLE_CORE_V1_FREEZE.md`**, **`docs/STABLE_CORE_V1_REGRESSION_CHECKLIST.md`** — fixed dangling references to the nonexistent `STABLE_CORE_V1.lock` file.
* **`README.md`, `INSTALLATION.md`, `CITATION.cff`** — version strings bumped to v1.2.110.

### Verified (no changes needed)
* Full repo-wide sweep for any other "still frozen"/"Status: ACTIVE" claims — none found.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.110`. 295 tests passing (unchanged baseline).

## [v1.2.109] - 2026-07-09 (build 329)

> **User asked to update all markdown docs, audit English/Japanese UI text and metadata output structure, check newly recorded videos, and fix all errors found.**
>
> **Found and fixed a real, confirmed scientific-accuracy bug via two fresh real recordings** (`test1_20260709_222235`, 4 cameras; `test2_20260709_222819`, 4 cameras, 1080p). Independently re-verified frame counts for all 8 videos against `ffprobe -count_frames` and full-file MD5 decode (`ffmpeg -f framemd5 -fps_mode passthrough`) — 7 of 8 matched the app's own `framesWritten` exactly, but **`test1_20260709_222235/cam2` did not**: the real file contains 9377 frames (confirmed three independent ways — container `nb_frames`, true decode count, and MD5 frame hash count), while the app's own metadata claimed only 9278 — a 99-frame under-count. The app's own post-finalize ffprobe recount (`frameIntegrity.postFinalizeProbedFrameCount`/`postFinalizeFrameCountMismatch`, added specifically to catch exactly this class of divergence) had *already detected and recorded* this exact mismatch in the raw JSON — but nothing downstream ever read it: `integrityVerdict` was computed purely from an internal CSV-vs-counter comparison (which agreed with itself and said `PASS_WITHIN_TOLERANCE`), and `verification.notes` never mentioned it, so the discrepancy was silently buried in a field no verdict or UI text ever surfaces.
>
> Fixed in `MainWindow.xaml.cs`: `frameIntegrity.integrityVerdict` now reports `WARN_POST_FINALIZE_MISMATCH` when the post-finalize recount disagrees with the encoder's own submission counter, taking priority over the CSV-based check (an independent recount of the real file is stronger evidence than two of the app's own counters agreeing with each other). The per-camera `verification.notes` now also includes a descriptive bilingual note with the actual vs. expected frame counts and the diff, escalating the camera to `PASS_WITH_WARNING` instead of silently passing. Also softened a comment on `recordingFrames` that overclaimed the real MP4 "always" matches the app's internal counter — true in the overwhelming common case, but this session proved it isn't a hard guarantee, which is the entire reason the post-finalize recount exists.
>
> This fix is forward-looking only (metadata files are treated as immutable historical records, per established practice this session) — the existing `test1_20260709_222235/cam2_metadata.json` on disk still shows the old verdict, but the raw `postFinalizeFrameCountMismatch: true` field was always there for anyone who inspected it closely. Future recordings hitting this rare condition will now show the warning natively.
>
> **Audited English/Japanese localization for consistency** (not just key presence, which was already covered in v1.2.107): compared every key's placeholder tokens (`{0}`, `{1}`, ...) between `en.json`/`ja.json` and flagged one apparent mismatch (`verifyActiveSlotsTemplate` — en has `{0} slot{1}`, ja has only `{0}`), which on inspection is correct Japanese grammar (no plural suffix needed), not a bug. Reviewed all 43 keys where the English and Japanese values are byte-identical — all are legitimate untranslated technical/data terms (CSV export column headers, proper nouns, units like "FPS"/"OS"/"CPU", audit-log prefixes already decided to stay English per the v1.2.92 owner override) rather than missed translations. No encoding/mojibake issues found.
>
> **Updated all markdown docs found to be stale**: `README.md` and `INSTALLATION.md`'s "Current release"/citation lines and `CITATION.cff`'s `version`/`date-released` were still frozen at v1.2.84 (2026-07-06) despite ~25 releases since; all now read v1.2.109. Added missing documentation to `docs/OUTPUT_FILES_AND_METADATA.md` for the `ffprobeAudit`, `frameQuality`, `motionBlurRisk`, and `visualQuality` V2 metadata sections — these appear in every real recording's metadata.json but were never described in the schema reference, including the important caveat that blur/exposure/duplicate-frame scoring is not implemented yet (`visualQuality.implemented: false`) and that `ffprobeAudit` is always empty by design (removed in v1.2.22-alpha). Swept the remaining ~20 markdown files for references to files deleted in the v1.2.107 cleanup pass and other stale content — none found; `docs/windows_camera_behavior_study.md`'s one remaining "V3B" mention is an already-correctly-labeled historical/superseded reference, not a live claim.

### Fixed
* **`MainWindow.xaml.cs`** — `frameIntegrity.integrityVerdict` and per-camera `verification.notes` now surface `PostFinalizeFrameCountMismatch` (an independent post-finalize ffprobe recount catching real-file/counter divergence) instead of silently ignoring it; a confirmed mismatch now correctly escalates the camera to `PASS_WITH_WARNING`.
* **`README.md`, `INSTALLATION.md`, `CITATION.cff`** — citation/current-release version strings updated from stale v1.2.84 to v1.2.109.

### Docs
* **`docs/OUTPUT_FILES_AND_METADATA.md`** — documented the previously-unlisted `ffprobeAudit`, `frameQuality`, `motionBlurRisk`, `visualQuality` V2 metadata sections and their current not-implemented/always-empty status.

### Verified (no changes needed)
* Frame counts, duplicate frames (0 found), color tagging, and timestamp/internal-clock consistency independently re-checked against 8 real videos from two new sessions — all matched except the one mismatch fixed above.
* English/Japanese localization: placeholder consistency and untranslated-value review across all 795 keys — no real bugs found.
* ~20 other markdown files swept for staleness — clean.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.109`. 295 tests passing (unchanged baseline).

## [v1.2.108] - 2026-07-09 (build 328)

> **User asked for a full audit of two newly recorded real sessions** (`test1_20260709_213916`, 4 cameras; `test2_20260709_214520`, 3 cameras) — metadata accuracy, duplicate/ghost frames, blur, color accuracy, frame counts, internal clock/timestamps — plus a check of the Video Verification page's own components/text, and specifically asked to check the Verification Log for duplicate/redundant text visible in a pasted log excerpt.
>
> **Verification Log "duplication" is not a bug.** Traced `AppendLog`/`RunScanAsync`/`RunVerifyAsync`/`VerifyAllBtn_Click`: the log is an intentionally cumulative running history (never cleared, not even by "Clear results") and there is no double-invocation path (`AutoVerifyAfterScan` defaults `false`, confirmed in `appsettings.json`; `VerifyAllBtn_Click` calls `RunVerifyAsync()` exactly once). The near-identical repeated blocks in the pasted log are simply Scan → Verify → Verify All each appending their own full pass to the same view, by design.
>
> **Found and fixed a real bug: the "Hardware Calibration Lock" summary card always showed `— (legacy pipeline)`, even for 100%-V2 sessions with real environmental-lock data available.** `ReconcileV2Summary` (the method that already corrects a dozen other summary cards for V2 sessions after the raw schema-blind legacy scan) never touched this one — it's set unconditionally in `ApplySummary` and nothing downstream ever revisited it, unlike the identical (and already-correct) computation `ApplyV2StandaloneResults` does for the no-ffprobe fallback path. Fixed by computing real lock status from `_v2MetadataByPath` the same way, inside `ReconcileV2Summary`. Verified against both real sessions: all 7 cameras report `environmentalLock.activeAtRecordingStart: false`, so the card now correctly reads "Not active" instead of the misleading legacy placeholder.
>
> **Independently re-verified the app's own claims against the raw video files, external to the app:**
> - **Frame counts**: `ffprobe -count_frames` (true decode-based count) matches the container `nb_frames` tag and the app's own `framesWritten` exactly for all 7 videos (10423/10426/10536/10426 and 5755/5754/5819).
> - **Duplicate/ghost frames**: exact MD5 frame hashing (`ffmpeg -f framemd5`) across all 7 full videos found **zero exact consecutive duplicate frames**, confirming the app's "Duplicate Frames: 0" claim independently. Caught and corrected a methodology pitfall in the process — plain `ffmpeg -f framemd5` silently applies default VFR frame-drop reconciliation and under-counted frames by ~1-2% versus the true decode; re-ran with `-fps_mode passthrough` to get frame-for-frame parity before trusting the duplicate analysis.
> - **Color accuracy**: `ffprobe` reports `color_range=pc`, `color_space/transfer/primaries=bt709` on all 7 files, matching the app's claimed "Full (0-255/pc)" BT.709 tagging exactly.
> - **Internal clock / timestamps**: recomputed min/max/mean frame interval directly from each `camN_timestamps.csv` and it matches the app's own `timing.*IntervalMs` fields to 3 decimal places; `frameIndex` is monotonically increasing with zero out-of-order rows and zero `droppedFrameWarning` flags across all 7 cameras.
> - **Blur**: the app does not implement blur detection at all (`visualQuality.implemented: false` in every camera's own metadata). Ran an external objective check (ffmpeg `blurdetect` filter, sampled start/middle/end of each video) as a first look — no established defect, but cam3 in both sessions (an OBSBOT Meet SE, vs. the j5 Webcam JVU250 used for the other slots) scored consistently higher (softer) than the other cameras in both independent recordings. Reported as a relative observation only; there's no in-app baseline to call it a real defect against.
> - **ffprobe container fields being null** in each camera's own `metadata.json` (`ffprobeAudit.available: false`) is expected, documented behavior (`MainWindow.xaml.cs`, "ffprobe... analysis removed in v1.2.22-alpha") — the automatic per-recording ffprobe audit was deliberately removed; the Video Verification page's on-demand ffprobe pass (used above) is the only place it runs now.

### Fixed
* **`ui/pages/VideoVerificationPage.xaml.cs`** (`ReconcileV2Summary`) — Hardware Calibration Lock summary card now shows the real per-camera `environmentalLock` status for V2 sessions instead of an unconditional "(legacy pipeline)" placeholder.

### Verified (no changes needed)
* Verification Log append behavior — cumulative by design, not a duplication bug.
* Frame counts, duplicate/ghost frames (0 found, full-file MD5 hash), color tagging, internal clock/timestamp CSV consistency — all independently re-verified against `test1_20260709_213916` and `test2_20260709_214520`'s real files and matched the app's own reported values exactly.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.108`. 295 tests passing (unchanged baseline).

## [v1.2.107] - 2026-07-09 (build 327)

> **User asked for a full cleanup pass**: check all app files/folders/scripts/components (including previously-frozen STABLE_CORE_V1 code), remove unnecessary files, run cleanup, remove unused localization keys and dead scripts, and fix any old/outdated code found in previously frozen files.
>
> **Dead code and stale comments:**
> - `capture/backend/V2SelectionHardeningModels.cs` — removed a stale doc comment claiming "V2 Stable and V3B use the same preset set"; the experimental V3/V3B backends were removed entirely in v1.2.22-alpha, so the reference was misleading residue from before that removal.
> - Deleted `installer/bump_version.py` and `scripts/maintenance/bump_version.py` — an automated version-bump tool that was never wired into the actual `build_release.bat` pipeline (confirmed via grep: zero invocations anywhere in the build scripts) and only wrote to the already-frozen `docs/changelogs/CHANGELOG.md` archive. Every real release since v1.2.30-alpha has used the manual edit process documented in `docs/developer_notes/versioning.md`, which is now updated to describe that process directly instead of a fictional automated one.
> - Deleted `scripts/diagnostics/run_verification_cli.cs` — an orphaned `dotnet-script` prototype with zero references anywhere in the repo, superseded by the actively-used, doc-referenced `scripts/diagnostics/VerifyFolderCli/` project (a real buildable CLI, see `STABLE_CORE_V1_FREEZE.md`'s "CLI verification" section).
> - Deleted `scripts/build/sign_release.ps1` — a 7-line forwarding shim to `scripts/packaging/sign_release.ps1`, explicitly commented "kept for older build scripts," but the real pipeline (`scripts/build/build_release.ps1`) calls the packaging copy directly and nothing calls the shim anymore.
>
> **Found and fixed a real bug in the workspace cleanup script itself:** `scripts/clean_workspace.ps1` tried to delete installer output by matching literal filenames (`Setup.exe`, `MultiCamApp_Setup.exe`), but the actual build pipeline has output versioned names (`MultiCamApp_{version}_Setup.exe`) since versioning was introduced — meaning this script has never actually deleted a real installer binary. Fixed to match `*_Setup.exe` via wildcard.
>
> **Unused localization keys:** cross-referenced all 863 keys in `en.json`/`ja.json` against every localization access pattern in the C# source (bracket-indexer and `T()`/`Tf()` helper calls) and confirmed no dynamically-constructed key names exist anywhere (all lookups use literal strings). Found and removed **68 keys with zero usage** — `experiment*` (16), `locomotor*` (7), `verifyBehavior*` (13), `verifyLocomotor*` (4), assorted `verify*`/`metaField*` (7), and 21 misc keys (`about`, `error`, `dropped`, `disconnected`, `layout`, `commercialUseTitle`, `copyrightTitle`, etc.) — down to 795 keys in each file. Spot-verified the riskier short/generic-looking keys (`about`, `error`, `layout`, etc.) directly before deleting, since a false positive here would silently reintroduce the exact "raw key name shown in UI" bug this session has fixed elsewhere. Validated both JSON files parse correctly and key counts match post-removal.
>
> **Disposable build artifacts purged** (none tracked by version control; all regenerable by rebuilding): `.build/` (30 GB of per-version historical `dist-1.2.x` folders going back to `dist-1.2.31-alpha`), 11 superseded versioned installer `Setup.exe` files in `installer/` (kept only the current v1.2.106 one), `scripts/diagnostics/VerifyFolderCli/bin`+`obj` (620 MB), a stray `__pycache__` folder, and 5 stale dev log files in `data/logs/` from May/June 2026. Confirmed with the user before deleting given the size and lack of a git safety net in this working copy.
>
> Refreshed `docs/STABLE_CORE_V1_FREEZE.md`'s "Current app release" line (was still showing v1.2.104).

### Fixed
* **`capture/backend/V2SelectionHardeningModels.cs`** — removed stale comment referencing the long-removed V3B backend.
* **`scripts/clean_workspace.ps1`** — installer cleanup now matches the real versioned `*_Setup.exe` filename pattern instead of literal names that never matched any real build output.
* **`docs/developer_notes/versioning.md`** — replaced the fictional `bump_version.py`-based workflow with the actual manual version-bump process, and noted the tool's removal.

### Removed
* `installer/bump_version.py`, `scripts/maintenance/bump_version.py` — dead code, never invoked by the build pipeline.
* `scripts/diagnostics/run_verification_cli.cs` — orphaned prototype superseded by `scripts/diagnostics/VerifyFolderCli/`.
* `scripts/build/sign_release.ps1` — orphaned forwarding shim, unused by the real pipeline.
* 68 unused keys from `source/MultiCamApp/MultiCamApp/localization/en.json` and `ja.json` (863 → 795 keys each).
* `.build/` (30 GB of historical dist folders), 11 superseded installer `Setup.exe` files, `VerifyFolderCli/bin`+`obj`, a stray `__pycache__`, and 5 stale dev log files — disposable, regenerable build artifacts, not source.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.107`. 295 tests passing (unchanged baseline).
* Rebuilt `scripts/diagnostics/VerifyFolderCli/VerifyFolderCli.csproj` after deleting its `bin`/`obj` to confirm it still compiles cleanly.
* Validated both `en.json`/`ja.json` parse as valid JSON with matching key counts after key removal.

## [v1.2.106] - 2026-07-09 (build 326)

> **User asked for a comprehensive audit of all STABLE_CORE_V1-related code, scripts, files, language, metadata, numbers, UI/frontend/backend, with an explicit follow-up check on the Video Verification page and Hardware Diagnostics page** — removing genuinely unused/outdated code and fixing errors found.
>
> **Found and fixed the same "language switch doesn't refresh already-computed content" bug class again, this time in `HardwareDiagnosticsPage.xaml.cs`.** `ApplyLanguage` only re-localized the placeholder text shown before a scan ran; if a scan had already completed, switching the UI language left the System/Camera/USB result cards and their summary `TextBox`es frozen in whichever language was active at the moment Run Scan was clicked. Fixed by caching the raw scan-result objects (`SystemProfile`, `CameraCapabilityReport`, `UsbTopologyReport`, plus layout count and sanitized file-name paths) as instance fields when a scan completes, and having `ApplyLanguage` rebuild every card and summary box from that cache on a language switch instead of only handling the not-yet-scanned placeholder case.
>
> **Ran full localization key audits** on `VideoVerificationPage.xaml.cs` (282 bracket-indexer keys) and the verification backend (`verification/*.cs`, `ScientificTimingAssessor.cs`; 117 `T()`/`Tf()` helper keys) plus all 63 `hwDiag*` keys used in `HardwareDiagnosticsPage.xaml.cs` — every key exists in both `en.json` and `ja.json`, zero missing. Checked `AboutPage.xaml.cs`/`AboutWindow.xaml.cs` (static content, bug pattern doesn't apply) and `MainWindow.xaml.cs` (no own `ApplyLanguage`, only delegates to sub-pages).
>
> **Confirmed the legacy OpenCV recording engine is still dormant** (`RecordingSession` never instantiated from `MainWindow.xaml.cs`; the fallback-tier policy only drops to it when Media Foundation is entirely unavailable). Found 5 files (`metadata/FrameTimestampTrimmingHelper.cs`, `metadata/RecordingTimingMetrics.cs`, `metadata/SessionSummaryWriter.cs`, `experiment/FrameTimingMonitor.cs`, `metadata/MetadataWriter.cs`) referenced only by that dormant engine, but recognized that deleting them would mean gutting ~9 more legacy-engine files and effectively removing the app's entire fallback recording tier for machines without Media Foundation — a product decision, not a cleanup. Asked the user explicitly; **user chose to keep the legacy engine as a dormant safety net**, so no deletion occurred.

### Fixed
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — `ApplyLanguage` now caches the last scan's `SystemProfile`/`CameraCapabilityReport`/`UsbTopologyReport` (plus layout count and file-name paths) and rebuilds the result cards and summary boxes from that cache on a language switch, instead of leaving already-scanned results frozen in the previous UI language.

### Verified (no changes needed)
* `verification/VerificationCaptureProfile.cs`, `verification/ExpectedSettingsResolver.cs`, `verification/VideoScanner.cs`, `verification/RecordingSessionDiscovery.cs` — clean.
* Localization completeness: `VideoVerificationPage.xaml.cs` (282 keys), verification backend + `ScientificTimingAssessor.cs` (117 keys), `HardwareDiagnosticsPage.xaml.cs` (63 `hwDiag*` keys) — zero missing keys in `en.json`/`ja.json`.
* `AboutPage.xaml.cs`, `AboutWindow.xaml.cs`, `MainWindow.xaml.cs` — no instance of the language-refresh bug pattern.

### Process
* Confirmed with the user before touching the 5 legacy-engine-only files identified during dead-code analysis; decision was to keep them (dormant safety net), not delete.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.106`. 295 tests passing (unchanged baseline).

## [v1.2.105] - 2026-07-09 (build 325)

> **User asked to lift the remaining STABLE_CORE_V1 freeze scope, simplify things to "just use the current V2," update the related verification components/metadata output, and refresh the documentation.** Following up on v1.2.104's freeze-lift, audited the remaining actively-exercised verification files (`VerificationCaptureProfile.cs`, `ExpectedSettingsResolver.cs`, `VideoScanner.cs`, `RecordingSessionDiscovery.cs`) — all clean, no bugs found (they already benefit from v1.2.104's schema fix since they work generically off `CameraMetadataRecord`). Confirmed the legacy OpenCV recording engine (`capture/`, `recording/`, `metadata/MetadataWriter.cs`) is dormant on this machine — `RecordingSession` is never instantiated from `MainWindow.xaml.cs`, and the backend-tier fallback policy only drops to it when Media Foundation is entirely unavailable, which contradicts every real recording audited this week. Left that subtree untouched: with no way to exercise it, a "fix" there would be unverifiable by construction.
>
> **Found and closed a genuine simplification: `VideoScanner.cs` only ever recognized the unprefixed `metadata.json`/`metadata.txt` names**, which is the entire reason `MainWindow.xaml.cs` has been writing a byte-identical duplicate of every V2 metadata file (`cam1_metadata.json` *and* `metadata.json`, `cam1_metadata.txt` *and* `metadata.txt`) since VideoEngineV2 shipped — doubling small-file I/O on every recording for a compatibility need that no longer has to exist now that the freeze is lifted. Added `RecordingSessionDiscovery.FindCameraMetadataFile(folder, slot, extension)` — prefers the slot-prefixed name, falls back to the unprefixed name for older recordings or the legacy engine — and wired it into `VideoScanner.cs` (3 call sites) and `SessionComparisonService.cs` (4 call sites, replacing independently-hardcoded `"metadata.json"`/`"metadata.txt"` existence checks). Verified against a scratchpad copy of a real session with the unprefixed duplicates deleted: Scan, Verify, and the missing-metadata warnings all work correctly off the prefixed files alone. With the scanner no longer requiring the duplicate, removed the actual duplicate write in `MainWindow.xaml.cs` (`WriteV2SlotMetadataAsync`) — new recordings now write one metadata file pair per camera instead of two. Also fixed a self-inflicted bug from that removal: a post-write file-existence check still expected the now-unwritten canonical paths and would have flagged every future recording as "missing required files" had it shipped uncorrected — caught before it ever ran against a real recording.
>
> Updated `docs/OUTPUT_FILES_AND_METADATA.md`, `docs/architecture/overview.md`, and `docs/user_guide/video_verification.md` to describe the new one-file-per-camera layout, the native (not corrected-after-the-fact) V2 schema support, and the fact that the old on-screen correction logic is now an inert safety net rather than load-bearing.

### Fixed
* **`verification/RecordingSessionDiscovery.cs`** — added `FindCameraMetadataFile`, resolving a camera's metadata file by preferring the slot-prefixed name over the unprefixed compatibility duplicate.
* **`verification/VideoScanner.cs`** — `CreateEntry`, `CreateMissingEntry`, and `ScanLooseFiles` now resolve metadata paths via `FindCameraMetadataFile` instead of only recognizing the unprefixed name.
* **`verification/SessionComparisonService.cs`** — the four places checking for `metadata.json`/`metadata.txt` existence now use `FindCameraMetadataFile` instead of independently hardcoding the unprefixed name.
* **`MainWindow.xaml.cs`** (`WriteV2SlotMetadataAsync`) — removed the now-unnecessary duplicate write of the unprefixed `metadata.json`/`metadata.txt`; new recordings write only the slot-prefixed pair. Also fixed the post-write required-files check, which still referenced the removed canonical paths and would have false-flagged every recording as incomplete.

### Docs
* Updated `docs/OUTPUT_FILES_AND_METADATA.md`, `docs/architecture/overview.md`, `docs/user_guide/video_verification.md` to reflect the one-file-per-camera layout and the v1.2.104 native-schema-support architecture.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.105`. 295 tests passing (unchanged baseline).
* Wrote and ran (then removed) a temporary test against a scratchpad copy of a real session with the unprefixed metadata duplicates deleted, confirming Scan/Verify work correctly off the slot-prefixed files alone; a second temporary test re-confirmed all 4 real sessions from this week still produce identical, correct results after both the scanner and duplicate-write changes.

## [v1.2.104] - 2026-07-09 (build 324)

> **User asked to lift the STABLE_CORE_V1 freeze on the Video Verification pipeline and fix the root cause**, rather than continuing the pattern from v1.2.93 through v1.2.103: find a place where a V2 recording shows a stale/wrong FAIL, patch it with a UI-layer correction, ship, repeat. Traced the actual root cause to `MetadataParser.ParseCameraMetadataJson` (STABLE_CORE_V1) — it reads a flat, top-level PascalCase JSON schema (e.g. root-level `FramesWritten`), but VideoEngineV2 writes a deeply nested, camelCase schema (e.g. `timing.framesWritten`). Reading a V2 file with this parser finds none of its expected properties and silently returns a record with every numeric field at zero — the single root cause behind essentially every "V2 recording shows FAIL/dash everywhere" bug fixed piecemeal this week.
>
> **This is a legitimate STABLE_CORE_V1 freeze exception under category 4 (scientific accuracy — "Video Verification gives incorrect PASS/PASS_WITH_WARNING/FAIL")**, not a stretch. Fixed at the source: `MetadataParser.LoadCameraMetadata` now detects a V2-schema JSON (reusing the already-existing, unprotected `V2MetadataReader`) and maps its real values into `CameraMetadataRecord` via a new `BuildRecordFromV2Metadata` method — additive only, the legacy JSON/TXT parsing paths are untouched.
>
> Three separate downstream checks in `VideoVerificationService.VerifyOneAsync` turned out to assume legacy-only exact-match semantics incompatible with V2's frame-counting model — most importantly, `FrameTimestampCsvRowCount != FrameCount` is treated as a hard FAIL, but V2 legitimately and expectedly has a small CSV-row/frames-written difference by design (the same difference V2's own `frameIntegrity.csvRowsDiff`/`integrityVerdict` already tolerates). Forcing real V2 numbers through these checks reliably produced false FAILs even with the schema fixed. Rather than loosen the legacy thresholds (risking the validated legacy/non-V2 baseline), a new `CameraMetadataRecord.IsV2Source` flag makes these V2 recordings trust their own already-computed, already-correct verdict (written into every camera's own metadata.json at recording-stop time, using the exact same PASS/PASS_WITH_WARNING/FAIL vocabulary) instead of re-deriving one from a schema shape it was never designed for. The legacy `else` branch is byte-for-byte unchanged.
>
> **Verified against 4 real, differently-shaped sessions recorded this week** (2/3/4-camera, 720p/1080p, one with genuine mid-session timing gaps), calling the raw `VideoVerificationService.VerifyAsync` pipeline directly with **none** of the UI-layer Enrich/Reconcile correction methods from v1.2.98–v1.2.101 in the loop: every camera now natively reports the correct verdict, every session natively reports the expected status (`PASS` for the clean 2-camera session, `PASS_WITH_WARNING` for the other three), and the exported audit report is correct without any later correction. The existing UI-layer correction methods (`EnrichV2SessionGroups`, `ReconcileV2SessionVerdict`, `ReconcileV2Summary`, etc.) are left in place, unmodified, as an inert safety net — their `if (status == Fail)` guards simply never trigger anymore since the status is never wrongly FAIL in the first place.
>
> Also mapped two additional V2 fields (`timing.firstFrameTimestampMs`/`lastFrameTimestampMs`) that turned out to correspond to legacy "critical" scientific-timing fields (`FirstFrameCaptureMonotonicSec`/`LastFrameCaptureMonotonicSec`) — without them, exported reports still said "Re-record before scientific use" despite the correct PASS_WITH_WARNING verdict, a real (if less severe) leftover inconsistency this fix surfaced and closed.
>
> Full regression checklist run to the extent practical without physical camera hardware in this session: build/test pass, offline verification re-run against real recordings spanning camera counts and resolutions the checklist's own dual-camera baseline doesn't otherwise cover. Logged as Approved Freeze Exception #9 in `STABLE_CORE_V1_EXCEPTIONS.md`.

### Fixed
* **`verification/MetadataParser.cs`** — `LoadCameraMetadata` now detects VideoEngineV2's JSON schema and maps it via new `BuildRecordFromV2Metadata`, instead of falling through to the legacy flat-schema parser that returns all zero/default values for V2 files. Added `CameraMetadataRecord.IsV2Source` marker.
* **`verification/VideoVerificationService.cs`** — `VerifyOneAsync` now trusts a V2-sourced record's own pre-computed `ScientificTimingStatus` instead of re-deriving one via `AssessScientificTimingStatus` and two additional legacy-specific completeness/CSV-equality checks that don't apply to V2's frame-counting model. Legacy (non-V2) behavior is completely unchanged.
* **`verification/V2MetadataReader.cs`** — added `FirstFrameTimestampMs`/`LastFrameTimestampMs` (reading `timing.firstFrameTimestampMs`/`lastFrameTimestampMs`), enabling `FirstFrameCaptureMonotonicSec`/`LastFrameCaptureMonotonicSec` mapping and fixing an overly-alarming "Re-record before scientific use" recommendation that persisted even for correctly-PASS_WITH_WARNING V2 recordings.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.104`. 295 tests passing (unchanged baseline — confirms zero regression to the legacy/non-V2 path).
* Wrote and ran (then removed) a temporary xUnit `[Theory]` test exercising the raw legacy pipeline (no UI-layer correction) against 4 real sessions recorded this week, confirming native-correct verdicts throughout; a second temporary test confirmed the exported audit report text is now correct natively.

### Process
* Logged as **Approved Freeze Exception #9** (category 4 — scientific accuracy) in `docs/STABLE_CORE_V1_EXCEPTIONS.md`, per the user's explicit request to follow the regression-checklist process properly rather than skip it.
* Updated `docs/STABLE_CORE_V1_FREEZE.md`'s current-release line and approved-exceptions list.

## [v1.2.103] - 2026-07-09 (build 323)

> **User asked (for the second time) why "FAIL: camN: individual camera audit failed" text appears in the Verification Log**, and this time asked to trace the exact origin of every line and fix it properly rather than just explain it again.
>
> **Traced every line to its source.** `VideoVerificationService.VerifyAsync` (STABLE_CORE_V1) uses a live streaming `Log()` callback — each line appears in the UI Verification Log the instant it's computed, not as a final summary. For any V2 session, `SessionComparisonService.BuildComparisonSummary` computes `Session status: FAIL`, `Scientific Timing Confidence: FAILED`, and a `FAIL: camN: individual camera audit failed` line per camera — using the same schema-blind legacy metadata parser responsible for every other "recomputed from scratch, doesn't understand V2's JSON" issue fixed this cycle. `VideoVerificationService.cs:137-141` then logs `audit.ComparisonSummaryText` line-by-line verbatim. Only *after* this entire scan finishes and returns does the UI layer's `ReconcileV2SessionVerdict` run and log the correction (`[V2] Corrected false-positive FAIL for session "..." -> WARNING`). Both halves are individually true at the moment they're written, but reading the log top-to-bottom hits a wall of FAIL text long before reaching the correction at the end.
>
> **Fixed by adding a disclaimer before the raw scan starts**, in the same spot the page already announces "Found N V2 session(s)" (which happens *before* `VerifyAsync` runs, since V2 detection is a separate, earlier step). Explains plainly that the legacy scan below is expected to show FAIL for these sessions, and that the corrected result appears at the end. No STABLE_CORE_V1 code was touched — the raw audit trail is preserved exactly as before, just with context added ahead of it instead of only after.

### Fixed
* **`VideoVerificationPage.xaml.cs` (`RunVerifyAsync`)** — added a disclaimer log line (new `verifyV2PreScanDisclaimer` key) immediately after V2 sessions are detected, before the legacy scan begins, explaining that upcoming FAIL text for those sessions is expected and gets corrected further down the log.
* **`localization/en.json`, `localization/ja.json`** — added `verifyV2PreScanDisclaimer`.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.103`. 295 tests passing (unchanged).
* Verified `en.json`/`ja.json` have no duplicate keys and matching key counts (863 each) after the edit.

## [v1.2.102] - 2026-07-09 (build 322)

> **User shared Video Verification page screenshots for a real v1.2.101 session** — the top-level cards all looked right (WARNING verdict, correct frame counts, 0 writer drops) except one: the "Frame Integrity" card read "Review details" — a vague fallback — even though every camera confirmed Original Capture Mode with 0 duplicates/placeholders, and the session's own detail text right below it already said "Frame integrity: Real frames only; no duplicates/placeholders" in plain English.
>
> **Same root cause as five earlier fixes this cycle**: `session.OriginalFramesOnly` (`ApplyOriginalCaptureSessionCardFields`, STABLE_CORE_V1) is computed from the legacy `CameraMetadataRecord`'s `FrameCount`/`DuplicatedFrames`/`PlaceholderFrames` fields — which the schema-blind legacy parser never populates for a V2 recording, so this check silently evaluates to "not confirmed" regardless of the real V2 data. `ReconcileV2Summary` already corrects several other cards the same schema-blind computation gets wrong (Recording Mode, Timestamp CSV Status, Scientific Timing Confidence) but had never touched this one.
>
> Fixed the same way as the others: when every camera's own V2 metadata confirms Original Capture Mode — which is V2's architectural guarantee of real frames only, not merely an unmeasured absence of evidence — correct the card to "Real frames only" and the underlying `report.SessionResult.OriginalFramesOnly` data field, so Export TXT agrees with what the screen now shows too.

### Fixed
* **`VideoVerificationPage.xaml.cs` (`ReconcileV2Summary`)** — now also corrects the "Frame Integrity" card (`CardOriginalFramesValue`) and `report.SessionResult.OriginalFramesOnly` when every camera confirms Original Capture Mode, instead of leaving the vague "Review details" fallback from the schema-blind legacy computation.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.102`. 295 tests passing (unchanged).

## [v1.2.101] - 2026-07-09 (build 321)

> **User asked for a full detail audit of a freshly recorded 4-camera session.** Cross-checking the recording metadata against independent ffprobe ground truth — the same methodology used for every prior session audit this cycle — turned up a genuine, previously-invisible data integrity issue: **cam1's final MP4 contained 13,155 actual frames, but the app's own metadata claimed 13,596** (`Frames submitted since recording start`), a 441-frame (3.24%) gap. Confirmed with `ffprobe -count_frames` (an actual decode pass, not just container metadata) — this was real, not a measurement artifact. cam2, cam3, and cam4 in the same session all matched their claimed frame counts exactly, as did every camera in every session audited earlier this cycle — this looked like an isolated, camera-specific event for this one take, not a systemic regression.
>
> **Root cause: `IMFSinkWriter.WriteSample` returning success does not guarantee a frame survives into the final file.** The Vortice.MediaFoundation binding only throws when the native call returns a failure HRESULT; a return of S_OK only confirms Media Foundation accepted the sample into its internal processing queue, not that the downstream H.264 encoder actually encoded and muxed it. Confirmed via the runtime logs: zero `V2_ENC_WRITE_ERROR` lines anywhere for this session — `SubmitFrame`'s exception handler never triggered, yet 441 frames are missing from the decoded output. The encoder's own log line (`V2_ENC_FINAL frames=13596`) is just a count of "submitted without exception," not a guarantee of what actually made it into the container. No exception-handling change in `SubmitFrame` could have caught this — there was genuinely nothing to catch.
>
> **Fixed by adding an independent post-finalization frame-count check.** Immediately after a camera's temp file is renamed to its final path (`VideoEngineV2.StopSlotRecordingAsync`), the app now runs a quick ffprobe recount (reusing the existing bundled `VideoProbeService`) and compares it against the encoder's own submitted-frame count. A mismatch is logged (`V2_POST_FINALIZE_FRAME_MISMATCH`) and surfaced directly in `metadata.txt`/`metadata.json` as a new "Post-recording frame verification" field — so this class of silent loss is now visible immediately after recording stops, instead of requiring someone to manually run ffprobe (as this investigation did) to ever discover it happened.

### Fixed
* **`VideoEngineV2.cs` (`StopSlotRecordingAsync`)** — added `PostFinalizeFrameCheckAsync`, an independent ffprobe recount of the just-finalized MP4, run once per camera immediately after the temp→final rename succeeds. Best-effort: any failure (ffprobe unavailable, probe error) leaves the result absent rather than blocking or failing the recording-stop flow.
* **`RecordingFinalizeResult.cs`** — added `PostFinalizeProbedFrameCount`/`PostFinalizeFrameCountMismatch` fields.
* **`MainWindow.xaml.cs`** — surfaces the check result as a new "Post-recording frame verification" line in `metadata.txt` and `postFinalizeProbedFrameCount`/`postFinalizeFrameCountMismatch` fields in `metadata.json`'s `frameIntegrity` section.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.101`. 295 tests passing (unchanged).
* Wrote and ran (then removed) a temporary test probing the real cam1.mp4/cam2.mp4 from the affected session directly, confirming the detection logic correctly flags cam1's genuine 441-frame mismatch while producing no false positive on cam2's exact match.

## [v1.2.100] - 2026-07-09 (build 320)

> **User ran Verify All on a fresh v1.2.99 recording and asked why "individual camera audit failed" / "Result: FAIL" text was still visible**, even though the session header correctly showed a corrected WARNING verdict and the table rows all showed WARNING. Investigating turned up two more instances of the same "different object graph, only one got corrected" bug pattern from v1.2.98/99, plus one clear self-inflicted mistake:
>
> **1. `vm.Row.DetailText`** (the big multi-section report shown in "Selected Video Detail" when a row is clicked) is baked into a string once at initial scan time — its embedded "Result:", "Failures:", "Reason:", and "Recommendation:" lines still read the stale pre-correction verdict verbatim, contradicting the table row directly above it. Fixed by prepending the same kind of corrective note already used for the session-level `ComparisonSummaryText`, so anyone reading the raw detail immediately sees why it still says FAIL further down (new `verifyV2RowCorrectedNote` key).
>
> **2. `video.Verdict`** was never corrected by `EnrichLegacyMetadataRecord`, only `video.ScientificTimingStatus`. Since `CameraAuditStatus.FromVideoResult` checks `video.Verdict == Fail` *first* — before ever looking at `ScientificTimingStatus` — any code that re-derives status by calling `FromVideoResult` fresh (specifically the auto-saved `video_audit_report.txt`'s per-camera `status:` line) kept showing FAIL even after every other fix, right next to a correctly-showing `scientific timing: WARNING`. Fixed by correcting `video.Verdict` too, using the same session-wide V2 audit result `ReconcileV2SessionVerdict` already uses for the table row, so every consumer of `FromVideoResult` agrees.
>
> **First attempt at fixing #2 introduced a new bug**: compared the normalized result (`NormalizeSessionResult` returns the short vocabulary `"PASS"`/`"WARNING"`/`"FAIL"`) against `CameraAuditStatus.PassWithWarning` (the long constant `"PASS_WITH_WARNING"`) — the exact "string vocabulary mismatch" class of bug already documented from a previous release. A test run caught it immediately (produced `status: PASS` instead of the correct `status: PASS_WITH_WARNING`); fixed by comparing against the literal `"WARNING"` string instead, matching the pattern the codebase's own `NormalizedResultToVerdict` helper already uses correctly.
>
> Also fixed during this investigation: a self-inflicted `ja.json` corruption. A PowerShell one-liner meant to add the new `verifyV2RowCorrectedNote` key failed (Windows PowerShell 5.1 doesn't support `ConvertFrom-Json -AsHashtable`) but still executed its next pipeline stage against a null value, truncating `ja.json` to 0 bytes. Recovered immediately from the intact `dist\localization\ja.json` copy staged by the v1.2.99 build, then added the key correctly via direct text edit (the file uses full-width `（）`/`／` punctuation, not ASCII, which was the root cause of an earlier failed match attempt).

### Fixed
* **`VideoVerificationPage.xaml.cs` (`ReconcileV2SessionVerdict`)** — now also prepends a corrective note to `vm.Row.DetailText` when correcting a row's status, explaining why the embedded Result/Failures/Reason/Recommendation text below still shows the original legacy verdict.
* **`V2VerificationRunner.cs` (`EnrichLegacyMetadataRecord`)** — now also corrects `video.Verdict` (not just `video.ScientificTimingStatus`), using the same session-wide V2 audit result already used to correct the table row, so `CameraAuditStatus.FromVideoResult` — and every export that calls it — agrees with what the UI shows.
* **`localization/en.json`, `localization/ja.json`** — added `verifyV2RowCorrectedNote`.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.100`. 295 tests passing (unchanged).
* Wrote and ran (then removed) two temporary tests against the real `test1_20260708_235146` session: one confirming `DetailText` correction, one confirming the auto-saved audit report's per-camera `status:` line no longer contradicts `scientific timing:` — the second test caught the string-vocabulary-mismatch bug in my first fix attempt before it shipped.

## [v1.2.99] - 2026-07-08 (build 319)

> **User asked to verify that all recording metadata (timestamps.csv, metadata.txt, metadata.json) hold true, consistent values suitable for downstream analysis.** Cross-checked every camera's metadata against independent ground truth: ran ffprobe directly against the actual `.mp4` files, counted real CSV rows on disk, and compared file sizes byte-for-byte. Frame counts matched ffprobe's `nb_frames` exactly on all 4 cameras; CSV row counts, frame-index continuity (0 gaps), file sizes, and session-level aggregates all checked out. **The recording-time metadata itself has no integrity issues.**
>
> **But the auto-saved `video_audit_report.txt` sitting in the session folder was stale and self-contradicting** — it showed `Overall verdict: Fail` with every camera `FAIL` and zeroed-out fields, while the same session's own camera metadata proved `PASS_WITH_WARNING` with complete, valid data. Root cause: `VideoVerificationService.VerifyAsync` (STABLE_CORE_V1) auto-saves this file to the session folder **before** the Video Verification page's V2 enrichment/reconciliation logic ever runs — that logic lives entirely in the UI layer and only executes after `VerifyAsync` returns. So this specific file was left stuck at its raw, schema-blind pre-correction state every time Verify All ran, even with v1.2.98's export fixes in place — unless the user separately clicked an Export button afterward.
>
> **Fixed by re-saving the file immediately after enrichment**, using the same corrected report the page just displayed, so `video_audit_report.txt` on disk always agrees with what Verify All shows — no separate Export click required.

### Fixed
* **`VideoVerificationPage.xaml.cs`** — added `ResaveAuditReportAfterEnrichment`, called right after `EnrichV2SessionGroups`/`EnrichV2VideoMetadata`/`ReconcileV2Summary` complete, re-writing `video_audit_report.txt` with the corrected report so it no longer lags behind the on-screen result.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.99`. 295 tests passing (unchanged).
* Wrote and ran (then removed) a temporary test confirming `VerifyAsync`'s raw auto-save shows `Overall verdict: Fail`, and that re-saving with the corrected report (replicating the new fix) changes it to `Overall verdict: Warning` — matching the real session's true state.
* Independently verified all 4 cameras' recording metadata (timestamps.csv, metadata.txt, metadata.json) against ffprobe run directly on the real `.mp4` files and against actual file sizes on disk — exact matches throughout, confirming the underlying recording metadata was never the problem.

## [v1.2.98] - 2026-07-08 (build 318)

> **User asked to re-check metadata output against the Video Verification page, on-screen and exported.** Writing a headless test that replicates a real Verify-All-then-Export click sequence (VerifyAsync → V2 enrichment → export) against a real 4-camera session — the exact same methodology used for v1.2.93's export-enrichment fix — turned up a chain of four related bugs, all the same shape: a V2 session's own audit already confirms `PASS_WITH_WARNING`, the on-screen cards/table correctly show the corrected status, but Export TXT/JSON/CSV still serialized the original schema-blind `FAIL` because the correction only ever touched a WPF widget's `Text` property or one specific field, never the underlying report data object(s) that the exporter reads directly.
>
> **Bug 1 — top-level summary never corrected.** `ReconcileV2Summary()` in `VideoVerificationPage.xaml.cs` recomputed `CardPassValue.Text`/`CardWarnValue.Text`/`CardFailValue.Text`/`CardOverallValue.Text` but never touched `report.Summary.VideosPassed/Warning/Failed`, `report.Summary.OverallVerdict`, or `report.SessionResult.OverallResult` — every export from a V2 session showed the wrong pass/warn/fail counts and `OverallVerdict: Fail` regardless of what the screen displayed.
>
> **Bug 2 — stale session message bullets.** `report.Summary.SessionMessages`/`report.SessionResult.SessionMessages` are populated once by `VideoVerificationService` at initial-scan time (STABLE_CORE_V1, before any V2 correction runs) with lines like `"test1_...: FAIL"` and failure bullets (`"individual camera audit failed"`, `"Inter-camera frame difference too large"`). `BuildSummaryText` already hides this stale block on-screen for a fully-V2 session, but the raw lists themselves were never corrected, so JSON/CSV export kept the contradicting FAIL text next to the now-correct `OverallVerdict`.
>
> **Bug 3 — per-row Details/Recommendation columns.** `ReconcileV2SessionVerdict()` corrected `vm.Row.AuditStatus` and cleared `vm.Row.ErrorMessages`, but `vm.Row.Details`/`vm.Row.Recommendation` (baked into strings once at initial `VerificationReportMapper.ToTableRow` mapping, from the same stale FAIL verdict) were left untouched — every CSV/JSON export row kept showing `Details: "Scientific timing status: FAIL"` and `Recommendation: "Review failure messages and re-record if the file is unusable."` next to a `Result` column correctly reading `WARNING`.
>
> **Bug 4 — scientific timing confidence, two more places.** The "Scientific Timing Confidence" section of `ReconcileV2Summary()` only corrected `CardTimingConfidenceValue.Text`, never `report.SessionResult.ScientificTimingStatus`/`SessionScientificTimingConfidence` (session-wide, exported directly) nor each `RecordingSessionAuditResult.SessionScientificTimingConfidence` in `report.SessionAudits` (per-session, also exported directly in JSON) — both kept exporting `FAIL`/`FAILED` even after the card confirmed the session's timing was fine.
>
> All four fixed the same way: correct the underlying report data object(s) at the same point the on-screen widget is corrected, using the same "only override a confirmed stale default" guard already established for `ReconcileV2Summary`'s existing corrections. Verified via the same real 4-camera session used throughout this investigation, asserting the JSON/CSV exports no longer contain any of the four stale-FAIL patterns in either language.

### Fixed
* **`VideoVerificationPage.xaml.cs` (`ReconcileV2Summary`)** — now also corrects `report.Summary.VideosPassed/Warning/Failed`, `report.Summary.OverallVerdict`, `report.SessionResult.OverallResult`, `report.Summary.SessionDurationMatch`, `report.Summary.SessionMessages`/`report.SessionResult.SessionMessages` (replaced with a corrected rollup line), and `report.SessionResult.ScientificTimingStatus`/`SessionScientificTimingConfidence` — previously only the on-screen summary cards were corrected.
* **`VideoVerificationPage.xaml.cs` (`ReconcileV2SessionVerdict`)** — now also corrects `vm.Row.Details`, `vm.Row.Recommendation`, `vm.Row.RecommendedAction`, and `audit.SessionScientificTimingConfidence` alongside the existing `AuditStatus`/`ErrorMessages` correction.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.98`. 295 tests passing (unchanged).
* Wrote and ran (then removed) a temporary test replicating a full Verify-All-then-Export sequence against a real 4-camera session, asserting the JSON/CSV exports contain no stale-FAIL text in any of the four locations above, in both English and Japanese — confirmed all four fixes hold.

## [v1.2.97] - 2026-07-08 (build 317)

> **User asked to verify Video Verification page results are language-pure** — English UI should show only English, Japanese UI only Japanese — and shared a Detailed View screenshot that failed this immediately: the "Camera Controls" section showed Japanese status text (`デバイス/ドライバー非対応`) mixed into an otherwise-English English-mode panel.
>
> **Root cause: recorded-language data displayed verbatim, regardless of current UI language.** `MainWindow.xaml.cs`'s `CtrlSupport`/`CtrlResult2` bake the camera-control status into the V2 metadata JSON already translated to whatever language was active *at recording time* (a closed set of ~7 phrase-pairs, e.g. "Not supported by device/driver" / "デバイス/ドライバー非対応"). The Video Verification page then displayed that baked-in string as-is — so a Japanese-mode recording viewed with the UI set to English still showed Japanese, and (for recordings made before v1.2.94's warning-message fix) an English-mode recording's warnings stayed English even when viewed in Japanese mode. Two different bugs, same symptom: the display layer trusted recorded-time language instead of matching current UI language.
>
> **Fixed by normalizing at display time, not just at record time.** Added `NormalizeControlStatus`/`NormalizeControlWarning` in `V2VerificationRunner.cs` — since the underlying phrase set is small and closed (verified by reading the exact `CtrlSupport`/`CtrlResult2` switch statements), both recognize either language's version of each known phrase and re-render it in the *current* UI language, regardless of what was recorded. Verified end-to-end against the user's real Japanese-recorded session, viewed in both English and Japanese modes, via a temporary test (removed after passing) — confirmed zero cross-language leakage in either direction.
>
> **Investigating the same panel further (same detail view, same screenshot) found two more unlocalized spots** in the exact section the user was looking at: the "Frame integrity" parenthetical note (`(timestamp CSV has N more row(s) than frames written)`) in `V2VerificationRunner.cs`, and the entire `V2RecordingVerifier.cs` issue-message generator (~13 distinct FAIL/WARN/INFO/OK messages feeding the "V2 File Check" section) — neither had ever been wired to the app's language system at all. Fixed both, preserving the literal `FAIL:`/`WARN:`/`INFO:`/`OK:` prefixes other code parses via `StartsWith`.
>
> Left alone (by design, matching the established status-code convention): `Status: Success` (raw `writerStatus` enum value) and `BT.709 primaries/transfer/matrix` (colorimetry standard terminology, not conventionally translated even in Japanese technical contexts).

### Fixed
* **`V2VerificationRunner.cs`** — added `NormalizeControlStatus`/`NormalizeControlWarning`, mapping the small closed set of camera-control status/warning phrases (in either language) to the current UI language before display, instead of showing whatever language the phrase happened to be recorded in.
* **`V2VerificationRunner.cs`** — the "Frame integrity" CSV-row-count-difference parenthetical is now localized.
* **`V2RecordingVerifier.cs`** — all ~13 FAIL/WARN/INFO/OK issue messages (temp-file/MP4/CSV/metadata checks) now localized; prefixes unchanged since other code depends on them for routing.
* **`localization/en.json`, `localization/ja.json`** — added ~20 new keys backing the above.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.97`. 295 tests passing (unchanged).
* Wrote and ran (then removed) a temporary test rendering the same real Japanese-recorded session's detail text in both English and Japanese modes via reflection-set `CurrentLanguage`, confirming no cross-language leakage in either direction — this is the same test methodology used for v1.2.93's export-enrichment fix.

## [v1.2.96] - 2026-07-08 (build 316)

> **User re-tested the freshly-installed v1.2.95 build and pasted the Verification Log** — everything visually checked out (Real Capture FPS, Recording Mode, Timestamp CSV Status all showing real values now, per v1.2.93-95's fixes), but the log contained a literal, untranslated key name: `sessionAuditSummaryTimingMode`, printed as-is with no value ever substituted.
>
> **Root cause: a missing localization key, not a code bug.** `SessionComparisonService`'s `T(language, key, fallback)` helper returns `language[key]` whenever `language != null` — and `LanguageManager.Get()` falls back to returning **the key name itself** when the key isn't found in the dictionary, silently ignoring the `fallback` parameter entirely. The fallback only ever gets used when `language == null` (e.g. `VerifyFolderCli`). So any `Tf(language, "someKey", "some fallback {0}", ...)` call where `"someKey"` was never actually added to `en.json`/`ja.json` will leak the raw key name into the UI/log the moment a `LanguageManager` is supplied — which is always the case in the real app.
>
> Ran a full audit rather than patching just the one visible key: extracted every `T(`/`Tf(`/`_language[...]` call across `verification/`, `ui/`, and `metadata/` (508 unique keys) and cross-checked all of them against both JSON files. Found **5 missing keys total** in `SessionComparisonService.cs` (only 1 of which had actually surfaced yet, in this exact 2-session scan) — the other 4 cover less common code paths (Original Capture duplicate-frame/interval-stability messages, a note-line formatter) that hadn't been hit by testing yet but had the same latent bug. All 5 are pure data additions to the JSON files — no `.cs` changes, so no STABLE_CORE_V1 exception needed.
>
> Since this release's fix is JSON-only, patched `dist\localization\en.json`/`ja.json` directly (the exact files the already-running v1.2.95 build reads at startup) rather than requiring a full rebuild — the fix is live immediately. Version bumped to keep `version.json`/`dist\config\version.json` honest about what's actually running, but the installer/Setup.exe was not rebuilt this round since no compiled code changed.

### Fixed
* **`localization/en.json`, `localization/ja.json`** — added 5 missing keys referenced by `SessionComparisonService.cs` (`sessionAuditSummaryTimingMode`, `sessionAuditSummaryNoteLine`, `sessionAuditOriginalCaptureDuplicates`, `sessionAuditOriginalCaptureIntervalUnstable`, `sessionAuditOriginalFrameDiffInfo`) that were silently leaking their raw key names into the Verification Log and session detail text instead of real translated text.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.96`. 295 tests passing (unchanged).
* Ran a comprehensive regex-based cross-check of all ~508 localization keys referenced across `verification/`, `ui/`, and `metadata/` against both JSON files to confirm no other keys have this same gap (0 missing after the fix, versus 5 before).

## [v1.2.95] - 2026-07-08 (build 315)

> **User shared live screenshots of both real sessions scanned together in one batch** (v1.2.91, the still-installed build) and asked to recheck the page's text. Two real issues visible in the screenshots that hadn't shown up before because earlier testing was always single-session:
>
> 1. **"Timestamp CSV Status: Present (8 sessions)"** — wrong unit word. The `8` is a correct count of *videos* with a written CSV (4 cameras × 2 sessions), but the label said "sessions" — there are only 2. Root cause was in the localization string itself (`verifyTimestampCsvPresentCount`), not the counting logic.
> 2. **"Recording Mode: -" and "Scientific Timing Source: -"**, both in the top summary cards *and* in each individual session's own simple-summary line ("Recording mode: -"), despite every camera in both sessions being confirmed Original Capture Mode. Traced this one much deeper than the cards: `SessionComparisonService` (STABLE_CORE_V1) only sets `audit.SessionTimingMode` when the *legacy* `CameraMetadataRecord.OriginalCaptureMode` already reads `true` — but `MetadataParser` explicitly writes `false`/`""` into that field for any V2 recording (confirmed via test: not just an unset default, an explicit overwrite), so this check has never fired for a single V2 session, ever. The per-row metadata enrichment added in v1.2.93 doesn't retroactively fix this, because `audit.SessionTimingMode` is computed earlier in `VerifyAsync`, before any enrichment runs.
>
> Fixed both. The second one needed a genuinely new correction step (not just reusing v1.2.93's per-row fix), added to `EnrichV2SessionGroups()` so it covers the per-session summary line for both single- and multi-session scans; the top-level cards already had a suitable correction point in `ReconcileV2Summary`.

### Fixed
* **`localization/en.json`, `localization/ja.json`** — `verifyTimestampCsvPresentCount` now says "videos" (English) / "件の動画" (Japanese) instead of "sessions" — the count it formats has always been a video/camera count, not a session count.
* **`VideoVerificationPage.xaml.cs`** — `EnrichV2SessionGroups()` now also corrects `audit.SessionTimingMode` (and `FrameCountDifferenceAcceptedBecauseOriginalMode`) per session when every camera in it confirms Original Capture Mode via its own V2 metadata — fixes the per-session "Recording mode: -" line for every scan, not just multi-session batches.
* **`VideoVerificationPage.xaml.cs`** — `ReconcileV2Summary()` now also corrects the "Recording Mode" and "Scientific Timing Source" top cards when every scanned video agrees, using the same pattern already used there for Timestamp CSV Status and Scientific Timing Confidence.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.95`. 295 tests passing (unchanged).
* Wrote and ran (then removed) two temporary tests: one confirming `CameraMetadataRecord.OriginalCaptureMode`/`RecordingTimingMode` really do come back `false`/empty (not just unset) from the legacy parser for a real V2 session; one confirming `BuildFriendlySessionSummary` renders "Recording mode: Original Capture Mode" instead of "-" once `SessionTimingMode` is corrected.

## [v1.2.94] - 2026-07-08 (build 314)

> **User asked for a full audit of two freshly-recorded real sessions** (`test1_20260708_210603`, English UI; `test2_20260708_211156`, Japanese UI) **against the Video Verification page and metadata output.** Ran ffprobe ground-truth against all 8 videos, then ran the full Scan→Verify→V2-enrich→Export pipeline (built as an end-to-end xunit test, not just code review) against both sessions.
>
> **Per-video enrichment (v1.2.93's fix) confirmed working correctly on both new sessions**: real measured FPS, frame counts, and CSV data all populated and matching ffprobe/CSV ground truth to within rounding (e.g. session 1 cam1: app-measured 29.684 fps vs ffprobe-derived 29.685 fps), both sessions' own recording-time audits agreeing on `PASS_WITH_WARNING` (cam4 in both sessions genuinely runs at true 30fps against the other cameras' ~29.68fps — expected Original Capture Mode behavior, not corruption).
>
> **Checking the raw JSON output for the Japanese session surfaced a real, separate localization gap**: several V2 camera-control `warning` messages (`BacklightCompensation not supported...`, `OpticalImageStabilizationControl not supported...`, `Flicker reduction is not exposed...`, etc.) are authored in `capture/video_engine_v2/CameraControlManagerV2.cs` as fixed English string literals with no language awareness at all — unlike the surrounding JSON, which is fully bilingual via `MainWindow.xaml.cs`'s existing `J(en, jp)` pattern. This produced a metadata.json that mixed English control warnings into an otherwise-Japanese document. Found a code comment showing this exact problem had already been partially patched for the human-readable `.txt` writer's flicker-reduction line, but never extended to the JSON writer or the other four warning messages.
>
> Entirely within `capture/video_engine_v2/` and `MainWindow.xaml.cs` — both explicitly outside STABLE_CORE_V1 per the freeze's engine-scope note, no exception needed.

### Fixed
* **`MainWindow.xaml.cs`** — added a `LocalizeControlWarning` helper (next to the existing `J()`/`CtrlSupport`/`CtrlResult2` helpers in `WriteV2SlotMetadataAsync`) that translates the known fixed-string camera-control warnings from `CameraControlManagerV2` into Japanese when recording in Japanese mode; wired into all five `controls.*.warning` fields in the V2 metadata JSON. Raw driver/exception-message warnings (not one of the known fixed strings) pass through untranslated rather than guessing at a translation.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.94`. 295 tests passing (unchanged).
* Wrote and ran (then removed) a temporary end-to-end test against both real sessions confirming per-video enrichment (FPS, frame counts, CSV, timing status) matches ffprobe ground truth with no leftover zero-value or false-FAIL artifacts. The control-warning localization fix is recording-time-only code and could not be exercised without physical camera hardware in this environment; verified by code review and build success only — confirm on next real Japanese-mode recording.

## [v1.2.93] - 2026-07-08 (build 313)

> **User asked to check whether the exported "Export TXT/JSON/CSV" reports (and the auto-saved `video_audit_report.txt`) could be fixed for VideoEngineV2 sessions**, having already found they show `Real Capture FPS: 0.000`, `requestedFps/writerFps/containerFps: 0.000/0.000/0.000`, `framesCaptured/framesWritten: 0/0`, `frameTimestampCsvWritten: False`, and `error: Scientific timing status: FAIL` for every camera — despite the interactive page's own table/detail panel already showing the correct, corrected values for the exact same session. Root cause: `EnrichV2SessionGroups()` (added in earlier releases) only ever patched the on-screen `VerificationTableRow` objects; a **second, completely separate object graph** — `report.Videos[i].Metadata` (`CameraMetadataRecord`, populated by the legacy schema-blind `MetadataParser`) — feeds both the export writer and the auto-saved report, and was never touched by any enrichment step. For a V2 recording that legacy parser "succeeds" (valid JSON, just the wrong schema) but extracts nothing meaningful, so every numeric field silently defaulted to 0/false.
>
> User explicitly ruled out removing the Export buttons instead of fixing them — they work correctly for legacy (non-V2) recordings and are called out as an expected feature in this project's own regression checklist.
>
> **Fixed by extending the same authoritative-already-computed-value pattern to the export path**, entirely within files the freeze's own engine-scope note already excludes from STABLE_CORE_V1 (`V2MetadataReader.cs`, `V2VerificationRunner.cs` — no protected file touched, no freeze exception needed this round). Added a new `V2VerificationRunner.EnrichLegacyMetadataRecord(video, meta)` and wired it into a new `EnrichV2VideoMetadata()` call alongside the existing `EnrichV2SessionGroups()`.
>
> **Building the real end-to-end test (Scan → VerifyAsync → V2Runner.Run → enrich → export) against the user's actual recorded session caught two real, previously-invisible bugs along the way**, neither of which I'd have found from code review alone:
> 1. `V2MetadataReader.TryRead` read `framesWritten` from the JSON's `"recording"` block, which has no such key at all (only `started`/`finalized`/`outputFile`/`writerStatus`/etc.) — the real value lives under `"timing.framesWritten"`. This meant `V2RecordingMetadata.FramesWritten` had silently returned `0` for **every VideoEngineV2 recording ever produced**, masked because most consumers had a working ffprobe fallback that happened to show the right number anyway. One consumer did **not** have a safe fallback: the frame-count-mismatch warning added earlier this session (`"Container has N real frames but the app's internal counter reported 0"`) would have fired as a false positive on every single V2 session, unconditionally, once a user's build picked it up.
> 2. My first draft of the Scientific-Timing-Status correction checked the wrong field — `CameraMetadataRecord.ScientificTimingStatus` (a mostly-unused field, empty by default) instead of `VideoVerificationResult.ScientificTimingStatus` (the field that actually drives the "Scientific timing status: FAIL" error message and export verdict). The end-to-end test caught this immediately by asserting against the real exported text instead of just the field I expected to matter.
>
> Both fixed; verified against the real session that the exported report and the app's own frame-count-mismatch warning are now both accurate. Temporary tests (real-hardware-session-gated, no-op on any other machine) removed after confirming, per this project's established practice for this class of verification.

### Fixed
* **`V2MetadataReader.cs`** — `FramesWritten` now correctly parsed from `timing.framesWritten` instead of a `recording.framesWritten` key that never existed; was silently `0` for every V2 recording until now. Also added parsing for `RecordingTimingMode`/`IsOriginalCaptureMode`, `MeanFrameIntervalMs`/`MinFrameIntervalMs`/`MaxFrameIntervalMs`/`IntervalStdMs`, and `FrameTimestampCsvFile`.
* **`V2VerificationRunner.cs`** — new `EnrichLegacyMetadataRecord(video, meta)` patches `CameraMetadataRecord` fields (FPS, frame counts, durations, capture-interval stats, timestamp CSV info, Original Capture Mode, Scientific Timing Status) from V2 metadata whenever the legacy parser left them at their zero/false/empty default. Never overwrites a value the legacy parser did manage to extract.
* **`VideoVerificationPage.xaml.cs`** — new `EnrichV2VideoMetadata()` call wires the above into the existing V2-enrichment step, so "Export TXT/JSON/CSV" now reflect the same corrected data the interactive page already shows. (The auto-saved `video_audit_report.txt`, written inside `VideoVerificationService.VerifyAsync` itself before this enrichment ever runs, is not covered by this fix — fixing that would require moving logic into the protected recording/verification sequence, which was intentionally out of scope here.)
* Corrected the Scientific-Timing-Status enrichment to check/update `VideoVerificationResult.ScientificTimingStatus` (the field that actually drives the FAIL error message) instead of the differently-populated `CameraMetadataRecord.ScientificTimingStatus`.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.93`. 295 tests passing (unchanged).
* Wrote and ran (then removed, per established practice) two temporary tests against the user's real 4-camera VideoEngineV2 session: confirmed `V2MetadataReader` parses the new fields correctly, and confirmed the full Scan→Verify→Enrich→Export pipeline produces a report with real FPS/frame-count/timestamp values and no false "Scientific timing status: FAIL" for a session the app's own recording-time audit already scored `PASS_WITH_WARNING`.

## [v1.2.92] - 2026-07-08 (build 312)

> **User asked to check the Video Verification page's English/Japanese text in full detail — every row, column, blank box, value, button, log line, and metadata output — and fix everything.** Ran a scripted, exhaustive cross-check (not spot-checking): extracted all 219 localization keys referenced anywhere in `VideoVerificationPage.xaml.cs`/`V2VerificationRunner.cs` and verified each exists in both `en.json` and `ja.json`, checked for duplicate keys across both files (818 keys each), and checked whether any Japanese value was accidentally left identical to its English counterpart. Found and fixed a real, previously-invisible bug: `verifyColCamera` (and three other keys — `verifyColStatus`, `verifyColDup`, `verifyNotVerifiedYet`) were defined **twice** in both JSON files, a leftover from an old table layout; since `LanguageManager.Load()` deserializes into a plain dictionary, the second definition silently overwrote the first, so the "Camera" column header showed the English abbreviation "Cam" even in Japanese mode. Removed the dead duplicate blocks.
>
> **Also fixed, all non-protected UI code:**
> - Several V2-path log lines and detail-panel strings (`[V2] Corrected false-positive FAIL...`, `[V2] Enriched N slot(s)...`, the "NOTE: Legacy verification reported..." explanation, a frame-count-mismatch warning, `DisplayTimingMode`'s "Original Capture Mode" label) were hardcoded English with no key lookup at all — added ~30 new keys and wired them in.
> - **`ApplyLanguage()` never refreshed the already-displayed "Selected Video Detail" panel.** Switching the language dropdown correctly refreshed the per-session table (bound properties re-read on template rebuild) but left the big detail text box showing whatever was last built — in whichever language was active *then* — until the next Verify or row click. Now regenerates it from current state (selected row → last full report → last V2-standalone summary) on every language switch.
> - **`V2MetadataReader.cs` never parsed `timing.estimatedFpsFromTimestamps`**, even though the app already computes and writes this value (the real, per-frame-timestamp-derived native FPS) to every V2 recording's metadata JSON. The "Real Capture FPS" column showed `-` for every VideoEngineV2 session as a result. Verified against a real recording: the newly-parsed value (e.g. cam1: 29.684) matches ffprobe's independently-computed average frame rate almost exactly (≈29.685) — confirms the fix pulls a real, accurate number, not a guess.
> - **"Recommended Action" always said "Use timestamp CSV" for any WARNING-status row**, even when the Timestamp CSV column itself read "-"/"MISSING" — i.e. it could tell the user to use a file that was never written. Now checks whether the column actually contains a row count before recommending it; otherwise shows a "no timestamp CSV" message instead.
>
> **User then asked to "defreeze all previous stable core, edit and fix all errors."** Read `STABLE_CORE_V1_FREEZE.md`/`STABLE_CORE_V1_EXCEPTIONS.md` before touching anything: found the freeze isn't a blanket lock — it's a formal exception process with 5 specific trigger categories (recording failure, camera-mapping bug, crash/recovery, scientific-accuracy issue, hardware-handling issue) — and that its own policy explicitly states **"cosmetic changes ... do not qualify."** Pure text-localization fixes don't fit any of the 5 categories. Rather than stretching a category to cover it, asked the owner directly; owner chose to override explicitly with an honest log entry (see exception #8 in `STABLE_CORE_V1_EXCEPTIONS.md`) rather than a fabricated "scientific accuracy" justification. Owner also confirmed: (a) scope limited to log/report text only, no behavior change; (b) recording-time metadata files (`metadata.json`/`.txt`) stay English-only, per that file's own explicit documented rule ("Recording metadata files remain English regardless of UI language") — untouched; (c) follow the regression checklist properly.
>
> **Localized the remaining hardcoded English in the protected verification pipeline**, all additive/parallel — no existing constant, method signature behavior, or classification logic removed or altered: `VideoVerificationService.cs` (no-videos/ffprobe-missing/session-status/audit-report-saved/overall-verdict log lines), `SessionComparisonService.cs` (Original-Capture session interpretation notes, a frames-captured-diff warning), `VerificationReportWriter.cs`/`VerificationReportMapper.cs` (the "Scientific Timing Confidence" export label and a few explanatory sentences shared with the log fixes above — the much larger per-field technical diagnostics dump in both files, e.g. "CPU average/peak", "Writer queue max depth/capacity", was deliberately left alone as out-of-scope technical output, not translatable prose). Added `GetXxx(LanguageManager?)` overloads to three previously-unprotected constant classes (`OriginalCaptureAuditPolicy`, `OriginalCaptureVerificationPolicy`, `ScientificTimingConfidence`) that resolve a key when a language is supplied and fall back to the original English constant otherwise — the constants themselves are untouched, so every other existing caller is unaffected.
>
> **Regression checklist (see `STABLE_CORE_V1_REGRESSION_CHECKLIST.md`), to the extent possible without physical camera hardware in this environment:** `dotnet build` Release — 0 errors; `dotnet test` — 295/295 passing; offline audit (`VerifyFolderCli`) re-run against a real 4-camera session before and after the change — byte-identical output (same `Overall: Fail`, frame diffs, per-camera verdicts and frame counts), confirming the text-only changes introduced zero classification/behavior drift. Physical dual-camera recording tests from the checklist were not run — not applicable, since no recording-engine code was touched.

### Fixed
* **`localization/en.json`, `localization/ja.json`** — removed 4 duplicate keys (`verifyColCamera`, `verifyColStatus`, `verifyColDup`, `verifyNotVerifiedYet`) that silently shadowed correct translations; added ~40 new keys covering previously-unlocalized V2 log/detail text and the protected-file text below.
* **`VideoVerificationPage.xaml.cs`** — V2 log lines, the "NOTE: Legacy verification..." explanation, `DisplayTimingMode`, and a frame-count-mismatch warning now localized; `ApplyLanguage()` now regenerates the Selected Video Detail panel instead of leaving it in the previously-active language; "Recommended Action" no longer tells the user to use a timestamp CSV that doesn't exist.
* **`V2VerificationRunner.cs`** — the "VideoEngineV2 Recording Engine" per-video detail section (Engine/Backend/Encoder/Camera/Controls labels) is now fully localized.
* **`V2MetadataReader.cs`** — now parses `timing.estimatedFpsFromTimestamps`; "Real Capture FPS" populates correctly for VideoEngineV2 sessions instead of showing `-`.
* **`VideoVerificationService.cs`, `SessionComparisonService.cs`, `VerificationReportWriter.cs`, `VerificationReportMapper.cs`, `VideoProbeService.cs`** (STABLE_CORE_V1 — explicit documented exception, see `STABLE_CORE_V1_EXCEPTIONS.md` entry #8) — remaining hardcoded English log/report strings now resolve a localization key when a language is supplied; unchanged when not (e.g. `VerifyFolderCli`).
* **`OriginalCaptureAuditPolicy.cs`, `OriginalCaptureVerificationPolicy.cs`, `ScientificTimingConfidence.cs`** (unprotected) — added `GetXxx(LanguageManager?)` localized overloads alongside the existing English constants.

### Tests
* Updated `BackendVersion` constant (`VideoEngineRegistry.cs`) and its test assertion to `1.2.92`. 295 tests passing (unchanged).
* Verified `V2MetadataReader`'s new `MeasuredFpsFromTimestamps` field against a real recording's metadata JSON and against ffprobe's independently-computed frame rate — matched to within 0.001 fps.
* Re-ran `VerifyFolderCli` against a real 4-camera session before and after the STABLE_CORE_V1 text edits — identical verdicts/frame counts, confirming no behavior change.

## [v1.2.91] - 2026-07-07 (build 311)

> **User re-tested v1.2.90 on two more real sessions and asked "why does it show WARNING, and check the detail text/metadata output too."** The top-level page (cards, per-session table) was now internally consistent and correct — WARNING for a session with a genuine ~107-frame inter-camera spread (OBSBOT running its own real 30.0fps against j5 cameras' real 29.686fps; the app's own recording-time audit independently agrees: `PASS_WITH_WARNING`, not a bug) and a clean PASS for a session with only a 21-frame spread. But two more rendering locations, not touched by v1.2.88-90, still showed the original stale FAIL data, contradicting the now-correct cards/table right below them on the same screen:
>
> 1. A separate "--- SESSION RESULT ---" block in the detail text — built from `report.SessionResult`, a **third**, independent whole-scan legacy aggregate (distinct from both `report.Summary`, fixed in v1.2.89/90, and the per-session `report.SessionAudits`, fixed in v1.2.88). It carries its own pre-baked `SessionMessages` bullet list (e.g. "cam1: individual camera audit failed") that v1.2.88's per-session correction never touched.
> 2. The per-video detail panel (right-hand "Selected Video Detail" for one camera) — showed `Status: PASS` at the top but then "Failures: - Scientific timing status: FAIL" and "Scientific Recommendation → Scientific timing status: FAIL" further down, plus "Metadata completeness: 0.0%" (technically accurate against the *legacy* 44-field schema V2 JSON doesn't use, but reads as an alarming data-loss signal for what is actually complete, fully-populated V2 metadata under a different schema).
>
> **Verified the actual recorded metadata files (`cam1_metadata.txt`/`.json`) are, and always were, completely correct** — every issue found and fixed across this whole investigation (v1.2.88-91) was confined to the Video Verification page's separate, later re-analysis of those files, never the recording pipeline's own output.
>
> **Fixed, still without touching `STABLE_CORE_V1`:** the "SESSION RESULT" block is now suppressed (replaced with a short, correct rollup reusing the same corrected counts already shown in the top cards) whenever every scanned video is confirmed VideoEngineV2; per-video "Scientific timing status" is enriched from the V2 metadata's own `timing.timingConfidence`/`verification.interCameraTimingConfidence` the same way the session-level equivalent already was; "Metadata completeness: 0.0%" is replaced with an explanatory note instead of a misleading percentage; and the per-row/per-video `ErrorMessages` list (driving the contradictory "Failures:" section) is cleared whenever that row's status gets corrected, mirroring the same fix already applied to the session-level `Failures` list in v1.2.88.

### Fixed
* **`VideoVerificationPage.xaml.cs`** — the redundant, stale "SESSION RESULT" detail-text block (a third independent legacy aggregate) is now suppressed and replaced with a short corrected rollup for confirmed-VideoEngineV2 folders.
* **`VideoVerificationPage.xaml.cs`**, **`V2MetadataReader.cs`** — per-video "Scientific timing status" and "Metadata completeness" in the detail panel now reflect the same V2-audit correction already applied elsewhere, instead of a stale legacy-schema FAIL/0%.
* **`VideoVerificationPage.xaml.cs`** — per-row/per-video `ErrorMessages` (driving the "Failures:" section) now cleared when that row's status is corrected, so the detail text stops contradicting the corrected badge above it.
* Extracted a shared `CountV2RowVerdicts()` helper so the top cards and the detail text always agree with each other and with the per-row table (previously two separate, hand-duplicated counting loops).

### Tests
* Updated `BackendVersion` assertion to `1.2.91`. 295 tests passing (unchanged). Manually cross-checked the real recorded `cam1_metadata.txt`/`.json` for two new real sessions — confirmed clean, unaffected by any of this investigation's UI-layer bugs.

## [v1.2.90] - 2026-07-07 (build 310)

> **v1.2.89's summary-card reconciliation had a real bug of its own, found from user testing.** The table correctly showed "WARNING" for every camera row, but the top cards contradicted it (Passed: 4, Warnings: 0), Scientific Timing Confidence showed "PASS" instead of the correct warning-level result, and "Session duration" still showed raw "FAIL" for this particular (single-session) scan.
>
> **Root cause: a string-vocabulary mismatch introduced in v1.2.88/89.** `SessionGroupViewModel.NormalizeSessionResult` returns the generic display trio `"PASS"`/`"WARNING"`/`"FAIL"`, but several of the v1.2.89 comparisons checked the result against `CameraAuditStatus.PassWithWarning`, which is the *different* string `"PASS_WITH_WARNING"`. `"WARNING" != "PASS_WITH_WARNING"`, so every one of those checks silently fell through to the "not a warning" branch — miscounting warnings as passes in the summary tally, in the Scientific Timing Confidence worst-case calculation, and in the `SessionVerdict` conversion (via `CameraAuditStatus.ToVerdict`, which expects the same `"PASS_WITH_WARNING"` vocabulary). Fixed by routing every normalized-result comparison through one new helper (`NormalizedResultToVerdict`) instead of re-deriving the mapping ad hoc in three different places with two different vocabularies.
>
> **Two more real gaps found and fixed alongside it:**
> - The session's `Failures` list (e.g. "Inter-camera frame difference too large") was never cleared when a session got corrected from FAIL to WARNING/PASS — so the Simple View's "Note:" line kept saying "verification failed; inspect details." even after the badge above it correctly said WARNING. Now cleared when a correction is applied (safe: reaching that code path already means V2's own full session audit independently re-validated everything).
> - "Session duration" only got corrected when multiple sessions were scanned at once (mirroring the pre-existing FPS Spread pattern) — but the same false-positive FAIL happens on a **single**-session scan too, which v1.2.89 didn't handle (exactly the case in the user's screenshot). Now corrects whenever the card shows a raw FAIL, regardless of session count, using the worst *already-corrected* per-session status instead of a vague "N sessions audited separately" placeholder.
> - Per-camera table rows' own "Timestamp CSV" column still showed "MISSING" even though the top-level Timestamp CSV Status card was already fixed in v1.2.89 — that top card and the per-row column are two separate fields; only the top one was enriched. Now both are.

### Fixed
* **`VideoVerificationPage.xaml.cs`** — fixed a string-vocabulary mismatch (`"WARNING"` vs. `"PASS_WITH_WARNING"`) that caused the v1.2.89 summary-card fix to silently miscount warnings as passes; centralized the mapping in one `NormalizedResultToVerdict` helper.
* **`VideoVerificationPage.xaml.cs`** — corrected sessions now clear their stale `Failures` list so the detail text's "Note:" line stops contradicting the corrected status.
* **`VideoVerificationPage.xaml.cs`** — Session Duration card now corrects on single-session scans too, not just multi-session ones.
* **`VideoVerificationPage.xaml.cs`**, **`V2MetadataReader.cs`** — per-row Timestamp CSV column enriched from V2 metadata the same way the top-level summary card already was.

### Tests
* Updated `BackendVersion` assertion to `1.2.90`. 295 tests passing (unchanged). Verified the new `V2MetadataReader` fields (`TimingConfidence=PASS`, `InterCameraTimingConfidence=PASS_WITH_WARNING`, `TimestampCsvWritten=true`, rows=53470) against the user's real session before removing the temporary test.

## [v1.2.89] - 2026-07-07 (build 309)

> **v1.2.88's session-level FAIL correction was confirmed working (per-row/per-session status flipped FAIL→WARNING correctly on real data) but the fix was incomplete** — user re-tested and found the top-level "Verification Summary" cards (Overall result, Failed count) and the "Timing Quality" cards (Session duration, Scientific Timing Confidence) still showed the original FAIL, plus Timestamp CSV Status still showed "MISSING/INCOMPLETE" despite the CSV genuinely existing with real data. Root cause: those cards are populated by `ApplySummary()` from `report.Summary`/`report.SessionResult` — legacy, schema-blind aggregate objects computed **before** any V2 enrichment runs — while v1.2.88's fix only touched the per-session `_sessionGroups` list, never re-ran `ApplySummary()` afterward.
>
> **Fixed the remaining cards, all still without touching any `STABLE_CORE_V1` file.** Extended `V2MetadataReader.cs` to also read `timing.timingConfidence`, `verification.interCameraTimingConfidence`, `timing.timestampCsvWritten`/`timestampCsvRows` — values the app already computes and writes at recording time. Added `ReconcileV2Summary()` (`VideoVerificationPage.xaml.cs`) which, only for a folder where every scanned video is confirmed VideoEngineV2, re-runs `ApplySummary()` (which now picks up v1.2.88's session-level corrections for free, since `report.SessionAudits` and `_sessionGroups[*].Audit` are the same mutable objects) and then overrides the specific stale cards: Pass/Warn/Fail/Overall recomputed from the corrected per-row statuses; Session Duration/FPS Spread cards (which meaninglessly compare across unrelated sessions — e.g. a 16s smoke test against a 30-minute session) get the same "N sessions audited separately" treatment already used for FPS spread, now extended to Session Duration too and properly localized (it was a hardcoded English string before); Scientific Timing Confidence uses the worst-case value across all sessions' own already-computed timing confidence; Timestamp CSV Status reflects the real, already-written CSV data instead of a field the legacy schema doesn't have.
>
> **Fixed the scroll bug more robustly.** v1.2.88's fix (unconditionally forwarding wheel events from the per-camera DataGrid to its nearest ancestor ScrollViewer) was directionally right but incomplete: it never let scrolling continue past the session list into the rest of the page once the list itself was exhausted. Replaced with a boundary-aware handler on the session list's own ScrollViewer: it scrolls itself normally while it has room, and only forwards further up to the outer page ScrollViewer once already at the top/bottom boundary in the requested direction — so scrolling anywhere over the verification table first scrolls through sessions, then continues scrolling the whole page, matching normal expectations.
>
> **Verified against a real 30-minute continuous 4-camera recording** (the user's `test1_20260707_215841`, the longest single session tested yet — prior sessions topped out at ~13 minutes): all 4 cameras produced valid H.264, ffprobe frame counts matched the app's own `framesWritten` exactly (e.g. cam2: 54026/54026), a full forced frame-by-frame decode of cam2's 54,026 frames found zero errors, zero UI freezes logged, no crash file, and the metadata's own `verification.globalSessionResult`/`timing.timingConfidence` both confirm `PASS_WITH_WARNING`/`PASS` — consistent with everything this release's fixes now correctly display.

### Fixed
* **`VideoVerificationPage.xaml.cs`** — the Verification Summary and Timing Quality cards, and Timestamp CSV Status, now correctly reflect VideoEngineV2 recordings' own already-computed audit instead of a stale legacy-schema-blind aggregate; extended the "N sessions audited separately" treatment (now localized) from FPS Spread to Session Duration.
* **`VideoVerificationPage.xaml.cs`** — scroll fix now boundary-aware: the session list scrolls itself first, then falls through to scrolling the whole page, instead of stopping at the session list's own boundary.
* **`V2MetadataReader.cs`** — reads `timing.timingConfidence`, `verification.interCameraTimingConfidence`, `timing.timestampCsvWritten`/`timestampCsvRows` for the above.

### Tests
* Updated `BackendVersion` assertion to `1.2.89`. 295 tests passing (unchanged). Verified end-to-end against a real 30-minute 4-camera recording (full decode, frame-count cross-check, log audit) — see above.

## [v1.2.88] - 2026-07-07 (build 308)

> **User reported two problems while testing v1.2.87's Deep Verify on real recordings**: (1) the mouse wheel wouldn't scroll while hovering over the Video Verification page's per-session results table, and (2) every one of 6 real recorded sessions showed session-level "FAIL" and per-camera "FAIL" despite the videos being genuinely valid (confirmed via Deep Verify itself: 0 duplicate frames, exact frame-count matches).
>
> **Root-caused the FAIL as a real bug, not a true failure.** The legacy ffprobe-based verification stack (`VideoVerificationService`/`SessionComparisonService`/`MetadataCompletenessPolicy`, all `STABLE_CORE_V1`) was built for the old flat metadata schema (`RecordingTimingMode`, `originalCaptureMode`, `measuredCameraFps`, etc. as top-level JSON keys) and has no visibility into VideoEngineV2's nested JSON schema (`videoSettings.recordingTimingMode`, `verification.sessionResult`, etc.) — the default recording pipeline since v1.1.7. When it tries to read a V2 recording's metadata, essentially every field the legacy schema expects comes back missing, and — critically — `SessionComparisonService`'s inter-camera frame-count check has no way to recognize "Original Capture Mode" (real per-camera frame-count differences from independently-measured FPS, which this app's own user guide documents as expected, not an error) for V2 recordings, so it FAILs the session outright on exactly the kind of frame-count spread (e.g. 239 frames across a 12-minute/4-camera session) the app is designed to correctly interpret as a warning, not a failure. Confirmed directly: the same recordings' own `cam*_metadata.json` files (written by the app itself at recording time, under `verification.sessionResult`/`globalSessionResult`) already correctly say `PASS_WITH_WARNING` for every one of the 6 sessions — the legacy path was simply blind to that already-correct answer.
>
> **Fixed without touching any `STABLE_CORE_V1` file.** Extended `V2MetadataReader.cs` (already explicitly V2-exempt from the freeze, per its own header comment) to also read `verification.sessionResult`/`globalSessionResult` — a value the app already computes and writes to disk at recording time, accounting for V2's Original Capture Mode semantics. Added a new `EnrichV2SessionGroups` step (`VideoVerificationPage.xaml.cs`, not a protected file) that, only when a session is confirmed fully VideoEngineV2 (V2 metadata present for every camera) and the app's own V2 audit disagrees with the legacy path's FAIL, corrects the displayed session/row status to match the V2 audit's already-correct verdict — one-directional (FAIL → PASS/WARNING only; never invents a more lenient result, and never touches a session where both paths already agree). The correction is visible in the UI (a "NOTE: Legacy verification reported FAIL... corrected to..." line) and logged, so it's never a silent override.
>
> **Fixed the scroll bug**: each per-camera `DataGrid` inside the session results list sets its own `ScrollViewer.VerticalScrollBarVisibility="Auto"`, and WPF's `DataGrid` always marks `MouseWheel` as handled once it processes the event — even with only 1-4 rows and nothing left to scroll internally. This silently ate every wheel-scroll attempt made while hovering over a table's rows, instead of letting it reach the session list's own scrollable region above it. Fixed by forwarding the event to the nearest ancestor `ScrollViewer` whenever a `DataGrid` receives it.

### Fixed
* **`VideoVerificationPage.xaml.cs`** — mouse wheel scrolling now works while hovering over a session's per-camera results table (was being silently consumed by the table's own `DataGrid`).
* **`V2MetadataReader.cs`**, **`VideoVerificationPage.xaml.cs`** — corrected false-positive session/camera FAIL verdicts for VideoEngineV2 recordings on the Video Verification page, caused by the legacy ffprobe-based verification stack not understanding V2's metadata schema. Uses the app's own already-computed, already-correct V2 session audit (`verification.globalSessionResult`, written at recording time) to fix the displayed verdict — never invents a result, never overrides a session where the two paths already agree.

### Tests
* Updated `BackendVersion` assertion to `1.2.88`. 295 tests passing (unchanged). Verified `V2MetadataReader`'s new fields and the FAIL-correction logic manually against the 6 real recorded sessions from the v1.2.87 test batch (`test1`-`test6`) before removing the temporary test files.

## [v1.2.87] - 2026-07-07 (build 307)

> **Added "Deep Verify" — an on-demand, independent per-frame MD5 duplicate-frame check on the Video Verification page**, per user request following the v1.2.86 native-COM-leak fix. Existing `DuplicateFrames`/`PlaceholderFrames` fields shown on the page are self-reported by the recorder's own internal counters — real, but never independently cross-checked against the actual video bytes. Deep Verify closes that gap by fully decoding every frame of a video and hashing it (MD5), then reporting how many hashes are exact repeats of an earlier frame — the same exact-hash method (not a perceptual/similarity check, which produces false positives on real camera footage) already used internally to audit this app's recordings.
>
> **New dependency: the app now bundles full `ffmpeg.exe` alongside the existing `ffprobe.exe`** (both already GPLv3-licensed, from the same gyan.dev build; `ffprobe.exe` alone can't do this — exact per-frame hashing requires the `-f framemd5` output muxer, an ffmpeg-only feature). This was a deliberate build/installer/security-policy change, not just a code change: `ffmpeg.exe` was previously explicitly excluded from every shipped artifact (`installer/MultiCamApp.iss` Excludes list, `scripts/build/stage_dist_runtime.ps1`'s copy list, and `scripts/packaging/validate_installer_security.py`'s allow-list, which would have hard-failed the release build the moment `ffmpeg.exe` appeared in `dist/` without this change) — all three now explicitly allow it under `runtime/ffmpeg/`, alongside `ffprobe.exe`.
>
> **New service `verification/DeepVerifyService.cs`** — deliberately NOT part of `VideoProbeService`/`VideoVerificationService`/`VideoScanner` (all `STABLE_CORE_V1`-protected); this is a new, additive, independent service that mirrors `VideoProbeService`'s `ResolveFfprobe` resolution-chain pattern for `ffmpeg.exe`, runs `ffmpeg -v error -i <file> -map 0:v:0 -f framemd5 -`, and counts exact hash repeats as duplicate frames. Always on-demand (click "Deep Verify" on the toolbar) — never runs automatically as part of Scan/Verify/Verify All, and never blocks or alters their results. Supports cancellation and a per-file timeout (`VerificationSettings.DeepVerifyTimeoutSeconds`, default 300s).
>
> Slow by design — roughly real-time relative to each recording's own duration (confirmed: hashing a 12-minute/720p recording took about a minute) — so it's opt-in only, with its own progress log lines, a dedicated "Deep Verify (Independent MD5 Check)" summary card group (status/files checked/total duplicates/worst file), and a red warning banner (mirroring the existing ffprobe-missing one) if `ffmpeg.exe` is somehow unavailable.

### Added
* **Video Verification page** — new "Deep Verify" button and summary card group performing an independent, on-demand per-frame MD5 duplicate-frame check via full `ffmpeg.exe` (`-f framemd5`), cross-checking the self-reported `DuplicateFrames`/`PlaceholderFrames` metadata fields against real decoded-frame hashes. New settings `Verification.FfmpegPath`/`Verification.DeepVerifyTimeoutSeconds`. New localization keys (`verifyDeepVerify*`, `verifyFfmpegMissing`) in `en.json`/`ja.json`.
* **Build/installer** — `ffmpeg.exe` is now staged, installed, and installer-verified alongside `ffprobe.exe` (`scripts/build/stage_dist_runtime.ps1`, `installer/MultiCamApp.iss`, `scripts/build/verify_release.ps1`, `scripts/packaging/validate_installer_security.py`, `scripts/packaging/inspect_setup_bundle.ps1`). `THIRD_PARTY_NOTICES.md` and `docs/user_guide/video_verification.md` updated accordingly.

### Tests
* Updated `BackendVersion` assertion to `1.2.87`. 295 tests passing (unchanged — Deep Verify has no automated test coverage yet since it depends on a real `ffmpeg.exe` process; validated manually against a real recorded session, see below).

## [v1.2.86] - 2026-07-07 (build 306)

> **Root-caused and fixed the "recording freezes / GPU-CPU overload / whole PC freeze after ~1 minute of continuous recording" report.** Investigation found that `MediaFoundationEncoderService.SubmitFrame` — called once per frame, per camera, on the frame-arrived thread — created two native COM objects every call (`IMFMediaBuffer` via `MFCreateMemoryBuffer`, `IMFSample` via `MFCreateSample`) and never disposed either one. Each `IMFMediaBuffer` holds an unmanaged allocation sized to one frame (e.g. ~5.5 MB at 1920x1080 BGRA, ~2.6 MB at 1280x720), which is only released when the wrapper's `Dispose()`/finalizer runs. Relying on GC finalization for this meant the single-threaded finalizer queue had to keep up with up to 240 allocations/sec (4 cameras x 60fps) — under sustained recording the allocation rate outpaced finalization, so committed unmanaged memory grew without bound (the managed heap looks tiny since each wrapper is just a pointer, so the GC has no signal to run more aggressively) until the system ran out of memory/GPU headroom, consistent with the reported escalation from recording stutter to full UI freeze to whole-PC freeze roughly a minute into a session. **Fixed** by explicitly disposing both objects in a `finally` block immediately after `WriteSample` returns — safe because `IMFSample.AddBuffer`/`IMFSinkWriter.WriteSample` both AddRef internally (standard COM ref-counting), so releasing our own reference does not free memory the sink writer still needs. Also disposed the one-time-per-recording `IMFAttributes`/`IMFMediaType` objects in `OpenAsync` for the same hygiene reason (negligible impact on their own, but no reason to leave them for the finalizer either).
>
> **Default resolution changed from 1080p to 720p @ 30fps** (both in `VideoEngineSettings` — the value the V2 capture pipeline actually reads when opening a camera — and in `config/appsettings.json`/the Resolution dropdown's pre-load fallback, which previously disagreed with each other in the 1080p direction). Requested per user report: 1080p's higher per-frame byte count made the leak above escalate faster, so 720p is both the safer and now the explicit out-of-the-box default across every layout (1-4 cameras).

### Fixed
* **`MediaFoundationEncoderService.cs`** — per-frame native COM leak (`IMFMediaBuffer`/`IMFSample` never disposed) that could exhaust unmanaged memory during long/multi-camera recordings, causing progressive freeze escalating to a full system freeze. Explicit `Dispose()` added for both objects plus the one-time media-type/attributes objects in `OpenAsync`.
* **`VideoEngineSettings.cs`**, **`config/appsettings.json`**, **`MainWindow.xaml.cs`** — default capture resolution changed from 1920x1080 to 1280x720 (30fps unchanged) so a fresh app start records at 720p by default in every camera layout.

### Tests
* Updated `BackendVersion` assertion to `1.2.86`. 295 tests passing (unchanged).

## [v1.2.85] - 2026-07-07 (build 305)

> **GitHub-publication readiness pass, per user request**: doc accuracy sweep, privacy/copyright/portability audits, and stale version-reference fixes ahead of open-sourcing this repository. No functional/recording code changed — this is a documentation and release-hygiene pass.
>
> **Stale version references fixed**: `README.md` ("Current release" banner + citation), `INSTALLATION.md` (citation), and `CITATION.cff` (`version`/`date-released`) all still said `v1.2.67` (17 releases behind); updated to the current version throughout. `docs/STABLE_CORE_V1_FREEZE.md`'s "Current app release" marker was similarly stale at v1.2.30-alpha.
>
> **Real factual documentation errors fixed**: `docs/OUTPUT_FILES_AND_METADATA.md`, `docs/user_guide/video_verification.md`, and `docs/architecture/overview.md` all still described the recorded MP4 color range as limited-range (`16-235`/`tv`) — that was true only briefly in v1.2.66/v1.2.67; the encoder was corrected to full-range (`0-255`/`pc`) in v1.2.78 after a side-by-side ffprobe comparison proved Windows Camera's real output is full-range, not limited (see v1.2.78 changelog entry). These three docs were describing already-reverted behavior as current.
>
> **Three independent audits run (privacy, copyright/license, hardware portability) — all came back clean**:
> - **Privacy**: no telemetry/network calls beyond the two known GitHub/local-file opens on the About page; no crash-reporting SDKs; machine name, CPU name, and camera hardware IDs are collected internally but explicitly redacted (`PrivacySanitizer.Redacted`) before any metadata/log file is written to disk; no hardcoded developer-machine paths in source.
> - **Copyright/license**: no unattributed third-party code found; every NuGet package referenced in any `.csproj` is documented in `THIRD_PARTY_NOTICES.md`; the custom non-commercial license and the bundled GPLv3 `ffprobe.exe` (distributed as a separate, unlinked executable) are correctly non-conflicting; copyright years consistent (2026–Present) everywhere.
> - **Hardware portability**: camera enumeration, resolution/FPS negotiation (with fallback ladder), and GPU-to-software rendering fallback are all driver-agnostic with no hardcoded camera counts/indices/names in the shipped app; the installer uses only Inno Setup path macros (no hardcoded drive letters or developer paths). One hardcoded-camera-name reference was found in `diagnostics/External4PreviewStressRunner.cs` (a manual dev-only stress-test tool gated behind an explicit `--external4-preview-stress` CLI flag, confirmed NOT invoked by the installer's `--smoke-test` path or by any normal app operation) — reviewed and left as-is since it can never run unintentionally for an end user.
>
> **GitHub push readiness confirmed**: repository is not yet a git repo (no `.git`); `.gitignore` already correctly excludes `dist/`, `.build/`, `installer/*_Setup.exe`, `installer/installer.zip`, `tools/`, video files, and logs; no secrets/credentials/API keys found in source; no recorded test video data or developer-machine paths inside the tracked tree.

### Fixed
* **Documentation** — `README.md`, `INSTALLATION.md`, `CITATION.cff`: stale `v1.2.67` version/citation references updated to current. `docs/OUTPUT_FILES_AND_METADATA.md`, `docs/user_guide/video_verification.md`, `docs/architecture/overview.md`: corrected stale limited-range (`tv`) color claims to reflect the full-range (`pc`) fix shipped in v1.2.78. `docs/STABLE_CORE_V1_FREEZE.md`: updated stale "current app release" marker.

### Tests
* Updated `BackendVersion` assertion to `1.2.85`. 295 tests passing (unchanged).

## [v1.2.84] - 2026-07-06 (build 304)

> **Verified live language-switch mid-session + full audit of 4 new sessions (16 videos) + a frame-accurate Start Recording response-time measurement via ShareX, per user request.** Confirmed the language selector correctly re-applies to metadata without restarting the app: 4 sessions recorded back-to-back with the language toggled between each (en/ja/en/ja) all produced correctly localized `metadata.txt`/`metadata.json`, including the v1.2.83 `[UI Diagnostics]` fix rendering correctly in both languages under this exact live-switch scenario. All 16 videos: `framesWritten` matched real `nb_frames` exactly, zero true duplicates (exact MD5), correct BT.709 color/resolution. ShareX-based frame-accurate measurement: Start Recording click-to-"Recording"-status delay was ~1.78s for a 4-camera 1080p session, consistent with the previously documented ~1-1.8s encoder-setup window (not a regression). Runtime log review: zero freezes during active Recording, GPU hardware encoding engaged throughout (D3D11/MediaFoundation both Available), zero crashes for this session's time window.
>
> **A real false-positive verification bug found and fixed**: all 8 of the batch's 1080p videos (but none of the 720p ones) were flagged with a `WARN_CSV_MISMATCH`/"gaps detected mid-session" verdict despite being genuinely clean recordings (verified frame-for-frame against ffprobe and exact MD5 hashing). Root cause: the timestamp CSV legitimately logs a few dozen extra rows during the ~1-1.8s Start Recording encoder-setup window (frames arrive and are logged while still in `Previewing`/`StartingRecording` state, before the encoder opens) — expected surplus, not lost data. The verdict check compared `csvRows` against `recordingFrames` with `Math.Abs()`, treating this expected surplus identically to a genuine deficit, and the existing tolerance (`max(20, frames/50)`) was too tight for higher-resolution sessions where the pre-roll window naturally logs more rows (22-37 extra for 1080p vs 10-17 for 720p, since encoder setup takes longer at higher resolution).

### Fixed
* **`MainWindow.xaml.cs`** — the CSV/frame-count integrity verdict (`WARN_CSV_MISMATCH`) no longer flags the expected pre-roll surplus (CSV rows logged during Start Recording's async encoder-setup window) as a mismatch. Only a genuine deficit (fewer CSV rows than frames actually submitted to the encoder) keeps the original tight tolerance; surplus now gets a generous allowance sized to the measured frame rate (~3 seconds worth of frames) instead of a flat percentage.

### Tests
* Updated `BackendVersion` assertion to `1.2.84`. 295 tests passing (unchanged).

## [v1.2.83] - 2026-07-06 (build 303)

> **Full audit of 21 new sessions (64 videos, 1-4 camera layouts, 3 resolutions) + a systematic warnings/GPU/UI-response sweep, per user request.** Every video: `framesWritten` matched real `nb_frames` exactly (0 diffs across all 64 — no recurrence of the v1.2.80/v1.2.82 frame-undercount anomaly), zero true duplicates, correct BT.709 color, correct full/pc color range. Zero UI Warning/Critical freezes across the entire batch (only 12 expected sub-second Minor stalls), zero GPU device-lost/fallback-to-WPF events, zero exceptions.
>
> **A real, confirmed localization bug found and fixed**: this batch included a language-switch test (test1-16 in English, test17-19 in Japanese), which surfaced a genuine gap — the `[UI Diagnostics]` section of the per-camera `metadata.txt` had every OTHER field label in this exact document properly localized into Japanese, but this one section's six field labels ("UI freeze detected", "UI freeze count", "Max UI freeze", "Freeze during state", "Preview FPS limit during recording", "Active camera count") were hardcoded English regardless of language — while the VALUES right next to them (はい/いいえ) were correctly localized, making the inconsistency obvious. A stale code comment ("technical data, not translated") had rationalized what was actually just a missed spot. Fixed by wrapping all six labels in the same `J(english, japanese)` pattern used by literally every other section in this method. Also localized the JSON `suspectedFreezeCause` value to match.

### Fixed
* **`MainWindow.xaml.cs`** — the `[UI Diagnostics]` metadata.txt section's six field labels are now properly localized (English/Japanese), matching every other section in the same document. The JSON `suspectedFreezeCause` value is now localized too.

### Tests
* Updated `BackendVersion` assertion to `1.2.83`. 295 tests passing (unchanged).

## [v1.2.82] - 2026-07-06 (build 302)

> **Full audit of 2 new sessions (8 videos) + ShareX, per user request.** 7 of 8 cameras clean: `framesWritten` matched real `nb_frames` exactly, zero true duplicates. One recurrence of the frame-undercount anomaly first seen in v1.2.80 — `test1_20260706_205956/cam4` (a Logitech c922 Pro Stream Webcam this time, not the OBSBOT from before, confirming this isn't camera-model-specific) had 1876 real frames in its mp4 (7 of them genuine duplicates, confirmed via exact MD5 hash) but the app's own counter reported only 1327 — under the same signature as before: severe measured-fps degradation (21.25 vs 30fps requested) and heavy mid-session gap counts (151).
>
> **The v1.2.80 diagnostic logging paid off**: the new `rawFrameArrived` counter (incremented unconditionally, before any state check) matched `csvRows` exactly (1700 = 1700) but both were still far below the real container frame count (1876) — proving the gap is *not* in the CSV-writer or encoder-counter plumbing (previously the leading theory), since even the least-gated counter in the whole pipeline undercounts the real muxed output. This narrows the likely cause to something between real frame delivery and the final container — plausibly the hardware H.264 encoder MFT performing its own internal frame-rate smoothing under large timestamp gaps, which would be outside this app's code entirely. Still not conclusively proven; no fix attempted.
>
> **A real gap found in the Video Verification page while investigating**: this exact class of bug was invisible to the app's own verification tooling even when ffprobe is available and successfully reads a video's true frame count. Both the V1 (`VerificationReportMapper.cs`) and V2 (`EnrichV2SessionGroups`) paths only ever *chose between* the app's self-reported frame count and ffprobe's real count (preferring the self-report when present) — neither path ever compared them. Since `VerificationReportMapper.cs`/`VerificationTableRow.cs` are STABLE_CORE_V1-protected, the fix was added to the unprotected V2 enrichment path instead: `EnrichV2SessionGroups()` now compares ffprobe's real frame count against the V2 metadata's `framesWritten` and appends a warning (visible in the row's Details) whenever they diverge by more than 5 frames — exactly the kind of gap this session's audit found twice now.

### Fixed
* **`ui/pages/VideoVerificationPage.xaml.cs`** — `EnrichV2SessionGroups()` now flags a warning when a real ffprobe-verified frame count diverges from the V2 metadata's self-reported `framesWritten` by more than 5 frames, closing a gap where this class of frame-accounting anomaly was invisible to the verification tool even with ffprobe available.

### Tests
* Updated `BackendVersion` assertion to `1.2.82`. 295 tests passing (unchanged).

## [v1.2.81] - 2026-07-06 (build 301)

> **Removed the "Disable Low-Light Compensation" checkbox from Advanced Camera Controls, per user decision.** Following the earlier discussion of which Advanced Camera Controls are worth keeping, this control was the weakest link: it's structurally dependent on exposure control (it only ever calls `VideoDeviceController.BacklightCompensation` from inside the exposure-apply path in `CameraSlotPipeline.cs`), and every camera tested this session (3× j5 Webcam JVU250, OBSBOT Meet SE) reported exposure control as "Not supported by device/driver" — making the toggle a no-op in practice on this hardware. Focus and Exposure controls themselves were kept, since research camera hardware (PTZ/machine-vision cameras) often does support them and removing them would silently break the app for anyone with better hardware than the test rig.
>
> Followed this codebase's existing "always-on internally, checkbox removed from UI" pattern (already used for `HighStabilityRecordingMode` and `ReapplyFocusBeforeRecording`) rather than deleting the backend capability outright: `DisableLowLightCompensation`/`DisableLowLightCompensationPerCamera` remain in `AppConfig.cs` and are now hardcoded to `true` at every former UI sync point, so `CameraSlotPipeline.cs`'s existing best-effort LLC-disable attempt (inside `ApplyExposureModeAsync`) keeps running exactly as before for any camera whose driver does support it — only the redundant, always-checked toggle is gone.

### Removed
* **`MainWindow.xaml`** — the "Disable Low-Light Compensation" checkbox in Advanced Camera Controls.
* **`MainWindow.xaml.cs`** — its Click handler, and the four call sites that synced it to/from `_vm.Config`, replaced with hardcoded `= true` assignments (matching the existing `HighStabilityRecordingMode` pattern).
* **`localization/en.json`, `localization/ja.json`** — the now-unused `disableLowLightCompensationCheckbox`/`disableLowLightCompensationTooltip` strings.

### Tests
* Updated `BackendVersion` assertion to `1.2.81`. 295 tests passing (unchanged).

## [v1.2.80] - 2026-07-06 (build 300)

> **Full-detail audit of 3 new 4-camera sessions (12 videos) + ShareX, per user request.** All 12 videos: correct BT.709 color, h264 codec, and — after correcting a methodology flaw discovered mid-audit — confirmed zero true duplicate frames across every camera. Only the already-known, already-mitigated `CAPABILITY_PROBE_ERROR` (cosmetic-only) appeared once in the runtime log; no other errors or Warning/Critical freezes.
>
> **Methodology correction (not an app bug, but affects how past duplicate-frame checks in this project should be read)**: `ffmpeg -f framemd5` without `-fps_mode passthrough` can silently drop real frames from its own output when re-syncing genuinely variable-frame-rate content to a nominal rate — confirmed on `test2_20260706_190504/cam1`, where plain `framemd5` produced only 1979 hash lines against a real, ffprobe-verified `nb_frames` of 2012 (stderr showed `drop=33`, i.e. ffmpeg's own pipeline, not the app, discarded 33 real frames). Re-running with `-fps_mode passthrough` (an *output* option — must go after `-i`) recovered the full, correct frame count with zero drops and zero duplicates. All 12 videos in this batch were re-verified with the corrected command; the "zero true duplicates" conclusion holds, but the technique itself needed this fix to be trustworthy on VFR content going forward.
>
> **One real, isolated anomaly investigated and diagnosed (not blindly fixed)**: `test1_20260706_190326/cam4` (an OBSBOT Meet SE virtual-camera device, which also showed severe FPS degradation — 20.65fps measured vs 30fps requested, 284 mid-session timestamp gaps) had 1942 real, unique, byte-verified frames in its mp4, but the app's own `framesWritten`/`FramesSubmittedSinceRecordingStart` counter reported only 1328, and the CSV timestamp row count only 1360 — a 582–614 frame undercount. The other 11 videos in this exact same batch all showed an *exact* 0-frame match between `framesWritten` and the real encoded frame count, confirming the counting mechanism is fundamentally sound and this is an isolated glitch tied to this one driver's stall/recovery behavior, not a systemic bug. Root cause not conclusively identified through static code review — `OnFrameArrived`, the CSV logger, and the encoder's `SubmitFrame`/`_framesSubmitted` path all checked out as internally consistent by design (CSV logging's state gate is a strict superset of the encoder's, so this undercount should not be possible under normal operation). Rather than guess at a fix for a not-yet-understood mechanism, added targeted diagnostic logging (matching this project's established "checkpoint logging first" approach from the Stop Preview crash saga) to catch enough evidence next time this recurs.

### Added
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — new unconditional `RawFrameArrivedCount` (increments before any state check, unlike every existing frame counter in this pipeline) and an expanded `V2_REC_STOP` log line now also reporting `rawFrameArrived`/`csvRows`/`encoderSubmitted` together, so a future recurrence of the frame-undercount anomaly can be directly compared against the real mp4's `nb_frames`.

### Tests
* Updated `BackendVersion` assertion to `1.2.80`. 295 tests passing (unchanged).

## [v1.2.79] - 2026-07-06 (build 299)

> **Frame-accurate audit of the Start Recording / Stop Recording UI transitions, per user request** — checked exactly how fast Status/Elapsed/Session Time update relative to the click, and whether the displayed values stay consistent with the real mp4 duration for downstream analysis. Used the ShareX screen recording at its native 30fps (33ms/frame) to pinpoint the transition frame-by-frame.
>
> **Start Recording**: the button disables instantly, but Status stays "Previewing" and Elapsed/Session Time stay at `00:00:00` for a real ~1.3–1.8 second window while all 4 camera encoders finish opening — this is genuine async setup latency, not lag in the UI update itself. Elapsed/Session Time reset to `00:00:00` and begin ticking in the exact same instant Status flips to "Recording" (same code path, same underlying stopwatch), so there's no separate delay between those two.
>
> **Stop Recording**: Status flips to "Previewing", both buttons disable, and Elapsed/Session Time freeze within 1–2 screen-capture frames (~33–67ms) of the click — genuinely instantaneous.
>
> **Precision caveat worth calling out for downstream analysis**: the frozen Elapsed/Session Time display uses whole-second `HH:MM:SS` formatting, so it will under-report the real mp4 duration by up to ~1 second purely from truncation, plus a little more because a few final frames are legitimately still flushed to each encoder in the moment between the UI freezing and the true per-camera encoder stop. In this test, the UI froze at `00:00:29` while the four camera files actually ran 30.16–30.47s. This isn't a bug — the on-screen counter was never meant to be sub-second-precise — but it means the live UI display should not be used as the precision source for downstream mp4 analysis; the metadata's `resolvedDurationS`/`framesWritten`/timestamp CSV (or the mp4 file itself) remain the accurate source, exactly as this project has already established.
>
> **One real minor bug found and fixed**: during every Start Recording click, "Cameras: X/4 ready" visibly dipped to `0/4` then climbed through partial counts before settling at `4/4`, even though nothing was actually wrong — confirmed via frame-by-frame ShareX review. Root cause: `CountV2ReadySlots()` only counted slots in `Previewing` or `Recording` state, but each slot passes through a `StartingRecording` transitional state on the way from one to the other, and a slot in that phase is still actively capturing, not "not ready".

### Fixed
* **`MainWindow.xaml.cs`** — `CountV2ReadySlots()` now also counts `StartingRecording`/`StoppingRecording` states as ready, so the "Cameras: X/4 ready" status no longer dips misleadingly during Start Recording.

### Tests
* Updated `BackendVersion` assertion to `1.2.79`. 295 tests passing (unchanged).

## [v1.2.78] - 2026-07-06 (build 298)

> **Full-detail three-way comparison of a new 3-camera MultiCamApp session, a simultaneous ShareX screen recording, and a simultaneous Windows Camera app recording, per user request.** Exact MD5 frame-hash checks found zero duplicate/ghost frames in every camera stream (MultiCamApp cam1/2/3 and Windows Camera all 100% unique frames); ShareX showed some exact-duplicate frames during static UI moments, which is expected for a screen recording and not evidence of an app freeze — cross-checked against the app's own freeze watchdog, which logged zero Warning/Critical freezes this session (only sub-second "Minor" stalls, all below the 1000ms Warning threshold, during ordinary camera-negotiation/stop-recording transitions). All three cameras requested 30fps and had 30fps formats successfully negotiated by their drivers, but real measured throughput came in at ~25fps uniformly across all three simultaneously-open cameras (2× j5 Webcam + 1× OBSBOT sharing a USB bus) — correctly surfaced as `PASS_WITH_WARNING` rather than hidden or padded, consistent with the app's "no artificial frame padding" design.
>
> **One real, evidence-based bug found and fixed**: every recording's metadata claimed `"colorRange": "Limited (16-235 / tv, matches Windows Camera)"` — but this side-by-side test proved that claim false. The real Windows Camera app recording from this exact session tagged `color_range=pc` (full, 0-255), not limited/tv. The limited-range choice was made in an earlier session based on an assumption about Windows Camera's convention that was never actually verified against real ffprobe output (the code comment literally said "per user's explicit choice", not "verified"). Presented the evidence to the user, who chose to make MultiCamApp's actual output genuinely match Windows Camera's real behavior rather than just fix the misleading text.
>
> Separately noted (not a MultiCamApp defect): the real Windows Camera recording's own color tagging is internally inconsistent — `color_primaries=bt709` but `color_space=bt470bg` and `color_transfer=unknown` — so MultiCamApp's fully-consistent BT.709 primaries/transfer/matrix tagging is arguably more correct regardless of the range setting.

### Fixed
* **`capture/video_engine_v2/MediaFoundationEncoderService.cs`** — `ApplyColorTags` now sets `MFNominalRange_Normal` (0-255/pc, value 1) instead of `MFNominalRange_16_235` (limited/tv, value 2), so newly recorded videos' color range genuinely matches Windows Camera's real output instead of an unverified assumption. Existing already-recorded files are unaffected (this only changes what future encodes tag).
* **`capture/backend/VideoEngineRegistry.cs`** — `ColorRange` metadata string updated to `"Full (0-255 / pc, matches Windows Camera)"` to match the corrected behavior.

### Tests
* Updated `BackendVersion` assertion to `1.2.78`. 295 tests passing (unchanged).

## [v1.2.77] - 2026-07-06 (build 297)

> **Full-detail audit of 29 new test sessions (72 videos across resolution/fps/camera-count combinations) plus a hardware-compatibility review for "any laptop, any GPU/CPU", per user request.** Every video: correct BT.709 color tags, h264 codec, resolution/fps matching per-camera hardware negotiation, and metadata frame counts matching container frame counts exactly (zero diffs across all 72). The `WARN_CSV_MISMATCH` verdicts seen on 8 of 72 cam-recordings (concentrated in the later rapid-succession 4-camera 1080p/720p sessions) are the same already-documented genuine camera/USB timing jitter detection, not a defect.
>
> **Two real gaps found and fixed, both about the app's own self-reported diagnostics being less accurate than the underlying functional behavior (which was already safe):**
>
> 1. **`VideoEngineDiagnostics.ProbeDirect3D11()`** only checked whether `d3d11.dll` exists in System32 — true on every Windows 7 SP1+ machine regardless of whether the actual GPU/driver can create a usable Direct3D 11 device. This fed directly into `HardwareEncoderAvailable`/`ReadwriteEnableHardwareTransforms` and the renderer/encoder diagnostics this project has repeatedly relied on to judge real GPU capability on a given machine. The real preview renderer and encoder already have safe fallback paths independent of this probe (D3D11 device creation is wrapped in try/catch with a WPF fallback; Media Foundation's software-MFT fallback is automatic regardless of `ReadwriteEnableHardwareTransforms`), so this was never a functional/crash risk — but on a machine with a genuinely non-functional GPU driver, logs and metadata would still falsely report `d3d11=Available`/`hardwareEncoderUsed=true`. Fixed: the probe now attempts real device creation with the same adapter, driver type, and feature levels (`Level_11_0`/`Level_10_1`) the actual renderer uses, immediately disposing the device, so diagnostics genuinely reflect what the hardware can do.
> 2. **`runtime\ffmpeg\ffprobe.exe` was only staged into `dist\`** by the release build script (`scripts\build\stage_dist_runtime.ps1`), never copied into a plain `dotnet build`/F5 output. This doesn't affect automatic per-recording metadata (which has never called ffprobe since v1.2.22-alpha, by design, to keep the recording-stop path free of synchronous external-process I/O) — but it does affect the Video Verification page's on-demand ffprobe audit feature, which silently falls back to a reduced "V2 standalone" mode with a warning banner when ffprobe isn't found next to the running exe. Fixed: `MultiCamApp.csproj` now copies `ffprobe.exe`/`FFMPEG_LICENSE.txt` from the project-root `runtime\ffmpeg\` folder into any build output (guarded by `Exists(...)` so checkouts without the vendor tools staged still build), matching what the packaged release already ships.
>
> Confirmed by code review (not yet re-tested on distinct hardware): the D3D11 preview and Media Foundation encoder fallback paths are already sound for machines without a capable/dedicated GPU — device creation failures fall back to WPF/CPU rendering, and MF's topology resolver automatically selects a software H.264 MFT when no hardware transform is available. The `SetPreviewFpsLimit`/GPU-preview-unthrottled split (only WPF/CPU-fallback slots are frame-rate-capped during multi-camera recording) was also verified correctly implemented — `Direct3DPreviewRenderer.PresentFrame` routes to the GPU render thread before the throttle check is ever reached.

### Fixed
* **`capture/video_engine_v2/VideoEngineDiagnostics.cs`** — `ProbeDirect3D11()` now performs a real `D3D11CreateDevice` probe (matching the production renderer's adapter/feature-level selection) instead of only checking for `d3d11.dll`'s existence.
* **`MultiCamApp.csproj`** — added a conditional `Content Include` so `ffprobe.exe`/`FFMPEG_LICENSE.txt` are copied into any build output, not just the packaged release, fixing the Video Verification page's ffprobe audit on dev builds.

### Tests
* Updated `BackendVersion` assertion to `1.2.77`. 295 tests passing (unchanged).

## [v1.2.76] - 2026-07-05 (build 296)

> **Full consistency audit of every Advanced Camera Controls / Video Settings / Session / Preview-Recording UI control against its backend wiring, per user request.** Resolution/FPS dropdowns, camera target selector, exposure controls (auto/manual/index/low-light/apply/default), environmental lock, session naming/output folder, and preview/recording/status controls were all traced end-to-end — all confirmed correctly bound and wired, and unaffected by the v1.2.68-75 Stop Preview crash/deadlock fixes.
>
> **One real bug found and fixed**: `LoadPerCameraFocusToUi()` never recomputed the Manual Focus slider's enabled state, unlike its exposure counterpart `LoadPerCameraExposureToUi()` (which already calls `UpdateManualExposureAvailability()` at the end). This meant clicking **Default Focus** while Auto Focus was checked correctly unchecked the box and reset the stored value, but left the Manual Focus slider/textbox stuck disabled — the user had to switch camera target or manually re-toggle Auto Focus to unstick it. `ApplyFocusSettingButton_Click` already called `UpdateManualFocusAvailability()` explicitly, so only the Default-Focus path was affected.
>
> **Noted, not changed**: `ConfirmPreRecordSettingsOrCancel()` (a resolution/FPS mismatch warning dialog) is fully implemented but never called from `StartRecordBtn_Click`. Traced its data source (`GetPreRecordSettingsMismatch()` → `GetLayoutRecordingSlots()`) to the legacy `CameraSlotPipeline` (STABLE_CORE_V1) objects, which are never opened while VideoEngineV2 is the active engine — the same pre-existing V1-only-no-op pattern already documented for `ReapplyCaptureSettingsToActivePreviewAsync`. Wiring the call would have zero effect under V2 (the mismatch list is always empty), so it was left as-is rather than treated as a functional gap; rebuilding a V2-native equivalent would be a new feature, not a wiring fix.

### Fixed
* **`MainWindow.xaml.cs`** — `LoadPerCameraFocusToUi()` now calls `UpdateManualFocusAvailability()` after loading per-camera focus state, matching the existing exposure-control pattern. Fixes the Manual Focus slider staying disabled after clicking Default Focus.

### Tests
* Updated `BackendVersion` assertion to `1.2.76`. 295 tests passing (unchanged).

## [v1.2.75] - 2026-07-05 (build 295)

> **Full audit of 12 test sessions / 43 recordings (post-v1.2.74) + housekeeping check on the Video Verification page, installer, and package references, per user request.** The batch confirmed v1.2.74's crash and deadlock fixes are holding (zero critical/warning freezes, zero errors), all videos valid with correct BT.709 color tags across 1080p/720p/480p and 15/30/60fps, zero true duplicate/ghost frames (verified via exact frame-hash comparison after an initial `mpdecimate` pass gave misleading perceptual-similarity false positives), and 100% GPU-rendered/hardware-encoded. Installer (`MultiCamApp.iss`) and package references (`Vortice.*` 3.8.3, `OpenCvSharp4` 4.10.0.20240616) were already fully consistent with the current version — no changes needed there.
>
> **One real gap found and fixed in the Video Verification page**: `MainWindow.xaml.cs` has computed a `frameIntegrity.integrityVerdict` (`PASS_WITHIN_TOLERANCE` / `WARN_CSV_MISMATCH` / `CSV_UNAVAILABLE`) for every recording since long before this session, but nothing in `V2MetadataReader.cs` or `V2VerificationRunner.cs` ever read it back — the app was correctly detecting real camera/USB timing jitter (confirmed in this audit: a `WARN_CSV_MISMATCH` verdict tracked precisely with genuine frame-interval instability on one physical camera under high-fps/high-res load) but never surfacing that verdict anywhere in the tool built specifically to show users whether their recordings are scientifically trustworthy.

### Fixed
* **`verification/V2MetadataReader.cs`** — now parses `frameIntegrity.integrityVerdict`/`csvRowsMatchFrames`/`csvRowsDiff` from the per-camera metadata JSON (previously written but never read).
* **`verification/V2VerificationRunner.cs`** — `BuildV2EngineDetailSection` now displays the frame-integrity verdict, the CSV-vs-frames row difference, and a plain-language explanation when `WARN_CSV_MISMATCH` fires (real camera/USB timing jitter, not data loss) in the Video Verification page's per-camera detail panel.

### Tests
* Updated `BackendVersion` assertion to `1.2.75`. 295 tests passing (unchanged).

## [v1.2.74] - 2026-07-05 (build 294)
## [v1.2.74] - 2026-07-05 (build 294)

> **v1.2.73 stopped the crash but introduced a genuine deadlock: clicking Stop Preview froze the whole app, with only the first camera slot's teardown completing before the UI became fully unresponsive.** Checked recorded videos again — all valid, correctly tagged, unaffected as always. The runtime log confirmed a real deadlock, not another crash: `UI_FREEZE_MINOR` escalated through `UI_FREEZE_WARNING` to `UI_FREEZE_CRITICAL` (3107ms), then the block eventually resolved roughly 34 seconds later — the classic signature of an async-over-sync deadlock rather than a native fault.
>
> **Root cause:** v1.2.73 fixed the STA-thread-affinity crash by wrapping the capture-teardown block in a *synchronous* `Dispatcher.Invoke(() => { ...GetAwaiter().GetResult()... })`. But `MediaFoundationCaptureService.StopAsync()`'s own internal `await _frameReader.StopAsync().AsTask(ct)` has no `ConfigureAwait(false)` — so once called from inside that `Dispatcher.Invoke` callback (where `SynchronizationContext.Current` is the WPF dispatcher's own context), its continuation tries to `Post()` back onto the exact same UI thread that is synchronously blocked in `.GetResult()` waiting for it. Textbook async-over-sync deadlock: the continuation can never run because the thread it needs to run on is stuck waiting for it.
>
> **Fix:** replaced the synchronous `Dispatcher.Invoke` + `.GetAwaiter().GetResult()` pattern with `Dispatcher.InvokeAsync(async () => { ...await... }).Task.Unwrap()`. The delegate still runs on/resumes on the UI thread (preserving the STA-thread-affinity guarantee v1.2.71-73 established), but this genuinely awaits rather than blocking the thread's message loop — so posted continuations can actually execute, closing the deadlock without reopening the original crash.

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `StopPreviewAsync`'s capture-teardown block now uses non-blocking `Dispatcher.InvokeAsync(...).Task.Unwrap()` instead of a synchronous `Dispatcher.Invoke` wrapping `.GetAwaiter().GetResult()`, eliminating a real deadlock introduced by v1.2.73's fix for the STA-thread-affinity crash.

### Tests
* Updated `BackendVersion` assertion to `1.2.74`. 295 tests passing (unchanged).

## [v1.2.73] - 2026-07-05 (build 293)
## [v1.2.73] - 2026-07-05 (build 293)

> **The v1.2.72 diagnostic logging delivered a definitive, unambiguous answer: v1.2.71's STA-thread-affinity theory was correct in mechanism but the fix itself had a bug, and this release corrects it.** The log showed: `V2_STOP_PREVIEW_THREAD_CHECK ... wasOffUiThread=False managedThreadId=1` (confirming execution WAS on the UI thread, thread 1, at that check) — immediately followed by `V2_CAPTURE_DISPOSE_MEDIACAPTURE_BEGIN ... managedThreadId=37`, a **different thread**, right before the crash. Checked recorded videos a 5th time: all 4 valid, correctly tagged — as always, the crash never touches recording integrity.
>
> **The actual bug in v1.2.71's fix**: it checked `_uiDispatcher.CheckAccess()` exactly once, and when that returned `true` (already on the UI thread), it took an "already safe" branch that called `_captureService.StopAsync(ct).ConfigureAwait(false)` directly — but that `.ConfigureAwait(false)` is exactly what let the continuation hop onto a thread-pool thread (thread 37) by the time `MediaCapture.Dispose()` ran moments later. The one-time check was correct; the branch it selected for the "already on the UI thread" case then undid that safety itself.
>
> **Fix:** the capture-teardown block now *always* runs inside a synchronous `Dispatcher.Invoke(...)`, with no conditional fallback to a `ConfigureAwait(false)` path. This is safe to call unconditionally — WPF's `Dispatcher.Invoke`, when already called from its own thread, simply executes the delegate in place with no marshaling or deadlock risk — so there is no longer any code path that can let `MediaCapture.Dispose()` drift off the UI thread.

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `StopPreviewAsync`'s capture-teardown block always runs inside `Dispatcher.Invoke(...)` now (previously conditional, with a still-unsafe fallback path that reintroduced the exact bug the condition was meant to prevent).

### Tests
* Updated `BackendVersion` assertion to `1.2.73`. 295 tests passing (unchanged).

## [v1.2.72] - 2026-07-05 (build 292)
## [v1.2.72] - 2026-07-05 (build 292)

> **The v1.2.71 STA-thread-affinity fix did NOT stop the crash — the user re-tested and it crashed a fourth time, at the exact same log line as before (`V2_CAPTURE_DISPOSE_MEDIACAPTURE_BEGIN`, no `_END`).** Checked recorded videos from this session again: all 4 valid, correctly tagged — unaffected, as with every prior occurrence. This means the `Dispatcher.Invoke` fix either didn't change anything (i.e., the code was already running on the UI thread the whole time, disproving the ConfigureAwait(false)-migration hypothesis) or something else entirely is at play. Rather than guess again, this release does two things: adds a diagnostic line that will prove definitively whether the dispatcher-redirect branch even engaged (`V2_STOP_PREVIEW_THREAD_CHECK`, logging `wasOffUiThread` and the managed thread ID, cross-checked against a matching thread-ID log now added at the actual `MediaCapture.Dispose()` call site) — and applies one additional concrete, low-risk fix candidate.
>
> **New fix candidate**: `CameraControlManagerV2.AttachAsync` (called in `OpenAsync`) stores a reference to `MediaCapture.VideoDeviceController` for the camera-control features (focus/exposure/white-balance/etc.), and a `Detach()` method already existed to release it — but nothing in `StopPreviewAsync` ever called it. Every Stop Preview ran `MediaCapture.Dispose()` while this class still held an external reference to `MediaCapture`'s own `VideoDeviceController` sub-object. `StopPreviewAsync` now calls `_controlManager.Detach()` immediately before capture teardown, on the theory that leftover references to a WinRT COM object's own sub-objects can interfere with that object's `Dispose()`/`Close()` on some driver stacks.

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `StopPreviewAsync` now calls `_controlManager.Detach()` before the capture-teardown block, releasing the `VideoDeviceController` reference before `MediaCapture.Dispose()` runs.

### Added
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — new `V2_STOP_PREVIEW_THREAD_CHECK` diagnostic line logs whether the v1.2.71 dispatcher-redirect actually engaged (`wasOffUiThread`) plus the managed thread ID at that point.
* **`capture/video_engine_v2/MediaFoundationCaptureService.cs`** — `V2_CAPTURE_DISPOSE_MEDIACAPTURE_BEGIN` now also logs its managed thread ID, so it can be directly cross-checked against the thread-check line above to confirm or refute whether the dispatcher redirect actually changed which thread `MediaCapture.Dispose()` runs on.

### Tests
* Updated `BackendVersion` assertion to `1.2.72`. 295 tests passing (unchanged).

## [v1.2.71] - 2026-07-05 (build 291)
## [v1.2.71] - 2026-07-05 (build 291)

> **The v1.2.70 diagnostic logging did its job: the next crash pinpointed the exact native call, and this release fixes it.** The log's last line before the process vanished was `V2_STOP_PREVIEW_CAPTURE_CLOSE_BEGIN` → `V2_CAPTURE_DISPOSE_MEDIACAPTURE_BEGIN` — no `_END` line ever followed. The fault is `MediaCapture.Dispose()` itself, called from inside `MediaFoundationCaptureService.CloseAsync()`. Recorded videos from this crash session were checked again and are unaffected (valid H.264, correct resolution/frame counts/color tags) — confirming, a second time, that this bug is confined to Stop Preview teardown.
>
> **Root cause, finally isolated:** `MediaFoundationCaptureService`'s own doc comment states `MediaCapture` must be opened and started from a UI-thread (STA apartment) context — and `OpenAsync` respects that (`CameraPipelineV2.OpenAsync` calls it with no `.ConfigureAwait(false)`, preserving the UI `SynchronizationContext`). But `StopPreviewAsync`'s teardown path does the opposite: `_timestampMonitor.StopAsync().ConfigureAwait(false)` runs first (added in v1.2.64 to fix an unrelated UI freeze), which discards the UI thread's `SynchronizationContext` for the rest of the method — so by the time `_captureService.StopAsync()`/`CloseAsync()` run afterward, they (and the `DisposeReader()`/`DisposeCapture()` calls inside them, including `MediaCapture.Dispose()`) execute on a **thread-pool thread, not the original UI/STA thread `MediaCapture` was created on**. Disposing an STA-affine WinRT COM object from the wrong thread/apartment is a well-documented source of native crashes with no managed exception to catch — exactly this bug's signature across v1.2.66/68/69/70, and also inadvertently disproving a premise of the v1.2.69 fix: `MediaFoundationCaptureService.OpenAsync` was checked directly this time, confirming no D3D11 device is actually shared with `MediaCapture` at all (`MemoryPreference` is hardcoded to `Cpu`; the desktop WinRT projection doesn't expose `Direct3D11Device` on `MediaCaptureInitializationSettings`) — so the "shared device race" v1.2.69 targeted was a real ordering improvement but not, in fact, the mechanism behind this exact crash.
>
> **Fix:** `CameraPipelineV2.StopPreviewAsync` now forces the `_captureService.StopAsync()`/`CloseAsync()` block back onto the UI dispatcher via a synchronous `Dispatcher.Invoke(...)` call before running it — the same pattern `Direct3DPreviewRenderer.Dispose()` already uses successfully for its own D3D11 render-thread teardown (proven working by the "D3D11_HOST: destroyed" log line introduced in v1.2.69/70). This guarantees `MediaCapture`/`MediaFrameReader` are always disposed on the exact thread/apartment they were created on, regardless of what `ConfigureAwait(false)` awaits ran earlier in the same method.

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `StopPreviewAsync` now captures the UI `Dispatcher` (via a new `_uiDispatcher` field set in `Initialise`) and, if execution has migrated off that thread, synchronously re-marshals the `_captureService.StopAsync()`/`CloseAsync()` calls back onto it via `Dispatcher.Invoke(...)` before running them — fixing an STA-thread-affinity violation on `MediaCapture.Dispose()`. Also corrected a stale code comment that assumed a shared D3D11 device between the GPU renderer and `MediaCapture` (verified this session that no such sharing actually occurs).

### Tests
* Updated `BackendVersion` assertion to `1.2.71`. 295 tests passing (unchanged).

## [v1.2.70] - 2026-07-05 (build 290)
## [v1.2.70] - 2026-07-05 (build 290)

> **The v1.2.69 fix was verified partially effective, but the crash still occurred a third time.** The runtime log proved real progress: `D3D11_HOST: destroyed` appeared for the first time ever in this investigation, confirming the v1.2.69 GPU render-thread teardown fix is working — at least one camera's swap chain host now tears down cleanly. But the app still crashed shortly after, again with zero further log lines and no `crash.log`, this time with nothing pointing to the GPU renderer as the culprit. **Recorded videos from this session were checked and are unaffected**: all 4 `cam*.mp4` files are valid H.264, correct 1920×1080 resolution, correct frame counts/durations matching their metadata, and correctly BT.709/limited-range color-tagged — the crash is confined to the post-recording Stop Preview teardown path, not the recording pipeline itself.
>
> **This is the third native-fault occurrence in the same "Stop Preview after a completed recording, GPU renderer active" scenario** (v1.2.66, v1.2.68, v1.2.69 each fixed a distinct, real, verified root cause in this area — MF ref-counting, then GPU render-thread lifetime — yet the symptom persists). Rather than guess a fourth mechanism without better evidence, this release adds fine-grained diagnostic checkpoints around every remaining native call in the teardown path — `MediaFrameReader.StopAsync()`, `MediaFrameReader.Dispose()`, and `MediaCapture.Dispose()` (previously the only three native calls left in this path with zero diagnostic logging around them) — so that whichever line is the LAST one logged before the next crash narrows the fault down to one specific native call instead of a whole method. No behavioral fix is claimed in this release; it is diagnostic instrumentation only, to make the next crash (if it recurs) immediately actionable instead of requiring another round of code-reading speculation.

### Added
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `StopPreviewAsync` now logs `V2_STOP_PREVIEW_BEGIN`/`_RENDERER_DISPOSE_BEGIN`/`_END`/`_CAPTURE_STOP_BEGIN`/`_END`/`_CAPTURE_CLOSE_BEGIN`/`_END` checkpoints, tagged with the camera's device name, bracketing every remaining step of the teardown sequence.
* **`capture/video_engine_v2/MediaFoundationCaptureService.cs`** — `StopAsync`/`CloseAsync` now log checkpoints around `MediaFrameReader.StopAsync()`, `MediaFrameReader.Dispose()`, and `MediaCapture.Dispose()` individually — previously these three native calls had no diagnostic logging at all.

### Tests
* Updated `BackendVersion` assertion to `1.2.70`. 295 tests passing (unchanged).

## [v1.2.69] - 2026-07-05 (build 289)
## [v1.2.69] - 2026-07-05 (build 289)

> **The v1.2.68 fix was verified NOT sufficient — the user re-tested and Stop Preview still crashed the app.** Confirmed via the runtime log that v1.2.68's fix *did* work as intended (`MF_RUNTIME_SHUTDOWN` correctly did not fire prematurely this time), but the app still vanished mid-`StopV2DefaultPipelineAsync()` with zero further log lines and no `crash.log` — same native-fault signature, but a different, additional root cause.
>
> **Root cause:** the GPU swap-chain renderer (`D3D11SwapChainHost`)'s dedicated render thread and its D3D11 device/swap chain were never torn down by Stop Preview at all — `CameraPipelineV2.StopPreviewAsync` only called `_previewRenderer.StopRendering()` (which just sets a flag; new frames stop being accepted, but the render thread itself keeps running, idling on a 50ms wait loop). The actual teardown (`Direct3DPreviewRenderer.Dispose()` → `D3D11SwapChainHost.Dispose()` → `DestroyWindowCore` → join render thread → release D3D11 device/swap chain) only ran the *next* time that camera slot's Start Preview replaced the whole pipeline object (`VideoEngineV2.PrepareSlotPreviewAsync` disposes the previous pipeline first) — never at Stop Preview itself. That left the render thread alive, still holding the exact same D3D11 device `MediaCapture` was initialised with for zero-copy GPU frame delivery, for the entire time between Stop Preview and the next Start Preview (or app exit) — a window during which the render thread's `Present()`/`CopyResource()` calls could run concurrently with whatever `MediaCapture.Close()` (called right after, inside the same `StopPreviewAsync`) does internally to that same shared device. A native race with no managed exception to catch.
>
> **Fix:** `CameraPipelineV2.StopPreviewAsync` now fully disposes the preview renderer (stopping and joining the GPU render thread, releasing the shared D3D11 device) *before* calling `_captureService.StopAsync()`/`CloseAsync()`, guaranteeing no concurrent D3D11 access between the two subsystems during teardown. Safe because each `CameraPipelineV2` instance is single-use for preview — Start Preview always constructs a fresh one, so nothing needs this renderer to survive Stop Preview. Also added `DeviceCreationFlags.VideoSupport` to the shared D3D11 device's creation flags (previously only `BgraSupport`) as defensive hardening, since some driver stacks expect it for a device that's handed to `MediaCaptureInitializationSettings.Direct3D11Device`.

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `StopPreviewAsync` now calls `_previewRenderer.Dispose()` (full GPU render-thread + D3D11 teardown) before closing the capture session, instead of leaving the render thread running until the next Start Preview.
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — added `DeviceCreationFlags.VideoSupport` alongside the existing `BgraSupport` flag when creating the shared D3D11 device.

### Tests
* Updated `BackendVersion` assertion to `1.2.69`. 295 tests passing (unchanged).

## [v1.2.68] - 2026-07-05 (build 288)

> **Fixed a real app crash the user hit on real hardware: clicking Stop Preview after a completed 4-camera recording crashed the app outright, with no exception logged anywhere — not even `crash.log`.** Same diagnostic signature as the v1.2.66 crash (native-level fault bypassing every .NET exception handler), traced to a genuine design gap in the v1.2.65 VFR migration's `MediaFoundationRuntime` ref-counted `MFStartup`/`MFShutdown` wrapper.
>
> **Root cause:** `MediaFoundationRuntime.AddRef()`/`Release()` were only ever called by `MediaFoundationEncoderService` (the recording encoder) — never by the capture side (`CameraPipelineV2`'s WinRT `MediaCapture`/`MediaFrameReader`, which is itself built on Media Foundation just like the encoder). The runtime log confirmed it: `MF_RUNTIME_SHUTDOWN` fired the instant the *last active recording* finalized — while all 4 cameras kept right on previewing afterward on `MediaCapture` sessions that still depended on the now-globally-torn-down MF platform. The very next MF-dependent operation on those sessions — `StopPreviewAsync`'s `_captureService.CloseAsync()`, triggered by the Stop Preview click — then crashed natively, since it was trying to tear down capture objects whose underlying platform had already been shut down out from under them.
>
> This reproduces with **any layout (1–4 cameras)**: it isn't specific to 4-camera sessions, just easiest to trigger there since every recording session (regardless of camera count) releases the encoder's MF ref the moment its *last* active recording stops — the bug fires whenever preview continues (in any layout) after every currently-recording camera has finished, which was already the ordinary post-recording state for every completed test in this session's history. Only this specific interaction sequence (finish recording → keep previewing → click Stop Preview) had not been exercised right before now.
>
> **Fix:** `CameraPipelineV2` now also holds an `MediaFoundationRuntime` ref for the entire time its camera is open — acquired in `OpenAsync` (before the capture session opens) and released only after `StopPreviewAsync`'s `_captureService.CloseAsync()` finishes (or in `Dispose()`, as an abnormal-teardown safety net). Since the same static ref-counted wrapper now has holders from both the capture side and the encoder side, `MFShutdown()` can only fire once every camera is genuinely closed — never merely because the *last recording* (as opposed to the last *camera*) finished.
>
> **Also audited** (per the user's request) Start Preview, Stop Preview, Start Recording, and Stop Recording across the relevant shared code paths for all 1–4 camera layouts — `StartV2DefaultPipelineAsync`/`StopV2DefaultPipelineAsync`/`StopAllSlotsPreviewAsync` all loop over the actual active layout size or only touch slots with a live pipeline, so no separate layout-count-specific defect was found beyond the MF ref-counting bug above (which is itself layout-agnostic and now fixed for all layouts).

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — added an MF platform ref held for the camera's whole open lifetime (`OpenAsync` → `StopPreviewAsync`/`Dispose`), independent of the encoder's own recording-lifetime ref in `MediaFoundationEncoderService`. Prevents `MediaFoundationRuntime.Release()` reaching zero (and calling `MFShutdown()`) while any camera is still open/previewing, even after every active recording has finished.

### Tests
* Updated `BackendVersion` assertion to `1.2.68`. 295 tests passing (unchanged).

## [v1.2.67] - 2026-07-05 (build 287)

> **Full-app audit** covering UI controls, EN/JA localization, exported metadata (JSON/TXT), FPS/resolution lists, advanced camera controls, the Video Verification page, scripts/packages/installer, the logging system, and directory structure. Four parallel research passes confirmed most of the app is consistent post-v1.2.66; found and fixed the following real gaps.

### Fixed
* **BT.709 color tagging (v1.2.66 Phase 2) was applied to every recorded MP4 but never surfaced anywhere a user could see it** — not in the exported per-camera metadata JSON/TXT, not in `BackendMetadata`, not on the Video Verification page. A user reading only the app's own output had no way to confirm the color tags the encoder was silently applying. Added `ColorTaggingApplied`/`ColorPrimaries`/`ColorTransferFunction`/`ColorMatrix`/`ColorRange` to `capture/backend/BackendIdentifiers.cs`'s `BackendMetadata` record, populated them in `VideoEngineRegistry.BuildMetadata`, exposed them in the per-camera metadata TXT (new "Color tagging applied" / "Color primaries" / "Color transfer function" / "Color matrix" / "Color range" lines, EN+JA) and JSON (`backendInfo.colorTaggingApplied` etc.) in `MainWindow.xaml.cs`, taught `verification/V2MetadataReader.cs` to parse the new `backendInfo` fields, and added a "Color tagging" line to the per-camera detail section `verification/V2VerificationRunner.cs` builds for the Video Verification page.
* **`VideoEngineRegistry.cs`'s `ManualExposureUiAvailable`/`ManualFocusUiAvailable` metadata fields were hardcoded `false`**, even though `MainWindow`'s Advanced Camera Controls expander has always exposed manual exposure/focus sliders (`ManualExposureSlider`/`ManualFocusSlider`, wired to `CameraControlManagerV2`). A user reading exported metadata would see "manual focus UI: not available" while looking at the very UI that contradicted it. Corrected both to `true` (driver-level support is reported separately via the pre-existing `ExposureControlSupported`/`AutofocusControlSupported` fields, which this change does not touch).
* **Removed `recording/writers/MediaFoundationVideoFileWriter.cs`** — confirmed dead code with zero call sites (independently verified by two audit passes): a `LowLagMediaRecording`-based `IVideoFileWriter` implementation left over from before the v1.2.65 IMFSinkWriter migration, never instantiated anywhere in the active `VideoEngineV2` path. Removed a stale `<see cref>` to it in `recording/writers/IVideoFileWriter.cs`'s doc comment.

### Reviewed, no change needed
* Session-level `session_metadata.json`'s `schemaVersion` ("1.2.0") vs. per-camera `{slot}_metadata.json`'s ("1.3.0"): the version bump on the per-camera schema specifically marked the `constantFrameRateTarget` → `frameTimingMode` field rename (v1.2.65); the session-level schema never had that field and its shape hasn't changed, so its version number is accurate as-is.
* FPS UI list (15/30/60) vs. `MainViewModel`'s `Math.Clamp(fps, 5, 60)`: the wider backend floor only matters for values the UI never sends (5–14 fps); it's a deliberately permissive guard for config-file-set values, not a UI/backend mismatch bug.
* `metadata/CameraRecordingMetadata.cs`'s `RecordingApi` default (`"LowLagMediaRecording"`) and `recording/RecordingSession.cs`'s conditional `RecordingApi` assignment: both are in the legacy OpenCV/DirectShow pipeline protected by `STABLE_CORE_V1` (see `docs/STABLE_CORE_V1_FREEZE.md`), always overwritten at runtime with the value describing whichever engine actually ran, and don't meet any freeze-exception trigger — left untouched per freeze policy.
* Package references (`Vortice.Direct3D11`/`Vortice.DXGI`/`Vortice.MediaFoundation` all 3.8.3, `OpenCvSharp4`/`OpenCvSharp4.runtime.win` both 4.10.0.20240616), the installer's `[Files]` wildcard inclusion, `build_release.bat`'s version.json-sourced versioning, and the logging system's 2 MB rotation / 20-file retention were all confirmed correct as-is.

### Tests
* Updated `BackendVersion` assertion to `1.2.67`. 295 tests passing (unchanged).

### Documentation (follow-on pass, same release, no app version bump)
Full sweep of every project `.md`/`.cff` doc for staleness against the current v1.2.67 state, following the same audit request:
* **Fixed a real packaging bug**: `scripts/packaging/create_release_zip.ps1` shipped `docs/changelogs/CHANGELOG.md` inside `installer.zip` — but that file is a stale archive only touched by the (unused-in-practice) `bump_version.py` tool, frozen at v1.2.30-alpha (2026-07-02), while every actual release since has been hand-documented in the root `CHANGELOG.md`. End users downloading `installer.zip` were getting a changelog missing v1.2.31 through v1.2.67. Fixed to copy the root `CHANGELOG.md` instead; `installer.zip` regenerated and verified to contain the current file.
* Corrected three doc references that pointed at that same stale `docs/changelogs/CHANGELOG.md` (`docs/developer_notes/versioning.md`, `docs/STABLE_CORE_V1_REGRESSION_CHECKLIST.md`, `docs/user_guide/video_verification.md`) to point at the root `CHANGELOG.md`, and documented the divergence in `versioning.md` so it isn't re-introduced.
* Fixed stale `v1.2.30-alpha`/`1.2.30-alpha` version strings left over from a 2026-07-02 docs pass in `README.md` (banner + citation), `INSTALLATION.md` (citation), `CITATION.cff`, and `docs/developer_notes/versioning.md`'s example `version.json` (which also still showed the `-alpha` suffix removed since v1.2.33 — corrected).
* `docs/architecture/overview.md` still described the recording encoder as WinRT `LowLagMediaRecording`; updated to describe the actual v1.2.65+ raw `IMFSinkWriter` pipeline and v1.2.66 color tagging.
* `docs/OUTPUT_FILES_AND_METADATA.md` and `docs/user_guide/video_verification.md` updated to document the new `frameTimingMode`/`backendInfo.color*` metadata fields and the Video Verification page's new color-tagging detail line (both added earlier in this same release).
* `THIRD_PARTY_NOTICES.md` and `SECURITY.md` were missing any mention of `Vortice.Direct3D11`/`Vortice.DXGI`/`Vortice.MediaFoundation` (bundled since v1.2.59/v1.2.65) — added (MIT license, Amer Koleci / Vortice.Windows).
* `docs/user_guide/hardware_diagnostics.md` still listed "WPF preview renderer replacement" as a future deferred item; the GPU-accelerated Direct3D11/Vortice preview renderer shipped in v1.2.59 — corrected.
* `docs/user_guide/video_verification.md`'s "Release ZIP contents" section conflated `Setup.exe`'s own bundled files (just the app + `THIRD_PARTY_NOTICES.md` + `LICENSE.txt`) with `installer.zip`'s wrapper contents (`Setup.exe` + `README.md`/`INSTALLATION.md`/`LICENSE.md`/`THIRD_PARTY_NOTICES.md`/`DIRECTORY_STRUCTURE.md`/`CITATION.cff`/`SECURITY.md`/`CHANGELOG.md`/user-guide docs) — corrected to describe both accurately, verified against `installer/MultiCamApp.iss`'s `[Files]` section and `scripts/packaging/create_release_zip.ps1`.
* Added forward-pointing "resolved" notes to two SUPERSEDED historical planning docs (`docs/windows_camera_behavior_study.md`, `tools/CameraCapabilityScanner/README.md`) noting that the `IMFSinkWriter`/color-tagging investigation they had flagged as future work was in fact completed in v1.2.65/66.
* Vendored third-party docs under `tools/dotnet/`, `tools/nuget-packages/`, `tools/python/` were left untouched (not ours to edit); `docs/changelogs/CHANGELOG.md` itself was left as the (now clearly documented) historical archive rather than backfilled, since backfilling it would just create a second changelog to keep in sync going forward.

## [v1.2.66] - 2026-07-05 (build 286)

> **Fixed a real app crash the user hit on real hardware right after the v1.2.65 VFR migration shipped: changing the FPS/resolution dropdown crashed the app outright, with no exception logged anywhere — not even in the dedicated `crash.log` safety net that catches every other unhandled-exception path in this codebase.** That absence was the key clue: it meant the crash bypassed `AppDomain.UnhandledException`, `DispatcherUnhandledException`, and `TaskScheduler.UnobservedTaskException` alike, which is the signature of a native-level fault (COM/interop corruption), not an ordinary .NET exception. Root-caused to two real bugs introduced by the VFR migration:
> 1. **`CameraPipelineV2.OnFrameArrived`'s new pixel-extraction block (added for encoder frame submission) had zero exception handling** — unlike every other frame-thread code path in this codebase. It runs on the `MediaFrameReader` callback thread, not the WPF UI thread, so an exception there was never caught by WPF's dispatcher handler.
> 2. **`MediaFoundationEncoderService.SubmitFrame` sized its destination buffer from `frame.Height` (the WinRT-negotiated format's reported height) instead of the actual bitmap's height the byte array was sized for** — these can momentarily disagree while a resolution/FPS change is renegotiating the capture format, which is exactly what the user was doing when it crashed.
> 3. **`IMFSinkWriter` access was never synchronized** — `SubmitFrame` (frame-arrived thread) and `FinaliseAsync`/`Dispose` (UI/thread-pool thread) could race on the same underlying COM object with no lock, and `IMFSinkWriter` is not documented as safe for concurrent calls (a risk this migration's own plan had flagged but not yet mitigated).
>
> Also found and fixed a real regression in the same investigation: **the user reported Start Recording still looked delayed even after the v1.2.64 dispatcher-congestion fix — and it had gotten measurably worse (freeze escalating all the way to `UI_FREEZE_CRITICAL`, ~3s) in the real test session.** Cause: `MediaFoundationEncoderService.OpenAsync`'s new Sink Writer setup (`AddStream`/`SetInputMediaType`/`BeginWriting`, which negotiates/instantiates the encoder MFT) is *entirely synchronous COM work with no real await point* — unlike the WinRT `PrepareLowLagRecordToStorageFileAsync` call it replaced, which was a genuine async operation. This meant `Task.WhenAll` across the 4 camera slots' `StartOneSlotRecordingAsync` calls no longer achieved real concurrency: each camera's encoder setup fully completed (blocking the calling thread) before the next camera's even started, serializing work that used to overlap. Fixed by wrapping `OpenAsync`'s COM setup in `Task.Run(...)`.
>
> **Implemented Phase 2 (color tagging), per the user's explicit choice to match Windows Camera exactly** (limited/tv range, over keeping today's full/pc range with only primaries/transfer/matrix fixed). Verified the exact native `MFVideoPrimaries`/`MFVideoTransferFunction`/`MFVideoTransferMatrix`/`MFNominalRange` integer values against the real Windows SDK header (`um/mfobjects.h`) rather than trusting memory/convention — this caught real mistakes before they shipped: `MFVideoPrimaries_BT709` is `2` (not `4`), `MFVideoTransFunc_709` is `5` (not `4`), and `MFNominalRange_16_235` is `2` (not `1`). Verified end-to-end with a standalone smoke test before touching the real codebase: ffprobe on the test output confirmed `color_primaries=bt709`, `color_transfer=bt709`, `color_space=bt709`, `color_range=tv` — genuinely working, unlike the v1.2.27 attempt (which used the WinRT `VideoEncodingProperties.Properties` bag at the wrong layer and never propagated into the muxed output, confirmed via ffprobe and reverted in v1.2.28).

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — wrapped the encoder pixel-extraction block in `OnFrameArrived` in a try/catch (logs `V2_ENC_SUBMIT_ERROR` and drops the frame instead of crashing the process); `SubmitFrame` call now passes the actual bitmap height explicitly instead of relying on the frame's WinRT-reported height.
* **`capture/video_engine_v2/MediaFoundationEncoderService.cs`** — `SubmitFrame` now takes an explicit `height` parameter and validates the destination buffer size against the actual pixel array length before copying (drops and logs `V2_ENC_SUBMIT_SIZE_MISMATCH` instead of risking an out-of-bounds native copy); all `_sinkWriter` access (`SubmitFrame`, `FinaliseAsync`, `Dispose`, and publishing a newly-configured writer in `OpenAsync`) now serialized under a single lock; `OpenAsync`'s Media Foundation setup now runs inside `Task.Run(...)` so `Task.WhenAll` across camera slots achieves real concurrency again; added `ApplyColorTags` (BT.709 primaries/transfer/matrix + limited-range, applied to both input and output media types).

### Tests
* Updated `BackendVersion` assertion to `1.2.66`. 295 tests passing (unchanged).

## [v1.2.65] - 2026-07-05 (build 285)

> **Replaced WinRT `LowLagMediaRecording` with a raw Media Foundation `IMFSinkWriter` pipeline (via `Vortice.MediaFoundation` 3.8.3) — real per-frame VFR timestamps instead of forced CFR container muxing.** The user's downstream analysis reads timing directly from the MP4 container, not the sidecar CSV — forced CFR was silently feeding it fabricated per-frame intervals. Every frame is now stamped with its real Media Foundation presentation timestamp (already the "ground truth" `FrameTimestampMonitor` writes to the CSV today), and `IMFSample.SampleDuration` is deliberately left unset so the muxer derives each frame's actual displayed duration from real consecutive timestamp deltas, not a fixed nominal rate.
>
> De-risked the same way as the earlier GPU migration: rather than trust documentation, downloaded and reflected the actual compiled `Vortice.MediaFoundation` 3.8.3 DLL to confirm every API signature used, then built and ran a full standalone smoke test (create sink writer → add H.264 stream → set BGRA8 input type → write 10 samples → finalize → verify with ffprobe) before touching the real codebase — it worked end-to-end on the first attempt. Also found and used the exact confirmed constants: `VideoFormatGuids.H264`/`Argb32` (both verified byte-for-byte against the well-known native GUIDs), `MediaTypeGuids.Video`, `MediaTypeAttributeKeys.MajorType`/`.Subtype`.
>
> This codebase had previously attempted something similar from scratch (`VideoEngineV3B`, hand-rolled, non-Vortice) which reached "real MP4 output, 1-camera-only" alpha before being fully deleted in v1.2.22-alpha for maintenance-simplicity reasons. This migration avoids repeating that outcome by rewriting the existing, already-isolated `MediaFoundationEncoderService.cs` in place (same public API, same single call site in `CameraPipelineV2.cs`) instead of adding a second, parallel backend.
>
> **Behavior change**: recordings are no longer capped at 3 hours (that was `MediaCapture`'s built-in WinRT limit, specific to `LowLagMediaRecording` — a raw `IMFSinkWriter` has no such cap).
>
> **Deliberately deferred to a fast-follow (Phase 2, not in this release)**: color tagging (`color_space`/`color_primaries` still read `unknown`). The v1.2.27 attempt to fix this via `VideoEncodingProperties.Properties` was confirmed non-functional and reverted in v1.2.28 because that WinRT property bag never reaches the muxed output — the correct fix is setting `MediaTypeAttributeKeys.VideoPrimaries`/`TransferFunction`/`YuvMatrix`/`VideoNominalRange` directly on the `IMFMediaType` the new Sink Writer already consumes, which is now possible but held back because matching Windows Camera exactly means switching from full-range (`pc`) to limited-range (`tv`) YUV, a real product decision (and possibly a genuine pixel-value change, not just a metadata tag) that needs user confirmation first.

### Changed
* **`capture/video_engine_v2/MediaFoundationEncoderService.cs`** — full rewrite in place against `IMFSinkWriter`: `OpenAsync` now builds the sink writer + H.264 output/BGRA8 input media types and calls `BeginWriting()`; new `SubmitFrame(V2FrameArrivedEventArgs, byte[], int)` writes one real-timestamped sample per frame (synchronous, called directly from the frame-arrived thread — deliberately not fire-and-forget, since `IMFSinkWriter.WriteSample` isn't documented safe for concurrent calls and out-of-order writes would corrupt the timestamp contract); `FinaliseAsync` runs `Finalize()` on a background thread (`ConfigureAwait(false)`, consistent with v1.2.64's fix). Same public API surface — zero signature changes needed at the `CameraPipelineV2.cs` call sites.
* **`capture/video_engine_v2/MediaFoundationRuntime.cs`** — new file: process-wide, ref-counted `MFStartup`/`MFShutdown` wrapper, since up to 4 concurrent camera slots each own a `MediaFoundationEncoderService` and a naive per-instance start/shutdown pair would tear down the shared MF platform state out from under sibling cameras still recording.
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `OnFrameArrived` now extracts raw BGRA8 bytes from `SoftwareBitmap` (same pooled-buffer/`CopyToBuffer` pattern `D3D11SwapChainHost.PresentFrame` already uses) and calls `_encoderService.SubmitFrame` before handing the bitmap to the preview renderer, replacing the old counter-only `NotifyFrameEncoded` call.
* **`MainWindow.xaml.cs`** — the hardcoded `Constant frame-rate target: Yes` metadata field (and its JSON equivalent `constantFrameRateTarget: true`) replaced with an honest `Frame timing mode: Variable (real per-frame timestamps)` (JSON: `frameTimingMode: "Variable"`, per-camera metadata JSON schema bumped to 1.3.0). Also fixed two now-stale strings referencing the removed `LowLagMediaRecording` by name (startup-jitter note, writer-queue-drops note).
* **`capture/backend/VideoEngineRegistry.cs`** — `EncoderApi` metadata field updated from `"LowLagMediaRecording (H.264 / MediaFoundation)"` to `"IMFSinkWriter (H.264 / MediaFoundation, Vortice.MediaFoundation)"`.
* **`MultiCamApp.csproj`** — added `Vortice.MediaFoundation` 3.8.3 package reference.

### Tests
* Updated `BackendVersion` assertion to `1.2.65`. 295 tests passing (unchanged).

## [v1.2.64] - 2026-07-05 (build 284)

> **Implemented the `ConfigureAwait(false)` fix flagged as a candidate in v1.2.63, after a 35-session systematic batch (every combination of 1-4 cameras × 480p/720p/1080p × 15/30/60fps) reproduced and precisely confirmed the root cause.** At 1080p/4-camera, 15fps recorded cleanly (start/stop both instant), but 30fps and 60fps both showed a multi-second freeze on **both** Start Recording and Stop Recording (30fps: ~5.7s start delay, escalating to `UI_FREEZE_CRITICAL`, plus a further ~1.8s stall on stop; 60fps: ~7.2s start delay, also critical) — reproducing on the very first and second recordings of a fresh app launch, ruling out the "builds up over consecutive sessions" theory from v1.2.62 entirely. Freeze severity scaled directly with requested fps, not session count.
>
> Confirmed via ffprobe that **the actual recorded video was never affected** — cam1's `Duration` metadata field for the 60fps session read 17.79s while its container was actually 24.08s (same as the other 3 cameras' ~23-24s, all consistent with each other) — proving the underlying recording ran the whole time; only the app's own bookkeeping (elapsed timer, per-camera `Duration` stopwatch, log lines) lagged behind by several seconds. This directly explains the user's separate report that "recording time on UI didn't work well" — the on-screen elapsed timer (`StartElapsedTimer`, called only after `Task.WhenAll` on all 4 slots' start-recording tasks completes) inherits the worst-case delay of whichever camera was slowest to confirm, so it visibly starts ticking several seconds after the fastest camera(s) already began recording.
>
> Root cause: every `await` in the `video_engine_v2` layer (`CameraPipelineV2`, `MediaFoundationEncoderService`) captures the default WPF `SynchronizationContext`, so each one's continuation must wait its turn on the same UI dispatcher queue the 4 GPU render threads are also posting to. Verified safe to fix by auditing every event these classes raise (`PipelineError`/`StateChanged`/`EncodingFinalised`/`FrameRendered`/`FallenBackToWpf`) — none have UI-thread-assuming subscribers reachable through this change (`StateChanged` has no external subscribers on `CameraPipelineV2`; `PipelineError` only reaches logging; `EncodingFinalised` has no subscribers at all; `FrameRendered`/`FallenBackToWpf` are already independently marshaled via `Dispatcher.BeginInvoke` inside `Direct3DPreviewRenderer`, unaffected by this change).
>
> Also confirmed via the batch: color/exposure/low-light-compensation readback is consistently "Not supported by device/driver" across ALL resolutions (480p/720p/1080p) — a hardware/driver limitation of the test cameras, not resolution-dependent, nothing to fix. Container color tags remain `color_space=unknown`/`color_primaries=unknown` with full-range (`pc`) YUV — the known, already-investigated-and-reverted v1.2.26/28 limitation (Windows Camera uses limited-range `tv` with `bt709`/`bt470bg` tags); would need the same `IMFSinkWriter` rewrite as VFR, already declined. Duplicate-frame spot-checks on the two worst-affected sessions: zero/negligible, video content unaffected.

### Fixed
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — `.ConfigureAwait(false)` added to all internal awaits in `StartPreviewAsync`, `StopPreviewAsync`, `StartRecordingAsync`, `StopRecordingAsync` (encoder open/start/finalise, capture start/stop/close, timestamp-monitor stop).
* **`capture/video_engine_v2/MediaFoundationEncoderService.cs`** — `.ConfigureAwait(false)` added to all `LowLagMediaRecording`/`StorageFolder` WinRT awaits in `OpenAsync`, `StartAsync`, `FinaliseAsync`.

### Tests
* Updated `BackendVersion` assertion to `1.2.64`. 295 tests passing (unchanged).

## [v1.2.63] - 2026-07-05 (build 283)

> **Fixed a real metadata bug that made the user's "Stop Recording looks frozen at 15fps AND 60fps" report look worse than it actually was.** A 6-session batch across 3-4 cameras × 15/30/60fps confirmed only ONE session actually froze (4-camera/60fps, `test3`) — but the two 4-camera sessions recorded *after* it (15fps and 30fps) both reported the exact same `Max UI freeze: 9435 ms` / `Freeze during state: RecordingStopping` in their own metadata, despite their own runtime logs showing zero freeze events during their own recordings. Root cause: `UiFreezeWatchdog`'s `_freezeCount`/`_maxFreezeMs`/`_lastFreezeState` fields are lifetime-of-the-app-process totals on a single long-lived instance — nothing ever reset them between recordings, so once any recording triggered a freeze, every subsequent recording's metadata falsely echoed it as if it happened during that later, actually-clean recording. This made an isolated 60fps issue look like it also affected 15fps.
>
> **The genuine freeze (60fps, first 4-camera session in the batch) is still open, but its root cause is now better understood.** ffprobe confirmed all 4 cameras' recorded containers came out full and consistent length (~27.5-28s) despite the metadata's stopwatch-based "Duration" field showing up to a 13s spread between cameras — meaning the actual video/encoder pipeline was fine and recording in real time the whole way through; only the app's own awareness of "this camera's recording has started" (and the diagnostics/elapsed-timer/UI thread) lagged behind by several seconds. This points at WPF dispatcher-continuation congestion (every `await` in the `video_engine_v2` layer captures the UI `SynchronizationContext` by default) rather than a native WinRT/hardware delay — see the new upgrade recommendation below. Not fixed this release; needs its own careful pass since a broad `ConfigureAwait(false)` change risks introducing cross-thread WPF exceptions if any event subscriber assumes UI-thread affinity.
>
> Video audit of all 21 files across the 6 sessions: correct 1920x1080 resolution throughout, only the same benign ~8-9 frame startup-settling duplicates seen in every prior batch (MD5-verified false positives from mpdecimate on near-static content), no corruption anywhere — confirms the user's own "video is ok" observation.

### Fixed
* **`diagnostics/UiFreezeWatchdog.cs`** — added `Reset()` to clear `FreezeCount`/`MaxFreezeMs`/`LastFreezeState` back to zero/`"None"`.
* **`MainWindow.xaml.cs`** — `StartV2DefaultRecordingAsync` now calls `_freezeWatchdog.Reset()` before starting, so each recording's metadata reflects only what happened during that recording.

### Suggested upgrade (not implemented, needs dedicated review)
* Audit `CameraPipelineV2`/`MediaFoundationEncoderService`/`FrameTimestampMonitor`'s internal `await` chains for safe `ConfigureAwait(false)` — none of these classes touch WPF UI objects directly (confirmed via grep, zero `Dispatcher`/UI-control references), so their continuations don't need the UI thread. Under heavy 4-camera GPU-render load, forcing these to resume on the congested UI dispatcher (the default today) is the likely cause of the still-open Stop/Start Recording UI-responsiveness lag. Needs verifying every subscriber of the events these classes raise (`PipelineError`, `EncoderError`, `SlotFrameRendered`, etc.) correctly marshals to the UI thread itself before flipping this, to avoid introducing cross-thread WPF exceptions.

### Tests
* Updated `BackendVersion` assertion to `1.2.63`. 295 tests passing (unchanged).

## [v1.2.62] - 2026-07-05 (build 282)

> **Fixed a real bug the user found by hand: clicking Stop Preview left the last camera frame frozen on screen instead of clearing it.** Root cause: `StopPreviewBtn_Click` only did `foreach (var c in _previewImages) c.Source = null` — clearing the WPF `Image` controls' bitmap source. That was sufficient before v1.2.59 because GPU rendering always silently failed and every slot used the WPF `Image` path. Now that GPU rendering actually works, a GPU-rendered slot's visible surface is a completely separate native HWND (`D3D11SwapChainHost`, set as `_cellBorders[slot].Child` in `OpenV2SlotAsync`) — clearing `Image.Source` has zero effect on it, and nothing else told it to stop showing its last-presented frame. `StartV2DefaultPipelineAsync` already had the correct "restore placeholder viewbox for GPU slots, clear bitmap for CPU slots" reset logic (used to clean up stale state before opening new slots) — that logic just wasn't also running on Stop. Factored it into a shared `ResetAllSlotPreviewSurfaces()` helper and call it from `StopV2DefaultPipelineAsync` too.
>
> Also verified, per the user's request, whether the 4-camera Stop Recording freeze (v1.2.61) actually improved on real hardware: a fresh batch of 4 consecutive 4-camera/mixed-fps sessions showed the freeze duration dropped from the previously-observed ~10s down to ~3s, and the previously-staggered per-slot stop timestamps now cluster within ~1.6s of each other (was ~9s) — the v1.2.61 fix is working. However, the same batch surfaced a **new, different** finding on its 4th back-to-back session: camera 1's `LowLagMediaRecording.StartAsync()` call itself (a native WinRT API call, not app code) took ~8.5 seconds to return while the other 3 cameras in the same session started in under 150ms — cam1's own recorded duration was correspondingly ~8.6s shorter than the other 3 cameras' in that session, fully explaining the discrepancy (no corruption, no ongoing frame loss once it started). No blocking `.Wait()`/`.Result` calls were found anywhere in the affected call chain (swept the whole `video_engine_v2/` folder) — the delay is inside the OS/driver's own encoder-session-start path. Likely cause: hardware H.264 encoder session pool contention after 4 consecutive 4-camera recordings in ~3 minutes, possibly worsened by `LowLagMediaRecording` not implementing `IDisposable` (its native COM resources are only released whenever .NET's GC happens to finalize the wrapper, which can lag under the sustained allocation pressure of 4 concurrent GPU render threads). **Not fixed this release** — this is a single, not-yet-reproduced-on-demand observation, and this project's history strongly favors gathering more evidence over guessing a fix for exactly this class of bug.

### Fixed
* **`MainWindow.xaml.cs`** — `StopV2DefaultPipelineAsync` now calls the same slot-surface reset logic `StartV2DefaultPipelineAsync` already used (extracted into `ResetAllSlotPreviewSurfaces()`), so Stop Preview correctly clears GPU-rendered slots (restores the placeholder viewbox) as well as WPF-rendered slots (clears the bitmap), instead of leaving GPU slots frozen on their last frame.

### Tests
* Updated `BackendVersion` assertion to `1.2.62`. 295 tests passing (unchanged).

## [v1.2.61] - 2026-07-05 (build 281)

> **Fixed a real ~10-second UI freeze on Stop Recording, found while testing a 4-camera/60fps session right after the Vortice.Windows migration (v1.2.59) started actually engaging GPU rendering for the first time.** A fresh batch (three 3-camera sessions + one 4-camera session) showed zero freezes on the 3-camera sessions but a `Max UI freeze: 10093 ms` on the 4-camera one, logged by the app's own `UiFreezeWatchdog` during `RecordingStopping`. The recording runtime log showed the per-slot `Recording → StoppingRecording` state transition firing for one camera immediately, then stalling for ~9 seconds before the other three fired in quick succession — a signature of a single blocking call inside a loop that otherwise runs each slot's stop sequence back-to-back on the UI thread (`StopRecordBtn_Click` → ... → `CameraPipelineV2.StopRecordingAsync`, all still synchronous up to their first genuine `await`, since nothing in the chain offloads to a background thread).
>
> Root cause: `FrameTimestampMonitor.CloseCsv()` (called from `.Stop()`, itself called directly and synchronously from `StopRecordingAsync`) did `_csvWriterTask?.Wait(TimeSpan.FromSeconds(2))` — a **blocking** wait on the background CSV-writer task, on the UI thread, once per camera slot. The comment above it assumed the writer queue would be "near-empty" by the time Stop() is called, a reasonable assumption under light load — but with 4 real (previously-always-broken, now finally working) GPU render threads and 4 concurrent hardware H.264 encoders genuinely competing for the CPU/GPU, the background writer task can fall behind and take close to the full 2-second timeout to drain. Four slots × up to 2s each ≈ the observed ~9-10s stall. This exact load pattern never existed before v1.2.59, since every previous session's GPU init silently failed and fell back to WPF/CPU rendering.
>
> Also hardened, as a smaller defensive measure found during the investigation: `D3D11SwapChainHost`'s per-frame `FramePresented` UI notification (`Dispatcher.BeginInvoke`) had no throttling or backpressure for GPU-rendered slots, unlike the already-hardened WPF fallback path (which has both a preview-fps throttle and a single-render-pending guard specifically to prevent exactly this kind of dispatcher-queue flood — see that file's own v1.2.58-era "critical fix for the 3-camera UI freeze" comment). Added the same kind of single-pending-notification coalescing guard to the GPU path so it can never queue more than one `FramePresented` callback per slot regardless of camera FPS or count.

### Fixed
* **`capture/video_engine_v2/FrameTimestampMonitor.cs`** — added `StopAsync()`/`CloseCsvAsync()`, using `Task.WaitAsync(TimeSpan)` instead of the blocking `Task.Wait(TimeSpan)`, so draining the background CSV writer on recording-stop no longer blocks the UI thread. The synchronous `Stop()`/`CloseCsv()` remain for `Dispose()`, which cannot await.
* **`capture/video_engine_v2/CameraPipelineV2.cs`** — the 3 call sites that stop the timestamp monitor (`StartPreviewAsync`'s error path, `StopPreviewAsync`, `StopRecordingAsync`) now `await _timestampMonitor.StopAsync()` instead of calling the blocking `Stop()`.
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — `RenderLoop`'s per-frame `FramePresented` notification is now coalesced (at most one `Dispatcher.BeginInvoke` in flight per slot at a time), mirroring the WPF renderer's existing `_renderPending` guard.

### Tests
* Updated `BackendVersion` assertion to `1.2.61`. 295 tests passing (unchanged).

## [v1.2.60] - 2026-07-05 (build 280)

> **First real-hardware confirmation that the v1.2.59 Vortice.Windows migration actually works — the 8-version GPU preview saga is resolved.** A 5-session test batch (2× 2-camera, 2× 3-camera, plus one more 2-camera) recorded immediately after v1.2.59 shipped shows the runtime log reporting `V2_RENDERER_INIT renderer=D3D11 d3d11=Available sharedDevice=True` followed by `D3D11_HOST: CreateSwapChainForHwnd succeeded` for **all 15 camera-slot initializations across all 5 sessions — zero fallbacks, zero `DXGI_ERROR_INVALID_CALL`, zero present-fatal/device-lost events**. This is the first time in the entire v1.2.42-v1.2.59 investigation that the GPU swap chain has succeeded even once on this test machine.
>
> While confirming this, found and fixed a real metadata-accuracy bug directly exposed by the fix actually working: the per-camera `.txt`/`.json` metadata's `Renderer`/`Preview API` fields were **hardcoded literal strings** (`"WPF"` / `"WPF WriteableBitmap (software)"`) in `MainWindow.xaml.cs` and `VideoEngineRegistry.BuildMetadata`, completely disconnected from which renderer actually ran — so every recorded session's metadata was claiming CPU/WPF preview even in sessions where GPU/Direct3D11 rendering was genuinely active. Wired both fields to the real per-slot `VideoEngineV2.GetSlotPreviewRenderer(slot)` value instead.
>
> Full audit of the 5-session batch: all sessions `PASS`/`PASS_WITH_WARNING` (expected per-driver control-support gaps only), inter-camera frame-count spread ≤5 frames, zero mid-session timestamp gaps, zero UI freezes. One session (`test1_20260705_004656`, the very first of the batch) showed 8-9 truly identical (MD5-matched) frames at the very start of both cam1 and cam2's recordings — consistent with genuine camera/scene settling at the start of a cold recording, not a systemic bug (recording goes through a completely separate encoder pipeline from the preview swap chain the Vortice migration touched; all 4 other sessions showed zero duplicate frames). No fix applied for this — flagged as a minor, low-confidence, unrelated observation.

### Fixed
* **`capture/backend/VideoEngineRegistry.cs`** — `BuildMetadata` now takes a `PreviewRendererType previewRenderer` parameter and derives `PreviewApi` from it (`"Direct3D11 GPU swap chain (Vortice.Windows)"` vs `"WPF WriteableBitmap (software)"`) instead of a hardcoded string.
* **`MainWindow.xaml.cs`** — the per-camera `.txt` metadata's `[Recording Engine] Renderer` line and both `BuildMetadata` call sites now read the real per-slot renderer via `_v2Engine.GetSlotPreviewRenderer(slot)` instead of a literal `"WPF"`.

### Tests
* Updated `BackendVersion` assertion to `1.2.60`. 295 tests passing (unchanged).

## [v1.2.59] - 2026-07-05 (build 279)

> **Replaced the entire hand-rolled D3D11/DXGI COM interop layer with Vortice.Windows.** Across 8 released versions (v1.2.42 → v1.2.58) of real-hardware-tested fix attempts on the hand-written, manually vtable-slot-counted `[ComImport]` interfaces (`ID3D11Device`, `IDXGIFactory2`, `IDXGISwapChain1`, etc.), `CreateSwapChainForHwnd` never once produced a working swap chain — every camera preview silently used the CPU/WPF fallback. Rather than continue hand-fixing individual parameters (a pattern that produced four disproven hypotheses in a row), replaced the interop layer entirely with **Vortice.Direct3D11**/**Vortice.DXGI** (SharpGen-generated, SDK-header-accurate bindings, versions 3.8.3) — a mature library that has already solved this exact class of vtable/QueryInterface-binding problem. `D3D11Interop.cs` shrank from ~452 lines (hand-rolled enums/structs/interfaces) to ~115 lines (only the WinRT `CreateDirect3D11DeviceFromDXGIDevice` glue, the WinRT-only `IDirect3DDxgiInterfaceAccess` interop interface, and `Win32Window` — none of which Vortice covers). `D3D11SwapChainHost.cs`'s internals were rewritten against Vortice's typed COM wrappers (`ID3D11Device`, `ID3D11DeviceContext`, `IDXGIFactory2`, `IDXGISwapChain1`, `ID3D11Texture2D`), removing the raw-vtable-function-pointer workaround, the manual GUID constants, and the multi-version diagnostic-probe logging that accumulated across the investigation. The public boundary (`Direct3DPreviewRenderer`, `D3D11PreviewPanel`, the WPF fallback, the preview-fps throttle) is completely unchanged — this is an internals swap behind an existing interface. Real-hardware verification (does the swap chain actually succeed now?) is still pending the next test session; if Vortice's `CreateSwapChainForHwnd` binding also fails, the exact same WPF fallback engages with zero behavior change anywhere outside the two interop files.

### Changed
* **`capture/video_engine_v2/D3D11Interop.cs`** — removed all hand-rolled, vtable-slot-counted D3D11/DXGI `[ComImport]` interfaces, enums, and structs (the actual bug surface across v1.2.42-v1.2.58). Kept only the WinRT/Win32 glue Vortice.Windows doesn't provide: `Win32Window`, `IDirect3DDxgiInterfaceAccess`, and the `CreateDirect3D11DeviceFromDXGIDevice` P/Invoke.
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — full internals rewrite against `Vortice.Direct3D11`/`Vortice.DXGI` typed COM wrappers instead of manual `Marshal.QueryInterface`/`GetTypedObjectForIUnknown`/raw-vtable dispatch. Device/factory setup now goes through `D3D11.D3D11CreateDevice` → `IDXGIDevice.GetAdapter()` → `IDXGIAdapter.GetParent<IDXGIFactory2>()`; swap chain creation through `IDXGIFactory2.CreateSwapChainForHwnd`; `Present`/`GetBuffer<T>()`/`ResizeBuffers`/`Map`/`Unmap`/`CopyResource`/`CreateTexture2D` all use Vortice's direct typed equivalents. Kept the one legacy-swap-effect retry (flip-model → BitBlt DISCARD on `DXGI_ERROR_INVALID_CALL`) as defensive fallback for older/quirky drivers.
* **`MultiCamApp.csproj`** — added `Vortice.Direct3D11` and `Vortice.DXGI` (3.8.3) package references.

### Tests
* Updated `BackendVersion` assertion to `1.2.59`. 295 tests passing (unchanged).

## [v1.2.58] - 2026-07-04 (build 278)

> **Legacy swap-effect fallback disproven too — ruled out presentation model entirely, moved to a thread-affinity hypothesis.** A ~20-session fresh batch showed the v1.2.57 legacy-`DISCARD` retry failing with the exact same `DXGI_ERROR_INVALID_CALL` as the flip-model attempt, every single time (0 successes across 150 total swap-chain attempts in the batch) — conclusively ruling out flip-vs-legacy presentation model as the variable, since both fail identically.
>
> New hypothesis, genuinely different from the three struct-field guesses already tried: `CreateSwapChainForHwnd` has always been called from a dedicated render thread — a deliberate design goal (see this file's header) to keep D3D11 work off the WPF dispatcher — which is a **different thread than the one that creates the HWND** (`BuildWindowCore` always runs on the UI/dispatcher thread for any `HwndHost`). Some DXGI driver stacks require swap-chain creation to happen on the window's owning thread. Moved the one-time `CreateSwapChain` call (and the initial black-frame present) to run synchronously inside `BuildWindowCore`, on the same UI thread that just created the HWND — the render thread now only waits for that to finish, then takes over `Present`/frame delivery for the rest of the host's lifetime, unchanged from before. This is a real architectural change (not another parameter tweak) directly testing whether thread affinity is the actual constraint.
>
> **Full audit across the entire accumulated video history**: scanned all 63 recorded camera files across every session ever produced this session (not just the newest batch) — zero duplicate frames anywhere. All writer statuses `Success`, zero UI freezes, no recurrence of the numeric-format bug, in both English and Japanese sessions. Checked "blurry at hands" specifically per the user's request via the newest ShareX recording: consistent with the established pattern — recorded files stay clean, live preview still shows moderate (not severe) motion blur during recording since GPU rendering is still on the WPF fallback; no new regression, same known cause as before.

### Changed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — swap chain creation moved from the dedicated render thread to `BuildWindowCore` (the UI/dispatcher thread that owns the HWND), testing whether DXGI's `DXGI_ERROR_INVALID_CALL` is caused by a thread-affinity mismatch between the creating thread and the window's owning thread. Unverified pending a real hardware test run.

### Tests
* Updated `BackendVersion` assertion to `1.2.58`. 295 tests passing (unchanged).

## [v1.2.57] - 2026-07-04 (build 277)

> **AlphaMode fix disproven too — added an empirical fallback instead of a third blind guess.** User recorded a massive batch (42 sessions covering every camera layout × every resolution) specifically to stress-test v1.2.56. Result: `DXGI_ERROR_INVALID_CALL` recurred in all 113 swap-chain attempts across the batch, with `alphaMode=Unspecified` confirmed in every log line — the v1.2.56 fix genuinely didn't help. Every other logged parameter (format, buffer count, scaling, swap effect, HWND validity) looks completely standard, and the vtable slot for `CreateSwapChainForHwnd` has now been hand-audited four times against the real Windows SDK header layout without finding an error.
>
> Rather than guess a third specific parameter blind, added an **empirical, self-diagnosing fallback**: `CreateSwapChain` now retries once with the legacy BitBlt presentation model (`DXGI_SWAP_EFFECT_DISCARD`, `BufferCount=1`) specifically when the flip-model attempt (`FLIP_DISCARD`, `BufferCount=2`) returns `DXGI_ERROR_INVALID_CALL`. Flip-model swap chains have meaningfully more OS/driver/window-state restrictions than the legacy model, so this experiment will empirically show whether flip-model itself is unsupported for this WPF-hosted child-window scenario on this hardware (if the legacy retry succeeds) or whether the real cause is something more fundamental affecting both models (if the retry fails identically). Both attempts are fully logged, so either outcome is immediately diagnosable from the next test run without further guessing.
>
> **Full audit of the 42-session batch plus UI appearance verification** (both requested by the user): every recorded file across the batch verified correct resolution (1920×1080/1280×720 matching each request), zero duplicate frames, numeric formatting and EN/JA localization holding correctly (including in sessions recorded entirely in Japanese). Sampled the preview UI at many points across a 19-minute ShareX screen recording spanning 1/2/4-camera layouts at 1080p and 720p: video content fills each panel correctly with no distortion, incorrect cropping, or stretching in any sampled combination; the FPS dropdown correctly showed only 15/30/60 and the resolution dropdown only 480p/720p/1080p, confirming earlier UI simplification work is holding. No new UI sizing defects found.

### Changed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — `CreateSwapChain` now retries once with the legacy `DISCARD` swap effect (`BufferCount=1`) specifically when the flip-model (`FLIP_DISCARD`) attempt returns `DXGI_ERROR_INVALID_CALL`, to empirically determine whether flip-model itself is the blocker on this hardware. Refactored the raw-vtable invocation into a reusable `TryCreateSwapChainForHwnd` helper shared by both attempts.

### Tests
* Updated `BackendVersion` assertion to `1.2.57`. 295 tests passing (unchanged).

## [v1.2.56] - 2026-07-04 (build 276)

> **Ruled out the placeholder-size hypothesis and found the actual likely cause of `DXGI_ERROR_INVALID_CALL`.** User recorded a large batch (7 sessions spanning 1/2/3/4-camera layouts and 1080p/720p) specifically to confirm the v1.2.55 fix works everywhere — it didn't: `DXGI_ERROR_INVALID_CALL` (0x887A0001) recurred identically in every single session regardless of camera count or resolution, with the new 1280×720 placeholder size logged each time, ruling that hypothesis out cleanly. This is useful negative evidence and also incidentally the confirmation that the bug (and any fix) really is layout-count-independent, exactly as structurally predicted in v1.2.55.
>
> Re-examined the full `DXGI_SWAP_CHAIN_DESC1` being passed and found a genuine, well-documented DXGI restriction being violated: `AlphaMode` was set to `DXGI_ALPHA_MODE_IGNORE`, but per DirectX documentation, `DXGI_ALPHA_MODE_IGNORE`/`PREMULTIPLIED`/`STRAIGHT` are **only valid for composition swap chains** (`CreateSwapChainForComposition`, DirectComposition-based). `CreateSwapChainForHwnd` requires `DXGI_ALPHA_MODE_UNSPECIFIED` — any other value returns exactly `DXGI_ERROR_INVALID_CALL`. This parameter had never been validated against real DXGI before the v1.2.53 raw-vtable fix started actually reaching native validation, so its long presence in the code was never evidence it was correct. Changed to `Unspecified`.
>
> Full audit of all 7 sessions: zero duplicate frames in every recorded file (13 files total) plus the new Windows Camera clip, correct resolutions throughout (1920×1080 for 1080p requests, 1280×720 for 720p requests), numeric formatting fix holding in both English and Japanese sessions, zero real UI freezes per the app's own dispatcher watchdog across all 7 (a long ShareX recording's `freezedetect` flagged many short "freezes" scattered throughout, but these are false positives from genuinely static screen content during setup pauses between the 7 back-to-back test recordings — the app's own watchdog is the authoritative signal here since it measures real dispatcher responsiveness, not pixel similarity).

### Fixed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — swap chain's `AlphaMode` changed from `DXGI_ALPHA_MODE_IGNORE` to `DXGI_ALPHA_MODE_UNSPECIFIED`, the value required for `CreateSwapChainForHwnd` (the `IGNORE`/`PREMULTIPLIED`/`STRAIGHT` values are composition-swap-chain-only and return `DXGI_ERROR_INVALID_CALL` otherwise). Unverified pending a real hardware test run.

### Tests
* Updated `BackendVersion` assertion to `1.2.56`. 295 tests passing (unchanged).

## [v1.2.55] - 2026-07-04 (build 275)

> **Attempted fix for the newly-surfaced `DXGI_ERROR_INVALID_CALL`, plus explicit verification that the whole GPU init path is uniform across all 1-4 camera layouts** (per user request to make sure this doesn't slip through for layouts other than the 3-camera one being tested). Confirmed structurally: `MainWindow`'s slot-opening loop (`for (var i = 0; i < _vm.State.CameraLayout; i++) { ... OpenV2SlotAsync(...) }`) has no per-count branching, and each active slot gets its own fully independent `CameraPipelineV2` → `Direct3DPreviewRenderer` → `D3D11SwapChainHost` chain with no shared state between slots — so this bug (and any fix for it) inherently applies identically whether 1, 2, 3, or 4 cameras are active; there is no code path where it could behave differently by camera count.
>
> For the actual `DXGI_ERROR_INVALID_CALL` itself: the leading candidate was the swap chain's initial placeholder size — `RenderLoop` created it at a degenerate 2×2 before resizing to the real camera resolution on the first frame. This exact code path had never been exercised against real DXGI validation before v1.2.53's raw-vtable fix started actually reaching `CreateSwapChainForHwnd` (every previous attempt failed earlier in the .NET interop layer, before DXGI ever saw the call), so nothing about "2×2 has always been in the code" implied it was actually valid. Changed the placeholder to 1280×720 — the app's existing generic preview-default size (`VideoEngineSettings.PreviewWidth/Height`), already used elsewhere for the WPF/CPU fallback's initial surface — instead of an untested edge-case size. The resize-on-first-frame logic (`EnsureSwapChainAtCameraRes`) is unaffected since it already only acts when dimensions actually change, regardless of what the placeholder was. Still unverified pending a real hardware test.

### Fixed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — swap chain's initial placeholder size changed from a degenerate 2×2 to 1280×720 (`VideoEngineSettings.PreviewWidth/Height`), the likely cause of the `DXGI_ERROR_INVALID_CALL` surfaced once the raw-vtable call started actually reaching real DXGI validation. Unverified pending a real hardware test run.

### Tests
* Updated `BackendVersion` assertion to `1.2.55`. 295 tests passing (unchanged).

## [v1.2.54] - 2026-07-04 (build 274)

> **Major GPU breakthrough confirmed by real testing: the raw vtable fix from v1.2.53 works.** The mysterious `InvalidCastException` that plagued every previous attempt is gone — `CreateSwapChainForHwnd (raw vtable) returned hr=0x887A0001 scPtr=0x0` now logs cleanly, meaning the call executes and returns normally for the first time in this entire investigation. The remaining problem is a genuine, honest native DXGI rejection: `0x887A0001` = `DXGI_ERROR_INVALID_CALL`. This is real progress — the months-long .NET interop mystery is resolved; what's left is an ordinary "which parameter does DXGI not like" question, not an unexplained framework bug. Added full parameter logging immediately before the call (HWND value + `IsWindow` validity, device pointer, requested width/height, format, buffer count, scaling/swap-effect/alpha-mode) so the next test pinpoints the exact cause — candidates include the 2×2 placeholder size (untested until now since the call never previously executed) or something about the child HWND's state at creation time.
>
> Full audit of a fresh session otherwise clean: zero duplicate frames in all 3 recorded files plus a new Windows Camera clip, fps consistent (29.4-30fps), zero mid-session UI freezes (the one freeze `ffmpeg freezedetect` found was 1.4s right at the very end of the screen recording — natural stillness when stopping, same pattern as every previous session), numeric formatting fix holding in real output.

### Added
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — logs HWND validity, device pointer, and full swap-chain-desc parameters immediately before the raw vtable `CreateSwapChainForHwnd` call, to diagnose the newly-surfaced `DXGI_ERROR_INVALID_CALL`.

### Tests
* Updated `BackendVersion` assertion to `1.2.54`. 295 tests passing (unchanged).

## [v1.2.53] - 2026-07-04 (build 273)

> **GPU bug: switched to raw vtable invocation after the isolated-call diagnostic conclusively proved the failure.** A fresh test confirmed the v1.2.52 isolation: `D3D11_HOST: CreateSwapChainForHwnd call ITSELF threw (not a returned HRESULT): InvalidCastException... hresult=0x80004002` — while `factory QueryInterface hr=0x00000000` succeeds on the very same object right beforehand. This proves `_dxgiFactory`'s binding is genuinely valid, but .NET's interface-based COM dispatch still fails specifically when invoking `CreateSwapChainForHwnd` through it, for reasons that remain unclear despite the vtable slot layout being hand-audited three times against the real Windows SDK header order and found correct. Rather than continue chasing this specific .NET interop quirk, switched to a fundamentally different, more robust technique for this one call: read the function pointer directly out of the object's vtable (slot 15) and invoke it via an unmanaged function pointer (`Marshal.GetDelegateForFunctionPointer`), bypassing C#'s `[ComImport]` interface dispatch entirely for this method. This is the standard bulletproof workaround when high-level COM interop misbehaves. Kept a second, independently-AddRef'd raw factory pointer (`_dxgiFactoryRawPtr`) alive for the object's lifetime specifically for this purpose, released in `DestroyWindowCore` alongside the existing typed `_dxgiFactory`. Still an attempted fix pending real-hardware confirmation — if `V2_SLOT_RENDERER_FALLBACK` still appears on the next test, the next diagnostic step is checking whether the raw call itself now succeeds or fails differently.
>
> **Requested checks all passed on a third independent test session**: elapsed timer 00:00:00→00:00:01 transition — steady ~1s ticks, no stutter (three sessions checked now, consistently clean). UI freeze/glitch — `[UI Diagnostics]` reports zero freezes, and `ffmpeg freezedetect` on the ShareX recording (strict default threshold) found only one freeze event, 0.63s right at the very end of the recording (natural stillness when stopping, not a mid-session glitch). Video correctness — zero duplicate frames (`mpdecimate`) in all 3 recorded files plus the new Windows Camera clip, fps consistent with every prior session. Numeric formatting fix from v1.2.52 confirmed working in real output (`-1.390 s`, `-1.191 s`, `-0.206 s` — real numbers, no more `-F3`).

### Fixed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — `CreateSwapChainForHwnd` is now invoked via raw vtable dispatch (`Marshal.GetDelegateForFunctionPointer`) instead of C#'s interface-based COM call, which was throwing `InvalidCastException` during the call itself despite the factory object's binding being otherwise valid. Unverified pending a real hardware test run.

### Tests
* Updated `BackendVersion` assertion to `1.2.53`. 295 tests passing (unchanged — still not unit-testable without real D3D11 hardware).

## [v1.2.52] - 2026-07-04 (build 272)

> **Real bug found by actually reading the metadata output text closely, as requested.** `cam1_metadata.txt`/`cam2_metadata.txt` from a fresh session showed `- Frame-count vs app timestamp diff: -F3 s` — a raw, unformatted C# format specifier leaking into the output instead of an actual number. Root cause: `MainWindow.xaml.cs` used a custom numeric format string `{value:+F3;-F3;0.000}` for two fields (frame-count-vs-app-timestamp diff and container-vs-app-timestamp diff). Custom sectioned numeric format strings only understand `0`/`#`-style placeholders — `F3` isn't valid inside a section and gets emitted as **literal characters**, so any negative value printed the literal text `-F3` (and any positive value would have printed `+F3`) instead of the real number; only exact-zero values happened to look correct since the third section `0.000` is valid syntax. Fixed both to the correct custom-format equivalent: `{value:+0.000;-0.000;0.000}`. Confirmed both cameras' negative-diff instance in the same session showed the identical bug (cam3's diff happened to be exactly zero, which is why it wasn't visible there).
>
> **GPU bug — v1.2.51's factory fix confirmed partially working, narrowed further.** The new test run showed `D3D11_HOST: factory QueryInterface hr=0x00000000` (success!) for all 3 slots — the `_dxgiFactory` binding fix from v1.2.51 genuinely resolved that specific failure. But the swap chain still fails immediately after with the same exception, and even the v1.2.50 "returned hr=..." log (which should fire unconditionally right after `CreateSwapChainForHwnd` returns, since it's declared `[PreserveSig]` and COM interop never auto-throws for a PreserveSig HRESULT) still never appeared. This proves the exception is thrown by the `CreateSwapChainForHwnd` call itself — an interop/vtable dispatch failure, not a returned E_NOINTERFACE — so isolated that exact call in its own try/catch with a dedicated log message to prove this conclusively on the next run.
>
> **Elapsed timer 00:00:00→00:00:01 transition checked again** (second independent session): ticks evenly at ~1.0s per second with no stutter or skip, same as the previous check — confirmed not a bug across two separate real recordings now.
>
> Full audit of `test1_20260704_204802`: standard `PASS_WITH_WARNING`, zero duplicate frames in all 3 files plus the new Windows Camera clip, fps consistent with every prior session.

### Fixed
* **`MainWindow.xaml.cs`** — the "Frame-count vs app timestamp diff" and "Container vs app timestamp diff" fields now use a valid custom numeric format string (`+0.000;-0.000;0.000`) instead of one with an invalid `F3` specifier embedded in it, which was printing the literal text `-F3`/`+F3` instead of the actual signed value.

### Added
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — isolated the `CreateSwapChainForHwnd` call in its own try/catch with a dedicated log message, to prove whether the GPU exception is thrown by the call itself versus a returned HRESULT.

### Tests
* Updated `BackendVersion` assertion to `1.2.52`. 295 tests passing (unchanged).

## [v1.2.51] - 2026-07-04 (build 271)

> **New lead on the GPU rendering bug.** A fresh test session showed v1.2.50's raw-HRESULT probe (logged immediately after `CreateSwapChainForHwnd` returns, before the HRESULT can be converted into an exception) **still never fired** — proving the exception is thrown *during* that call, not after it returns. A normal method call throwing mid-invocation (rather than returning a failure HRESULT) points at the RCW backing `_dxgiFactory` itself: `_dxgiFactory = (IDXGIFactory2)Marshal.GetObjectForIUnknown(factoryPtr)` creates an untyped RCW and defers the real interface binding to first use — which is exactly this call, `_dxgiFactory.CreateSwapChainForHwnd(...)`. That's the same "implicit cast defers a second QueryInterface" trap already fixed for the swap chain pointer itself in v1.2.47, just one level earlier in the chain and never previously touched. Applied the identical fix: an explicit `Marshal.QueryInterface` for `IDXGIFactory2`'s real IID (trusted since `IDXGIObject::GetParent`'s REFIID lookup already proved `factoryPtr` supports it) followed by `Marshal.GetTypedObjectForIUnknown`, with logging on the QueryInterface result. Still an attempted fix pending real-hardware confirmation, not a guaranteed resolution — same honesty standard as every previous attempt at this bug.
>
> Also noted (not fixed, single occurrence so far, self-recovered on retry): one session hit `MediaCapture failed (0xC00D3704): Hardware MFT failed to start streaming due to lack of hardware resources` on one slot during 3-camera concurrent startup — the app retried and all 3 cameras opened successfully on the second attempt. Not deterministic yet, watching for recurrence before investigating further.
>
> Full audit of `test1_20260704_203036` (3 cameras): standard `PASS_WITH_WARNING`, all 3 writers finalized successfully, gaps are the usual startup transients. `mpdecimate` again found zero true duplicate frames in all 3 recorded files and the new Windows Camera comparison clip. Preview ghosting during recording continues to look mild (consistent with the v1.2.49 mitigation), not worse.

### Fixed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — `_dxgiFactory`'s construction now uses an explicit `Marshal.QueryInterface` + `Marshal.GetTypedObjectForIUnknown` instead of an implicit `(IDXGIFactory2)Marshal.GetObjectForIUnknown(...)` cast, matching the pattern already applied to the swap chain pointer. Unverified pending a real hardware test run.

### Tests
* Updated `BackendVersion` assertion to `1.2.51`. 295 tests passing (unchanged — still not unit-testable without real D3D11 hardware).

## [v1.2.50] - 2026-07-04 (build 270)

> **Full re-audit of a new 3-camera session (`test2_20260704_200511`) plus a specific investigation into the recording-start elapsed-timer "00:00:00 → 00:00:01" transition and internal clock precision, requested after the user noticed the timer tick.** Extracted frame-by-frame crops of the Elapsed display from a fresh ShareX recording at 5 frames/second resolution around the exact Start Recording click: the counter reads `00:00:00` continuously from the moment Recording state begins until ~0.9-1.0s later, then ticks to `00:00:01`, then to `00:00:02` about another ~1.0s after that — a normal, evenly-paced one-tick-per-real-second display with no stutter, skip, or double-increment. Also checked the internal per-frame CSV clock: all 3 cameras' first recorded frame lands within 9.8-22.2ms of each other in real UTC time, with normal ~32ms frame intervals from the start — no synchronization anomaly. **No bug found in either area; the elapsed timer and internal clock both behave correctly.**
>
> **GPU rendering still confirmed broken** (same `InvalidCastException 0x80004002` on every slot, same as v1.2.49) — but this build's `dist/` output was stale relative to source: the raw-HRESULT diagnostic added to `D3D11SwapChainHost.CreateSwapChain` immediately after v1.2.49 shipped was never actually published (only `dotnet build`/`test` had been run since, not `build_release`). This release finally packages that diagnostic — logs the literal HRESULT from `CreateSwapChainForHwnd` itself before it can be converted into an exception, which should make the next test run's log show, for the first time, whether the factory call itself is failing or something later in the chain is.
>
> **Frame counts / fps / duplicate-frame check** (test2, 65.7-65.8s, 3 cameras): 1930/1964/1931 frames, 29.35-29.85fps — consistent with every prior session. `mpdecimate` found **zero true duplicate frames** in all 3 recorded files and in the new Windows Camera comparison clip (`WIN_20260704_20_05_02_Pro.mp4`, 2241 frames, 29.68fps, also zero duplicates). Re-confirmed the recording-phase preview ghosting from v1.2.49's mitigation: visually milder than the original report, single coherent blur streaks in this session's ShareX capture rather than the earlier multi-position ghosting — improvement holding.

### Added
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — logs the raw HRESULT and swap-chain pointer immediately after `CreateSwapChainForHwnd` returns, before `ThrowExceptionForHR` can convert it into an exception (carried over from the previous turn, now actually published via `build_release`).

### Tests
* Updated `BackendVersion` assertion to `1.2.50`. 295 tests passing (unchanged).

## [v1.2.49] - 2026-07-04 (build 269)

> **Real side-by-side comparison vs Windows Camera app (user-provided: `test1_20260704_191644` + Windows Camera's `WIN_20260704_19_16_38_Pro.mp4` + a synchronized ShareX screen recording of both apps' live UI).** This is the first genuinely conclusive evidence for the "preview degrades after Start Recording" report.
>
> **v1.2.47/48's GPU fix did NOT work — confirmed wrong diagnosis.** The runtime log for this fresh session shows the identical `InvalidCastException hresult=0x80004002` on every slot, and critically, neither of v1.2.47's new diagnostic log lines (the QueryInterface-succeeded line, or the typed-wrap-failed line) appear at all — proving the failure happens at the very first `Marshal.QueryInterface` call for the base `IDXGISwapChain` IID itself, not the implicit cast one line later as v1.2.47 assumed. That assumption was based on the exception *message* ("Specified cast is not valid") looking generic — but that message is simply what .NET always produces for E_NOINTERFACE via `Marshal.GetExceptionForHR`, regardless of which call triggered it, so it couldn't actually distinguish the two failure points. Audited the full COM interop vtable chain (`IDXGIFactory2`, `CreateSwapChainForHwnd`) by hand against the real Windows SDK layout — no slot-ordering bug found; the analogous `GetObjectForIUnknown`+cast pattern already works correctly for `_dxgiFactory` earlier in the same method. Root cause remains unresolved. Added a non-fatal diagnostic probe in `D3D11SwapChainHost.CreateSwapChain` that queries the raw swap-chain pointer for IUnknown (sanity baseline) and IDXGISwapChain1's *own* real IID, logging both HRESULTs, so the next real test conclusively shows whether the pointer is valid at all.
>
> **Confirmed via direct visual inspection of the ShareX recording**: MultiCamApp's own CAM1/CAM3 preview panels show a clear multi-position "ghosting" artifact on fast arm motion specifically after the Recording state begins (visible as 2-3 overlapping hand positions instead of one smooth blur streak) — while Windows Camera's live preview, running continuously alongside in the same recording, shows a single coherent motion blur throughout with no visible change. This is the classic signature of frame-skipping/low-refresh preview, not camera-sensor motion blur (which both apps show identically when idle/slow-moving). Confirms the `SetAllSlotsPreviewFpsLimit` throttle (only meant to apply to the WPF/CPU fallback renderer) is the direct visible cause, since every slot is still stuck on that fallback.
>
> **Interim mitigation applied** (not a fix for the GPU root cause): raised `VideoEngineV2.RecommendedPreviewFpsForRecordingCameras`'s preview-fps-cap-during-recording from 20/15/10/8 (1-4 cameras) to 30/24/18/12. These original low values were tuned back when the frame-arrived callback thread also did synchronous CSV disk I/O — the actual cause of the severe v1.2.37 freeze finding, fixed by moving that I/O off the hot path in v1.2.37 — so the throttle no longer needs to be this aggressive to protect UI responsiveness. Needs a real 4-camera stress test to confirm no freeze regression.
>
> **Other comparison findings** (via ffprobe/ffmpeg on the actual recorded files, no code changes needed): color/brightness nearly identical (YAVG 119.8 vs 115.8, SATAVG 10.3 vs 11.0 — both well within normal per-session auto-exposure variance); both apps show identical normal motion blur on fast movement in the recorded files themselves (not app-specific). Windows Camera's MP4 is honestly VFR (container `r_frame_rate` 60/1 vs `avg_frame_rate` ~29.68 — real per-frame duration jitter preserved directly in the file, including a real ~21fps stutter cluster); MultiCamApp's MP4 is forced CFR (every packet exactly 33.88ms apart) — real capture jitter lives only in the sidecar timestamp CSV, invisible in the video file itself. Neither is "wrong," but it means judging smoothness from the recorded MP4 alone would make MultiCamApp look artificially smoother than Windows Camera's honest file — the real difference is in the **live preview**, not the saved recording. Confirmed (still, no regression): MultiCamApp lacks `color_space`/`color_primaries` container tags that Windows Camera has (`bt470bg`/`bt709`) — this is the same known, previously-reverted limitation from v1.2.26/28 (`VideoEncodingProperties.Properties` doesn't propagate into the muxed H.264 via `PrepareLowLagRecordToStorageFileAsync`), not a new gap.

### Changed
* **`capture/video_engine_v2/VideoEngineV2.cs`** — `RecommendedPreviewFpsForRecordingCameras` raised from 20/15/10/8 to 30/24/18/12 fps (1/2/3/4 cameras) as an interim mitigation for visible preview ghosting during recording.

### Added
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — non-fatal diagnostic probe querying the swap-chain pointer for IUnknown and IDXGISwapChain1's real IID, to disambiguate the GPU rendering failure on the next test run.

### Tests
* Updated `BackendVersion` assertion to `1.2.49`. 295 tests passing (unchanged).

## [v1.2.48] - 2026-07-04 (build 268)

> **Real Japanese-language recordings (first ones that exist) exposed two localization gaps the v1.2.47 code-search-based verification missed.** Actually reading `test2_20260704_185719`'s real `.txt` output in Japanese (not just grepping the writer code for `J(...)` calls) showed two full English sentences leaking into otherwise-Japanese metadata: (1) `[セッション検証]`'s notes and `[カメラ間比較]`'s summary line — e.g. *"Compared 4 cameras. Frame range: 558-564 (spread: 6)..."* — came straight from `ComputeSessionVerificationStats` (`MainWindow.xaml.cs`), which builds every note/summary string as plain English with no `isJa` branching at all (unlike the rest of the metadata writer). (2) The flicker-reduction control's warning message (`CameraControlManagerV2.cs`'s fixed backend string, "Flicker reduction is not exposed by the WinRT camera control API...") was emitted raw regardless of language — only its "警告:"/"Warning:" *prefix* was localized, not the message body, unlike the analogous low-light-compensation warning a few lines above it which has a proper bespoke Japanese sentence. Fixed both: `ComputeSessionVerificationStats` now builds every note and the inter-camera comparison summary via an `N(en, ja)` helper keyed on `_sessionLanguage`; the flicker warning now substitutes a full Japanese translation when the message matches the known backend string and the session language is Japanese.
>
> Also confirmed: the 5-session batch used to catch this (`test1`×3 EN, `test2`×2 JA) was recorded against the **installed v1.2.46 build**, not the v1.2.47 GPU-preview fix from the same day — `dist\config\version.json` still read 1.2.46, and the runtime log still showed `V2_SLOT_RENDERER_FALLBACK` on every slot. That fix remains genuinely untested; a `build_release` + reinstall + fresh recording is needed before it can be confirmed or refuted. FPS results in this batch (15→14.8, 30→29.6-29.7, 60→fallback to 30) match all prior findings, no new anomalies.

### Fixed
* **`MainWindow.xaml.cs`** — `ComputeSessionVerificationStats`'s notes list and inter-camera-comparison summary string are now localized via `_sessionLanguage` instead of always English.
* **`MainWindow.xaml.cs`** — the flicker-reduction control's warning message body is now translated to Japanese when the session language is Japanese, matching the localization already present for the equivalent low-light-compensation warning.

### Tests
* Updated `BackendVersion` assertion to `1.2.48`. 295 tests passing (unchanged).

## [v1.2.47] - 2026-07-04 (build 267)

> **Root-caused the "preview looks smooth before recording, shaky/blurry/ghosted on fast motion after clicking Start Recording" report.** Audited 7 fresh sessions (test2/test3/test4, 4-camera, 480p/720p/1080p × 15/30/60fps) — no rename failures (v1.2.46 fix holds), full EN/JA metadata localization confirmed intact (all 21 section headers + control-status strings have verified `ja.json` counterparts; only the technical `CameraFormatSelectorV2` fallback-reason strings stay English by design, unchanged since v1.2.43). **Coverage gap flagged, not fabricated:** the 7 real sessions don't cover 720p@15/30, 480p@15, any Japanese-language session, or 1/2/3-camera layouts — real recordings for those combinations are still needed to fully close out this request.
>
> **Root cause of the preview regression:** `dist\logs\app_runtime_20260704.txt` shows `V2_RENDERER_INIT renderer=D3D11 d3d11=Available` followed immediately by `V2_SLOT_RENDERER_FALLBACK slot=N renderer=WPF` for **every single camera slot in every session** — GPU-accelerated preview has never actually run on this machine, confirming the v1.2.42 fix attempt did not work (same `InvalidCastException: Specified cast is not valid, hresult=0x80004002` still fires, just one line later than before). Traced it to `D3D11SwapChainHost.CreateSwapChain` (`capture/video_engine_v2/D3D11SwapChainHost.cs`): the v1.2.42 fix correctly retargeted the manual `Marshal.QueryInterface` call to the base `IDXGISwapChain` IID (confirmed via new logging — that call now succeeds), but the very next line still did `(IDXGISwapChain1)Marshal.GetObjectForIUnknown(sc1Ptr)` — an explicit C# interface cast on a COM object, which makes the CLR perform its *own* internal QueryInterface using .NET's COM interop, independent of the manual one just completed. That second, implicit query is where the failure actually was. Fixed by replacing it with `Marshal.GetTypedObjectForIUnknown(sc1Ptr, typeof(IDXGISwapChain1))`, the documented API for wrapping an already-QueryInterface'd pointer without a second native call. **Because every camera slot silently fell back to the WPF/CPU renderer, every recording session was also hitting `VideoEngineV2.RecommendedPreviewFpsForRecordingCameras`'s aggressive preview-fps cap (20/15/10/8fps for 1/2/3/4 cameras) via naive elapsed-time frame-dropping in `Direct3DPreviewRenderer.PresentFrame`** — the GPU path bypasses this throttle entirely (own independent render thread), so this cap was only ever meant to apply to WPF fallback slots, but since 100% of slots were on WPF, 100% of recordings were subject to a hard drop to as low as 8fps preview the instant recording started, which is the direct mechanism behind the reported shake/blur/ghosting under fast motion. This is a genuine attempted fix based on documented .NET Marshal API semantics, not blind guessing — but like v1.2.42, it still needs a real test run to confirm `V2_SLOT_RENDERER_FALLBACK` no longer fires and GPU rendering actually engages.
>
> **Logging upgraded** to make this diagnosable end-to-end on the next run without needing a live debugger: logs the successful base-IID QueryInterface HRESULT, wraps the `GetTypedObjectForIUnknown` step in its own try/catch with a distinct failure message, and logs a per-slot GPU-vs-WPF renderer summary alongside the actual preview-fps cap applied at recording start (`V2_RECORDING_PREVIEW_FPS_CAP slots=N cap=N renderers=[cam1=..., cam2=...]`).

### Fixed
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — `CreateSwapChain` now wraps the already-QueryInterface'd swap chain pointer via `Marshal.GetTypedObjectForIUnknown` instead of an implicit `(IDXGISwapChain1)` cast on a `GetObjectForIUnknown`-created RCW, which was triggering a second, failing COM QueryInterface under .NET's interop layer. Unverified pending a real hardware test run.

### Added
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — logs the base-IID QueryInterface success HRESULT and isolates the subsequent typed-wrap step in its own try/catch with a distinct failure message.
* **`capture/video_engine_v2/VideoEngineV2.cs`** — new `GetSlotPreviewRenderer(slot)` accessor.
* **`MainWindow.xaml.cs`** — logs `V2_RECORDING_PREVIEW_FPS_CAP` with the per-slot GPU/WPF renderer breakdown and the applied preview-fps cap whenever recording starts.

### Tests
* Updated `BackendVersion` assertion to `1.2.47`. 295 tests passing (unchanged — the GPU-path fix isn't unit-testable without real D3D11 hardware).

## [v1.2.46] - 2026-07-04 (build 266)

> **Fixed real recording-finalization race condition found in a fresh 4-camera test batch (test1_15fps/30fps/60fps).** `cam4` in the 30fps session never renamed from `cam4.tmp.mp4` to `cam4.mp4` — `writerStatus=RenameFailed`, `"The process cannot access the file because it is being used by another process."` Root cause: `VideoEngineV2.StopAllSlotsRecordingAsync` stops all active camera slots concurrently via `Task.WhenAll`, and each slot's `StopSlotRecordingAsync` immediately attempted `File.Move(temp, final)` right after its own `pipeline.StopRecordingAsync()` returned — zero delay, zero retry, single generic `catch (Exception)`. With 3 identical-model j5 webcams finalizing simultaneously under 4-camera load, one MediaFoundation sink's file handle hadn't fully released yet when its `File.Move` fired — a timing race, not a deterministic failure (the other 3 cameras in the same session finalized fine). Fixed by wrapping the rename in a retry loop (up to 5 attempts, `100ms * attempt` backoff) that retries specifically on `IOException` (file-in-use) while still failing immediately on any other exception type (e.g. permissions) where retrying wouldn't help. Manually recovered the stranded file from this session (`test1_30fps_20260704_174343\cam4\cam4.tmp.mp4` → `cam4.mp4`) and verified via ffprobe it's a complete, valid recording (h264, 1920x1080, 61.06s, 1812 frames) — no data was lost, only the auto-rename step failed. Neither `VideoEngineV2.cs` nor the legacy `MediaFoundationVideoFileWriter.cs` rename path is STABLE_CORE_V1-protected. Separately confirmed (no bug, expected behavior): at 60fps, the OBSBOT Meet SE genuinely achieves 60fps while all 3 j5 webcams cap at 30fps (same hardware ceiling as prior single-camera testing) — produces a large (3536-frame) but honestly-reported `PASS_WITH_WARNING` frame-count spread within that session, not a defect.

### Fixed
* **`capture/video_engine_v2/VideoEngineV2.cs`** — `StopSlotRecordingAsync`'s temp→final `File.Move` now retries up to 5 times with increasing backoff on `IOException` specifically, instead of failing permanently on the first file-in-use error during concurrent multi-camera finalization.

### Tests
* Updated `BackendVersion` assertion to `1.2.46`. 295 tests passing (unchanged).

## [v1.2.45] - 2026-07-04 (build 265)

> **Trimmed the recording FPS option list from `{15, 24, 30, 60, 100, 120}` to `{15, 30, 60}`.** Prompted by a real 6-session test batch (15/24/30/60/60/60 requested — the last two intended-100/120 sessions both landed at 60fps) that showed the j5 Webcam JVU250 caps out at 30fps for 1080p regardless of what's requested above that. While tracing why the metadata showed "Requested FPS: 60" even for what should've been 100/120fps attempts, found `MainViewModel.ApplyCaptureSettings` already silently clamps every fps selection to `Math.Clamp(fps, 5, 60)` before it reaches the encoder or the metadata writer — so 100/120fps were never actually reachable through the UI in the first place, just silently downgraded with no user-visible warning. Removed the two dead options (100/120) along with 24fps (rarely a native webcam mode; most non-30fps requests already fall back to 30fps per the `CameraFormatSelectorV2` ladder), keeping the three values that are both genuinely selectable end-to-end and common across consumer webcams. `MainWindow.xaml.cs`'s `FpsBox` population array and default `SelectedIndex` updated accordingly. Deliberately left `VerificationCaptureProfile.KnownFps` (`[15, 24, 30, 60]`, STABLE_CORE_V1-protected) untouched — a superset of the new UI list causes no functional issue and touching a frozen-core file isn't warranted for this change. `MainViewModel.cs`'s existing `Math.Clamp(fps, 5, 60)` also left as-is (upper bound already matches the new max; the silent-clamp UX gap itself is a separate concern, not fixed here).

### Changed
* **`MainWindow.xaml.cs`** — `BuildVideoSettingsCombos()`'s `FpsBox` options reduced from `{15, 24, 30, 60, 100, 120}` to `{15, 30, 60}`; default `SelectedIndex` updated from `2` to `1` to keep pointing at 30fps.
* **`localization/en.json`, `localization/ja.json`** — `fpsBoxTooltip` still advertised the removed 24/100/120 fps options ("15/24/30 fps — standard... 60/100/120 fps — high-speed..."); updated to describe only the surviving 15/30/60 fps split in both languages.

### Tests
* Updated `BackendVersion` assertion to `1.2.45`. 289 tests passing.

## [v1.2.44] - 2026-07-04 (build 264)

> **Fixed: camera preview showed as a small floating image in an otherwise black panel after changing FPS mid-session.** User reported this with 5 screenshots at 480p cycling through 60/15/120/100/24fps — 4 panels filled correctly, one (15fps) showed a tiny image adrift in a mostly-black cell. Traced to two related gaps in the GPU (D3D11) preview path. First: every settings-triggered reopen (`ReapplyV2VideoSettingsToActivePreviewAsync`, added v1.2.39) creates a brand-new `D3D11PreviewPanel`, and that panel's letterbox aspect ratio only ever got corrected reactively via `SizeChanged` (doesn't fire if the container is already the same size as before a reopen) or `CameraResolutionKnown` (only fires once the first real frame arrives, which can lag — especially at unusual FPS values where camera negotiation takes longer). In that gap the panel's `HwndHost` child sat at its own tiny natural/placeholder size instead of filling the cell. Second, and the actual root cause of the *wrong aspect ratio* specifically: `D3D11PreviewPanel` always defaulted to a 16:9 aspect ratio until corrected, and the call chain feeding it (`MainWindow.OpenV2SlotAsync` → `VideoEngineV2.PrepareSlotPreviewAsync` → `Direct3DPreviewRenderer.Initialise`) never passed the user's actually-selected resolution — it silently fell back to the fixed `VideoEngineSettings.PreviewWidth/Height` (1280x720, 16:9) default, which is wrong for any 4:3 selection like 480p (640x480). Fixed three ways: (1) `D3D11PreviewPanel`'s constructor now accepts an optional initial width/height and seeds its aspect ratio from that immediately, instead of always starting at 16:9; (2) `Direct3DPreviewRenderer.Initialise` (which already receives the correct preview width/height) now passes them through to the panel; (3) `MainWindow.OpenV2SlotAsync` now explicitly passes `VideoEngineSettings.DefaultPreferredWidth/Height` (the user's real selection) into `PrepareSlotPreviewAsync` instead of leaving it to the generic 1280x720 default. Together this means a freshly (re)opened GPU preview panel is correctly sized and correctly proportioned from the very first layout pass, not just eventually once a frame arrives. Also added a `Loaded` handler to `D3D11PreviewPanel` so it sizes itself proactively as soon as it has real layout dimensions, closing the reactive-event gap regardless of aspect ratio correctness. Separately verified (no change needed): the WPF/CPU fallback preview path (`Direct3DPreviewRenderer`'s `WriteableBitmap`) already self-corrects on every frame via `MainWindow.OnV2SlotFrameRendered`'s reference-equality check against `GetSlotPreviewBitmap`, so a bitmap replaced mid-session (e.g. after a `needsResize`) is always picked up within one frame — this path was not the source of the reported bug. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`capture/video_engine_v2/D3D11PreviewPanel.cs`** — Constructor now accepts an optional initial width/height to seed the letterbox aspect ratio immediately instead of always defaulting to 16:9; added a `Loaded` handler so the panel sizes itself proactively instead of only reacting to `SizeChanged`/`CameraResolutionKnown`.
* **`capture/video_engine_v2/Direct3DPreviewRenderer.cs`** — Passes its already-known preview width/height through to `D3D11PreviewPanel` at construction time.
* **`MainWindow.xaml.cs`** — `OpenV2SlotAsync` now passes the user's actually-selected resolution (`VideoEngineSettings.DefaultPreferredWidth/Height`) into `PrepareSlotPreviewAsync` instead of relying on the generic fixed 1280x720 preview default, fixing incorrect aspect-ratio/sizing for non-16:9 selections (e.g. 480p) on every camera-slot (re)open.

### Tests
* Updated `BackendVersion` assertion to `1.2.44`. 289 tests passing.

## [v1.2.43] - 2026-07-04 (build 263)

> **Major fix: selecting a low resolution with a non-30fps rate silently recorded at 1080p anyway.** Audited a new test batch (test24-40) covering 1080p/720p/480p combined with 15/24/30/60fps. Found a severe, confirmed-via-real-recordings bug: requesting 480p or 720p at 60fps or 24fps consistently produced a 1920x1080 recording instead — the metadata's own "Requested resolution: 480p" vs "Selected resolution: 1920x1080" mismatch made this directly visible. Root cause: `CameraFormatSelectorV2.Select()`'s fallback logic, when the exact resolution+fps combo isn't available, fell into a resolution ladder ordered high-to-low (1080p → 720p → 360p/480p) — and since cheap webcams commonly support 1080p@30fps as a baseline mode, ANY non-30fps request at ANY resolution effectively got silently "upgraded" to 1080p, completely defeating the purpose of picking a lower resolution. Fixed by adding a new priority step between the exact-match check and the full ladder: try the *user's actual requested resolution* at 30fps first, before ever falling through to a different resolution — only falls through to the full ladder if the camera doesn't support the requested resolution at any framerate. Added 6 new regression tests (`CameraFormatSelectorV2Tests.cs`) covering this exact scenario with a simulated 30fps-only camera. Separately, while reading a freshly-Japanese-language test session's actual metadata output (the user had switched UI language), found several genuine remaining localization gaps hiding in fields that looked correctly labelled but showed English *values*: "Writer status" showed the raw English enum name ("Success") instead of a translated status; "Hardware encoder used" and "Timestamp CSV status" showed raw English backend strings ("Yes"/"Written") instead of "はい"/"書き込み済み" even though the exact same concepts were correctly translated elsewhere in the same file; and the entire `ClassifyTimingBehavior` diagnostic-notes generator (feeding the `[Timing Classification]` section) had zero localization at all — every dynamic note ("No timing issues detected.", gap-count messages, FPS-mismatch messages) stayed English regardless of session language. Fixed all of these; classification *codes* (VFR/CFR_LIKE/etc.) deliberately kept as fixed English tokens, matching the existing PASS/WARNING/FAIL convention, since they may be matched elsewhere. Re-verified (no changes needed): the freeze fix continues to hold at 720p across all layout sizes (zero mid-session gaps, confirmed via direct ffprobe frame-timing analysis); an `mpdecimate`-flagged "duplicate frame" run turned out to be a false positive on inspection (MD5-hashed 5 consecutive frames, all different — natural sensor noise during a static scene, not app-level duplication); the diagnostics logging already added in earlier releases (`V2_OPENED`'s `fallback=` field) already correctly surfaces this fix's behavior with no further logging upgrade needed. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`capture/video_engine_v2/CameraFormatSelectorV2.cs`** — Format selection now tries the requested resolution at 30fps before falling back to a different (usually higher) resolution, fixing silent "upgrades" to 1080p when a non-30fps rate was requested at a lower resolution.
* **`MainWindow.xaml.cs`** — "Writer status", "Hardware encoder used", and "Timestamp CSV status" metadata fields now show translated Japanese values instead of raw English backend strings. `ClassifyTimingBehavior`'s dynamic diagnostic notes are now fully localized (classification codes like VFR/CFR_LIKE intentionally kept in English, matching the PASS/WARNING/FAIL convention).

### Added
* **`MultiCamApp.Tests/CameraFormatSelectorV2Tests.cs`** — New regression test file covering the resolution-fallback fix (6 tests): requested-resolution-at-30fps preservation, exact-match behavior, and full-ladder fallback when the requested resolution isn't available at all.

### Tests
* Updated `BackendVersion` assertion to `1.2.43`. 295 tests passing (was 289; +6 new).

## [v1.2.42] - 2026-07-04 (build 262)

> **Full audit of a fresh 720p test batch (test19-23, C:\Users\1\Videos\, 1/2/3/4-camera layouts) — confirms the v1.2.39-41 fixes work, and finds the actual root cause of the GPU rendering failure.** Requested resolution now correctly reads "720p" and matches "Selected resolution: 1280x720" across every layout size — directly confirmed with ffprobe against the real MP4 files (all 4 cameras genuinely 1280x720, not just metadata claims). Zero mid-session timestamp gaps across all cameras in all 5 sessions, re-verified directly against encoded frame timing (max delta 48ms, zero irregular deltas) — the v1.2.37 freeze fix continues to hold at 720p. Ran ffmpeg's `mpdecimate` filter for a rigorous duplicate-frame check; an initial ~93-frame "duplicate" count in a 1850-frame clip was investigated further by comparing MD5 hashes of 5 consecutive frames from the flagged region — all 5 had different hashes and file sizes, confirming these are genuinely distinct captures of a relatively static scene (natural sensor noise), not app-level duplicated frames. Visually inspected extracted frames: colors, sharpness, and framing all correct at 720p across two different camera models (j5 Webcam JVU250, OBSBOT Meet SE). Confirmed zero `CAPABILITY_PROBE_ERROR` occurrences (v1.2.38 fix holding) and a clean `UiFreezeWatchdog` result (`freezeCount=0`) across the whole batch. **Found and fixed the actual root cause of the 100%-fallback GPU rendering issue flagged in v1.2.38.** The v1.2.38 diagnostics upgrade (explicit `QueryInterface` + HRESULT logging) paid off immediately: the fresh test batch's log showed the exact failure is `hresult=0x80004002` (`E_NOINTERFACE`) when querying for `IDXGISwapChain1`. Tracing the C# interop declaration showed the interface only ever calls methods within the *base* `IDXGISwapChain`'s vtable range (`Present`/`GetBuffer`/`ResizeBuffers`, slots 8-17) — never any of `IDXGISwapChain1`'s own extended members (slots 18+, e.g. `Present1`/`GetDesc1`). The code never actually needed `IDXGISwapChain1` at all. Fixed by querying for the base `IDXGISwapChain` IID (`310d36a0-d2e7-4c0a-aa04-6a9d23b8886a`) instead, which is guaranteed to be supported by any swap chain regardless of which DXGI factory version created it. This is a genuine attempted fix, not just more diagnostics — but GPU rendering itself still needs a fresh test run to confirm it now actually initializes (look for `V2_RENDERER_INIT renderer=D3D11` with no following swap-chain-init-failure line). Exposure/shutter control remains "Not supported by device/driver" for all cameras — this is an unchanged, pre-existing hardware/driver limitation, not a regression. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`capture/video_engine_v2/D3D11Interop.cs`, `D3D11SwapChainHost.cs`** — The swap-chain interface query now requests the base `IDXGISwapChain` GUID instead of `IDXGISwapChain1`'s (which the code never actually needed and which failed with `E_NOINTERFACE` on the audited machine), fixing the underlying cause of the 100% GPU-rendering-fallback issue found in v1.2.38.

### Tests
* Updated `BackendVersion` assertion to `1.2.42`. 289 tests passing.

## [v1.2.41] - 2026-07-04 (build 261)

> **Fixed: "Requested FPS" in metadata output was always the negotiated/fallback FPS, not what the user actually selected.** Follow-up audit after v1.2.39/v1.2.40's resolution fixes, checking that every resolution (1080p/720p/480p) and every FPS option (15/24/30/60/100/120) works correctly across all camera layouts, per user request. Found the same "requested vs. selected" conflation bug I'd already fixed for resolution also existed for FPS: the metadata's "Requested FPS"/"Requested frame rate" fields (in `[Video Settings]`/`[Camera — camN]`) and "Selected camera format FPS"/"Writer/container FPS" fields all read from the exact same variable (`selFmt.NominalFps`, the camera's negotiated format) — meaning all these differently-labeled fields always showed the identical number. A user who explicitly selected 60/100/120 fps on a camera that doesn't support it (falls back to a lower fps via the format selector's priority ladder) would see "Requested FPS: 30" in their output, silently hiding that a fallback occurred. Fixed the `[Video Settings]`/`[Camera]` section's "Requested" fields (txt and json) to read from `VideoEngineSettings.DefaultPreferredFps` (the true original selection) instead, while deliberately leaving the separate `[Timing Models]` section's own "Requested FPS" unchanged — that one feeds directly into an adjacent nominal-duration calculation (`frames / requestedFps`) and must stay self-consistent with the negotiated value used in that math, not the original request. Also verified (read-only, no changes needed): the GPU swap-chain sizing logic already dynamically adapts to whatever resolution frames actually arrive at (not hardcoded to 1080p); the v1.2.39 layout-reapply fix already scopes to `_vm.State.CameraLayout` generically, so it applies uniformly across 1/2/3/4-camera layouts; the Japanese/English label localization (`J(...)` helper) is correctly driven by the locked session language and was untouched by this fix — only the underlying values changed, not the label wrapper. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`MainWindow.xaml.cs`** — `WriteV2SlotMetadataAsync`'s "Requested FPS"/"Requested frame rate" fields (txt and json, `[Video Settings]`/`[Camera]` sections) now reflect the actual user-selected FPS instead of the negotiated/fallback value shared with "Selected"/"Writer" FPS fields.

### Tests
* Updated `BackendVersion` assertion to `1.2.41`. 289 tests passing.

## [v1.2.40] - 2026-07-04 (build 260)

> **Fixed: metadata output always claimed "1080p" regardless of actual recording resolution.** Follow-up to v1.2.39's fix (which made resolution/FPS selection actually apply to the camera). User asked to confirm metadata output is correct for 1080p, 720p, and 360p. Found the per-camera metadata writer (`WriteV2SlotMetadataAsync`) had the literal strings `"1080p"` and `"1920x1080"` **hardcoded** for the "Requested resolution" field in all four places it's written (human-readable `.txt`, machine-readable `.json`, both the `[Video Settings]` session-level line and the per-camera `[Camera — camN]` line) — completely independent of what the user actually selected or what the camera was actually opened at. This explains why the v1.2.38 audit found "Requested resolution: 1080p" in all 19 test sessions even before the v1.2.39 root cause was understood: the metadata text itself could never have said anything else, even for a genuinely successful 720p or 360p recording. Fixed by computing the label/WxH string from `VideoEngineSettings.DefaultPreferredWidth/Height` (the same source `CameraPipelineV2.OpenAsync` itself reads) instead of a literal. Separately, while auditing this, found and fixed a real mislabeling bug: the UI's "360p" preset actually requested 640x480 (VGA, 4:3 aspect) — not true 360p (640x360, 16:9, matching 720p/1080p's aspect ratio). A second, unused piece of scaffold code elsewhere in the project (`ResolutionPresets` in `V2SelectionHardeningModels.cs`, never wired into production) already had the technically-correct 640x360 definition, confirming this was an oversight. Asked the user how to resolve the conflict: change actual capture behavior to true 640x360 risking a camera without that exact mode falling back to a higher resolution via the format-selector's priority ladder (unverifiable without real hardware), or keep the proven-reliable 640x480 and just fix the label. User chose the safe option — relabelled to "480p", capture behavior unchanged. A backward-compatible alias still accepts the old "360p" string in `CaptureResolutionPreset.TryFromLabel` for any legacy callers. Updated two docs (`INSTALLATION.md`, `docs/user_guide/hardware_diagnostics.md`) that told users to select "360p" from the UI, which no longer exists as a label. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`MainWindow.xaml.cs`** — `WriteV2SlotMetadataAsync`'s "Requested resolution" field (txt and json, session-level and per-camera) now reflects the actual requested resolution instead of a hardcoded "1080p"/"1920x1080".
* **`capture/CaptureResolutionPreset.cs`** — The "360p" UI preset (640x480) relabelled to the accurate "480p"; capture behavior unchanged. `TryFromLabel` still accepts the old "360p" string as a backward-compatible alias.

### Changed
* **`INSTALLATION.md`, `docs/user_guide/hardware_diagnostics.md`** — Updated "360p" references to "480p" to match the corrected UI label.

### Tests
* Updated `BackendVersion` assertion to `1.2.40`. Updated `VerificationCaptureProfileTests.FormatDisplayLabel_returns_preset_labels`'s 640x480 expectation from "360p" to "480p" to match the intentional relabel. 289 tests passing.

## [v1.2.39] - 2026-07-04 (build 259)

> **Fixed: changing resolution/FPS after Start Preview silently never took effect — root cause of "720p never actually records as 720p".** User reported selecting 720p in the UI and recording, but the output was never actually 720p in any camera layout; asked to also verify 360p and every available FPS option. Traced the bug precisely: the dropdown's `SelectionChanged` handler correctly updates `VideoEngineSettings.DefaultPreferredWidth/Height/Fps` immediately, but the "reapply to active preview" mechanism triggered afterward (`MainViewModel.ReapplyCaptureSettingsToActivePreviewAsync`) only operates on the legacy `CameraSlotPipeline` objects (STABLE_CORE_V1) — which are never opened when VideoEngineV2 is the active engine (the exclusive/default recording path since v1.2.22-alpha). For every real V2 session this reapply call was a complete, silent no-op: the dropdown visually showed the new selection and the static settings updated, but the *already-open* V2 camera kept its original capture format, and `CameraPipelineV2.StartRecordingAsync` reads the recording resolution directly from that already-open format (`_captureService.ActiveFormat`) — so a resolution/FPS change made anytime after Start Preview was silently discarded. This matches the confirmed audit finding that all 19 test sessions recorded 1080p regardless of intent. Fixed by adding a V2-aware reapply path (`MainWindow.ReapplyV2VideoSettingsToActivePreviewAsync`) that closes and reopens each currently-previewing V2 slot — reusing the exact same `OpenV2SlotAsync` sequence Start Preview itself already uses — so the new resolution/FPS actually takes effect via `CameraPipelineV2.OpenAsync`'s existing (and already correct) "read current `VideoEngineSettings` when no explicit format is passed" behavior. Applies uniformly to every resolution (360p/720p/1080p) and every FPS option (15/24/30/60/100/120) since they all flow through the identical code path — same root cause, same fix. Only triggers while merely Previewing (not Recording, which already correctly shows a "restart required" hint) and only for slots that are actually open. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`MainWindow.xaml.cs`** — Resolution/FPS dropdown changes made after Start Preview now actually apply to VideoEngineV2 cameras (previously silently ignored for every real recording session, since the existing reapply mechanism only handled the unused legacy pipeline). New `ReapplyV2VideoSettingsToActivePreviewAsync` closes and reopens each active preview slot with the newly-selected settings.

### Tests
* Updated `BackendVersion` assertion to `1.2.39`. 289/289 tests passing.

## [v1.2.38] - 2026-07-04 (build 258)

> **Full audit of 19 test sessions (test1-18, C:\Users\1\Videos\, systematic 1/2/3/4-camera matrix, all 1080p/30fps) — confirms the v1.2.37 fix worked, and finds two new issues.** Every session across all four camera-layout sizes recorded successfully: 0 FAIL results, 0 writer errors, 0 unfinalized recordings, and — critically — **0 mid-session timestamp gaps in all 38 individual camera recordings**, up from real gaps observed pre-fix (v1.2.34 audit). Directly re-verified via ffprobe on a 4-camera session's actual MP4 (not just the self-reported CSV) — zero irregular frame-timing deltas across the full recording, confirming the freeze fix is working. No camera-slot-specific (cam1 vs cam2 vs cam3 vs cam4) or layout-size-specific failure pattern found anywhere. The recurring `PASS_WITH_WARNING` verdict in multi-camera sessions is fully explained and confirmed correct, not a bug: sessions mixing the OBSBOT Meet SE (~29.98fps average) with j5 Webcam JVU250 units (~29.67fps average) show a ~19-22 frame count spread over a ~60s recording — matching the predicted 0.31fps × 60s ≈ 18.6 frames almost exactly. This is an honest report of genuine hardware frame-rate differences between camera models, not an app defect; sessions using only one camera model show near-zero spread. Confirmed the known `cam1_metadata.json`/`metadata.json` duplicate pair (legacy `VideoScanner.cs` compatibility, STABLE_CORE_V1) is still present and still byte-identical — intentional, not a new bug. **Could not compare 1080p vs 720p stability as requested — all 19 sessions in this batch were recorded at 1080p; no 720p test data exists to compare against.** Found two new issues while checking the runtime log: (1) the GPU (Direct3D11) preview renderer **failed to initialize in all 38 camera-slot inits across the entire batch** — every single preview silently fell back to the CPU/WPF rendering path, with only a bare `InvalidCastException: Specified cast is not valid` logged (no HRESULT, no detail). This is a major, unverified-root-cause finding directly relevant to why GPU real-time rendering hasn't been engaging at all; replaced the implicit RCW cast with an explicit `QueryInterface` call and richer exception logging (HRESULT, InnerException) so the actual COM-level cause is diagnosable from the next test run instead of requiring a live debugger attach — did not attempt a blind fix of the interop itself without being able to verify against real hardware. (2) A lower-severity, already-diagnosed cosmetic bug: the Advanced Camera Controls capability probe (exposure/focus range display) threw "The object has been closed" 58 times across the batch, always for slot 0, triggered by the camera-target selector changing while that slot's camera was mid-teardown/reopen during a layout transition — fixed with a pipeline-state guard that skips the probe unless the slot is actually open; this never affected recording, only the displayed range text. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`MainWindow.xaml.cs`** — `ProbeAndShowCameraCapabilitiesAsync` now checks the target slot's pipeline state before probing, avoiding a benign-but-noisy "The object has been closed" exception during layout transitions.

### Added
* **`capture/video_engine_v2/D3D11SwapChainHost.cs`** — `CreateSwapChain` now uses explicit `Marshal.QueryInterface` (surfacing the real COM HRESULT) instead of an implicit RCW cast for `IDXGISwapChain1`; the render-thread catch block now logs `HResult` and `InnerException` too. No behavior change if the QueryInterface succeeds — this is purely a diagnostics upgrade for a swap-chain-init failure that occurred in 100% of camera-slot inits in the audited test batch.

### Tests
* Updated `BackendVersion` assertion to `1.2.38`. 289/289 tests passing.

## [v1.2.37] - 2026-07-04 (build 257)

> **Root-caused and fixed the live preview freezing bug — the biggest stability finding of this project.** User audited a 4-camera session (`test4_20260704_010719`) against Windows Camera app and, critically, provided synchronized ShareX screen recordings of both apps' live UI during equivalent sessions (`cameraapp.mp4` vs `MultiCamApp.mp4`). Running ffmpeg's `freezedetect` filter on both (a quantitative, frame-content-diff based measurement, not just visual inspection) found Windows Camera app's preview essentially never freezes (one 0.17s freeze at startup only), while MultiCamApp's preview was frozen for a **cumulative 87 of 115 seconds — 75% of the entire recording — across 114 separate freeze events**, some lasting over 7 seconds. This had been completely invisible to the app's own `UiFreezeWatchdog`, which only logged a handful of sub-1.5-second stalls for the same session — because the watchdog pings the WPF dispatcher, and the dispatcher was never actually blocked. Traced the real cause: `FrameTimestampMonitor.RecordFrame()` ran synchronously on each camera's frame-arrived callback thread — the exact same thread that hands each frame to the GPU preview renderer immediately afterward — and performed **synchronous disk I/O** (`StreamWriter.WriteLine` + a `Flush()` every 10 frames, ~333ms cadence) directly on that thread. With 4 cameras each doing this independently, any transient disk latency (antivirus real-time scanning, search indexer, ordinary disk contention — all common on consumer PCs) on any one camera's CSV write blocked that camera's frame delivery to the renderer for the duration of the stall; the render thread just kept the last successfully-presented frame on screen, invisible to dispatcher-based freeze detection. Fixed by moving all CSV file I/O off the frame-arrived hot path onto a dedicated background task, connected via an unbounded `System.Threading.Channels.Channel<string>`: `RecordFrame()` now only does timing math and string formatting (fast, no I/O) and hands the row to the channel, which a background task drains and writes/flushes independently — a slow disk can now never block frame delivery to the renderer. CSV file format/schema is byte-for-byte unchanged; only *when* and *on what thread* each row physically gets written to disk changed. This is exactly the kind of architecture professional real-time camera apps (including Windows Camera app) already use — never do blocking I/O on the capture-to-render hot path. Could not verify the fix with real hardware in this environment; recommend the user re-run the same test4-style 4-camera recording + simultaneous screen recording to confirm the freeze pattern is gone. This fix may also explain — not yet confirmed — the "phantom timestamp CSV gaps" finding from v1.2.34 (timestamp CSV reporting frame gaps absent from the actual encoded MP4); callback-thread stalls delaying when `RecordFrame()` even executes would directly explain jittery wall-clock-derived interval statistics independent of true camera delivery timing. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`capture/video_engine_v2/FrameTimestampMonitor.cs`** — Per-frame timestamp CSV writing moved off the camera frame-arrived callback thread onto a dedicated background task via `Channel<string>`, eliminating a synchronous-disk-I/O stall that could freeze the live preview for multiple seconds per occurrence, invisible to `UiFreezeWatchdog`. `OpenCsv`/`RecordFrame`/`CloseCsv` all updated; CSV file format unchanged.

### Tests
* Updated `BackendVersion` assertion to `1.2.37`. 289/289 tests passing.

## [v1.2.36] - 2026-07-04 (build 256)

> **Logging system upgrade, part 2: recording function, metadata output, and internal video calculations.** Follow-up to v1.2.35's logging upgrade, this time targeting camera recording function, UI/frontend, backend, metadata output, and internal video-related calculations specifically. Audited logging density across the codebase and found the single biggest gap: `ComputeSessionVerificationStats` in `MainWindow.xaml.cs` — the exact calculation that decides a session's PASS/PASS_WITH_WARNING/FAIL verdict (frame-count spread, duration spread, inter-camera timing confidence) — had **zero** logging. Previously the only way to see *why* a session got flagged was to open its metadata files and manually re-derive the spread math by hand (this is exactly what was needed during the v1.2.34 real-recording audit). Also found and fixed: `V2MetadataReader.TryRead()` silently swallowed every parse exception (`catch { return null; }`), so a corrupted or unexpected-schema metadata JSON looked identical in the UI to "no metadata written yet" with no diagnostic trail; `V2RecordingVerifier.Verify()` and `V2VerificationRunner.Run()` (the Video Verification page's V2 calculation engine) had no logging despite producing the FAIL/WARN issue lists shown to the user. Before making any changes, checked file-level `STABLE_CORE_V1 protected component` banners across the codebase (49 files) to get an authoritative list of what's frozen — confirmed the legacy verification/metadata/session-comparison system (`MetadataParser.cs`, `VideoScanner.cs`, `SessionComparisonService.cs`, `VideoVerificationService.cs`, etc.) requires a formally-triggered freeze exception before any change, including log-only additions, per the documented modification rules. Asked the user how to handle this; confirmed to leave the frozen legacy pipeline alone since VideoEngineV2 is the primary recording path today and is now fully covered. UI/frontend layer (`MainViewModel.cs`, 68 existing log calls) and camera-control checkbox toggles were spot-checked and found already adequately covered — pure UI state toggles don't need dedicated logging since their eventual applied result is already logged via existing `V2_CONTROL` lines. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Added
* **`MainWindow.xaml.cs`** — `ComputeSessionVerificationStats` now logs `V2_SESSION_STATS_COMPUTED` with the full calculation result (frame range/spread, duration range/spread, inter-camera confidence, notes) at both its normal and zero-camera early-return paths.
* **`MainWindow.xaml.cs`** — `WriteV2SlotMetadataAsync`'s `V2_METADATA_WRITTEN` log line now includes the resolved duration source/value and estimated FPS/mean interval, not just the pass/fail result.
* **`verification/V2MetadataReader.cs`** — `TryRead`'s previously-silent catch block now logs the exception type/message and file name.
* **`verification/V2RecordingVerifier.cs`** — `Verify()` now logs `V2_VERIFY_ISSUES` whenever a FAIL or WARN issue is produced.
* **`verification/V2VerificationRunner.cs`** — `Run()` now logs a `V2_VERIFY_RUN` session-level summary (sessions found, per-session status and slot count).

### Tests
* Updated `BackendVersion` assertion to `1.2.36`. 289/289 tests passing.

## [v1.2.35] - 2026-07-04 (build 255)

> **Logging system upgrade for deeper debugging and system-wide investigation.** User asked for the log system to be upgraded across essential components and the main app build to support more detailed debugging. Reviewed existing coverage (`AppDiagnosticLogger` — `app_runtime_*.txt`, `recording_runtime_*.log`, `crash_or_failure_*.txt`, plus per-session trace files) across 15 files and found it reasonably dense already, but with three concrete gaps: (1) the app had no environment snapshot written into the live, ongoing runtime log — `startup_attempt.log` captured basic info but gets *overwritten* every launch, so historical runs couldn't be correlated to their environment from the daily rotating log a developer would actually be tailing; (2) camera device enumeration only logged a count, not which devices were actually found, discarding useful context for device-selection bugs; (3) the Media Foundation encoder's resolved configuration (actual resolution, bitrate, codec, hardware-encoder decision) was never logged at the point it's decided (`OpenAsync`) — only a minimal line at `StartAsync` with no resolution/bitrate, forcing anyone debugging a live quality issue to wait until the session ended and metadata files were written. Fixed all three, deliberately keeping the additions cheap: no WMI/hardware-probing calls added to the startup path (that already exists on-demand via Hardware Diagnostics and would slow every launch by 150-600ms if duplicated here) — the new startup banner only cross-references `SystemProfile.latest.json`'s timestamp rather than re-querying hardware. No COM/native interop changes were made (a GPU-adapter-name-in-logs idea was considered and dropped — reaching it would require adding a new vtable method to the raw `IDXGIAdapter` COM interop interface, which is real production risk for every recording session for a marginal debugging convenience, not attempted). V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Added
* **`utils/AppDiagnosticLogger.cs`** — new `SystemBanner()` method, called once at `Application_Startup`, writes app version/build/stage, OS version, .NET runtime version, process architecture, processor count, working-set memory, UI culture, and process ID into the same rotating `app_runtime_*.txt` log used for ongoing debugging (not a separate overwritten file).
* **`capture/video_engine_v2/VideoEngineV2.cs`** — `V2_DEVICES` log line now includes the full enumerated device list (name, kind, format count, discovery source per device), not just a count.
* **`capture/video_engine_v2/MediaFoundationEncoderService.cs`** — new `V2_ENC_OPEN` log line at `OpenAsync` reporting the fully-resolved encoder configuration (requested vs. actual resolution, bitrate, codec, target FPS, hardware-encoder availability/decision) at the point it's decided, instead of only a minimal line at `StartAsync`.

### Tests
* Updated `BackendVersion` assertion to `1.2.35`. 289/289 tests passing.

## [v1.2.34] - 2026-07-04 (build 254)

> **Real-recording audit vs Windows Camera app (session tesst1_20260703_235347).** User reported blurry/ghosted frames during motion, asked whether preview/recording FPS and shutter speed differ from Windows Camera app, and whether Stop Recording is as responsive. Investigated with real evidence: extracted individual frames from the actual `cam1.mp4` during arm movement (ffmpeg) and found genuine single-frame motion blur (sharp static background, blurred moving arm — classic slow-shutter blur, not a frame-drop/ghosting artifact). Extracted a comparable frame from the user's own Windows Camera app recording of the same motion (`WIN_20260703_23_59_27_Pro.mp4`, same room/lighting) and found **comparable motion blur there too** — this is inherent indoor-lighting shutter behavior affecting both apps, not a MultiCamApp-specific defect. Verified Stop Recording is already fast: the runtime log shows all 4 cameras finalize their MP4s within ~0.3s of the click, and the UI already freezes to "stopped" state instantly (v1.2.18-alpha). Confirmed preview (~27.5fps) vs recording (~29.1fps) FPS divergence is intentional decoupled-FPS design, not a bug. Found and fixed a real, unrelated bug while investigating: `SetFlickerReductionAutoAsync` used C# `dynamic` dispatch to reach `VideoDeviceController.FlickerReductionControl`, which is **guaranteed to throw on every call** — verified this WinRT member is absent from the public metadata of every installed Windows SDK including the newest (10.0.26100.0), and CsWinRT-projected WinRT objects don't implement the COM `IDispatch` late-binding that `dynamic` needs to reach members missing from the compile-time projection, so this could never have worked. Replaced with an immediate, honest "Unsupported" result — same practical outcome, no more wasted per-camera exceptions, clearer diagnostic message. Also found: (a) exposure control read back "Unsupported" on all 4 cameras this session, including the OBSBOT Meet SE which showed real WinRT exposure support in earlier single-camera testing — inconclusive whether this is a driver/environment regression or a capability-negotiation race condition under concurrent 4-camera open (needs real-hardware testing to distinguish, not fixed blind); (b) the app's own per-frame timestamp CSV (used for scientific timing verification) reports frame-timing gaps that do **not** appear in the actual encoded MP4 — direct ffprobe frame-by-frame analysis of `cam1.mp4` and `cam4.mp4` found zero irregular frame-pacing deltas across either file's full duration, despite the CSV claiming up to 5 mid-session gaps — suggesting the monitoring thread's timestamps may not accurately reflect real encoder frame delivery, which could be producing false PASS_WITH_WARNING verdicts. Both items spawned as follow-up investigations requiring real hardware / deeper pipeline tracing rather than fixed speculatively. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`capture/video_engine_v2/CameraControlManagerV2.cs`** — `SetFlickerReductionAutoAsync` no longer uses `dynamic` dispatch against a WinRT member (`FlickerReductionControl`) that's absent from all Windows SDK metadata and was guaranteed to throw; now returns an immediate, clearly-worded `Unsupported` result instead.

### Investigated, not code-changed (see spawned follow-ups)
* Exposure control unsupported on OBSBOT Meet SE in a 4-camera session (previously verified supported standalone) — needs real-hardware A/B testing before touching latency-sensitive camera-open code.
* Frame-timestamp CSV reports gaps not present in the actual recorded MP4 — needs tracing through `V2_FrameTimestampMonitor`'s capture point relative to the real encoder pipeline.

### Tests
* Updated `BackendVersion` assertion to `1.2.34`. 289/289 tests passing.

## [v1.2.33] - 2026-07-02 (build 253)

> **Portability audit + remaining localization gaps.** Versioning convention change starting this release: version strings drop the `-alpha` pre-release suffix (was `1.2.x-alpha`, now plain `1.2.x`); the `stage` field in `version.json`/tooltips still reports maturity separately. User asked for confirmation that Hardware Diagnostics, Video Verification, GPU rendering, camera recording/preview, MP4 writing, and metadata output all work correctly on any PC/laptop with any camera hardware and across all supported camera layouts (1-4), not defaulting to one specific setup — plus that the installer is fully offline-installable. Three parallel audits ran: (1) Hardware Diagnostics + Video Verification portability — no genuine portability bugs found; all scanner/verification code already iterates over actual camera count with no hardcoded vendor/model/count assumptions, WMI calls are exception-wrapped for restricted environments, zero/one/many camera and zero/one/many GPU cases all handled. Found and fixed one low-severity relevance gap: the Hardware Diagnostics preset advisory text said "4-camera 1080p" for 3-camera layouts too — reworded to "3-4 camera 1080p". (2) GPU/recording/preview/MP4/metadata across all layouts — confirmed no hardcoded defaulting to a single layout size anywhere in the recording pipeline (`ResponsiveLayoutManager`, `D3D11SwapChainHost`, `MediaFoundationEncoderService`, `SessionSummaryWriter`, `MetadataWriter`); every camera slot is sized/encoded/rendered independently per actual active camera count, and metadata text correctly adapts wording for single-camera vs multi-camera sessions. No changes needed. (3) Installer offline self-containment — confirmed SAFE: self-contained .NET 8 runtime (`--self-contained true -r win-x64`), all app assets/config/localization bundled into the installer payload, VC++ Redistributable bundled locally (`installer/vc_redist.x64.exe`, no internet fetch), WinRT capture APIs are OS-native to Windows 10/11 (no separate SDK dependency). Separately, closed out remaining hardcoded-English gaps found by a targeted sweep of `VideoVerificationPage.xaml.cs` (summary cards, `BuildSummaryText` detail panel, `RefreshSummaryEmpty`/`RefreshSummaryScanned` placeholder states, the "No videos found" message) and `App.xaml.cs`'s single-instance startup dialog (now picks EN/JA based on OS display language, since no app LanguageManager exists yet at that point in startup). Flagged (not fixed, to avoid a risky quick patch) one deeper gap: `ScientificTimingConfidence.HighMessage`/`OriginalCaptureVerificationPolicy.FrameCountDifferenceNote` are shared constants used by both the live UI and non-UI report writers, so localizing them needs a careful follow-up rather than an inline edit. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — Preset advisory text for 3-camera layouts no longer says "4-camera 1080p"; now "3-4 camera 1080p".
* **`ui/pages/VideoVerificationPage.xaml.cs`** — ~20 hardcoded English strings in summary cards, `BuildSummaryText`, `RefreshSummaryEmpty`/`RefreshSummaryScanned`, and the no-videos-found message now localize to Japanese.
* **`App.xaml.cs`** — Single-instance startup MessageBox now shows Japanese text when the OS display language is Japanese (previously always English, since no LanguageManager exists this early in startup).
* **`MainWindow.xaml.cs`, `ui/HeaderBar.xaml.cs`** — Language-locked-during-recording tooltip now uses a proper localization key instead of an inline ternary.

### Changed
* **Versioning convention** — Version strings no longer carry a `-alpha` suffix going forward (`config/version.json`, `MultiCamApp.csproj`, `installer/MultiCamApp.iss`, `VideoEngineRegistry.cs` `BackendVersion`).
* **`localization/en.json`, `localization/ja.json`** — ~20 new keys for the Video Verification page gaps above, plus a `languageLockedDuringRecordingTooltip` key and the reworded `hwDiagPresetAdvisory`/`hwDiagPresetAdvisory1Cam`/`hwDiagPresetAdvisory2Cam` keys.

### Tests
* Updated `BackendVersion` assertion to `1.2.33`. 289/289 tests passing.

### Fixed (installer tooling)
* **`installer/extract_version_json.ps1`** — dropping the `-alpha` suffix surfaced a bug: this script only appended the build number to the numeric version (e.g. `1.2.33.253`) when the version string contained a hyphen, leaving plain versions like `1.2.33` un-suffixed. But `installer/MultiCamApp.iss`'s `AppVersionNumeric` (and thus the compiled Setup.exe's actual file `ProductVersion`) always includes the build number regardless of suffix — causing `build_release.bat`'s post-build version-match check to fail (`Setup.exe version mismatch: setup=1.2.33.253 source=1.2.33`) and the installer filename to come out wrong (`MultiCamApp_1.2.33_Setup.exe` instead of the established `MultiCamApp_1.2.33.253_Setup.exe` convention). Now always appends the build number, matching the `.iss` convention unconditionally.
* **`scripts/packaging/create_release_zip.ps1`** — had the identical hyphen-conditional bug when resolving the built Setup.exe's filename to bundle into `installer.zip`; fixed the same way.

## [v1.2.33-alpha] - 2026-07-02 (build 252)

> **Hardware Diagnostics report correctness pass.** User asked to verify the Hardware Diagnostics page's three report types (System, Camera Capability, USB Topology) actually run correctly and produce text that's suitable/relevant, plus confirm the Open Logs Folder and Copy Diagnostic Summary buttons work. Found and fixed four real issues: (1) `BuildPresetAdvisory()` always showed "Recommended for 4-camera 1080p: use separate USB ports..." advice regardless of the user's actual selected camera layout — a 1-camera user saw 4-camera-specific USB/lighting guidance that didn't apply to them; now tiered by actual layout count (1/2/vs 3-4 cameras) via `_getLayoutCount()`. (2) The USB Topology summary (`BuildUsbSummary`) discarded the real scan results and always showed a static "USB topology unavailable." placeholder — the scanner was already collecting real per-camera data (display name, camera type; the USB controller/hub field itself is honestly reported as "Unknown" since true controller/hub detection was never implemented), but the UI layer wasn't rendering it. Fixed to render the actual `UsbTopologyReport.SelectedCameras`/`Notes`/`Warnings`. (3) `BuildUsbSummary` was printing the privacy-safety note twice (once from `report.Notes`, once hardcoded) — removed the duplicate. (4) `BuildCameraSummary` silently dropped two informative scanner notes ("no stress test is run" and "focus support is reported as unavailable unless the driver exposes it") that never reached the UI; now renders `report.Notes` directly instead of a redundant hand-written advisory line. Also added `diagnostics/DiagnosticsLocalization.cs` (a static `LanguageManager` holder, since the `SystemCapabilityScanner`/`CameraCapabilityScanner`/`UsbTopologyScanner` classes run on background threads via `Task.Run` and have no direct UI reference) and localized every `Notes`/`Warnings`/`EncoderHints` string those scanners produce — previously this text was hardcoded English even when the UI language was Japanese. Verified `OpenLogsButton_Click` (opens `PathHelper.LogsFolder()`, matches `AppDiagnosticLogger.PrimaryDir`) and `CopySummaryButton_Click` (copies `_lastSummary` from the updated `BuildFullSummary` signature) both work correctly against the refactored report builders. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Fixed
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — `BuildPresetAdvisory` now takes the actual selected layout count and returns layout-appropriate advisory text instead of always assuming 4 cameras.
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — `BuildUsbSummary`/`BuildUsbStatusCard` now render the real `UsbTopologyReport` scan results instead of a hardcoded "unavailable" placeholder.
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — Removed a duplicated privacy-safety note line in `BuildUsbSummary`; `BuildCameraSummary` now surfaces all scanner notes instead of dropping two of them.

### Added
* **`diagnostics/DiagnosticsLocalization.cs`** — Static `LanguageManager` holder for background-thread diagnostics scanners.
* **`localization/en.json`, `localization/ja.json`** — ~26 new keys: layout-aware preset advisory text (1/2-camera variants) and full localization of the diagnostics scanner classes' Notes/Warnings/EncoderHints strings.

### Changed
* **`diagnostics/SystemCapabilityScanner.cs`, `CameraCapabilityScanner.cs`, `UsbTopologyScanner.cs`** — All `.Notes`/`.Warnings`/`.EncoderHints` text now localized via `DiagnosticsLocalization.T(...)` instead of hardcoded English.
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — `ApplyLanguage` now sets `Diagnostics.DiagnosticsLocalization.Current` so scanner output localizes on language switch.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.33-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.33-alpha`. 289/289 tests passing.

## [v1.2.32-alpha] - 2026-07-02 (build 251)

> **Comprehensive Japanese localization pass across all UI pages.** Prior localization work (v1.2.31-alpha) fixed the Environmental Lock/Calibration handlers and Hardware Diagnostics status messages, but real-world screenshots with Japanese selected showed the *majority* of the Advanced Camera Controls panel, the entire Video Verification page, and the entire Hardware Diagnostics page were still hardcoded English — labels, tooltips, table column headers, status/result text, and report builders. This release closes that gap: ~150 new localization keys added across `en.json`/`ja.json`, covering every static XAML label/tooltip/button in the Advanced Camera Controls panel (camera target, focus/exposure controls, white balance, environmental lock section) and the Hardware Diagnostics page (page chrome, summary cards, detailed report builders), plus the dynamic text builders in `MainWindow.xaml.cs` (manual focus/exposure availability messages, default-restore status switches, WB status) and `VideoVerificationPage.xaml.cs` (session summaries, table column headers/tooltips for both Simple and Detailed views, the full detail-panel section builder, and the ffprobe-unavailable standalone summary). `VideoVerificationPage`'s nested view-model classes (constructed without a `LanguageManager` reference throughout the file) now read a static `CurrentLanguage` property set by `ApplyLanguage()`. `HardwareDiagnosticsPage` previously had zero localization infrastructure — added an `ApplyLanguage(LanguageManager)` method with proper state tracking (`_lastScanTimeLocal`/`_scanHasRun`) so re-localizing after a scan doesn't lose the scan timestamp or falsely reset to placeholder text. Deliberately left as fixed English (matching the app's "except numerical, default camera names, and original english names" scope): `CAM1`/`CAM2`/etc. slot labels, the `PASS`/`WARNING`/`FAIL` verdict codes used internally for status-color matching (translating these would have broken `ToSimpleRecommendedAction`'s switch matching), and the About page's academic citation text (citations conventionally stay in one canonical format). Also fixed a real bug found in the same review: `CameraSlotPipeline.ApplyExposureModeAsync` was silently skipping the low-light-compensation disable whenever "Auto Exposure" was checked, because the LLC-disable code was nested only inside the manual-exposure branch — moved it to run regardless of exposure mode. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`capture/CameraSlotPipeline.cs`** — `ApplyExposureModeAsync`: low-light-compensation disable now applies regardless of auto/manual exposure mode.
* **`MainWindow.xaml`** — Advanced Camera Controls panel: every static label/tooltip/checkbox/button given `x:Name` and localized (was ~25 hardcoded English strings).
* **`MainWindow.xaml.cs`** — `RefreshTexts()` now localizes the full Advanced Camera Controls panel; `UpdateManualFocusAvailability`, `BuildExposureStatusText`, `DefaultFocusButton_Click`/`DefaultExposureButton_Click` status switches, and WB status messages now use localization keys instead of hardcoded English.
* **`ui/pages/VideoVerificationPage.xaml`** — All group titles, card labels, and static text given `x:Name` and localized.
* **`ui/pages/VideoVerificationPage.xaml.cs`** — Added static `CurrentLanguage` for nested view-model classes; localized session summaries, table column headers/tooltips (both views), detail-panel builder, and V2-standalone summary text.
* **`ui/pages/HardwareDiagnosticsPage.xaml`** — All static text given `x:Name`.
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — `ApplyLanguage` now fully localizes page chrome and report builders; added scan-state tracking to prevent language-switch regressions.
* **`localization/en.json`, `localization/ja.json`** — ~150 new keys added.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.32-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.32-alpha`. 289/289 tests passing.

## [v1.2.31-alpha] - 2026-07-02 (build 250)

> **Advanced Camera Controls, Japanese localization, and repo cleanup audit.** Three parallel audits: Advanced Camera Controls section (no critical bugs, but found one real inconsistency), Japanese localization (25+ hardcoded English strings, outdated font), and repo file hygiene (~25.5 GB of disposable build/test artifacts). Fixed: `CameraSlotPipeline.ApplyExposureModeAsync` was silently skipping the low-light-compensation disable whenever "Auto Exposure" was checked (LLC-disable code was nested only inside the manual-exposure branch), even though the returned status falsely claimed it was requested — moved LLC-disable to run regardless of auto/manual exposure mode. Added 24 new localization keys and wired up `_vm.Language`/`L[...]` lookups to replace hardcoded English in the Environmental Lock/One-Shot Calibrate handlers (`MainWindow.xaml.cs`) and Hardware Diagnostics status messages (`HardwareDiagnosticsPage.xaml.cs`, which previously had zero localization support — added an `ApplyLanguage` method and wired it into the app's language-refresh flow). Replaced the outdated "MS Gothic" bitmap font used for Japanese UI with a modern fallback chain (`Yu Gothic UI, Meiryo UI, Meiryo, Segoe UI, Arial`). Fixed `docs/OUTPUT_FILES_AND_METADATA.md`'s incorrect claim that metadata is always English — VideoEngineV2's `.txt` metadata and embedded status strings actually do follow the session's UI language (locked at recording start); only the legacy OpenCV-fallback `MetadataWriter.cs` path and all JSON schema field *names* stay English. Reconciled inconsistent citation text (author list/wording) across the About page, README.md, and INSTALLATION.md. Cleaned up ~25.5 GB of disposable build/test artifacts: old `.build/dist-*` staging folders, 10 superseded installer `Setup.exe` builds, and `data/temp/` trial recordings from 2026-05-27 — see repo for current state. Fixed a `.gitignore` gap where versioned installer EXEs weren't covered by the ignore pattern. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`capture/CameraSlotPipeline.cs`** — `ApplyExposureModeAsync`: low-light-compensation disable now applies regardless of auto/manual exposure mode (was silently skipped when Auto Exposure was checked).
* **`MainWindow.xaml.cs`** — Environmental Lock/One-Shot Calibrate handlers now use `_vm.Language[...]` lookups instead of hardcoded English; `RefreshTexts()` now localizes the Lock/One-Shot button labels and tooltips, and calls `HardwareDiagnosticsPage.ApplyLanguage`.
* **`ui/pages/HardwareDiagnosticsPage.xaml.cs`** — Added `ApplyLanguage(LanguageManager)`; status messages and the Basic Display Adapter warning dialog now localized.
* **`utils/FontHelper.cs`** — Japanese font changed from `MS Gothic` to `Yu Gothic UI, Meiryo UI, Meiryo, Segoe UI, Arial`; English font given an explicit `Segoe UI, Arial` fallback chain.
* **`localization/en.json`, `localization/ja.json`** — Added 24 new keys for calibration status, Environmental Lock/One-Shot button labels and tooltips, and Hardware Diagnostics status messages.
* **`ui/AboutPage.xaml.cs`, `README.md`, `INSTALLATION.md`** — Citation text reconciled to consistent author list and wording.
* **`docs/OUTPUT_FILES_AND_METADATA.md`** — Corrected the metadata-language claim to reflect actual VideoEngineV2 vs. legacy-pipeline behavior.
* **`.gitignore`** — Added `installer/*_Setup.exe` pattern (previously only the bare `Setup.exe` was covered).
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.31-alpha`.

### Removed
* Old `.build/dist-*` staging folders (kept only the latest), 10 superseded `installer/MultiCamApp_*_Setup.exe` builds, stale bare `installer/Setup.exe`, and `data/temp/` trial recordings from 2026-05-27 (~25.5 GB total).

### Tests
* Updated `BackendVersion` assertion to `1.2.31-alpha`. 289/289 tests passing.

## [v1.2.30-alpha] - 2026-07-02 (build 249)

> **Removed broken "(unknown)" device-name footer from camera preview panels; verified Video Verification and Hardware Diagnostics pages need no changes.** The small text at the bottom of each camera panel (device-name footer bar) read `CameraSlotPipeline.DeviceName`, which falls back to `"(unknown)"` when `_deviceInfo` isn't populated — this was showing even during active, successful recording. Root cause not fully isolated (would need hardware-attached debugging to confirm why `DeviceInformation.CreateFromIdAsync` doesn't end up populating it for the default V2 path), but the bar was redundant regardless — the camera's device name is already shown in the device-selector dropdown above each panel. Removed the footer bar entirely (border, text block, layout row, and the two theme brushes that only it used) rather than leave broken/redundant UI. Checked whether Video Verification or Hardware Diagnostics needed updates for today's earlier metadata/flicker-reduction changes: Video Verification's `V2MetadataReader`/`V2VerificationRunner` already parse and display every camera control's `warning` field generically via `JsonElement.EnumerateObject()` (not a hardcoded per-control list), so it automatically surfaces the flicker-reduction and metadata fixes with zero code changes. Hardware Diagnostics is a fully independent hardware-inventory tool (system/GPU/camera-capability/USB scan) unrelated to per-recording metadata — no changes needed there either. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MainWindow.xaml.cs`** — Removed the device-name footer bar (`_deviceFooters`, `footerBar`/`deviceFooter` construction, and its `UpdatePreviewOverlayStats` update) from each camera preview panel.
* **`ui/PreviewPanelTheme.cs`** — Removed `FooterBarBackground`/`FooterForeground` brushes (only used by the removed footer).
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.30-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.30-alpha`. 289/289 tests passing.

## [v1.2.29-alpha] - 2026-07-02 (build 248)

> **Flicker reduction retry-loop fix; STABLE_CORE_V1 audit (no code changes, findings reported).** Found and fixed a real bug in `SetFlickerReductionAutoAsync`: it picked one candidate value (Auto/60Hz/50Hz) by priority from the driver's `SupportedValues` capability list and attempted `SetValueAsync` exactly once — if the driver rejected that one value (a device can list a value as supported yet still decline to apply it), the method gave up immediately without ever trying the other listed candidates. Now loops through all supported candidates in priority order and only reports failure if every one is rejected. Separately audited STABLE_CORE_V1 (no protected files modified, per freeze policy) — see conversation for full findings: the freeze declaration (`docs/STABLE_CORE_V1_FREEZE.md`) was last updated for v1.1.21 (build 214) and describes the legacy OpenCV recording pipeline (`RecordingSession.cs` → `MetadataWriter.cs`, PascalCase field schema), which is confirmed still reachable only as a fallback; the primary V2/WinRT pipeline that produces virtually all current recordings writes metadata via unprotected code in `MainWindow.xaml.cs` (`V2_METADATA_WRITTEN`, camelCase schema) that predates the freeze doc's last revision. No code change made pending user decision on whether to update the freeze documentation. V2 stable recording workflow, STABLE_CORE_V1 protected files, and CSV schema unchanged.

### Changed
* **`CameraControlManagerV2.cs`** — `SetFlickerReductionAutoAsync`: retries all supported candidate values instead of giving up after the first rejection; failure warning now lists every rejected candidate.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.29-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.29-alpha`. 289/289 tests passing.

## [v1.2.28-alpha] - 2026-07-02 (build 247)

> **Full audit of C:\Users\1\Videos\ (3 test sessions); metadata completeness fixes; v1.2.27 color tagging reverted (verified non-functional).** Audited all 3 recording sessions (`test1/2/3_20260701_*`), their MP4s, and their metadata files. Found `cam1_metadata.json/txt` and `metadata.json/txt` are byte-identical duplicates in every session — **not a bug**: `VideoScanner.cs` (STABLE_CORE_V1, frozen) hardcodes the unprefixed `metadata.json`/`metadata.txt` filenames for its scan/verify logic, so the app intentionally writes both the current prefixed names and the legacy unprefixed names for backward compatibility. Left untouched. Found a real metadata gap: `flickerReduction` (and `digitalStabilization`) were the only controls missing the `warning` field that every other control (`focus`/`exposure`/`lowLightCompensation`) includes — so a "Failed" flicker-reduction result gave zero diagnostic info. Root cause traced deeper: `SetFlickerReductionAutoAsync` never populated a `WarningMessage` when the driver's `SetValueAsync` returned false without throwing — fixed to report the target mode and rejection. Verified via ffprobe on all 3 real recordings that the v1.2.27 color-primaries/matrix/range tagging (`mp4.Video.Properties[MF_MT_*]`) has **no effect** — all 3 still show `color_space`/`color_primaries=unknown` despite being built from build 246. Reverted that code (was shipping a no-op) with a detailed comment explaining why: `MediaCapture.PrepareLowLagRecordToStorageFileAsync` does not appear to propagate extra `VideoEncodingProperties.Properties` entries into the muxed H.264 output; properly fixing this would require replacing the encoder with a custom Media Foundation Sink Writer pipeline, which was not attempted. This has no visible playback impact since `color_range` already matches Windows Camera app and untagged HD H.264 is universally interpreted as BT.709 by virtually all players. No unnecessary files found to remove — the session folders are the user's own test recordings, not orphaned artifacts. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MainWindow.xaml.cs`** — `flickerReduction`/`digitalStabilization` JSON and text metadata now include the `warning` field, matching the other controls.
* **`CameraControlManagerV2.cs`** — `SetFlickerReductionAutoAsync`: populates `WarningMessage` with the target mode and rejection reason when the driver declines to apply flicker reduction.
* **`MediaFoundationEncoderService.cs`** — Reverted the v1.2.27 `MF_MT_VIDEO_PRIMARIES`/`MF_MT_YUV_MATRIX`/`MF_MT_VIDEO_NOMINAL_RANGE` property-bag tagging (confirmed non-functional via real-hardware ffprobe); left a comment explaining what was tried and why it doesn't work through this API.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.28-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.28-alpha`. 289/289 tests passing.

## [v1.2.27-alpha] - 2026-07-01 (build 246)

> **Fix: H.264 output now tagged with color primaries/matrix/range to match Windows Camera app.** Prior audit found MultiCamApp's MP4 output left `color_space`/`color_primaries` untagged ("unknown" in ffprobe) while Windows Camera app tags `color_primaries=bt709`/`color_space=bt470bg` (both apps already agreed on `color_range=pc`). `MediaFoundationEncoderService.BuildEncodingProfile` now sets `MF_MT_VIDEO_PRIMARIES` (MFVideoPrimaries_BT709=2), `MF_MT_YUV_MATRIX` (MFVideoTransferMatrix_BT601=2 — documented by Microsoft as "also used for SMPTE 170 and ITU-R BT.470-2 System B,G", which is the value expected to produce ffprobe's `bt470bg` reading like Windows Camera app), and `MF_MT_VIDEO_NOMINAL_RANGE` (MFNominalRange_0_255=1) via the WinRT `VideoEncodingProperties.Properties` attribute bag, since these aren't exposed as first-class WinRT properties. GUIDs and enum values were verified against two independent Windows SDK header mirrors plus official Microsoft Learn API docs before being hardcoded, given a wrong GUID would fail silently. This is a container-metadata tag only — it does not change encoded pixel values, only how players interpret them. **Needs verification on real hardware**: only an actual recording can confirm the Media Foundation H.264 MFT honors these attributes and ffprobe reports the expected tags. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MediaFoundationEncoderService.cs`** — `BuildEncodingProfile`: tags `mp4.Video.Properties` with `MF_MT_VIDEO_PRIMARIES`/`MF_MT_YUV_MATRIX`/`MF_MT_VIDEO_NOMINAL_RANGE`.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.27-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.27-alpha`. 289/289 tests passing (no unit test covers actual MFT attribute honoring — requires a real recording + ffprobe to verify).

## [v1.2.26-alpha] - 2026-07-01 (build 245)

> **v1.2.25 lighting fix confirmed working; second Start Recording status flash removed.** Side-by-side audit of `test1_20260701_153308/cam1.mp4` (MultiCamApp) vs `WIN_20260701_15_18_31_Pro.mp4` (Windows Camera app), same OBSBOT Meet SE hardware: extracted frames now visually match Windows Camera app's brightness (v1.2.25's LLC-gating fix confirmed active via `lowLightCompensation.result: "Not attempted"` in the recording's own metadata). Codec/resolution/fps/profile identical (H.264 Main, 1920x1080, 30fps, `yuvj420p` full-range) between both apps; MultiCamApp bitrate ~16.7 Mbps vs Windows Camera ~17.8 Mbps (close, both near the 18 Mbps target). Two minor non-blocking differences noted: Windows Camera app tags `color_space=bt470bg`/`color_primaries=bt709` while MultiCamApp leaves these unknown/untagged (cosmetic — most players default to bt709 for 1080p regardless), and Windows Camera app records an AAC audio track while MultiCamApp is video-only by design. Timestamp audit of the new recording: 1826 CSV rows, zero gaps, ~29.98fps measured, PASS. Separately, found and removed a second "environmental lock" warning: `StartV2DefaultRecordingAsync` was calling `ShowPreviewStatus(...)` with "Recording without environmental lock — auto exposure/WB may drift..." on every Start Recording click where Environmental Lock hadn't been used first (i.e. most of the time) — a different code path than the `CalibrationStatusLabel` text removed in v1.2.25, which is why it kept appearing as a brief flash. Start Recording is now fully silent on both status surfaces. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`MainWindow.xaml.cs`** — `StartV2DefaultRecordingAsync()`: removed the `ShowPreviewStatus(...)` environmental-lock warning shown on every Start Recording click.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.26-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.26-alpha`. 289/289 tests passing.

## [v1.2.25-alpha] - 2026-07-01 (build 244)

> **Audit of `test1_20260701_150907` recording; lighting fix; Start Recording UI simplified.** Real-world recording audit (cam1.mp4, OBSBOT Meet SE) confirmed the frame remained dark throughout (verified visually via extracted frames at t=0.5s/8s/15s) despite the v1.2.24-alpha exposure-convergence fix, because `cam1_metadata.json` showed `ExposureControl` is **not supported** by this device/driver — auto-exposure ran uncontrolled the whole time regardless of app logic. Root cause was instead the app's unconditional low-light/backlight-compensation disable: it was darkening the picture on devices where exposure can't be locked anyway, buying zero reproducibility benefit. `ApplyResearchDefaultsAsync` now only disables low-light compensation when `ExposureControl.Supported` is true — when exposure is uncontrollable, LLC is left alone so the driver's own low-light handling can help. Also removed the "Hardware parameters hard-locked for dataset purity." / "Recording started (no environmental lock active...)" status text that appeared on `CalibrationStatusLabel` merely from clicking Start Recording — that label is for the Environmental Lock / One-Shot Calibrate buttons, not a Start Recording side-effect; clicking Start Recording is now silent on that label (still logged internally). Timestamp/clock audit of the same recording: 601 CSV rows, sequential frame indices with zero gaps, zero dropped-frame warnings, mean interval 33.39ms (≈29.95fps), one 66.5ms startup blip in the first few frames (harmless), UTC capture timestamps consistent with local wall clock — PASS. V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`CameraControlManagerV2.cs`** — `ApplyResearchDefaultsInternalAsync`: low-light compensation disable is now gated on `ExposureControl.Supported`; skipped (with a logged reason) when exposure can't be controlled.
* **`MainWindow.xaml.cs`** — `StartRecordBtn_Click`: removed the `CalibrationStatusLabel.Text` update on every recording start; internal diagnostic log line kept (not user-visible).
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.25-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.25-alpha`. 289/289 tests passing.

## [v1.2.24-alpha] - 2026-07-01 (build 243)

> **Fix: CAM1-class cameras (real ExposureControl support) opening underexposed.** `CameraControlManagerV2.ApplyResearchDefaultsAsync` disables auto-exposure immediately on camera open to keep exposure stable for research recordings. For cameras without WinRT `ExposureControl` support (most cheap UVC webcams) this was always a no-op — their onboard auto-exposure kept running normally. But for cameras that *do* implement `ExposureControl` (e.g. OBSBOT Meet SE), the freeze happened at the very first readback after open, before the sensor's own auto-exposure loop had any time to converge, often locking in a dark transient value for the entire session. `DisableAutoExposureAsync` now — only when `ExposureControl.Supported` — explicitly re-enables auto-exposure and waits 500 ms before freezing, giving the driver's AE loop a short settle window first. Cameras without exposure control are unaffected (zero added delay, same as before). V2 stable recording workflow, STABLE_CORE_V1 components, and CSV schema unchanged.

### Changed
* **`CameraControlManagerV2.cs`** — `DisableAutoExposureAsync()`: added `ExposureConvergenceDelayMs = 500` settle window (only when `ExposureControl.Supported`) before freezing exposure at camera open and via the "Default Exposure" button.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.24-alpha`.

### Tests
* Updated `BackendVersion` assertion to `1.2.24-alpha`. 289/289 tests passing (no hardware-dependent tests cover this path — behavior verified by code trace, not unit test).

## [v1.2.23-alpha] - 2026-07-01 (build 242)

> **Windows Camera app audit: bitrate quality parity + dead-code cleanup.** Full comparison of MultiCamApp against the Windows built-in Camera app across camera logic, exposure, focus, lighting, GPU, and MP4 writing confirmed every user-facing control (resolution, FPS, autofocus/manual focus, auto/manual exposure, low-light compensation, manual white balance in Kelvin, environmental lock, one-shot calibrate) is correctly wired end-to-end to real WinRT camera APIs — no dead UI handlers found. Default recording bitrate raised from 7.5 Mbps to 18 Mbps (`WindowsCameraLike` preset) to match Windows Camera app output quality at 1080p30. Removed confirmed-dead code left over from the v1.2.22-alpha V3/V3B backend removal. V2 stable recording workflow, STABLE_CORE_V1 components, CSV schema, and verification verdict semantics unchanged.

### Changed
* **`VideoEngineSettings.cs`** — Default `BitrateProfile`/`TargetBitrateKbps` changed from `Standard` (7,500 Kbps) to `WindowsCameraLike` (18,000 Kbps).
* **`IVideoEngineBackend.cs`**, **`RecordingFinalizeResult.cs`** — Stale comments referencing the removed V3/V3B experimental backends rewritten to describe current V2-only reality.
* **`VideoEngineRegistry.cs`** — `BackendVersion` bumped to `1.2.23-alpha`.

### Removed
* **`BackendInitDiagnostics.Scaffold()`** — unused factory method with zero callers (leftover from V3 scaffold removal).
* **`CameraControlManagerV2.SetBrightness()`** — implemented but unreachable; no UI control and no other caller.
* **`V2CameraControl.Brightness/Contrast/Gain/Zoom`** — enum values with no working implementation or caller (Contrast/Gain/Zoom were never implemented; Brightness's only use was the removed method).

### Tests
* Removed `BackendInitDiagnostics_Scaffold_Factory` (exercised removed method). Updated `V2Settings_BitrateProfile_DefaultsStandard` → `V2Settings_BitrateProfile_DefaultsWindowsCameraLike`. Updated `BackendVersion` assertion to `1.2.23-alpha`.

## [v1.2.22-alpha] - 2026-06-29 (build 237)

> **V3/V3B experimental backends removed; app runs exclusively on VideoEngineV2 Stable (Windows Camera parity).** VideoEngineV3 (22 source files) and VideoEngineV3B (15 source files) deleted. All V3/V3B routing code removed from VideoEngineRegistry, BackendIdentifiers, and MainWindow. Default config switched to `previewEngine: winrt` and `recordingEngine: windows_camera`. BackendMetadata simplified to V2-relevant fields only (V3/V3B diagnostic fields removed). V2 stable recording workflow, STABLE_CORE_V1 components, CSV schema, and verification verdict semantics unchanged.

### Removed
* **VideoEngineV3** — 22 source files in `capture/video_engine_v3/` deleted.
* **VideoEngineV3B** — 15 source files in `capture/video_engine_v3b/` deleted.
* **V3/V3B test files** — 17 test files deleted (11 V3 + 6 V3B).
* **V3 verification/metadata files** — `V3RecordingVerifier.cs`, `V3SessionSummaryAppender.cs` deleted.
* **BackendIds** — `VideoEngineV3Experimental` and `VideoEngineV3B_SinkWriterScientific` constants removed.
* **BackendMetadata** — ~110 V3/V3B diagnostic fields removed; V2-relevant fields retained.

### Changed
* **`VideoEngineRegistry.cs`** — Rewritten to V2-only (~150 lines from 1079). All V3/V3B routing, events, and BuildMetadata wiring removed.
* **`MainWindow.xaml.cs`** — All V3/V3B event handlers, preview/recording routing, stop paths, session summary appending, and JSON metadata building removed.
* **`dist/config/appsettings.json`** — `previewEngine: winrt`, `recordingEngine: windows_camera` (was both `opencv`).
* **`source/config/appsettings.json`** — Same as above.

### Tests
* **284/284 tests passing** (754 V3/V3B tests removed with deleted backends).

## [v1.2.21-alpha] - 2026-06-29 (build 236)

> **Windows Camera parity: immediate recording start and smoother preview during recording.** Three latency sources eliminated: (1) `OpenCvPreviewController` no longer runs a 3-second blocking FPS measurement before opening the VideoWriter — it uses the live preview FPS (`_liveFps`, tracked continuously during preview) directly, so OpenCV recording starts within milliseconds of click. (2) `CameraSlotPipeline.PrepareLowLagRecordingAsync` now applies focus and exposure camera controls in **parallel** (instead of sequentially) and with a **300 ms timeout** each (was 1500 ms each, potentially 3000 ms total); for cameras that do not support these controls (the common case) the parallel tasks complete in < 50 ms. (3) `StartRecordBtn_Click` now disables the button and updates UI state **before** awaiting any pending settings reapply task, so the user sees instant response at click time regardless of background state. Preview FPS cap during recording raised from 15 fps → 20 fps (below 1080p) and 10 fps → 15 fps (1080p+), matching Windows Camera preview smoothness. V2 stable recording behavior, CSV schema, and STABLE_CORE_V1 components unchanged.

### Changed
* **`OpenCvPreviewController.cs`** — Eliminated `MeasureCaptureFpsAsync()` blocking call at recording start. Writer FPS now resolved immediately from `_liveFps` (or `requestedFps` as fallback). `MeasureCaptureFpsAsync()` method retained but no longer called in the default recording path. `DefaultRecordingPreviewFpsCap` raised 15 → 20 fps; `HighResolutionRecordingPreviewFpsCap` raised 10 → 15 fps.
* **`CameraSlotPipeline.cs`** — `PrepareLowLagRecordingAsync`: focus and exposure camera controls run in parallel via `Task.WhenAll`; per-control timeout reduced 1500 ms → 300 ms. Pre-recording camera control latency reduced from up to 3 s → up to 300 ms.
* **`MainWindow.xaml.cs`** — `StartRecordBtn_Click`: `_recordUiBusy = true` and `UpdateUiStateFromCurrentState()` now called before awaiting the pending video-settings reapply task. Early-exit on `ConfirmPreRecordStorageOrCancel` properly resets `_recordUiBusy` and action buttons.

### Tests
* Updated `OpenCvRecordingQueuePolicyTests` expected values (15 → 20 for 720p, 10 → 15 for 1080p); added `ResolveRecordingPreviewFpsCap_BelowFullHd_Returns20`.
* **1038/1038 tests passing.**

## [v1.2.20-alpha] - 2026-06-29 (build 235)

> **Honest measured-FPS policy and classification.** New `MeasuredFpsPolicy` evaluator classifies timestamp-CSV measured FPS vs. requested FPS using six status levels (Pass / PassWithInfo / ConsistentLowerRealFps / DriverVfrBehavior / PassWithWarning / Fail). Policy: the app never inserts duplicate, ghost, or placeholder frames to reach the requested FPS. If the camera/driver consistently delivers slightly lower real FPS (e.g. 29.68 fps when 30 requested), this is classified as Pass — not a failure. Padding-prohibition constants (`NoArtificialFramePadding`, `NoDuplicateFramePadding`, `NoPlaceholderFrames`) are always `true`. Seven new `BackendMetadata` fields. `RecordingSelectionContext.WithMeasuredFps()` added for post-recording update. V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `MeasuredFpsPolicy.cs` — `RealFpsStabilityStatus` enum (Pass / PassWithInfo / ConsistentLowerRealFps / DriverVfrBehavior / PassWithWarning / Fail / NotEvaluated); `MeasuredFpsEvaluationResult` record (diff, percent diff, status, accepted flag, padding-prohibition constants, reason); `MeasuredFpsPolicy.Evaluate()` pure-logic evaluator (thresholds: ±0.6 fps → Pass, ±1.5 fps → PassWithInfo, ≤10% consistently lower → ConsistentLowerRealFps, driver VFR → DriverVfrBehavior, gaps/large drift → PassWithWarning or Fail; duplicate/placeholder frames → always Fail).
* `RecordingSelectionContext.MeasuredFpsEvaluation` property; `WithMeasuredFps(eval)` method for post-recording immutable update.
* 7 new `BackendMetadata` fields: `MeasuredFpsDiffFromRequested`, `MeasuredFpsPercentDiffFromRequested`, `RealFpsStabilityStatus`, `ConsistentLowerRealFpsAccepted`, `NoArtificialFramePadding` (always `true`), `NoDuplicateFramePadding` (always `true`), `NoPlaceholderFrames` (always `true`).
* 34 new unit tests in `MeasuredFpsPolicyTests`: exact/boundary FPS cases (29.5–30.1 → Pass), real-session cam1/2/3 29.68 fps → Pass, cam4 30.00 fps → Pass, PassWithInfo range, ConsistentLowerRealFps, DriverVfrBehavior (with and without VFR flag), PassWithWarning (gaps, drift), Fail (severe gaps, >15% drift), policy violations (duplicate/placeholder → always Fail), padding constants always true, zero requestedFps → NotEvaluated, diff/percent calculation, BackendMetadata defaults, `WithMeasuredFps` copy semantics, Theory test across real 4-camera session values.

## [v1.2.19-alpha] - 2026-06-28 (build 234)

> **Resolution/FPS/backend/GPU/autofocus selection hardening.** New pure-logic selection models (`ResolutionPresets`, `ResolutionSelectionPolicy`, `FpsSelectionPolicy`, `AutofocusPolicyReport`, `RecordingSelectionContext`) added in `V2SelectionHardeningModels.cs`. BackendMetadata extended with 30 new fields covering resolution preset tracking (RequestedResolutionPreset, RequestedWidth/Height, SelectedWidth/Height, ResolutionSelectionStatus, ResolutionFallbackUsed/Reason), FPS selection audit (RequestedFps, SelectedFps, WriterFps, MeasuredCameraFps, FpsSelectionStatus, FpsFallbackUsed/Reason, DriverVfrDetected), backend consistency (RequestedBackend, RecordingEngine), GPU/encoder hardening (GpuAccelerationAvailable, EncoderBackend, EncoderFallbackUsed/Reason), and autofocus policy confirmation (AutofocusControlSupported, AutofocusOffAttempted/Succeeded, AutofocusPolicyResult, ExposureControlSupported, ManualExposureUiAvailable=false, ManualFocusUiAvailable=false). `VideoEngineRegistry.SetRecordingSelectionContext()` added for callers to supply per-recording context. V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `V2SelectionHardeningModels.cs` — `ResolutionPresetDimensions` record; `ResolutionPresets` static class (1080p→1920×1080, 720p→1280×720, 360p→640×360, `ForLabel`, `IsKnownPreset`, `ForDimensions`); `ResolutionSelectionResult` record; `ResolutionSelectionPolicy.Evaluate()` (Exact/Fallback/Unavailable); `FpsSelectionResult` record; `FpsSelectionPolicy.Evaluate()` (±0.02 fps tolerance, DriverVfrDetected); `AutofocusPolicyReport` record with factory methods `NotSupported`, `FromAttempt`, `Unknown` (ManualExposureUiAvailable and ManualFocusUiAvailable always false); `RecordingSelectionContext` record; `RecordingSelectionContext.From()` factory.
* `VideoEngineRegistry.SetRecordingSelectionContext(RecordingSelectionContext)` — stores selection context for inclusion in the next `BuildMetadata()` call; `GetRecordingSelectionContext()` for inspection.
* 30 new `BackendMetadata` fields: `RequestedResolutionPreset`, `RequestedWidth`, `RequestedHeight`, `SelectedWidth`, `SelectedHeight`, `ResolutionSelectionStatus`, `ResolutionFallbackUsed`, `ResolutionFallbackReason`, `RequestedFps`, `SelectedFps`, `WriterFps`, `ContainerFps`, `MeasuredCameraFps`, `FpsSelectionStatus`, `FpsFallbackUsed`, `FpsFallbackReason`, `DriverVfrDetected`, `RequestedBackend`, `RecordingEngine`, `GpuAccelerationAvailable`, `EncoderBackend`, `EncoderFallbackUsed`, `EncoderFallbackReason`, `AutofocusControlSupported`, `AutofocusOffAttempted`, `AutofocusOffSucceeded`, `AutofocusPolicyResult`, `ExposureControlSupported`, `ManualExposureUiAvailable`, `ManualFocusUiAvailable`.
* 44 new unit tests in `V2SelectionHardeningTests`: `ResolutionPresets` (ForLabel, ForDimensions, IsKnownPreset — all 3 presets and edge cases), `ResolutionSelectionPolicy` (exact/fallback/unavailable/explicit-reason/dimension-preservation), `FpsSelectionPolicy` (exact/within-tolerance/fallback/DriverVfr/explicit-reason/zero/propagation), `AutofocusPolicyReport` (NotSupported/FromAttempt/ManualSliderNeverAvailable/ExposureControlPropagation), `BackendMetadata` default field values, `RecordingSelectionContext.From()` (resolution/fps/autofocus/backend/encoder propagation), real-session representative test (4-camera 1080p/30fps V2 session).

### Changed
* Version assertions updated in 5 test files: `1.2.18-alpha` → `1.2.19-alpha`.

## [v1.2.18-alpha] - 2026-06-28 (build 233)

> **Stop Recording UI — immediate stopped state.** After clicking Stop Recording, the main status panel now shows "Preview Active" immediately. The elapsed timer freezes at the stop-click time. "Finalizing MP4 files…", "Analyzing recording quality…", and "Stopping recording…" are no longer shown in the main status panel. Internal MP4 finalization, CSV writing, metadata writing, ffprobe audit, and visual quality analysis all still complete safely in a silent post-stop phase. Start Recording and Stop Recording buttons remain disabled during this phase; controls restore normally after finalization. V2 Stable recording behavior and STABLE_CORE_V1 are unchanged.

### Fixed
* **Stop Recording showed "Finalizing" / "Analyzing recording quality..." / "Finalizing MP4 files..." in main status panel** — root cause: `StopRecordBtn_Click` set `StatusValue.Text = "Finalizing"` and `ShowPreviewStatus(...)` was called three times inside `StopV2DefaultRecordingAsync` with transient messages. Fix: removed all three `ShowPreviewStatus` calls from the stop path; replaced the "Finalizing" status with "Preview Active" (the actual post-stop state); status panel protected from overwrite by `_isPostStopProcessing` guard during silent finalization.
* **`GetNeutralStatusText` had dead "Stopping" branch** — `_vm.StatusDisplay.StartsWith("Stopping")` path was never set via code; removed in v1.2.18-alpha.

### Changed
* `_isRecordingFinalizing` renamed to `_isPostStopProcessing` — clearer intent: UI is visibly stopped, internal finalization is silent.
* `_finalizedElapsed` renamed to `_postStopFrozenElapsed`.

### Added
* 30 new unit tests in `StopRecordingUiBehaviorTests` — covers stop-click status freeze, elapsed freeze, button state, no-Finalizing contract, no-Analyzing-recording contract, UpdateStatusDashboard guard, finalization success/exception recovery, double-click guard, V2/V3B policy contract, elapsed formatting.

## [v1.2.17-alpha] - 2026-06-28 (build 232)

> **V3B experimental 1–4 camera support + Stop Recording finalizing UI state.** V3B layout support expanded from 1-camera-only to 1–4 cameras experimental (`Alpha_v1217` singleton, `V3BRecordingPolicy.MultiCamera`). New multi-camera coordinator architecture: `V3BMultiCameraSession` (all-or-none start, best-effort stop, shared session start tick), `V3BMultiCameraVerifier` (session-level verdict), `V3BSessionTimingModel` (per-slot offsets and inter-camera spread), `V3BSlotRuntime` (per-slot recording runtime). 16 new `BackendMetadata` fields. `VideoEngineRegistry` updated to use `Alpha_v1217`. Stop Recording UI now enters a stable "Finalizing" state immediately on click: button disabled, elapsed frozen, status fixed until finalization completes. V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `V3BMultiCameraModels.cs` — `V3BSlotRuntime` (per-slot recording runtime with `ComputeDeviceIdHash`), `V3BSessionTimingModel` (per-slot offsets, inter-camera spread, reference camera selection, `Compute()` factory), `V3BMultiCameraStartResult`, `V3BMultiCameraStopResult`, `V3BMultiCameraDiagnostics`, `V3BMultiCameraVerificationResult`.
* `V3BMultiCameraVerifier.cs` — pure-logic session-level verifier. Runs per-slot `V3BVerifier.Verify()` and aggregates to session verdict: `Fail` if any slot has a fatal issue, `PassWithExperimentalWarning` if all pass, `NotRun` if no slots provided.
* `V3BMultiCameraSession.cs` — coordinator for 1–4 simultaneous `VideoEngineV3B` instances. All-or-none start policy (rolls back started slots on any failure), best-effort stop (continues even if one slot throws), shared monotonic session start tick captured before first slot.
* `V3BRecordingPolicy.MultiCamera` singleton — `MaxActiveCameras=4`, `V3BAlphaLimitation="ExperimentalMultiCamera_1to4"`, `TimestampCsvRequiredForSuccess=true`.
* `V3BLayoutSupport.Alpha_v1217` singleton — `LayoutSupportStatus="ExperimentalMultiCamera"`, `MaxSupportedActiveCameras=4`, `CurrentSupportedLayouts="1,2,3,4"`.
* `V3BFallbackReason.V3B_MaxCamerasExceeded`, `V3B_NoActiveCameras`, `SlotPrepareFailed` — new fallback reasons for multi-camera policy failures.
* 16 new `BackendMetadata` fields: `V3BMultiCameraMode`, `V3BActiveSlotCount`, `V3BDeviceIdHash`, `V3BSessionStartMonotonicTicks`, `V3BSlotStartMonotonicTicks`, `V3BSlotStartOffsetMs`, `V3BSlotStopOffsetMs`, `V3BInterCameraStartSpreadMs`, `V3BInterCameraStopSpreadMs`, `V3BSlotFinalized`, `V3BSlotAuditStatus`, `V3BSlotAuditWarnings`, `V3BSlotFrameRowsMatchWrittenFrames`, `V3BSlotContainerReadable`, `V3BSessionVerdict`, `V3BSessionNote`.
* **Stop Recording finalizing UI state** — `_isRecordingFinalizing` flag in `MainWindow`. On Stop click: button disabled immediately, elapsed frozen at click time, status set to "Finalizing", `UpdateStatusDashboard` and `ElapsedTimer_Tick` skip all updates. Flag cleared and UI restored after finalization completes or fails.
* 52 new unit tests: `V3BLayoutSupportV1217Tests` (20) + `V3BMultiCameraModelsTests` (32).

### Changed
* `V3BLayoutSupport` — `LayoutStatusFor()` now returns `"ExperimentalSupported"` / `"NotSupported"` (was `"Supported"` / `"NotYetSupported"` in v1.2.15-alpha); `NextPlannedLayout` updated to "Validation of each layout before scientific use".
* `V3BRecordingPolicy.CheckCameraCount(0)` — now returns `(false, V3B_NoActiveCameras)` instead of `(true, None)`.
* `VideoEngineRegistry` — V3B layout info now uses `Alpha_v1217`; status supplement shows "1–4 cameras (experimental)".
* Version assertions updated in 11 test files: `1.2.16-alpha` → `1.2.17-alpha`.

## [v1.2.16-alpha] - 2026-06-28 (build 231)

> **V3B 1-camera timing audit and verification hardening.** V3B post-recording timing audit models added (`V3BTimingAuditResult`, `V3BTimestampCsvAuditResult`, `V3BContainerCsvComparison`). `V3BCsvAuditor` pure-logic CSV structure auditor checks column schema, frameIndex sequence, monotonic ticks, sinkWriter sample times/durations, and v3bFrameStatus values. `V3BVerifier` extended with 7 new issue kinds including 3 new fatal checks (non-monotonic sample times, non-positive durations, writer-not-finalized-after-claimed-success). 9 new `BackendMetadata` fields. V3B audit runs automatically in `StopRecordingAsync`. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added
* `V3BTimingAuditModels.cs` — `V3BTimingAuditStatus`, `V3BTimingAuditWarning` (flags), `V3BTimestampCsvAuditResult`, `V3BContainerCsvComparison`, `V3BTimingAuditResult` with `Evaluate(Inputs)` factory.
* `V3BCsvAuditor.cs` — Pure-logic CSV structure auditor. Validates required columns, frameIndex sequence, monotonicTicks strictly increasing, appTimestamp non-decreasing, sinkWriterSampleTime monotonic, sinkWriterSampleDuration positive, valid v3bFrameStatus values, and row count vs expected. No file I/O — caller passes `IReadOnlyList<string>`.
* `V3BRecordingDiagnostics.CsvFilePath`, `CsvAuditResult`, `TimingAuditResult` — V3B audit results stored on diagnostics after stop.
* `VideoEngineV3B.StopRecordingAsync` — runs CSV audit after CSV close, builds full `V3BTimingAuditResult` after MF session diagnostics.
* 7 new `V3BVerificationIssueKind` values: `NonMonotonicSampleTimes` (fatal), `NonPositiveSampleDurations` (fatal), `WriterNotFinalizedAfterClaimedSuccess` (fatal), `FfprobeUnavailable` (warning), `ContainerFrameCountMismatch` (warning), `ConversionFailuresOccurred` (warning), `DroppedBeforeWriterOccurred` (warning).
* `V3BVerifier.VerifyInputs` extended: `WriterFinalized`, `SampleTimesMonotonic`, `SampleDurationsPositive`, `FfprobeAvailable`, `ContainerFrameCountAvailable`, `ContainerFrameCount`, `ConversionFailures`, `DroppedBeforeWriter`.
* 9 new `BackendMetadata` fields: `V3BTimingAuditStatus`, `V3BTimingAuditWarnings`, `V3BTimingAuditMessage`, `V3BCsvRowCount`, `V3BCsvRowsMatchWrittenFrames`, `V3BCsvSampleTimesMonotonic`, `V3BCsvSampleDurationsPositive`, `V3BContainerFrameCountMatchesCsv`, `V3BExperimentalValidationNote`.
* 41 new unit tests: `V3BCsvAuditorTests` (18) + `V3BTimingAuditTests` (23).

## [v1.2.15-alpha] - 2026-06-28 (build 230)

> **Metadata consistency fix + V3B expansion readiness model.** `TimestampCsvStatus` in BackendMetadata now correctly shows `Written` for successful V2 sessions (was incorrectly `Skipped` in v1.2.14-alpha). V3B `V3BLayoutSupport` pure-logic model added: 1-camera only, 2/3/4 camera `NotYetSupported`. `V3BStatusSupplement` is now surfaced in the UI backend label; shows concise multi-camera refusal message when V3B is selected with >1 active camera. V2 Stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Fixed
* **`TimestampCsvStatus = Skipped` when CSV was written** — root cause: `VideoEngineV2.StopSlotRecordingAsync` never set `RecordingFinalizeResult.TimestampCsvRows`, so `BuildMetadata` always saw `TimestampCsvRows = 0`. Fix: V2 stop path now sets `TimestampCsvRows = pipeline.TimestampMonitorFrameCount`. Defensive fallback: `BuildMetadata` also considers `FramesWrittenDuringRecording > 0` when deriving status.

### Added
* `V3BLayoutSupport` record (`V3BRecordingModels.cs`) — pure-logic expansion readiness model. `LayoutSupportStatus`, `MaxSupportedActiveCameras`, `CurrentSupportedLayouts`, `NextPlannedLayout`, `MultiCameraReadinessReason`, `IsLayoutSupported(int)`, `LayoutStatusFor(int)`, `GetUnsupportedMessage()`. Singleton `Alpha_v1215` used by registry and UI.
* `BackendMetadata.V3BLayoutSupportStatus`, `V3BMaxSupportedActiveCameras`, `V3BCurrentSupportedLayouts`, `V3BNextPlannedLayout`, `V3BMultiCameraReadinessReason` — five new metadata fields for V3B layout expansion status.
* `VideoEngineRegistry.V3BStatusSupplement` — now surfaced in `UpdateBackendStatusLabel`; shows `GetUnsupportedMessage()` when multi-camera violation is active, otherwise full V3B status lines.
* 19 new unit tests: `V2TimestampCsvStatusTests` (8 tests — Written/Skipped logic, defensive fallback), `V3BLayoutSupportTests` (11 tests — IsLayoutSupported, LayoutStatusFor, policy alignment, concise message).

## [v1.2.14-alpha] - 2026-06-28 (build 229)

> **Post-audit hardening.** Stop-recording double-click race fully hardened with `StopRecordingGuard`. Frame counter scope clarified: preview-inclusive `FramesWritten` separated from recording-relative `FramesSubmittedSinceRecordingStart` and `FramesWrittenDuringRecording`. Audit report now issues an informational note (not a warning) when the preview-inclusive counter differs from recording-only CSV/ffprobe frames. V2 Stable recording behavior unchanged. STABLE_CORE_V1 untouched. 796 tests pass.

### Fixed
* **Stop Recording double-click race** — `StopRecordBtn_Click` now enters `StopRecordingGuard` (Interlocked CAS) before disabling the button. A second click while finalization is in progress is silently ignored. Guard resets in `finally` so exceptions never leave the UI permanently disabled.
* **Audit PASS_WITH_WARNING false positive** — mismatch warning for `FramesWritten` vs timestamp CSV rows was triggered by a preview-inclusive counter that includes frames before recording started. The comparison now uses the recording-relative `FramesSubmittedSinceRecordingStart`. When CSV and ffprobe agree within tolerance the session result stays PASS; an informational note documents the preview-inclusive divergence.
* **Misleading metadata label** — `[Timing]` section now outputs `Frames written (preview-inclusive)` and separate `Frames submitted since recording start` fields. `Timestamp rows match frames written` renamed to `Timestamp rows match recorded frames` and compares against the recording-relative counter.
* **`TimestampCsvStatus = Skipped` when CSV was written** — `timestampCsvStatus` is set from `result.TimestampCsvRows > 0`; no logic change needed (rows now flow correctly from `FramesWrittenDuringRecording`).

### Added
* `StopRecordingGuard` (`utils/StopRecordingGuard.cs`) — thread-safe re-entrancy guard for the stop-recording UI flow. `TryEnter()` / `Release()` via `Interlocked.CompareExchange`. Fully unit-testable without WPF.
* `RecordingFinalizeResult.FramesSubmittedSinceRecordingStart` — encoder's recording-relative frame count (reset at `StartAsync`; excludes preview frames).
* `RecordingFinalizeResult.FramesWrittenDuringRecording` — timestamp CSV row count at recording stop (== `TimestampCsvRows`; agrees with ffprobe frame count).
* `RecordingFinalizeResult.FrameCounterScope` — `"PreviewInclusive"` for `FramesWritten` (V2), `"Unknown"` for V3/V3B.
* `CameraHealthSnapshot.FramesSubmittedSincePreviewStart` — alias for `FramesDelivered` with explicit scope documentation.
* `MediaFoundationEncoderService.FramesSubmittedSinceRecordingStart` — named alias for `FramesSubmitted`; both reset in `StartAsync`.
* `CameraPipelineV2.EncoderFramesSubmittedSinceRecordingStart` — surfaces encoder's recording-relative count to `VideoEngineV2`.
* `CameraPipelineV2.TimestampMonitorFrameCount` — surfaces `FrameTimestampMonitor.FrameCount` (CSV rows written during recording).
* `BackendMetadata.V2FramesSubmittedSinceRecordingStart`, `V2FrameCounterScope`, `V2FramesWrittenDuringRecording` — three new metadata fields for V2 frame counter scope documentation.
* 6 new unit tests: `StopRecordingGuardTests` (guard TryEnter/Release/re-entrancy), `V2FrameCounterScopeTests` (mismatch detection, informational note logic, tolerance boundary).

### Changed
* `VideoEngineRegistry.BuildMetadata` BackendVersion bumped to `"1.2.14-alpha"`.

## [v1.2.13-alpha] - 2026-06-26 (build 228)

> **Experimental alpha.** Real V3B 1-camera frame acquisition path. `V3BFrameAcquisitionSession` (MediaCapture SharedReadOnly + MediaFrameReader → BGRA32). `UseRealFrameAcquisition` flag. `OnRawFrameAcquired` computes sample times from Stopwatch ticks. 9 new acquisition metadata fields. `ConversionFailed` frame status. V3B BackendVersion 3B.3.0-alpha. 790 tests pass. V2 Stable unchanged. STABLE_CORE_V1 untouched.

### Added
* `V3BFrameAcquisitionSession` — MediaCapture (SharedReadOnly) + MediaFrameReader → BGRA32; scored format selection; `FrameCallback` delivers frames to engine.
* `VideoEngineV3B.UseRealFrameAcquisition` (default `true`) — wires real acquisition session; `false` = test mode (caller submits via `SubmitFrameAsync`).
* `VideoEngineV3B.DeviceId` — camera selection for real acquisition.
* `VideoEngineV3B.OnRawFrameAcquired` (private) — computes sampleTime100ns from recording-relative ticks; measured duration or FPS fallback.
* `V3BFrameStatus.ConversionFailed` — BGRA32 conversion failed; no CSV row written.
* `V3BFrameTelemetryAggregator.ConversionFailures` counter.
* 9 new `BackendMetadata` V3B acquisition fields + 9 new `V3BRecordingDiagnostics` fields.
* 34 new tests → 790 total.

### Changed
* `VideoEngineV3B.BackendVersion` → `"3B.3.0-alpha"`.
* `StopRecordingAsync` stops acquisition before SinkWriter finalize.
* Removed unused `_prevSampleTime100ns` field (CS0414 resolved).
* Registry `BackendVersion` → `"1.2.13-alpha"`. Version → `1.2.13-alpha` / build `228`.

---

## [v1.2.12-alpha] - 2026-06-26 (build 227)

> **Experimental alpha.** First real V3B MP4 output. H.264 encoding via WinRT MediaStreamSource + MediaTranscoder. CPU-copy BGRA32 frame path. 1-camera only. Disabled by default. V3B still experimental — not validated for scientific timing-critical recordings. V2 Stable unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3BMfSinkWriterSession`** — real WinRT-based SinkWriter session:
  * `MediaStreamSource` with uncompressed BGRA32 stream descriptor
  * Bounded channel (capacity 16, backpressure) bridges push (frame-arrived) to pull (SampleRequested)
  * `MediaTranscoder` with `HardwareAccelerationEnabled = true` → H.264 MP4
  * Per-sample timestamps via `MediaStreamSample.Timestamp` (100ns units → `TimeSpan.Ticks`)
  * Basic container validation post-finalize (file existence + size check)
  * `WireInto(V3BSinkWriterRecorder)` wires delegates and sets `DelegatesWiredExplicitly = true`
* **`VideoEngineV3B.UseRealMediaFoundation`** property (default `true`):
  * When `true`: `StartRecordingAsync` creates `V3BMfSinkWriterSession` and wires it, unless delegates were explicitly injected (test mode)
  * When `false`: scaffold delegates remain (backward-compatible test behavior)
* **`V3BSinkWriterRecorder.MarkDelegatesWired()`** — marks delegates as explicitly set; prevents real MF wiring in `StartRecordingAsync`
* **`V3BRecordingStatus.SinkWriterUnavailable`** — real MF init attempted but failed (distinct from `SinkWriterScaffold` = delegates not wired)
* **13 new `BackendMetadata` V3B fields:**
  * `V3BSinkWriterAvailable`, `V3BSinkWriterInitialized`, `V3BSinkWriterFailureReason`
  * `V3BInputPixelFormat` ("BGRA32"), `V3BWriterPixelFormat` ("H264_MP4"), `V3BFrameConversionMode` ("CPU_COPY_BGRA32_TO_H264"), `V3BFrameConversionWarning`
  * `V3BContainerReadable`, `V3BContainerDurationSeconds`, `V3BContainerFrameCount`, `V3BContainerFps`, `V3BOutputFileSizeBytes`, `V3BCodecName`
* **13 new `V3BRecordingDiagnostics` fields** (mirrors BackendMetadata additions)
* **25 new tests** → 756 total

### Changed

* `VideoEngineV3B.BackendVersion` → `"3B.2.0-alpha"` (first real output path)
* `VideoEngineV3B.StartRecordingAsync`: wires real `V3BMfSinkWriterSession` by default; `SinkWriterUnavailable` status when MF fails; explicit-delegate check prevents clobbering test mocks
* `VideoEngineV3B.StopRecordingAsync`: populates container validation diagnostics from `V3BMfSinkWriterSession` after finalize
* `VideoEngineV3B.DisposeAsync`: disposes `_mfSession`
* `V3BSinkWriterRecorder` comment and state updated
* `VideoEngineRegistry.BuildMetadata`: populates 13 new V3B fields
* `docs/V3B_SINKWRITER_SCIENTIFIC_BACKEND.md`: updated with real implementation details
* Version → `1.2.12-alpha` / build `227`

### Unchanged

* VideoEngineV2 stable recording behavior
* V2 timestamp CSV schema
* V2 Video Verification verdict semantics
* Session output folder layout
* VideoEngineV3 LowLag
* `TimestampCsvStatus` for V3 (`NotAvailableForV3Alpha`)
* STABLE_CORE_V1 components

---

## [v1.2.11-alpha] - 2026-06-26 (build 226)

> **Experimental alpha.** VideoEngineV3B SinkWriter Scientific backend scaffold.
> 1-camera only. SinkWriter delegate-injected (scaffold — no real MP4/CSV in this alpha).
> V3B disabled by default. Full model, policy, telemetry, CSV, verifier, and diagnostics layers implemented.
> V2 Stable remains recommended for timing-critical scientific recordings. V2 behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **New namespace `MultiCamApp.Capture.VideoEngineV3B`** — 7 new files:
  * **`V3BRecordingModels.cs`** — `V3BRecordingStatus` (11 values), `V3BReadinessStatus` (9), `V3BFrameStatus` (7), `V3BFallbackReason` (10)
  * **`V3BRecordingPolicy.cs`** — immutable record; `EnabledByDefault = false`, `MaxActiveCameras = 1`, `NoDuplicates/NoPlaceholders/NoArtificialCfrPadding = true`; `CheckCameraCount()` pure check (before opening any resources); `Default` and `Developer` singletons
  * **`V3BFrameTelemetry.cs`** — `V3BFrameTelemetryRow`, `V3BFrameTelemetryAggregator` (frame/submit/write/drop/dup counts, interval stats, `RowCountMatchesWrittenFrames`, `IsDuplicateSampleTime()`), `V3BSampleTimeConverter` (ticks→100ns, fps→default duration)
  * **`V3BTimestampCsvWriter.cs`** — 13-column schema; `OpenAsync`/`WriteRowAsync`/`DisposeAsync`; row count tracked; `TimestampCsvStatus` = `WrittenForV3BAlpha` / `FailedForV3BAlpha` / `NotAvailable`; `TimestampSource = MediaFrameReaderAppTimestamp`
  * **`V3BVerificationModels.cs`** — `V3BVerificationVerdict` (4), `V3BVerificationIssueKind` (9), `V3BVerificationIssue` (IsFatal), `V3BVerificationResult` (FromIssues), `V3BVerifier` pure-logic static verifier (MP4/CSV/row-count/dup/placeholder checks; scaffold → PassWithExperimentalWarning; fatal issues → Fail)
  * **`V3BRecordingDiagnostics.cs`** — mutable diagnostics snapshot; `ApplyTelemetry()` populates from aggregator
  * **`V3BSinkWriterRecorder.cs`** — delegate-injected scaffold; `InitDelegate/SubmitSampleDelegate/FinalizeDelegate` default to `false`; explains real MediaFoundation COM path in comments
  * **`VideoEngineV3B.cs`** — orchestrator; one-camera check before opening resources; CSV open → SinkWriter init → frame submission loop → stop → telemetry; scaffold returns false + cleans up partial CSV; `SubmitFrameAsync` enforces no-duplicate policy; `StopRecordingAsync` idempotent; `DisposeAsync` safe

* **`BackendIds.VideoEngineV3B_SinkWriterScientific`** — new backend identifier constant
* **30 new `BackendMetadata` V3B fields** — all diagnostic/policy/timing/CSV fields listed in docs
* **`VideoEngineRegistry` V3B additions:**
  * `IsV3BSelected` property
  * `V3BStatusSupplement` property (UI status lines when V3B active)
  * `SelectBackend` handles `VideoEngineV3B_SinkWriterScientific` — creates `VideoEngineV3B` instance; no WinRT dependency at selection time
  * `TryStartV3BRecordingAsync` — routing method; returns false if V3B not active or policy refuses
  * `StopV3BRecordingAsync` — idempotent stop; returns `RecordingFinalizeResult`
  * `GetV3BEngine` — exposes engine for delegate injection before recording starts
  * `BuildMetadata` populates all 30 V3B fields from `_v3b.Diagnostics`
  * `FormatId` updated for V3B display name
  * `Dispose` releases V3B engine
* **`docs/V3B_SINKWRITER_SCIENTIFIC_BACKEND.md`** — architecture, why V3 LowLag cannot provide CSV, why V3B uses MFR+SinkWriter, 1-camera alpha limitation, timestamp CSV policy, scaffold wiring, V2 recommendation, metadata fields, verification, future plan
* **~95 new tests** in `VideoEngineV3BTests.cs` covering all classes, policies, lifecycle paths, CSV schema, verifier, metadata fields, supplement, routing, V2 stability

### Changed

* `BackendIdentifiers.cs` — `BackendIds` + `BackendMetadata` (30 new V3B fields)
* `VideoEngineRegistry.cs` — version → `"1.2.11-alpha"`; V3B routing + supplement + BuildMetadata
* Version files → `1.2.11-alpha` / build `226`
* `V3ExperimentalReadinessInputs` changed from `sealed class` to `sealed record` (enables `with` in tests — no behavior change)

### Tests

* **Prior 636 tests unchanged and passing**
* **95 new tests → 731 total**
* Version assertions updated in all prior test files → `"1.2.11-alpha"`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* VideoEngineV3 LowLag — kept, unchanged
* `TimestampCsvStatus` for V3 — remains `"NotAvailableForV3Alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.10-alpha] - 2026-06-26 (build 225)

> **Experimental alpha.** V3 experimental readiness layer. V3 is clearly labeled experimental.
> `TimestampCsvStatus` and `TimestampSource` remain `NotAvailableForV3Alpha`. Maximum achievable
> readiness level is `NotForScientificTiming` while timestamp CSV is not implemented.
> V2 Stable is recommended for timing-critical scientific recordings. V2 stable behavior unchanged.
> STABLE_CORE_V1 untouched.

### Added

* **`V3ExperimentalReadinessModels.cs`** — pure-logic readiness types:
  * `V3ExperimentalReadinessLevel` (6 values: NotAvailable / Unsafe / ExperimentalOnly / TestableWithWarning / ReadyForNonCriticalTesting / NotForScientificTiming)
  * `V3ExperimentalReadinessStatus` (14 values: Unknown / NotEvaluated / V3NotSelected / EnumerationFailed / NoDevicesFound / PreviewUnavailable / RecordingUnavailable / MappingLowConfidence / TimestampCsvUnavailable / SidecarConflict / SidecarNotProven / SidecarProbeSucceeded / FallbackActive / Evaluated)
  * `V3ExperimentalReadinessReason` (15 values: None / V3BackendNotSelected / NoDevicesEnumerated / EnumerationFailed / PreviewPipelineNotAvailable / RecordingPipelineNotAvailable / MappingConfidenceLow / MappingConfidenceUnknown / TimestampCsvNotImplemented / SidecarExclusiveControlConflict / SidecarNotProvenSafe / SidecarDisabledForAlphaSafety / SidecarProbeSuccess / FallbackToV2Active / AllBasicCapabilitiesAvailable)
  * `V3ExperimentalReadinessInputs` — plain-data inputs (IsV3Selected, BackendVersion, DetectedCameraCount, MappingConfidence, PreviewAvailable, RecordingCapable, ContainerValidationAvailable, TimestampCsvStatus, TimestampSource, TimestampSidecarSupportLevel, FallbackActive, EnumerationFailed)
  * `V3ExperimentalReadinessReport` — immutable report (ReadinessLevel, ReadinessStatus, PrimaryReason, AllReasons, + inputs echoed, RecommendedUse, NotRecommendedUse, ReadinessMessage, IsNotForScientificTiming)

* **`V3ExperimentalReadinessEvaluator.cs`** — pure-logic stateless evaluator:
  * `Evaluate(inputs)` — covers: V3 not selected → NotAvailable; no devices → Unsafe; no preview/recording → ExperimentalOnly; low mapping confidence → TestableWithWarning; all basic capabilities + CSV unavailable → NotForScientificTiming; all capabilities + CSV available → ReadyForNonCriticalTesting
  * Always sets `RecommendedUse` ("VideoEngineV3 is experimental and may be used for backend testing and non-critical recordings.")
  * Always sets `NotRecommendedUse` ("Use VideoEngineV2 Stable for timing-critical scientific recordings until V3 timestamp CSV support is implemented.")
  * Populates `AllReasons` with all applicable reasons including sidecar and CSV reason
  * Message includes Unknown mapping confidence note when relevant

* **6 new `BackendMetadata` fields** (v1.2.10-alpha):
  * `V3ExperimentalReadinessLevel` — computed by `V3ExperimentalReadinessEvaluator`
  * `V3ExperimentalReadinessStatus`
  * `V3ExperimentalReadinessReason`
  * `V3RecommendedUse`
  * `V3NotRecommendedUse`
  * `V3ReadinessMessage`

* **Session summary readiness section** in `V3SessionSummaryAppender.AppendAsync`:
  * Optional `V3ExperimentalReadinessReport? readiness` parameter (backward-compatible)
  * When provided: writes `V3 Experimental Readiness:` section with level/status/reason/message/recommended-use/not-recommended-use

* **`V3SlotSummaryEntry`** — 6 new readiness fields (ReadinessLevel, ReadinessStatus, ReadinessReason, RecommendedUse, NotRecommendedUse, ReadinessMessage); `FromValidation` extended with optional params (backward-compatible)

* **V3StatusSupplement timing advisory** — always shown when V3 is active:
  * `Timing: No V3 timestamp CSV in alpha`
  * `Rec: Use V2 Stable for scientific timing`

* **`docs/V3_EXPERIMENTAL_NOTES.md`** — developer/user documentation covering V3 capabilities, why timing is unavailable, recommended use, feature flags, readiness diagnostics, version history

* **~60 new tests** in `VideoEngineV3ReadinessTests.cs`:
  * Enum value completeness (ReadinessLevel/Status/Reason)
  * ReadinessReport defaults, IsNotForScientificTiming
  * Evaluator: V3 not selected, no devices, enumeration failed, no preview, no recording, low mapping confidence, failed mapping, CSV unavailable → NotForScientificTiming, CSV available → ReadyForNonCritical, recommended/not-recommended text, sidecar reasons, AllReasons always has TimestampCsvNotImplemented, report carries inputs, Unknown mapping note
  * BackendMetadata: default fields, V2 active → V3NotSelected/NotAvailable, V3 active → level not NotAvailable/recommendations set/readiness message set, NotRecommendedUse mentions V2, version = 1.2.10-alpha
  * V3StatusSupplement: V3 active → timing + V2 warning lines, V2 active → empty
  * Session summary: AppendAsync with readiness → writes section, without readiness → no section, always has timing note
  * V3SlotSummaryEntry: without readiness params → defaults, with params → fields set, readiness does not affect TimestampCsvStatus
  * V2 stable: default backend, fallback not used, evaluator V2 backend → NotAvailable

### Changed

* **`VideoEngineRegistry`** — version → `"1.2.10-alpha"`; `BuildMetadata` computes and populates 6 readiness fields; `V3StatusSupplement` adds timing advisory lines when V3 active

* **`BackendIdentifiers.cs`** — 6 new readiness fields on `BackendMetadata`

* **`V3SessionSummaryAppender`** — `AppendAsync` accepts optional `V3ExperimentalReadinessReport?`; readiness section written when provided; 6 new fields on `V3SlotSummaryEntry`; `FromValidation` extended

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.10-alpha / build 225

### Tests

* **Prior 586 tests unchanged and passing**
* **~60 new tests → ~646 total**
* Version assertions updated in `VideoEngineBackendTests`, `VideoEngineV3PreviewTests`, `VideoEngineV3TimestampSidecarTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* `V3RecordingVerifier` — unchanged
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"`
* `TimestampSource` — remains `"NotAvailableForV3Alpha"`
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.9-alpha] - 2026-06-26 (build 224)

> **Experimental alpha.** V3 timestamp sidecar feasibility probe. Probe is disabled by default
> (`EnableV3TimestampSidecarExperimental = false`). Probe enabled only under explicit developer
> opt-in. Does not write timestamp CSV. `TimestampCsvStatus` and `TimestampSource` remain
> `NotAvailableForV3Alpha`. Probe failure cannot corrupt recording. V3 is not scientifically
> timing-equivalent to V2. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3TimestampSidecarProbeModels.cs`** — pure-logic probe types:
  * `V3TimestampSidecarProbeStatus` (9 values: Disabled / NotAttempted / Attempted / Started / SamplesReceived / ConflictDetected / Failed / Cancelled / Timeout)
  * `V3TimestampSidecarProbePhase` (7 values: NotStarted / PolicyCheck / DeviceOpen / FrameReaderStart / SampleCollection / Cleanup / Completed)
  * `V3TimestampSidecarProbeFailureReason` (10 values: None / DisabledByPolicy / ConflictWithRecording / DeviceOpenFailed / FrameReaderStartFailed / NoSamplesReceived / ProbeTimeout / ExceptionDuringProbe / Cancelled / Unknown)
  * `V3TimestampSidecarProbeResult` — immutable result with static factories: `Disabled()`, `Conflict(reason)`, `Cancelled(durationMs)`. Fields: Enabled, Attempted, Started, SamplesReceived, SampleCount, FirstSampleUtc, LastSampleUtc, LongestGapMs, ProbeDurationMs, ConflictDetected, ConflictReason, ProbeStatus, ProbePhase, FailureReason, SupportLevel, SanitizedError, Note.

* **`V3TimestampSidecarProbe.cs`** — developer-only feasibility probe class:
  * `ProbeDurationMs` — bounded probe window (default 1500 ms)
  * `FrameSampleDelegate` — delegate injection (`Func<string, CancellationToken, Task<IReadOnlyList<long>>>`) for full unit-testability without WinRT
  * `RunAsync(deviceId, policy, ct)` — never throws; handles all cases:
    * `policy.Enabled = false` → `Disabled` (DisabledByPolicy; DisabledForAlphaSafety)
    * cancellation before probe → `Cancelled`
    * `AllowConcurrentWithRecording = false` → `ConflictDetected` (ConflictWithRecording; ConflictWithExclusiveRecording)
    * `OperationCanceledException` in delegate → `Cancelled` (caller CT) or `Timeout` (probe CT)
    * exception in delegate → `Failed` (ExceptionDuringProbe; FailedSafely); error sanitized
    * no samples returned → `Started` (NoSamplesReceived; Unknown)
    * samples returned → `SamplesReceived` (None; SupportedWithWarning); longest gap and UTC bounds computed

* **`VideoEngineRegistry.SetLastProbeResult(result)`** — stores probe result; null-safe (falls back to `Disabled()`)
* **`VideoEngineRegistry.GetLastProbeResult()`** — retrieves last stored probe result

* **9 new `BackendMetadata` fields** (v1.2.9-alpha):
  * `V3TimestampSidecarProbeStatus` — "Disabled" when V3 + default; "NotAvailable" when V2
  * `V3TimestampSidecarProbeAttempted` (bool)
  * `V3TimestampSidecarProbeSamplesReceived` (bool)
  * `V3TimestampSidecarProbeSampleCount` (int)
  * `V3TimestampSidecarProbeDurationMs` (long)
  * `V3TimestampSidecarProbeConflictDetected` (bool)
  * `V3TimestampSidecarProbeFailureReason` — "None" by default
  * `V3TimestampSidecarProbeSupportLevel` — "DisabledForAlphaSafety" by default
  * `V3TimestampSidecarProbeNote` — advisory message

* **`V3SlotSummaryEntry`** — 9 new probe fields; `FromValidation` extended with optional probe params (backward-compatible)

* **Session summary probe lines** in `V3SessionSummaryAppender.AppendAsync`:
  * `V3 Sidecar Probe: {status} (Attempted: {bool})`
  * `V3 Sidecar Probe Support Level: {level}`
  * `V3 Sidecar Probe Samples: {count} in {ms} ms` (when samples received)
  * `V3 Sidecar Probe: ExclusiveControl conflict detected. Recording unaffected.` (when conflict)
  * `V3 Sidecar Probe Note: {note}` (when attempted + note set)

* **~60 new tests** in `VideoEngineV3TimestampSidecarProbeTests.cs`:
  * Enum value completeness (ProbeStatus/ProbePhase/ProbeFailureReason)
  * ProbeResult static factories (Disabled, Conflict, Cancelled) and default constructor
  * `V3TimestampSidecarProbe.RunAsync`: disabled, not-attempted, conflict, cancelled-before-start, timeout, exception (sanitized error), no-samples, samples-received (count/duration/UTC bounds/longest gap/one-sample edge case)
  * TimestampCsvStatus remains NotAvailableForV3Alpha after samples received
  * Probe failure/conflict does not mark recording slot failed
  * Support level mapping for all outcomes
  * BackendMetadata: default probe fields, V3 active + default → Disabled, V2 active → NotAvailable, conflict result, samples-received result, SetNull fallback, GetLastProbeResult default + after set
  * V3SlotSummaryEntry: probe defaults, conflict params, samples params, probe fields do not affect TimestampCsvStatus
  * V3Verifier: missing CSV still ExperimentalWarning after probe with samples, after probe conflict, missing MP4 still Fail

### Changed

* **`VideoEngineRegistry`** — version bumped to `"1.2.9-alpha"`; `BuildMetadata` populates 9 new probe fields from `_lastProbeResult`; added `SetLastProbeResult`/`GetLastProbeResult`

* **`V3SessionSummaryAppender`** — 9 new probe fields on `V3SlotSummaryEntry`; probe lines in `AppendAsync`

* **`BackendIdentifiers.cs`** — 9 new probe fields on `BackendMetadata`

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.9-alpha / build 224

### Tests

* **Prior 541 tests unchanged and passing**
* **~60 new tests → ~601 total**
* Version assertions updated in `VideoEngineBackendTests`, `VideoEngineV3PreviewTests`, `VideoEngineV3TimestampSidecarTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* `V3RecordingVerifier` — missing CSV remains ExperimentalWarning; MP4 missing remains Fail
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"` regardless of probe result
* `TimestampSource` — remains `"NotAvailableForV3Alpha"` regardless of probe result
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.8-alpha] - 2026-06-26 (build 223)

> **Experimental alpha.** V3 timestamp sidecar feasibility layer. Sidecar is disabled by default
> (`EnableV3TimestampSidecarExperimental = false`). `TimestampCsvStatus` and `TimestampSource`
> remain `NotAvailableForV3Alpha`. Sidecar failure cannot corrupt recording. V3 is not
> scientifically timing-equivalent to V2. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3TimestampSidecarModels.cs`** — pure-logic timestamp sidecar types:
  * `V3TimestampSidecarStatus` (9 values: Disabled / NotAttempted / Probing / Running / Completed / ConflictDetected / Unsupported / Failed / Cancelled)
  * `V3TimestampSidecarSupportLevel` (7 values: Unknown / DisabledForAlphaSafety / Supported / SupportedWithWarning / ConflictWithExclusiveRecording / UnsupportedByDriver / FailedSafely)
  * `V3TimestampSidecarFailureReason` (9 values: None / DisabledByPolicy / ProbeConflict / DeviceOpenFailed / FrameReaderStartFailed / FrameReaderFailed / ExclusiveControlConflict / Cancelled / Unknown)
  * `V3TimestampSidecarPolicy` — immutable policy (Enabled=false default; AllowConcurrentWithRecording=false; FailRecordingIfSidecarFails always false; `Disabled` and `DevTest` static factories)
  * `V3TimestampSidecarDiagnostics` — mutable per-slot diagnostics with `RecordSample()` (tracks samples + longest gap), `RecordError()` (sanitised), `Clone()`
  * `V3TimestampSidecarFrameSample` — per-frame timestamp sample (SlotIndex, TimestampMs, FrameIndex, Note)

* **`V3TimestampSidecarSettings.cs`** — developer-only feature flags:
  * `EnableV3TimestampSidecarExperimental` = **false** (default; must remain false in release builds)
  * `GetEffectivePolicy()` — returns `Disabled` policy when flag is false; `DevTest` when true

* **`V3TimestampSidecar.cs`** — sidecar skeleton class:
  * `StartAsync(deviceId, policy, ct)` — disabled policy → Disabled status; cancelled → Cancelled; ExclusiveControl conflict → ConflictDetected; probe scaffold → NotAttempted; never throws
  * `StopAsync(ct)` — no-op when not Running; transitions Running → Completed
  * `RecordFrameSample(timestampMs)` — no-op when not Running; no CSV written in this alpha patch
  * `GetDiagnostics()` — returns diagnostics clone
  * `DisposeAsync()` — safe/idempotent; no WinRT resources in this scaffold
  * Recording is never failed due to sidecar outcome (`FailRecordingIfSidecarFails` always false)

* **7 new `BackendMetadata` fields** (v1.2.8-alpha):
  * `V3TimestampSidecarEnabled` (bool)
  * `V3TimestampSidecarStatus` — "Disabled" when V3 + sidecar off; "NotAvailable" when V2
  * `V3TimestampSidecarSupportLevel` — "DisabledForAlphaSafety" when disabled; "NotAvailable" when V2
  * `V3TimestampSidecarFailureReason` — "DisabledByPolicy" when disabled; "NotAvailable" when V2
  * `V3TimestampSidecarSamplesCaptured` (int)
  * `V3TimestampSidecarLongestGapMs` (long)
  * `V3TimestampSidecarNote` — advisory message; set for V3, empty for V2

* **`V3SlotSummaryEntry`** — 6 new timestamp sidecar fields; `FromValidation` extended with optional sidecar params (backward-compatible)

* **Session summary sidecar lines** in `V3SessionSummaryAppender.AppendAsync`:
  * `V3 Timestamp Sidecar: Disabled [Disabled]` (or status when active)
  * `V3 Timestamp Sidecar Support Level: DisabledForAlphaSafety`
  * `V3 Timestamp Sidecar Samples: {n}` (when > 0)
  * `V3 Timestamp Sidecar Note: {note}` (when set)

* **~55 new tests** in `VideoEngineV3TimestampSidecarTests.cs`:
  * Enum value completeness (Status/SupportLevel/FailureReason)
  * Policy defaults and static factories
  * Settings feature flag default false, GetEffectivePolicy
  * Diagnostics: defaults, RecordSample count/gap tracking, RecordError sanitization, Clone independence
  * FrameSample field storage
  * Skeleton: StartAsync disabled/conflict/cancelled/not-attempted, StopAsync transitions, RecordFrameSample guards, GetDiagnostics clone, DisposeAsync idempotent, StartAsync after dispose
  * Failure isolation: sidecar conflict/failure does not set recording state to failed
  * TimestampCsvStatus remains NotAvailableForV3Alpha when sidecar disabled (V3 active)
  * BackendMetadata: default safe values, V2 active shows NotAvailable, V3 note is set, version = "1.2.8-alpha"
  * V3SlotSummaryEntry: without sidecar params → defaults, with conflict fields, samples populated
  * V3Verifier: missing CSV still ExperimentalWarning (unchanged), WarnOnMissingTimestampCsv=false suppresses, missing MP4 still Fail

### Changed

* **`VideoEngineRegistry`** — version bumped to `"1.2.8-alpha"`; `BuildMetadata` populates 7 new sidecar fields; reads `V3TimestampSidecarSettings.EnableV3TimestampSidecarExperimental`

* **`V3SessionSummaryAppender`** — 6 new sidecar fields on `V3SlotSummaryEntry`; sidecar lines in `AppendAsync`

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.8-alpha / build 223

### Tests

* **Prior 494 tests unchanged and passing**
* **~55 new tests → ~549 total**
* Version assertion updated in `VideoEngineBackendTests` and `VideoEngineV3PreviewTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* `V3RecordingVerifier` — missing CSV remains ExperimentalWarning; MP4 missing remains Fail
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"` for V3 when sidecar disabled
* `TimestampSource` — remains `"NotAvailableForV3Alpha"` for V3 when sidecar disabled
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched

---

## [v1.2.7-alpha] - 2026-06-26 (build 222)

> **Experimental alpha.** V3 recording lifecycle hardening. Idempotent start/stop,
> safe multi-slot cleanup, per-slot output integrity tracking, stress cycler with
> delegate injection, 4 new BackendMetadata output integrity fields, output integrity
> lines in session_summary.txt. V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3RecordingStressModels.cs`** — pure-logic stress model types:
  * `V3RecordingStressFailureKind` (10 values: None / RecordingStartFailed / RecordingStopFailed / OutputFileMissing / OutputFileEmpty / FfprobeReadFailed / ZeroDuration / PreviewSuspendFailed / PreviewResumeFailed / Unknown)
  * `V3RecordingStressCycleResult` — per-cycle result tracking all start/stop/output/preview suspension fields; `IsSuccess` = start + stop + non-empty MP4
  * `V3RecordingStressSummary` — aggregated summary (TotalCycles, SuccessfulCycles, FailedCycles, MissingMp4Count, EmptyMp4Count, FfprobeFailureCount, ZeroDurationCount, PreviewSuspendCount, PreviewResumeSuccessCount); `Aggregate()` static factory; `SuccessRate`; `AllCyclesPassed`

* **`V3RecordingStressCycler.cs`** — developer-only stress cycler with delegate injection:
  * All I/O via injected delegates: `StartRecordingDelegate`, `StopRecordingDelegate`, `AuditDelegate`, `SuspendPreviewDelegate`, `ResumePreviewDelegate`
  * Testable without real cameras; pattern mirrors `V3PreviewStressCycler` (v1.2.3-alpha)
  * `RunCycleAsync` — single-slot recording cycle: suspend preview → start → stop → resume preview → audit
  * `RunMultipleCyclesAsync` — repeated cycles with cancellation support; returns `V3RecordingStressSummary`
  * Preview resume always attempted after stop when suspended, regardless of stop outcome
  * NOT staged to public installer

* **Per-slot output integrity fields** on `V3RecordingSlotState`:
  * `LastOutputValidationStatus` (string) — "NotRun" / "FileSizeOk" / "FileEmpty" / "FileMissing" / "StopFailed" / "Unknown"
  * `LastOutputFileSizeBytes` (long)
  * `LastOutputDurationSeconds` (double)
  * Populated in `StopSlotRecordingAsync` after each successful stop

* **4 new `BackendMetadata` fields** (v1.2.7-alpha):
  * `V3LastOutputValidationStatus` — mirrors slot state; "NotRun" when V2 active or no stop yet
  * `V3LastOutputFileSizeBytes`
  * `V3LastOutputDurationSeconds`
  * `V3StressValidationNote` — optional developer note from stress run

* **`V3SlotSummaryEntry`** — 4 new output integrity fields: `OutputValidationStatus`, `OutputFileSizeBytes`, `OutputDurationSeconds`, `StressValidationNote`; `FromValidation` signature extended (backward-compatible optional params)

* **Session summary output integrity lines** in `V3SessionSummaryAppender.AppendAsync`:
  * `V3 Output Validation Status: {status}` when not "NotRun"
  * `V3 Output File Size: {KB}` when > 0
  * `V3 Output Duration (clock): {s}` when > 0
  * `V3 Stress Validation: {note}` when set

* **46 new tests** in `VideoEngineV3RecordingHardeningTests.cs`:
  * `V3RecordingStressFailureKindTests` — all 10 values verified
  * `V3RecordingStressCycleResultTests` — IsSuccess logic (start/stop/mp4 required)
  * `V3RecordingStressSummaryTests` — Aggregate empty/all-success/mixed, preview tracking, zero-duration counting
  * `V3RecordingStressCyclerTests` — start fail, stop fail, success recording-only, preview suspension/resume, resume failure isolates from recording, file missing, file empty, ffprobe unavailable, start throws, multi-cycle aggregation, cancellation
  * `V3RecordingSlotStateOutputIntegrityTests` — default values, field mutation
  * `VideoEngineV3HardeningTests` — StopAll no-op, double stop, dispose state, empty filesets
  * `BackendMetadataOutputIntegrityTests` — defaults "NotRun", V2 active defaults
  * `V3SlotSummaryEntryOutputIntegrityTests` — FromValidation without/with integrity params
  * `V3RecordingPerSlotFailureIsolationTests` — slot state isolation, diagnostics clone independence, combined state isolation

### Changed

* **`VideoEngineV3.StartSlotRecordingAsync`** — idempotent: returns immediately (no-op) if slot is already in `Recording` state; prevents duplicate recorder creation on the same slot

* **`VideoEngineV3.StopSlotRecordingAsync`** — populates `LastOutputValidationStatus`, `LastOutputFileSizeBytes`, `LastOutputDurationSeconds` on slot state after each stop; file-size check is best-effort (errors → "Unknown")

* **`VideoEngineV3.StopAllSlotsRecordingAsync`** — each per-slot task now runs under `CancellationToken.None`; inner exceptions are caught and returned as failure results, not propagated to `Task.WhenAll`; app-close cleanup is safe even when `ct` is already cancelled

* **`VideoEngineRegistry`** — version bumped to `"1.2.7-alpha"`; `BuildMetadata` populates 4 new output integrity fields from first stopped V3 slot

* **Version files** — `version.json`, `MultiCamApp.csproj`, `MultiCamApp.iss` → 1.2.7-alpha / build 222

### Tests

* **Prior 449 tests unchanged and passing**
* **46 new tests → 495 total**
* Version assertion updated in `VideoEngineBackendTests` and `VideoEngineV3PreviewTests`

### Unchanged

* VideoEngineV2 stable recording behavior — no changes
* V2 timestamp CSV schema — unchanged
* V2 Video Verification verdict semantics — unchanged
* Session output folder layout — unchanged
* VideoEngineV3 `BackendVersion` — unchanged at `"3.4.0-alpha"`
* STABLE_CORE_V1 components — untouched
* `TimestampCsvStatus` — remains `"NotAvailableForV3Alpha"` for V3

---

## [v1.2.6-alpha] - 2026-06-26 (build 221)

> **Experimental alpha.** V3 combined preview + recording lifecycle coordination.
> V3 preview is suspended before recording starts (ExclusiveControl safety policy).
> Preview resume attempted after recording stops. Resume failure does not affect recording result.
> Recording priority is always higher than preview priority.
> V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3CombinedLifecycleModels.cs`** — pure-logic combined lifecycle types:
  * `V3CombinedModeStatus` — NotActive / PreviewOnly / RecordingOnly / PreviewAndRecording / PreviewSuspendedForRecording / Failed / Fallback.
  * `V3PreviewRecordingConflictReason` — ExclusiveControlRequired / SafetyDefault / PreviewNotActive / Unknown.
  * `V3CombinedSlotState` — per-slot tracking: PreviewWasRunningBeforeRecording, PreviewSuspendedForRecording, PreviewResumeAttempted/Succeeded, ConflictReason, LastCombinedError.
  * `V3CombinedSession` — session-level snapshot: SuspendedSlots, ResumedSlots, FailedSlots.
  * `V3CombinedDiagnostics` — counters: suspension count, resume attempt/success/failure counts.
* **`VideoEngineV3`** — new combined lifecycle methods:
  * `SuspendPreviewForRecordingAsync` — stops preview if running before recording (ExclusiveControl policy); marks PreviewSuspendedForRecording.
  * `OnRecordingStartedAfterSuspension` — updates combined status after recording starts.
  * `ResumePreviewAfterRecordingAsync` — tries to restart preview after recording; failure isolated from recording result.
  * `GetCombinedSlotState`, `GetCombinedSession`, `GetCombinedDiagnostics`, `ResetCombinedSlotState`.
* **`VideoEngineRegistry`** — new combined lifecycle routing:
  * `TryStartV3SlotRecordingWithPreviewCoordinationAsync` — replaces `TryStartV3SlotRecordingAsync` at call sites; detects and suspends V3 preview before recording.
  * `StopV3SlotRecordingWithPreviewRecoveryAsync` — replaces `StopV3SlotRecordingAsync`; attempts preview resume after recording stops.
  * `SetUiDispatcher` — stores dispatcher for preview resume.
  * `GetV3CombinedSlotState`, `GetV3CombinedSession`, `GetV3CombinedDiagnostics`.
* **7 new `BackendMetadata` fields** (v1.2.6-alpha): V3CombinedStatus, V3PreviewWasRunningBeforeRecording, V3PreviewSuspendedForRecording, V3PreviewResumeAttemptedAfterRecording, V3PreviewResumeSucceededAfterRecording, V3PreviewRecordingConflictReason, V3CombinedLifecycleWarning.
* **`V3SlotSummaryEntry`** — 7 new combined lifecycle fields; `FromValidation` accepts optional `V3CombinedSlotState`.
* **`V3SessionSummaryAppender`** — outputs combined lifecycle section per slot when preview was suspended.

### Changed

* `MainWindow` — `StartOneSlotRecordingAsync` calls `TryStartV3SlotRecordingWithPreviewCoordinationAsync`; `StopV2AllSlotsRecordingAsync` calls `StopV3SlotRecordingWithPreviewRecoveryAsync`.
* `MainWindow` — TXT metadata includes V3 combined lifecycle section; JSON metadata includes `v3CombinedLifecycle` block.
* `VideoEngineRegistry.V3StatusSupplement` — shows `[preview suspended]` tag and preview resume failure note.
* `VideoEngineRegistry.BuildMetadata` — version bumped to `"1.2.6-alpha"`.
* `VideoEngineV3.GetSlotPipelineState` — returns `Recording` when slot is in PreviewSuspendedForRecording state.
* Tests — `VideoEngineBackendTests` and `VideoEngineV3PreviewTests` version assertions updated to `"1.2.6-alpha"`.

### Tests

* Added `VideoEngineV3CombinedLifecycleTests.cs` — 40 new pure-logic tests.

### Unchanged

* VideoEngineV2 stable recording behavior: unchanged.
* V2 timestamp CSV schema: unchanged.
* V2 Video Verification verdict semantics: unchanged.
* Session output folder layout: unchanged.
* STABLE_CORE_V1 (VideoVerificationService, MetadataParser, VideoScanner, RecordingTimingMetrics, SessionComparisonService, MetadataWriter, RecordingSession, RecordingDiagnosticsMonitor, ScientificTimingAssessor, AppConfig, SessionSummaryWriter, MetadataCompletenessPolicy): untouched.
* TimestampCsvStatus = NotAvailableForV3Alpha: unchanged.

## [v1.2.5-alpha] - 2026-06-26 (build 220)

> **Experimental alpha.** V3 recording audibility pass.
> Adds post-recording container validation, V3RecordingVerifier, V3 session summary appendix.
> Missing timestamp CSV = ExperimentalWarning (not FAIL) for V3 alpha recordings.
> V2 stable behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **`V3ContainerValidation.cs`** — pure-logic result model: `V3ContainerValidationStatus` (NotRun/Valid/ValidWithWarning/Invalid), `V3ContainerValidation` record (FileExists, FileSizeBytes, FfprobeReadable, DurationSeconds, FrameCount, FpsTag, CodecName, Width, Height, PixelFormat, ValidationStatus). Factory helpers: FileMissing, FileEmpty, FfprobeUnavailable, FfprobeFailed, NotRun.
* **`V3PostRecordingAudit.cs`** — stateless async runner: wraps `V2PostRecordingAudit.RunFfprobeAsync`, produces `V3ContainerValidation`. Never throws; all failures captured in result.
* **`V3RecordingVerifier.cs`** — independent V3 verifier (NOT modifying STABLE_CORE_V1):
  * `V3VerificationVerdict` — Pass / PassWithExperimentalWarning / Fail.
  * `V3VerificationIssue` — Severity: Info / ExperimentalWarning / Fail.
  * Missing timestamp CSV → `ExperimentalWarning` (not `Fail`); corrupt/missing MP4 → `Fail`.
  * Includes explicit wording: "Per-frame timestamp CSV is not available in V3 alpha backend."
  * `V3VerificationOptions.Default` — RequireMp4Exists=true, WarnOnMissingTimestampCsv=true.
* **`V3SessionSummaryAppender.cs`** — appends V3 experimental section to `session_summary.txt` after V2 writer finishes. Includes: per-slot recording status, timestamp unavailable policy note, MP4 readability, codec, duration, verification verdict. Does NOT modify `SessionSummaryWriter` (STABLE_CORE_V1).
* **6 new `BackendMetadata` fields** (v1.2.5-alpha): V3ContainerReadable, V3ContainerDurationSeconds, V3ContainerFrameCount, V3ContainerFps, V3CodecName, V3PostRecordingValidationStatus.

### Changed

* `VideoEngineRegistry.BuildMetadata` — new optional `V3ContainerValidation? v3Container` parameter; populates 6 new container fields; `BackendVersion = "1.2.5-alpha"`.
* `MainWindow.StopV2AllSlotsRecordingAsync` — Phase 3b runs `V3PostRecordingAudit.RunAsync` for V3 slots; Phase 3c runs `V3RecordingVerifier`; Phase 7 appends V3 session summary section; V3 slots skip duplicate/VQ detection.
* `MainWindow.WriteV2SlotMetadataAsync` — accepts `V3ContainerValidation?`; passes to `BuildMetadata` and `BuildBackendMetadataJson`; writes V3 container validation lines to TXT metadata.
* `MainWindow.BuildBackendMetadataJson` — accepts `V3ContainerValidation?`; adds `v3ContainerValidation` block to JSON output.
* Metadata TXT — V3 container validation section (readable, duration, codec, FPS, frame count).
* Metadata JSON — `backendInfo.v3ContainerValidation` object.

### Verification policy clarification

* V3 alpha missing timestamp CSV: `ExperimentalWarning` — not `Fail`. Explicitly documented in issues list with recommendation to use V2 stable for timing-critical recordings.
* V2 missing timestamp CSV: unchanged — still `Fail` per existing `V2RecordingVerifier` rules.
* Corrupt/missing V3 MP4: `Fail` — this is always a recording failure.

---

## [v1.2.4-alpha] - 2026-06-25 (build 219)

> **Experimental alpha.** First VideoEngineV3 recording pipeline.
> V3 recording uses MediaCapture + LowLagMediaRecording for H.264 MP4 output.
> Per-frame timestamp CSV not available from this path.
> V3 recording failure falls back to V2 or reports ExperimentalWarning.
> V2 stable recording behavior unchanged. STABLE_CORE_V1 untouched.

### Added

* **V3RecordingModels.cs** — pure-logic types (no WinRT):
  * `V3RecordingState` enum — 7 states (NotStarted/Preparing/Starting/Recording/Stopping/Stopped/Failed).
  * `V3RecordingStatus` enum — session-level (NotStarted/Recording/PartiallyRecording/Stopped/Failed).
  * `V3RecordingFailureReason` enum — 12 values classifying recording failures by cause.
  * `V3RecordingStateValidator` — 12 valid transitions, `Describe()`, `SanitiseError()`.
  * `V3RecordingSlotState` — mutable per-slot state (DeviceKey, OutputMp4Path, FramesCaptured, etc.).
  * `V3RecordingSession` — session snapshot across all slots.
  * `V3RecordingDiagnostics` — session counters (StartAttempts, SuccessfulStarts, FailureCount, etc.) + `Clone()`.
* **V3SlotRecorder.cs** — per-slot V3 recording pipeline (WinRT-dependent):
  * `InitialiseAsync(deviceId, tempOutputPath)` — opens MediaCapture (ExclusiveControl, Video-only), creates StorageFile.
  * `StartAsync()` — builds `MediaEncodingProfile.CreateMp4(Auto)` (no audio), calls `PrepareLowLagRecordToStorageFileAsync`, starts recording.
  * `StopAsync(finalOutputPath)` — calls `FinishAsync()`, renames temp → final, returns `RecordingFinalizeResult`. Never throws.
  * `RecordingFailed` event — fired on `MediaCapture.Failed` during active recording.
  * `DisposeAsync()` — safe cleanup; deletes corrupt temp file if present.
* **11 new BackendMetadata fields** (v1.2.4-alpha): V3RecordingStatus, V3RecordingFailureReason, V3RecordingFallbackRecommended, V3RecordingFallbackReason, V3RecordingStartAttempts, V3RecordingSuccessfulStarts, V3RecordingStopAttempts, V3RecordingSuccessfulStops, V3RecordingFailureCount, TimestampSource, TimestampCsvStatus.

### Changed

* `VideoEngineV3` — recording stubs replaced with real pipeline:
  * `IsRecordingCapable = true` (was false).
  * `BackendVersion = "3.4.0-alpha"`.
  * `StartSlotRecordingAsync` — initialises `V3SlotRecorder`, starts recording, fires `SlotRecordingFailed` on failure.
  * `StopSlotRecordingAsync` — finishes recording, returns `RecordingFinalizeResult`.
  * `StopAllSlotsRecordingAsync` — stops all active slots in parallel; per-slot failures isolated.
  * `GetSlotPipelineState` — now returns `Recording` when V3 recording active, `Previewing` when V3 preview active.
  * `GetRecordingSession()`, `GetRecordingDiagnostics()` — new public accessors.
  * `SlotRecordingFailed` event added.
* `VideoEngineRegistry` — V3 recording routing:
  * `IsV3RecordingPreferred` — true when V3 active + confidence High/Medium.
  * `IsV3SlotPreviewActive(slot)` — exposes per-slot V3 preview active flag.
  * `IsV3SlotRecordingActive(slot)` — per-slot V3 recording active flag.
  * `TryStartV3SlotRecordingAsync(slot, deviceId, fileSet)` — sets device ID, starts V3 recording, returns bool.
  * `StopV3SlotRecordingAsync(slot, fileSet)` — stops V3 recording, returns `RecordingFinalizeResult`.
  * `GetV3RecordingSession()`, `GetV3RecordingDiagnostics()` — session and diagnostic snapshots.
  * `SlotRecordingFailed` event relay.
  * `BuildMetadata` → `BackendVersion = "1.2.4-alpha"` + 11 new recording/timestamp fields.
  * `V3StatusSupplement` — now shows V3 recording status line.
* `MainWindow` — V3 recording integration:
  * `_v3SlotRecordingActive[]` field tracks which slots are using V3 recording.
  * Active slot detection now includes `IsV3SlotPreviewActive(i)` so V3-preview-only slots are included.
  * `StartOneSlotRecordingAsync` — tries V3 first via `TryStartV3SlotRecordingAsync`; falls back to V2 if failed.
  * `StopV2AllSlotsRecordingAsync` — routes V3 slots to `StopV3SlotRecordingAsync`; V2 slots unchanged.
  * `V3SlotRecordingFailed` subscription + `OnV3SlotRecordingFailed` handler → `UpdateBackendStatusLabel`.
  * `BuildBackendMetadataJson` — includes `v3Recording` block and `timestampSource`/`timestampCsvStatus` fields.
  * TXT metadata — V3 recording fields and timestamp source written to `[Recording Backend — camN]` section.

### Timestamp / verification policy

* V3 alpha recording: `TimestampSource = NotAvailableForV3Alpha`, `TimestampCsvStatus = NotAvailableForV3Alpha`.
* No per-frame CSV rows are written for V3 recording. No fake rows.
* Verification for V3 recordings: ffprobe still runs for MP4 readability; timestamp CSV check is skipped with explicit warning in metadata.
* V2 recording: `TimestampSource = V2_FrameTimestampMonitor`; existing timestamp behavior unchanged.

---

## [v1.2.3-alpha] - 2026-06-25 (build 218)

> **Experimental alpha.** VideoEngineV3 preview lifecycle hardening.
> Idempotent start/stop, startup timeout watchdog, per-slot failure isolation, session diagnostics.
> V3 recording still NOT implemented. STABLE_CORE_V1 untouched. VideoEngineV2 recording unchanged.

### Added

* **V3PreviewDiagnostics** — new file `V3PreviewDiagnostics.cs` (pure-logic, no WinRT):
  * `V3PreviewFailureReason` enum — 10 values classifying preview failures by cause.
  * `V3PreviewStateValidator` — pure-logic HashSet of 10 valid (From, To) state transitions; `IsValid()`, `Describe()`, `SanitiseError()` (strips 20+ char hex IDs for privacy).
  * `V3PreviewSessionDiagnostics` — session-level counters: StartAttempts, SuccessfulStarts, StopAttempts, SuccessfulStops, FailureCount, FallbackCount, StartupTimeoutCount, LastError, LastFallbackReason, LongestFrameGapMs, ConsecutiveFrameErrorCount. `Clone()` for snapshot.
  * `V3PreviewStressCycler` — developer utility, delegate-injected, no WinRT. Runs N start→stop cycles, returns `StressCycleResult` (TotalCycles, SuccessStarts, FailedStarts, CleanStops, ErrorStops, FullySuccessful).
* **Startup timeout watchdog** — `V3SlotPreview` launches `RunStartupWatchdogAsync` (3000ms) after FrameReader.StartAsync. Cancelled on first frame arrival. Timeout → `RecordStartupTimeout()` + `MarkFailed(StartupTimeout)` + `PreviewFailed` event.
* **`PreviewFailed` event on V3SlotPreview** — raised after MarkFailed. Relay: `VideoEngineV3.SlotPreviewFailed` → `VideoEngineRegistry.V3SlotPreviewFailed` → `MainWindow.OnV3SlotPreviewFailed` → `UpdateBackendStatusLabel()`.
* **11 new BackendMetadata fields** (v1.2.3-alpha lifecycle diagnostics): V3PreviewStartAttempts, V3PreviewSuccessfulStarts, V3PreviewStopAttempts, V3PreviewSuccessfulStops, V3PreviewFailureCount, V3PreviewFallbackCount, V3PreviewStartupTimeoutCount, V3PreviewLastError, V3PreviewLastFallbackReason, V3PreviewLongestFrameGapMs, V3PreviewConsecutiveFrameErrors. All written to JSON and TXT metadata.

### Changed

* `V3SlotPreview.StartAsync` — idempotent: Running/Starting → log + no-op; Failed → log "call StopAsync first" + no-op. Resets frame counters on each new start cycle. Increments `V3PreviewSessionDiagnostics.StartAttempts` before init; `SuccessfulStarts` after FrameReader starts.
* `V3SlotPreview.StopAsync` — idempotent: Stopped/Stopping → log + no-op. Cancels watchdog CTS. Increments StopAttempts/SuccessfulStops.
* `V3SlotPreview.MarkFailed` — now takes `V3PreviewFailureReason`; sanitises error string via `SanitiseError()`.
* `V3SlotPreview` constructor — now requires `V3PreviewSessionDiagnostics sessionDiag` parameter.
* `V3PreviewSlotState` — new fields: `FailureReason`, `StartAttempts`, `StopAttempts`.
* `V3PreviewFrameStats` — new fields: `LongestFrameGapMs`, `ConsecutiveFrameErrorCount`, `LastFrameAgeMs`.
* `VideoEngineV3` — `_sessionDiag` field, `SlotPreviewFailed` event, `GetPreviewDiagnostics()`. `PrepareSlotPreviewAsync` resets slot state on each call.
* `VideoEngineRegistry` — `V3SlotPreviewFailed` event relay; `GetV3PreviewDiagnostics()`; `BuildMetadata` → `BackendVersion = "1.2.3-alpha"` + 11 new diagnostic fields; improved `V3StatusSupplement` (Running/Partial/Failed/Fallback format with slot count and FPS).
* `MainWindow` — subscribes/unsubscribes `V3SlotPreviewFailed` alongside `V3SlotFrameRendered`; `OnV3SlotPreviewFailed` handler calls `UpdateBackendStatusLabel()`.

---

## [v1.2.2-alpha] - 2026-06-25 (build 217)

> **Experimental alpha.** First functional VideoEngineV3 preview pipeline.
> V3 preview replaces V2 preview when mapping confidence is High or Medium.
> V3 recording still NOT implemented. STABLE_CORE_V1 untouched. VideoEngineV2 recording unchanged.

### Added

* **V3 preview pipeline** — `V3SlotPreview` per-slot MediaCapture + MediaFrameReader preview. Uses `StreamingCaptureMode.Video`, `SharedReadOnly`, CPU memory for SoftwareBitmap delivery. Same WinRT API family as V2's `MediaFoundationCaptureService`. No audio, no recording, no MP4.
* **Preview throttle** — `V3PreviewThrottle` pure-logic class: Eco ~4 FPS (250 ms gate), Balanced ~9 FPS (110 ms gate, default), Smooth ~15 FPS (66 ms gate). Tracks frames received/displayed/skipped. FPS measured against `Stopwatch` session elapsed time.
* **Preview models** — `V3PreviewState` (NotStarted/Starting/Running/Stopping/Stopped/Failed), `V3PreviewStatus`, `V3PreviewThrottleMode`, `V3PreviewFrameStats`, `V3PreviewSlotState`, `V3PreviewSession`.
* **Registry preview routing** — `VideoEngineRegistry.TryStartV3SlotPreviewAsync` starts V3 preview when `IsV3PreviewPreferred`; returns false and logs fallback on failure. `StopV3AllSlotsPreviewAsync`, `GetV3SlotPreviewBitmap`, `V3SlotFrameRendered` event added.
* **V3 preview fallback** — Low mapping confidence → falls back to V2 preview. V3 `StartAsync` failure → caught, state → Failed, falls back to V2. Partial slot failure does not affect other slots.
* **UI integration** — `BackendStatusText` now shows active V3 slot count + average FPS. `OpenV2SlotAsync` tries V3 first when `IsV3PreviewPreferred`; falls back to V2 on failure.
* **V3 preview metadata** — 9 new fields in `BackendMetadata` (V3PreviewAvailable, V3PreviewStatus, V3PreviewFallbackUsed, V3PreviewFallbackReason, V3PreviewTargetFps, V3PreviewMeasuredFps, V3PreviewFramesReceived, V3PreviewFramesDisplayed, V3PreviewFramesSkippedByThrottle). Written to both JSON `v3Preview` block and TXT `[Recording Backend — camN]` section.

### Changed

* `VideoEngineV3` — preview stubs replaced with real `PrepareSlotPreviewAsync`/`StartSlotPreviewAsync`/`StopSlotPreviewAsync`/`StopAllSlotsPreviewAsync`. `GetSlotPreviewBitmap` returns live `WriteableBitmap`. `GetSlotOverlayData` shows V3 state.
* `BackendMetadata` — 9 new V3 preview diagnostic fields (all default-safe).
* `VideoEngineRegistry.BuildMetadata` → `BackendVersion` = `"1.2.2-alpha"`.
* `VideoEngineV3.BackendVersion` → `"3.1.0-alpha"` (unchanged).

---

## [v1.2.1-alpha] - 2026-06-25 (build 216)

> **Experimental alpha.** First real V3 capability layer: native camera enumeration and capability probing.
> Recording remains on VideoEngineV2_Stable. STABLE_CORE_V1 untouched.

### Added

* **V3 native camera enumeration** — `V3CameraEnumerator` enumerates cameras via `MediaFrameSourceGroup.FindAllAsync()` (with `DeviceInformation` fallback); populates privacy-safe `V3CameraDeviceInfo` records (display name, device index, SHA-256 hash of symbolic link, device source kind USB/BuiltIn/Unknown, availability).
* **V3 capability probing** — `V3CameraEnumerator.ProbeCapabilitiesAsync()` collects supported resolutions/FPS from `MediaFrameSourceGroup` format descriptions without opening any camera.
* **Duplicate-name resolver** — `V3DuplicateNameResolver` groups cameras by display name and classifies `V3DeviceMappingConfidence`: High (all unique), Medium (duplicates but unique keys), Low (duplicates + no key disambiguation). Low → auto fallback to V2.
* **V3 enumeration models** — `V3CameraDeviceInfo`, `V3CameraCapability`, `V3CapabilityProbeResult`, `V3EnumerationResult` (with `Empty()` and `Failed()` factories).
* **V3 enumeration in metadata** — `backendInfo.v3Enumeration` JSON object and V3 TXT fields in `[Recording Backend — camN]` section: `V3CameraEnumerationAvailable`, `V3DetectedCameraCount`, `V3CapabilityProbeStatus`, `V3DeviceMappingConfidence`, `V3FallbackRecommended`, `V3FallbackReason`, `duplicateNameGroups`, `sameNameDeviceCount`.
* **UI supplement** — `BackendStatusText` now shows V3 camera count + mapping confidence when V3 is active.
* **`VideoEngineRegistry.RunV3EnumerationAsync()`** — called after backend selection in `InitV2EngineAsync`; no-op when V2 is active.
* **44 new tests** — `VideoEngineV3EnumerationTests.cs`: model round-trips, factory methods, duplicate-name resolver logic (all confidence levels, case-insensitive, multi-group), hash/source-kind pure helpers, BackendMetadata V3 fields, registry V3 status supplement.

### Changed

* `BackendMetadata` — 7 new V3 diagnostic fields (all default-safe).
* `VideoEngineV3.BackendVersion` → `"3.1.0-alpha"`.
* `VideoEngineRegistry.BuildMetadata` → populates V3 fields from live enumeration result; `BackendVersion` field = `"1.2.1-alpha"`.
* Schema version remains `"1.2.0"` (JSON structure unchanged; new fields are additions only).

### Unchanged

* All STABLE_CORE_V1 systems — not modified.
* V2 recording pipeline — identical behavior to v1.2.0-alpha.
* Timestamp CSV schema — 10-column format unchanged.
* All existing JSON/TXT metadata fields.

## [v1.2.0-alpha] - 2026-06-25 (build 215)

> **Experimental alpha.** Architecture patch only — no user-visible recording behavior changes.
> Default backend remains VideoEngineV2_Stable. STABLE_CORE_V1 systems are untouched.

### Added

* **Backend abstraction layer** — new `IVideoEngineBackend` interface in `MultiCamApp.Capture.Backend`; defines the contract for all future video engine backends.
* **VideoEngineRegistry** — selects active backend at startup, manages V2/V3 switching, handles fallback, and exposes `BackendStatusDisplay` for the UI label.
* **VideoEngineV3_Experimental scaffold** — compiles and integrates; all recording/preview methods are safe stubs returning failure results; `Activate()` runs a capability probe and sets state to `ScaffoldReady`.
* **Backend metadata in output files** — `backendInfo` JSON object and `[Recording Backend — camN]` TXT section added to per-camera metadata output; 14 fields: `RecordingBackend`, `BackendVersion`, `BackendMode`, `BackendFallbackUsed`, `BackendFallbackReason`, `CaptureApi`, `PreviewApi`, `EncoderApi`, `HardwareEncoderUsed`, `HardwareEncoderEvidence`, `PreviewIndependentFromRecording`, `PreviewTargetFps`, `PreviewMeasuredFps`, `RecordingMeasuredRealFps`.
* **Backend init diagnostics** — `BackendInitDiagnostics` container with `Success()`, `Failure()`, `Scaffold()` factories; surfaces MediaFoundation/D3D11/encoder/preview/watchdog/storage sub-statuses.
* **UI backend status label** — `BackendStatusText` TextBlock shows "Backend: VideoEngineV2 Stable" (or V3 Experimental with fallback note when applicable).
* **`VideoEngineSettings.RequestedBackendId`** — static opt-in property for backend selection; defaults to `"VideoEngineV2_Stable"`.
* **Schema version bump** — metadata JSON `schemaVersion` updated to `"1.2.0"`.
* **Backend tests** — `MultiCamApp.Tests/VideoEngineBackendTests.cs` with 30 unit tests covering registry selection, V3 scaffold identity/lifecycle, metadata fields, diagnostics factories, and V2 settings backward-compatibility.

### Unchanged

* All STABLE_CORE_V1 systems (VideoVerificationService, MetadataParser, VideoScanner, RecordingTimingMetrics, SessionComparisonService, MetadataWriter, RecordingSession, RecordingDiagnosticsMonitor, ScientificTimingAssessor, AppConfig) — not modified.
* Existing recording behavior — MainWindow still calls `_v2Engine` directly; V2 pipeline behavior is identical to v1.1.21.
* Timestamp CSV schema — 10-column format unchanged.
* All existing JSON/TXT metadata fields — no fields removed or renamed.

## [v1.1.21] - 2026-06-25 (build 214)

### Added

* **Timestamp source metadata** — new `[Timestamp Source — camN]` TXT section and `timestampSource` JSON object explicitly document that CSV timestamps are app-side monotonic (`Stopwatch.GetTimestamp / Stopwatch.Frequency`), relative to recording start, captured at CSV write time on the background frame thread, and are not hardware camera presentation timestamps.
* **Camera control readback limitations** — new `cameraControlReadbackLimitations` JSON object and TXT note in `[Camera Controls]` section clarify that `Unknown/Unavailable` readback values mean the Windows camera API did not expose the value, not that the setting failed or that recording is affected.
* **Timing model clarification** — `[Timing Models]` TXT labels updated to make purpose of each timing source explicit: nominal (frame-count), player/container interpretation (ffprobe), primary audit (app timestamps), recording lifecycle (stopwatch). `timingModels` JSON `basis` strings updated to match. Added `hardwarePresentationTimestampUsed: false` flag.

### Changed

* No recording behavior changes. No frame writing changes. No exposure/shutter control changes.
* Metadata schema version → `1.1.21`.

## [v1.1.20] - 2026-06-24 (build 213)

### Added

* **Real frame timing policy** — `FrameTimestampMonitor` now writes recording-relative timestamps (`appTimestampMsFromRecStart`, `appTimestampSecondsFromRecStart`) and high-resolution monotonic ticks (`monotonicTicks = Stopwatch.GetTimestamp()`) to the CSV. Timestamps are now relative to recording start, not preview start.
* **Timing Models section** — new `[Timing Models — camN]` TXT section and `timingModels` JSON object separates four timing interpretations: frame-count timing, container timing (ffprobe), app timestamp timing (CSV), and internal clock timing (stopwatch). Shows diffs between models.
* **Timing Classification** — new `ClassifyTimingBehavior` method classifies each camera's timing into: `CFR_LIKE`, `STARTUP_SETTLING_ONLY`, `VFR_DRIVER_BEHAVIOR`, `MID_SESSION_GAPS_DETECTED`, `FPS_MISMATCH_DETECTED`, `POSSIBLE_EXPOSURE_LIMITED_FPS`. Multiple labels can apply. Reported in `[Timing Classification]` TXT section and `timingClassification` JSON object.
* **Frame integrity check** — new `frameIntegrity` JSON object reports: framesWritten, timestampCsvRows, exact match flag, diff, duplicateFramesDetected, integrityVerdict (PASS/WARN_CSV_MISMATCH/CSV_UNAVAILABLE). TXT timestamps-rows line now shows exact match with count diff.
* **Improved VFR classification** — added interval StdDev (`IntervalStdMs`), coefficient of variation (`CvPercent`), and CSV-based FPS mismatch detection on top of the existing r_frame_rate heuristic.
* **Timing reference camera** — `ComputeTimingReferenceCamera` now scores cameras by mid-session gap count, interval jitter, and measured FPS (not just CFR flag). Selection reason includes jitter σ value.
* **Exposure / FPS safety warning** — if measured FPS < 90% of requested FPS, a warning is added to `[Timing Models]` TXT and `timingModels.fpsSafetyWarning` JSON describing possible causes (exposure, LLC, driver, USB bandwidth).

### Changed

* `FrameTimestampMonitor` CSV columns 2–4 updated: `captureTimestampMonotonicMs` → `appTimestampMsFromRecStart` (now recording-relative, not preview-relative); `writerInputTimestampMs` → `appTimestampSecondsFromRecStart`; `writtenTimestampMonotonicMs` → `monotonicTicks`. Column 7 (gap flag) unchanged.
* `TimestampCsvStats` — added `IntervalStdMs` and `CvPercent` fields.
* `WriteV2SlotMetadataAsync` — added `allTsStats` parameter for session-wide timing reference scoring.
* Metadata schema version → `1.1.20`.

## [v1.1.19] - 2026-06-24 (build 212)

### Added

* **Timing reference camera selection** — `ComputeTimingReferenceCamera` selects the CFR camera closest to 30 fps as the timing reference. New `[Timing Reference]` TXT section and `timingReference` JSON field: referenceCamera, reason, authoritativeSource, per-camera CFR status.
* **VFR driver behavior reporting** — detects driver-level VFR (`r_frame_rate=60/1` but avg≈29.68 fps) and reports it as `DriverVfr` behavior, not a recording failure. New `[VFR Driver Behavior]` TXT section and `vfrDriverBehavior` JSON field with note that timestamp CSV is the authoritative timing source.
* **Startup settling analysis** — `ReadTimestampCsvStatsAsync` now classifies gaps into startup (first 10 s) and mid-session. New `[Startup Settling]` TXT section and `startupSettling` JSON field: startAfterWarmupSeconds, startupGapCount, midSessionGapCount, stableAfterWarmup, analysisSafeInterval.
* **Bitrate quality profiles** — `V2BitrateProfile` enum (Standard=7500, High=12000, WindowsCameraLike=18000) added to `VideoEngineSettings`. Default changed from 8000 → 7500 kbps (Standard). New `[Bitrate Profile]` TXT section and `bitrateProfile` JSON field.
* **Metadata schema version** bumped to `1.1.19`.
* **Windows Camera patterns** — `MediaCapture.RecordLimitationExceeded` handler (WinRT 3-hour recording limit auto-stop) and `DisplayRequest.RequestActive()` / `RequestRelease()` (prevents system sleep during recording) added to the V2 engine.

### Changed

* `WriteV2SlotMetadataAsync` — added `allFfprobe` parameter for session-wide timing reference computation.
* `TimestampCsvStats` — added `StartupGapCount` and `MidSessionGapCount` fields.
* `VideoEngineSettings.TargetBitrateKbps` default changed from 8000 to 7500 (Standard profile).

## [v1.1.17] - 2026-06-24 (build 210)

### Added

* **Visual quality analysis** (`V2VisualQualityAnalyzer`) — post-recording analysis using OpenCvSharp4. Samples 40 frames per camera. Computes: Laplacian variance (blur score), overexposed pixel % (luma ≥ 245), underexposed pixel % (luma ≤ 15), brightness mean/std across frames (instability), 3×3 regional brightness imbalance (uneven lighting/shadow). Verdict: PASS / PASS_WITH_WARNING / FAIL_VISUAL_QUALITY. Results in new `[Visual Quality — camN]` TXT section and `visualQuality` JSON field.
* **Camera capability probe** (`V2CameraCapabilitySnapshot`, `CameraControlManagerV2.ProbeCapabilities()`) — reads driver-reported ranges from open VideoDeviceController: ExposureControl.Min/Max/Step/Value (seconds), FocusControl.Min/Max/Step/Value (driver steps), BacklightCompensation support. Accessible via `VideoEngineV2.GetSlotCapabilities(slot)`.
* **Capability probe on camera target change** — when the user selects a camera in the Focus/Exposure panel, the driver range is probed and shown in the status labels: "Exposure range: Xms – Yms | Current: Zms" and "Focus: Supported | Range: N–M steps | Current: K". If unsupported, "Not supported by this device/driver."
* **Per-Apply audit log** — `ApplyFocusSettingButton_Click` and `ApplyExposureSettingButton_Click` now log resolved slot, device name, requested mode (auto/manual), requested value, and full apply result (applied, readback status, readback value, warning) to `AppDiagnosticLogger.Runtime`.
* **Start/stop state labels** — `ShowPreviewStatus` calls during recording lifecycle: "Preparing cameras..." → "Starting encoders..." → "Recording..." (on start); "Stopping cameras..." → "Finalizing MP4 files..." → "Writing metadata..." → "Complete." (on stop).
* **Metadata schema version** bumped to `1.1.17` in both per-camera and session JSON.

### Changed

* `StopV2AllSlotsRecordingAsync` — duplicate-frame detection and visual quality analysis now run sequentially per camera (dup then VQ, to avoid file contention), with cameras running in parallel via `Task.WhenAll`. Previously dup ran in a separate parallel pass.
* `WriteV2SlotMetadataAsync` — added `Diagnostics.VisualQualityResult? vq` parameter.

## [v1.1.16] - 2026-06-24 (build 209)

### Changed

* Maintenance version bump only. No code changes from v1.1.15.

## [v1.1.15] - 2026-06-24

### Fixed

* **Timestamp CSV path bug** — `CameraPipelineV2.StartRecordingAsync` was computing the CSV path from `tempOutputPath` ("cam1.tmp.mp4") by stripping extension, producing "cam1.tmp_timestamps.csv" instead of "cam1_timestamps.csv". Fixed by adding a `timestampCsvPath` parameter and passing `fileSet.TimestampCsvPath` from `VideoEngineV2.StartSlotRecordingAsync`. `VideoEngineSettings.WriteTimestampCsv = true` was already set; the path bug was the only cause of missing CSVs.
* **One-camera frame count range** — was still written as per-camera min/max only. Now uses global session-level min/max across all active cameras.
* **Timing confidence** — was PASS even when CSV and ffprobe both unavailable (stopwatch alone). Now PASS_WITH_WARNING when neither CSV data nor ffprobe container duration is available to corroborate the monotonic stopwatch.
* **Inter-camera comparison** — was "Not yet implemented". Now computes global frame range, duration range, and FPS spread across all active cameras with PASS/PASS_WITH_WARNING/FAIL result.
* **Duplicate-frame detection** — was "Not available in this build". Now implemented using OpenCvSharp4: samples 30 frames per camera, computes mean grayscale absolute difference, classifies near-identical frame evidence.

### Added

* **ffprobe post-finalization audit** — runs `runtime/ffmpeg/ffprobe.exe` against each finalized MP4 after recording stops. Extracts codec, pixel format, resolution, avg_frame_rate, r_frame_rate, container duration, nb_frames, bitrate, constant-frame-rate flag. Results written to both TXT and JSON. New `[ffprobe Audit — camN]` section in TXT metadata; `ffprobeAudit` object in JSON. Per-slot ffprobe tasks run in parallel.
* **Duplicate-frame detection** (`V2PostRecordingAudit.RunDuplicateDetectionAsync`) — 30-frame sample, grayscale mean absolute diff per frame pair. Distinguishes static-scene near-identical from software duplication. Evidence level: None / Suspected. Results in `[Frame Quality — camN]` section.
* **`session_metadata.txt` and `session_metadata.json`** — written to session root folder after all cameras stop. Contains: session summary, per-camera table (frames, duration, FPS, CSV rows, ffprobe duration, dup evidence), global inter-camera comparison stats, global session verification result and notes.
* **`SessionVerificationStats` record** and `ComputeSessionVerificationStats` method — computes global frame min/max/spread, duration min/max/spread, inter-camera timing confidence, global PASS/PASS_WITH_WARNING/FAIL verdict.
* **`WriteV2SessionMetadataAsync`** — writes `session_metadata.txt` and `session_metadata.json`.
* **`V2PostRecordingAudit` class** (`diagnostics/V2PostRecordingAudit.cs`) — static helpers for `RunFfprobeAsync` and `RunDuplicateDetectionAsync`. `FfprobeResult` and `DuplicateDetectionResult` data classes.
* **Writer queue diagnostics** — reports "Not tracked (LowLagMediaRecording handles encoding internally)" for queue drops/depth/latency; reports preview dropped frames from `RecordingHealthMonitor`.
* **Refactored `StopV2AllSlotsRecordingAsync`** — 7-phase pipeline: stop slots → read CSV stats → ffprobe (parallel) → dup detection (parallel) → compute session stats → write per-camera metadata → write session metadata.
* **Japanese support** for all new fields: ffprobe audit section, frame quality updates, inter-camera comparison, session verification, session metadata files.
* **`container duration (ffprobe)`** now shown in `[Timing]` section and JSON `timing.containerDurationS`.
* **JSON schema v1.1.15** — new `ffprobeAudit` top-level object, updated `frameQuality` (implemented detection, near-identical stats, writer queue note), updated `verification` (globalSessionResult, interCameraTimingConfidence, frameCountSpread, globalVerificationNotes), updated `timing.timingConfidence`.

### Changed

* `WriteV2SlotMetadataAsync` now accepts pre-computed `TimestampCsvStats?`, `FfprobeResult?`, `DuplicateDetectionResult?`, and `SessionVerificationStats` instead of computing them internally.
* Version bumped to v1.1.15, build 208.

## [v1.1.14] - 2026-06-24

### Fixed

* **App version in metadata** — was printing `MultiCamApp.Core.VersionInfo` (the C# class name) instead of the version string. Fixed by using `_vm.Version.Version`, `.Build`, `.Stage` individually.
* **Duration always 0.00s** — `RecordingFinalizeResult` was constructed without the `Duration` field in `VideoEngineV2.StopSlotRecordingAsync`. Added `RecordingElapsed` public property to `CameraPipelineV2` (reads `_recordingClock.Elapsed` which is valid after `Stopwatch.Stop()`). Duration is now populated before the result is returned.
* **Timestamp CSV status mismatch** — metadata reported "Not written" when the CSV file existed. Fixed by checking `File.Exists` directly.
* **Duplicate-frame detection claim** — removed "Not implemented (planned)" which allowed clean PASS. Now "Not available in this build" with `PASS_WITH_WARNING`.
* **Motion blur risk config-based** — was reading `AutoExposureEnabledPerCamera` / `DisableLowLightCompensationPerCamera` config flags (UI intent, not driver state). Now reads actual `V2ControlApplyResult` for Exposure and LowLightCompensation controls. Risk is `Unknown` unless exposure is driver-confirmed fixed.
* **One-camera frame count range** — was always `0 - 0`. Now writes actual `framesWritten - framesWritten`.
* **UI diagnostics in Japanese metadata** — was silently dropped (was inside English-only else branch). Both paths now include `[UI 診断]` section.

### Added

* **Timestamp interval analysis** — CSV is read post-recording to compute: first/last frame timestamps, total duration, mean/median/min/max/P95/P99 frame intervals, gap count, estimated FPS. Written to both TXT and JSON.
* **Detailed camera control sections** — Focus, Exposure, LLC, Digital stabilization, Flicker reduction each get a subsection with: support status, request made, apply result, readback value, confirmed state, warning.
* **Finalization reporting** — `[Recording — camN]` adds: Recording finalized, Temporary file used, Temporary file renamed, File size.
* **Session Verification** — `PASS` / `PASS_WITH_WARNING` / `FAIL` with documented rules for each condition. Notes list specific evidence.
* **Duration fallback chain** — monotonic stopwatch → CSV first/last delta → health session elapsed → Unknown. Duration is no longer zero when frames were written unless all three sources fail.
* **`TimestampCsvStats` class** and `ReadTimestampCsvStatsAsync` in MainWindow.
* **`RecordingElapsed` property** on `CameraPipelineV2`.
* **metadata.json schema v1.1.14** — restructured top-level: `schemaVersion`, `appVersion` (object), `session`, `recordingEngine`, `videoSettings`, `cameras` (array), `controls` (per-type objects), `recording`, `timing` (full stats), `frameQuality`, `motionBlurRisk`, `verification`, `uiDiagnostics`.
* **Japanese metadata** uses same redesigned structure with Japanese labels and section names throughout.

### Changed

* Version bumped to v1.1.14, build 207.

## [v1.1.13] - 2026-06-24

### Fixed

* **3-camera UI freeze** — root cause identified and fixed: `Direct3DPreviewRenderer.PresentFrame` was calling `_dispatcher.BeginInvoke(DispatcherPriority.Render, ...)` on every camera frame. Since WPF `Render` priority (7) is higher than `Input` priority (5), 3 cameras × 30 fps = 90 Render-priority operations per second completely starved mouse/keyboard input processing.
* Changed preview dispatch priority from `Render` to `Background` (priority 4, lower than `Input` 5). Mouse clicks and button presses now always preempt preview frame rendering.
* Added frame-drop gate: at most one `BeginInvoke` is queued per camera renderer at any time. Incoming frames are dropped if the previous render has not been processed yet. This prevents unbounded dispatcher queue growth.
* Pre-allocated reusable pixel buffer per renderer (`byte[] _pixelBuffer`). Previously allocated 8 MB per frame per camera: 3 cameras × 30 fps × 8 MB = 720 MB/s of managed heap allocation causing GC pauses. Buffer is now reused across frames; allocation only occurs on first frame or resolution change.
* Adaptive preview FPS throttle applied at recording start: 20 fps (1 camera), 15 fps (2 cameras), 10 fps (3 cameras), 8 fps (4 cameras). Preview FPS is restored to unlimited on Stop Recording.
* Parallel recording startup: all camera slots now start `LowLagMediaRecording` concurrently via `Task.WhenAll` instead of sequentially. Eliminates 1–2 second stall per camera during startup (3 cameras previously took 3–6 s sequentially).
* Folder creation moved to `Task.Run` to avoid synchronous I/O on the UI thread at recording start.
* Stop Recording button now becomes enabled as soon as any slot enters Recording state, even while other slots are still initializing. Previous behavior: Stop was disabled during the entire `_recordUiBusy` window.
* Preview FPS limit restored to unlimited on `StopV2DefaultRecordingAsync`.

### Added

* `UiFreezeWatchdog`: background timer posts heartbeats to the WPF dispatcher at 100 ms intervals; measures response latency. Logs stalls > 250 ms (minor), 1000 ms (warning), 3000 ms (critical freeze). Freeze count and max duration tracked across the session. Critical freeze counted once per pending cycle (not once per 100 ms tick during a prolonged freeze).
* `CameraPipelineV2.SetPreviewFpsLimit(int maxFps)`: per-slot preview throttle method forwarded to `Direct3DPreviewRenderer.SetPreviewFpsLimit`.
* `VideoEngineV2.SetAllSlotsPreviewFpsLimit(int maxFps)`: sets throttle on all active pipeline slots.
* `VideoEngineV2.RecommendedPreviewFpsForRecordingCameras(int count)`: returns the recommended limit based on active camera count.
* Session metadata (`metadata.json` and `metadata.txt`) now includes `[UI Diagnostics]` / `uiDiagnostics` section: `uiFreezeDetected`, `uiFreezeCount`, `maxUiFreezeMs`, `uiFreezeDuringState`, `previewFpsThrottled`, `activeCameraCount`, `suspectedFreezeCause`.
* Watchdog state labels in preview start (`PreviewStarting` / `Previewing`), preview stop (`Idle`), recording start (`RecordingStarting` / `Recording`), and recording stop (`RecordingStopping` / `Previewing`).

### Changed

* `StartV2DefaultRecordingAsync`: folded sequential slot loop into parallel `Task.WhenAll`; folder I/O moved off UI thread; file-set array population logic corrected (previously had incorrect index mapping bug).
* Version bumped to v1.1.13, build 206.

## [v1.1.11] - 2026-06-23

### Added

* **Default Focus button**: Restores the selected camera's research-safe focus default (autofocus disabled, fixed/manual mode) via `VideoEngineV2 → CameraControlManagerV2`. Clears the per-camera manual focus override stored in `AppConfig.ManualFocusValuesPerCamera`. The per-camera `_focusDefaultActivePerCamera` flag prevents old manual values from being re-applied after a default restore.
* **Default Exposure button**: Restores the selected camera's research-safe exposure default (auto exposure disabled) via the same control chain. Clears the per-camera manual exposure override and sets `_exposureDefaultActivePerCamera` flag.
* Per-camera restore methods added to `CameraPipelineV2` (`RestoreFocusDefaultAsync`, `RestoreExposureDefaultAsync`) and exposed as public pass-throughs on `VideoEngineV2` (`RestoreSlotFocusDefaultAsync`, `RestoreSlotExposureDefaultAsync`).
* Focus and exposure restore status messages distinguish confirmed success, unsupported device, failed, and camera-not-yet-open cases.
* Japanese localization keys `defaultFocusButton` and `defaultExposureButton` added to `ja.json` and `en.json`.
* Recording metadata (`metadata.txt` and `metadata.json`) now includes FPS and timing detail section: requested FPS, selected format FPS, measured capture FPS, note that recording FPS is independent from preview throttle.
* Recording metadata now includes motion blur risk assessment: `"low"` when fixed exposure and low-light compensation are both off; `"medium"` when either is active, with a note explaining why.
* Exposure status messages after "Apply Exposure" are now honest: distinguish between request-accepted-with-readback-unavailable and confirmed states instead of falsely claiming "applied".

### Changed

* `ApplyFocusSettingButton_Click`: clears `_focusDefaultActivePerCamera` flag when the user explicitly applies manual focus settings.
* `ApplyExposureSettingButton_Click`: uses `BuildExposureStatusText` for honest result messaging; clears `_exposureDefaultActivePerCamera` flag.
* `UpdateCameraControlButtonLabels` now updates Default Focus and Default Exposure button labels in addition to Apply buttons; labels read from `en.json`/`ja.json` language keys.
* Version bumped to v1.1.11, build 204.

## [v1.1.10] - 2026-06-23

### Changed

* OpenCV DLL is no longer a hard startup requirement. VideoEngineV2 uses Windows MediaFoundation for all preview and recording; the OpenCV startup check now logs an advisory warning instead of blocking startup. This fixes startup failures on systems where `OpenCvSharpExtern.dll` is absent or VC++ redist is missing.
* Language selection is locked while recording is active. The language dropdown is disabled when recording starts and re-enabled when recording fully stops, preventing mid-session language changes.
* Recording metadata language now follows the UI language selected at recording start. If Japanese is selected before clicking Start Recording, `metadata.txt` is written in Japanese for that session. If English is selected, `metadata.txt` is written in English. The locked session language is recorded in `metadata.json` as `metadataLanguage`.
* Metadata text files are now written with explicit UTF-8 encoding to ensure correct Japanese character output.
* Japanese metadata sections now use Japanese section headers (`[セッション]`, `[録画エンジン]`, `[カメラ]`, `[カメラ制御]`, `[録画]`) and localized field labels and values.
* Fixed remaining English-only strings in `ja.json`: experiment mode timing labels, About page section titles, license/commercial use body text, attribution titles, third-party notices title and button, and the startup critical DLL error message.
* About page: "Released:" label, third-party notices body text, and FFmpeg license button are now localized via language keys instead of hardcoded English strings.
* `AboutPage.xaml.cs` now uses `lang["releasedLabel"]`, `lang["thirdPartyNoticesBody"]`, and `lang["viewFfmpegLicense"]` keys from both `en.json` and `ja.json`.
* `startupCriticalDllMissing` error message in both localization files no longer mentions OpenCV by name, as the V2 pipeline does not require it.
* Added `thirdPartyNoticesBody`, `viewFfmpegLicense`, and `releasedLabel` keys to both `en.json` and `ja.json`.
* Version bumped to v1.1.10, build 203.

## [v1.1.9] - 2026-06-23

### Fixed

* Recording elapsed and session timers now reset correctly at each recording session start. Added `_v2RecordingStopwatch` (Stopwatch) owned by MainWindow and restarted in `StartElapsedTimer`. Timer display returns to `00:00:00` when not recording.
* Camera readiness count in status panel now uses `CountV2ReadySlots()`, which queries V2 slot state directly, fixing the `0/N ready` display during active preview.
* Camera target dropdown in Advanced Camera Controls now shows only cameras assigned in the current active layout, filtering out unassigned or out-of-layout slots.
* `StartV2DefaultRecordingAsync` clears `_v2RecordingFileSets` and `_v2SessionFolderPath` before each recording, preventing stale data from a prior session from affecting the new one.

### Removed

* `HighStabilityRecordingModeCheckBox` — High-Stability Recording Mode is always enabled internally; the checkbox was redundant and removed from the UI.
* `ReapplyFocusBeforeRecordingCheckBox` — Re-apply focus before recording is always active internally; checkbox removed.
* `ReapplyExposureBeforeRecordingCheckBox` — Re-apply exposure before recording is always active internally; checkbox removed.
* Associated click handlers (`HighStabilityRecordingModeCheckBox_Click`, `ReapplyFocusBeforeRecordingCheckBox_Click`, `ReapplyExposureBeforeRecordingCheckBox_Click`) removed from code.

## [v1.1.8] - 2026-06-23

### Changed

* VideoEngineV2 is now the default and sole active recording engine for all normal recording sessions. Legacy pipeline is isolated from the recording flow.
* Removed `V2TestPanel` Expander from the main UI. Preview status is now shown via `PreviewStartupStatusText` using the `ShowPreviewStatus(message, isError)` helper.
* Removed legacy-only methods: `ApplyV2ButtonOverride`, `StartV2PreviewAsync`, `StopV2PreviewAsync`, `StartV2RecordingAsync`, `StopV2RecordingAsync`.
* `OnV2SlotFrameRendered` cleaned to remove `V2TestPanel.SetPreviewBitmap` call.
* `StartV2DefaultPipelineAsync` cleaned to remove `V2TestPanel.ClearError` and `IsActive` guard.
* All `V2TestPanel.ShowError` calls replaced with `ShowPreviewStatus` or `AppDiagnosticLogger.Runtime`.
* `WriteV2SlotMetadataAsync` updated with full `[Session]` block in both TXT and JSON output, including session name, folder, recording date/time, active cameras, active slots, and engine identifier.
* Fixed `CS1061` build error: `result.Duration` is `TimeSpan` (value type), not `TimeSpan?`. Removed `.HasValue`/`.Value` usage.
* `StopV2AllSlotsRecordingAsync` cleaned to remove stale backward-compatibility line.

## [v1.1.0] - 2026-06-23

### Added

* Original Capture Mode for preserving real camera frames only
* Per-frame Timestamp CSV files for timing-sensitive analysis
* Scientific timing confidence reporting
* Privacy-safe metadata summaries
* Video Verification Simple View and Detailed View
* Offline video/container metadata inspection using bundled `ffprobe.exe`
* Synchronized start gate for active 2–4 camera sessions
* Scientific exposure defaults
* Clearer camera-control metadata wording
* Offline Windows installer for Windows 10/11
* Non-commercial academic and research license
* Third-party notices for bundled runtime and build components
* Citation metadata through `CITATION.cff`

### Changed

* Updated recording logic to avoid duplicate-frame and placeholder-frame insertion
* Updated verification wording to distinguish Real Capture FPS from MP4/container Playback FPS
* Updated metadata and report wording for research readability
* Updated documentation for installation, verification, hardware diagnostics, security, licensing, and release packaging
* Updated GitHub repository structure for public source release preparation

### Notes

* Timestamp CSV is the recommended timing source for timing-sensitive analysis.
* MP4 playback FPS and container duration are playback/container metadata, not the primary scientific timing source.
* Frame counts may differ between cameras when devices deliver real frames at slightly different measured FPS.
* Recordings should be verified using the in-app Video Verification page after capture.
