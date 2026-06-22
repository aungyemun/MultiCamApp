#!/usr/bin/env python3
"""Bump MultiCamApp semantic version and sync project files."""
from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VERSION_FILE = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "config" / "version.json"
CSPROJ = ROOT / "source" / "MultiCamApp" / "MultiCamApp" / "MultiCamApp.csproj"
LAUNCHER_CSPROJ = ROOT / "source" / "MultiCamApp" / "MultiCamApp.Launcher" / "MultiCamApp.Launcher.csproj"
INSTALLER_ISS = ROOT / "installer" / "MultiCamApp.iss"
CHANGELOG = ROOT / "docs" / "changelogs" / "CHANGELOG.md"


def load_version() -> dict:
    return json.loads(VERSION_FILE.read_text(encoding="utf-8"))


def save_version(data: dict) -> None:
    VERSION_FILE.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def infer_stage(version: str) -> str:
    # Handle pre-release tags
    if "-" in version:
        tag = version.split("-")[1].lower()
        if "alpha" in tag: return "alpha"
        if "beta" in tag: return "beta"
        if "rc" in tag: return "release_candidate"
    
    # Standard versioning
    parts = version.split("-")[0].split(".")
    major = int(parts[0])
    minor = int(parts[1])
    
    if major >= 1:
        return "stable"
    if minor >= 9:
        return "release_candidate"
    if minor >= 5:
        return "beta"
    if minor >= 2:
        return "feature_milestone"
    if minor >= 1:
        return "alpha"
    return "experimental"


def bump_semver(version: str, part: str) -> str:
    # Strip pre-release tag for calculation
    base_version = version.split("-")[0]
    major, minor, patch = (int(x) for x in base_version.split("."))
    
    if part == "major":
        return f"{major + 1}.0.0"
    if part == "minor":
        return f"{major}.{minor + 1}.0"
    # patch
    return f"{major}.{minor}.{patch + 1}"


def sync_csproj_file(path: Path, version: str, insert_after: str | None = None) -> None:
    # Strip pre-release tag for FileVersion (must be numeric)
    numeric_version = version.split("-")[0]
    
    text = path.read_text(encoding="utf-8")
    if "<Version>" in text:
        text = re.sub(r"<Version>.*?</Version>", f"<Version>{version}</Version>", text)
    if "<FileVersion>" in text:
        text = re.sub(r"<FileVersion>.*?</FileVersion>", f"<FileVersion>{numeric_version}.0</FileVersion>", text)
    if "<InformationalVersion>" in text:
        text = re.sub(r"<InformationalVersion>.*?</InformationalVersion>", f"<InformationalVersion>{version}</InformationalVersion>", text)
    elif insert_after and insert_after in text:
        text = text.replace(
            insert_after,
            f"{insert_after}\n    <Version>{version}</Version>",
        )
    path.write_text(text, encoding="utf-8")


def sync_csproj(version: str) -> None:
    sync_csproj_file(
        CSPROJ,
        version,
        "<AssemblyName>MultiCamApp</AssemblyName>",
    )
    if LAUNCHER_CSPROJ.is_file():
        sync_csproj_file(LAUNCHER_CSPROJ, version)


def sync_installer(version: str) -> None:
    # Strip pre-release tag for installer VersionInfoVersion if needed, 
    # but Inno Setup AppVersion can handle strings.
    text = INSTALLER_ISS.read_text(encoding="utf-8")
    text = re.sub(r'#define AppVersion ".*?"', f'#define AppVersion "{version}"', text)
    INSTALLER_ISS.write_text(text, encoding="utf-8")


def append_changelog(version: str, notes: str, part: str) -> None:
    today = date.today().isoformat()
    header = f"## [{version}] - {today}"
    
    # Check if header already exists
    if CHANGELOG.exists() and header in CHANGELOG.read_text(encoding="utf-8"):
        return

    # Determine kind based on part
    kind = {"patch": "Fixed", "minor": "Added", "major": "Changed"}[part]
    
    # Format notes as bullet points if they aren't already
    body_lines = notes.strip().split("\n")
    formatted_notes = ""
    for line in body_lines:
        line = line.strip()
        if not line: continue
        if not line.startswith("-"):
            formatted_notes += f"- {line}\n"
        else:
            formatted_notes += f"{line}\n"
            
    if not formatted_notes:
        formatted_notes = f"- {kind} release for {version}.\n"

    entry = f"\n{header}\n\n### {kind}\n{formatted_notes}"
    
    content = CHANGELOG.read_text(encoding="utf-8") if CHANGELOG.exists() else "# Changelog\n"
    if "# Changelog" in content:
        content = content.replace("# Changelog\n", f"# Changelog\n{entry}", 1)
    else:
        content = f"# Changelog\n{entry}"
    CHANGELOG.write_text(content, encoding="utf-8")


def run_bump(part: str, notes: str = "") -> dict:
    data = load_version()
    old = data["version"]
    data["version"] = bump_semver(old, part)
    data["build"] = int(data.get("build", 0)) + 1
    data["stage"] = infer_stage(data["version"])
    data["releaseDate"] = date.today().isoformat()
    if notes:
        data["notes"] = notes
    save_version(data)
    sync_csproj(data["version"])
    sync_installer(data["version"])
    append_changelog(data["version"], notes or data.get("notes", ""), part)
    return data


def main() -> int:
    parser = argparse.ArgumentParser(description="Bump MultiCamApp version")
    parser.add_argument(
        "part",
        nargs="?",
        choices=["patch", "minor", "major"],
        default="patch",
        help="patch (+0.0.1), minor (+0.1.0), major (+1.0.0)",
    )
    parser.add_argument("--notes", "-n", default="", help="Changelog / version notes")
    parser.add_argument("--show", action="store_true", help="Print current version only")
    args = parser.parse_args()

    if args.show:
        print(json.dumps(load_version(), indent=2))
        return 0

    data = run_bump(args.part, args.notes)
    print(f"Version: {data['version']}  Build: {data['build']}  Stage: {data['stage']}")
    print(f"Synced: version.json, csproj, installer, changelog")
    return 0


if __name__ == "__main__":
    sys.exit(main())
