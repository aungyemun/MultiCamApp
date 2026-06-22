@echo off
REM One-time setup: vendor tools + local SDK/Python/Inno (run from project root)
setlocal
cd /d "%~dp0.."
echo [1/2] Downloading vendor tools (FFmpeg, VC++ redist)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\scripts\download_vendor_tools.ps1"
if errorlevel 1 exit /b 1
echo.
echo [2/2] Installing local build toolchain...
powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\scripts\setup\install_dev_environment.ps1" -SkipFfmpeg
if errorlevel 1 exit /b 1
echo.
echo Setup complete. Next:
echo   . .\env\activate.ps1
echo   installer\build_release.bat
exit /b 0
