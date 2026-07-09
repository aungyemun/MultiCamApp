#!/usr/bin/env python3
"""Audit all MP4 files in a folder tree: FPS, resolution, duration, vs metadata."""
import json
import os
import shutil
import subprocess
import sys
import csv
import argparse
from datetime import datetime, timezone
from pathlib import Path

ORIGINAL_CAPTURE_MODE = "OriginalCapture"
LEGACY_CONSTANT_FRAME_COUNT_MODE = "LegacyConstantFrameCount"
FPS_DIFF_ACCEPTABLE = 0.5
WALL_DURATION_DIFF_ACCEPTABLE_SEC = 0.1
START_END_OFFSET_ACCEPTABLE_SEC = 0.1
START_END_OFFSET_ACCEPTABLE_MS = START_END_OFFSET_ACCEPTABLE_SEC * 1000.0
UNSTABLE_CAPTURE_INTERVAL_STD_MS = 10.0
UNSTABLE_MAX_CONSECUTIVE_NO_FRAME = 2
VISUAL_CHECK_FRAME_LIMIT = 10000
VISUAL_CHECK_WIDTH = 64
VISUAL_CHECK_HEIGHT = 36
VISUAL_NEAR_IDENTICAL_MAD_THRESHOLD = 1.0
ORIGINAL_CAPTURE_SESSION_NOTE = (
    "Original Capture Mode preserves only real captured frames. Frame counts may differ between cameras. "
    "Use timestamp CSV for timing-sensitive analysis."
)
STABLE_DIFFERENT_FPS_NOTE = (
    "Camera delivered stable measured FPS different from requested FPS. This is acceptable in Original Capture Mode "
    "when stable and recorded in metadata."
)
ORIGINAL_CAPTURE_REAL_FRAMES_MESSAGE = "Original Capture Mode preserved only real camera frames."
ORIGINAL_CAPTURE_FRAME_DIFF_MESSAGE = "Frame counts may differ because cameras delivered real frames at different measured FPS."
ORIGINAL_CAPTURE_ANALYSIS_MESSAGE = "Use per-frame timestamps or measured FPS for trimming and scientific analysis."
ORIGINAL_CAPTURE_CONTAINER_DURATION_MESSAGE = "MP4 container duration may be frame-based and shorter than wall-clock duration."
TIMESTAMP_TRIM_WARNING = (
    "MP4 container duration is frame-based and differs from real wall-clock duration. "
    "For scientific trimming, use per-frame capture timestamps or measured camera FPS."
)
TIMESTAMP_TRIM_GUIDANCE = (
    "To trim 30s to 630s, select frames where captureMonotonicSec is between "
    "firstFrameCaptureMonotonicSec + 30 and +630."
)
CRITICAL_TIMING_FIELDS = (
    "SessionStartUtcTime",
    "SessionStopUtcTime",
    "SessionMonotonicDurationSec",
    "RecordingRequestedStartUtcTime",
    "CameraRecordingStartUtcTime",
    "FirstFrameUtcTime",
    "FirstFrameMonotonicSec",
    "LastFrameUtcTime",
    "LastFrameMonotonicSec",
    "CameraRecordingStopUtcTime",
    "WriterClosedUtcTime",
    "WriterClosedMonotonicSec",
    "FrameTimestampCsvPath",
    "FrameTimestampCsvWritten",
    "FrameTimestampCsvRowCount",
    "FpsStabilityGrade",
)


def find_ffprobe() -> str | None:
    root = os.environ.get("MULTICAMAPP_ROOT")
    if root:
        local = Path(root) / "runtime" / "ffmpeg" / "ffprobe.exe"
        if local.is_file():
            return str(local)
    dist = Path(__file__).resolve().parents[2] / "dist" / "runtime" / "ffmpeg" / "ffprobe.exe"
    if dist.is_file():
        return str(dist)
    repo_runtime = Path(__file__).resolve().parents[2] / "runtime" / "ffmpeg" / "ffprobe.exe"
    if repo_runtime.is_file():
        return str(repo_runtime)
    return shutil.which("ffprobe")


def find_ffmpeg(ffprobe: str | None = None) -> str | None:
    if ffprobe:
        candidate = Path(ffprobe).with_name("ffmpeg.exe")
        if candidate.is_file():
            return str(candidate)
    root = os.environ.get("MULTICAMAPP_ROOT")
    if root:
        local = Path(root) / "runtime" / "ffmpeg" / "ffmpeg.exe"
        if local.is_file():
            return str(local)
    dist = Path(__file__).resolve().parents[2] / "dist" / "runtime" / "ffmpeg" / "ffmpeg.exe"
    if dist.is_file():
        return str(dist)
    repo_runtime = Path(__file__).resolve().parents[2] / "runtime" / "ffmpeg" / "ffmpeg.exe"
    if repo_runtime.is_file():
        return str(repo_runtime)
    return shutil.which("ffmpeg")


def rate_to_float(rate: str | None) -> float:
    if not rate or rate == "0/0":
        return 0.0
    if "/" in rate:
        n, d = rate.split("/", 1)
        try:
            return float(n) / float(d) if float(d) != 0 else 0.0
        except ValueError:
            return 0.0
    try:
        return float(rate)
    except ValueError:
        return 0.0


def probe(ffprobe: str, path: Path) -> dict:
    cmd = [
        ffprobe, "-v", "quiet", "-print_format", "json",
        "-show_format", "-show_streams", str(path),
    ]
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        return {"error": r.stderr.strip() or f"ffprobe exit {r.returncode}"}
    data = json.loads(r.stdout)
    video = next((s for s in data.get("streams", []) if s.get("codec_type") == "video"), {})
    fmt = data.get("format", {})
    avg_fps = rate_to_float(video.get("avg_frame_rate"))
    raw_fps = rate_to_float(video.get("r_frame_rate"))
    duration = float(fmt.get("duration") or 0)
    size = int(fmt.get("size") or 0)
    nb_frames = video.get("nb_frames")
    frame_count = int(nb_frames) if nb_frames and str(nb_frames).isdigit() else None
    effective_fps = (frame_count / duration) if frame_count and duration > 0 else None
    bitrate = int(fmt.get("bit_rate") or 0) or (int(size * 8 / duration) if duration > 0 and size else 0)
    return {
        "codec": video.get("codec_name"),
        "codec_long": video.get("codec_long_name"),
        "pix_fmt": video.get("pix_fmt"),
        "width": video.get("width"),
        "height": video.get("height"),
        "avg_frame_rate": video.get("avg_frame_rate"),
        "r_frame_rate": video.get("r_frame_rate"),
        "avg_fps": round(avg_fps, 6),
        "raw_fps": round(raw_fps, 6),
        "constant_fps": abs(avg_fps - raw_fps) <= 0.05 if avg_fps and raw_fps else None,
        "frame_count": frame_count,
        "duration_sec": round(duration, 6),
        "size_bytes": size,
        "size_gb": round(size / (1024 ** 3), 3),
        "bitrate_mbps": round(bitrate / 1_000_000, 2),
        "effective_fps": round(effective_fps, 6) if effective_fps else None,
    }


def load_metadata(cam_dir: Path) -> dict | None:
    meta_path = cam_dir / "metadata.json"
    if not meta_path.is_file():
        return None
    # Accept both canonical UTF-8 JSON and older .NET output that included a BOM.
    with meta_path.open(encoding="utf-8-sig") as f:
        return normalize_metadata(json.load(f))


def normalize_metadata(meta: dict) -> dict:
    """Expose current nested V2 metadata through the legacy audit field contract."""
    timing = meta.get("timing")
    cameras = meta.get("cameras")
    if not isinstance(timing, dict) or not isinstance(cameras, list) or not cameras:
        return meta

    camera = cameras[0] if isinstance(cameras[0], dict) else {}
    settings = meta.get("videoSettings") if isinstance(meta.get("videoSettings"), dict) else {}
    verification = meta.get("verification") if isinstance(meta.get("verification"), dict) else {}
    app_version = meta.get("appVersion") if isinstance(meta.get("appVersion"), dict) else {}
    startup = meta.get("startupSettling") if isinstance(meta.get("startupSettling"), dict) else {}
    timing_models = meta.get("timingModels") if isinstance(meta.get("timingModels"), dict) else {}
    app_timing = timing_models.get("appTimestampTiming") if isinstance(timing_models.get("appTimestampTiming"), dict) else {}

    selected_resolution = str(camera.get("selectedResolution") or "")
    width = height = None
    if "x" in selected_resolution.lower():
        try:
            width, height = (int(v) for v in selected_resolution.lower().split("x", 1))
        except ValueError:
            width = height = None

    rows = int(timing.get("timestampCsvRows") or 0)
    gap_count = int(timing.get("timestampGapCount") or 0)
    interval_std = float(app_timing.get("intervalStdMs") or 0)
    stable_after_warmup = bool(startup.get("stableAfterWarmup"))

    aliases = {
        "Width": width,
        "Height": height,
        "RequestedResolution": camera.get("requestedResolution") or settings.get("requestedResolution"),
        "SelectedResolution": selected_resolution,
        "RequestedFps": camera.get("requestedFps") or settings.get("requestedFps"),
        "SelectedFps": camera.get("selectedFormatFps"),
        "WriterFps": camera.get("writerFps"),
        "MeasuredCameraFps": timing.get("estimatedFpsFromTimestamps"),
        "FramesWritten": timing.get("framesWritten"),
        "FramesCaptured": rows,
        "FrameTimestampCsvWritten": bool(timing.get("timestampCsvWritten")),
        "FrameTimestampCsvRowCount": rows,
        "FrameTimestampCsvPath": timing.get("timestampCsvFile"),
        "RecordingTimingMode": settings.get("recordingTimingMode") or "V2ParallelTimestampCapture",
        "OriginalCaptureMode": False,
        "ConstantFrameCountMode": False,
        "WallClockDurationSeconds": timing.get("resolvedDurationS") or timing.get("monotonicDurationS"),
        "CaptureIntervalCount": max(0, rows - 1),
        "CaptureIntervalMeanMs": timing.get("meanFrameIntervalMs"),
        "CaptureIntervalMedianMs": timing.get("medianFrameIntervalMs"),
        "CaptureIntervalMinMs": timing.get("minFrameIntervalMs"),
        "CaptureIntervalMaxMs": timing.get("maxFrameIntervalMs"),
        "CaptureIntervalP95Ms": timing.get("p95FrameIntervalMs"),
        "CaptureIntervalP99Ms": timing.get("p99FrameIntervalMs"),
        "CaptureIntervalStdMs": interval_std,
        "LongGapCount": gap_count,
        "SevereLongGapCount": gap_count,
        "FpsStabilityGrade": "Good" if stable_after_warmup and gap_count == 0 else "Borderline",
        "ScientificTimingStatus": verification.get("timingConfidence"),
        "ScientificTimingMessage": verification.get("sessionResult"),
        "AppVersion": app_version.get("version"),
        "BuildNumber": app_version.get("build"),
    }
    for key, value in aliases.items():
        if value is not None and key not in meta:
            meta[key] = value
    return meta


