"""
Bump patch version before packaging (called from build_release.bat).

Syncs version.json, csproj, and installer/MultiCamApp.iss via the project bump script.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BUMP = ROOT / "scripts" / "maintenance" / "bump_version.py"


def main() -> int:
    if not BUMP.is_file():
        print(f"Missing bump script: {BUMP}", file=sys.stderr)
        return 1
    proc = subprocess.run(
        [sys.executable, str(BUMP), "patch", "-n",
         "Release maintenance build.\n"
         "Keep version metadata, installer AppVersion, and published executable versions in sync.\n"
         "Use build_release.bat to produce a verified dist bundle and installer."],
        cwd=ROOT,
    )
    return proc.returncode


if __name__ == "__main__":
    sys.exit(main())
