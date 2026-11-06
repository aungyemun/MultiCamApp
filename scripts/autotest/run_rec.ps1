# One real recording through the UI. Writes <OutDir>\<Name>_run.json with timings and UI readbacks.
param([string]$Name, [string]$Cam1, [string]$Cam2, [string]$Cam3, [string]$Cam4, [string]$Res = '720p', [string]$Fps = '30 fps', [int]$Seconds = 60,
      [string]$Layout = '2 Cameras', [string]$Flicker = '', [string]$OutDir = '', [switch]$NoLaunch, [switch]$Lock, [string]$Quality = '')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"; $exe = $McExe; if (-not $OutDir) { $OutDir = $McRuns }
New-Item -ItemType Directory $OutDir -Force | Out-Null
. "$PSScriptRoot\lib.ps1"
# Every UI-automation call runs in a child with a hard timeout (waitenabled: its own wait + 30 s; others: 60 s).
function U { $t = if ($args[0] -eq 'waitenabled') { [int]$args[2] + 30 } elseif ($args[0] -eq 'launch') { 90 } else { 60 }
  $res = Invoke-Timed powershell.exe (@('-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSScriptRoot\mcui.ps1") + $args) $t
  if ($res.ExitCode -ne 0) { throw "mcui $($args -join ' ') failed (exit $($res.ExitCode)): $($res.Err.Trim()) $($res.Out.Trim())" }
  $res.Out -split "`r?`n" | Where-Object { $_ -ne '' } }
function Shot($tag) { Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSScriptRoot\capwin.ps1",'-Out',"$OutDir\${Name}_$tag.png",'-Keep') 30 | Out-Null }
$r = [ordered]@{ name = $Name; res = $Res; fps = $Fps; seconds = $Seconds; layout = $Layout; cam1 = $Cam1; cam2 = $Cam2; steps = @() }
function Step($s) { $r.steps += "{0:HH:mm:ss.fff} {1}" -f (Get-Date), $s }
$sw = [Diagnostics.Stopwatch]::StartNew()
# RAM/CPU sampler (1 Hz) for the whole run
$memFile = "$OutDir\${Name}_mem.csv"
$job = Start-Job -ArgumentList $memFile, ($Seconds + 900) -ScriptBlock { param($f, $maxSec) 'time,workingSetMB,privateMB,cpuSec,threads,handles' | Set-Content $f
  $end = (Get-Date).AddSeconds($maxSec)   # self-terminates even if the parent is killed
  while ((Get-Date) -lt $end) { $p = Get-Process MultiCamApp -ErrorAction SilentlyContinue | Select -First 1
    if ($p) { '{0:HH:mm:ss},{1:F1},{2:F1},{3:F2},{4},{5}' -f (Get-Date), ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB), $p.TotalProcessorTime.TotalSeconds, $p.Threads.Count, $p.HandleCount | Add-Content $f }
    Start-Sleep 1 } }
try {
  if (-not $NoLaunch) { $tl = $sw.Elapsed.TotalSeconds; U launch $exe | Out-Null; $r.appLaunchToUiSec = [math]::Round($sw.Elapsed.TotalSeconds - $tl, 2); Step 'launched' }
  U radio $Layout | Out-Null
  U select Cam1Box $Cam1 | Out-Null
  if ($Cam2) { U select Cam2Box $Cam2 | Out-Null }
  if ($Cam3) { U select Cam3Box $Cam3 | Out-Null }
  if ($Cam4) { U select Cam4Box $Cam4 | Out-Null }
  U select ResolutionBox $Res | Out-Null
  U select FpsBox $Fps | Out-Null
  if ($Flicker) { U expand AdvancedCameraControlsExpander | Out-Null; Start-Sleep -Milliseconds 500; U select FrequencyBox $Flicker | Out-Null; $r.flicker = $Flicker }
  if ($Quality) { U expand AdvancedCameraControlsExpander | Out-Null; Start-Sleep -Milliseconds 500; U select QualityBox $Quality | Out-Null; $r.quality = (U read QualityBox) }
  U text SessionBox $Name | Out-Null
  $r.uiSelection = (U read 'Cam1Box,Cam2Box,Cam3Box,Cam4Box,ResolutionBox,FpsBox')
  $r.sessionPreview = (U read SessionFolderPreviewValue)
  Step 'configured'
  $t0 = $sw.Elapsed.TotalSeconds; U click StartPreviewBtn | Out-Null
  U waitenabled StartRecordBtn 60 | Out-Null
  $r.previewReadySec = [math]::Round($sw.Elapsed.TotalSeconds - $t0, 2); Step 'preview ready'
  Start-Sleep 4; $r.previewStatus = (U read 'StatusValue,PreviewStatusValue,CamerasValue'); Shot 'preview'
  if ($Lock) { U click LockEnvironmentButton | Out-Null; Start-Sleep 4; $r.lockStatus = (U read 'CalibrationStatusLabel'); Shot 'locked' }
  $t1 = $sw.Elapsed.TotalSeconds; U click StartRecordBtn | Out-Null
  U waitenabled StopRecordBtn 30 | Out-Null
  $r.recordStartUiSec = [math]::Round($sw.Elapsed.TotalSeconds - $t1, 2); Step 'recording'
  Start-Sleep ([math]::Max(1, [int]($Seconds / 2))); $r.midStatus = (U read 'StatusValue,ElapsedValue,SessionTimeValue,CamerasValue'); Shot 'recording'
  Start-Sleep ([math]::Max(1, $Seconds - [int]($Seconds / 2)))
  $t2 = $sw.Elapsed.TotalSeconds; U click StopRecordBtn | Out-Null
  U waitenabled StartRecordBtn 120 | Out-Null
  $r.recordStopUiSec = [math]::Round($sw.Elapsed.TotalSeconds - $t2, 2); Step 'stopped'
  Start-Sleep 3; $r.afterStatus = (U read 'StatusValue,ElapsedValue,PreviewStatusValue,CamerasValue'); Shot 'after'
  $t3 = $sw.Elapsed.TotalSeconds; U click StopPreviewBtn | Out-Null
  U waitenabled StartPreviewBtn 60 | Out-Null
  $r.stopPreviewSec = [math]::Round($sw.Elapsed.TotalSeconds - $t3, 2); Step 'preview stopped'
  $r.ok = $true
} catch { $r.ok = $false; $r.error = "$_"; Step "ERROR $_"; try { Shot 'error' } catch {} }
Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue
$m = Import-Csv $memFile -ErrorAction SilentlyContinue
if ($m) { $ws = $m | % { [double]$_.workingSetMB }; $r.ramMB = [ordered]@{ min = ($ws | Measure -Minimum).Minimum; max = ($ws | Measure -Maximum).Maximum; last = $ws[-1]; samples = $ws.Count } }
$r | ConvertTo-Json -Depth 4 | Set-Content "$OutDir\${Name}_run.json" -Encoding UTF8
$r | ConvertTo-Json -Depth 4
