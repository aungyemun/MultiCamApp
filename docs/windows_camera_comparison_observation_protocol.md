# Windows Camera vs MultiCamApp Observation Protocol

**Phase:** Observation Only — No Recording Tests, No Automation
**Status:** SUPERSEDED (2026-07-02) — this checklist template was never filled in; real side-by-side recording tests (not just observation) were later run against shipping VideoEngineV2, comparing actual Windows Camera app recordings against MultiCamApp recordings via ffprobe and extracted frames. See v1.2.24-alpha–v1.2.27-alpha in [CHANGELOG.md](../CHANGELOG.md) and [windows_camera_behavior_study.md](windows_camera_behavior_study.md) Section 10 for the real findings. Kept for reference as a reusable manual-observation procedure if further UI-level (non-recorded) comparison is ever needed.

This protocol provides a simple, safe, observation-only procedure to compare Windows Camera (Microsoft) and MultiCamApp behavior. No validation scripts are run. No automated camera tests are performed. No video files are analyzed.

---

## 1. Observation Conditions

Keep the following conditions constant for all observations in this protocol:

| Condition | Requirement |
|---|---|
| PC | Same PC for all observations |
| Camera | Same physical camera device |
| USB port | Same USB port (do not swap ports between tests) |
| Lighting | Same room lighting |
| Camera position | Same physical position and angle |
| Scene | Same subject/background in frame |
| Room brightness | Same — do not change lights between observations |
| Background apps | No other heavy or camera-using apps running |
| Time of day | Ideally same session to avoid lighting changes |

> **Note:** If conditions change mid-observation, mark that row with an asterisk (*) and note what changed.

---

## 2. Research-Safe Camera Settings (Before Observing Windows Camera)

Before starting any Windows Camera observation, open Windows Settings and attempt to turn OFF the following if they are available:

| Setting | Location | Action | Status |
|---|---|---|---|
| Windows Studio Effects | Settings → Bluetooth & devices → Cameras → [camera name] | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Background blur | Windows Camera app or Camera Settings | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Eye contact | Camera Settings | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Automatic framing | Camera Settings | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Portrait light | Camera Settings | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Creative filters | Windows Camera app | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Digital stabilization | Windows Camera settings (if available) | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| Low-light compensation | Camera Settings (if available) | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |
| HDR / image enhancement | Camera Settings (if available) | Turn OFF | ☐ Off / ☐ Not found / ☐ Unknown |

> **Important:** If a setting cannot be found, mark it as **Unknown**, not Off. Do not assume it is disabled.

---

## 3. What to Observe in Windows Camera

Open the Windows Camera app (Microsoft, from Microsoft Store or Start menu).

Work through each item and record your observation:

**Preview behavior:**
- [ ] Does preview look smooth? (no stuttering, consistent motion)
- [ ] Does preview show visible delay? (lag between real movement and screen)
- [ ] Does exposure change automatically in response to lighting? (screen gets brighter/darker)
- [ ] Does focus hunt? (camera visibly refocuses repeatedly)

**Camera settings and effects:**
- [ ] Are Studio Effects available in the Windows Camera app?
- [ ] Are digital stabilization or image enhancement settings available?
- [ ] Are resolution/FPS settings visible in Windows Camera?

**System resource behavior (open Task Manager → Performance → GPU during observation):**
- [ ] Does Task Manager show GPU 3D activity during preview?
- [ ] Does Task Manager show GPU Video Encode activity during recording?
- [ ] Does CPU usage remain low during preview?

**Camera lifecycle behavior:**
- [ ] Does the camera start quickly? (preview appears within ~2 seconds of opening app)
- [ ] Does the camera stop cleanly when app is closed?
- [ ] If unplugged and replugged, does the camera recover normally?

---

## 4. What to Observe in MultiCamApp (Current Version)

Open MultiCamApp and observe the same camera under the same conditions.

**Preview behavior:**
- [ ] Does preview look less smooth than Windows Camera?
- [ ] Does preview show visible delay compared to Windows Camera?
- [ ] Does exposure change automatically?
- [ ] Does focus hunt?

**Recording behavior:**
- [ ] Does start recording show a visible delay?
- [ ] Does stop recording show a visible delay?

**System resource behavior (Task Manager during observation):**
- [ ] Does CPU usage appear higher than in Windows Camera?
- [ ] Does GPU usage appear lower or different than in Windows Camera?

**Camera lifecycle behavior:**
- [ ] Does camera unplug/replug recover normally in MultiCamApp?
- [ ] Are any warnings shown in the MultiCamApp UI?

---

## 5. Runtime Observation Table

Fill in during observation session:

| Category | Windows Camera observation | MultiCamApp observation | Difference | Notes |
|---|---|---|---|---|
| Preview smoothness | | | | |
| Preview delay | | | | |
| CPU usage | | | | |
| RAM usage | | | | |
| GPU 3D usage | | | | |
| GPU Video Encode usage | | | | |
| Camera startup speed | | | | |
| Camera stop behavior | | | | |
| Exposure stability | | | | |
| Focus stability | | | | |
| Camera settings visibility | | | | |
| Studio Effects availability | | | | |
| Digital stabilization availability | | | | |
| USB unplug / replug behavior | | | | |
| User-facing warnings | | | | |

---

## 6. Interpretation Guide

Use these notes to interpret what you observe:

- **If Windows Camera preview is smoother:** preview rendering or native camera pipeline may be more efficient (GPU rendering likely involved).
- **If GPU 3D rises during Windows Camera preview:** GPU-assisted preview rendering (Direct3D / DXVA2) is likely being used.
- **If GPU Video Encode rises during Windows Camera recording:** OS/hardware encoding (H.264/HEVC via Media Foundation) is likely being used.
- **If CPU is lower in Windows Camera:** native preview/encoding pipeline may offload work to GPU/driver that OpenCV does not.
- **If exposure changes in low light:** shutter speed / exposure adjustment may affect actual delivered FPS — this is scientifically important to flag in future recording.
- **If Studio Effects or digital stabilization are available:** these should remain **OFF by default** for all scientific research recordings, and their status should be logged in session metadata.
- **If focus hunts:** auto-focus may affect frame quality during a recording — manual focus lock should be investigated for scientific sessions.

---

## 7. Final Observation Summary

Fill in after completing the observation session:

| Question | Observation | Confidence | Future action |
|---|---|---|---|
| Is Windows Camera preview smoother? | | | |
| Does Windows Camera appear to use GPU preview? | | | |
| Does Windows Camera appear to use hardware/OS encoding? | | | |
| Are Studio Effects available on this PC? | | | |
| Is digital stabilization available? | | | |
| Are camera controls visible in Windows Settings? | | | |
| Which behavior should MultiCamApp learn from? | | | |
| Which behavior should MultiCamApp avoid for research? | | | |
