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
  "version": "1.1.0",
  "build": 193,
  "stage": "stable",
  "releaseDate": "2026-06-23",
  "notes": ""
}
```

## Bump commands

```powershell
python installer\bump_version.py patch --notes "Fix preview layout on 125% scaling"
python installer\bump_version.py minor --notes "Add session export tool"
python installer\bump_version.py major --notes "Stable 1.0 architecture"
python installer\bump_version.py --show
```

Automatically updates:

- `config/version.json`
- `MultiCamApp.csproj` (`<Version>`)
- `installer/MultiCamApp.iss`
- `docs/changelogs/CHANGELOG.md`

Runtime reads version via `VersionService` (About page, recording metadata, assembly info).

## Increment rules

- **Patch** (`+0.0.1`): bug fixes, UI tweaks, metadata/diagnostics fixes
- **Minor** (`+0.1.0`): new features, modules, workflow or architecture improvements
- **Major** (`+1.0.0`): major rewrite, breaking changes, stable milestone

Recommendation: stay on `0.0.x` during active testing; do not rush to `1.0.0`.
