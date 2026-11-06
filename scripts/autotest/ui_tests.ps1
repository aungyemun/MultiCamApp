# UI function tests (no recording). Writes runs\ui_*.png and runs\ui_tests.json. Every child call has a hard timeout.
$here = $PSScriptRoot; . "$here\lib.ps1"; $out = $McRuns
$exe = $McExe
$errors = New-Object Collections.ArrayList
function U { $t = if ($args[0] -eq 'launch') { 90 } else { 45 }
  $res = Invoke-Timed powershell.exe (@('-NoProfile','-ExecutionPolicy','Bypass','-File',"$here\mcui.ps1") + $args) $t
  if ($res.ExitCode -ne 0) { [void]$errors.Add("$($args -join ' ') -> exit $($res.ExitCode): $($res.Err.Trim()) $($res.Out.Trim())") }
  $res.Out -split "`r?`n" | Where-Object { $_ -ne '' } }
function Shot($t) { Invoke-Timed powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$here\capwin.ps1",'-Out',"$out\ui_$t.png",'-Keep') 30 | Out-Null }
$ja = -join [char[]](0x65E5, 0x672C, 0x8A9E)   # Japanese language name (built from code points: file stays ASCII)
$r = [ordered]@{}
$deadline = (Get-Date).AddMinutes(25)          # whole-script budget
U launch $exe | Out-Null
# Video Verification: scan + verify all on the default folder (contains all test sessions)
U click VerifyNavBtn | Out-Null; Start-Sleep 2
U click ScanBtn | Out-Null; Start-Sleep 8
$r.afterScan = U read 'ProgressText,CardFoundValue'
$t0 = Get-Date; U click VerifyAllBtn | Out-Null
$verifyEnd = (Get-Date).AddMinutes(15)
for ($i = 0; (Get-Date) -lt $verifyEnd; $i++) { Start-Sleep 3; if ((U enabled VerifyAllBtn) -eq 'True' -and $i -gt 2) { break } }
$r.verifyAllSec = [int]((Get-Date) - $t0).TotalSeconds
$r.verifyAllTimedOut = (Get-Date) -ge $verifyEnd
$r.cards = U read 'ProgressText,CardOverallValue,CardFoundValue,CardPassValue,CardWarnValue,CardFailValue,CardTimingConfidenceValue,CardTimingModeValue,CardOriginalFramesValue,CardDuplicateFramesValue,CardTimestampCsvValue'
Shot 'verify_all'
if ((Get-Date) -lt $deadline) {
  # Hardware diagnostics: scan, copy summary, status must persist
  U click HardwareNavBtn | Out-Null; U click RunScanButton | Out-Null; Start-Sleep 15
  $r.hwAfterScan = U read 'StatusText,LastScanText'
  U click CopySummaryButton | Out-Null; Start-Sleep 2
  $r.hwAfterCopy = U read 'StatusText'
  Start-Sleep 3; $r.hwStatusPersist = U read 'StatusText'
  Shot 'hardware'
  # Main: layout cycling and language switch
  U click MainNavBtn | Out-Null
  foreach ($l in '4 Cameras','3 Cameras','1 Camera','2 Cameras') { U radio $l | Out-Null; Start-Sleep 1; $r["layout_$l"] = U read 'Cam1Box,Cam2Box,Cam3Box,Cam4Box' }
  Shot 'layout2_idle'
  $r.languageItems = U items LanguageBox
  U select LanguageBox $ja | Out-Null; Start-Sleep 2; Shot 'ja_main'
  $r.jaStatus = U read 'StatusValue,CamerasValue'
  U click VerifyNavBtn | Out-Null; Start-Sleep 2; Shot 'ja_verify'
  U click MainNavBtn | Out-Null; U select LanguageBox 'English' | Out-Null
} else { [void]$errors.Add('script deadline reached before hardware/layout/language steps') }
U close | Out-Null
$r.errors = @($errors)
$r | ConvertTo-Json -Depth 4 | Set-Content "$out\ui_tests.json" -Encoding UTF8
$r | ConvertTo-Json -Depth 4
