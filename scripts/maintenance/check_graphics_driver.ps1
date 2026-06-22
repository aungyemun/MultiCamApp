param(
    [switch] $Json
)

$ErrorActionPreference = "Stop"

function Convert-WmiDate {
    param([string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "Unknown" }
    try {
        return ([System.Management.ManagementDateTimeConverter]::ToDateTime($Value)).ToString("yyyy-MM-dd")
    }
    catch {
        return $Value
    }
}

try {
    $adapters = Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object {
        [pscustomobject]@{
            Name = if ($_.Name) { $_.Name } else { "Unknown" }
            DriverProvider = if ($_.DriverProviderName) { $_.DriverProviderName } else { "Unknown" }
            DriverVersion = if ($_.DriverVersion) { $_.DriverVersion } else { "Unknown" }
            DriverDate = Convert-WmiDate $_.DriverDate
            IsMicrosoftBasicDisplayAdapter = ($_.Name -match "Microsoft Basic Display")
            IsIntel = ($_.Name -match "Intel")
            IsNvidia = ($_.Name -match "NVIDIA")
            IsAmd = ($_.Name -match "AMD|Radeon")
        }
    }

    $warnings = @()
    if ($adapters | Where-Object { $_.IsMicrosoftBasicDisplayAdapter }) {
        $warnings += "WARNING: Microsoft Basic Display Adapter detected. Install the official Intel/NVIDIA/AMD graphics driver for best preview and recording reliability."
    }
    if (-not ($adapters | Where-Object { $_.IsIntel -or $_.IsNvidia -or $_.IsAmd })) {
        $warnings += "No Intel/NVIDIA/AMD display adapter was identified. This may be normal on some systems, but official GPU drivers are recommended."
    }

    $result = [pscustomobject]@{
        ScanTimeLocal = (Get-Date).ToString("s")
        Adapters = $adapters
        Warnings = $warnings
        Recommendation = "Install the official Intel, NVIDIA, or AMD graphics driver from the PC or GPU vendor. This script is offline-only and does not download drivers."
    }

    if ($Json) {
        $result | ConvertTo-Json -Depth 5
    }
    else {
        Write-Host "MultiCamApp Graphics Driver Check"
        Write-Host "Scan time: $($result.ScanTimeLocal)"
        Write-Host ""
        foreach ($adapter in $adapters) {
            Write-Host "Display adapter: $($adapter.Name)"
            Write-Host "  Provider: $($adapter.DriverProvider)"
            Write-Host "  Version : $($adapter.DriverVersion)"
            Write-Host "  Date    : $($adapter.DriverDate)"
        }
        if ($warnings.Count -gt 0) {
            Write-Host ""
            foreach ($warning in $warnings) { Write-Warning $warning }
        }
        Write-Host ""
        Write-Host $result.Recommendation
    }
}
catch {
    $message = "Graphics driver information could not be read: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    if ($Json) {
        [pscustomobject]@{
            ScanTimeLocal = (Get-Date).ToString("s")
            Adapters = @()
            Warnings = @($message)
            Recommendation = "If preview/recording is unstable, install official Intel/NVIDIA/AMD graphics drivers."
        } | ConvertTo-Json -Depth 5
    }
    else {
        Write-Warning $message
        Write-Host "If preview/recording is unstable, install official Intel/NVIDIA/AMD graphics drivers."
    }
}
