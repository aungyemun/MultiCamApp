#!/usr/bin/env python3
"""Apply STABLE_CORE_V2 file banners on the current, actively-used production pipeline
(VideoEngineV2 recording engine + native V2 verification/session-comparison), effective
MultiCamApp v2.0.0 (first stable release). See docs/STABLE_CORE_V2_FREEZE.md.

For files that previously carried a STABLE_CORE_V1 banner and are being promoted here,
the old V1 banner block is replaced with the new V2 one (history is kept in
docs/STABLE_CORE_V2_FREEZE.md's lineage note, not duplicated in every file header).
"""
from __future__ import annotations

import re
from pathlib import Path

V2_BANNER = (
    "////////////////////////////////////////////////////\n"
    "/// STABLE_CORE_V2\n"
    "/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).\n"
    "/// Do not modify without documented regression testing.\n"
    "/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.\n"
    "////////////////////////////////////////////////////\n"
    "// STABLE_CORE_V2 protected component — modification requires regression checklist; "
    "do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.\n"
    "\n"
)

OLD_V1_BANNER_RE = re.compile(
    r"^/{50,}\r?\n"
    r"/// STABLE_CORE_V1\r?\n"
    r"/// Validated in MultiCamApp v1\.0\.36 build 136\.\r?\n"
    r"/// Do not modify without documented regression testing\.\r?\n"
    r"/// Protected: recording, metadata, verification, session comparison\.\r?\n"
    r"/{50,}\r?\n"
    r"// STABLE_CORE_V1 protected component.*?\r?\n"
    r"\r?\n",
    re.MULTILINE,
)

ROOT = Path(__file__).resolve().parents[2] / "source" / "MultiCamApp" / "MultiCamApp"

FILES = [
    # VideoEngineV2 — the current, active camera recording engine
    "capture/video_engine_v2/CameraControlManagerV2.cs",
    "capture/video_engine_v2/CameraDeviceManagerV2.cs",
    "capture/video_engine_v2/CameraDeviceWatcher.cs",
    "capture/video_engine_v2/CameraFormatSelectorV2.cs",
    "capture/video_engine_v2/CameraPipelineV2.cs",
    "capture/video_engine_v2/D3D11Interop.cs",
    "capture/video_engine_v2/D3D11PreviewPanel.cs",
    "capture/video_engine_v2/D3D11SwapChainHost.cs",
    "capture/video_engine_v2/Direct3DPreviewRenderer.cs",
    "capture/video_engine_v2/FrameTimestampMonitor.cs",
    "capture/video_engine_v2/MediaFoundationCaptureService.cs",
    "capture/video_engine_v2/MediaFoundationEncoderService.cs",
    "capture/video_engine_v2/MediaFoundationRuntime.cs",
    "capture/video_engine_v2/RecordingHealthMonitor.cs",
    "capture/video_engine_v2/VideoEngineBackendSelector.cs",
    "capture/video_engine_v2/VideoEngineDiagnostics.cs",
    "capture/video_engine_v2/VideoEngineFallbackPolicy.cs",
    "capture/video_engine_v2/VideoEngineSettings.cs",
    "capture/video_engine_v2/VideoEngineV2.cs",
    "capture/video_engine_v2/VideoEngineV2Models.cs",
    # Backend abstraction / registry (selects and describes the active engine)
    "capture/backend/VideoEngineRegistry.cs",
    "capture/backend/BackendIdentifiers.cs",
    "capture/backend/IVideoEngineBackend.cs",
    "capture/backend/V2SelectionHardeningModels.cs",
    # Recording orchestration entry point (V2 metadata writer lives here)
    "MainWindow.xaml.cs",
    # Video verification + session comparison (native V2-aware pipeline)
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
    "verification/V2MetadataReader.cs",
    "verification/V2VerificationRunner.cs",
    "verification/V2RecordingVerifier.cs",
    "verification/MetadataCompletenessPolicy.cs",
    "verification/DeepVerifyService.cs",
    "ui/pages/VideoVerificationPage.xaml.cs",
    "metadata/ScientificTimingAssessor.cs",
    # Shared timing/config infrastructure used by the pipeline above
    "utils/MonotonicClock.cs",
    "core/AppConfig.cs",
]


def main() -> int:
    for rel in FILES:
        path = ROOT / rel
        if not path.is_file():
            print(f"MISSING: {rel}")
            continue
        original = path.read_text(encoding="utf-8")
        text = original.lstrip("﻿")
        bom = "﻿" if original.startswith("﻿") else ""

        text = OLD_V1_BANNER_RE.sub("", text, count=1)

        has_v2_banner = text.startswith("////////////////////////////////////////////////////") and "STABLE_CORE_V2" in text[:400]
        if has_v2_banner:
            print(f"OK: {rel}")
            continue

        text = V2_BANNER + text
        path.write_text(bom + text, encoding="utf-8")
        print(f"TAGGED: {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
