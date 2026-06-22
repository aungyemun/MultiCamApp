# Changelog

All notable changes to MultiCamApp will be documented in this file.

## [v1.1.0] - 2026-06-23

### Added

* Original Capture Mode for preserving real camera frames only
* Per-frame Timestamp CSV files for timing-sensitive analysis
* Scientific timing confidence reporting
* Privacy-safe metadata summaries
* Video Verification Simple View and Detailed View
* Offline video/container metadata inspection using bundled `ffprobe.exe`
* Synchronized start gate for active 2–4 camera sessions
* Scientific exposure defaults
* Clearer camera-control metadata wording
* Offline Windows installer for Windows 10/11
* Non-commercial academic and research license
* Third-party notices for bundled runtime and build components
* Citation metadata through `CITATION.cff`

### Changed

* Updated recording logic to avoid duplicate-frame and placeholder-frame insertion
* Updated verification wording to distinguish Real Capture FPS from MP4/container Playback FPS
* Updated metadata and report wording for research readability
* Updated documentation for installation, verification, hardware diagnostics, security, licensing, and release packaging
* Updated GitHub repository structure for public source release preparation

### Notes

* Timestamp CSV is the recommended timing source for timing-sensitive analysis.
* MP4 playback FPS and container duration are playback/container metadata, not the primary scientific timing source.
* Frame counts may differ between cameras when devices deliver real frames at slightly different measured FPS.
* Recordings should be verified using the in-app Video Verification page after capture.
