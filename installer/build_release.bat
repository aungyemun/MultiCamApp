@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0.."
if errorlevel 1 exit /b 1

set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS_EXE%" (
    where pwsh >nul 2>&1 && set "PS_EXE=pwsh" || set "PS_EXE=powershell"
)
set "ISCC_EXE=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not exist "%ISCC_EXE%" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not exist "%ISCC_EXE%" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_EXE%" set "ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_EXE%" set "ISCC_EXE=%CD%\tools\inno\ISCC.exe"

set "SUMMARY_FILE=installer\build_release_summary.txt"
if exist "%SUMMARY_FILE%" del /q "%SUMMARY_FILE%"
echo === MultiCamApp Release Build ===> "%SUMMARY_FILE%"
echo Build started: %DATE% %TIME%>>"%SUMMARY_FILE%"

echo Running scripts\build\build_release.ps1...>>"%SUMMARY_FILE%"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "scripts\build\build_release.ps1" >>"%SUMMARY_FILE%" 2>&1
if errorlevel 1 (
    echo build_release.ps1 failed. See %SUMMARY_FILE%
    exit /b 1
)

set "TMP_VER=installer\.build_version.tmp"
if exist "%TMP_VER%" del /q "%TMP_VER%"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "installer\extract_version_json.ps1" "source\MultiCamApp\MultiCamApp\config\version.json" "%TMP_VER%" >>"%SUMMARY_FILE%" 2>&1
if errorlevel 1 exit /b 1
for /f "usebackq tokens=1 delims=|" %%v in ("%TMP_VER%") do set "VER=%%v"
if exist "%TMP_VER%" del /q "%TMP_VER%"

if not exist "%ISCC_EXE%" (
    echo Inno Setup compiler not found: %ISCC_EXE%>>"%SUMMARY_FILE%"
    exit /b 1
)

if not exist "installer\vc_redist.x64.exe" (
    echo WARNING: installer\vc_redist.x64.exe not found. VC++ runtime will be skipped during installation.>>"%SUMMARY_FILE%"
    echo WARNING: installer\vc_redist.x64.exe missing - VC++ runtime will NOT be bundled.
)

set "SETUP_OUT=installer\MultiCamApp_%VER%_Setup.exe"
echo Building %SETUP_OUT% with AppVersion=%VER%...>>"%SUMMARY_FILE%"
"%ISCC_EXE%" /DAppVersion="%VER%" /DPublishDir="..\dist" "%~dp0MultiCamApp.iss" >>"%SUMMARY_FILE%" 2>&1
if errorlevel 1 (
    echo ISCC failed. See %SUMMARY_FILE%
    exit /b 1
)

if not exist "%SETUP_OUT%" (
    echo %SETUP_OUT% not produced>>"%SUMMARY_FILE%"
    exit /b 1
)

set "TMP_SETUP_VER=installer\.build_setupver.tmp"
if exist "%TMP_SETUP_VER%" del /q "%TMP_SETUP_VER%"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "installer\extract_file_productversion.ps1" "%SETUP_OUT%" "%TMP_SETUP_VER%" >>"%SUMMARY_FILE%" 2>&1
if errorlevel 1 exit /b 1
for /f "usebackq delims=" %%s in ("%TMP_SETUP_VER%") do set "SETUP_VER=%%s"
if exist "%TMP_SETUP_VER%" del /q "%TMP_SETUP_VER%"

if /I not "%SETUP_VER%"=="%VER%" (
    echo Setup.exe version mismatch: setup=%SETUP_VER% source=%VER%>>"%SUMMARY_FILE%"
    exit /b 1
)

echo %SETUP_OUT% product version=%SETUP_VER%>>"%SUMMARY_FILE%"

echo Building installer\installer.zip...>>"%SUMMARY_FILE%"
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "scripts\packaging\create_release_zip.ps1" -DestinationZip "installer\installer.zip" >>"%SUMMARY_FILE%" 2>&1
if errorlevel 1 (
    echo create_release_zip.ps1 failed. See %SUMMARY_FILE%
    exit /b 1
)
if not exist "installer\installer.zip" (
    echo installer.zip not produced>>"%SUMMARY_FILE%"
    exit /b 1
)
echo installer.zip produced>>"%SUMMARY_FILE%"

echo Build finished: %DATE% %TIME%>>"%SUMMARY_FILE%"
echo Summary written to %SUMMARY_FILE%
exit /b 0
