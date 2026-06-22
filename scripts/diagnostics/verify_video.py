#!/usr/bin/env python3
"""Verify MP4 files (duration, resolution, codec) using ffprobe if available."""
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path


def _find_ffprobe() -> str | None:
    root = os.environ.get("MULTICAMAPP_ROOT")
    if root:
        local = Path(root) / "runtime" / "ffmpeg" / "ffprobe.exe"
        if local.is_file():
            return str(local)
    return shutil.which("ffprobe")


def probe(path: Path) -> dict:
    ffprobe = _find_ffprobe()
    if not ffprobe:
        return {"file": str(path), "error": "ffprobe not found"}
    cmd = [
        ffprobe, "-v", "quiet", "-print_format", "json",
        "-show_format", "-show_streams", str(path)
    ]
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        return {"file": str(path), "error": r.stderr}
    data = json.loads(r.stdout)
    video = next((s for s in data.get("streams", []) if s.get("codec_type") == "video"), {})
    avg_rate = video.get("avg_frame_rate")
    raw_rate = video.get("r_frame_rate")
    def _rate_to_float(rate: str | None) -> float:
        if not rate or rate == "0/0":
            return 0.0
        if "/" in rate:
            n, d = rate.split("/", 1)
            try:
                return float(n) / float(d) if float(d) != 0 else 0.0
            except ValueError:
                return 0.0
        try:
            return float(rate)
        except ValueError:
            return 0.0
    avg_fps = _rate_to_float(avg_rate)
    raw_fps = _rate_to_float(raw_rate)
    return {
        "file": str(path),
        "codec": video.get("codec_name"),
        "width": video.get("width"),
        "height": video.get("height"),
        "fps": avg_rate,
        "avg_frame_rate": avg_rate,
        "r_frame_rate": raw_rate,
        "constant_fps": abs(avg_fps - raw_fps) <= 0.05 if avg_fps and raw_fps else None,
        "frame_count": video.get("nb_frames"),
        "duration": data.get("format", {}).get("duration"),
        "size_bytes": data.get("format", {}).get("size"),
    }


def main():
    if len(sys.argv) < 2:
        print("Usage: verify_video.py <file.mp4> [file2 ...]")
        sys.exit(1)
    for p in sys.argv[1:]:
        print(json.dumps(probe(Path(p)), indent=2))


if __name__ == "__main__":
    main()
