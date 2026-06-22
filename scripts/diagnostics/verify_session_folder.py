#!/usr/bin/env python3
"""Developer helper: scan a session folder and print ffprobe results for each MP4."""
import json
import sys
from pathlib import Path

# Reuse single-file probe from verify_video.py in same directory
sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_video import probe  # noqa: E402


def main():
    if len(sys.argv) < 2:
        print("Usage: verify_session_folder.py <session_folder>")
        sys.exit(1)
    root = Path(sys.argv[1])
    if not root.is_dir():
        print(f"Not a directory: {root}")
        sys.exit(1)
    mp4s = sorted(root.rglob("*.mp4"))
    print(f"Session: {root}")
    print(f"Found {len(mp4s)} MP4 file(s)\n")
    for p in mp4s:
        print(json.dumps(probe(p), indent=2))
        print()


if __name__ == "__main__":
    main()
