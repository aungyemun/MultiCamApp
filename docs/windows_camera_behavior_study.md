# Windows Camera Behavior Study for MultiCamApp

**Phase:** Observation and Documentation Only
**Status:** SUPERSEDED (2026-07-02) — the "Windows Native Camera Engine Core Update" this study planned for was implemented as **VideoEngineV2** (`capture/video_engine_v2/`), shipped starting v1.1.7 and made the default/exclusive engine as of v1.2.22-alpha (V3/V3B experimental alternatives removed). Sections 6–10 below were never filled in during the planning phase, but real, empirical Windows Camera vs MultiCamApp comparisons were later run directly against shipping VideoEngineV2 code (side-by-side frame extraction, ffprobe stream comparison, real recordings) — see the v1.2.24-alpha through v1.2.27-alpha entries in [CHANGELOG.md](../CHANGELOG.md) for the actual findings, since they supersede the open questions in this document. Kept for historical/design-rationale reference.

**Update (2026-07-05, v1.2.65–v1.2.67):** the "investigate `IMFSinkWriter` directly" follow-up this study's Section 10 flagged as the path to real VUI color tagging was in fact carried out — `MediaFoundationEncoderService` was rewritten around a raw `IMFSinkWriter` pipeline (v1.2.65, also fixing forced-CFR muxing into real per-frame VFR timestamps), and BT.709 color primaries/transfer/matrix + limited-range tagging shipped in v1.2.66, exposed in metadata/verification in v1.2.67. `color_primaries`/`color_transfer`/`color_space` no longer read `unknown`.
**Working directory:** `D:\Software testing\MultiCamApp_Version_Develop`

This document studies how Windows Camera and Windows camera settings behave so that MultiCamApp can later be upgraded with a safer, more reliable Windows-native camera engine. This phase is for observation and documentation only. No current MultiCamApp code is changed in this phase.

---

## 1. Scope and Rules

- This study does **not** reverse-engineer Microsoft Camera.
- This study does **not** copy Microsoft Camera internals or binaries.
- This study does **not** decompile, patch, or inspect private Microsoft Camera code.
- This study only uses public documentation, public Windows APIs, system diagnostics, and user-facing observations.
- Current MultiCamApp recording, preview, UI, metadata, verification, and camera pipeline code is **not changed** in this phase.
- No camera recording tests are automated in this phase.
- No ffprobe or video validation helper is created in this phase.
- No camera FPS validation commands are added in this phase.
- The final result of this study will support a later implementation plan called:

  > **"MultiCamApp Windows Native Camera Engine Core Update"**

Sources used in this study:

| Source type | Allowed |
|---|---|
| Public Microsoft documentation (learn.microsoft.com) | Yes |
| Public Windows Runtime (WinRT) APIs | Yes |
| Public Media Foundation APIs | Yes |
| Observable user-facing Windows Camera behavior | Yes |
| Safe system diagnostics (Get-PnpDevice, dxdiag, etc.) | Yes |
| User-created notes and observations | Yes |
| Microsoft Camera app binaries / private internals | **No** |
| Decompilation or reverse engineering | **No** |
| Proprietary third-party camera tools | **No** |

---

## 2. Why This Study Is Needed

Windows Camera may appear smoother and more stable than MultiCamApp's current implementation because it likely uses:

- **Windows-native camera APIs** (MediaCapture / Media Foundation) instead of OpenCV DirectShow
- **Native mode negotiation** at the driver/OS level
- **OS preview pipeline** with optimized frame delivery
- **GPU-assisted rendering** (Direct3D / DXVA2 / D3D11) for preview
- **OS encoder paths** (hardware H.264/HEVC via Media Foundation Sink Writer)
- **Tighter driver/OS integration** for USB camera device lifecycle

MultiCamApp's scientific requirements mean it cannot blindly adopt the same approach, because the app must remain stricter than Windows Camera in several areas:

