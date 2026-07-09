# scan_windows_camera_environment.ps1
# Windows Camera Behavior Study - Local Environment Scanner
#
# Purpose: Collect non-sensitive local Windows camera environment information
#          for the MultiCamApp Windows Camera Behavior Study.
#
# This script does NOT:
#   - Open any camera
#   - Record any video
#   - Test camera FPS
#   - Validate any video file
#   - Run ffprobe
#   - Collect usernames or personal folder paths
#   - Collect network information
#   - Collect precise hardware serial numbers (where avoidable)
#
# Output files:
#   diagnostics/windows_camera_environment_report.txt
#   diagnostics/windows_camera_environment_report.json
#   diagnostics/dxdiag_multicam.txt  (if dxdiag is available)

$ErrorActionPreference = "Continue"

# --- Paths ---
$scriptRoot   = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$diagDir      = Join-Path $scriptRoot "diagnostics"
$reportTxt    = Join-Path $diagDir "windows_camera_environment_report.txt"
$reportJson   = Join-Path $diagDir "windows_camera_environment_report.json"
$dxdiagPath   = Join-Path $diagDir "dxdiag_multicam.txt"

if (-not (Test-Path $diagDir)) {
    New-Item -ItemType Directory -Force -Path $diagDir | Out-Null
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$report    = [ordered]@{}

Write-Host ""
Write-Host "=== MultiCamApp Windows Camera Environment Scanner ==="
Write-Host "Timestamp : $timestamp"
Write-Host "Output    : $diagDir"
Write-Host ""

# --- 1. Timestamp ---
$report["ScanTimestamp"] = $timestamp

# --- 2. Windows version ---
Write-Host "[1/10] Windows version..."
try {
    $osInfo = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $report["WindowsVersion"]      = $osInfo.Caption
    $report["WindowsBuild"]        = $osInfo.BuildNumber
    $report["OSArchitecture"]      = $osInfo.OSArchitecture
    $report["WindowsVersion_Full"] = $osInfo.Version
} catch {
    $report["WindowsVersion"] = "Error: $_"
}

# --- 3. CPU ---
Write-Host "[2/10] CPU information..."
try {
    $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
    $report["CPU_Name"]               = $cpu.Name.Trim()
    $report["CPU_Cores"]              = $cpu.NumberOfCores
    $report["CPU_LogicalProcessors"]  = $cpu.NumberOfLogicalProcessors
    $report["CPU_MaxClockSpeedMHz"]   = $cpu.MaxClockSpeed
} catch {
    $report["CPU"] = "Error: $_"
}

# --- 4. RAM ---
Write-Host "[3/10] RAM..."
try {
    $cs  = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $ramGB = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    $report["RAM_TotalGB"] = $ramGB
} catch {
    $report["RAM_TotalGB"] = "Error: $_"
}

# --- 5. GPU ---
Write-Host "[4/10] GPU adapters..."
try {
    $gpus = Get-CimInstance Win32_VideoController -ErrorAction Stop
    $gpuList = @()
    foreach ($g in $gpus) {
        $gpuList += [ordered]@{
            Name          = $g.Name
            DriverVersion = $g.DriverVersion
            DriverDate    = if ($g.DriverDate) { $g.DriverDate.ToString("yyyy-MM-dd") } else { "Unknown" }
            AdapterRAM_MB = if ($g.AdapterRAM) { [math]::Round($g.AdapterRAM / 1MB) } else { "Unknown" }
            VideoProcessor = $g.VideoProcessor
        }
    }
    $report["GPU_Adapters"] = $gpuList
} catch {
    $report["GPU_Adapters"] = "Error: $_"
}

# --- 6. Camera devices (PnP class: Camera) ---
Write-Host "[5/10] Camera devices (PnP)..."
try {
    $cameras = Get-PnpDevice -Class Camera -ErrorAction Stop
    $cameraList = @()
    foreach ($c in $cameras) {
        $cameraList += [ordered]@{
            FriendlyName = $c.FriendlyName
            Status       = $c.Status
            InstanceId   = ($c.InstanceId -replace 'USB\\VID_[0-9A-Fa-f]+&PID_[0-9A-Fa-f]+\\.*', 'USB\VID_xxxx&PID_xxxx\(redacted)')
        }
    }
    $report["Camera_PnpDevices"] = $cameraList
} catch {
    $report["Camera_PnpDevices"] = "Error: $_"
}

# --- 7. Imaging devices (PnP class: Image) ---
Write-Host "[6/10] Imaging devices (PnP)..."
try {
    $imaging = Get-PnpDevice -Class Image -ErrorAction Stop
    $imagingList = @()
    foreach ($i in $imaging) {
        $imagingList += [ordered]@{
            FriendlyName = $i.FriendlyName
            Status       = $i.Status
        }
    }
    $report["Imaging_PnpDevices"] = $imagingList
} catch {
    $report["Imaging_PnpDevices"] = "Error: $_"
}

# --- 8. USB controllers and hubs ---
Write-Host "[7/10] USB controllers and hubs..."
try {
    $usb = Get-PnpDevice -Class USB -ErrorAction Stop
    $usbList = @()
    foreach ($u in $usb) {
        $usbList += [ordered]@{
            FriendlyName = $u.FriendlyName
            Status       = $u.Status
        }
    }
    $report["USB_Controllers_Hubs"] = $usbList
} catch {
    $report["USB_Controllers_Hubs"] = "Error: $_"
}

# --- 9. Appx packages: Camera-related ---
Write-Host "[8/10] Appx packages (camera / studio)..."
try {
    $camPkgs = Get-AppxPackage -Name "*camera*" -ErrorAction Stop
    $camPkgList = @()
    foreach ($p in $camPkgs) {
        $camPkgList += [ordered]@{
            Name    = $p.Name
            Version = $p.Version
            Publisher = $p.Publisher
        }
    }
    $report["Appx_CameraPackages"] = $camPkgList
} catch {
    $report["Appx_CameraPackages"] = "Error: $_"
}

# --- 10. Appx packages: Studio Effects-related ---
try {
    $studioPkgs = Get-AppxPackage -Name "*studio*" -ErrorAction Stop
    $studioPkgList = @()
    foreach ($p in $studioPkgs) {
        $studioPkgList += [ordered]@{
            Name    = $p.Name
            Version = $p.Version
            Publisher = $p.Publisher
        }
    }
    $report["Appx_StudioPackages"] = $studioPkgList
} catch {
    $report["Appx_StudioPackages"] = "Error: $_"
}

# --- 11. Active power plan ---
Write-Host "[9/10] Power plan and GPU scheduling..."
try {
    $powerOutput = & powercfg /GETACTIVESCHEME 2>&1
    $report["PowerPlan_Active"] = ($powerOutput -join " ").Trim()
} catch {
    $report["PowerPlan_Active"] = "Error: $_"
}

# --- 12. Hardware-Accelerated GPU Scheduling (HAGS) ---
try {
    $hagsPath  = "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"
    $hagsValue = Get-ItemProperty -Path $hagsPath -Name "HwSchMode" -ErrorAction Stop
    $hagsMode  = $hagsValue.HwSchMode
    $report["GPU_HardwareScheduling_HwSchMode"] = switch ($hagsMode) {
        0 { "Disabled (0)" }
        2 { "Enabled (2)" }
        default { "Unknown ($hagsMode)" }
    }
} catch {
    $report["GPU_HardwareScheduling_HwSchMode"] = "Not found or not readable"
}

# --- 13. Camera privacy registry (non-sensitive keys only) ---
try {
    $privPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam"
    if (Test-Path $privPath) {
        $privValue = (Get-ItemProperty -Path $privPath -Name "Value" -ErrorAction Stop).Value
        $report["CameraPrivacy_SystemDefault"] = $privValue
    } else {
        $report["CameraPrivacy_SystemDefault"] = "Key not found"
    }
} catch {
    $report["CameraPrivacy_SystemDefault"] = "Error: $_"
}

# --- Write TXT report ---
Write-Host "[10/10] Writing reports..."

$lines = @()
$lines += "=== MultiCamApp Windows Camera Environment Report ==="
$lines += "Generated : $timestamp"
$lines += ""
foreach ($key in $report.Keys) {
    $val = $report[$key]
    if ($val -is [array] -or $val -is [System.Collections.IList]) {
        $lines += "[$key]"
        foreach ($item in $val) {
            if ($item -is [System.Collections.IDictionary]) {
                foreach ($k in $item.Keys) {
                    $lines += "  $k : $($item[$k])"
                }
                $lines += ""
            } else {
                $lines += "  $item"
            }
        }
    } else {
        $lines += "$key : $val"
    }
}

$lines | Out-File -FilePath $reportTxt -Encoding utf8 -Force

# --- Write JSON report ---
$report | ConvertTo-Json -Depth 6 | Out-File -FilePath $reportJson -Encoding utf8 -Force

# --- Optional: dxdiag ---
$dxdiagGenerated = $false
try {
    $dxdiagExe = "$env:SystemRoot\System32\dxdiag.exe"
    if (Test-Path $dxdiagExe) {
        Write-Host "Running dxdiag (this may take 10-20 seconds)..."
        & $dxdiagExe /t $dxdiagPath | Out-Null
        $waited = 0
        while (-not (Test-Path $dxdiagPath) -and $waited -lt 30) {
            Start-Sleep -Seconds 2
            $waited += 2
        }
        if (Test-Path $dxdiagPath) {
            $dxdiagGenerated = $true
        }
    }
} catch {
    # dxdiag not available or failed — not critical
}

# --- Summary ---
Write-Host ""
Write-Host "=== Scan complete ==="
Write-Host "TXT report : $reportTxt"
Write-Host "JSON report: $reportJson"
if ($dxdiagGenerated) {
    Write-Host "DxDiag     : $dxdiagPath"
} else {
    Write-Host "DxDiag     : Not generated (dxdiag unavailable or timed out)"
}
Write-Host ""
Write-Host "Next step: paste scan results into docs/windows_camera_behavior_study.md Section 7."
