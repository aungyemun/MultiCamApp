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
Copy-Item (Join-Path $root "installer\Setup.exe")          $zipRoot
Copy-Item (Join-Path $root "README.md")                    $zipRoot
Copy-Item (Join-Path $root "INSTALLATION.md")              $zipRoot
Copy-Item (Join-Path $root "LICENSE.md")                   $zipRoot
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md")       $zipRoot
Copy-Item (Join-Path $root "DIRECTORY_STRUCTURE.md")       $zipRoot
Copy-Item (Join-Path $root "CITATION.cff")                 $zipRoot
Copy-Item (Join-Path $root "SECURITY.md")                  $zipRoot

# Changelog
Copy-Item (Join-Path $root "docs\changelogs\CHANGELOG.md") $changelogDir

# User guides
Copy-Item (Join-Path $root "docs\user_guide\video_verification.md")  $userGuideDir
Copy-Item (Join-Path $root "docs\user_guide\hardware_diagnostics.md") $userGuideDir
Copy-Item (Join-Path $root "docs\user_guide\security_antivirus.md")   $userGuideDir

# GPL v3 compliance: ffprobe.exe is bundled in Setup.exe; include the license notice alongside.
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
