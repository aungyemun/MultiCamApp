@echo off
rem MultiCamApp Offline Diagnostic (Start Menu shortcut target).
rem Launches the app for an offline, no-internet-required diagnostic check:
rem Hardware Diagnostics (Run Hardware Scan) and Video Verification are both
rem fully offline once the app window opens.

set "APP_DIR=%~dp0.."
cd /d "%APP_DIR%"

echo Launching MultiCamApp.exe (offline diagnostic) from %APP_DIR% ...
echo Once the app opens, use Hardware Diagnostics (Run Hardware Scan) or
echo Video Verification to check a recorded session, fully offline.
echo.
echo If the app fails to start, check:
echo   %APP_DIR%\logs\startup_diagnostics.json
echo   %APP_DIR%\logs\crash.log
echo.

"%APP_DIR%\MultiCamApp.exe"

echo.
echo MultiCamApp.exe exited with code %ERRORLEVEL%.
pause
