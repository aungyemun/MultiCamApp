# Sudden-close test: record, force-kill the app mid-recording, inspect what survives. Writes runs\<Name>_crash.json
param([string]$Name = 'cx01_kill_midrec', [string]$Cam1 = 'j5 Webcam JVU250 #1', [string]$Cam2 = 'c922 Pro Stream Webcam',
      [string]$Res = '720p', [string]$Fps = '30 fps', [int]$RecordSeconds = 15, [string]$Videos = '')
$d = $PSScriptRoot; . "$d\lib.ps1"; if (-not $Videos) { $Videos = $McVideos }
$exe = $McExe
$ffprobe = Join-Path $McFfDir 'ffprobe.exe'
$r = [ordered]@{ name = $Name; errors = @() }
function U { $t = if ($args[0] -eq 'launch') { 90 } elseif ($args[0] -eq 'waitenabled') { [int]$args[2] + 30 } else { 45 }
  $res = Invoke-Timed powershell.exe (@('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\mcui.ps1") + $args) $t
  if ($res.ExitCode -ne 0) { $script:r.errors += "$($args -join ' '): $($res.Err.Trim())" }
  $res.Out -split "`r?`n" | Where-Object { $_ -ne '' } }
U launch $exe | Out-Null
U radio '2 Cameras' | Out-Null; U select Cam1Box $Cam1 | Out-Null; U select Cam2Box $Cam2 | Out-Null
U select ResolutionBox $Res | Out-Null; U select FpsBox $Fps | Out-Null; U text SessionBox $Name | Out-Null
U click StartPreviewBtn | Out-Null; U waitenabled StartRecordBtn 60 | Out-Null; Start-Sleep 3
U click StartRecordBtn | Out-Null; U waitenabled StopRecordBtn 30 | Out-Null
Start-Sleep $RecordSeconds
$p = Get-Process MultiCamApp -ErrorAction SilentlyContinue | Select -First 1
$r.killedPid = $p.Id; $r.killedAt = (Get-Date).ToString('HH:mm:ss.fff')
& taskkill.exe /F /PID $p.Id | Out-Null            # sudden close: no shutdown code runs
Start-Sleep 3
$s = Get-ChildItem $Videos -Directory -Filter "${Name}_*" | Sort LastWriteTime -Desc | Select -First 1
$r.session = $s.Name
$r.files = @(Get-ChildItem $s.FullName -Recurse -File | % { [ordered]@{ file = $_.FullName.Substring($s.FullName.Length + 1); bytes = $_.Length } })
$r.videos = @(Get-ChildItem $s.FullName -Recurse -File -Include *.mp4 | % {
  $pr = Invoke-Timed $ffprobe @('-v','error','-count_frames','-select_streams','v:0','-show_entries','stream=nb_read_frames,width,height:format=duration','-of','json',$_.FullName) 120
  [ordered]@{ file = $_.Name; exit = $pr.ExitCode; probe = ($pr.Out -replace '\s+',' ').Trim(); error = $pr.Err.Trim() } })
$r.csvRows = @(Get-ChildItem $s.FullName -Recurse -File -Include *timestamps*.csv | % { [ordered]@{ file = $_.Name; rows = ((Get-Content $_.FullName | Measure-Object -Line).Lines - 1) } })
$r | ConvertTo-Json -Depth 5 | Set-Content "$McRuns\${Name}_crash.json" -Encoding UTF8
$r | ConvertTo-Json -Depth 5
