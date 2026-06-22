#!/usr/bin/env python3
"""Apply or refresh STABLE_CORE_V1 file banners on protected stable-core sources."""
from __future__ import annotations

import re
from pathlib import Path

BANNER = (
    "////////////////////////////////////////////////////\n"
    "/// STABLE_CORE_V1\n"
    "/// Validated in MultiCamApp v1.0.36 build 136.\n"
    "/// Do not modify without documented regression testing.\n"
    "/// Protected: recording, metadata, verification, session comparison.\n"
    "////////////////////////////////////////////////////\n"
    "// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.\n"
    "\n"
)

OLD_BANNER_RE = re.compile(
    r"^/{50,}\r?\n"
    r"(?:///{1,3} STABLE_RECORDING_CORE_V1.*?\r?\n)+"
    r"(?:// .*?\r?\n)+"
    r"/{50,}\r?\n"
    r"// STABLE_RECORDING_CORE_V1.*?\r?\n"
    r"\r?\n",
    re.MULTILINE | re.DOTALL,
)

OLD_BANNER_RE2 = re.compile(
    r"^/{50,}\r?\n"
    r"// STABLE_RECORDING_CORE_V1\r?\n"
    r"// Recording subsystem frozen after validation\.\r?\n"
    r"// Changes require regression testing\.\r?\n"
    r"// Do not modify without justification\.\r?\n"
    r"/{50,}\r?\n"
    r"// STABLE_RECORDING_CORE_V1 - DO NOT MODIFY WITHOUT VALIDATION\r?\n"
    r"\r?\n",
    re.MULTILINE,
)

ROOT = Path(__file__).resolve().parents[2] / "source" / "MultiCamApp" / "MultiCamApp"

FILES = [
    # Recording engine
    "recording/MultiCameraRecordingCoordinator.cs",
    "recording/RecordingSession.cs",
    "recording/RecordingController.cs",
    "recording/RecordingFileNaming.cs",
    "recording/OutputFolderManager.cs",
    "recording/SessionFolderNameGenerator.cs",
    "recording/RecordingCameraStats.cs",
    "recording/RecordingTiming.cs",
    "recording/MediaProfileBuilder.cs",
    "recording/RecordingSettings.cs",
    "capture/CameraManager.cs",
    "capture/CameraDeviceDiscovery.cs",
    "capture/CameraSlotPipeline.cs",
    "capture/OpenCvPreviewController.cs",
    "capture/CameraModeSelector.cs",
    "capture/CameraMode.cs",
    "capture/CaptureResolutionHelper.cs",
    "capture/CameraProfile.cs",
    "capture/DirectShowVideoDeviceEnumerator.cs",
    "capture/OpenCvDeviceMapper.cs",
    "capture/PreviewController.cs",
    "capture/SlotDeviceOwnership.cs",
    "experiment/FrameTimingMonitor.cs",
    "utils/MonotonicClock.cs",
    "utils/PathHelper.cs",
    "core/AppConfig.cs",
    # Metadata system
    "metadata/MetadataWriter.cs",
    "metadata/CameraRecordingMetadata.cs",
    "metadata/SessionSummaryWriter.cs",
    "metadata/RecordingTimingMetrics.cs",
    "metadata/ScientificTimingAssessor.cs",
    "metadata/CaptureTimingSnapshot.cs",
    "metadata/CaptureIntervalMetadataFormatter.cs",
    # Video verification + session comparison
    "verification/VideoVerificationService.cs",
    "verification/SessionComparisonService.cs",
    "verification/VideoProbeService.cs",
    "verification/VideoScanner.cs",
    "verification/ExpectedSettingsResolver.cs",
    "verification/MetadataParser.cs",
    "verification/VerificationReportWriter.cs",
    "verification/VerificationReportMapper.cs",
    "verification/CameraAuditStatus.cs",
    "verification/RecordingSessionDiscovery.cs",
    "verification/VerificationCaptureProfile.cs",
    "verification/VerificationTableRow.cs",
    "verification/VerificationResult.cs",
    "verification/VerificationMatchStatus.cs",
]


def strip_old_banner(text: str) -> str:
    for pattern in (OLD_BANNER_RE, OLD_BANNER_RE2):
        while True:
            new_text = pattern.sub("", text, count=1)
            if new_text == text:
                break
            text = new_text
    return text.lstrip("\ufeff")


def main() -> int:
    for rel in FILES:
        path = ROOT / rel
        if not path.is_file():
            print(f"MISSING: {rel}")
            continue
        original = path.read_text(encoding="utf-8")
        text = strip_old_banner(original)
        has_core_banner = text.startswith("////////////////////////////////////////////////////") and "STABLE_CORE_V1" in text[:400]
        if not has_core_banner:
            text = BANNER + text
            print(f"TAGGED: {rel}")
        elif text != original:
            print(f"CLEANED: {rel}")
        else:
            print(f"OK: {rel}")
        path.write_text(text, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
