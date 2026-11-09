"""Render the README guide illustrations from guide_graphics.html.

Usage: python render.py <output dir>
Requires the Python Playwright package with Chromium. No network access is used: the page
loads no external resources.
"""
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

PANELS = {"workflow": "guide_workflow.png", "folder": "guide_folder.png", "results": "guide_results.png"}


def main() -> None:
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    page_url = (Path(__file__).parent / "guide_graphics.html").resolve().as_uri()
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={"width": 1200, "height": 900}, device_scale_factor=2)
        page.route("**/*", lambda r: r.continue_() if r.request.url.startswith("file:") else r.abort())
        page.goto(page_url)
        page.evaluate("document.fonts.ready")
        for sel, name in PANELS.items():
            # include the shadow: pad the clip around the panel
            box = page.locator(f"#{sel}").bounding_box()
            clip = {"x": box["x"] - 20, "y": box["y"] - 14, "width": box["width"] + 40, "height": box["height"] + 44}
            page.screenshot(path=str(out / name), clip=clip, full_page=True, omit_background=True)
            print(name)
        browser.close()


if __name__ == "__main__":
    main()
