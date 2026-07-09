param(
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)
$ErrorActionPreference = "Stop"

$verFile = Join-Path $Root "source\MultiCamApp\MultiCamApp\config\version.json"
$ver = (Get-Content $verFile -Raw | ConvertFrom-Json).version
$distExe = Join-Path $Root "dist\MultiCamApp.exe"
$rootExe = Join-Path $Root "MultiCamApp.exe"
$iss = Join-Path $Root "installer\MultiCamApp.iss"
$setupNew = Join-Path $Root "installer\Setup.exe"
$setupLegacy = Join-Path $Root "installer\setup.exe"

$fail = @()
function Get-ProductVersion([string]$Path) {
    if (-not (Test-Path $Path)) { return "" }
    return ((Get-Item $Path).VersionInfo.ProductVersion -as [string]).Trim()
}

if (-not (Test-Path $distExe)) { $fail += "Missing dist\MultiCamApp.exe" }
if (-not (Test-Path $rootExe)) { $fail += "Missing root MultiCamApp.exe launcher" }
if (-not (Test-Path (Join-Path $Root "dist\localization\en.json"))) { $fail += "Missing dist\localization\en.json" }
if (-not (Test-Path (Join-Path $Root "dist\runtime\ffmpeg\ffprobe.exe"))) { $fail += "Missing dist\runtime\ffmpeg\ffprobe.exe" }
if (-not (Test-Path (Join-Path $Root "dist\runtime\ffmpeg\ffmpeg.exe"))) { $fail += "Missing dist\runtime\ffmpeg\ffmpeg.exe (Deep Verify)" }
if (-not (Test-Path (Join-Path $Root "dist\config\appsettings.json"))) { $fail += "Missing dist\config\appsettings.json" }
if (Test-Path (Join-Path $Root "dist\localization\LanguageManager.cs")) { $fail += "dist\localization should not contain LanguageManager.cs" }

# OpenCV native DLL — required by legacy pipeline and V2 Tier4 fallback
if (-not (Test-Path (Join-Path $Root "dist\OpenCvSharpExtern.dll"))) {
    $fail += "Missing dist\OpenCvSharpExtern.dll (required for legacy OpenCV pipeline)"
}

# VC++ Redistributable — required on fresh offline Windows installs
# Warn only (not error) — the file is a large binary not always present in repo
if (-not (Test-Path (Join-Path $Root "dist\runtime\vc_runtime\vc_redist.x64.exe"))) {
    Write-Warning "dist\runtime\vc_runtime\vc_redist.x64.exe not staged. Fresh offline installs may fail if VC++ runtime is missing."
}

if (Test-Path $distExe) {
    $distVersion = Get-ProductVersion $distExe
    if ($distVersion -ne $ver) {
        $fail += "dist\MultiCamApp.exe ProductVersion mismatch: exe=$distVersion version.json=$ver"
    }
}

$distVersionJson = Join-Path $Root "dist\config\version.json"
if (Test-Path $distVersionJson) {
    $publishedVersion = (Get-Content $distVersionJson -Raw | ConvertFrom-Json).version
    if ($publishedVersion -ne $ver) {
        $fail += "dist\config\version.json mismatch: dist=$publishedVersion source=$ver"
    }
}
else {
    $fail += "Missing dist\config\version.json"
}

if (Test-Path $iss) {
    $issText = Get-Content $iss -Raw
    if ($issText -notmatch [regex]::Escape("#define AppVersion `"$ver`"")) {
        $fail += "installer\MultiCamApp.iss AppVersion does not match version.json ($ver)"
    }
}

$csproj = Join-Path $Root "source\MultiCamApp\MultiCamApp\MultiCamApp.csproj"
if ((Get-Content $csproj -Raw) -notmatch "<Version>$ver</Version>") {
    $fail += "MultiCamApp.csproj Version does not match version.json ($ver)"
}

if (-not (Test-Path $setupNew) -and -not (Test-Path $setupLegacy)) {
    Write-Host "NOTE: installer not built yet (run ISCC after build_release.bat)"
}
elseif (Test-Path $setupNew) {
    $setupVersion = Get-ProductVersion $setupNew
    if ($setupVersion -and $setupVersion -ne $ver) {
        Write-Host "NOTE: existing installer\Setup.exe ProductVersion is $setupVersion; rebuild installer for v$ver"
    }
}

if ($fail.Count -gt 0) {
    foreach ($f in $fail) { Write-Host "VERIFY FAIL: $f" }
    throw "Release verification failed."
}

Write-Host "Release verify OK (v$ver)"
