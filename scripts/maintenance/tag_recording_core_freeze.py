#!/usr/bin/env python3
"""Deprecated wrapper — use tag_stable_core_v1.py (STABLE_CORE_V1 supersedes STABLE_RECORDING_CORE_V1)."""
import subprocess
import sys
from pathlib import Path

if __name__ == "__main__":
    target = Path(__file__).with_name("tag_stable_core_v1.py")
    raise SystemExit(subprocess.call([sys.executable, str(target)]))