| Area | Windows Camera | MultiCamApp requirement |
|---|---|---|
| FPS reporting | May show nominal FPS | Must measure actual delivered FPS |
| Fast-playback risk | Not checked | Must detect and warn |
| Exposure verification | Not confirmed | Must attempt readback and warn if unconfirmed |
| Image effects | May apply studio effects | Must keep OFF by default for research |
| Container duration | Not cross-validated | Must match wall-clock duration |
| Timestamp accuracy | Not verified externally | Must produce timestamp CSV per frame |
| Frame drop detection | Not surfaced | Must report and warn |

MultiCamApp must learn from Windows Camera's observable smoothness and reliability, while remaining more transparent and scientifically accountable.

---

## 3. Public Microsoft API Research

### A. Windows.Media.Capture.MediaCapture

**What it is:**
A Windows Runtime (WinRT) camera capture API available in UWP-style and desktop-bridged Windows applications. It is the high-level camera API used in Microsoft's own Windows Camera app.

**What it may do:**
- Initialize and open camera devices by device ID
- Provide preview streams to a UI element (CaptureElement or compatible surface)
- Capture photos
- Record video to files (MP4/MKV with native codecs)
- Interact with camera controls through VideoDeviceController
- Support shared or exclusive camera access modes

**How it may help MultiCamApp later:**
- Replace the current OpenCV DirectShow capture pipeline with a Windows-native pipeline
- Enable better mode negotiation and hardware codec access
- Potentially improve USB camera start stability and reconnection

**Relates to:** preview, recording, camera initialization, Windows camera pipeline access

**Limitations for scientific recording:**
- App architecture integration is required (requires WinRT interop or migration from pure WPF)
- Multi-camera simultaneous support must be validated with real USB hubs and webcams
- Scientific frame timestamping is not built-in; external frame timing must still be measured
- Frame drop detection is not automatic

**Notes/TODOs for future implementation:**
- Investigate `MediaCaptureInitializationSettings.SharingMode` for multi-camera support
- Confirm device enumeration compatibility with USB webcams (vs. built-in cameras)
- Test whether `MediaCapture` can open 4 USB cameras simultaneously on this PC

---

### B. Windows.Media.Capture.Frames.MediaFrameReader

**What it is:**
A WinRT API for reading individual frames from a camera frame source. Provides event-driven or polling-based access to frames as they arrive from the camera.

**What it may do:**
- Deliver camera frames one-by-one to the application
- Provide system timestamp per frame (`MediaFrameReference.SystemRelativeTime`)
- Expose raw video or bitmap frames for processing or saving
- Enable custom recording and preview paths at the frame level

**How it may help MultiCamApp later:**
- Enable frame-by-frame access for building a scientific timestamp CSV
- Allow raw/unprocessed frame inspection before encoding
- Support custom recording pipeline with verified frame counts
- Possible replacement for the current OpenCV frame grab loop

**Relates to:** frame-by-frame access, scientific timestamping, raw frame inspection, custom recording/preview

**Limitations for scientific recording:**
- Frame delivery timing must be validated on real hardware (delivery is not guaranteed to match camera sensor cadence exactly)
- Performance under 4-camera simultaneous load must be tested later
- Requires careful threading and memory management (frames must be closed promptly)
- May require color format conversion (NV12 → BGR/RGB for processing)

**Notes/TODOs for future implementation:**
- Review `MediaFrameReader.AcquisitionMode` (Buffered vs. Realtime) and its impact on frame drops
- Evaluate whether `SystemRelativeTime` is accurate enough for scientific inter-camera timing

---

### C. Windows.Media.Capture.Frames.MediaFrameSourceGroup

**What it is:**
A WinRT API for discovering available camera frame source groups on the system. A source group can contain multiple frame sources (color, depth, IR, etc.) from a single camera device.

**What it may do:**
- Enumerate all camera frame source groups on the PC
- Identify available frame source types per device (color, depth, infrared)
- Allow selection of specific sources within a multi-stream camera

**How it may help MultiCamApp later:**
- Improve camera enumeration beyond simple device name matching
- Support cameras with multiple streams (e.g., Intel RealSense, Azure Kinect if relevant)
- Provide a more reliable device discovery path than the current DirectShow enumeration

