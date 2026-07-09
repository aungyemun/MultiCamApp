ffprobe.exe — bundled for in-app Video Verification (offline, fast metadata-only checks).
ffmpeg.exe  — bundled for the on-demand Deep Verify per-frame MD5 duplicate-frame check
              on the Video Verification page (opt-in only, never runs automatically;
              see DeepVerifyService.cs).

Both are shipped to end users. The installer's final validation step fails the install
if either file is missing (see MultiCamApp.iss).

Installed location (next to MultiCamApp.exe):
  runtime\ffmpeg\ffprobe.exe
  runtime\ffmpeg\ffmpeg.exe

Populated by: scripts\download_vendor_tools.ps1
Staged into dist\ by: scripts\build\stage_dist_runtime.ps1
