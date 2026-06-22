#!/usr/bin/env python3
"""Build MultiCamApp.ico from Multicam.png (source assets only)."""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PNG = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "assets" / "icons" / "Multicam.png"
ICO = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "assets" / "icons" / "MultiCamApp.ico"
SIZES = (16, 24, 32, 48, 64, 128, 256)


def square_rgba(img):
    from PIL import Image

    img = img.convert("RGBA")
    w, h = img.size
    side = max(w, h)
    pad = max(4, int(side * 0.06))
    canvas_side = side + pad * 2
    canvas = Image.new("RGBA", (canvas_side, canvas_side), (0, 0, 0, 0))
    canvas.paste(img, ((canvas_side - w) // 2, (canvas_side - h) // 2), img)
    return canvas


def main() -> int:
    try:
        from PIL import Image, ImageFilter
    except ImportError:
        print("Install Pillow: scripts\\setup\\install_dev_environment.ps1", file=sys.stderr)
        return 1
    if not PNG.is_file():
        print(f"Missing {PNG}", file=sys.stderr)
        return 1
    base = square_rgba(Image.open(PNG))
    base = base.filter(ImageFilter.UnsharpMask(radius=1.2, percent=130, threshold=2))
    master = base.resize((256, 256), Image.Resampling.LANCZOS)
    ICO.parent.mkdir(parents=True, exist_ok=True)
    master.save(ICO, format="ICO", sizes=[(s, s) for s in SIZES])
    print(f"Wrote {ICO}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
