# Simulate MultiCamApp launch in an offline environment
# This script sets environment variables to trigger diagnostic paths and runs the app from dist\

$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$Dist = Join-Path $Root "dist"
$Exe = Join-Path $Dist "MultiCamApp.exe"

if (-not (Test-Path $Exe)) {
    Write-Error "MultiCamApp.exe not found in dist\. Run build_release.bat first."
    exit 1
}

Write-Host "--- Simulating Offline Launch ---"
Write-Host "App Path: $Exe"

# 1. Disable network at the OS level (optional, requires admin, so we use a flag instead)
# We can set a custom environment variable that the app respects to skip network calls.
$env:MULTICAMAPP_OFFLINE_SIMULATION = "true"

# 2. Run the app and capture logs
Write-Host "Launching app..."
Start-Process -FilePath $Exe -WorkingDirectory $Dist -Wait

Write-Host "`n--- Launch Complete ---"
Write-Host "Checking logs..."

$LogDir = Join-Path $Dist "logs"
if (Test-Path $LogDir) {
    Get-ChildItem $LogDir -Filter "*.log" | ForEach-Object {
        Write-Host "`nLog: $($_.Name)" -ForegroundColor Cyan
        Get-Content $_.FullName -Tail 10
    }
} else {
    Write-Warning "No logs folder found in $Dist"
}

# 3. Run the offline diagnostic batch file
$DiagBat = Join-Path $Dist "runtime\run_offline_diagnostic.bat"
if (Test-Path $DiagBat) {
    Write-Host "`nRunning Offline Diagnostic Tool..."
    cmd.exe /c $DiagBat
}

Write-Host "`nSimulation Finished."