**Relates to:** camera device enumeration, frame source discovery, advanced camera support

**Limitations for scientific recording:**
- External USB webcam support (particularly older UVC webcams) must be confirmed on real devices
- May expose complex source group structures not relevant for simple USB webcams
- Requires testing to confirm all target cameras appear in `MediaFrameSourceGroup.FindAllAsync()`

**Notes/TODOs for future implementation:**
- Compare `MediaFrameSourceGroup.FindAllAsync()` results with the current `DirectShowVideoDeviceEnumerator` results for the same cameras

---

### D. Windows.Media.Devices.VideoDeviceController

**What it is:**
A WinRT API providing programmatic access to camera hardware controls. Accessible through a `MediaCapture` instance.

**What it may do:**
- Control and read back exposure (auto/manual, value, compensation)
- Control and read back focus (auto/manual, value)
- Control brightness, contrast, white balance, saturation
- Control zoom, pan, tilt (if camera supports them)
- Query supported control ranges and defaults

**How it may help MultiCamApp later:**
- Replace or supplement the current OpenCV-based camera control attempts
- Enable more reliable apply + readback confirmation of exposure and focus settings
- Allow explicit disabling of auto exposure, auto focus, and low-light compensation per camera

**Relates to:** camera controls (exposure, focus, brightness, contrast, white balance, zoom, pan/tilt)

**Limitations for scientific recording:**
- Not all cameras or drivers support every control — unsupported controls must be detected and reported
- Apply/readback validation is still required (setting a value does not guarantee the camera applies it)
- Some controls may only work in specific camera modes
- Readback unreliability must be surfaced in metadata when it occurs

**Notes/TODOs for future implementation:**
- Build an apply + readback + warn pattern using `VideoDeviceController` to replace current `CameraManager` exposure workarounds
- Map current `CameraProfile.cs` fields to `VideoDeviceController` equivalents

---

### E. Windows.Media.MediaProperties.MediaEncodingProfile

**What it is:**
A WinRT API for defining video/audio encoding profiles, including container format, codec, resolution, and frame rate, used with `MediaCapture` recording.

**What it may do:**
- Define H.264 or H.265 (HEVC) encoding profiles
- Specify MP4 or MKV container
- Request a target FPS and resolution for recording
- Use OS or hardware-accelerated encoding if available

**How it may help MultiCamApp later:**
- Provide a structured way to select recording codec, resolution, and FPS profile
- Enable native OS encoding path (hardware H.264/HEVC if GPU/driver supports it)
- Replace the current OpenCV MJPEG/AVI recording path

**Relates to:** recording profiles, codec selection, resolution/FPS selection, OS encoding paths

**Limitations for scientific recording:**
- Selected FPS in the profile is a request, not a guarantee — actual camera delivery FPS must still be measured
- Container timestamps and duration must be cross-validated (fast-playback risk exists even with native APIs)
- OS encoding adds a processing step that must not hide dropped frames
- Scientific timestamp CSV is still required regardless of encoding profile

**Notes/TODOs for future implementation:**
- Compare H.264 container duration and frame count against wall-clock duration in a future validation step
- Evaluate whether hardware encoding (GPU/NPU) is available on this PC

---

### F. Media Foundation Source Reader

**What it is:**
A lower-level Win32/COM API (`IMFSourceReader`) for reading media samples from cameras, files, or other media sources. Part of the Windows Media Foundation framework.

**What it may do:**
- Open camera devices as media sources using device symbolic links
- Enumerate and select media types (resolution, FPS, pixel format: MJPEG, YUY2, NV12, etc.)
- Read frames as `IMFSample` objects with presentation timestamps
- Provide direct access to camera frame data before any encoding

**How it may help MultiCamApp later:**
- Potentially better mode negotiation than OpenCV DirectShow (can enumerate and select exact media types)
- Lower-level access to camera frame timestamps
- Alternative to `MediaFrameReader` for desktop Win32 integration without WinRT
- Access to MJPEG, YUY2, NV12 raw pixel formats

**Relates to:** native frame capture, mode negotiation, media type enumeration, pixel format access