def _format_capture_interval_ms(meta: dict, key: str, legacy_key: str | None = None) -> str:
    count = meta.get("CaptureIntervalCount", 0) or 0
    if count < 1:
        msg = meta.get("CaptureIntervalStatsMessage") or "Insufficient capture timestamps for interval statistics."
        return f"Unavailable ({msg})"
    value = meta.get(key)
    if value in (None, 0) and legacy_key:
        value = meta.get(legacy_key)
    return f"{float(value):.3f}" if value not in (None, "") else "Unavailable"


def _meta_value(meta: dict, *keys, default=0):
    for key in keys:
        if key in meta and meta[key] not in (None, ""):
            return meta[key]
    return default


def _meta_int(meta: dict, *keys, default=0) -> int:
    value = _meta_value(meta, *keys, default=default)
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


DUPLICATE_CORRECTED_SESSION_MESSAGE = (
    "Session result: PASS_WITH_WARNING. Videos are valid and constant-frame-count aligned. "
    "Duplicate-frame correction was applied because one or more cameras delivered below the target FPS. "
    "No writer queue drops or placeholders were detected."
)


def _as_float(value, default=0.0) -> float:
    try:
        if value in (None, ""):
            return default
        return float(value)
    except (TypeError, ValueError):
        return default


def _meta_bool(meta: dict, *keys, default=False) -> bool:
    value = _meta_value(meta, *keys, default=default)
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        return value.strip().lower() in ("true", "1", "yes", "y")
    return bool(value)


