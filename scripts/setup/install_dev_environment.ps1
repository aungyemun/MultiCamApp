# Install local build toolchain under tools\ and runtime\ (developer PC only).
# Run once:  scripts\setup_tools.bat
# Or:      .\scripts\setup\install_dev_environment.ps1
param(
    [switch]$SkipVendor,
    [switch]$SkipFfmpeg,
    [switch]$SkipInno,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
$env:PIP_NO_PYTHON_VERSION_WARNING = "1"
$env:PYTHONDONTWRITEBYTECODE = "1"

@("tools\dotnet", "tools\python", "tools\inno", "tools\nuget-packages", "tools\wheelhouse",
  "runtime\ffmpeg", "runtime\vc_runtime", "data\logs", "data\temp", "dist", "env") | ForEach-Object {
    New-Item -ItemType Directory -Force -Path (Join-Path $root $_) | Out-Null
}

function Download-File($Url, $OutPath) {
    if ((Test-Path $OutPath) -and -not $Force) {
        Write-Host "  skip (exists): $OutPath"
        return
    }
    Write-Host "  download: $Url"
    Invoke-WebRequest -Uri $Url -OutFile $OutPath -UseBasicParsing
}

# --- Vendor tools (FFmpeg + VC++), same as RecordMultipleCamerasApp ---
if (-not $SkipVendor) {
    $needVendor = $Force -or -not (Test-Path (Join-Path $root "installer\vc_redist.x64.exe"))
    if ($needVendor) {
        Write-Host "`n[1] Vendor tools (FFmpeg, VC++ redist)"
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\download_vendor_tools.ps1")
    } else {
        Write-Host "`n[1] Vendor tools OK (skip download)"
    }
}

# --- .NET SDK 8.0.421 ---
Write-Host "`n[2] .NET SDK -> tools\dotnet"
$dotnetInstall = Join-Path $root "tools\dotnet-install.ps1"
Download-File "https://dot.net/v1/dotnet-install.ps1" $dotnetInstall
& $dotnetInstall -InstallDir (Join-Path $root "tools\dotnet") -Version "8.0.421" -Architecture x64
$localDotnet = Join-Path $root "tools\dotnet\dotnet.exe"
if (-not (Test-Path $localDotnet)) { throw "dotnet-install failed" }
Write-Host "  dotnet $($(& $localDotnet --version))"

$proj = Join-Path $root "source\MultiCamApp\MultiCamApp\MultiCamApp.csproj"
$env:DOTNET_ROOT = Join-Path $root "tools\dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:NUGET_PACKAGES = Join-Path $root "tools\nuget-packages"

Write-Host "`n[3] NuGet restore + Debug build"
& $localDotnet restore $proj
& $localDotnet build $proj -c Debug
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# --- Python embeddable + pip (scripts\requirements.txt) ---
Write-Host "`n[4] Python -> tools\python"
$pyVer = "3.12.10"
$pyZip = Join-Path $root "data\temp\python-embed.zip"
Download-File "https://www.python.org/ftp/python/$pyVer/python-$pyVer-embed-amd64.zip" $pyZip
$pyDest = Join-Path $root "tools\python"
if ($Force -and (Test-Path $pyDest)) { Remove-Item $pyDest -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pyDest | Out-Null
Expand-Archive $pyZip -DestinationPath $pyDest -Force
$pth = Get-ChildItem $pyDest -Filter "python*._pth" | Select-Object -First 1
if ($pth) {
    $content = Get-Content $pth.FullName
    $content = $content -replace "#import site", "import site"
    if ($content -notcontains "Lib\site-packages") { $content += "Lib\site-packages" }
    Set-Content $pth.FullName $content
}
New-Item -ItemType Directory -Force -Path (Join-Path $pyDest "Lib\site-packages") | Out-Null
$pyExe = Join-Path $pyDest "python.exe"
& $pyExe --version

Write-Host "`n[5] pip packages (scripts\requirements.txt)"
$getPip = Join-Path $root "data\temp\get-pip.py"
Download-File "https://bootstrap.pypa.io/get-pip.py" $getPip
& $pyExe $getPip --no-warn-script-location
$req = Join-Path $root "scripts\requirements.txt"
$wheelhouse = Join-Path $root "tools\wheelhouse"
if ((Get-ChildItem $wheelhouse -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) {
    & $pyExe -m pip install --no-index -r $req -f $wheelhouse
} else {
    & $pyExe -m pip install -r $req
}

# --- Inno Setup 6 ---
if (-not $SkipInno) {
    Write-Host "`n[6] Inno Setup -> tools\inno (or system)"
    $innoDir = Join-Path $root "tools\inno"
    $localIscc = Join-Path $innoDir "ISCC.exe"
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (Test-Path $localIscc) {
        Write-Host "  skip (exists): $localIscc"
    } elseif ($found) {
        New-Item -ItemType Directory -Force -Path $innoDir | Out-Null
        Copy-Item $found $localIscc -Force
        Write-Host "  copied from $found"
    } else {
        $innoInstaller = Join-Path $root "data\temp\innosetup-installer.exe"
        Download-File "https://jrsoftware.org/download.php/is.exe" $innoInstaller
        Write-Host "  saved: data\temp\innosetup-installer.exe"
        Write-Host "  run: data\temp\innosetup-installer.exe /VERYSILENT /DIR=`"$innoDir`""
        Write-Host "  or install Inno Setup 6, then re-run this script"
    }
}

Write-Host "`n[7] Application icon"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\build\ensure_app_icon.ps1")

# --- env\activate ---
Write-Host "`n[8] env\activate scripts"
$activatePs1 = @"
# Dot-source from repo root:  . .\env\activate.ps1
`$Root = Split-Path `$PSScriptRoot -Parent
`$paths = @(
    (Join-Path `$Root "tools\dotnet"),
    (Join-Path `$Root "tools\python"),
    (Join-Path `$Root "runtime\ffmpeg"),
    (Join-Path `$Root "tools\inno")
) -join ";"
`$env:PATH = "`$paths;`$env:PATH"
`$env:DOTNET_ROOT = Join-Path `$Root "tools\dotnet"
`$env:DOTNET_MULTILEVEL_LOOKUP = "0"
`$env:NUGET_PACKAGES = Join-Path `$Root "tools\nuget-packages"
`$env:MULTICAMAPP_ROOT = `$Root
`$env:PYTHONIOENCODING = "utf-8"
`$env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
Write-Host "MultiCamApp env active (dotnet, python, ffmpeg, inno on PATH)"
"@
Set-Content (Join-Path $root "env\activate.ps1") $activatePs1 -Encoding UTF8

$activateCmd = @"
@echo off
set "ROOT=%~dp0.."
set "PATH=%ROOT%\tools\dotnet;%ROOT%\tools\python;%ROOT%\runtime\ffmpeg;%ROOT%\tools\inno;%PATH%"
set "DOTNET_ROOT=%ROOT%\tools\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "NUGET_PACKAGES=%ROOT%\tools\nuget-packages"
set "MULTICAMAPP_ROOT=%ROOT%"
set "PYTHONIOENCODING=utf-8"
echo MultiCamApp env active
"@
Set-Content (Join-Path $root "env\activate.cmd") $activateCmd -Encoding ASCII

$iscc = @(
    (Join-Path $root "tools\inno\ISCC.exe"),
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

@{
    installedAt = (Get-Date).ToString("o")
    dotnet = "8.0.421"
    python = $pyVer
    ffmpeg = if (Test-Path (Join-Path $root "runtime\ffmpeg\ffprobe.exe")) { "ok" } else { "missing" }
    vcRedist = if (Test-Path (Join-Path $root "installer\vc_redist.x64.exe")) { "ok" } else { "missing" }
    inno = if ($iscc) { $iscc } else { "missing" }
} | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $root "tools\manifest.json")

Write-Host "`nDone."
Write-Host "  . `"$root\env\activate.ps1`""
Write-Host "  installer\build_release.bat"
