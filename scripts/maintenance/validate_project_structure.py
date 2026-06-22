#!/usr/bin/env python3

from pathlib import Path



ROOT = Path(__file__).resolve().parents[2]

REQUIRED = [

    "source/MultiCamApp/MultiCamApp.sln",

    "source/MultiCamApp/MultiCamApp/MultiCamApp.csproj",
    "source/MultiCamApp/MultiCamApp.Launcher/MultiCamApp.Launcher.csproj",

    "source/MultiCamApp/MultiCamApp/config/appsettings.json",

    "source/MultiCamApp/MultiCamApp/config/version.json",

    "source/MultiCamApp/MultiCamApp/localization/en.json",

    "source/MultiCamApp/MultiCamApp/localization/ja.json",

    "source/MultiCamApp/MultiCamApp/assets/icons/Multicam.png",

    "source/MultiCamApp/MultiCamApp/assets/icons/MultiCamApp.ico",

    "scripts/build/build_release.ps1",
    "scripts/build/verify_release.ps1",
    "scripts/run_app.bat",

    "scripts/build/build_app_icon.py",

    "scripts/clean_workspace.ps1",

    "scripts/download_vendor_tools.ps1",
    "scripts/requirements.txt",
    "scripts/setup_tools.bat",
    "scripts/setup/install_dev_environment.ps1",

    "installer/build_release.bat",

    "installer/MultiCamApp.iss",
    "scripts/packaging/sign_release.ps1",
    "scripts/packaging/validate_installer_security.py",
    "docs/user_guide/security_antivirus.md",
    "INSTALLATION.md",
    "docs/developer_notes/release_checklist.md",

    "env/activate.ps1",

    "docs/changelogs/CHANGELOG.md",

    "data/logs/.gitkeep",

    "data/temp/.gitkeep",

]



FORBIDDEN_TOP = [

    "releases", "backups", "cache", "logs", "output", "temp", "assets",

    "config", "localization",

    "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant",

    "tests", ".build",

]



VERSION_KEYS = {"version", "build", "stage"}





def main():

    missing = [p for p in REQUIRED if not (ROOT / p).exists()]

    if missing:

        print("MISSING:")

        for m in missing:

            print(" ", m)

        raise SystemExit(1)



    stray = [p for p in FORBIDDEN_TOP if (ROOT / p).exists()]

    if stray:

        print("Remove obsolete paths (scripts/clean_workspace.ps1):")

        for s in stray:

            print(" ", s)

        raise SystemExit(1)



    legacy = ROOT / "dist" / "MultiCamApp"

    if legacy.is_dir():

        print("Remove legacy dist/MultiCamApp (use flat dist/ only)")

        raise SystemExit(1)



    import json

    ver = json.loads((ROOT / "source/MultiCamApp/MultiCamApp/config/version.json").read_text(encoding="utf-8"))

    if not VERSION_KEYS.issubset(ver.keys()):

        raise SystemExit(1)



    print("OK: structure valid")

    print(f"    version {ver['version']} build {ver['build']} stage {ver['stage']}")





if __name__ == "__main__":

    main()

