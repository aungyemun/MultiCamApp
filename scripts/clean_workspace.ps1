# MultiCamApp Workspace Cleanup Script
# Removes all generated build artifacts, logs, and temporary files.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

Write-Host "--- Cleaning MultiCamApp Workspace ---"

# 1. Directories to remove
$RemoveDirs = @(
    "dist", ".build", "releases", "logs", "debug", "temp", "tmp", 
    "source/MultiCamApp/MultiCamApp/bin", 
    "source/MultiCamApp/MultiCamApp/obj",
    "source/MultiCamApp/MultiCamApp.Launcher/bin",
    "source/MultiCamApp/MultiCamApp.Launcher/obj"
)

foreach ($dir in $RemoveDirs) {
    $fullPath = Join-Path $Root $dir
    if (Test-Path $fullPath) {
        Write-Host "Removing $dir ..."
        Remove-Item -Recurse -Force $fullPath
    }
}

# 2. Files to remove from root
$rootFiles = @(
    "MultiCamApp.exe", "MultiCamApp.dll", "MultiCamApp.deps.json", 
    "MultiCamApp.runtimeconfig.json", "MultiCamApp.pdb", "createdump.exe",
    "DIRECTORY_STRUCTURE.md.bak", "generate_tree.py"
)

foreach ($file in $rootFiles) {
    $fullPath = Join-Path $Root $file
    if (Test-Path $fullPath) {
        Write-Host "Removing $file ..."
        Remove-Item -Force $fullPath
    }
}

# 3. Clean up __pycache__ and .pyc files
Write-Host "Cleaning Python caches..."
Get-ChildItem -Path $Root -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force
Get-ChildItem -Path $Root -Recurse -File -Filter "*.pyc" | Remove-Item -Force

# 4. Clean up installer folder
Write-Host "Cleaning installer artifacts..."
$setupFiles = @("Setup.exe", "MultiCamApp_Setup.exe", "setup.exe")
foreach ($setup in $setupFiles) {
    $path = Join-Path $Root "installer\$setup"
    if (Test-Path $path) { Remove-Item -Force $path }
}
Get-ChildItem -Path (Join-Path $Root "installer") -Filter "*.zip" | Remove-Item -Force

# 5. Re-create empty dist folder
if (-not (Test-Path "dist")) {
    New-Item -ItemType Directory -Path "dist" | Out-Null
}
if (-not (Test-Path "dist\.gitkeep")) {
    New-Item -ItemType File -Path "dist\.gitkeep" | Out-Null
}

Write-Host "`nWorkspace clean. Ready for a fresh build."
