$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$png = Join-Path $root "source\MultiCamApp\MultiCamApp\assets\icons\Multicam.png"
$ico = Join-Path $root "source\MultiCamApp\MultiCamApp\assets\icons\MultiCamApp.ico"

if (-not (Test-Path $png)) { Write-Error "Missing $png" }
if ((Test-Path $ico) -and (Get-Item $ico).LastWriteTime -ge (Get-Item $png).LastWriteTime) {
    Write-Host "App icon up to date"
    return
}

$py = Join-Path $root "tools\python\python.exe"
$iconScript = Join-Path $PSScriptRoot "build_app_icon.py"
if ((Test-Path $py) -and (Test-Path $iconScript)) {
    & $py $iconScript
    if ($LASTEXITCODE -eq 0) { return }
}
Write-Error "Could not build MultiCamApp.ico. Run scripts\setup\install_dev_environment.ps1"
