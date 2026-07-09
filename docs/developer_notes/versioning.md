# Versioning

MultiCamApp uses [semantic versioning](https://semver.org/) with early development stages.

## Version flow

| Range | Stage | Meaning |
|-------|--------|---------|
| `0.0.x` | `experimental` | Internal testing, unstable |
| `0.1.x` | `alpha` | First minimally usable builds |
| `0.2.x`–`0.4.x` | `feature_milestone` | Larger feature milestones |
| `0.5.x`–`0.8.x` | `beta` | Beta development |
| `0.9.x` | `release_candidate` | Release candidate |
| `1.0.0+` | `stable` | First stable release |

`0.0.99` is the last patch before `0.1.0` (patch bump rolls over automatically).

## Source of truth

`source/MultiCamApp/MultiCamApp/config/version.json`

```json
{
  "version": "2.0.0",
  "build": 333,
  "stage": "stable",
  "releaseDate": "2026-07-10"
}
```

**Note:** From v1.0.36 (2026-06-11) through v1.2.112 (2026-07-10), the project intentionally stayed in the `alpha` stage past `1.0.0` rather than moving to a bare `stable` stage as the table above originally intended — active development continued with frequent alpha builds while the VideoEngineV2 rewrite, GPU-accelerated preview, and native V2 metadata/verification support settled. **As of v1.2.33, version strings stopped carrying a `-alpha` suffix** (was `1.2.x-alpha`, now plain `1.2.x`); maturity was tracked only via the separate `stage` field, which is not automatically derived from the version number and had to be kept in sync manually. **As of v2.0.0 (2026-07-10), the project has moved to the `stable` stage** — the alpha-era note above is now historical context, not current status.

## Bump process (manual)

Version bumps since v1.2.30-alpha have been done by directly editing these files in lockstep:

- `source/MultiCamApp/MultiCamApp/config/version.json` (`version`, `build`)
- `source/MultiCamApp/MultiCamApp/MultiCamApp.csproj` (`<Version>`, `<FileVersion>`, `<InformationalVersion>`)
- `installer/MultiCamApp.iss` (`#define AppVersion`, `#define AppVersionNumeric`)
- `source/MultiCamApp/MultiCamApp/capture/backend/VideoEngineRegistry.cs` (`BackendVersion` constant)
- `source/MultiCamApp/MultiCamApp.Tests/VideoEngineBackendTests.cs` (matching `BackendVersion` assertion)

alongside a detailed hand-written entry in the **root [`CHANGELOG.md`](../../CHANGELOG.md)** — the authoritative, current changelog. `docs/changelogs/CHANGELOG.md` is a historical archive frozen at v1.2.30-alpha (2026-07-02) and is not updated going forward.

**Note:** an earlier automated `bump_version.py` tool (previously at `installer/bump_version.py` and `scripts/maintenance/bump_version.py`) was never wired into the actual `build_release.bat` pipeline and wrote only to the frozen `docs/changelogs/CHANGELOG.md` archive; it was removed as dead code (v1.2.107) rather than kept as an unused, drifting alternative to the manual process above.

Runtime reads version via `VersionService` (About page, recording metadata, assembly info).

## Increment rules

- **Patch** (`+0.0.1`): bug fixes, UI tweaks, metadata/diagnostics fixes
- **Minor** (`+0.1.0`): new features, modules, workflow or architecture improvements
- **Major** (`+1.0.0`): major rewrite, breaking changes, stable milestone

As of `v2.0.0`, the project is on its second stable major line — future patch/minor bumps continue under `2.x.y` following the increment rules above; reserve another major bump (`3.0.0`) for the next breaking-change/architecture milestone, not routine fixes.
