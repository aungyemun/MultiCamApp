#!/usr/bin/env python3
"""Build the app icon assets from Multicam.png (source artwork, unchanged).

Outputs (both regenerated from the same artwork, so they stay consistent):
  * MulticamMark.png - 256 px text-free mark (the two cameras and the record dot on a white
    rounded tile with transparent corners). The full logo's "Record Multiple Cameras" text
    is unreadable below ~64 px, so the header logo and small icon sizes use this mark.
  * MultiCamApp.ico  - 16/24/32/48/64 px frames from the mark, 128/256 px from the full logo.
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ICONS = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "assets" / "icons"
PNG = ICONS / "Multicam.png"
MARK = ICONS / "MulticamMark.png"
ICO = ICONS / "MultiCamApp.ico"
SMALL_SIZES = (16, 24, 32, 48, 64)
LARGE_SIZES = (128, 256)
# Graphic area above the text in Multicam.png, as fractions of the image size
# (measured on the 1254 px artwork: x 110-1144, y 240-846).
ART_BOX = (110 / 1254, 240 / 1254, 1144 / 1254, 846 / 1254)


def rounded_mask(size: int, radius_frac: float):
    from PIL import Image, ImageDraw

    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=int(size * radius_frac), fill=255)
    return mask


def build_mark(src, size: int = 1024):
    from PIL import Image

    w, h = src.size
    art = src.convert("RGB").crop(tuple(int(v * (w if i % 2 == 0 else h)) for i, v in enumerate(ART_BOX)))
    tile = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    tw = int(size * 0.86)
    th = int(art.size[1] * tw / art.size[0])
    tile.paste(art.resize((tw, th), Image.Resampling.LANCZOS), ((size - tw) // 2, (size - th) // 2))
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(tile, (0, 0), rounded_mask(size, 0.18))
    return out


def main() -> int:
    try:
        from PIL import Image, ImageFilter
    except ImportError:
        print("Install Pillow: scripts\\setup\\install_dev_environment.ps1", file=sys.stderr)
        return 1
    if not PNG.is_file():
        print(f"Missing {PNG}", file=sys.stderr)
        return 1
    source = Image.open(PNG)
    mark = build_mark(source)
    mark.resize((256, 256), Image.Resampling.LANCZOS).save(MARK, format="PNG")
    print(f"Wrote {MARK}")

    # The artwork is opaque RGB: give the full logo the same transparent rounded corners as the mark.
    full = source.convert("RGBA")
    clipped = Image.new("RGBA", full.size, (0, 0, 0, 0))
    clipped.paste(full, (0, 0), rounded_mask(full.size[0], 0.18))
    full = clipped.filter(ImageFilter.UnsharpMask(radius=1.2, percent=130, threshold=2))
    frames = [mark.resize((s, s), Image.Resampling.LANCZOS) for s in SMALL_SIZES]
    frames += [full.resize((s, s), Image.Resampling.LANCZOS) for s in LARGE_SIZES]
    ICO.parent.mkdir(parents=True, exist_ok=True)
    frames[-1].save(ICO, format="ICO", sizes=[f.size for f in frames], append_images=frames[:-1])
    print(f"Wrote {ICO}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
