param (
    [Parameter(Mandatory=$true)]
    [ValidateSet("BeforeBuild", "AfterInstall")]
    [string]$Mode,

    [Parameter(Mandatory=$false)]
    [string]$InstallPath = ""
)

$ErrorActionPreference = "Continue"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$AuditDir = Join-Path $Root "release_audit"
if (-not (Test-Path $AuditDir)) { New-Item -ItemType Directory $AuditDir | Out-Null }
$ReportFile = Join-Path $AuditDir "setup_bundle_inspection_report.txt"

$TargetDir = ""
if ($Mode -eq "BeforeBuild") {
    $TargetDir = Join-Path $Root "dist"
    Write-Host "--- BeforeBuild Inspection: $TargetDir ---"
} else {
    if (-not $InstallPath) {
        Write-Error "InstallPath is required for AfterInstall mode."
        exit 1
    }
    $TargetDir = $InstallPath
    Write-Host "--- AfterInstall Inspection: $TargetDir ---"
}

$Report = @()
$Report += "=========================================="
$Report += "MultiCamApp Setup Bundle Inspection Report"
$Report += "=========================================="
$Report += "Mode: $Mode"
$Report += "Target: $TargetDir"
$Report += "Timestamp: $(Get-Date)"
$Report += ""

$CriticalMissing = $false

function Check-Item {
    param($Name, $RelativePath, $IsCritical = $true, $IsFolder = $false)
    $FullPath = Join-Path $TargetDir $RelativePath
    $Exists = Test-Path $FullPath
    $Status = if ($Exists) { "OK" } else { if ($IsCritical) { "MISSING" } else { "OPTIONAL-MISSING" } }
    
    if ($IsCritical -and -not $Exists) {
        $script:CriticalMissing = $true
    }

    $line = "[{0}] {1,-25} | {2}" -f $Status, $Name, $RelativePath
    Write-Host $line
    $script:Report += $line

    if ($Exists -and -not $IsFolder) {
        # Execution check if it's an exe
        if ($RelativePath.EndsWith(".exe")) {
            try {
                if ($Name -eq "ffprobe") {
                    $out = & $FullPath -version 2>&1 | Select-Object -First 1
                    $script:Report += "      Output: $out"
                } elseif ($Name -eq "python") {
                    $out = & $FullPath --version 2>&1
                    $script:Report += "      Output: $out"
                    # Test imports if python
                    $importTest = & $FullPath -c "import sys; print('Python OK');" 2>&1
                    $script:Report += "      Import Test: $importTest"
                } elseif ($Name -eq "Rscript") {
                    $out = & $FullPath --version 2>&1
                    $script:Report += "      Output: $out"
                }
            } catch {
                $script:Report += "      Execution Check FAILED: $($_.Exception.Message)"
                if ($IsCritical) { $script:CriticalMissing = $true }
            }
        }
    }
}

# --- Define Components to Check ---

# Core App
Check-Item "MultiCamApp.exe" "MultiCamApp.exe"

# Native Dependencies
Check-Item "OpenCvSharpExtern" "OpenCvSharpExtern.dll"
Check-Item "OpenCV FFmpeg bridge" "opencv_videoio_ffmpeg4100_64.dll"

# Assets & Config
Check-Item "App Settings" "config\appsettings.json"
Check-Item "Localization (EN)" "localization\en.json"
Check-Item "App Icon" "assets\icons\MultiCamApp.ico"

# Bundled Runtimes
Check-Item "ffprobe" "runtime\ffmpeg\ffprobe.exe"
Check-Item "python" "runtime\python\python.exe" $false
Check-Item "python-lib" "runtime\python\Lib\site-packages" $false $true
Check-Item "Rscript" "runtime\R\bin\Rscript.exe" $false
Check-Item "R-library" "runtime\R\library" $false $true

# Diagnostic Tools
Check-Item "Offline Diagnostic" "runtime\run_offline_diagnostic.bat"
Check-Item "Runtime Setup" "runtime\setup_runtime.bat"

$Report += ""
if ($CriticalMissing) {
    $Report += "RESULT: FAILED - Critical components are missing."
    Write-Host "`nFAILED: Critical components are missing." -ForegroundColor Red
} else {
    $Report += "RESULT: PASSED - All critical components present."
    Write-Host "`nPASSED: All critical components present." -ForegroundColor Green
}

$Report | Out-File $ReportFile -Encoding utf8
Write-Host "Report saved to: $ReportFile"

if ($CriticalMissing) { exit 1 } else { exit 0 }
