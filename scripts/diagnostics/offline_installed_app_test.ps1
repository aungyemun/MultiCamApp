# Test an installed MultiCamApp in an offline simulation
# This script locates the installed app, runs it, and collects logs from LocalAppData.

$ErrorActionPreference = "Stop"

# 1. Locate Installation
$InstallPath = ""
$AppId = "{A8C4E2B1-9F3D-4A6E-B5C1-2D8E7F0A3B4C}" # From MultiCamApp.iss
$RegPath = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\${AppId}_is1"
if (-not (Test-Path $RegPath)) {
    $RegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${AppId}_is1"
}

if (Test-Path $RegPath) {
    $InstallPath = (Get-ItemProperty $RegPath).InstallLocation
}

if (-not $InstallPath -or -not (Test-Path $InstallPath)) {
    Write-Host "Could not find installation in registry. Checking default path..."
    $DefaultPath = Join-Path $env:ProgramFiles "MultiCamApp"
    if (Test-Path $DefaultPath) {
        $InstallPath = $DefaultPath
    }
}

if (-not $InstallPath) {
    Write-Error "MultiCamApp installation not found. Please run Setup.exe first."
    exit 1
}

Write-Host "--- Offline Installed App Test ---"
Write-Host "Install Path: $InstallPath"

# 2. Prepare Log Collection
$UserLogDir = Join-Path $InstallPath "logs"
Write-Host "Log Dir: $UserLogDir"

# 3. Run App
$Exe = Join-Path $InstallPath "MultiCamApp.exe"
Write-Host "Launching: $Exe"
Start-Process -FilePath $Exe -WorkingDirectory $InstallPath -Wait

# 4. Collect and Report
Write-Host "`n--- Diagnostic Results ---"
if (Test-Path $UserLogDir) {
    $Logs = Get-ChildItem $UserLogDir -Filter "*.log"
    foreach ($Log in $Logs) {
        Write-Host "`nLog: $($Log.Name)" -ForegroundColor Cyan
        Get-Content $Log.FullName -Tail 20
    }
} else {
    Write-Warning "User log directory not found at $UserLogDir"
}

# 5. Check for critical files
$Required = @(
    "MultiCamApp.exe",
    "OpenCvSharpExtern.dll",
    "opencv_videoio_ffmpeg4100_64.dll",
    "config\appsettings.json",
    "localization\en.json",
    "runtime\ffmpeg\ffprobe.exe"
)

Write-Host "`n--- File Integrity Check ---"
foreach ($File in $Required) {
    $Path = Join-Path $InstallPath $File
    if (Test-Path $Path) {
        Write-Host "[OK] $File"
    } else {
        Write-Host "[MISSING] $File" -ForegroundColor Red
    }
}

Write-Host "`nTest Finished."
