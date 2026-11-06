# Manual-exposure path test through the UI (C922, 720p60). Writes runs\<Name>_run.json. All child calls time-limited.
param([string]$Name = 'x01_c922_720p60_manualexp', [string]$Cam = 'c922 Pro Stream Webcam', [string]$Value = '142', [int]$Seconds = 20)
$here = $PSScriptRoot; . "$here\lib.ps1"; $out = $McRuns
$exe = $McExe
$r = [ordered]@{ name = $Name; value = $Value; errors = @() }
function U { $t = if ($args[0] -eq 'launch') { 90 } elseif ($args[0] -eq 'waitenabled') { [int]$args[2] + 30 } else { 45 }
  $res = Invoke-Timed powershell.exe (@('-NoProfile','-ExecutionPolicy','Bypass','-File',"$here\mcui.ps1") + $args) $t
  if ($res.ExitCode -ne 0) { $script:r.errors += "$($args -join ' '): $($res.Err.Trim()) $($res.Out.Trim())" }
  $res.Out -split "`r?`n" | Where-Object { $_ -ne '' } }
function Shot($t) { Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$here\capwin.ps1",'-Out',"$out\${Name}_$t.png",'-Keep') 30 | Out-Null }
U launch $exe | Out-Null
U radio '1 Camera' | Out-Null; U select Cam1Box $Cam | Out-Null; U select ResolutionBox '720p' | Out-Null; U select FpsBox '60 fps' | Out-Null
U expand AdvancedCameraControlsExpander | Out-Null; Start-Sleep 1; U select FrequencyBox '50 Hz' | Out-Null
U text SessionBox $Name | Out-Null
U click StartPreviewBtn | Out-Null; U waitenabled StartRecordBtn 60 | Out-Null; Start-Sleep 4
$r.before = U read 'AdvancedAutoExposureCheckBox,ManualExposureValueBox,ExposureStatusText'
U check AdvancedAutoExposureCheckBox false | Out-Null
U text ManualExposureValueBox $Value | Out-Null; Start-Sleep 1
U click ApplyExposureSettingButton | Out-Null; Start-Sleep 4
$r.afterApply = U read 'AdvancedAutoExposureCheckBox,ManualExposureValueBox,ExposureStatusText'
Shot 'applied'
U click StartRecordBtn | Out-Null; U waitenabled StopRecordBtn 30 | Out-Null
Start-Sleep $Seconds; Shot 'recording'
U click StopRecordBtn | Out-Null; U waitenabled StartRecordBtn 120 | Out-Null
U click StopPreviewBtn | Out-Null; U waitenabled StartPreviewBtn 60 | Out-Null
U close | Out-Null
$r | ConvertTo-Json -Depth 4 | Set-Content "$out\${Name}_run.json" -Encoding UTF8
$r | ConvertTo-Json -Depth 4
