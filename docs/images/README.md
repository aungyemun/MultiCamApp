# Images

Screenshots and illustrations of MultiCamApp for the README, the user guides and other material such as slides, posters or posts. All images show the real application (version 2.1.0, build 339).

## Guide cards — `guide/`

Self-contained images: each has a step label, a title, the MultiCamApp mark and, for screenshots, numbered markers with their explanations. They can be used on their own without surrounding text.

| File | Shows | Size (px) |
| --- | --- | --- |
| [`overview_workflow.png`](guide/overview_workflow.png) | The five steps: install, connect, set up, record, check | 1785 × 649 |
| [`step2_choose_cameras.png`](guide/step2_choose_cameras.png) | Step 2: number of cameras, camera per slot, Refresh cameras | 1784 × 1149 |
| [`step3_picture_settings.png`](guide/step3_picture_settings.png) | Step 3: resolution, frame rate, Advanced Camera Controls | 1784 × 1149 |
| [`step4_save_location.png`](guide/step4_save_location.png) | Step 4: output folder, session title, final folder name | 1784 × 1149 |
| [`step5_preview_record.png`](guide/step5_preview_record.png) | Step 5: Start Preview, Start/Stop Recording, status | 1784 × 1149 |
| [`step6_recording_folder.png`](guide/step6_recording_folder.png) | Step 6: contents of a recording folder | 1785 × 855 |
| [`step7_verify.png`](guide/step7_verify.png) | Step 7: Browse, Verify All, Deep Verify, export | 1784 × 859 |
| [`step8_results_summary.png`](guide/step8_results_summary.png) | Step 8: overall result, counts, timing confidence | 1784 × 898 |
| [`step8_results_meaning.png`](guide/step8_results_meaning.png) | Step 8: what PASS, WARNING and FAIL mean | 1785 × 697 |
| [`step8_session_table.png`](guide/step8_session_table.png) | Step 8: per-session table, result and frames per camera | 1784 × 946 |
| [`extra_hardware_diagnostics.png`](guide/extra_hardware_diagnostics.png) | Hardware Diagnostics: scan, advice, USB connection | 1784 × 1324 |
| [`extra_about.png`](guide/extra_about.png) | About window: version, Copy Version Info, licence | 1784 × 939 |

All cards share one style: dark navy background, amber markers, Inter typeface, transparent rounded corners with a soft shadow (they work on light and dark pages). Screenshots keep their real pixels (no scaling).

## Social preview

| File | Use | Size (px) |
| --- | --- | --- |
| [`social_preview.png`](social_preview.png) | Link preview for the repository. Upload it under **Settings → General → Social preview** on GitHub. | 1280 × 640 |

## Application screenshots

Plain window captures (Windows 11), used in the README gallery.

| File | Shows | Size (px) |
| --- | --- | --- |
| [`main_en.png`](main_en.png) | Main window, 4-camera layout | 1600 × 1000 |
| [`main_advanced_en.png`](main_advanced_en.png) | Main window with Advanced Camera Controls open | 1600 × 1000 |
| [`main_ja.png`](main_ja.png) | Main window, Japanese user interface | 1600 × 1000 |
| [`verify_ready_en.png`](verify_ready_en.png) | Video Verification before a run | 1600 × 1000 |
| [`verify_results_en.png`](verify_results_en.png) | Video Verification after Verify All and Deep Verify | 1600 × 1000 |
| [`verify_results_ja.png`](verify_results_ja.png) | Video Verification results, Japanese user interface | 1600 × 1000 |
| [`hardware_en.png`](hardware_en.png) | Hardware Diagnostics after a scan | 1600 × 1000 |

## Privacy

- Camera preview is turned off in every screenshot, so no camera images appear.
- Verification screenshots use demo recordings made for this purpose; the folder is shown as `V:\`.
- The Windows user name in the save path is replaced with `C:\Users\<you>\Videos` before an image is saved. The script stops if the path is only partly visible.
- Device names (webcam models) and the hardware summary of the test computer are visible; they are not personal data.

## How the images are made

Everything is reproducible from the repository with the scripts in [`scripts/autotest/`](../../scripts/autotest/README.md):

1. **Capture** (Windows, app built): `guide_screenshots.ps1 -Folder <demo sessions> -OutDir <raw>` — drives the app with UI Automation, saves window captures and the on-screen position of every control. Map the demo folder to a drive letter first (`subst V: <folder>`) for a short path.
2. **Mark up**: `python guide_graphics/annotate.py <raw> <work>` — crops each capture and draws the numbered markers.
3. **Render**: `python guide_graphics/render.py <work> docs/images/guide` — builds the cards, the three illustrations and `social_preview.png` (Python Playwright with Chromium; all network requests are blocked).

The text of every card (titles, explanations, tips) and the marker positions are defined in one place, [`guide_graphics/guide_spec.py`](../../scripts/autotest/guide_graphics/guide_spec.py). The illustrations are HTML in [`guide_graphics/illustrations.html`](../../scripts/autotest/guide_graphics/illustrations.html) with the shared style sheet `style.css`. The README gallery screenshots come from `screenshots.ps1`.

## Licence

© 2026 Aung Ye Mun and contributors. The images are part of MultiCamApp and are covered by its [licence](../../LICENSE.md). Check it before using them outside non-commercial academic, educational or personal work.
