#!/usr/bin/env python3
"""Combine cam1..cam4 metadata.txt into a session summary snippet."""
from pathlib import Path
import sys


def read_meta(cam_dir: Path) -> str:
    f = cam_dir / "metadata.txt"
    return f.read_text(encoding="utf-8") if f.exists() else ""


def main():
    if len(sys.argv) < 2:
        print("Usage: summarize_metadata.py <session_folder>")
        sys.exit(1)
    session = Path(sys.argv[1])
    parts = []
    for name in ("cam1", "cam2", "cam3", "cam4"):
        d = session / name
        if d.is_dir():
            parts.append(f"=== {name} ===\n{read_meta(d)}")
    out = session / "session_summary_generated.txt"
    out.write_text("\n\n".join(parts), encoding="utf-8")
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
