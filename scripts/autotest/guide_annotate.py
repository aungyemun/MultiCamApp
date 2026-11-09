"""Annotated user-guide images for README.md (docs/images/guide/*.png).

Input: raw window captures and element-rectangle JSON written by scripts/autotest/guide_screenshots.ps1.
Output: cropped screenshots with numbered callouts, short labels and a soft rounded frame.

The main page shows the Windows Videos folder, which contains the user name; it is replaced by the
generic placeholder C:\\Users\\<you>\\Videos (MASK_TEXT) before anything is saved.

Usage: python guide_annotate.py <raw dir> <output dir>
Requires Pillow. Fonts: Inter (if installed) or Segoe UI on Windows.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ACCENT = (251, 191, 36)        # amber: readable on the app's dark navy UI
ACCENT_FILL = (251, 191, 36, 34)
INK = (17, 24, 39)
PILL_BG = (15, 23, 42, 238)
PILL_TEXT = (248, 250, 252)
MASK_TEXT = r"C:\Users\<you>\Videos"


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    names = (["Inter-SemiBold.otf", "Inter-SemiBold.ttf", "segoeuib.ttf", "DejaVuSans-Bold.ttf"] if bold
             else ["Inter-Regular.otf", "Inter-Regular.ttf", "segoeui.ttf", "DejaVuSans.ttf"])
    dirs = ["", "/usr/share/fonts/truetype/inter/", "/usr/share/fonts/opentype/inter/",
            "/usr/share/fonts/truetype/dejavu/", "C:/Windows/Fonts/"]
    for n in names:
        for d in dirs:
            try:
                return ImageFont.truetype(d + n, size)
            except OSError:
                continue
    import matplotlib.font_manager as fm  # last resort
    path = fm.findfont(fm.FontProperties(family="Inter", weight="semibold" if bold else "regular"))
    return ImageFont.truetype(path, size)


def union(rects):
    xs = [r[0] for r in rects]; ys = [r[1] for r in rects]
    xe = [r[0] + r[2] for r in rects]; ye = [r[1] + r[3] for r in rects]
    return [min(xs), min(ys), max(xe) - min(xs), max(ye) - min(ys)]


def resolve(target, elems):
    """target: element key, list of keys (union), or a literal [x, y, w, h]."""
    if isinstance(target, str):
        return elems[target]
    if all(isinstance(t, (int, float)) for t in target):
        return list(target)
    return union([resolve(t, elems) for t in target])


# Each image: source capture, crop box (x0, y0, x1, y1) in window pixels or None for the whole window,
# and callouts (number, target, legend text, highlight padding[, badge position: corner|tr|left|top]). The legend is drawn below the screenshot,
# so labels never cover the interface.
SPECS = {
    "guide_1_cameras": ("main_s0", None, [
        (1, ["radio:1 Camera", "radio:4 Cameras"], "Choose how many cameras to use (1 to 4).", 8, "left"),
        (2, "Cam1Box", "Pick a camera for each slot.", 6, "left"),
        (3, "RefreshDevicesBtn", "Plugged in a camera after starting? Click Refresh cameras.", 4, "left"),
        (4, [518, 198, 1046, 724], "Each camera's live picture appears here after Start Preview.", 0, "top"),
    ]),
    "guide_2_quality": ("main_s60", (0, 205, 1080, 520), [
        (1, "ResolutionBox", "Picture size. 720p is a good starting point.", 6, "left"),
        (2, "FpsBox", "Frames per second. 30 fps suits most recordings.", 6, "left"),
        (3, "AdvancedCameraControlsExpander", "Optional: focus, exposure, anti-flicker and video quality.", 4, "left"),
    ]),
    "guide_3_save": ("main_s100", (0, 330, 1080, 640), [
        (1, ["OutputBox", "BrowseBtn"], "Where recordings are saved. Click Browse... to change it.", 6, "left"),
        (2, "SessionBox", "Optional name for this recording.", 6, "left"),
        (3, "SessionFolderPreviewValue", "Each recording gets its own folder, named with the date and time.", 4, "left"),
    ]),
    "guide_4_record": ("main_s100", None, [
        (1, "StartPreviewBtn", "Start Preview: check that every picture looks right.", 5, "left"),
        (2, "StartRecordBtn", "Start Recording: all cameras record together.", 5, "tr"),
        (3, "StopRecordBtn", "Stop Recording when you are finished.", 5, "tr"),
        (4, ["StatusSectionTitle", "StatusValue", "ElapsedValue", "CamerasValue"],
         "Status and recording time.", 10, "tr"),
    ]),
    "guide_5_verify": ("verify_s0", (0, 175, 1600, 410), [
        (1, "BrowseFolderBtn", "Choose the folder that holds your recordings.", 4),
        (2, "VerifyAllBtn", "Verify All: check every video in the folder.", 4),
        (3, "DeepVerifyBtn", "Deep Verify: extra frame-by-frame check (takes longer).", 4),
        (4, ["ExportTxtBtn", "ExportJsonBtn", "ExportCsvBtn", "ExportAllBtn"], "Save the report as a file.", 5),
    ]),
    "guide_6_results": ("verify_s0", (0, 530, 1600, 910), [
        (1, ["CardOverallLabel", "CardOverallValue"], "Overall result for the whole folder.", 14, "tr"),
        (2, ["CardPassLabel", "CardPassValue", "CardFailLabel", "CardFailValue"],
         "How many videos passed, need attention, or failed.", 14, "tr"),
        (3, ["ScientificTimingConfidenceLabel", "CardTimingConfidenceValue"], "How reliable the timing is.", 14, "tr"),
    ]),
    "guide_7_sessions": ("verify_s50", (0, 505, 1600, 935), [
        (1, ["SimpleViewRadio", "DetailedViewRadio"], "Switch between a simple and a detailed table.", 6, "tr"),
        (2, [25, 590, 470, 30], "One section for each recording session.", 2, "tr"),
        (3, [25, 840, 150, 75], "Result for each camera.", 2, "tr"),
        (4, [893, 840, 242, 75], "Frames saved and frames lost (0 lost is what you want).", 2, "tr"),
    ]),
    "guide_8_hardware": ("hardware", (0, 175, 1600, 905), [
        (1, "RunScanButton", "Check this computer and its cameras.", 5, "tr"),
        (2, ["StatusText", "RecommendedPresetText"], "Plain-language advice.", 6, "tr"),
        (3, ["UsbStatusCardTitleText", "UsbStatusCardText"], "Which USB connection each camera uses.", 12, "tr"),
    ]),
    "guide_9_about": ("about", None, []),
}


def text_size(draw, text, f):
    b = draw.textbbox((0, 0), text, font=f)
    return b[2] - b[0], b[3] - b[1], b[1]


def annotate(raw: Path, name: str, src: str, crop, callouts) -> Image.Image:
    base = Image.open(raw / f"{src}.png").convert("RGBA")
    elems = json.loads((raw / f"{src}.json").read_text(encoding="utf-8-sig"))

    # privacy: replace the user's Videos path in the output-folder box
    if "OutputBox" in elems and elems["OutputBox"][1] < base.size[1] and elems["OutputBox"][1] + elems["OutputBox"][3] > 0:
        x, y, w, h = elems["OutputBox"]
        if y + h > base.size[1] or y < 0 or x + w > base.size[0]:
            raise SystemExit(f"{name}: output-folder box only partly visible; choose another capture")
        d0 = ImageDraw.Draw(base)
        bg = base.getpixel((x + w - 8, y + h // 2))
        d0.rectangle([x + 3, y + 3, x + w - 4, y + h - 4], fill=bg)
        f0 = font(15)
        tw, th, off = text_size(d0, MASK_TEXT, f0)
        d0.text((x + 12, y + (h - th) / 2 - off), MASK_TEXT, font=f0, fill=(229, 231, 235))

    overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(overlay)
    fnum = font(19, bold=True)
    boxes = []
    for n, target, _, pad, *opt in callouts:
        at = opt[0] if opt else "corner"
        x, y, w, h = resolve(target, elems)
        box = (x - pad, y - pad, x + w + pad, y + h + pad)
        big = (box[2] - box[0]) * (box[3] - box[1]) > 150_000
        d.rounded_rectangle(box, radius=9, fill=None if big else ACCENT_FILL, outline=ACCENT, width=3)
        boxes.append((n, box, big, at))
    for n, (x0, y0, x1, y1), big, at in boxes:      # badges last, so no outline crosses them
        cx, cy = {"corner": (x0, y0), "tr": (x1, y0), "left": (x0 - 21, (y0 + y1) / 2),
                  "top": ((x0 + x1) / 2, y0)}[at]
        badge(d, cx, cy, n, fnum)
    img = Image.alpha_composite(base, overlay)
    if crop:
        img = img.crop(crop)
    return legend(img, [(c[0], c[2]) for c in callouts])


def badge(d, cx, cy, n, f, r=16):
    d.ellipse([cx - r - 2, cy - r - 2, cx + r + 2, cy + r + 2], fill=INK)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ACCENT)
    tw, th, off = text_size(d, str(n), f)
    d.text((cx - tw / 2, cy - th / 2 - off), str(n), font=f, fill=INK)


def legend(img: Image.Image, items) -> Image.Image:
    """Numbered explanations in a strip below the screenshot."""
    if not items:
        return img
    w = img.size[0]
    f, fnum = font(20), font(19, bold=True)
    cols = 2 if w >= 1300 else 1
    rows = (len(items) + cols - 1) // cols
    row_h, pad_x, pad_y = 46, 30, 22
    h = pad_y * 2 + rows * row_h
    strip = Image.new("RGBA", (w, h), (11, 18, 32, 255))
    d = ImageDraw.Draw(strip)
    d.line([(0, 0), (w, 0)], fill=ACCENT, width=3)
    col_w = (w - 2 * pad_x) / cols
    for i, (n, text) in enumerate(items):
        c, r = (i // rows, i % rows) if cols > 1 else (0, i)
        x = pad_x + c * col_w
        y = pad_y + r * row_h + row_h / 2
        badge(d, x + 16, y, n, fnum)
        tw, th, off = text_size(d, text, f)
        d.text((x + 46, y - th / 2 - off), text, font=f, fill=PILL_TEXT)
    out = Image.new("RGBA", (w, img.size[1] + h))
    out.paste(img, (0, 0)); out.paste(strip, (0, img.size[1]))
    return out


def frame(img: Image.Image, radius: int = 14, margin: int = 22) -> Image.Image:
    w, h = img.size
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, fill=255)
    out = Image.new("RGBA", (w + 2 * margin, h + 2 * margin), (0, 0, 0, 0))
    shadow = Image.new("RGBA", out.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([margin, margin + 6, margin + w, margin + h + 6], radius=radius,
                                             fill=(0, 0, 0, 110))
    out = Image.alpha_composite(out, shadow.filter(ImageFilter.GaussianBlur(10)))
    card = img.copy(); card.putalpha(mask)
    out.alpha_composite(card, (margin, margin))
    ImageDraw.Draw(out).rounded_rectangle([margin, margin, margin + w - 1, margin + h - 1], radius=radius,
                                          outline=(51, 65, 85, 255), width=1)
    return out


def main() -> None:
    raw, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    only = set(sys.argv[3:])
    for name, (src, crop, callouts) in SPECS.items():
        if only and name not in only:
            continue
        img = frame(annotate(raw, name, src, crop, callouts))
        img.save(out / f"{name}.png", optimize=True)
        print(f"{name}.png  {img.size[0]}x{img.size[1]}")


if __name__ == "__main__":
    main()
