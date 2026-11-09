# Raw screenshots for the guide images (docs/images/guide/*), used in README.md and docs/user_guide/getting_started.md.
# Video preview stays OFF (no camera images); verification uses demo sessions only.
# Every shot is saved with a JSON file of on-screen element rectangles (window-relative, physical px),
# which guide_graphics/annotate.py uses to place the numbered markers (see docs/images/README.md).
# Usage: guide_screenshots.ps1 -Folder <folder with demo sessions> -OutDir <raw output folder>
#        [-Camera '<device name>'] [-Layout '1 Camera']
# Tip: map the demo folder to a drive letter first (subst V: <folder>) so the folder box shows a short path.
# Note: the main page shows the Windows Videos folder (contains the user name); guide_graphics/annotate.py masks it.
param([Parameter(Mandatory)][string]$Folder, [Parameter(Mandatory)][string]$OutDir,
      [string]$Camera = 'Integrated Webcam (Built-in front)', [string]$Layout = '1 Camera',
      [int]$Width = 1600, [int]$Height = 1000, [switch]$AboutOnly)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
New-Item -ItemType Directory $OutDir -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class GW { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[GW]::SetProcessDPIAware() | Out-Null
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]; $CT = [Windows.Automation.ControlType]
function Cond($p, $v) { New-Object Windows.Automation.PropertyCondition($p, $v) }
function Proc { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1 }
function Win($h = $script:mainH) { $AE::FromHandle($h) }
function ById($id) { $e = (Win).FindFirst($Scope::Descendants, (Cond $AE::AutomationIdProperty $id)); if (-not $e) { throw "not found: $id" }; $e }
function Click($id) { (ById $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Sel($box, $name) { $e = ById $box; $ec = $e.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern); $ec.Expand(); Start-Sleep -Milliseconds 300
  $hit = $e.FindAll($Scope::Descendants, (Cond $AE::ControlTypeProperty $CT::ListItem)) | ? { $_.Current.Name -eq $name } | Select -First 1
  if (-not $hit) { $ec.Collapse(); throw "item '$name' not in $box" }
  $hit.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $ec.Collapse() }
function Radio($name) { $e = (Win).FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition((Cond $AE::ControlTypeProperty $CT::RadioButton), (Cond $AE::NameProperty $name))))
  if (-not $e) { throw "radio not found: $name" }; $e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function WaitEnabled($id, $sec) { Start-Sleep 2; for ($i = 0; $i -lt $sec; $i++) { if ((ById $id).Current.IsEnabled) { return }; Start-Sleep 1 }; throw "$id not enabled" }
function Expand($id, [bool]$open) { $p = (ById $id).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern); if ($open) { $p.Expand() } else { $p.Collapse() } }
# The visible vertical scroll area whose rectangle contains the given element.
function ScrollerOf($id) { $t = (ById $id).Current.BoundingRectangle
  (Win).FindAll($Scope::Descendants, (Cond $AE::IsScrollPatternAvailableProperty $true)) | ? { $_.Current.ControlType -eq $CT::Pane } |
    ? { $r = $_.Current.BoundingRectangle; $t.X -ge $r.X -and $t.X -lt ($r.X + $r.Width) } |
    ? { $_.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).Current.VerticallyScrollable } | Select -First 1 }
function ScrollTo($pane, [double]$pct) { $sp = $pane.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern); $sp.SetScrollPercent(-1, $pct); Start-Sleep -Milliseconds 700 }
function Shot($name, $h = $script:mainH) {
  $rc = New-Object GW+R; [GW]::GetWindowRect($h, [ref]$rc) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($rc.Rr - $rc.L), ($rc.B - $rc.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [GW]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $OutDir "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  # element rectangles: by AutomationId, and radio buttons / list items by name
  $map = [ordered]@{}
  foreach ($e in (Win $h).FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) {
    try { $c = $e.Current } catch { continue }
    if ($c.IsOffscreen) { continue }
    $r = $c.BoundingRectangle; if ($r.IsEmpty -or $r.Width -lt 2 -or $r.Height -lt 2) { continue }
    $key = if ($c.AutomationId -and $c.AutomationId -notmatch '^\d+$') { $c.AutomationId } elseif ($c.ControlType -eq $CT::RadioButton) { "radio:$($c.Name)" } else { $null }
    if (-not $key -or $map.Contains($key)) { continue }
    $map[$key] = @([int]($r.X - $rc.L), [int]($r.Y - $rc.T), [int]$r.Width, [int]$r.Height)
  }
  [IO.File]::WriteAllText((Join-Path $OutDir "$name.json"), ($map | ConvertTo-Json -Compress))
  "saved $name.png ($($map.Count) elements)" }
function CloseApp { $p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } } }

$env:MULTICAMAPP_TEST_VERIFY_FOLDER = (Resolve-Path $Folder).Path
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
try {
  [GW]::SetWindowPos($script:mainH, [IntPtr]::Zero, 40, 40, $Width, $Height, 0x0004) | Out-Null
  for ($i = 0; $i -lt 40; $i++) { try { $null = ById 'StartPreviewBtn'; break } catch { Start-Sleep 1 } }
  Start-Sleep 4
  Sel 'LanguageBox' 'English'; Start-Sleep 1

  if (-not $AboutOnly) {
  # ── Main page: set-up steps (preview off) ──
  Radio $Layout; Start-Sleep 1
  Sel 'Cam1Box' $Camera; Sel 'ResolutionBox' '720p'; Sel 'FpsBox' '30 fps'; Start-Sleep 2
  $left = ScrollerOf 'LayoutSectionTitle'
  ScrollTo $left 0;   Shot 'main_s0'
  ScrollTo $left 35;  Shot 'main_s35'
  ScrollTo $left 60;  Shot 'main_s60'
  ScrollTo $left 80;  Shot 'main_s80'
  ScrollTo $left 100; Shot 'main_s100'
  ScrollTo $left 0

  # ── Video Verification ──
  Click 'VerifyNavBtn'; Start-Sleep 5; Shot 'verify_ready'
  Click 'VerifyAllBtn'; WaitEnabled 'VerifyAllBtn' 300; Start-Sleep 2
  Click 'DeepVerifyBtn'; WaitEnabled 'DeepVerifyBtn' 600; Start-Sleep 2
  $page = ScrollerOf 'PageTitle'
  foreach ($p in 0, 25, 50, 75, 100) { ScrollTo $page $p; Shot "verify_s$p" }
  (ById 'DetailedViewRadio').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 2
  foreach ($p in 75, 100) { ScrollTo $page $p; Shot "verify_detailed_s$p" }
  (ById 'SimpleViewRadio').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 1; ScrollTo $page 0

  # ── Hardware Diagnostics ──
  Click 'HardwareNavBtn'; Start-Sleep 1; Click 'RunScanButton'; WaitEnabled 'RunScanButton' 180; Start-Sleep 1; Shot 'hardware'

  }
  # ── About window (modal, owned by the main window) ──
  Click 'MainNavBtn'; Start-Sleep 1
  Click 'AboutButton'   # returns immediately; the dialog opens modally
  $about = $null
  for ($i = 0; $i -lt 20 -and -not $about; $i++) { Start-Sleep 1
    $about = (Win).FindFirst($Scope::Children, (Cond $AE::ControlTypeProperty $CT::Window)) }
  if (-not $about) { throw 'about window not found' }
  Start-Sleep 2
  $ah = [IntPtr]$about.Current.NativeWindowHandle
  Shot 'about' $ah
  $about.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep 1
} finally { CloseApp }
