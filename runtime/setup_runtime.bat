@echo off
setlocal enabledelayedexpansion

rem MultiCamApp bundled runtime setup/verification script.
rem Invoked by the installer as: setup_runtime.bat install
rem Also safe to re-run manually from the installed app folder for troubleshooting.

set "RUNTIME_DIR=%~dp0"
if "%RUNTIME_DIR:~-1%"=="\" set "RUNTIME_DIR=%RUNTIME_DIR:~0,-1%"
set "APP_DIR=%RUNTIME_DIR%\.."
set "LOG_DIR=%APP_DIR%\logs"
set "LOG_FILE=%LOG_DIR%\runtime_setup.log"
set "EXIT_FILE=%LOG_DIR%\runtime_setup_exit.txt"
set "FLAG_FILE=%RUNTIME_DIR%\runtime_initialized.flag"
set "ENV_FILE=%RUNTIME_DIR%\runtime_paths.env"
set "FFPROBE=%RUNTIME_DIR%\ffmpeg\ffprobe.exe"
set "FFMPEG=%RUNTIME_DIR%\ffmpeg\ffmpeg.exe"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1

echo [%date% %time%] Runtime setup started (%1) > "%LOG_FILE%"

if not exist "%FFPROBE%" (
    echo [%date% %time%] FAIL: ffprobe.exe not found at %FFPROBE% >> "%LOG_FILE%"
    echo 1 > "%EXIT_FILE%"
    exit /b 1
)

if not exist "%FFMPEG%" (
    echo [%date% %time%] FAIL: ffmpeg.exe not found at %FFMPEG% >> "%LOG_FILE%"
    echo 1 > "%EXIT_FILE%"
    exit /b 1
)

rem Small env file other tools/scripts can source for the bundled tool paths.
(
    echo FFPROBE_PATH=%FFPROBE%
    echo FFMPEG_PATH=%FFMPEG%
    echo RUNTIME_DIR=%RUNTIME_DIR%
) > "%ENV_FILE%"

echo Initialized %date% %time%> "%FLAG_FILE%"

echo [%date% %time%] ffprobe.exe verified: %FFPROBE% >> "%LOG_FILE%"
echo [%date% %time%] ffmpeg.exe verified: %FFMPEG% >> "%LOG_FILE%"
echo [%date% %time%] Runtime setup completed successfully >> "%LOG_FILE%"
echo 0 > "%EXIT_FILE%"
exit /b 0
