# Copy vendor runtime tools into dist\ after dotnet publish.
param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Dist
)

$ErrorActionPreference = "Stop"
$srcFfmpeg = Join-Path $Root "runtime\ffmpeg"
$destFfmpeg = Join-Path $Dist "runtime\ffmpeg"

# 1. FFmpeg tools
if (-not (Test-Path (Join-Path $srcFfmpeg "ffprobe.exe"))) {
    Write-Error "Missing runtime\ffmpeg\ffprobe.exe. Run scripts\download_vendor_tools.ps1 or installer\build_release.bat"
}
if (-not (Test-Path (Join-Path $srcFfmpeg "FFMPEG_LICENSE.txt"))) {
    Write-Error "Missing runtime\ffmpeg\FFMPEG_LICENSE.txt. Recreate it from the THIRD_PARTY_NOTICES.md FFmpeg section or the previous session output."
}

New-Item -ItemType Directory -Force -Path $destFfmpeg | Out-Null
# Ship both ffprobe.exe (fast metadata-only Video Verification) and ffmpeg.exe (the on-demand
# Deep Verify per-frame MD5 duplicate-frame check — see DeepVerifyService.cs). ffmpeg.exe is
# opt-in from the UI and never runs automatically.
# FFMPEG_LICENSE.txt must be shipped alongside both for GPL v3 compliance.
if (-not (Test-Path (Join-Path $srcFfmpeg "ffmpeg.exe"))) {
    Write-Error "Missing runtime\ffmpeg\ffmpeg.exe. Run scripts\download_vendor_tools.ps1 or installer\build_release.bat"
}
foreach ($name in @("ffprobe.exe", "ffmpeg.exe", "FFMPEG_LICENSE.txt")) {
    $src = Join-Path $srcFfmpeg $name
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $destFfmpeg $name) -Force
        Write-Host "Staged dist\runtime\ffmpeg\$name"
    }
}

# 2. Runtime setup scripts (public release artifacts only)
# NOTE: run_external4_preview_stress.bat is intentionally NOT staged here.
#       It is a developer-only hardware-specific stress test (requires 4-camera dev rig).
#       The developer copy lives in scripts\dev\run_external4_preview_stress.bat.
$srcSetup = Join-Path $Root "runtime\setup_runtime.bat"
$srcDebug = Join-Path $Root "runtime\run_app_debug.bat"
$srcOffline = Join-Path $Root "runtime\run_offline_diagnostic.bat"
$destRuntime = Join-Path $Dist "runtime"
New-Item -ItemType Directory -Force -Path $destRuntime | Out-Null

if (Test-Path $srcSetup) {
    Copy-Item $srcSetup $destRuntime -Force
    Write-Host "Staged dist\runtime\setup_runtime.bat"
}
if (Test-Path $srcDebug) {
    Copy-Item $srcDebug $destRuntime -Force
    Write-Host "Staged dist\runtime\run_app_debug.bat"
}
if (Test-Path $srcOffline) {
    Copy-Item $srcOffline $destRuntime -Force
    Write-Host "Staged dist\runtime\run_offline_diagnostic.bat"
}

# 3. Python runtime (if exists in runtime\python)
$srcPython = Join-Path $Root "runtime\python"
$destPython = Join-Path $Dist "runtime\python"
if (Test-Path $srcPython) {
    New-Item -ItemType Directory -Force -Path $destPython | Out-Null
    Copy-Item "$srcPython\*" $destPython -Recurse -Force
    Write-Host "Staged dist\runtime\python (portable)"
}

# 4. R runtime (if exists in runtime\R)
$srcR = Join-Path $Root "runtime\R"
$destR = Join-Path $Dist "runtime\R"
if (Test-Path $srcR) {
    New-Item -ItemType Directory -Force -Path $destR | Out-Null
    Copy-Item "$srcR\*" $destR -Recurse -Force
    Write-Host "Staged dist\runtime\R (portable)"
}

foreach ($dll in Get-ChildItem $srcFfmpeg -Filter "*.dll" -File -ErrorAction SilentlyContinue) {
    Copy-Item $dll.FullName (Join-Path $destFfmpeg $dll.Name) -Force
    Write-Host "Staged dist\runtime\ffmpeg\$($dll.Name)"
}

$readme = Join-Path $srcFfmpeg "README.txt"
if (Test-Path $readme) {
    Copy-Item $readme (Join-Path $destFfmpeg "README.txt") -Force
}

# 5. VC++ 2015-2022 x64 Redistributable
# Bundled for offline installs. The installer (Setup.exe) runs vc_redist.x64.exe silently.
# MultiCamApp itself does NOT run the installer; Setup.exe does.
# runtime\vc_runtime\vc_redist.x64.exe is in .gitignore (binary) — download via
# scripts\download_vendor_tools.ps1 or place manually from aka.ms/vs/17/release/vc_redist.x64.exe
$srcVcRedist  = Join-Path $Root "runtime\vc_runtime\vc_redist.x64.exe"
$destVcRedist = Join-Path $Dist "runtime\vc_runtime"
if (Test-Path $srcVcRedist) {
    New-Item -ItemType Directory -Force -Path $destVcRedist | Out-Null
    Copy-Item $srcVcRedist (Join-Path $destVcRedist "vc_redist.x64.exe") -Force
    Write-Host "Staged dist\runtime\vc_runtime\vc_redist.x64.exe"
}
else {
    Write-Warning "runtime\vc_runtime\vc_redist.x64.exe not found — fresh offline installs may fail if VC++ runtime is missing. Download from aka.ms/vs/17/release/vc_redist.x64.exe"
}
