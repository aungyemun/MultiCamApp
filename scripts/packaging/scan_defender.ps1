# Optional Windows Defender custom scan on release artifacts (build machine only).
param(
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [switch]$Strict
)
$ErrorActionPreference = "Stop"

$targets = @(
    (Join-Path $Root "dist"),
    (Join-Path $Root "installer\Setup.exe"),
    (Join-Path $Root "installer\setup.exe")
) | Where-Object { Test-Path $_ }

if ($targets.Count -eq 0) {
    Write-Host "Defender scan: skipped (no dist or installer)"
    return
}

$mp = "${env:ProgramFiles}\Windows Defender\MpCmdRun.exe"
if (-not (Test-Path $mp)) {
    $msg = "MpCmdRun.exe not found — Defender scan skipped"
    if ($Strict) { throw $msg }
    Write-Host $msg
    return
}

foreach ($t in $targets) {
    Write-Host "Defender scan: $t"
    & $mp -Scan -ScanType 3 -File $t
    if ($LASTEXITCODE -ge 2) {
        $msg = "Defender reported issues for $t (exit $LASTEXITCODE)"
        if ($Strict) { throw $msg }
        Write-Warning $msg
    }
}
Write-Host "Defender scan: completed"
