# After Verify All + Deep Verify on a large folder: is every session reachable by scrolling, and what
# does the page say for failed sessions? UI Automation only (no mouse or keyboard input).
# Usage: scroll_check.ps1 -Folder <copy of sessions> [-FailFilter cx0]
param([Parameter(Mandatory)][string]$Folder, [string]$FailFilter = 'cx0', [int]$DeepTimeoutSec = 1800, [switch]$SkipDeep)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SC { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[SC]::SetProcessDPIAware() | Out-Null
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]
function Cond($p, $v) { New-Object Windows.Automation.PropertyCondition($p, $v) }
function Proc { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1 }
function Win { $AE::FromHandle($script:mainH) }
function ById($id) { $e = (Win).FindFirst($Scope::Descendants, (Cond $AE::AutomationIdProperty $id)); if (-not $e) { throw "not found: $id" }; $e }
function Click($id) { (ById $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function WaitEnabled($id, $sec) { $sw = [Diagnostics.Stopwatch]::StartNew(); Start-Sleep 2; while ($sw.Elapsed.TotalSeconds -lt $sec) { if ((ById $id).Current.IsEnabled) { return [int]$sw.Elapsed.TotalSeconds }; Start-Sleep 2 }; throw "$id not enabled" }
function Shot($name) { $rc = New-Object SC+R; [SC]::GetWindowRect($script:mainH, [ref]$rc) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($rc.Rr - $rc.L), ($rc.B - $rc.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [SC]::PrintWindow($script:mainH, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $McRuns "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }
function Groups { (Win).FindAll($Scope::Descendants, (Cond $AE::ClassNameProperty 'Expander')) }
function Scrollers { @((Win).FindAll($Scope::Descendants, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Pane))) | ? {
  try { $_.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern) | Out-Null; $true } catch { $false } }) }

$env:MULTICAMAPP_TEST_VERIFY_FOLDER = (Resolve-Path $Folder).Path
Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
$t0 = Get-Date
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
[SC]::SetWindowPos($script:mainH, [IntPtr]::Zero, 40, 40, 1600, 1000, 0x0004) | Out-Null
for ($i = 0; $i -lt 40; $i++) { try { $null = ById 'StartPreviewBtn'; break } catch { Start-Sleep 1 } }
Start-Sleep 2
$r = [ordered]@{}
Click 'VerifyNavBtn'; Start-Sleep 8
Click 'VerifyAllBtn'; $r.verifyAllSec = WaitEnabled 'VerifyAllBtn' 900
if (-not $SkipDeep) { Click 'DeepVerifyBtn'; $r.deepSec = WaitEnabled 'DeepVerifyBtn' $DeepTimeoutSec }; Start-Sleep 3
$r.deepCards = [ordered]@{}; foreach ($id in 'CardDeepVerifyStatusValue','CardDeepVerifyFilesValue','CardDeepVerifyDuplicatesValue','CardDeepVerifyWorstFileValue','CardVisualBandsValue','CardVisualFlashValue','CardVisualNoiseValue','CardOverallValue','CardPassValue','CardWarnValue','CardFailValue') { try { $r.deepCards[$id] = (ById $id).Current.Name } catch {} }

# Scroll ranges of every scrollable pane, then scroll each fully down and check the last session header.
$g = @(Groups); $r.sessionGroups = $g.Count; $r.expanded = @($g | ? { $_.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Current.ExpandCollapseState -eq 'Expanded' }).Count
$r.scrollers = @(); foreach ($s in Scrollers) { $p = $s.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).Current
  $r.scrollers += "id='$($s.Current.AutomationId)' vScrollable=$($p.VerticallyScrollable) viewPct=$([math]::Round($p.VerticalViewSize,1)) pos=$([math]::Round($p.VerticalScrollPercent,1))" }
Shot 'scroll_top'
foreach ($s in Scrollers) { $sp = $s.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern); if ($sp.Current.VerticallyScrollable) { $sp.SetScrollPercent(-1, 100) } }
Start-Sleep 2
$last = @(Groups)[-1]
$r.lastSession = $last.Current.Name
$r.lastSessionOffscreen = $last.Current.IsOffscreen
$wr = (Win).Current.BoundingRectangle; $lr = $last.Current.BoundingRectangle
$r.lastSessionInsideWindow = ($lr.Top -ge $wr.Top) -and ($lr.Bottom -le $wr.Bottom)
Shot 'scroll_bottom'

# Failed sessions: header and summary text as shown on the page.
$r.failed = @()
foreach ($e in @(Groups) | ? { $_.Current.Name -like "*$FailFilter*" }) {
  $e.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $texts = @($e.FindAll($Scope::Descendants, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | % { $_.Current.Name } | ? { $_ -and $_.Length -gt 1 } | Select -Unique)
  $r.failed += [ordered]@{ header = $e.Current.Name; texts = $texts } }
$p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } }
$log = Get-ChildItem (Join-Path (Split-Path $McExe -Parent) 'logs') -Filter 'app_runtime_*.txt' | Sort LastWriteTime -Desc | Select -First 1
$r.freezes = @(Get-Content $log.FullName | ? { try { [datetime]::ParseExact($_.Substring(0, 19), 'yyyy-MM-dd HH:mm:ss', $null) -ge $t0 } catch { $false } } | Select-String 'UI_FREEZE_(WARNING|CRITICAL)|LATENCY_HIGH|STOPPED' | % { $_.Line.Substring(11) } | Select -Last 12)
$out = Join-Path $McRuns 'scroll_check.json'
[IO.File]::WriteAllText($out, ($r | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
"wrote $out"
