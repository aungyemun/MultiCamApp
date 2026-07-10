# Windows Camera Environment Study Summary

**Generated from:** `diagnostics/windows_camera_environment_report.txt` + `diagnostics/dxdiag_multicam.txt`
**Scan timestamp:** 2026-06-23 15:36:35
**Machine:** Alienware x15 R2

---

## Windows Version

| Field | Value |
|---|---|
| Product name | Microsoft Windows 11 Home |
| Build number | 26200 |
| Full version | 10.0.26200 (64-bit) |
| DirectX version | DirectX 12 |

---

## CPU

| Field | Value |
|---|---|
| Name | 12th Gen Intel Core i9-12900H |
| Physical cores | 14 |
| Logical processors | 20 |
| Max clock speed | 2,500 MHz |

**MultiCamApp note:** 20 logical processors provides ample headroom for simultaneous 4-camera recording threads. CPU is not the bottleneck for the current OpenCV pipeline under normal conditions.

---

## RAM

| Field | Value |
|---|---|
| Total installed | 31.7 GB (reported by OS) |
| Available OS memory | 32,440 MB (from dxdiag) |

**MultiCamApp note:** RAM is not a constraint for multi-camera frame buffering.

---

## GPU Adapters

| Name | Dedicated VRAM | Driver Version | Driver Date | Feature Levels | HW Scheduling |
|---|---|---|---|---|---|
| Intel Iris Xe Graphics | 128 MB dedicated / 16,219 MB shared | 32.0.101.7084 | 2026-01-15 | D3D12 (12_1 max) | Not confirmed from registry |
| NVIDIA GeForce RTX 3080 Laptop GPU | 16,175 MB dedicated | 32.0.15.9608 | 2026-03-31 | D3D12 (12_2 max) | **Enabled (Stable)** |

**Key findings:**
- NVIDIA RTX 3080 Laptop GPU is present with **Hardware Scheduling enabled** (confirmed from dxdiag: `DriverSupportState:Stable Enabled:True`)
- Both GPU and Intel Xe support DirectX 12 / WDDM 3.2 — GPU-assisted preview is technically feasible
- NVIDIA driver model: WDDM 3.2
- NVIDIA adapter attributes include: `D3D12_GRAPHICS`, `D3D12_GENERIC_ML`, `D3D12_GENERIC_MEDIA` — hardware video encode path is likely available
- Registry key `HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` was not readable from this scan context, but dxdiag confirms HAGS is **Enabled** on the NVIDIA GPU

**MultiCamApp implication:** A future Direct3D/GPU preview path would run on the NVIDIA RTX 3080. Hardware H.264/HEVC encoding via NVENC is likely available and should be evaluated in the engine upgrade phase.

---

## Camera Devices Detected (PnP Class: Camera)

| Device name | PnP Status | Notes |
|---|---|---|
| j5 Webcam JVU250 | Unknown | Appears 4× (likely multi-instance across USB hubs) |
| OBSBOT Meet SE StreamCamera | Unknown | Appears 2× (likely multi-instance across USB hubs) |
| USB Live Camera | Unknown | 1 instance |
| C922 Pro Stream Webcam (Logitech) | Unknown | 1 instance |
| Integrated Webcam | **OK** | Built-in laptop webcam (Microdia VID_0C45) |
| Integrated IR Webcam | **OK** | Built-in IR webcam (same device, different interface) |

**Key findings:**
- **6 distinct camera entries** are currently visible (10 total including duplicates across USB hubs)
- Most external cameras show PnP status `Unknown` — this is expected when cameras are **not physically connected** at scan time (the OS retains historical entries)
- Only the built-in integrated webcams show `OK` status, confirming they are currently connected
- j5 Webcam JVU250 appears 4 times — likely 4 identical USB capture cards/adapters; confirms multi-camera setup is real
- OBSBOT Meet SE and C922 Pro Stream Webcam are present — higher-end cameras with likely better driver support

**MultiCamApp implication:** The j5 Webcam JVU250 duplicate entries (4×) explain why DirectShow enumeration ordering matters. The `DuplicateUsbCapturePolicy` code is addressing a real hardware pattern on this PC.

---

## Imaging Devices Detected (PnP Class: Image)

| Device name | PnP Status |
|---|---|
| WSD Scan Device | Unknown |
| Canon MF720C Series | OK |

**Note:** The Canon printer/scanner appears as an Imaging device. Not relevant to camera recording.

---

