# Validate installed shortcuts for MultiCamApp
param(
    [string]$InstallPath = "C:\Program Files\MultiCamApp"
)

$WshShell = New-Object -ComObject WScript.Shell
$DesktopPath = [System.Environment]::GetFolderPath("Desktop")
$StartMenuPath = [System.Environment]::GetFolderPath("CommonPrograms")
$ShortcutName = "MultiCamApp.lnk"

function Check-Shortcut($path, $label) {
    if (Test-Path $path) {
        $shortcut = $WshShell.CreateShortcut($path)
        Write-Host "Checking $label shortcut: $path"
        
        $target = $shortcut.TargetPath
        $workingDir = $shortcut.WorkingDirectory
        
        $expectedTarget = Join-Path $InstallPath "MultiCamApp.exe"
        
        if ($target -eq $expectedTarget) {
            Write-Host "  [OK] Target: $target" -ForegroundColor Green
        } else {
            Write-Host "  [FAIL] Target: $target (Expected: $expectedTarget)" -ForegroundColor Red
        }
        
        if ($workingDir -eq $InstallPath) {
            Write-Host "  [OK] Start in: $workingDir" -ForegroundColor Green
        } else {
            Write-Host "  [FAIL] Start in: $workingDir (Expected: $InstallPath)" -ForegroundColor Red
        }
    } else {
        Write-Host "  [SKIP] $label shortcut not found at $path" -ForegroundColor Yellow
    }
}

Write-Host "--- MultiCamApp Shortcut Validation ---"
Write-Host "Expected Install Path: $InstallPath"

# Check Desktop
Check-Shortcut (Join-Path $DesktopPath $ShortcutName) "Desktop"

# Check Start Menu
$StartMenuFolder = Join-Path $StartMenuPath "MultiCamApp"
Check-Shortcut (Join-Path $StartMenuFolder $ShortcutName) "Start Menu"

Write-Host "--- Validation Complete ---"
