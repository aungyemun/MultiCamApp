"""Step 1 of 2 for the guide images: numbered markers on the raw window captures.

Reads <raw>/<source>.png + .json (from scripts/autotest/guide_screenshots.ps1) and writes one cropped,
marked-up screenshot per card to <work>/<card>.png. render.py then builds the finished cards.

Privacy: the main page shows the Windows Videos folder, which contains the user name. It is replaced
by the generic placeholder MASK_TEXT before anything is saved; the run stops if the box is only
partly visible, so a partial user name can never be published.

Usage: python annotate.py <raw dir> <work dir>
Requires Pillow. Fonts: Inter if installed, otherwise Segoe UI (Windows) or DejaVu Sans.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).parent))
from guide_spec import CARDS  # noqa: E402

ACCENT = (251, 191, 36)              # amber, readable on the app's dark navy UI
ACCENT_FILL = (251, 191, 36, 30)
INK = (17, 24, 39)
MASK_TEXT = r"C:\Users\<you>\Videos"

FONT_FILES = {
    False: ["/usr/share/fonts/opentype/inter/Inter-Regular.otf", "C:/Windows/Fonts/segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"],
    True: ["/usr/share/fonts/opentype/inter/Inter-Bold.otf", "C:/Windows/Fonts/segoeuib.ttf",
           "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"],
}


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    for path in FONT_FILES[bold]:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    raise SystemExit("no usable font found (install Inter or run on Windows)")


def union(rects):
    x0 = min(r[0] for r in rects); y0 = min(r[1] for r in rects)
    x1 = max(r[0] + r[2] for r in rects); y1 = max(r[1] + r[3] for r in rects)
    return [x0, y0, x1 - x0, y1 - y0]


def resolve(target, elems):
    if isinstance(target, str):
        return elems[target]
    if all(isinstance(t, (int, float)) for t in target):
        return list(target)
    return union([resolve(t, elems) for t in target])


def text_box(draw, text, f):
    b = draw.textbbox((0, 0), text, font=f)
    return b[2] - b[0], b[3] - b[1], b[1]


def badge(draw, cx, cy, n, f, r=17):
    draw.ellipse([cx - r - 3, cy - r - 3, cx + r + 3, cy + r + 3], fill=INK)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ACCENT)
    tw, th, off = text_box(draw, str(n), f)
    draw.text((cx - tw / 2 - 0.5, cy - th / 2 - off), str(n), font=f, fill=INK)


def mask_user_path(img: Image.Image, elems: dict, crop) -> None:
    if "OutputBox" not in elems:
        return
    x, y, w, h = elems["OutputBox"]
    cx0, cy0, cx1, cy1 = crop
    # The settings panel scrolls underneath the fixed Controls section; UI Automation still reports a
    # position for a box scrolled out of view, so clip against the panel's visible bottom edge too.
    view_bottom = elems["ControlsSectionTitle"][1] - 24 if "ControlsSectionTitle" in elems else img.size[1]
    vis_y1 = min(cy1, view_bottom)
    if x + w <= cx0 or x >= cx1 or y + h <= cy0 or y >= vis_y1:
        return                                            # not visible in the published area
    if x < cx0 or y < cy0 or x + w > cx1 or y + h > vis_y1:
        raise SystemExit("output-folder box only partly visible; adjust the crop or recapture")
    d = ImageDraw.Draw(img)
    bg = img.getpixel((x + w - 6, y + h // 2))
    d.rectangle([x + 3, y + 3, x + w - 4, y + h - 4], fill=bg)
    f = font(15)
    _, th, off = text_box(d, MASK_TEXT, f)
    d.text((x + 12, y + (h - th) / 2 - off), MASK_TEXT, font=f, fill=(229, 231, 235))


def annotate(raw: Path, card: dict) -> Image.Image:
    base = Image.open(raw / f"{card['source']}.png").convert("RGBA")
    elems = json.loads((raw / f"{card['source']}.json").read_text(encoding="utf-8-sig"))
    mask_user_path(base, elems, card["crop"])
    overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(overlay)
    boxes = []
    for n, target, pad, at, *_ in card["items"]:
        x, y, w, h = resolve(target, elems)
        box = (x - pad, y - pad, x + w + pad, y + h + pad)
        d.rounded_rectangle(box, radius=9, fill=ACCENT_FILL, outline=ACCENT, width=3)
        boxes.append((n, box, at))
    fnum = font(19, bold=True)
    for n, (x0, y0, x1, y1), at in boxes:          # badges last, so no outline crosses them
        cx, cy = {"corner": (x0, y0), "tr": (x1, y0), "left": (x0 - 22, (y0 + y1) / 2),
                  "top": ((x0 + x1) / 2, y0)}[at]
        badge(d, cx, cy, n, fnum)
    return Image.alpha_composite(base, overlay).crop(card["crop"])


def main() -> None:
    raw, work = Path(sys.argv[1]), Path(sys.argv[2])
    work.mkdir(parents=True, exist_ok=True)
    for name, card in CARDS.items():
        img = annotate(raw, card)
        img.convert("RGB").save(work / f"{name}.png", optimize=True)
        print(f"{name}.png  {img.size[0]}x{img.size[1]}")


if __name__ == "__main__":
    main()
