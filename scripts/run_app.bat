@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

if exist "dist\MultiCamApp.exe" (
    start "" /D "%CD%\dist" "%CD%\dist\MultiCamApp.exe"
    exit /b 0
)

if exist "MultiCamApp.exe" (
    start "" "%CD%\MultiCamApp.exe"
    exit /b 0
)

echo MultiCamApp is not built yet.
echo Run:  installer\build_release.bat
exit /b 1
