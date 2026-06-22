param(
    [Parameter(Mandatory = $true)]
    [string]$Folder,
    [string]$AppRoot = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ([string]::IsNullOrWhiteSpace($AppRoot)) {
    $AppRoot = Join-Path $repo "dist"
}

if (-not (Test-Path $Folder)) {
    Write-Error "Folder not found: $Folder"
}

$env:MULTICAMAPP_ROOT = $AppRoot
$auditPy = Join-Path $repo "scripts\diagnostics\audit_videos_folder.py"
$verifyProj = Join-Path $repo "scripts\diagnostics\VerifyFolderCli\VerifyFolderCli.csproj"
$auditOut = Join-Path $Folder "video_audit_report.txt"

Write-Host "=== Session validation audit ===" -ForegroundColor Cyan
Write-Host "Folder: $Folder"
Write-Host "MULTICAMAPP_ROOT: $AppRoot"
Write-Host ""

python $auditPy $Folder $auditOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project $verifyProj -c Release -- $Folder
$verifyExit = $LASTEXITCODE

Write-Host ""
Write-Host "Reports:" -ForegroundColor Green
Write-Host "  $auditOut"
Write-Host "  $(Join-Path $Folder 'verification_page_report.txt')"
Write-Host "  $(Join-Path $Folder 'verification_page_report.json')"

exit $verifyExit
