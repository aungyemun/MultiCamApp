# UI tour: screenshots of every page (English and Japanese), page-switch latency, About dialog,
# and UI-freeze log lines during the tour. Runs UI Automation in-process; the caller should wrap
# this script in Invoke-Timed. Writes <runs>\tour_*.png and <runs>\ui_tour.json.
param([string]$Cam1 = 'c922 Pro Stream Webcam', [string]$Cam2 = 'Integrated Webcam (Built-in front)', [int]$Switches = 5)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TW { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[TW]::SetProcessDPIAware() | Out-Null
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]
$out = $McRuns; $r = [ordered]@{ shots = @(); switchMs = @(); errors = @() }
function Cond($p, $v) { New-Object Windows.Automation.PropertyCondition($p, $v) }
function Proc { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1 }
function Win { $AE::FromHandle($script:mainH) }
function ById($id) { $e = (Win).FindFirst($Scope::Descendants, (Cond $AE::AutomationIdProperty $id)); if (-not $e) { throw "not found: $id" }; $e }
function Click($id) { (ById $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Sel($box, $name) { $e = ById $box; $ec = $e.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern); $ec.Expand(); Start-Sleep -Milliseconds 300
  $hit = $e.FindAll($Scope::Descendants, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::ListItem))) | ? { $_.Current.Name -eq $name } | Select -First 1
  if (-not $hit) { $ec.Collapse(); throw "item '$name' not in $box" }
  $hit.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $ec.Collapse() }
function Radio($name) { $e = (Win).FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition((Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::RadioButton)), (Cond $AE::NameProperty $name))))
  $e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Visible($id) { try { -not (ById $id).Current.IsOffscreen } catch { $false } }
function ShotH($h, $tag) { $rc = New-Object TW+R; [TW]::GetWindowRect($h, [ref]$rc) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($rc.Rr - $rc.L), ($rc.B - $rc.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [TW]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $f = Join-Path $out "tour_$tag.png"; $bmp.Save($f, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $script:r.shots += "tour_$tag.png" }
function Shot($tag) { ShotH $script:mainH $tag }
function Step($name, [scriptblock]$b) { try { & $b } catch { $script:r.errors += "${name}: $_" } }
# Page switch: click the nav button, then poll until the target page's marker element is on screen.
function NavTo($nav, $marker) { $sw = [Diagnostics.Stopwatch]::StartNew(); Click $nav
  while ($sw.ElapsedMilliseconds -lt 5000 -and -not (Visible $marker)) { Start-Sleep -Milliseconds 10 }
  $reach = [int]$sw.ElapsedMilliseconds
  # Cost of one lookup on the already-visible page: the switch time includes at least this much
  # UI Automation overhead, so (reach - query) is the upper bound of the app's own switch time.
  $q = [Diagnostics.Stopwatch]::StartNew(); $null = Visible $marker
  "{0}(query {1})" -f $reach, [int]$q.ElapsedMilliseconds }
function AboutShot($tag) {
  Click 'AboutButton'; Start-Sleep 2
  $pid0 = (Proc).Id
  $wins = [Windows.Automation.AutomationElement]::RootElement.FindAll($Scope::Children, (Cond $AE::ProcessIdProperty $pid0))
  # The main window handle is fixed at launch: Process.MainWindowHandle can point at the dialog while it is active.
  $dlg = $wins | ? { $_.Current.NativeWindowHandle -ne $script:mainH.ToInt32() } | Select -First 1
  if (-not $dlg) { $dlg = (Win).FindFirst($Scope::Children, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) }
  if (-not $dlg) { throw 'About dialog not found' }
  ShotH ([IntPtr]$dlg.Current.NativeWindowHandle) $tag
  $dlg.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep 1 }
$ja = -join [char[]](0x65E5, 0x672C, 0x8A9E)
$logs = Join-Path (Split-Path $McExe -Parent) 'logs'
$tStart = Get-Date

# Close earlier instances that still have a window; a windowless instance stuck in driver teardown cannot be killed and is skipped.
Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
for ($i = 0; $i -lt 40; $i++) { if (Visible 'StartPreviewBtn') { break }; Start-Sleep 1 }
Start-Sleep 3
Step 'english' { Sel 'LanguageBox' 'English'; Start-Sleep 1 }
foreach ($lang in 'en', 'ja') {
  if ($lang -eq 'ja') { Step 'ja' { Click 'MainNavBtn'; Start-Sleep 1; Sel 'LanguageBox' $ja; Start-Sleep 2 } }
  Step "$lang main" { Shot "${lang}_main_idle" }
  Step "$lang advanced" { ((ById 'AdvancedCameraControlsExpander').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand(); Start-Sleep 1; Shot "${lang}_main_advanced"
    ((ById 'AdvancedCameraControlsExpander').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Collapse() }
  if ($lang -eq 'en') {
    Step 'preview' { Radio '2 Cameras'; Start-Sleep 1; Sel 'Cam1Box' $Cam1; Sel 'Cam2Box' $Cam2; Sel 'ResolutionBox' '720p'; Sel 'FpsBox' '30 fps'
      Click 'StartPreviewBtn'; for ($i = 0; $i -lt 60; $i++) { if ((ById 'StartRecordBtn').Current.IsEnabled) { break }; Start-Sleep 1 }; Start-Sleep 4 }
  }
  Step "$lang preview shot" { Shot "${lang}_main_preview" }
  for ($k = 0; $k -lt $Switches; $k++) {
    Step "$lang switch $k" {
      $a = NavTo 'VerifyNavBtn' 'ScanBtn'; if ($k -eq 0) { Start-Sleep 1; Shot "${lang}_verify" }
      $b = NavTo 'HardwareNavBtn' 'RunScanButton'; if ($k -eq 0) { Start-Sleep 1; Shot "${lang}_hardware" }
      $c = NavTo 'MainNavBtn' 'StartPreviewBtn'
      $script:r.switchMs += "$lang verify=$a hardware=$b main=$c" }
  }
  Step "$lang about" { AboutShot "${lang}_about" }
}
Step 'stop preview' { Click 'MainNavBtn'; Click 'StopPreviewBtn'; for ($i = 0; $i -lt 30; $i++) { if ((ById 'StartPreviewBtn').Current.IsEnabled) { break }; Start-Sleep 1 } }
Step 'back to english' { Sel 'LanguageBox' 'English'; Start-Sleep 1 }
$p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } }
# UI freezes logged by the app's watchdog during the tour
$rl = Get-ChildItem $logs -Filter 'recording_runtime_*.log' -ErrorAction SilentlyContinue | Sort LastWriteTime -Desc | Select -First 1
$al = Get-ChildItem $logs -Filter 'app_runtime_*.txt' -ErrorAction SilentlyContinue | Sort LastWriteTime -Desc | Select -First 1
$r.freezeLines = @(foreach ($l in @($rl, $al) | ? { $_ }) { Select-String $l.FullName -Pattern 'UI_FREEZE_(MINOR|WARNING|CRITICAL|WATCHDOG_STOPPED)' | ? {
  $ts = $_.Line.Substring(0, 23); try { [datetime]::ParseExact($ts, 'yyyy-MM-dd HH:mm:ss.fff', $null) -ge $tStart } catch { $false } } | % { $_.Line.Substring(11) } })
$r | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'ui_tour.json') -Encoding UTF8
$r | ConvertTo-Json -Depth 4
