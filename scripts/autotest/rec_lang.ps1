# One recording with the UI switched to a given language before preview/recording starts, so the
# per-camera metadata text is written in that language. Controls are selected while the UI is in
# English (names are language-specific), then the language is switched; start/stop use automation
# IDs. Every UI call runs in a time-limited child. Usage: rec_lang.ps1 -Name x -Lang ja -Cam1 .. -Cam2 ..
param([string]$Name, [ValidateSet('en','ja')][string]$Lang = 'ja', [string]$Cam1, [string]$Cam2,
      [string]$Res = '720p', [string]$Fps = '30 fps', [int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
function U { $t = if ($args[0] -eq 'waitenabled') { [int]$args[2] + 30 } elseif ($args[0] -eq 'launch') { 90 } else { 60 }
  $res = Invoke-Timed powershell.exe (@('-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSScriptRoot\mcui.ps1") + $args) $t
  if ($res.ExitCode -ne 0) { throw "mcui $($args -join ' ') failed: $($res.Err.Trim()) $($res.Out.Trim())" }
  $res.Out -split "`r?`n" | Where-Object { $_ -ne '' } }
$langName = if ($Lang -eq 'ja') { -join [char[]](0x65E5, 0x672C, 0x8A9E) } else { 'English' }
$r = [ordered]@{ name = $Name; lang = $Lang; ok = $false }
try {
  U launch $McExe | Out-Null
  U select LanguageBox 'English' | Out-Null
  U radio $(if ($Cam2) { '2 Cameras' } else { '1 Camera' }) | Out-Null
  U select Cam1Box $Cam1 | Out-Null
  if ($Cam2) { U select Cam2Box $Cam2 | Out-Null }
  U select ResolutionBox $Res | Out-Null
  U select FpsBox $Fps | Out-Null
  U text SessionBox $Name | Out-Null
  U select LanguageBox $langName | Out-Null; Start-Sleep 2
  U click StartPreviewBtn | Out-Null
  U waitenabled StartRecordBtn 60 | Out-Null; Start-Sleep 3
  U click StartRecordBtn | Out-Null
  U waitenabled StopRecordBtn 30 | Out-Null
  Start-Sleep $Seconds
  U click StopRecordBtn | Out-Null
  U waitenabled StartRecordBtn 120 | Out-Null; Start-Sleep 3
  $r.statusAfter = U read 'StatusValue,CamerasValue'
  U click StopPreviewBtn | Out-Null
  U waitenabled StartPreviewBtn 60 | Out-Null
  U select LanguageBox 'English' | Out-Null
  $r.ok = $true
} catch { $r.error = "$_" }
try { U close | Out-Null } catch {}
$r | ConvertTo-Json -Depth 3
