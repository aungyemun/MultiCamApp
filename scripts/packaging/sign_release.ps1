# Authenticode signing for release builds. Skips safely when no certificate is configured.
param(
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$TimestampUrl = $(if ($env:MULTICAMAPP_TIMESTAMP_URL) { $env:MULTICAMAPP_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }),
    [switch]$Strict
)
$ErrorActionPreference = "Stop"

function Find-SignTool {
    $kits = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )
    foreach ($base in $kits) {
        if (-not (Test-Path $base)) { continue }
        $found = Get-ChildItem -Path $base -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

function Test-SignatureValid {
    param([string]$Path)
    $sig = Get-AuthenticodeSignature -FilePath $Path
    return $sig.Status -eq "Valid"
}

$thumbprint = $env:MULTICAMAPP_SIGN_THUMBPRINT
$pfx = $env:MULTICAMAPP_SIGN_PFX
$pfxPass = $env:MULTICAMAPP_SIGN_PASSWORD
$certPath = $env:MULTICAMAPP_SIGN_CERT

if (-not $thumbprint -and -not $pfx -and -not $certPath) {
    Write-Host "Sign: skipped (no certificate — set MULTICAMAPP_SIGN_PFX, MULTICAMAPP_SIGN_CERT, or MULTICAMAPP_SIGN_THUMBPRINT)"
    return
}

$signtool = Find-SignTool
if (-not $signtool) {
    $msg = "signtool.exe not found. Install Windows SDK Signing Tools."
    if ($Strict) { throw $msg }
    Write-Warning $msg
    return
}

$setupExe = Join-Path $Root "installer\Setup.exe"
$legacySetup = Join-Path $Root "installer\setup.exe"
$targets = @(
    (Join-Path $Root "dist\MultiCamApp.exe")
) | Where-Object { Test-Path $_ }

if (Test-Path $setupExe) { $targets += $setupExe }
elseif (Test-Path $legacySetup) { $targets += $legacySetup }

$distDir = Join-Path $Root "dist"
if (Test-Path $distDir) {
    $targets += Get-ChildItem $distDir -Filter "*.dll" -File |
        Where-Object { $_.Name -match "^(MultiCamApp|Presentation|WindowsBase|System\.|Microsoft\.)" } |
        Select-Object -First 12 -ExpandProperty FullName
}

$targets = $targets | Select-Object -Unique
if ($targets.Count -eq 0) {
    Write-Warning "Sign: no files to sign."
    return
}

$signArgs = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl)
if ($pfx) {
    $signArgs += "/f", $pfx
    if ($pfxPass) { $signArgs += "/p", $pfxPass }
}
elseif ($certPath) {
    $signArgs += "/f", $certPath
    if ($pfxPass) { $signArgs += "/p", $pfxPass }
}
else {
    $signArgs += "/sha1", $thumbprint
}

$failed = @()
foreach ($file in $targets) {
    Write-Host "Signing $file ..."
    & $signtool @($signArgs + $file)
    if ($LASTEXITCODE -ne 0) {
        $failed += $file
        continue
    }
    if (Test-SignatureValid $file) {
        Write-Host "  Verified: Valid signature"
    }
    else {
        Write-Warning "  Verify: signature not Valid (may be unsigned timestamp or test cert)"
    }
}

if ($failed.Count -gt 0) {
    $msg = "Signing failed for: $($failed -join ', ')"
    if ($Strict) { throw $msg }
    Write-Warning $msg
}
else {
    Write-Host "Sign: OK ($($targets.Count) file(s))"
}
