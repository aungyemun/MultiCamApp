# Download bundled vendor tools for offline MultiCamApp builds.
# Run from repo root:  powershell -ExecutionPolicy Bypass -File scripts\download_vendor_tools.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$FfmpegDir = Join-Path $Root "runtime\ffmpeg"
$VcRuntime = Join-Path $Root "runtime\vc_runtime\vc_redist.x64.exe"
$VcInstaller = Join-Path $Root "installer\vc_redist.x64.exe"

New-Item -ItemType Directory -Force -Path $FfmpegDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $VcRuntime) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $VcInstaller) | Out-Null

Write-Host "Downloading Microsoft VC++ x64 redistributable..."
$VcUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
Invoke-WebRequest -Uri $VcUrl -OutFile $VcRuntime -UseBasicParsing
Copy-Item -Force $VcRuntime $VcInstaller
Write-Host "  -> runtime\vc_runtime\vc_redist.x64.exe"
Write-Host "  -> installer\vc_redist.x64.exe"

Write-Host "Downloading FFmpeg essentials..."
$ZipPath = Join-Path $Root "data\temp\ffmpeg-release-essentials.zip"
$ExtractRoot = Join-Path $Root "data\temp\ffmpeg-extract"
New-Item -ItemType Directory -Force -Path (Split-Path $ZipPath) | Out-Null
$FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
Invoke-WebRequest -Uri $FfmpegUrl -OutFile $ZipPath -UseBasicParsing
if (Test-Path $ExtractRoot) { Remove-Item -Recurse -Force $ExtractRoot }
Expand-Archive -Path $ZipPath -DestinationPath $ExtractRoot -Force
$BinDir = Get-ChildItem -Path $ExtractRoot -Recurse -Directory -Filter "bin" | Select-Object -First 1
if (-not $BinDir) { throw "Could not find bin folder in FFmpeg archive." }
foreach ($name in @("ffmpeg.exe", "ffprobe.exe")) {
    $src = Join-Path $BinDir.FullName $name
    if (-not (Test-Path $src)) { throw "Missing $name in FFmpeg archive." }
    Copy-Item -Force $src (Join-Path $FfmpegDir $name)
    Write-Host "  -> runtime\ffmpeg\$name"
}

Write-Host "Done."
