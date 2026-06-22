@echo off
:: Developer-only hardware-specific stress test.
:: Not included in public installer or release zip.
:: Requires the developer's 4-camera rig labels:
::   j5 Webcam JVU250 #1/#2/#3 and OBSBOT.
::
:: Usage: Run this from the dist\runtime\ folder, or from inside the installed app folder.
:: The --external4-preview-stress command-line mode is handled by External4PreviewStressRunner.cs.
setlocal
cd /d "%~dp0..\.."
if not exist "dist\runtime\MultiCamApp.exe" (
    echo MultiCamApp.exe not found. Run this from the repo root after a successful build.
    echo Expected: dist\runtime\MultiCamApp.exe
    exit /b 1
)
echo Running external 4-camera preview stress test...
echo Output logs will be written to logs\external4_preview_stress_*.txt and *.csv
"dist\runtime\MultiCamApp.exe" --external4-preview-stress
exit /b %ERRORLEVEL%