def _average_hash(frame: bytes, width: int, height: int) -> int:
    hash_w = 8
    hash_h = 8
    values = []
    for y in range(hash_h):
        start_y = y * height // hash_h
        end_y = max(start_y + 1, (y + 1) * height // hash_h)
        for x in range(hash_w):
            start_x = x * width // hash_w
            end_x = max(start_x + 1, (x + 1) * width // hash_w)
            total = 0
            count = 0
            for yy in range(start_y, end_y):
                row = yy * width
                for xx in range(start_x, end_x):
                    total += frame[row + xx]
                    count += 1
            values.append(total / count if count else 0)
    mean = sum(values) / len(values) if values else 0
    bits = 0
    for i, value in enumerate(values):
        if value >= mean:
            bits |= 1 << i
    return bits


def _hamming_distance(left: int, right: int) -> int:
    return (left ^ right).bit_count()


def visual_near_duplicate_check(ffmpeg: str | None, path: Path) -> dict:
    if not ffmpeg:
        return {"enabled": True, "error": "ffmpeg not found"}

    frame_size = VISUAL_CHECK_WIDTH * VISUAL_CHECK_HEIGHT
    cmd = [
        ffmpeg,
        "-v", "error",
        "-i", str(path),
        "-map", "0:v:0",
        "-frames:v", str(VISUAL_CHECK_FRAME_LIMIT),
        "-vf", f"scale={VISUAL_CHECK_WIDTH}:{VISUAL_CHECK_HEIGHT},format=gray",
        "-f", "rawvideo",
        "-",
    ]
    try:
        result = subprocess.run(cmd, capture_output=True, timeout=180)
    except Exception as exc:
        return {"enabled": True, "error": str(exc)}

    if result.returncode != 0:
        return {"enabled": True, "error": result.stderr.decode("utf-8", errors="replace").strip()}

    raw = result.stdout
    frame_count = len(raw) // frame_size
    if frame_count < 2:
        return {
            "enabled": True,
            "framesSampled": frame_count,
            "meanAbsolutePixelDifference": 0.0,
            "grayscaleDifferenceMean": 0.0,
            "perceptualHashDifference": 0.0,
            "nearIdenticalConsecutiveFrameCount": 0,
            "nearIdenticalConsecutiveFrameRate": 0.0,
        }

    differences = []
    hash_differences = []
    near_identical = 0
    exact_duplicates = 0
    exact_run = near_run = 0
    longest_exact_run = longest_near_run = 0
    previous = raw[0:frame_size]
    previous_hash = _average_hash(previous, VISUAL_CHECK_WIDTH, VISUAL_CHECK_HEIGHT)
    for index in range(1, frame_count):
        current = raw[index * frame_size:(index + 1) * frame_size]
        diff = sum(abs(a - b) for a, b in zip(previous, current)) / frame_size
        differences.append(diff)
        if current == previous:
            exact_duplicates += 1
            exact_run += 1
            longest_exact_run = max(longest_exact_run, exact_run)
        else:
            exact_run = 0
        if diff <= VISUAL_NEAR_IDENTICAL_MAD_THRESHOLD:
            near_identical += 1
            near_run += 1
            longest_near_run = max(longest_near_run, near_run)
        else:
            near_run = 0
        current_hash = _average_hash(current, VISUAL_CHECK_WIDTH, VISUAL_CHECK_HEIGHT)
        hash_differences.append(_hamming_distance(previous_hash, current_hash))
        previous = current
        previous_hash = current_hash

    comparisons = max(1, frame_count - 1)
    mean_difference = sum(differences) / len(differences) if differences else 0.0
    mean_hash_difference = sum(hash_differences) / len(hash_differences) if hash_differences else 0.0
    return {
        "enabled": True,
        "framesSampled": frame_count,
        "meanAbsolutePixelDifference": round(mean_difference, 6),
        "grayscaleDifferenceMean": round(mean_difference, 6),
        "perceptualHashDifference": round(mean_hash_difference, 6),
        "nearIdenticalConsecutiveFrameCount": near_identical,
        "nearIdenticalConsecutiveFrameRate": round(near_identical / comparisons, 6),
        "exactConsecutiveDuplicateCount": exact_duplicates,
        "longestExactDuplicateRun": longest_exact_run,
        "longestNearIdenticalRun": longest_near_run,
    }


def duplicate_frames(meta: dict | None) -> int:
    if not meta:
        return 0
    return _meta_int(meta, "DuplicateFrames") + _meta_int(meta, "DuplicatedFrames")


def app_created_duplicate_frames(meta: dict | None) -> int:
    if not meta:
        return 0
    return _meta_int(meta, "DuplicateFrames")


def app_created_placeholder_frames(meta: dict | None) -> int:
    if not meta:
        return 0
    return _meta_int(meta, "PlaceholderFrames")


def original_capture_mode(meta: dict | None) -> bool:
    if not meta:
        return False
    mode = str(meta.get("RecordingTimingMode") or meta.get("recordingTimingMode") or "").strip().lower()
    return bool(meta.get("OriginalCaptureMode") or meta.get("originalCaptureMode") or mode == ORIGINAL_CAPTURE_MODE.lower())


def recording_timing_mode(meta: dict | None) -> str:
    if not meta:
        return "-"
    explicit = str(meta.get("RecordingTimingMode") or meta.get("recordingTimingMode") or "").strip()
    if explicit:
        return explicit
    if duplicate_frames(meta) > 0 or bool(meta.get("ConstantFrameCountMode")):
        return LEGACY_CONSTANT_FRAME_COUNT_MODE
    return "-"


def frames_written(meta: dict | None, probe_data: dict) -> int:
    if meta:
        written = _meta_int(meta, "FramesWritten", "FrameCount", default=0)
        if written:
            return written
    return int(probe_data.get("frame_count") or 0)


def duration_seconds(meta: dict | None, probe_data: dict) -> float:
    if meta:
        duration = _as_float(_meta_value(
            meta, "WallClockDurationSeconds", "WallDurationSeconds", "DurationSeconds", default=0
        ))
        if duration > 0:
            return duration
    return _as_float(probe_data.get("duration_sec"))


def measured_camera_fps(meta: dict | None) -> float:
    return _as_float(_meta_value(
        meta or {},
        "MeasuredCameraFps",
        "MeasuredWriterFps",
        "MeasuredCameraFpsFromFirstLastFrame",
        "MeasuredCameraFpsFromMeanInterval",
        default=0))


def writer_or_container_fps(meta: dict | None, probe_data: dict | None = None) -> float:
    probe_data = probe_data or {}
    return _as_float(_meta_value(
        meta or {},
        "WriterFps",
        "ContainerFps",
        "RecordingWriterFps",
        "SelectedDeviceFps",
        default=probe_data.get("avg_fps") or 0))


def fps_stability_grade(meta: dict | None) -> str:
    return str(_meta_value(meta or {}, "FpsStabilityGrade", "fpsStabilityGrade", default="")).strip()


def measured_fps_is_stable(meta: dict | None) -> bool:
    grade = fps_stability_grade(meta).lower()
    if grade in ("excellent", "good"):
        return True
    if grade in ("borderline", "unstable", "failed"):
        return False
    interval_std = _as_float(_meta_value(meta or {}, "CaptureIntervalStdMs", "CaptureJitterMs", default=0))
    p99 = _as_float(_meta_value(meta or {}, "CaptureIntervalP99Ms", "captureIntervalP99Ms", default=0))
    expected = _as_float(_meta_value(meta or {}, "ExpectedIntervalMs", "expectedIntervalMs", default=0))
    severe = _meta_int(meta or {}, "SevereLongGapCount", "severeLongGapCount")
    if expected > 0 and p99 > 0:
        return interval_std <= UNSTABLE_CAPTURE_INTERVAL_STD_MS and p99 <= expected * 1.75 and severe == 0
    return interval_std > 0 and interval_std <= UNSTABLE_CAPTURE_INTERVAL_STD_MS and severe == 0


def original_capture_timing_fields_missing(meta: dict) -> list[str]:
    missing = []
    for field in CRITICAL_TIMING_FIELDS:
        value = _meta_value(meta, field, field[:1].lower() + field[1:], default="")
        if value in ("", None):
            missing.append(field)
    return missing


def session_scientific_timing_confidence(group: list[dict]) -> str:
    if not group:
        return "FAILED"
    if any(r["status"] == "FAIL" or "error" in r["probe"] for r in group):
        return "FAILED"
    metas = [r.get("metadata") for r in group if r.get("metadata")]
    if len(metas) != len(group):
        return "LOW"
    original = all(original_capture_mode(m) and not _meta_bool(m, "ConstantFrameCountMode", "constantFrameCountMode") for m in metas)
    no_loss = all(
        _meta_int(m, "FramesCaptured", default=_meta_int(m, "FramesWritten", "FrameCount")) == _meta_int(m, "FramesWritten", "FrameCount")
        and duplicate_frames(m) == 0
        and _meta_int(m, "PlaceholderFrames") == 0
        and _meta_int(m, "WriterQueueDrops") == 0
        for m in metas
    )
    timestamp_ok = all(
        _meta_bool(m, "FrameTimestampCsvWritten", "frameTimestampCsvWritten")
        and _meta_int(m, "FrameTimestampCsvRowCount", "frameTimestampCsvRowCount") == _meta_int(m, "FramesWritten", "FrameCount")
        for m in metas
    )
    stable = all(measured_fps_is_stable(m) for m in metas)
    start_ok = all(_as_float(_meta_value(m, "InterCameraStartOffsetMs", default=0)) <= START_END_OFFSET_ACCEPTABLE_MS for m in metas)
    wall = [_as_float(_meta_value(m, "WallClockDurationSeconds", "WallDurationSeconds", default=0)) for m in metas]
    wall = [w for w in wall if w > 0]
    wall_ok = len(wall) < 2 or max(wall) - min(wall) <= WALL_DURATION_DIFF_ACCEPTABLE_SEC
    metadata_complete = all(not original_capture_timing_fields_missing(m) for m in metas)
    borderline = any(fps_stability_grade(m).lower() == "borderline" or _meta_int(m, "LongGapCount", "longGapCount") > 0 for m in metas)
    low_conditions = any(
        fps_stability_grade(m).lower() in ("unstable", "failed")
        or _meta_int(m, "SevereLongGapCount", "severeLongGapCount") > 0
        or _meta_int(m, "LongGapCount", "longGapCount") > 2
        for m in metas
    )

    if original and no_loss and timestamp_ok and stable and start_ok and wall_ok and metadata_complete:
        return "HIGH"
    if no_loss and not low_conditions and (not timestamp_ok or borderline):
        return "MEDIUM"
    return "LOW"


def wall_clock_duration(meta: dict | None, probe_data: dict) -> float:
    return duration_seconds(meta, probe_data)


def duplicate_rate_per_minute(meta: dict | None, probe_data: dict) -> float:
    duration = duration_seconds(meta, probe_data)
    return duplicate_frames(meta) / (duration / 60.0) if duration > 0 else 0.0


def duplicate_percentage(meta: dict | None, probe_data: dict) -> float:
    written = frames_written(meta, probe_data)
    return duplicate_frames(meta) * 100.0 / written if written > 0 else 0.0


def camera_stability(meta: dict | None, probe_data: dict) -> str:
    if not meta:
        return "Poor"
    pct = duplicate_percentage(meta, probe_data)
    queue_drops = _meta_int(meta, "WriterQueueDrops")
    placeholders = _meta_int(meta, "PlaceholderFrames")
    duplicates = duplicate_frames(meta)
    writer_fps = _as_float(_meta_value(meta, "WriterFps", "RecordingWriterFps", "SelectedDeviceFps", default=0))
    measured_fps = _as_float(_meta_value(meta, "MeasuredCameraFps", "MeasuredWriterFps", default=0))

    if queue_drops > 0 or placeholders > 0 or pct > 2.0:
        return "Poor"
    if duplicates <= 1 and writer_fps > 0 and measured_fps > 0 and abs(writer_fps - measured_fps) <= 0.05:
        return "Excellent"
    if pct < 0.5:
        return "Good"
    if pct <= 2.0:
        return "Warning"
    return "Poor"


def recommended_preset(meta: dict | None, probe_data: dict) -> str:
    if original_capture_mode(meta):
        if _meta_int(meta or {}, "WriterQueueDrops") > 0:
            return "Original Capture Mode recorded writer queue drops. Reduce camera load or re-record before scientific use."
        if _meta_int(meta or {}, "PlaceholderFrames") > 0:
            return "Original Capture Mode should not contain placeholders. Review metadata and re-record before scientific use."
        if duplicate_frames(meta) > 0:
            return "Original Capture Mode expected duplicateFrames=0. Review metadata before scientific use."
        return "Original Capture Mode: Real frames only; no duplicates/placeholders. Use timestamp CSV for timing-sensitive analysis."
    stability = camera_stability(meta, probe_data)
    resolution = ""
    if meta:
        resolution = str(meta.get("RequestedResolution") or meta.get("SelectedResolution") or meta.get("Resolution") or "")
    if not resolution and "error" not in probe_data:
        resolution = f"{probe_data.get('width')}x{probe_data.get('height')}"

    if stability == "Poor":
        return "Reduce resolution or camera count, use separate USB controllers, and re-record before scientific use."
    if stability == "Warning" and ("1080" in resolution or "1920x1080" in resolution):
        return "Valid and aligned. Keep duplicate-frame reporting enabled; use 720p or fewer cameras if the duplicate rate must be reduced."
    if stability == "Warning":
        return "Valid and aligned. Keep duplicate-frame reporting enabled; reduce camera load if the duplicate rate must be reduced."
    return "Current preset is stable. Keep duplicate-frame reporting enabled for audit transparency."


def _session_message(status: str, duplicates: int, queue_drops: int, placeholders: int) -> str:
    if status.startswith("PASS_ORIGINAL_TIMING"):
        return (
            f"Session result: {status}. Original capture mode preserved only real camera frames. "
            "No duplicate frames, placeholders, or writer queue drops were detected."
        )
    if status == "PASS_WITH_WARNING" and duplicates > 0 and queue_drops == 0 and placeholders == 0:
        return DUPLICATE_CORRECTED_SESSION_MESSAGE
    if queue_drops > 0 or placeholders > 0:
        return (
            f"Session result: {status}. Timing integrity warning: writer queue drops or placeholders "
            "were detected; review before scientific use."
        )
    if status == "PASS":
        return (
            "Session result: PASS. Videos are valid and constant-frame-count aligned. "
            "No duplicate-frame correction, writer queue drops, or placeholders were detected."
        )
    return f"Session result: {status}. Review individual camera audit details."


def session_summaries(results: list[dict]) -> list[dict]:
    summaries = []
    sessions = sorted({r["session"] for r in results})
    for session in sessions:
        group = [r for r in results if r["session"] == session]
        if not group:
            continue
        statuses = [r["status"] for r in group]
        metas = [r.get("metadata") for r in group if r.get("metadata")]
        original_session = bool(metas) and all(original_capture_mode(m) for m in metas)
        total_duplicates = sum(duplicate_frames(r.get("metadata")) for r in group)
        queue_total = sum(_meta_int(r.get("metadata") or {}, "WriterQueueDrops") for r in group)
        placeholder_total = sum(_meta_int(r.get("metadata") or {}, "PlaceholderFrames") for r in group)
        visual_near_identical_total = sum(
            int((r.get("visual") or {}).get("nearIdenticalConsecutiveFrameCount") or 0)
            for r in group
        )
        if "FAIL" in statuses or queue_total > 0 or placeholder_total > 0:
            status = "FAIL"
        elif total_duplicates > 0 or "PASS_WITH_WARNING" in statuses:
            status = "PASS_WITH_WARNING"
        elif any(s == "PASS_ORIGINAL_TIMING_WITH_NOTE" for s in statuses):
            status = "PASS_ORIGINAL_TIMING_WITH_NOTE"
        elif statuses and all(s == "PASS_ORIGINAL_TIMING" for s in statuses):
            status = "PASS_ORIGINAL_TIMING"
        else:
            status = "PASS"

        frame_counts = [frames_written(r.get("metadata"), r["probe"]) for r in group]
        durations = [duration_seconds(r.get("metadata"), r["probe"]) for r in group]
        measured_fps_values = [measured_camera_fps(r.get("metadata")) for r in group if measured_camera_fps(r.get("metadata")) > 0]
        wall_durations = [wall_clock_duration(r.get("metadata"), r["probe"]) for r in group if wall_clock_duration(r.get("metadata"), r["probe"]) > 0]
        container_vs_wall_values = [
            abs(_as_float(_meta_value(r.get("metadata") or {}, "ContainerVsWallClockDifferenceSeconds", "TimestampDriftSeconds", default=0)))
            for r in group
        ]
        start_offsets = [_as_float(_meta_value(r.get("metadata") or {}, "InterCameraStartOffsetMs", default=0)) for r in group]
        end_offsets = [_as_float(_meta_value(r.get("metadata") or {}, "InterCameraStopOffsetMs", default=0)) for r in group]
        resolutions = []
        for r in group:
            p = r["probe"]
            if "error" not in p and p.get("width") and p.get("height"):
                resolutions.append(f"{p.get('width')}x{p.get('height')}")
        resolutions = sorted(set(resolutions))

        def best_key(r: dict):
            rank = {"Excellent": 0, "Good": 1, "Warning": 2, "Poor": 3}.get(
                camera_stability(r.get("metadata"), r["probe"]), 4
            )
            meta = r.get("metadata") or {}
            writer_fps = _as_float(_meta_value(meta, "WriterFps", "RecordingWriterFps", "SelectedDeviceFps", default=30))
            measured_fps = _as_float(_meta_value(meta, "MeasuredCameraFps", "MeasuredWriterFps", default=0))
            return (rank, duplicate_frames(meta), abs(writer_fps - measured_fps), r["camera"])

        best = sorted(group, key=best_key)[0]
        correction_cameras = sorted(r["camera"] for r in group if duplicate_frames(r.get("metadata")) > 0)
        summaries.append({
            "session": session,
            "resolution": ", ".join(resolutions) if resolutions else "-",
            "duration": max(durations) if durations else 0,
            "camera_count": len(group),
            "status": status,
            "session_scientific_timing_confidence": session_scientific_timing_confidence(group),
            "max_duplicates": max((duplicate_frames(r.get("metadata")) for r in group), default=0),
            "total_duplicates": total_duplicates,
            "queue_total": queue_total,
            "placeholder_total": placeholder_total,
            "visual_near_identical_frames": visual_near_identical_total,
            "frame_min": min(frame_counts) if frame_counts else 0,
            "frame_max": max(frame_counts) if frame_counts else 0,
            "session_timing_mode": ORIGINAL_CAPTURE_MODE if original_session else LEGACY_CONSTANT_FRAME_COUNT_MODE if total_duplicates > 0 else "-",
            "max_measured_fps_difference": (max(measured_fps_values) - min(measured_fps_values)) if len(measured_fps_values) >= 2 else 0,
            "max_wall_clock_duration_difference_sec": (max(wall_durations) - min(wall_durations)) if len(wall_durations) >= 2 else 0,
            "max_start_offset_sec": max(start_offsets) / 1000.0 if start_offsets else 0,
            "max_end_offset_sec": max(end_offsets) / 1000.0 if end_offsets else 0,
            "max_container_vs_wall_clock_difference_sec": max(container_vs_wall_values) if container_vs_wall_values else 0,
            "max_frame_count_difference": (max(frame_counts) - min(frame_counts)) if len(frame_counts) >= 2 else 0,
            "frame_count_difference_accepted": bool(original_session),
            "best_camera": best["camera"],
            "correction_cameras": ", ".join(correction_cameras) if correction_cameras else "None",
            "message": _session_message(status, total_duplicates, queue_total, placeholder_total),
        })
    return summaries


def _timestamp_csv_path(meta: dict, cam_dir: Path) -> Path:
    explicit = str(meta.get("FrameTimestampCsvPath") or meta.get("frameTimestampCsvPath") or "").strip()
    if explicit:
        path = Path(explicit)
        return path if path.is_absolute() else cam_dir / path
    return cam_dir / f"{cam_dir.name}_frame_timestamps.csv"


def _timestamp_csv_data_rows(path: Path) -> int:
    try:
        with path.open("r", encoding="utf-8-sig", newline="") as fh:
            return max(0, sum(1 for _ in fh) - 1)
    except OSError:
        return -1


def assess(meta: dict | None, probe_data: dict, cam_dir: Path | None = None) -> list[str]:
    issues: list[str] = []
    warnings: list[str] = []
    info: list[str] = []
    if "error" in probe_data:
        return [f"FAIL: ffprobe error: {probe_data['error']}"]

    if meta:
        exp_w = meta.get("Width")
        exp_h = meta.get("Height")
        if probe_data.get("width") != exp_w or probe_data.get("height") != exp_h:
            issues.append(
                f"Resolution mismatch: probe {probe_data.get('width')}x{probe_data.get('height')} "
                f"vs metadata {exp_w}x{exp_h}"
            )

        frames_written = _meta_int(meta, "FramesWritten", "FrameCount", default=0)
        frames_captured = _meta_value(meta, "FramesCaptured", default=frames_written)
        frames_captured_int = _as_float(frames_captured)

        queue_drops = _meta_int(meta, "WriterQueueDrops")
        duplicate_frames = _meta_int(meta, "DuplicateFrames") + _meta_int(meta, "DuplicatedFrames")
        placeholder_frames = _meta_int(meta, "PlaceholderFrames")
        constant_frame_count = _meta_bool(meta, "ConstantFrameCountMode", "constantFrameCountMode")
        original_capture = original_capture_mode(meta)
        strict_original_mode = (
            recording_timing_mode(meta) == ORIGINAL_CAPTURE_MODE
            and _meta_bool(meta, "OriginalCaptureMode", "originalCaptureMode")
            and not constant_frame_count
        )

        if original_capture and not strict_original_mode:
            issues.append(
                "Original Capture metadata mode mismatch: "
                f"recordingTimingMode={recording_timing_mode(meta)}, originalCaptureMode={_meta_bool(meta, 'OriginalCaptureMode', 'originalCaptureMode')}, "
                f"constantFrameCountMode={constant_frame_count}"
            )

        if frames_captured_int != frames_written:
            msg = f"Frames captured ({frames_captured}) differs from frames written ({frames_written})"
            if original_capture and frames_written < frames_captured_int:
                issues.append(msg)
            else:
                warnings.append(msg)

        if queue_drops:
            issues.append(
                "Recording integrity issue: "
                f"queue drops={queue_drops}, duplicates={duplicate_frames}, "
                f"placeholders={placeholder_frames}"
            )
        elif original_capture and duplicate_frames:
            issues.append(
                "Original Capture Mode expected duplicateFrames=0: "
                f"duplicates={duplicate_frames}, placeholders={placeholder_frames}"
            )
        elif original_capture and placeholder_frames:
            issues.append(
                "Original Capture Mode expected placeholderFrames=0: "
                f"placeholders={placeholder_frames}"
            )
        elif placeholder_frames:
            issues.append(f"Recording integrity issue: placeholders={placeholder_frames}")
        elif (duplicate_frames or placeholder_frames) and not constant_frame_count:
            issues.append(
                "Unexpected duplicate/placeholder frames: "
                f"duplicates={duplicate_frames}, placeholders={placeholder_frames}"
            )
        elif duplicate_frames or placeholder_frames:
            warnings.append(
                "Constant frame count sync inserted duplicate frames: "
                f"duplicates={duplicate_frames}, placeholders={placeholder_frames}"
            )

        writer_fps = _meta_value(meta, "WriterFps", "RecordingWriterFps", "SelectedDeviceFps")
        container_fps = _meta_value(meta, "ContainerFps", "RecordingWriterFps", default=probe_data.get("avg_fps"))
        measured_camera_fps_value = _meta_value(meta, "MeasuredCameraFps", "MeasuredWriterFps")
        wall_duration = _meta_value(
            meta, "WallClockDurationSeconds", "WallDurationSeconds", "DurationSeconds"
        )
        frame_based_duration = _meta_value(meta, "FrameBasedDurationSeconds")
        container_duration = probe_data.get("duration_sec") or _meta_value(meta, "ContainerDurationSeconds")
        container_vs_wall = _meta_value(
            meta, "ContainerVsWallClockDifferenceSeconds", "TimestampDriftSeconds",
            default=(container_duration - wall_duration if container_duration and wall_duration else 0),
        )

        probe_data["timing_summary"] = {
            "wall_clock_duration_sec": wall_duration,
            "frame_based_duration_sec": frame_based_duration,
            "ffprobe_container_duration_sec": container_duration,
            "container_vs_wall_clock_difference_sec": container_vs_wall,
            "writer_fps": writer_fps,
            "container_fps": container_fps,
            "measured_camera_fps": measured_camera_fps_value,
        }

        trim_warning = str(
            _meta_value(meta, "TrimWarning", "trimWarning", default="")
            or ""
        ).strip()
        if (
            not trim_warning
            and original_capture
            and wall_duration
            and container_duration
            and abs(container_duration - wall_duration) > 0.5
        ):
            trim_warning = TIMESTAMP_TRIM_WARNING
        if trim_warning:
            info.append(f"Trim warning: {trim_warning}")

        if writer_fps and probe_data.get("avg_fps") and abs(probe_data["avg_fps"] - writer_fps) > 0.1:
            info.append(
                f"Playback FPS tag {probe_data['avg_fps']} matches writer FPS {writer_fps:.3f}"
            )

        stable_fps = measured_fps_is_stable(meta)
        writer_measured_delta = abs(_as_float(writer_fps) - _as_float(measured_camera_fps_value)) if writer_fps and measured_camera_fps_value else 0
        container_measured_delta = abs(_as_float(container_fps) - _as_float(measured_camera_fps_value)) if container_fps and measured_camera_fps_value else 0
        requested_fps = _as_float(_meta_value(meta, "RequestedFps", "requestedFps", default=0))
        requested_measured_delta = abs(requested_fps - _as_float(measured_camera_fps_value)) if requested_fps and measured_camera_fps_value else 0

        if original_capture and stable_fps and measured_camera_fps_value and (
            (writer_fps and writer_measured_delta > 0.05)
            or (container_fps and container_measured_delta > 0.05)
            or (requested_fps and requested_measured_delta > 0.05)
        ):
            info.append(STABLE_DIFFERENT_FPS_NOTE)
            info.append("Timing note: Use timestamp CSV for timing-sensitive analysis.")
        elif measured_camera_fps_value and writer_fps and writer_measured_delta > FPS_DIFF_ACCEPTABLE:
            warnings.append(
                f"Playback/writer FPS ({writer_fps:.3f}) differs from Real Capture FPS "
                f"({measured_camera_fps_value:.3f})"
            )

        interval_std = _as_float(_meta_value(meta, "CaptureIntervalStdMs", "CaptureJitterMs", default=0))
        interval_p99 = _as_float(_meta_value(meta, "CaptureIntervalP99Ms", "captureIntervalP99Ms", default=0))
        expected_interval = _as_float(_meta_value(meta, "ExpectedIntervalMs", "expectedIntervalMs", default=0))
        long_gaps = _meta_int(meta, "LongGapCount", "longGapCount")
        severe_long_gaps = _meta_int(meta, "SevereLongGapCount", "severeLongGapCount")
        grade = fps_stability_grade(meta)
        max_no_frame = _meta_int(meta, "MaxConsecutiveNoFrame")
        if original_capture and grade.lower() == "failed":
            issues.append("FPS stability grade is Failed.")
        elif original_capture and (
            grade.lower() in ("borderline", "unstable")
            or long_gaps > 0
            or (expected_interval > 0 and interval_p99 > expected_interval * 1.75)
            or interval_std > UNSTABLE_CAPTURE_INTERVAL_STD_MS
            or max_no_frame > UNSTABLE_MAX_CONSECUTIVE_NO_FRAME
        ):
            warnings.append(
                f"Original Capture interval stability warning: grade={grade or '-'}, std={interval_std:.3f}ms, "
                f"p99={interval_p99:.3f}ms, longGaps={long_gaps}, severeLongGaps={severe_long_gaps}, "
                f"maxConsecutiveNoFrame={max_no_frame}"
            )

        if wall_duration and container_duration and abs(container_duration - wall_duration) > 5:
            info.append(
                f"Timing note: container duration ({container_duration:.3f}s) is frame-based; "
                f"wall-clock recording duration was {wall_duration:.3f}s "
                f"(difference {container_vs_wall:.3f}s). Use timestamp CSV for timing-sensitive analysis."
            )

        if frames_written and probe_data.get("frame_count") is not None:
            delta = probe_data["frame_count"] - frames_written
            if delta != 0:
                warnings.append(
                    f"Frame count delta: ffprobe {probe_data['frame_count']} vs metadata {frames_written} ({delta:+d})"
                )

        if original_capture and frames_written and cam_dir is not None:
            timestamp_csv_written = _meta_bool(meta, "FrameTimestampCsvWritten", "frameTimestampCsvWritten")
            timestamp_csv_rows = _meta_int(meta, "FrameTimestampCsvRowCount", "frameTimestampCsvRowCount", default=0)
            timestamp_csv = _timestamp_csv_path(meta, cam_dir)
            actual_rows = _timestamp_csv_data_rows(timestamp_csv)
            if not timestamp_csv_written or actual_rows < 0 or timestamp_csv_rows != frames_written or actual_rows != frames_written:
                issues.append(
                    "Frame timestamp CSV metadata incomplete: "
                    f"written={timestamp_csv_written}, metadataRows={timestamp_csv_rows}, "
                    f"actualRows={actual_rows if actual_rows >= 0 else 'missing'}, frames={frames_written}, "
                    f"path={timestamp_csv}"
                )

            missing_timing = original_capture_timing_fields_missing(meta)
            if missing_timing:
                issues.append("Metadata missing critical timing fields: " + ", ".join(missing_timing))

            if not stable_fps:
                warnings.append(
                    f"Measured FPS stability is not clearly stable: grade={grade or '-'}, std={interval_std:.3f}ms, "
                    f"p99={interval_p99:.3f}ms, longGaps={long_gaps}, severeLongGaps={severe_long_gaps}"
                )
            container_close = abs(_as_float(container_vs_wall)) <= WALL_DURATION_DIFF_ACCEPTABLE_SEC
            fps_close = (
                measured_camera_fps_value > 0
                and (
                    (writer_fps and writer_measured_delta <= FPS_DIFF_ACCEPTABLE)
                    or (container_fps and container_measured_delta <= FPS_DIFF_ACCEPTABLE)
                )
            )
            if stable_fps and not (container_close or fps_close):
                warnings.append(
                    "Original Capture Mode timing cross-check warning: Container duration differs from wall-clock time. "
                    "Use timestamp CSV for scientific trimming and analysis."
                )

            info.append(ORIGINAL_CAPTURE_REAL_FRAMES_MESSAGE)
            info.append(ORIGINAL_CAPTURE_FRAME_DIFF_MESSAGE)
            info.append(ORIGINAL_CAPTURE_ANALYSIS_MESSAGE)
            info.append(ORIGINAL_CAPTURE_CONTAINER_DURATION_MESSAGE)

        timing_message = meta.get("ScientificTimingMessage")
        timing_status = str(meta.get("ScientificTimingStatus") or "").strip().upper()
        if timing_status == "FAIL":
            issues.append(
                "Metadata scientific timing status: FAIL"
                + (f" - {timing_message}" if timing_message else "")
            )
        elif timing_status == "PASS_WITH_WARNING":
            warnings.append(
                "Metadata scientific timing status: PASS_WITH_WARNING"
                + (f" - {timing_message}" if timing_message else "")
            )
        elif timing_status.startswith("PASS_ORIGINAL_TIMING"):
            info.append(
                f"Metadata scientific timing status: {timing_status}"
                + (f" - {timing_message}" if timing_message else "")
            )
        elif timing_status:
            info.append(f"Metadata scientific timing status: {timing_status}")
        if timing_message:
            info.append(f"Metadata scientific timing message: {timing_message}")

        if probe_data.get("constant_fps") is False:
            warnings.append(
                f"Non-constant FPS tag: avg={probe_data.get('avg_frame_rate')} raw={probe_data.get('r_frame_rate')}"
            )

    if probe_data.get("codec") not in ("mpeg4", "h264", "hevc"):
        warnings.append(f"Unexpected codec: {probe_data.get('codec')}")

    status = "PASS"
    if issues:
        status = "FAIL"
    elif warnings:
        status = "PASS_WITH_WARNING"
    elif meta and original_capture_mode(meta):
        measured = measured_camera_fps(meta)
        writer_fps = writer_or_container_fps(meta, probe_data)
        requested_fps = _as_float(_meta_value(meta, "RequestedFps", "requestedFps", default=0))
        differs = (
            measured > 0
            and (
                (writer_fps > 0 and abs(writer_fps - measured) > 0.05)
                or (requested_fps > 0 and abs(requested_fps - measured) > 0.05)
            )
        )
        status = "PASS_ORIGINAL_TIMING_WITH_NOTE" if differs else "PASS_ORIGINAL_TIMING"
    elif meta and str(meta.get("ScientificTimingStatus") or "").strip().upper().startswith("PASS_ORIGINAL_TIMING"):
        status = str(meta.get("ScientificTimingStatus")).strip().upper()
    return [status] + issues + warnings + info


def format_report(root: Path, results: list[dict]) -> str:
    visual_check_enabled = any((r.get("visual") or {}).get("enabled") for r in results)
    lines = [
        "MultiCamApp Video Audit Report",
        f"Folder: {root}",
        f"Generated: {datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S UTC')}",
        f"Videos audited: {len(results)}",
        f"enableVisualNearDuplicateCheck: {str(visual_check_enabled).lower()}",
        "",
    ]

    pass_n = sum(1 for r in results if r["status"] == "PASS")
    original_pass_n = sum(1 for r in results if r["status"] == "PASS_ORIGINAL_TIMING")
    original_note_n = sum(1 for r in results if r["status"] == "PASS_ORIGINAL_TIMING_WITH_NOTE")
    warn_n = sum(1 for r in results if r["status"] == "PASS_WITH_WARNING")
    fail_n = sum(1 for r in results if r["status"] == "FAIL")
    metas = [r.get("metadata") for r in results if r.get("metadata")]
    total_original_frames = sum(
        frames_written(r.get("metadata"), r["probe"])
        for r in results
        if original_capture_mode(r.get("metadata"))
    )
    total_duplicate_frames = sum(duplicate_frames(m) for m in metas)
    total_app_created_duplicate_frames = sum(app_created_duplicate_frames(m) for m in metas)
    total_placeholder_frames = sum(_meta_int(m, "PlaceholderFrames") for m in metas)
    total_app_created_placeholder_frames = sum(app_created_placeholder_frames(m) for m in metas)
    total_writer_queue_drops = sum(_meta_int(m, "WriterQueueDrops") for m in metas)
    timestamp_presence = [_meta_bool(m, "FrameTimestampCsvWritten", "frameTimestampCsvWritten") for m in metas]
    timestamp_rows_match = [
        _meta_int(m, "FrameTimestampCsvRowCount", "frameTimestampCsvRowCount") == _meta_int(m, "FramesWritten", "FrameCount")
        for m in metas
    ]
    measured_values = [measured_camera_fps(m) for m in metas if measured_camera_fps(m) > 0]
    start_offsets = [_as_float(_meta_value(m, "InterCameraStartOffsetMs", default=0)) for m in metas]
    end_offsets = [_as_float(_meta_value(m, "InterCameraStopOffsetMs", default=0)) for m in metas]
    wall_durations = [_as_float(_meta_value(m, "WallClockDurationSeconds", "WallDurationSeconds", default=0)) for m in metas]
    wall_durations = [v for v in wall_durations if v > 0]
    container_vs_wall = [
        abs(_as_float(_meta_value(m, "ContainerVsWallClockDifferenceSeconds", "TimestampDriftSeconds", default=0)))
        for m in metas
    ]
    max_measured_fps_difference = max(measured_values) - min(measured_values) if len(measured_values) >= 2 else 0
    max_start_offset_ms = max(start_offsets) if start_offsets else 0
    max_end_offset_ms = max(end_offsets) if end_offsets else 0
    max_wall_clock_duration_difference = max(wall_durations) - min(wall_durations) if len(wall_durations) >= 2 else 0
    max_container_vs_wall = max(container_vs_wall) if container_vs_wall else 0
    all_csv_present = all(timestamp_presence) if timestamp_presence else False
    all_csv_rows_match = all(timestamp_rows_match) if timestamp_rows_match else False
    session_confidences = [session_scientific_timing_confidence([r for r in results if r["session"] == session])
                           for session in sorted({r["session"] for r in results})]
    if fail_n > 0 or any(c == "FAILED" for c in session_confidences):
        confidence = "FAILED"
    elif any(c == "LOW" for c in session_confidences):
        confidence = "LOW"
    elif warn_n > 0 or any(c == "MEDIUM" for c in session_confidences):
        confidence = "MEDIUM"
    else:
        confidence = "HIGH"
    lines += [
        "Summary",
        f"  PASS: {pass_n}",
        f"  PASS_ORIGINAL_TIMING: {original_pass_n}",
        f"  PASS_ORIGINAL_TIMING_WITH_NOTE: {original_note_n}",
        f"  PASS_WITH_WARNING: {warn_n}",
        f"  FAIL: {fail_n}",
        f"  totalOriginalFrames: {total_original_frames}",
        f"  totalDuplicateFrames: {total_duplicate_frames}",
        f"  totalAppCreatedDuplicateFrames: {total_app_created_duplicate_frames}",
        f"  totalPlaceholderFrames: {total_placeholder_frames}",
        f"  totalAppCreatedPlaceholderFrames: {total_app_created_placeholder_frames}",
        f"  totalWriterQueueDrops: {total_writer_queue_drops}",
        f"  allFrameTimestampCsvPresent: {str(all_csv_present).lower()}",
        f"  allFrameTimestampRowsMatchFramesWritten: {str(all_csv_rows_match).lower()}",
        f"  maxMeasuredFpsDifference: {max_measured_fps_difference:.6f}",
        f"  maxStartOffsetMs: {max_start_offset_ms:.3f}",
        f"  maxEndOffsetMs: {max_end_offset_ms:.3f}",
        f"  maxWallClockDurationDifferenceSec: {max_wall_clock_duration_difference:.6f}",
        f"  maxContainerVsWallClockDifferenceSec: {max_container_vs_wall:.6f}",
        f"  scientificTimingConfidence: {confidence}",
        "",
        "Session summaries",
    ]
    for s in session_summaries(results):
        lines += [
            f"  Session name: {s['session']}",
            f"    Resolution: {s['resolution']}",
            f"    Approx duration: {s['duration']:.3f}s",
            f"    Number of cameras: {s['camera_count']}",
            f"    sessionTimingMode: {s['session_timing_mode']}",
            f"    Scientific status: {s['status']}",
            f"    Scientific Timing Confidence: {s['session_scientific_timing_confidence']}",
            f"    maxMeasuredFpsDifference: {s['max_measured_fps_difference']:.3f}",
            f"    maxWallClockDurationDifferenceSec: {s['max_wall_clock_duration_difference_sec']:.3f}",
            f"    maxStartOffsetSec: {s['max_start_offset_sec']:.3f}",
            f"    maxEndOffsetSec: {s['max_end_offset_sec']:.3f}",
            f"    maxContainerVsWallClockDifferenceSec: {s['max_container_vs_wall_clock_difference_sec']:.3f}",
            f"    maxFrameCountDifference: {s['max_frame_count_difference']}",
            f"    frameCountDifferenceAcceptedBecauseOriginalMode: {str(s['frame_count_difference_accepted']).lower()}",
            f"    Max duplicate frames: {s['max_duplicates']}",
            f"    Total duplicate frames: {s['total_duplicates']}",
            f"    Visual near-identical frames: {s['visual_near_identical_frames']}",
            f"    Queue drops total: {s['queue_total']}",
            f"    Placeholder total: {s['placeholder_total']}",
            f"    Frame count range: {s['frame_min']} - {s['frame_max']}",
            f"    Best/stablest camera: {s['best_camera']}",
            f"    Cameras needing duplicate-frame correction: {s['correction_cameras']}",
            f"    {s['message']}",
            "    Real frames only; no duplicates/placeholders. Use timestamp CSV for timing-sensitive analysis."
            if s["session_scientific_timing_confidence"] == "HIGH" else "",
            f"    {ORIGINAL_CAPTURE_SESSION_NOTE}" if s["frame_count_difference_accepted"] else "",
            "",
        ]
    lines += [
        "=" * 80,
        "",
    ]

    for r in results:
        lines.append(f"## {r['session']} / {r['camera']} - {r['status']}")
        lines.append(f"File: {r['file']}")
        p = r["probe"]
        if "error" in p:
            lines.append(f"  ERROR: {p['error']}")
        else:
            lines.append(f"  Codec: {p.get('codec')} ({p.get('codec_long', '')})")
            lines.append(f"  Resolution: {p.get('width')}x{p.get('height')}  pix_fmt={p.get('pix_fmt')}")
            lines.append(
                f"  FPS tags: avg={p.get('avg_frame_rate')} ({p.get('avg_fps')})  "
                f"raw={p.get('r_frame_rate')} ({p.get('raw_fps')})  constant={p.get('constant_fps')}"
            )
            lines.append(
                f"  Frames (ffprobe): {p.get('frame_count')}  ffprobe container duration: {p.get('duration_sec')}s"
            )
            lines.append(f"  Size: {p.get('size_gb')} GB  Bitrate: {p.get('bitrate_mbps')} Mbps")
            timing = p.get("timing_summary")
            if timing:
                lines.append("  Timing breakdown:")
                lines.append(f"    Wall-clock duration (metadata): {timing.get('wall_clock_duration_sec')}s")
                lines.append(f"    Frame-based duration (metadata): {timing.get('frame_based_duration_sec')}s")
                lines.append(f"    ffprobe/container duration: {timing.get('ffprobe_container_duration_sec')}s")
                lines.append(
                    f"    Container vs wall-clock difference: {timing.get('container_vs_wall_clock_difference_sec')}s"
                )
                lines.append(
                    f"    FPS writer/container/measured-camera: "
                    f"{timing.get('writer_fps')} / {timing.get('container_fps')} / {timing.get('measured_camera_fps')}"
                )

        m = r.get("metadata")
        visual = r.get("visual") or {}
        if visual.get("enabled"):
            lines.append("  Visual Near-Duplicate Check:")
            if visual.get("error"):
                lines.append(f"    Error: {visual.get('error')}")
            else:
                lines.append(f"    Frames sampled: {visual.get('framesSampled')}")
                lines.append(f"    meanAbsolutePixelDifference: {visual.get('meanAbsolutePixelDifference')}")
                lines.append(f"    grayscaleDifferenceMean: {visual.get('grayscaleDifferenceMean')}")
                lines.append(f"    perceptualHashDifference: {visual.get('perceptualHashDifference')}")
                lines.append(f"    visualNearIdenticalFrames: {visual.get('nearIdenticalConsecutiveFrameCount')}")
                lines.append(f"    visualNearIdenticalFrameRate: {visual.get('nearIdenticalConsecutiveFrameRate')}")
                lines.append(f"    exactConsecutiveDuplicateFrames: {visual.get('exactConsecutiveDuplicateCount')}")
                lines.append(f"    longestExactDuplicateRun: {visual.get('longestExactDuplicateRun')}")
                lines.append(f"    longestNearIdenticalRun: {visual.get('longestNearIdenticalRun')}")
                app_duplicates = app_created_duplicate_frames(m)
                if int(visual.get("nearIdenticalConsecutiveFrameCount") or 0) > 0 and app_duplicates == 0:
                    lines.append(
                        "    Near-identical visual frames detected. This may be normal in static scenes or low movement. "
                        "Metadata shows no app-created duplicate frames."
                    )
        if m:
            lines.append("  Metadata:")
            lines.append("    Timing Integrity:")
            lines.append(
                f"      Session start local/UTC/mono: "
                f"{_meta_value(m, 'SessionStartLocalTime', 'sessionStartLocalTime', default='')} / "
                f"{_meta_value(m, 'SessionStartUtcTime', 'sessionStartUtcTime', default='')} / "
                f"{_meta_value(m, 'SessionStartMonotonicSec', 'sessionStartMonotonicSec', default='')}"
            )
            lines.append(
                f"      Session stop local/UTC/mono: "
                f"{_meta_value(m, 'SessionStopLocalTime', 'sessionStopLocalTime', default='')} / "
                f"{_meta_value(m, 'SessionStopUtcTime', 'sessionStopUtcTime', default='')} / "
                f"{_meta_value(m, 'SessionStopMonotonicSec', 'sessionStopMonotonicSec', default='')}"
            )
            lines.append(
                f"      Session duration wall/monotonic sec: "
                f"{_meta_value(m, 'SessionWallClockDurationSec', 'sessionWallClockDurationSec', default='')} / "
                f"{_meta_value(m, 'SessionMonotonicDurationSec', 'sessionMonotonicDurationSec', default='')}"
            )
            lines.append(
                f"      Requested start local/UTC/mono: "
                f"{_meta_value(m, 'RecordingRequestedStartLocalTime', 'recordingRequestedStartLocalTime', default='')} / "
                f"{_meta_value(m, 'RecordingRequestedStartUtcTime', 'recordingRequestedStartUtcTime', default='')} / "
                f"{_meta_value(m, 'RecordingRequestedStartMonotonicSec', 'recordingRequestedStartMonotonicSec', default='')}"
            )
            lines.append(
                f"      Camera recording start local/UTC/mono: "
                f"{_meta_value(m, 'CameraRecordingStartLocalTime', 'cameraRecordingStartLocalTime', default='')} / "
                f"{_meta_value(m, 'CameraRecordingStartUtcTime', 'cameraRecordingStartUtcTime', default='')} / "
                f"{_meta_value(m, 'CameraRecordingStartMonotonicSec', 'cameraRecordingStartMonotonicSec', default='')}"
            )
            lines.append(
                f"      First frame local/UTC/mono: "
                f"{_meta_value(m, 'FirstFrameLocalTime', 'firstFrameLocalTime', default='')} / "
                f"{_meta_value(m, 'FirstFrameUtcTime', 'firstFrameUtcTime', default='')} / "
                f"{_meta_value(m, 'FirstFrameMonotonicSec', 'firstFrameMonotonicSec', default='')}"
            )
            lines.append(
                f"      Last frame local/UTC/mono: "
                f"{_meta_value(m, 'LastFrameLocalTime', 'lastFrameLocalTime', default='')} / "
                f"{_meta_value(m, 'LastFrameUtcTime', 'lastFrameUtcTime', default='')} / "
                f"{_meta_value(m, 'LastFrameMonotonicSec', 'lastFrameMonotonicSec', default='')}"
            )
            lines.append(
                f"      Camera stop local/UTC/mono: "
                f"{_meta_value(m, 'CameraRecordingStopLocalTime', 'cameraRecordingStopLocalTime', default='')} / "
                f"{_meta_value(m, 'CameraRecordingStopUtcTime', 'cameraRecordingStopUtcTime', default='')} / "
                f"{_meta_value(m, 'CameraRecordingStopMonotonicSec', 'cameraRecordingStopMonotonicSec', default='')}"
            )
            lines.append(
                f"      Writer closed local/UTC/mono: "
                f"{_meta_value(m, 'WriterClosedLocalTime', 'writerClosedLocalTime', default='')} / "
                f"{_meta_value(m, 'WriterClosedUtcTime', 'writerClosedUtcTime', default='')} / "
                f"{_meta_value(m, 'WriterClosedMonotonicSec', 'writerClosedMonotonicSec', default='')}"
            )
            lines.append("      Scientific duration source: Timestamp CSV and monotonic first/last frame timing; MP4 container duration is informational in Original Capture Mode.")
            requested_res = m.get("RequestedResolution") or m.get("Resolution") or "-"
            selected_res = m.get("SelectedResolution") or m.get("Resolution") or "-"
            meta_res = f"{m.get('Width', m.get('PixelWidth', '-'))}x{m.get('Height', m.get('PixelHeight', '-'))}"
            probe_res = f"{p.get('width')}x{p.get('height')}" if "error" not in p else "-"
            lines.append(
                f"    UI requested/selected/metadata/probe resolution: "
                f"{requested_res} / {selected_res} / {meta_res} / {probe_res}"
            )
            lines.append(
                f"    Requested/Selected/Writer/Playback FPS: {m.get('RequestedFps')} / "
                f"{m.get('SelectedFps', m.get('SelectedDeviceFps'))} / "
                f"{m.get('WriterFps', m.get('RecordingWriterFps'))} / "
                f"{m.get('ContainerFps', m.get('RecordingWriterFps'))}"
            )
            lines.append(
                f"    requestedFps / writerFps / containerFps: "
                f"{m.get('RequestedFps')} / {m.get('WriterFps', m.get('RecordingWriterFps'))} / "
                f"{m.get('ContainerFps', m.get('RecordingWriterFps'))}"
            )
            lines.append(
                f"    Measured camera FPS: {m.get('MeasuredCameraFps', m.get('MeasuredWriterFps'))}"
            )
            lines.append(
                f"    Real Capture FPS / scientific timing FPS: "
                f"{m.get('MeasuredCameraFps', m.get('MeasuredWriterFps'))} / "
                f"{m.get('MeasuredCameraFps', m.get('MeasuredWriterFps'))}"
            )
            lines.append(
                f"    Playback FPS: "
                f"{m.get('ContainerFps', m.get('RecordingWriterFps'))}"
            )
            lines.append("    Scientific timing source: Timestamp CSV")
            lines.append(f"    framesCaptured / framesWritten: {m.get('FramesCaptured')} / {m.get('FramesWritten')}")
            interval_count = m.get("CaptureIntervalCount", 0) or 0
            lines.append(f"    Capture interval count: {interval_count if interval_count >= 1 else 'Unavailable'}")
            lines.append(
                f"    capture interval mean/min/max/std ms: "
                f"{_format_capture_interval_ms(m, 'CaptureIntervalMeanMs', 'AverageCaptureIntervalMs')} / "
                f"{_format_capture_interval_ms(m, 'CaptureIntervalMinMs', 'MinCaptureIntervalMs')} / "
                f"{_format_capture_interval_ms(m, 'CaptureIntervalMaxMs', 'MaxCaptureIntervalMs')} / "
                f"{_format_capture_interval_ms(m, 'CaptureIntervalStdMs', 'CaptureJitterMs')}"
            )
            lines.append(
                f"    FPS stability grade: {_meta_value(m, 'FpsStabilityGrade', 'fpsStabilityGrade', default='')}"
            )
            lines.append(
                f"    measuredCameraFpsFromFirstLastFrame / meanInterval: "
                f"{_meta_value(m, 'MeasuredCameraFpsFromFirstLastFrame', 'measuredCameraFpsFromFirstLastFrame', default='')} / "
                f"{_meta_value(m, 'MeasuredCameraFpsFromMeanInterval', 'measuredCameraFpsFromMeanInterval', default='')}"
            )
            lines.append(
                f"    expectedIntervalMs / requestedExpectedIntervalMs: "
                f"{_meta_value(m, 'ExpectedIntervalMs', 'expectedIntervalMs', default='')} / "
                f"{_meta_value(m, 'RequestedExpectedIntervalMs', 'requestedExpectedIntervalMs', default='')}"
            )
            lines.append(
                f"    interval median/p95/p99 ms: "
                f"{_meta_value(m, 'CaptureIntervalMedianMs', 'captureIntervalMedianMs', default='')} / "
                f"{_meta_value(m, 'CaptureIntervalP95Ms', 'captureIntervalP95Ms', default='')} / "
                f"{_meta_value(m, 'CaptureIntervalP99Ms', 'captureIntervalP99Ms', default='')}"
            )
            lines.append(
                f"    interval error mean/absMean ms: "
                f"{_meta_value(m, 'MeanIntervalErrorMs', 'meanIntervalErrorMs', default='')} / "
                f"{_meta_value(m, 'AbsoluteMeanIntervalErrorMs', 'absoluteMeanIntervalErrorMs', default='')}"
            )
            lines.append(
                f"    long/short/severeLong gaps: "
                f"{_meta_value(m, 'LongGapCount', 'longGapCount', default='')} / "
                f"{_meta_value(m, 'ShortGapCount', 'shortGapCount', default='')} / "
                f"{_meta_value(m, 'SevereLongGapCount', 'severeLongGapCount', default='')}"
            )
            lines.append(f"    jitterScoreMs: {_meta_value(m, 'JitterScoreMs', 'jitterScoreMs', default='')}")
            duplicates = _meta_int(m, 'DuplicateFrames') + _meta_int(m, 'DuplicatedFrames')
            lines.append(f"    duplicateFrames / placeholderFrames / writerQueueDrops: {duplicates} / {m.get('PlaceholderFrames')} / {m.get('WriterQueueDrops')}")
            lines.append(
                f"    appCreatedDuplicateFrames / appCreatedPlaceholderFrames / visualNearIdenticalFrames: "
                f"{app_created_duplicate_frames(m)} / {app_created_placeholder_frames(m)} / "
                f"{(r.get('visual') or {}).get('nearIdenticalConsecutiveFrameCount', '')}"
            )
            lines.append(
                f"    Duplicate rate: {duplicate_rate_per_minute(m, p):.1f}/min, "
                f"{duplicate_percentage(m, p):.1f}% of written frames"
            )
            lines.append(f"    Camera stability: {camera_stability(m, p)}")
            lines.append(f"    recommendedAction: {recommended_preset(m, p)}")
            mode = recording_timing_mode(m)
            lines.append(f"    recordingTimingMode: {mode}")
            if original_capture_mode(m):
                lines.append("    Original Capture Mode: Real frames only; no duplicates/placeholders. Frame counts may differ because cameras delivered real frames at different measured FPS.")
            else:
                lines.append(f"    Constant frame count mode: {m.get('ConstantFrameCountMode')}")
            lines.append(
                f"    frameTimestampCsv written/rows/path: "
                f"{_meta_value(m, 'FrameTimestampCsvWritten', 'frameTimestampCsvWritten', default='')} / "
                f"{_meta_value(m, 'FrameTimestampCsvRowCount', 'frameTimestampCsvRowCount', default='')} / "
                f"{_meta_value(m, 'FrameTimestampCsvPath', 'frameTimestampCsvPath', default='')}"
            )
            trim_warning = _meta_value(m, "TrimWarning", "trimWarning", default="")
            container_vs_wall = _as_float(_meta_value(m, "ContainerVsWallClockDifferenceSeconds", "TimestampDriftSeconds", default=0))
            if not trim_warning and original_capture_mode(m) and abs(container_vs_wall) > 0.5:
                trim_warning = TIMESTAMP_TRIM_WARNING
            lines.append(
                f"    trimRecommendedTimeSource: "
                f"{_meta_value(m, 'TrimRecommendedTimeSource', 'trimRecommendedTimeSource', default='')}"
            )
            lines.append(
                f"    supportsTimestampBasedTrimming: "
                f"{_meta_value(m, 'SupportsTimestampBasedTrimming', 'supportsTimestampBasedTrimming', default='')}"
            )
            lines.append(
                f"    scientificTrimStartReference: "
                f"{_meta_value(m, 'ScientificTrimStartReference', 'scientificTrimStartReference', default='')}"
            )
            lines.append(
                f"    scientificTrimEndReference: "
                f"{_meta_value(m, 'ScientificTrimEndReference', 'scientificTrimEndReference', default='')}"
            )
            if trim_warning:
                lines.append(f"    trimWarning: {trim_warning}")
            if original_capture_mode(m):
                lines.append(f"    Trimming guidance: {TIMESTAMP_TRIM_GUIDANCE}")
            lines.append(f"    Scientific timing: {m.get('ScientificTimingStatus')} - {m.get('ScientificTimingMessage', '')}")
            lines.append(f"    App version: {m.get('AppVersion')} build {m.get('BuildNumber')}")

        for note in r.get("notes", [])[1:]:
            lines.append(f"  - {note}")
        lines.append("")
    return "\n".join(lines)


def write_csv_report(out: Path, results: list[dict]) -> None:
    csv_path = out.with_suffix(".csv")
    fieldnames = [
        "session", "camera", "status", "file", "size_bytes", "size_gb", "bitrate_mbps",
        "probe_resolution", "metadata_resolution", "requested_resolution", "selected_resolution",
        "resolution_probe_matches_metadata", "requested_fps", "selected_fps", "writer_fps",
        "container_fps", "measured_camera_fps", "ffprobe_avg_fps", "fps_requested_vs_selected_delta",
        "fps_writer_vs_measured_delta", "probe_frames", "metadata_frames_written",
        "metadata_frames_captured", "frames_probe_minus_metadata", "queue_drops",
        "duplicates", "duplicate_rate_per_minute", "duplicate_percentage",
        "camera_stability", "recommended_preset", "placeholders", "constant_frame_count_mode",
        "recording_timing_mode", "original_capture_mode", "effective_recorded_fps", "wall_clock_duration_sec",
        "frame_based_duration_sec", "container_duration_sec", "container_vs_wall_clock_sec",
        "capture_interval_mean_ms", "capture_interval_min_ms", "capture_interval_max_ms",
        "capture_interval_std_ms", "inter_camera_frame_diff", "inter_camera_start_offset_ms",
        "inter_camera_stop_offset_ms", "scientific_timing_status", "scientific_timing_message",
        "app_version", "build_number",
    ]
    fieldnames = list(dict.fromkeys(fieldnames + [
        "session_scientific_timing_confidence",
        "frame_timestamp_csv_path", "frame_timestamp_csv_written", "frame_timestamp_csv_row_count",
        "first_frame_capture_utc_time", "last_frame_capture_utc_time",
        "first_frame_capture_monotonic_sec", "last_frame_capture_monotonic_sec",
        "first_to_last_frame_duration_sec",
        "session_start_local_time", "session_start_utc_time", "session_start_monotonic_ticks",
        "session_start_monotonic_sec", "session_stop_local_time", "session_stop_utc_time",
        "session_stop_monotonic_ticks", "session_stop_monotonic_sec",
        "session_wall_clock_duration_sec", "session_monotonic_duration_sec",
        "recording_requested_start_local_time", "recording_requested_start_utc_time",
        "recording_requested_start_monotonic_sec",
        "camera_recording_start_local_time", "camera_recording_start_utc_time",
        "camera_recording_start_monotonic_sec",
        "first_frame_local_time", "first_frame_utc_time", "first_frame_monotonic_sec",
        "last_frame_local_time", "last_frame_utc_time", "last_frame_monotonic_sec",
        "camera_recording_stop_local_time", "camera_recording_stop_utc_time",
        "camera_recording_stop_monotonic_sec",
        "writer_closed_local_time", "writer_closed_utc_time", "writer_closed_monotonic_sec",
        "capture_interval_median_ms", "capture_interval_p95_ms", "capture_interval_p99_ms",
        "measured_camera_fps_from_first_last_frame", "measured_camera_fps_from_mean_interval",
        "expected_interval_ms", "requested_expected_interval_ms",
        "mean_interval_error_ms", "absolute_mean_interval_error_ms",
        "long_gap_count", "short_gap_count", "severe_long_gap_count",
        "jitter_score_ms", "fps_stability_grade",
        "trim_recommended_time_source", "trim_warning",
        "scientific_trim_start_reference", "scientific_trim_end_reference",
        "supports_timestamp_based_trimming",
        "enable_visual_near_duplicate_check",
        "app_created_duplicate_frames", "app_created_placeholder_frames",
        "visual_near_identical_frames", "visual_near_identical_frame_rate",
        "visual_exact_duplicate_frames", "visual_longest_exact_duplicate_run",
        "visual_longest_near_identical_run",
        "mean_absolute_pixel_difference", "grayscale_difference_mean",
        "perceptual_hash_difference", "visual_frames_sampled",
    ]))
    with csv_path.open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in results:
            p = r["probe"]
            m = r.get("metadata") or {}
            probe_w = p.get("width") if "error" not in p else None
            probe_h = p.get("height") if "error" not in p else None
            meta_w = m.get("Width", m.get("PixelWidth"))
            meta_h = m.get("Height", m.get("PixelHeight"))
            requested_fps = _meta_value(m, "RequestedFps", default="")
            selected_fps = _meta_value(m, "SelectedFps", "SelectedDeviceFps", default="")
            writer_fps = _meta_value(m, "WriterFps", "RecordingWriterFps", default="")
            measured_fps = _meta_value(m, "MeasuredCameraFps", "MeasuredWriterFps", default="")
            probe_frames = p.get("frame_count") if "error" not in p else None
            written = _meta_int(m, "FramesWritten", "FrameCount", default=0)
            duplicates = _meta_int(m, "DuplicateFrames") + _meta_int(m, "DuplicatedFrames")
            visual = r.get("visual") or {}
            trim_warning = _meta_value(m, "TrimWarning", "trimWarning", default="")
            container_vs_wall = _as_float(_meta_value(m, "ContainerVsWallClockDifferenceSeconds", "TimestampDriftSeconds", default=0))
            if not trim_warning and original_capture_mode(m) and abs(container_vs_wall) > 0.5:
                trim_warning = TIMESTAMP_TRIM_WARNING
            row = {
                "session": r["session"],
                "camera": r["camera"],
                "status": r["status"],
                "session_scientific_timing_confidence": session_scientific_timing_confidence(
                    [x for x in results if x["session"] == r["session"]]
                ),
                "file": r["file"],
                "size_bytes": p.get("size_bytes", ""),
                "size_gb": p.get("size_gb", ""),
                "bitrate_mbps": p.get("bitrate_mbps", ""),
                "probe_resolution": f"{probe_w}x{probe_h}" if probe_w and probe_h else "",
                "metadata_resolution": f"{meta_w}x{meta_h}" if meta_w and meta_h else "",
                "requested_resolution": m.get("RequestedResolution", ""),
                "selected_resolution": m.get("SelectedResolution", ""),
                "resolution_probe_matches_metadata": probe_w == meta_w and probe_h == meta_h if probe_w and meta_w else "",
                "requested_fps": requested_fps,
                "selected_fps": selected_fps,
                "writer_fps": writer_fps,
                "container_fps": _meta_value(m, "ContainerFps", default=p.get("avg_fps", "")),
                "measured_camera_fps": measured_fps,
                "ffprobe_avg_fps": p.get("avg_fps", ""),
                "fps_requested_vs_selected_delta": (
                    abs(float(requested_fps) - float(selected_fps))
                    if requested_fps not in ("", None) and selected_fps not in ("", None)
                    else ""
                ),
                "fps_writer_vs_measured_delta": (
                    abs(float(writer_fps) - float(measured_fps))
                    if writer_fps not in ("", None) and measured_fps not in ("", None)
                    else ""
                ),
                "probe_frames": probe_frames,
                "metadata_frames_written": written or "",
                "metadata_frames_captured": _meta_value(m, "FramesCaptured", default=""),
                "frames_probe_minus_metadata": (probe_frames - written) if probe_frames is not None and written else "",
                "queue_drops": _meta_value(m, "WriterQueueDrops", default=""),
                "duplicates": duplicates,
                "app_created_duplicate_frames": app_created_duplicate_frames(m),
                "app_created_placeholder_frames": app_created_placeholder_frames(m),
                "enable_visual_near_duplicate_check": bool(visual.get("enabled")),
                "visual_near_identical_frames": visual.get("nearIdenticalConsecutiveFrameCount", ""),
                "visual_near_identical_frame_rate": visual.get("nearIdenticalConsecutiveFrameRate", ""),
                "visual_exact_duplicate_frames": visual.get("exactConsecutiveDuplicateCount", ""),
                "visual_longest_exact_duplicate_run": visual.get("longestExactDuplicateRun", ""),
                "visual_longest_near_identical_run": visual.get("longestNearIdenticalRun", ""),
                "mean_absolute_pixel_difference": visual.get("meanAbsolutePixelDifference", ""),
                "grayscale_difference_mean": visual.get("grayscaleDifferenceMean", ""),
                "perceptual_hash_difference": visual.get("perceptualHashDifference", ""),
                "visual_frames_sampled": visual.get("framesSampled", ""),
                "duplicate_rate_per_minute": round(duplicate_rate_per_minute(m, p), 3),
                "duplicate_percentage": round(duplicate_percentage(m, p), 3),
                "camera_stability": camera_stability(m, p),
                "recommended_preset": recommended_preset(m, p),
                "placeholders": _meta_value(m, "PlaceholderFrames", default=""),
                "constant_frame_count_mode": m.get("ConstantFrameCountMode", ""),
                "recording_timing_mode": recording_timing_mode(m),
                "original_capture_mode": m.get("OriginalCaptureMode", m.get("originalCaptureMode", "")),
                "frame_timestamp_csv_path": _meta_value(m, "FrameTimestampCsvPath", "frameTimestampCsvPath", default=""),
                "frame_timestamp_csv_written": _meta_value(m, "FrameTimestampCsvWritten", "frameTimestampCsvWritten", default=""),
                "frame_timestamp_csv_row_count": _meta_value(m, "FrameTimestampCsvRowCount", "frameTimestampCsvRowCount", default=""),
                "first_frame_capture_utc_time": _meta_value(m, "FirstFrameCaptureUtcTime", "firstFrameCaptureUtcTime", default=""),
                "last_frame_capture_utc_time": _meta_value(m, "LastFrameCaptureUtcTime", "lastFrameCaptureUtcTime", default=""),
                "first_frame_capture_monotonic_sec": _meta_value(m, "FirstFrameCaptureMonotonicSec", "firstFrameCaptureMonotonicSec", default=""),
                "last_frame_capture_monotonic_sec": _meta_value(m, "LastFrameCaptureMonotonicSec", "lastFrameCaptureMonotonicSec", default=""),
                "first_to_last_frame_duration_sec": _meta_value(m, "FirstToLastFrameDurationSec", "firstToLastFrameDurationSec", default=""),
                "trim_recommended_time_source": _meta_value(m, "TrimRecommendedTimeSource", "trimRecommendedTimeSource", default=""),
                "trim_warning": trim_warning,
                "scientific_trim_start_reference": _meta_value(m, "ScientificTrimStartReference", "scientificTrimStartReference", default=""),
                "scientific_trim_end_reference": _meta_value(m, "ScientificTrimEndReference", "scientificTrimEndReference", default=""),
                "supports_timestamp_based_trimming": _meta_value(m, "SupportsTimestampBasedTrimming", "supportsTimestampBasedTrimming", default=""),
                "session_start_local_time": _meta_value(m, "SessionStartLocalTime", "sessionStartLocalTime", default=""),
                "session_start_utc_time": _meta_value(m, "SessionStartUtcTime", "sessionStartUtcTime", default=""),
                "session_start_monotonic_ticks": _meta_value(m, "SessionStartMonotonicTicks", "sessionStartMonotonicTicks", default=""),
                "session_start_monotonic_sec": _meta_value(m, "SessionStartMonotonicSec", "sessionStartMonotonicSec", default=""),
                "session_stop_local_time": _meta_value(m, "SessionStopLocalTime", "sessionStopLocalTime", default=""),
                "session_stop_utc_time": _meta_value(m, "SessionStopUtcTime", "sessionStopUtcTime", default=""),
                "session_stop_monotonic_ticks": _meta_value(m, "SessionStopMonotonicTicks", "sessionStopMonotonicTicks", default=""),
                "session_stop_monotonic_sec": _meta_value(m, "SessionStopMonotonicSec", "sessionStopMonotonicSec", default=""),
                "session_wall_clock_duration_sec": _meta_value(m, "SessionWallClockDurationSec", "sessionWallClockDurationSec", default=""),
                "session_monotonic_duration_sec": _meta_value(m, "SessionMonotonicDurationSec", "sessionMonotonicDurationSec", default=""),
                "recording_requested_start_local_time": _meta_value(m, "RecordingRequestedStartLocalTime", "recordingRequestedStartLocalTime", default=""),
                "recording_requested_start_utc_time": _meta_value(m, "RecordingRequestedStartUtcTime", "recordingRequestedStartUtcTime", default=""),
                "recording_requested_start_monotonic_sec": _meta_value(m, "RecordingRequestedStartMonotonicSec", "recordingRequestedStartMonotonicSec", default=""),
                "camera_recording_start_local_time": _meta_value(m, "CameraRecordingStartLocalTime", "cameraRecordingStartLocalTime", default=""),
                "camera_recording_start_utc_time": _meta_value(m, "CameraRecordingStartUtcTime", "cameraRecordingStartUtcTime", default=""),
                "camera_recording_start_monotonic_sec": _meta_value(m, "CameraRecordingStartMonotonicSec", "cameraRecordingStartMonotonicSec", default=""),
                "first_frame_local_time": _meta_value(m, "FirstFrameLocalTime", "firstFrameLocalTime", default=""),
                "first_frame_utc_time": _meta_value(m, "FirstFrameUtcTime", "firstFrameUtcTime", default=""),
                "first_frame_monotonic_sec": _meta_value(m, "FirstFrameMonotonicSec", "firstFrameMonotonicSec", default=""),
                "last_frame_local_time": _meta_value(m, "LastFrameLocalTime", "lastFrameLocalTime", default=""),
                "last_frame_utc_time": _meta_value(m, "LastFrameUtcTime", "lastFrameUtcTime", default=""),
                "last_frame_monotonic_sec": _meta_value(m, "LastFrameMonotonicSec", "lastFrameMonotonicSec", default=""),
                "camera_recording_stop_local_time": _meta_value(m, "CameraRecordingStopLocalTime", "cameraRecordingStopLocalTime", default=""),
                "camera_recording_stop_utc_time": _meta_value(m, "CameraRecordingStopUtcTime", "cameraRecordingStopUtcTime", default=""),
                "camera_recording_stop_monotonic_sec": _meta_value(m, "CameraRecordingStopMonotonicSec", "cameraRecordingStopMonotonicSec", default=""),
                "writer_closed_local_time": _meta_value(m, "WriterClosedLocalTime", "writerClosedLocalTime", default=""),
                "writer_closed_utc_time": _meta_value(m, "WriterClosedUtcTime", "writerClosedUtcTime", default=""),
                "writer_closed_monotonic_sec": _meta_value(m, "WriterClosedMonotonicSec", "writerClosedMonotonicSec", default=""),
                "effective_recorded_fps": _meta_value(m, "EffectivePlaybackFps", "MeasuredCameraFps", default=""),
                "wall_clock_duration_sec": _meta_value(m, "WallClockDurationSeconds", "WallDurationSeconds", default=""),
                "frame_based_duration_sec": _meta_value(m, "FrameBasedDurationSeconds", default=""),
                "container_duration_sec": p.get("duration_sec", ""),
                "container_vs_wall_clock_sec": _meta_value(m, "ContainerVsWallClockDifferenceSeconds", "TimestampDriftSeconds", default=""),
                "capture_interval_mean_ms": _meta_value(m, "CaptureIntervalMeanMs", "AverageCaptureIntervalMs", default=""),
                "capture_interval_median_ms": _meta_value(m, "CaptureIntervalMedianMs", "captureIntervalMedianMs", default=""),
                "capture_interval_min_ms": _meta_value(m, "CaptureIntervalMinMs", "MinCaptureIntervalMs", default=""),
                "capture_interval_max_ms": _meta_value(m, "CaptureIntervalMaxMs", "MaxCaptureIntervalMs", default=""),
                "capture_interval_p95_ms": _meta_value(m, "CaptureIntervalP95Ms", "captureIntervalP95Ms", default=""),
                "capture_interval_p99_ms": _meta_value(m, "CaptureIntervalP99Ms", "captureIntervalP99Ms", default=""),
                "capture_interval_std_ms": _meta_value(m, "CaptureIntervalStdMs", "CaptureJitterMs", default=""),
                "measured_camera_fps_from_first_last_frame": _meta_value(m, "MeasuredCameraFpsFromFirstLastFrame", "measuredCameraFpsFromFirstLastFrame", default=""),
                "measured_camera_fps_from_mean_interval": _meta_value(m, "MeasuredCameraFpsFromMeanInterval", "measuredCameraFpsFromMeanInterval", default=""),
                "expected_interval_ms": _meta_value(m, "ExpectedIntervalMs", "expectedIntervalMs", default=""),
                "requested_expected_interval_ms": _meta_value(m, "RequestedExpectedIntervalMs", "requestedExpectedIntervalMs", default=""),
                "mean_interval_error_ms": _meta_value(m, "MeanIntervalErrorMs", "meanIntervalErrorMs", default=""),
                "absolute_mean_interval_error_ms": _meta_value(m, "AbsoluteMeanIntervalErrorMs", "absoluteMeanIntervalErrorMs", default=""),
                "long_gap_count": _meta_value(m, "LongGapCount", "longGapCount", default=""),
                "short_gap_count": _meta_value(m, "ShortGapCount", "shortGapCount", default=""),
                "severe_long_gap_count": _meta_value(m, "SevereLongGapCount", "severeLongGapCount", default=""),
                "jitter_score_ms": _meta_value(m, "JitterScoreMs", "jitterScoreMs", default=""),
                "fps_stability_grade": _meta_value(m, "FpsStabilityGrade", "fpsStabilityGrade", default=""),
                "inter_camera_frame_diff": _meta_value(m, "InterCameraFrameDifference", "InterCameraFrameDiff", default=""),
                "inter_camera_start_offset_ms": _meta_value(m, "InterCameraStartOffsetMs", default=""),
                "inter_camera_stop_offset_ms": _meta_value(m, "InterCameraStopOffsetMs", default=""),
                "scientific_timing_status": m.get("ScientificTimingStatus", ""),
                "scientific_timing_message": m.get("ScientificTimingMessage", ""),
                "app_version": m.get("AppVersion", ""),
                "build_number": m.get("BuildNumber", ""),
            }
            writer.writerow(row)


def audit_folder(root: Path, ffprobe: str, ffmpeg: str | None = None, enable_visual_near_duplicate_check: bool = False) -> list[dict]:
    results = []
    for mp4 in sorted(root.rglob("*.mp4")):
        cam_dir = mp4.parent
        session = cam_dir.parent.name
        camera = cam_dir.name
        meta = load_metadata(cam_dir)
        probe_data = probe(ffprobe, mp4)
        notes = assess(meta, probe_data, cam_dir)
        visual = (
            visual_near_duplicate_check(ffmpeg, mp4)
            if enable_visual_near_duplicate_check
            else {"enabled": False}
        )
        if (
            enable_visual_near_duplicate_check
            and not visual.get("error")
            and int(visual.get("nearIdenticalConsecutiveFrameCount") or 0) > 0
            and app_created_duplicate_frames(meta) == 0
        ):
            notes.append(
                "Near-identical visual frames detected. This may be normal in static scenes or low movement. "
                "Metadata shows no app-created duplicate frames."
            )
        results.append({
            "session": session,
            "camera": camera,
            "file": str(mp4),
            "status": notes[0],
            "probe": probe_data,
            "metadata": meta,
            "visual": visual,
            "notes": notes,
        })
    return results


def main():
    parser = argparse.ArgumentParser(description="Audit MultiCamApp MP4 files and timing metadata.")
    parser.add_argument("folder", help="Folder tree containing recorded MP4 files.")
    parser.add_argument("report", nargs="?", help="Output report path. Defaults to video_audit_report.txt in the folder.")
    parser.add_argument(
        "--enableVisualNearDuplicateCheck",
        "--enable-visual-near-duplicate-check",
        action="store_true",
        dest="enable_visual_near_duplicate_check",
        help="Offline audit only: sample consecutive frames and report near-identical visual frames as notes.",
    )
    args = parser.parse_args()

    root = Path(args.folder)
    if not root.is_dir():
        print(f"Not a directory: {root}")
        sys.exit(1)
    ffprobe = find_ffprobe()
    if not ffprobe:
        print("ffprobe not found")
        sys.exit(1)
    ffmpeg = find_ffmpeg(ffprobe) if args.enable_visual_near_duplicate_check else None
    results = audit_folder(
        root,
        ffprobe,
        ffmpeg=ffmpeg,
        enable_visual_near_duplicate_check=args.enable_visual_near_duplicate_check,
    )
    report = format_report(root, results)
    out = Path(args.report) if args.report else root / "video_audit_report.txt"
    out.write_text(report, encoding="utf-8")
    write_csv_report(out, results)
    sys.stdout.buffer.write((report + f"\n\nReport saved: {out}\nCSV saved: {out.with_suffix('.csv')}\n").encode("utf-8", errors="replace"))


if __name__ == "__main__":
    main()
