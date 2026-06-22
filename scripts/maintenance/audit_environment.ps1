param([switch]$Repair)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root

$checks = [ordered]@{
    "source/MultiCamApp/MultiCamApp.sln" = "file"
    "source/MultiCamApp/MultiCamApp/assets/icons/MultiCamApp.ico" = "file"
    "installer/build_release.bat" = "file"
    "env/activate.ps1" = "file"
    "tools/dotnet/dotnet.exe" = "tool"
    "tools/python/python.exe" = "tool"
    "runtime/ffmpeg/ffprobe.exe" = "tool"
    "runtime/vc_runtime/vc_redist.x64.exe" = "tool"
    "installer/vc_redist.x64.exe" = "tool"
    "tools/inno/ISCC.exe" = "optional"
}

$missing = @()
foreach ($entry in $checks.GetEnumerator()) {
    $ok = Test-Path (Join-Path $root ($entry.Key -replace "/", "\"))
    if ($ok) { Write-Host "[OK] $($entry.Key)" }
    elseif ($entry.Value -eq "optional") { Write-Host "[--] $($entry.Key) (optional)" }
    else { $missing += $entry.Key; Write-Host "[!!] $($entry.Key)" }
}

if ($missing -and $Repair) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\download_vendor_tools.ps1")
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\setup\install_dev_environment.ps1") -SkipVendor
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($iscc) {
        New-Item -Force -ItemType Directory (Join-Path $root "tools\inno") | Out-Null
        Copy-Item $iscc (Join-Path $root "tools\inno\ISCC.exe") -Force
    }
}
elseif ($missing) {
    Write-Host "`nRun: scripts\maintenance\audit_environment.ps1 -Repair"
    exit 1
}

$env:PYTHONIOENCODING = "utf-8"
& (Join-Path $root "tools\python\python.exe") (Join-Path $root "scripts\maintenance\validate_project_structure.py")