**Limitations for scientific recording:**
- Implementation complexity is higher than WinRT APIs
- Requires handling of COM threading, presentation timestamps, buffer management, and color conversion
- Timestamp accuracy from `IMFSample` must be validated against wall clock
- Requires testing with all target USB webcam models

**Notes/TODOs for future implementation:**
- Consider as fallback or alternative path if WinRT `MediaFrameReader` has multi-camera limitations
- Evaluate `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK` for consistent device identification

---

### G. Media Foundation Sink Writer

**What it is:**
A lower-level Win32/COM API (`IMFSinkWriter`) for encoding and writing media to files. Part of Windows Media Foundation. Provides access to hardware-accelerated H.264/HEVC encoding if available.

**What it may do:**
- Encode raw video frames to H.264 or HEVC
- Write MP4 containers with correct timing
- Use hardware encoder (GPU/NPU via DXVA2 or D3D11VA) when available
- Write media samples with explicit presentation timestamps

**How it may help MultiCamApp later:**
- Provide OS/hardware encoding path as alternative to OpenCV MJPEG/AVI
- Enable hardware-accelerated recording with lower CPU cost
- Support explicit per-frame presentation timestamps for scientific accuracy

**Relates to:** video encoding, MP4 writing, OS encoder path, hardware encoder

**Limitations for scientific recording:**
- Must not hide dropped frames — frame count in container must match frames written
- Scientific timestamp CSV is still required in addition to container timestamps
- Container duration must be verified against wall-clock duration after recording
- Frame timing and presentation timestamps must not be assumed correct without validation

**Notes/TODOs for future implementation:**
- Do not replace current OpenCV recording path until Sink Writer path has been fully validated
- Verify hardware encoder availability on this PC using `MFTEnumEx` or DirectX/GPU report

---

### H. Direct3D / Direct2D Preview Rendering

**What it is:**
Windows GPU rendering APIs. Direct3D 11 can be used for camera preview rendering via texture surfaces, and Direct2D can be used for UI overlays and compositing.

**What it may do:**
- Render camera preview frames to a GPU texture surface (lower CPU, higher GPU 3D)
- Support latest-frame-only preview (skip old frames under load, reducing latency)
- Enable smooth overlay rendering of camera slot labels, status, and warnings
- Allow compositing of multiple camera previews on a single GPU surface

**How it may help MultiCamApp later:**
- Smoother preview performance for 4 simultaneous cameras
- Lower CPU usage for preview, freeing resources for recording
- More responsive latest-frame preview (no frame queue backlog)

**Relates to:** preview rendering, GPU rendering, UI overlay, multi-camera compositing

**Limitations for scientific recording:**
- Preview smoothness does not prove recording correctness
- GPU preview path must remain fully separate from scientific recording and frame verification
- WPF-based fallback preview must remain available if GPU surface rendering fails
- GPU preview rendering must never delay or affect recording pipeline timing

**Notes/TODOs for future implementation:**
- Measure GPU 3D usage with current WPF preview vs. a future D3D preview surface
- Consider `SwapChainPanel` or `D3DImage` WPF interop for GPU preview integration

---

### I. Windows Studio Effects / Camera Settings

**What it is:**
Windows 11 OS-level camera effects and settings, which may include background blur, eye contact correction, automatic framing, portrait light, creative filters, and voice focus. Some effects require NPU (Neural Processing Unit) or dedicated AI hardware. Available through Windows Settings → Bluetooth & devices → Cameras.

**What it may do:**
- Apply background blur to the camera feed at the OS level
- Apply eye contact simulation (gaze correction)
- Apply automatic framing (crop/zoom to keep subject centered)
- Apply portrait lighting enhancement
- Apply creative filters (e.g., illustrated style)
- Apply noise reduction or voice focus for audio (if available)

**How it may help MultiCamApp later:**
- Optional non-scientific preview/recording enhancement feature for non-research use cases
- Detection and reporting of active effects status in metadata

