#!/usr/bin/env python3
"""Validate release layout for security-friendly installer behavior."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DIST = ROOT / "dist"
INSTALLER = ROOT / "installer"
ISS = INSTALLER / "MultiCamApp.iss"
VERSION = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "config" / "version.json"
APPSETTINGS = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "config" / "appsettings.json"
INSTALLATION_DOC = ROOT / "INSTALLATION.md"
POLICY_DOC = ROOT / "docs" / "user_guide" / "security_antivirus.md"

ALLOWED_EXE = {"MultiCamApp.exe", "createdump.exe"}
ALLOWED_SCRIPTS = {"runtime/setup_runtime.bat", "runtime/run_app_debug.bat"}
FORBIDDEN_DIST_SUFFIXES = {".ps1", ".bat", ".cmd", ".vbs", ".js"}
FORBIDDEN_DIST_NAMES = {"ffmpeg.exe", "ffprobe.exe", "python.exe", "dotnet.exe"}
ALLOWED_VENDOR_SUBPATH = Path("runtime") / "ffmpeg"
ALLOWED_VENDOR_TOOL_NAMES = {"ffprobe.exe", "ffmpeg.exe"}


def is_allowed_vendor_tool(name: str, rel: Path) -> bool:
    """True if `name` is ffprobe.exe/ffmpeg.exe staged under runtime/ffmpeg/ — both are
    legitimate bundled tools (ffprobe for fast Video Verification, ffmpeg for the on-demand
    Deep Verify per-frame MD5 check), never linked into MultiCamApp.exe itself."""
    return (
        name in ALLOWED_VENDOR_TOOL_NAMES
        and len(rel.parts) >= 3
        and Path(*rel.parts[:2]) == ALLOWED_VENDOR_SUBPATH
    )
SUSPICIOUS_ISS_PATTERNS = [
    r"RunOnce",
    r"RunServices",
    r"ScheduledTask",
    r"ShellExec.*powershell",
    r"ShellExec.*cmd\.exe",
    r"AppInit_DLLs",
    r"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
]


def fail(msg: str) -> None:
    print(f"SECURITY CHECK FAIL: {msg}")
    sys.exit(1)


def ok(msg: str) -> None:
    print(f"OK: {msg}")


def main() -> int:
    errors: list[str] = []

    if not VERSION.is_file():
        errors.append(f"Missing {VERSION}")
    else:
        ver = json.loads(VERSION.read_text(encoding="utf-8"))
        if "version" not in ver:
            errors.append("version.json missing version")
        else:
            ok(f"version {ver['version']}")

    if not APPSETTINGS.is_file():
        errors.append("Missing appsettings.json")
    else:
        cfg = json.loads(APPSETTINGS.read_text(encoding="utf-8"))
        sec = cfg.get("security") or {}
        if sec.get("requireAntivirusExclusion"):
            errors.append("security.requireAntivirusExclusion must be false")
        if sec.get("runAtStartup"):
            errors.append("security.runAtStartup must be false")
        if sec.get("installBackgroundService"):
            errors.append("security.installBackgroundService must be false")
        if sec.get("installScheduledTask"):
            errors.append("security.installScheduledTask must be false")
        if sec.get("hiddenRecordingAllowed"):
            errors.append("security.hiddenRecordingAllowed must be false")
        if not sec.get("cameraAccessOnlyAfterUserAction", True):
            errors.append("security.cameraAccessOnlyAfterUserAction must be true")
        ok("appsettings security section")

    if not INSTALLATION_DOC.is_file():
        errors.append(f"Missing {INSTALLATION_DOC}")
    if not POLICY_DOC.is_file():
        errors.append(f"Missing {POLICY_DOC}")

    if not ISS.is_file():
        errors.append("Missing installer/MultiCamApp.iss")
    else:
        iss_text = ISS.read_text(encoding="utf-8")
        if "OutputBaseFilename=Setup" not in iss_text:
            errors.append("Inno OutputBaseFilename must be Setup")
        if "PrivilegesRequired=lowest" not in iss_text:
            errors.append("Inno PrivilegesRequired should be lowest")
        if "LicenseFile=" not in iss_text:
            errors.append("Inno must include LicenseFile")
        for pat in SUSPICIOUS_ISS_PATTERNS:
            if re.search(pat, iss_text, re.IGNORECASE):
                errors.append(f"Inno suspicious pattern: {pat}")
        ok("installer script policy")

    setup = INSTALLER / "Setup.exe"
    legacy = INSTALLER / "setup.exe"
    if not setup.is_file() and not legacy.is_file():
        errors.append("Installer exe not built (Setup.exe)")
    else:
        ok("installer package present")

    if not (DIST / "MultiCamApp.exe").is_file():
        errors.append("dist/MultiCamApp.exe missing — run build_release first")
    else:
        ok("dist/MultiCamApp.exe")

    dist_cfg = DIST / "config" / "appsettings.json"
    if DIST.is_dir() and not dist_cfg.is_file():
        errors.append("dist/config/appsettings.json missing")

    if DIST.is_dir():
        for path in DIST.rglob("*"):
            if not path.is_file():
                continue
            rel = path.relative_to(DIST)
            name = path.name.lower()
            rel_str = rel.as_posix()
            if path.suffix.lower() in FORBIDDEN_DIST_SUFFIXES and rel_str not in ALLOWED_SCRIPTS:
                errors.append(f"Forbidden script in dist: {rel}")
            if name in FORBIDDEN_DIST_NAMES:
                if not is_allowed_vendor_tool(name, rel):
                    errors.append(f"Forbidden bundled tool in dist: {rel}")
            if path.suffix.lower() == ".exe":
                vendor_tool = is_allowed_vendor_tool(path.name, rel)
                if path.name not in ALLOWED_EXE and not vendor_tool:
                    errors.append(f"Unexpected exe in dist: {rel}")

        ffprobe = DIST / "runtime" / "ffmpeg" / "ffprobe.exe"
        if not ffprobe.is_file():
            errors.append("dist/runtime/ffmpeg/ffprobe.exe missing (Video Verification)")
        else:
            ok("dist/runtime/ffmpeg/ffprobe.exe")

        ffmpeg = DIST / "runtime" / "ffmpeg" / "ffmpeg.exe"
        if not ffmpeg.is_file():
            errors.append("dist/runtime/ffmpeg/ffmpeg.exe missing (Deep Verify)")
        else:
            ok("dist/runtime/ffmpeg/ffmpeg.exe")

        if not (DIST / "localization" / "en.json").is_file():
            errors.append("dist/localization/en.json missing")
        if (DIST / "localization" / "LanguageManager.cs").is_file():
            errors.append("dist must not contain LanguageManager.cs")

        ok("dist folder allowlist scan")

    if errors:
        print("\n".join(errors))
        return 1

    print("SECURITY VALIDATION: all checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
