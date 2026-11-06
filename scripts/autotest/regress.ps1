# Full regression on real cameras. Every child process has a hard time limit. Output: runs\regress.log (this script's stdout).
# Camera names are the Windows device names; pass your own with -C/-B/-J1/-J2 (layouts: L1=C, L2=C+B, L3=C+B+J1, L4=B+J1+J2+C).
param([int]$Seconds = 30, [string]$C = 'c922 Pro Stream Webcam', [string]$B = 'Integrated Webcam (Built-in front)',
      [string]$J1 = 'j5 Webcam JVU250 #1', [string]$J2 = 'j5 Webcam JVU250 #2')
$d = $PSScriptRoot; . "$d\lib.ps1"; $root = $McRoot; $s = "$root\source\MultiCamApp"
$py = $McPy; $ff = Join-Path $McFfDir 'ffmpeg.exe'
$logs = "$s\MultiCamApp\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\logs"
$env:PYTHONIOENCODING = 'utf-8'
"=== BUILD + UNIT TESTS $(Get-Date -Format HH:mm:ss)"
$cmd = ". '$root\env\activate.ps1' | Out-Null; dotnet build '$s\MultiCamApp.sln' -c Release -p:Platform=x64 -nologo -v q 2>&1 | Select-String -Pattern 'error |Build succeeded|Error\(s\)' | Select -Unique | Select -First 10; dotnet test '$s\MultiCamApp.sln' -c Release -p:Platform=x64 --no-build -nologo 2>&1 | Select-String -Pattern 'Passed!|Failed!|\[FAIL\]' | Select -First 5; dotnet build '$d\AuditCheck' -c Release -nologo -v q 2>&1 | Select-String 'error |Build succeeded' | Select -First 3"
$b = Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-Command',$cmd) 900
$b.Out.Trim() -replace [regex]::Escape($root),'<root>'
if ($b.Out -notmatch 'Passed!') { "ABORT: build/tests failed"; return }
$ac = (Get-ChildItem "$d\AuditCheck\bin" -Recurse -Filter AuditCheck.exe | Select -First 1).FullName

"=== WARM-UP: two starts in one session $(Get-Date -Format HH:mm:ss)"
(Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\restart_test.ps1",'-Times','2','-Name','r02_warmup') 600).Out.Trim()
$wl = Get-ChildItem $logs -Filter 'recording_runtime_*.log' | Sort LastWriteTime -Desc | Select -First 1
Select-String $wl.FullName -Pattern 'V2_ENCODER_WARMUP' | Select -Last 1 | % { $_.Line.Substring(11) }

$plan = @(
  @{ n='L1_1cam_c922_1080p30';        L='1 Camera';  c=@($C);             r='1080p'; f='30 fps'; w=1920; h=1080; fps=30 },
  @{ n='L2_2cam_c922_builtin_720p30'; L='2 Cameras'; c=@($C,$B);          r='720p';  f='30 fps'; w=1280; h=720;  fps=30 },
  @{ n='L3_3cam_c922_builtin_j5_720p30'; L='3 Cameras'; c=@($C,$B,$J1);   r='720p';  f='30 fps'; w=1280; h=720;  fps=30 },
  @{ n='L4_4cam_all_480p30';          L='4 Cameras'; c=@($B,$J1,$J2,$C);  r='480p';  f='30 fps'; w=640;  h=480;  fps=30 }
)
foreach ($t in $plan) {
  "=== $($t.n) $(Get-Date -Format HH:mm:ss)"
  $a = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\run_rec.ps1",'-Name',$t.n,'-Layout',$t.L,'-Res',$t.r,'-Fps',$t.f,'-Seconds',$Seconds)
  for ($i=0; $i -lt $t.c.Count; $i++) { $a += "-Cam$($i+1)"; $a += $t.c[$i] }
  $rr = Invoke-Timed powershell.exe $a ($Seconds + 420)
  Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\mcui.ps1",'close') 60 | Out-Null
  $run = Get-Content "$McRuns\$($t.n)_run.json" -Raw | ConvertFrom-Json
  "run ok=$($run.ok) err=$($run.error) launch=$($run.appLaunchToUiSec)s preview=$($run.previewReadySec)s ramMax=$($run.ramMB.max)MB"
  $ss = Get-ChildItem $McVideos -Directory -Filter "$($t.n)_*" | Sort LastWriteTime -Desc | Select -First 1
  $al = Get-ChildItem $logs -Filter 'app_runtime_*.txt' | Sort LastWriteTime -Desc | Select -First 1
  Select-String $al.FullName -Pattern "V2_DEFAULT_RECORDING_STARTED.*$($ss.Name)" | Select -Last 1 | % { "  " + ($_.Line -replace '.*(startMs=\d+).*','$1') }
  Invoke-Timed $py @("$d\analyze.py", $ss.FullName, $t.w, $t.h, $t.fps, '--quality') 1200 | Out-Null
  Copy-Item "$($ss.FullName)\_autotest_analysis.json" "$McRuns\$($t.n)_analysis.json" -ErrorAction SilentlyContinue
  (& $py -I -W ignore "$d\summ_runs.py" $t.n) | Select -Skip 1
  (Invoke-Timed $ac @($ff, $ss.FullName, '20') 600).Out -split "`r?`n" | ? { $_.Trim().StartsWith('{') } | % { $o = $_ | ConvertFrom-Json; "  visual {0}: bands={1}% flashes={2} noise={3} verdict={4}" -f $o.file, $o.BandStrengthPct, $o.FlashFrames, $o.TemporalNoiseYdif, $o.Verdict }
  Get-ChildItem $ss.FullName -Directory | % { $m = "$($_.FullName)\$($_.Name)_metadata.txt"; if (Test-Path $m) { "  meta $($_.Name): " + ((Select-String $m -Pattern 'Preview measured FPS|Max UI freeze|Camera input subtype|Focus during recording|Focus position|Auto exposure during recording|Anti-flicker requested|Rate control' | % { $_.Line.Trim('- ') }) -join ' | ') } }
}

"=== CRASH TEST $(Get-Date -Format HH:mm:ss)"
$cr = Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\crash_test.ps1",'-Name','cx03_regress_kill') 400
$cj = $cr.Out | ConvertFrom-Json; $cj.videos | % { "  $($_.file) exit=$($_.exit) $($_.probe)" }; $cj.csvRows | % { "  $($_.file) rows=$($_.rows)" }
$exe = $McExe
Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\mcui.ps1",'launch',$exe) 90 | Out-Null; Start-Sleep 12
"  status after relaunch: " + (Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\mcui.ps1",'read','StatusValue') 45).Out.Trim()
Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\mcui.ps1",'close') 60 | Out-Null
$cs = Get-ChildItem $McVideos -Directory -Filter 'cx03_regress_kill_*' | Sort LastWriteTime -Desc | Select -First 1
Get-Content "$($cs.FullName)\RECOVERY_REPORT.txt" -ErrorAction SilentlyContinue | Select-String 'Status:|\] |Video:|CSV rows' | % { "  " + $_.Line.Trim() }

"=== UI PAGE TESTS $(Get-Date -Format HH:mm:ss)"
$u = Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$d\ui_tests.ps1") 1800
$uj = $u.Out | ConvertFrom-Json
"  verify: " + ($uj.cards -join ' | ')
"  hardware: " + ($uj.hwAfterScan -join ' | ') + " / copy: $($uj.hwAfterCopy) / persist: $($uj.hwStatusPersist)"
"  ja: " + ($uj.jaStatus -join ' | ') + "  errors: " + ($uj.errors -join ' ; ')
"=== DONE $(Get-Date -Format HH:mm:ss)"