**Scientific limitation:**
- Effects modify the original camera image before the app receives frames — this is scientifically unacceptable by default
- **Default must be OFF for all scientific recording sessions**
- If Studio Effects status cannot be verified programmatically, metadata must say `StudioEffects: Unknown`
- If effects are enabled and detected, a scientific warning must be shown and logged
- Effects cannot be silently applied without user awareness

**Notes/TODOs for future implementation:**
- Investigate `Windows.Media.Effects` and Windows Settings Camera page for programmatic status detection
- Add a pre-recording Studio Effects status check to the scientific preflight checklist

---

### J. CameraCaptureUI

**What it is:**
A simple WinRT API (`Windows.Media.Capture.CameraCaptureUI`) that displays a system-provided camera capture dialog, allowing the user to take a photo or record a short video through a built-in OS UI.

**Potential use:**
- Reference only for understanding the concept of OS-mediated capture

**Limitations:**
- Not suitable as a MultiCamApp core engine
- No multi-camera support
- No programmatic timestamping or frame verification
- No access to raw frames or scientific metadata
- Not appropriate for simultaneous multi-camera scientific recording

**Notes:**
Included for completeness only. Not considered for any future implementation.

---

## 4. Windows Camera Behavior Hypotheses

| Observed / expected Windows Camera behavior | Possible technical reason | MultiCamApp learning point |
|---|---|---|
| Smoother preview | Native preview path / GPU rendering (Direct3D) | Separate preview rendering from recording pipeline |
| Better FPS stability | Confirmed native camera mode negotiation | Add capability scanner to confirm actual mode before recording |
| Lower CPU preview | Direct3D / OS pipeline handles preview offload | Add GPU-assisted preview path later |
| Better recording codec | OS encoder (Media Foundation Sink Writer / hardware H.264) | Evaluate Media Foundation Sink Writer as future recording path |
| Better exposure behavior | VideoDeviceController + driver integration | Implement apply/readback validation with explicit warnings |
| Studio Effects support | Windows Settings camera effects / NPU | Keep OFF by default; detect and warn if active |
| Plug-and-play USB camera handling | Windows camera stack (UVC / PnP) | Improve device enumeration, reconnection, and slot recovery |

---

## 5. Scientific Recording Requirements

MultiCamApp **must be stricter** than Windows Camera. The following scientific principles must be preserved in any future engine upgrade:

- Raw / unaltered camera feed is preferred by default
- Image effects (Studio Effects, background blur, eye contact, etc.) must be **OFF by default**
- Digital stabilization must be **OFF by default**
- Low-light compensation must be **OFF by default** unless explicitly enabled and documented
- Actual FPS must be **measured**, not assumed from the selected profile
- Selected FPS must not be trusted blindly — camera may not deliver the requested rate
- Exposure setting must be **confirmed by readback** when possible; warn if readback is unreliable
- Container duration must **match wall-clock duration** — fast-playback risk must be detected
- Timestamp CSV must **match the number of frames written**
- Inactive camera slots must **not affect multi-camera frame verification**
- All warnings must be surfaced in metadata and UI — nothing hidden

---

## 6. What Must Be Learned From This Study

- [ ] Which Windows camera APIs are most relevant for MultiCamApp *(documented in Section 3 — pending manual decision)*
- [ ] Which camera settings are user-visible in Windows Settings *(requires manual observation)*
- [ ] Whether Studio Effects are available on this PC *(not found in Appx scan — requires manual check in Windows Settings)*
- [ ] Whether digital stabilization exists in Windows camera settings on this PC *(requires manual observation)*
- [ ] Whether Windows Camera appears to use smoother preview behavior *(requires manual observation — use `diagnostics/windows_camera_manual_observation_template.md`)*
- [ ] Whether Task Manager shows GPU 3D activity during Windows Camera preview *(requires manual observation)*
- [ ] Whether Task Manager shows GPU Video Encode activity during Windows Camera recording *(requires manual observation)*
- [ ] Whether Windows Camera settings expose resolution/FPS options for the user's cameras *(requires manual observation)*
- [x] Whether camera privacy/access settings in Windows could affect MultiCamApp — **System-level Allow confirmed. No block.**
- [x] Whether USB camera devices appear as Camera or Imaging devices — **j5 ×4, OBSBOT ×2, C922, USB Live = Camera class. Canon/WSD = Imaging class.**
- [x] Whether GPU and DirectX capabilities are sufficient for future GPU-assisted preview — **Yes. NVIDIA RTX 3080, DirectX 12 feature level 12_2, HAGS enabled.**

