# Real-camera regression harness (developer tool, not shipped)

These scripts drive the real MultiCamApp UI through Windows UI Automation, record with real cameras, and check the output files independently with FFmpeg/ffprobe and Python. Every child process runs with a hard time limit (`Invoke-Timed` in `lib.ps1`), so a hung app or tool cannot block a run.

## Requirements

* A Release build of the app (`env\activate.ps1`, then `dotnet build source\MultiCamApp\MultiCamApp.sln -c Release -p:Platform=x64`).
* The repository's bundled `tools\python\python.exe` (standard library only) and `runtime\ffmpeg\ffmpeg.exe` / `ffprobe.exe`.
* Connected cameras. The app records to its default output folder (your *Videos* folder).

All paths are relative to the repository. Override them with these environment variables:

| Variable | Default |
|---|---|
| `MCA_EXE` | Release `MultiCamApp.exe` under `source\` |
| `MCA_PYTHON` | `tools\python\python.exe` |
| `MCA_FFMPEG_DIR` | `runtime\ffmpeg` |
| `MCA_RUNS_DIR` | `data\temp\autotest\runs` (gitignored) |
| `MCA_VIDEOS_DIR` | the Windows *Videos* folder |

## Entry point

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autotest\regress.ps1 -Seconds 30 `
  -C '<camera 1 name>' -B '<camera 2 name>' -J1 '<camera 3 name>' -J2 '<camera 4 name>' > data\temp\autotest\runs\regress.log
```

Camera names are the Windows device names shown in the app's camera list. The run performs:

1. A build and the unit tests (aborts on failure).
2. An encoder warm-up check: two starts in one app session.
3. Layouts 1–4. Each one records through the UI and then checks:
   * frame count, resolution and FPS;
   * that CSV rows equal video frames;
   * duplicate, frozen and black frames, and timing gaps;
   * bitrate and sharpness;
   * the inter-camera start spread;
   * the visual stability audit (light-flicker bands, flashes, noise);
   * metadata readbacks.
4. A sudden-close test: the app is killed mid-recording, then relaunched to check recovery.
5. UI page tests: Video Verification, Hardware page and the Japanese UI.

The Video Verification page test scans the whole default output folder. Sessions from the sudden-close test have no metadata, so that scan reports an overall FAIL by design (see `docs/user_guide/video_verification.md`).

## Other scripts

| Script | Purpose |
|---|---|
| `run_rec.ps1` | One recording through the UI (`-Layout`, `-Cam1..4`, `-Res`, `-Fps`, `-Seconds`, `-Flicker`, `-Quality`, `-Lock`) |
| `crash_test.ps1` | Kill the app while recording and inspect what survives |
| `restart_test.ps1` | Repeated start/stop in one session (start latency) |
| `test_exposure.ps1`, `test_focus.ps1` | Manual exposure / focus through the UI, with camera readback |
| `ui_tests.ps1` | UI page tests without recording |
| `ui_tour.ps1` | Screenshots of every page and the About dialog in English and Japanese, page-switch timing, and the app's UI-freeze log lines |
| `rec_lang.ps1 -Lang en\|ja` | One recording with the UI in the given language, to check that the metadata text is in that language |
| `analyze.py <session> <w> <h> <fps> [--quality]` | Independent per-file checks (ffprobe, framemd5, CSV) |
| `visual_audit.py <file.mp4> [--seconds N] [--kymo]` | Independent light-flicker band / flash / noise audit |
| `va_batch.ps1`, `summ_runs.py` | Batch helpers and summaries |
| `AuditCheck\` | CLI that runs the app's own `VisualStabilityAuditService` (used to cross-check `visual_audit.py`) |
| `FormatProbe\` | Low-level camera probe: modes, frame timing, UVC exposure/focus/anti-flicker. Exposure, anti-flicker and the autofocus test restore the camera's settings afterwards. `aftest <value>` deliberately moves the lens; `reset` returns the camera to auto |

Results go to `MCA_RUNS_DIR`. `regress.log` replaces the repository path with `<root>`. Other run files (JSON, screenshots) can contain local folder paths and camera names, so review them before sharing.
