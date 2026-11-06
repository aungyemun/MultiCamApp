# UI-freeze measurement for Verify All on a large folder (no Deep Verify). Reads the app's own
# watchdog lines (UI_FREEZE_*, UI_HEARTBEAT_LATENCY_HIGH) and VERIFY_UI_PHASE_TIMING from the runtime log.
# Usage: big_verify.ps1 -Folder <copy of sessions>   (Verify All saves video_audit_report.txt into it)
param([Parameter(Mandatory)][string]$Folder, [int]$TimeoutSec = 900)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]
function Proc { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1 }
function ById($id) { $e = $AE::FromHandle($script:mainH).FindFirst($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id))); if (-not $e) { throw "not found: $id" }; $e }
function Click($id) { (ById $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
$env:MULTICAMAPP_TEST_VERIFY_FOLDER = (Resolve-Path $Folder).Path
Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
$t0 = Get-Date
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
for ($i = 0; $i -lt 40; $i++) { try { $null = ById 'StartPreviewBtn'; break } catch { Start-Sleep 1 } }
Start-Sleep 2
$r = [ordered]@{}
$sw = [Diagnostics.Stopwatch]::StartNew(); Click 'VerifyNavBtn'
for ($i = 0; $i -lt 120; $i++) { if ((ById 'VerifyAllBtn').Current.IsEnabled -and ((ById 'ProgressText').Current.Name -match '\d')) { break }; Start-Sleep 1 }
$r.scanSec = [int]$sw.Elapsed.TotalSeconds; Start-Sleep 3
$tVerify = Get-Date; $sw.Restart(); Click 'VerifyAllBtn'; Start-Sleep 2
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) { if ((ById 'VerifyAllBtn').Current.IsEnabled) { break }; Start-Sleep 1 }
$r.verifyAllSec = [int]$sw.Elapsed.TotalSeconds
Start-Sleep 3
$p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } }
$log = Get-ChildItem (Join-Path (Split-Path $McExe -Parent) 'logs') -Filter 'app_runtime_*.txt' | Sort LastWriteTime -Desc | Select -First 1
$lines = Get-Content $log.FullName | ? { try { [datetime]::ParseExact($_.Substring(0, 19), 'yyyy-MM-dd HH:mm:ss', $null) -ge $t0.AddSeconds(-1) } catch { $false } }
$r.verifyStartedAt = $tVerify.ToString('HH:mm:ss')
$r.freezes = @($lines | Select-String 'UI_FREEZE|UI_HEARTBEAT_LATENCY_HIGH' | % { $_.Line.Substring(11) })
$r.phaseTiming = @($lines | Select-String 'VERIFY_UI_PHASE_TIMING' | % { $_.Line.Substring(11) })
$r | ConvertTo-Json -Depth 4