---

## 7. Local Environment Scan Summary

**Status: Collected — 2026-06-23**
**Source:** `diagnostics/windows_camera_environment_report.txt` + `diagnostics/dxdiag_multicam.txt`
**Full details:** `diagnostics/windows_camera_study_summary.md`

| Item | Value |
|---|---|
| Windows version | Microsoft Windows 11 Home, Build 26200 (10.0.26200) |
| OS architecture | 64-bit |
| System model | Alienware x15 R2 |
| CPU | 12th Gen Intel Core i9-12900H — 14 cores / 20 logical processors @ 2.5 GHz |
| Total RAM (GB) | 31.7 GB |
| GPU adapter(s) | Intel Iris Xe Graphics · NVIDIA GeForce RTX 3080 Laptop GPU |
| GPU driver version(s) | Intel 32.0.101.7084 (2026-01-15) · NVIDIA 32.0.15.9608 (2026-03-31) |
| DirectX version | DirectX 12 — NVIDIA feature level 12_2; Intel feature level 12_1 |
| NVIDIA driver model | WDDM 3.2 |
| Camera devices (PnP) | j5 Webcam JVU250 ×4 · OBSBOT Meet SE StreamCamera ×2 · USB Live Camera ×1 · C922 Pro Stream Webcam ×1 · Integrated Webcam (OK) · Integrated IR Webcam (OK) |
| Imaging devices (PnP) | Canon MF720C Series (OK) · WSD Scan Device |
| USB controllers / hubs | Intel USB 3.1 + USB 3.2 host controllers · USB4 Root Router · Anker USB4 hub · Generic SuperSpeed hub |
| USB anomalies | 1× Port Reset Failed · 2× Device Descriptor Request Failed |
| Installed Camera-related Appx packages | Microsoft.WindowsCamera 2025.2510.2.0 |
| Studio Effects-related Appx packages | None detected via Get-AppxPackage — may be OS-integrated |
| Active power plan | High performance (GUID 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c) |
| Hardware-accelerated GPU scheduling | Enabled on NVIDIA RTX 3080 (dxdiag: DriverSupportState:Stable Enabled:True) |
| Camera privacy (system level) | Allow |

### Key Findings from Environment Scan

- **NVIDIA RTX 3080 Laptop GPU** with WDDM 3.2, DirectX 12 (feature level 12_2), and HAGS enabled — GPU-assisted preview and hardware video encode (NVENC) are technically available on this PC
- **j5 Webcam JVU250 appears 4× in PnP** — confirms the real 4-camera USB capture card setup; explains why `DuplicateUsbCapturePolicy` is critical
- **Complex USB hub topology** (USB4 Anker hub + Intel 3.1/3.2 host controllers) — inter-hub latency is a real consideration for synchronized multi-camera timing
- **Windows Camera app installed** (v2025.2510.2.0) — available for manual comparison observation
- **Studio Effects**: not found as separate Appx package; requires manual confirmation in Windows Settings → Bluetooth & devices → Cameras
- **Camera privacy**: system-level Allow — no OS-level block affecting MultiCamApp

---

## 8. Windows Camera Observation Summary

**Status: Not collected yet.**

After manually observing Windows Camera (Microsoft Store app), fill in this section:

| Observation | Notes |
|---|---|
| Preview smoothness | |
| Preview delay (visible lag) | |
| Available Windows Camera settings (visible in app) | |
| Visible Studio Effects settings | |
| Visible digital stabilization setting | |
| Visible resolution / FPS settings | |
| CPU behavior (Task Manager during preview) | |
| CPU behavior (Task Manager during recording) | |
| GPU 3D behavior (Task Manager during preview) | |
| GPU Video Encode behavior (Task Manager during recording) | |
| Exposure / focus stability (visual) | |
| Any limitations or unknowns | |