## USB Controllers and Hubs Summary

| Item | Detail |
|---|---|
| Intel USB 3.10 eXtensible Host Controller | OK — primary USB 3.1 host |
| Intel USB 3.20 eXtensible Host Controller | OK — USB 3.2 host |
| USB4 Root Router (1.0) | OK |
| USB4 Host Router (Microsoft) | OK |
| USB4 Router (1.0) — Anker USB4 Device | OK — external USB4 hub detected |
| Generic SuperSpeed USB Hub | OK |
| Generic USB Hub | OK |
| USB Root Hub (USB 3.0) × 2 | OK |
| Realtek USB 3.0 Card Reader | One OK, one Unknown |
| Unknown USB Device (Port Reset Failed) | 1 entry — possible problematic port |
| Unknown USB Device (Device Descriptor Request Failed) | 2 entries — possible failed/disconnected devices |

**Key findings:**
- USB infrastructure is extensive: USB 3.1, USB 3.2, and USB4 host controllers all present
- An **Anker USB4 hub** is detected — external USB hub being used for camera connections
- 3 unknown/failed USB device entries exist — these may correspond to cameras that were previously connected or failed to enumerate
- The "Port Reset Failed" entry is worth monitoring; it could indicate a camera that intermittently disconnects

**MultiCamApp implication:** The USB hub topology is complex (USB4 hub + multiple host controllers). The `UsbTopologyScanner` in `diagnostics/` is addressing a real need — inter-hub latency differences could affect synchronized multi-camera recording.

---

## Camera-Related Appx Packages

| Package name | Version | Publisher |
|---|---|---|
| Microsoft.WindowsCamera | 2025.2510.2.0 | Microsoft Corporation |

**Key findings:**
- Windows Camera app version **2025.2510.2.0** is installed — this is the current Microsoft Store version
- No other camera-related Appx packages detected

---

## Studio Effects-Related Appx Packages

| Result |
|---|
| No Studio Effects Appx packages detected by `Get-AppxPackage *studio*` |

**Note:** Windows Studio Effects may still be present as part of the OS camera pipeline rather than a separate Appx package. Availability should be confirmed by opening Windows Settings → Bluetooth & devices → Cameras during the manual observation phase.

---

## Active Power Plan

| Field | Value |
|---|---|
| Active power plan | **High performance** |
| GUID | 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c |

**MultiCamApp implication:** High performance power plan is active — CPU and GPU throttling are not a factor. This is the correct setting for multi-camera recording.

---

## Hardware-Accelerated GPU Scheduling (HAGS)

| Source | Finding |
|---|---|
| Registry (HwSchMode) | Not readable from scan context |
| dxdiag (NVIDIA RTX 3080) | **Enabled** (`DriverSupportState:Stable Enabled:True`) |

**MultiCamApp implication:** HAGS is confirmed enabled on the NVIDIA GPU. This means Windows can schedule GPU work with lower latency — relevant for a future GPU-assisted preview pipeline.

---

## Camera Privacy / Access

| Field | Value |
|---|---|
| System-level camera access | **Allow** |
| Registry key | `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam` |

**MultiCamApp implication:** Camera access is allowed at the system level. App-level permission may still need to be confirmed in Windows Privacy Settings, but no system-level block is present.

---

## DirectX Availability

| Field | Value |
|---|---|
| dxdiag generated | Yes — `diagnostics/dxdiag_multicam.txt` |
| DirectX version | DirectX 12 |
| Intel Xe feature level | 12_1 max |
| NVIDIA RTX 3080 feature level | **12_2 max** |
| NVIDIA driver model | WDDM 3.2 |

---

## Limitations and Unknowns

| Item | Status |
|---|---|
| Windows Studio Effects availability | Unknown — not detectable from Appx packages alone; requires manual check in Windows Settings |
| Digital stabilization availability | Unknown — requires manual check in Windows Camera settings |
| Low-light compensation availability | Unknown — requires manual check |
| HAGS registry key | Not readable in this scan context (dxdiag confirms it is enabled) |
| External cameras (j5, OBSBOT, C922, USB Live) | Status `Unknown` in PnP — not physically connected at scan time |
| Actual camera mode support | Not collected (no camera was opened) |
| FPS delivery confirmation | Not collected (no recording was performed) |
| GPU Video Encode availability (NVENC) | Likely available based on RTX 3080 hardware; needs future validation |
| Windows Camera Observation Summary | **Not collected yet — requires manual observation session** |
