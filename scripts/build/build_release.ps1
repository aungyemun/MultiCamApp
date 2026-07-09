# Publish bundle -> dist\ ; dev launcher -> project root (not included in installer)
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$proj = Join-Path $root "source\MultiCamApp\MultiCamApp\MultiCamApp.csproj"
$launcherProj = Join-Path $root "source\MultiCamApp\MultiCamApp.Launcher\MultiCamApp.Launcher.csproj"
$verFile = Join-Path $root "source\MultiCamApp\MultiCamApp\config\version.json"
$dist = Join-Path $root "dist"
$scratch = Join-Path $root ".build"
$rootExe = Join-Path $root "MultiCamApp.exe"
$rootStaging = Join-Path $root ".build\root-publish"
$packaging = Join-Path $root "scripts\packaging"
$localDotnet = Join-Path $root "tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$verJson = Get-Content $verFile -Raw | ConvertFrom-Json
$appVersion = $verJson.version
$stage = Join-Path $scratch "dist-$appVersion"

function Get-ProductVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return "" }
    return ((Get-Item $Path).VersionInfo.ProductVersion -as [string]).Trim()
}

& (Join-Path $PSScriptRoot "ensure_app_icon.ps1")

Write-Host "Publishing v$appVersion -> $stage (installer bundle, self-contained single-file apphost)..."
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
& $dotnet publish $proj -c Release -r win-x64 --self-contained true -p:Version=$appVersion -p:InformationalVersion=$appVersion -p:IncludeSourceRevisionInInformationalVersion=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $stage

$stageExe = Join-Path $stage "MultiCamApp.exe"
if (-not (Test-Path $stageExe)) { Write-Error "MultiCamApp.exe not found in $stage" }
$stageVersion = Get-ProductVersion $stageExe
if ($stageVersion -ne $appVersion) {
    Write-Error "Staged MultiCamApp.exe version mismatch: exe=$stageVersion json=$appVersion"
}

$thirdPartyNotices = Join-Path $root "THIRD_PARTY_NOTICES.md"
if (Test-Path $thirdPartyNotices) {
    Copy-Item $thirdPartyNotices (Join-Path $stage "THIRD_PARTY_NOTICES.md") -Force
}

Write-Host "Staging runtime tools (ffprobe for Video Verification, ffmpeg for Deep Verify)..."
& (Join-Path $PSScriptRoot "stage_dist_runtime.ps1") -Root $root -Dist $stage

Write-Host "Mirroring clean staged bundle -> dist\ ..."
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Force -Path $dist | Out-Null }
& robocopy $stage $dist /MIR /R:2 /W:1 /NFL /NDL /NP
$robocopyExit = $LASTEXITCODE
if ($robocopyExit -gt 7) {
    Write-Error "Could not update dist\ from $stage (robocopy exit $robocopyExit). Close MultiCamApp/Explorer handles or reboot, then rerun."
}

$distExe = Join-Path $dist "MultiCamApp.exe"
if (-not (Test-Path $distExe)) { Write-Error "MultiCamApp.exe not found in dist\" }
$distVersion = Get-ProductVersion $distExe
if ($distVersion -ne $appVersion) {
    Write-Error "dist\MultiCamApp.exe version mismatch: exe=$distVersion json=$appVersion"
}

Write-Host "Publishing dev launcher -> project root MultiCamApp.exe (not shipped in installer)..."
if (Test-Path $rootStaging) { Remove-Item $rootStaging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $rootStaging | Out-Null
& $dotnet publish $launcherProj -c Release -r win-x64 --self-contained true -p:Version=$appVersion -p:InformationalVersion=$appVersion -p:SourceRevisionId="" -o $rootStaging
$launcherBuilt = Get-ChildItem $rootStaging -Filter "*.exe" | Select-Object -First 1
if (-not $launcherBuilt) { Write-Error "Launcher exe not found in $rootStaging" }
Copy-Item $launcherBuilt.FullName $rootExe -Force

if (Test-Path $rootStaging) { Remove-Item $rootStaging -Recurse -Force }

& (Join-Path $PSScriptRoot "verify_release.ps1") -Root $root

$signScript = Join-Path $packaging "sign_release.ps1"
if (Test-Path $signScript) {
    & $signScript -Root $root
}

Write-Host "dist\MultiCamApp.exe       (end-user app; installer input)"
Write-Host "$stage       (clean staged installer input retained)"
Write-Host "MultiCamApp.exe            (dev launcher only -> dist\)"
Write-Host "Next: build installer\Setup.exe via installer\build_release.bat"