---

## 9. Findings and Decisions

**Status: Pending.**

After completing the environment scan and observation phase, use this section to decide:

- Which backend should be investigated first (MediaCapture vs. Media Foundation Source Reader)
- Which camera controls are essential for scientific validity
- Which effects must remain OFF by default and how to detect them
- Which diagnostics are required before recording in the new engine
- Which items belong in v1.1.1, v1.2.0, v1.3.0, and later releases

---

## 10. Final Learning Summary

**Filled in 2026-07-02 from real side-by-side testing against shipping VideoEngineV2 (v1.2.24-alpha–v1.2.27-alpha), not the original observation-phase plan. See CHANGELOG.md for full detail per version.**

| Question | Finding | Evidence | Future action |
|---|---|---|---|
| What does Windows Camera appear to do better? | Never force-disables ISP auto-adjustments (auto-exposure/LLC/etc. run untouched); tags H.264 output with explicit `color_primaries=bt709`/`color_space=bt470bg`; records an AAC audio track | Real recordings, ffprobe comparison, v1.2.24–v1.2.27 | Color tagging attempted (v1.2.27) via `VideoEncodingProperties.Properties[MF_MT_*]`, confirmed non-functional via `PrepareLowLagRecordToStorageFileAsync` — see `feedback_mf_color_tagging_limitation` memory note. Audio not implemented (out of scope, video-only tool by design). |
| What does MultiCamApp currently lack? | Explicit color primaries/matrix VUI tags on H.264 output; audio recording | ffprobe: `color_space=unknown`/`color_primaries=unknown` vs Windows Camera's `bt470bg`/`bt709` | Would require replacing `MediaFoundationEncoderService`'s `LowLagMediaRecording` convenience API with a custom Media Foundation Sink Writer pipeline — not attempted, low priority (no visible playback difference; `color_range=pc` already matches) |
| Which feature is essential for scientific validity? | Disabling low-light compensation only when exposure is also controllable (`ExposureControl.Supported`) — confirmed via real hardware that disabling LLC unconditionally darkens footage with zero reproducibility benefit when exposure can't be locked anyway | v1.2.25-alpha fix, confirmed via ffprobe/frame extraction on OBSBOT Meet SE | Applied — `CameraControlManagerV2.ApplyResearchDefaultsInternalAsync` now gates LLC-disable on exposure support |
| Which feature is optional / cosmetic? | Color primaries/matrix container tagging (video looks identical in every mainstream player either way) | ffprobe comparison, v1.2.27-alpha | Deprioritized |
| Which effects must remain OFF by default? | Confirmed still correct: auto-focus, auto-exposure (when controllable), low-light compensation (when exposure controllable), optical stabilization | `VideoEngineSettings.cs` defaults, unchanged this session | No action needed |
| Which Windows API should be investigated first? | `MediaCapture` + `LowLagMediaRecording` (already the shipping choice) — confirmed adequate for bitrate/codec/resolution/fps parity with Windows Camera; confirmed **inadequate** for VUI color-tag control | v1.2.20–v1.2.30-alpha real-world testing | If VUI tagging is ever required, investigate `IMFSinkWriter` directly |
| Which encoder path should be studied first? | `MediaFoundationEncoderService` (`LowLagMediaRecording`) — bitrate parity achieved (v1.2.23-alpha: `WindowsCameraLike` 18 Mbps profile) | Real recordings, ffprobe bitrate comparison | None — parity achieved for the properties this API can control |
| Which preview path should be studied first? | D3D11 swap-chain GPU preview with WPF `WriteableBitmap` software fallback — both already implemented; startup transient investigated 2026-07-02 (see conversation/CHANGELOG for the Start Preview glitch investigation) | `D3D11SwapChainHost.cs`, `Direct3DPreviewRenderer.cs` | No code change made — candidate causes identified (UI surface swap reflow, 2×2-placeholder-to-real-resolution swap chain resize, deliberate 500ms exposure-convergence settle) but each carries real regression risk to already-tuned startup-latency code; not changed without hardware verification |
