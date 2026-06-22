# MultiCamApp Release Cleanliness Validator
# Scans dist\ and installer\ for forbidden development/test artifacts.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$installer = Join-Path $root "installer"

Write-Host "--- Release Clean Audit ---"

# 1. Forbidden Extensions
$forbiddenExtensions = @(".pdb", ".pyc", ".cache", ".tmp", ".bak", ".log", ".user", ".orig", ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".webm", ".m4v")

# 2. Forbidden Folders (exact names or patterns)
$forbiddenFolders = @(
    "CameraSwitchTest", "RecordingTrialTest", "VerifySession", 
    "diagnostics", "setup", "maintenance", "developer_notes", 
    "logs", "debug", "test", "tests", "tmp", "temp", "nuget-packages", "__pycache__"
)

# 3. Forbidden Path Patterns (regex)
$forbiddenPatterns = @(
    "C:\\Users\\",
    "Session_\d{8}_\d{6}",
    "final_test_",
    "720ptest_",
    "1080pTest_",
    "recordings",
    "outputs"
)

$foundIssues = 0

function Check-Path($path, $label) {
    if (-not (Test-Path $path)) { return }
    
    Write-Host "Auditing ${label}: ${path}"
    
    # Check files
    Get-ChildItem -Path $path -Recurse -File | ForEach-Object {
        $fileName = $_.Name
        $filePath = $_.FullName
        $ext = [System.IO.Path]::GetExtension($fileName).ToLower()
        
        # Extension check
        if ($forbiddenExtensions -contains $ext) {
            Write-Host "  [FAIL] Forbidden file extension found: ${filePath}" -ForegroundColor Red
            $script:foundIssues++
        }
        
        # Pattern check
        foreach ($pattern in $forbiddenPatterns) {
            if ($filePath -match $pattern) {
                Write-Host "  [FAIL] Forbidden pattern '${pattern}' found in path: ${filePath}" -ForegroundColor Red
                $script:foundIssues++
            }
        }
    }
    
    # Check directories
    Get-ChildItem -Path $path -Recurse -Directory | ForEach-Object {
        $dirName = $_.Name
        if ($forbiddenFolders -contains $dirName) {
            Write-Host "  [FAIL] Forbidden folder found: $($_.FullName)" -ForegroundColor Red
            $script:foundIssues++
        }
    }
}

Check-Path $dist "Distribution Folder"
Check-Path $installer "Installer Folder"

if ($foundIssues -gt 0) {
    Write-Host "`nERROR: Release audit failed. $foundIssues forbidden items detected." -ForegroundColor Red
    exit 1
}

Write-Host "`nRelease audit OK. No development artifacts detected in release paths." -ForegroundColor Green
exit 0
