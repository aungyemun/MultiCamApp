# Dot-source from repo root:  . .\env\activate.ps1
$Root = Split-Path $PSScriptRoot -Parent
$paths = @(
    (Join-Path $Root "tools\dotnet"),
    (Join-Path $Root "tools\python"),
    (Join-Path $Root "runtime\ffmpeg"),
    (Join-Path $Root "tools\inno")
) -join ";"
$env:PATH = "$paths;$env:PATH"
$env:DOTNET_ROOT = Join-Path $Root "tools\dotnet"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:NUGET_PACKAGES = Join-Path $Root "tools\nuget-packages"
$env:MULTICAMAPP_ROOT = $Root
$env:PYTHONIOENCODING = "utf-8"
$env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
Write-Host "MultiCamApp env active (dotnet, python, ffmpeg, inno on PATH)"
