# Starts/stops recording N times in ONE app session and reports the app's own start/stop timings from its log.
param([int]$Times = 3, [string]$Cam1 = 'c922 Pro Stream Webcam', [string]$Cam2 = 'Integrated Webcam (Built-in front)', [string]$Name = 'r01_restart')
$d = $PSScriptRoot; . "$d\lib.ps1"; $root = $McRoot
$exe = $McExe
function U { $t = if ($args[0] -eq 'launch') { 90 } elseif ($args[0] -eq 'waitenabled') { [int]$args[2] + 30 } else { 45 }
  (Invoke-Timed powershell.exe (@('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\mcui.ps1") + $args) $t).Out -split "`r?`n" | ? { $_ } }
$t0 = Get-Date
U launch $exe | Out-Null
U radio '2 Cameras' | Out-Null; U select Cam1Box $Cam1 | Out-Null; U select Cam2Box $Cam2 | Out-Null; U text SessionBox $Name | Out-Null
U click StartPreviewBtn | Out-Null; U waitenabled StartRecordBtn 60 | Out-Null; Start-Sleep 5
for ($i = 1; $i -le $Times; $i++) {
  U click StartRecordBtn | Out-Null; U waitenabled StopRecordBtn 30 | Out-Null; Start-Sleep 5
  U click StopRecordBtn | Out-Null; U waitenabled StartRecordBtn 120 | Out-Null; Start-Sleep 3
}
U click StopPreviewBtn | Out-Null; U waitenabled StartPreviewBtn 60 | Out-Null; U close | Out-Null
$log = Get-ChildItem "$root\source\MultiCamApp\MultiCamApp\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\logs" -Filter 'app_runtime_*.txt' | Sort LastWriteTime -Desc | Select -First 1
Select-String $log.FullName -Pattern 'V2_DEFAULT_RECORDING_STARTED|V2_SLOT_RECORDING_STARTED|V2_ENCODER_WARMUP' | ? { [datetime]::ParseExact($_.Line.Substring(0,23), 'yyyy-MM-dd HH:mm:ss.fff', $null) -ge $t0 } | % { $_.Line.Substring(11, [Math]::Min(120, $_.Line.Length - 11)) }
