# Windows Camera vs MultiCamApp — Manual Observation Template

**Instructions:** Fill this template while observing Windows Camera and MultiCamApp preview side-by-side.
Do NOT record any video. Do NOT run any validation scripts during observation.
Open Task Manager → Performance tab (GPU, CPU, RAM) in a separate window while observing.

**Date of observation:** _______________
**PC:** Alienware x15 R2
**Camera used for comparison:** _______________
**USB port used:** _______________
**Room lighting condition:** _______________

---

## A. Windows Camera Preview Observation

Open the Windows Camera app (Microsoft Store version 2025.2510.2.0).
Before observing, turn OFF any Studio Effects if available (see Section 2 of the protocol).

| Item | Observation | Notes |
|---|---|---|
| Preview smoothness | | e.g. Smooth / Slight stutter / Choppy |
| Preview stutter | | e.g. None / Occasional / Frequent |
| Preview delay / lag | | e.g. None visible / ~0.5s / Noticeable |
| CPU usage during preview | | e.g. ~5% / ~15% (from Task Manager) |
| RAM usage during preview | | e.g. ~200 MB |
| GPU 3D usage during preview | | e.g. ~10% Intel Xe / ~5% RTX 3080 |
| GPU Video Decode usage during preview (if visible) | | e.g. Not visible / ~15% |
| GPU Copy usage during preview (if visible) | | e.g. Not visible / ~3% |
| Camera startup speed | | e.g. Instant (<1s) / ~2s / Slow (>3s) |
| Camera stop behavior | | e.g. Instant / Slight delay |
| Camera light turns off after close | | e.g. Yes immediately / Delayed / No |
| Exposure stability | | e.g. Stable / Adjusts in low light / Hunting |
| Focus stability | | e.g. Stable / Hunts / Fixed |
| Brightness stability | | e.g. Stable / Fluctuates |
| White balance stability | | e.g. Stable / Shifts |
| Windows Studio Effects availability | | e.g. Available in Settings / Not found |
| Background blur availability | | e.g. Available / Not found / Unknown |
| Eye contact availability | | e.g. Available / Not found / Unknown |
| Auto framing availability | | e.g. Available / Not found / Unknown |
| Digital stabilization availability | | e.g. Available / Not found / Unknown |
| Low-light compensation availability | | e.g. Available / Not found / Unknown |
| Resolution / FPS setting visibility | | e.g. Visible (1080p/30fps) / Not visible |
| USB unplug / replug recovery | | e.g. Recovered instantly / Required reopen |
| Any Windows privacy / access warning | | e.g. None / Camera access dialog shown |

**Additional Windows Camera notes:**

```
(free text)
```

---

## B. MultiCamApp Preview Observation

Open MultiCamApp and observe the same camera slot under the same conditions as above.

| Item | Observation | Notes |
|---|---|---|
| Preview smoothness compared with Windows Camera | | e.g. Same / Slightly less smooth / Noticeably less smooth |
| Preview stutter | | e.g. None / Occasional / Frequent |
| Preview delay / lag | | e.g. None visible / ~0.5s / Noticeable |
| CPU usage during preview | | e.g. ~12% / ~25% (from Task Manager) |
| RAM usage during preview | | e.g. ~350 MB |
| GPU 3D usage during preview | | e.g. ~2% / Lower than Windows Camera |
| GPU Video Decode usage during preview (if visible) | | e.g. Not visible |
| GPU Copy usage during preview (if visible) | | e.g. Not visible |
| Camera startup speed | | e.g. ~2s / Slower than Windows Camera |
| Camera stop behavior | | e.g. Instant / Slight delay |
| Camera light turns off after close | | e.g. Yes / Delayed / No |
| Exposure stability | | e.g. Stable / Adjusts / Hunting |
| Focus stability | | e.g. Stable / Hunts / Fixed |
| Brightness stability | | e.g. Stable / Fluctuates |
| White balance stability | | e.g. Stable / Shifts |
| UI warnings shown | | e.g. None / Exposure warning / FPS warning |
| USB unplug / replug recovery | | e.g. Recovered / Required restart |
| App closes cleanly | | e.g. Yes / Slight delay / Requires force close |
| Any remaining process after closing | | e.g. None (check Task Manager) |

**Additional MultiCamApp notes:**

```
(free text)
```

---

## C. Final Observation Summary

Fill after completing both A and B observations.

| Question | Observation | Confidence (High / Medium / Low) | Future action |
|---|---|---|---|
| Is Windows Camera preview smoother? | | | |
| Does Windows Camera appear to use GPU-assisted preview? | | | |
| Does Windows Camera use lower CPU during preview? | | | |
| Are Windows Studio Effects available on this PC? | | | |
| Is digital stabilization available? | | | |
| Is low-light compensation available? | | | |
| Are camera controls visible in Windows Settings? | | | |
| Does Windows Camera close / release camera cleanly? | | | |
| Does MultiCamApp close / release camera cleanly? | | | |
| Which behavior should MultiCamApp learn from? | | | |
| Which behavior should MultiCamApp avoid for research? | | | |

---

## D. Task Manager GPU Engine Reference

When recording GPU usage in Tasks A and B, refer to this guide for which GPU engine to watch in Task Manager:

| Task Manager engine label | What it means | Relevant for |
|---|---|---|
| GPU 3D | General GPU rendering / compute work | Preview rendering, D3D pipeline |
| GPU Video Decode | Hardware video decoding (DXVA2 / D3D11VA) | Compressed camera stream decoding (MJPEG) |
| GPU Video Encode | Hardware video encoding (NVENC / QuickSync) | Recording codec (not applicable to preview-only) |
| GPU Copy | Data transfers between CPU and GPU memory | Frame buffer upload/download |
| GPU Compute | General compute (ML, AI effects) | Studio Effects / NPU workloads |

**How to see individual GPU engines in Task Manager:**
1. Open Task Manager → Performance → GPU
2. Right-click the GPU graph → Change graph to → Select engine(s)
3. This will show individual 3D / Video Decode / Video Encode / Copy engines

---

**Observation complete. Save this file and transfer results to:**
- `docs/windows_camera_behavior_study.md` — Sections 7, 8, and 9
- `docs/windows_camera_comparison_observation_protocol.md` — Section 5 (Runtime Observation Table) and Section 7 (Final Summary)
