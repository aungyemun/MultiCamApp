# Forwards to scripts/packaging/sign_release.ps1 (kept for older build scripts).
param(
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [switch]$Strict
)
$script = Join-Path (Split-Path $PSScriptRoot -Parent) "packaging\sign_release.ps1"
& $script -Root $Root @PSBoundParameters
