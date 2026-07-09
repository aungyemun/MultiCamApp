param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root

$zipRoot = Join-Path $env:TEMP ("multicam_release_" + [guid]::NewGuid().ToString("n"))
$userGuideDir = Join-Path $zipRoot "docs\user_guide"
$changelogDir = Join-Path $zipRoot "docs\changelogs"
$zipRuntimeFfmpeg = Join-Path $zipRoot "runtime\ffmpeg"
New-Item -ItemType Directory -Path $userGuideDir -Force | Out-Null
New-Item -ItemType Directory -Path $changelogDir -Force | Out-Null
New-Item -ItemType Directory -Path $zipRuntimeFfmpeg -Force | Out-Null

# Core installer and root docs
# build_release.bat produces a version-qualified filename (installer\MultiCamApp_<ver>_Setup.exe),
# never a bare installer\Setup.exe — resolve the current version's actual filename here instead
# of hardcoding "Setup.exe" (which only worked before because a stale copy happened to exist).
# installer\extract_version_json.ps1 always emits "<version-without-suffix>.<build>" (matching
# installer\MultiCamApp.iss's AppVersionNumeric convention) regardless of whether the version
# string carries a pre-release suffix, so mirror that exact logic here.
$versionJson = Get-Content (Join-Path $root "source\MultiCamApp\MultiCamApp\config\version.json") -Raw | ConvertFrom-Json
$verNumeric = "{0}.{1}" -f ($versionJson.version -split '-')[0], $versionJson.build
$setupExePath = Join-Path $root "installer\MultiCamApp_${verNumeric}_Setup.exe"
if (-not (Test-Path $setupExePath)) {
    Write-Error "Setup.exe not found for current version: $setupExePath (run installer\build_release.bat first)"
    exit 1
}
Copy-Item $setupExePath (Join-Path $zipRoot "Setup.exe")
Copy-Item (Join-Path $root "README.md")                    $zipRoot
Copy-Item (Join-Path $root "INSTALLATION.md")              $zipRoot
Copy-Item (Join-Path $root "LICENSE.md")                   $zipRoot
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md")       $zipRoot
Copy-Item (Join-Path $root "DIRECTORY_STRUCTURE.md")       $zipRoot
Copy-Item (Join-Path $root "CITATION.cff")                 $zipRoot
Copy-Item (Join-Path $root "SECURITY.md")                  $zipRoot

# Changelog
# NOTE: docs\changelogs\CHANGELOG.md is a stale archive only updated by the (currently unused)
# scripts\maintenance\bump_version.py tool; every actual release since v1.2.30-alpha has been
# hand-documented in the root CHANGELOG.md instead (see docs\developer_notes\versioning.md). Ship
# that one so end users get the real, current release history rather than one frozen at v1.2.30-alpha.
Copy-Item (Join-Path $root "CHANGELOG.md") $changelogDir

# User guides
Copy-Item (Join-Path $root "docs\user_guide\video_verification.md")  $userGuideDir
Copy-Item (Join-Path $root "docs\user_guide\hardware_diagnostics.md") $userGuideDir
Copy-Item (Join-Path $root "docs\user_guide\security_antivirus.md")   $userGuideDir

# GPL v3 compliance: ffprobe.exe and ffmpeg.exe are bundled in Setup.exe; include the license notice alongside.
Copy-Item (Join-Path $root "runtime\ffmpeg\FFMPEG_LICENSE.txt") $zipRuntimeFfmpeg

$dest = if ([System.IO.Path]::IsPathRooted($DestinationZip)) { $DestinationZip } else { Join-Path $root $DestinationZip }
if (Test-Path $dest) {
    Remove-Item $dest -Force
}

Push-Location $zipRoot
try {
    Compress-Archive -Path "*" -DestinationPath $dest -Force
}
finally {
    Pop-Location
    Remove-Item $zipRoot -Recurse -Force
}

Write-Host "Release package: $dest"
