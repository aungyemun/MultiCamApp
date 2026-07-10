@echo off
rem MultiCamApp Diagnostic Launch (Start Menu shortcut target).
rem Runs the app with its console window visible so startup errors are visible
rem even if the app closes before its own error dialog can be read.

set "APP_DIR=%~dp0.."
cd /d "%APP_DIR%"

echo Launching MultiCamApp.exe from %APP_DIR% ...
echo If the app closes unexpectedly, check:
echo   %APP_DIR%\logs\startup_diagnostics.json
echo   %APP_DIR%\logs\crash.log
echo.

"%APP_DIR%\MultiCamApp.exe"

echo.
echo MultiCamApp.exe exited with code %ERRORLEVEL%.
pause
