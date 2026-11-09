"""Step 2 of 2 for the guide images: finished cards, illustrations and the social preview.

Inputs:  <work dir> from annotate.py (marked-up screenshots), guide_spec.py, style.css, illustrations.html,
         the app mark (source/MultiCamApp/MultiCamApp/assets/icons/MulticamMark.png) and the main-window
         screenshot (docs/images/main_en.png) for the social preview.
Output:  <out dir>/*.png (docs/images/guide/) and docs/images/social_preview.png.

Usage: python render.py <work dir> <out dir> [--repo <repository root>] [--social <png path>]
Requires the Python Playwright package with Chromium. Pages are loaded from local files only;
every network request is blocked.
"""
from __future__ import annotations

import argparse
import base64
import html
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from guide_spec import CARDS  # noqa: E402

CARD_SCALE = 1                  # screenshots keep their real pixels
ILLUSTRATION_SCALE = 1704 / 1100  # illustrations come out as wide as the cards


def data_uri(path: Path) -> str:
    return "data:image/png;base64," + base64.b64encode(path.read_bytes()).decode()


def card_html(name: str, card: dict, shot: Path, logo: str) -> str:
    items = "".join(
        f'<li><div class="n">{n}</div><div><b>{html.escape(t)}</b><span>{html.escape(d)}</span></div></li>'
        for n, _target, _pad, _at, t, d in card["items"])
    tip = (f'<div class="tip"><b>Tip</b>{html.escape(card["tip"])}</div>' if card.get("tip") else "")
    return f"""
<section class="panel sc" id="{name}">
  <div class="head">
    <div><div class="eyebrow">{html.escape(card['eyebrow'])}</div><h2>{html.escape(card['title'])}</h2>
         <p class="lead">{html.escape(card['lead'])}</p></div>
    <div class="brand"><img src="{logo}" alt="">MultiCamApp</div>
  </div>
  <div class="{card['layout']}"><img class="shot" src="{data_uri(shot)}" alt="">
    <div class="col"><ol>{items}</ol>{tip}</div></div>
</section>"""


def shoot(page, ids, out: Path, pad: int) -> None:
    for el_id in ids:
        box = page.locator(f"#{el_id}").bounding_box()
        clip = {"x": box["x"] - pad, "y": box["y"] - pad, "width": box["width"] + 2 * pad,
                "height": box["height"] + 2 * pad + 8}
        page.screenshot(path=str(out / f"{el_id}.png"), clip=clip, full_page=True, omit_background=True)
        print(f"{el_id}.png")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("work"); ap.add_argument("out")
    ap.add_argument("--repo", default=str(HERE.parents[2]))
    ap.add_argument("--social", default=None)
    a = ap.parse_args()
    work, out, repo = Path(a.work), Path(a.out), Path(a.repo)
    out.mkdir(parents=True, exist_ok=True)
    social_out = Path(a.social) if a.social else repo / "docs" / "images" / "social_preview.png"
    logo = data_uri(repo / "source" / "MultiCamApp" / "MultiCamApp" / "assets" / "icons" / "MulticamMark.png")
    hero = data_uri(repo / "docs" / "images" / "main_en.png")

    cards_page = HERE / "_cards.html"
    illus_page = HERE / "_illustrations.html"
    body = "".join(card_html(n, c, work / f"{n}.png", logo) for n, c in CARDS.items())
    cards_page.write_text(f'<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="style.css">'
                          f'</head><body>{body}</body></html>', encoding="utf-8")
    illus_page.write_text((HERE / "illustrations.html").read_text(encoding="utf-8")
                          .replace("{{LOGO}}", logo).replace("{{HERO}}", hero), encoding="utf-8")
    try:
        with sync_playwright() as p:
            browser = p.chromium.launch()
            for page_file, scale, ids, pad in (
                    (cards_page, CARD_SCALE, list(CARDS), 40),
                    (illus_page, ILLUSTRATION_SCALE,
                     ["overview_workflow", "step6_recording_folder", "step8_results_meaning"], 26)):
                page = browser.new_page(viewport={"width": 1800, "height": 1000}, device_scale_factor=scale)
                page.route("**/*", lambda r: r.continue_() if r.request.url.startswith(("file:", "data:")) else r.abort())
                page.goto(page_file.as_uri())
                page.evaluate("document.fonts.ready")
                shoot(page, ids, out, pad)
                if page_file is illus_page:
                    social = browser.new_page(viewport={"width": 1400, "height": 800}, device_scale_factor=1)
                    social.route("**/*", lambda r: r.continue_() if r.request.url.startswith(("file:", "data:")) else r.abort())
                    social.goto(page_file.as_uri()); social.evaluate("document.fonts.ready")
                    box = social.locator("#social_preview").bounding_box()
                    clip = {"x": round(box["x"]), "y": round(box["y"]), "width": 1280, "height": 640}
                    social.screenshot(path=str(social_out), clip=clip, full_page=True)
                    print(social_out.name)
            browser.close()
    finally:
        cards_page.unlink(missing_ok=True); illus_page.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
