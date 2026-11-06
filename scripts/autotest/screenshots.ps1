# README screenshots: video preview OFF (no camera images), fixed window size, demo sessions only.
# Usage: screenshots.ps1 -Folder <folder with demo sessions> [-OutDir docs\images] [-VerifyOnly]
# -VerifyOnly retakes only the Video Verification screenshots (no cameras needed).
# Uses the app's test hook MULTICAMAPP_TEST_VERIFY_FOLDER so no file dialog is driven.
# Tip: map the folder to a drive letter first (subst V: <folder>) so the folder box shows a short path.
param([Parameter(Mandatory)][string]$Folder, [string]$OutDir = '', [int]$Width = 1600, [int]$Height = 1000, [switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
if (-not $OutDir) { $OutDir = Join-Path $McRoot 'docs\images' }
New-Item -ItemType Directory $OutDir -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SW { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[SW]::SetProcessDPIAware() | Out-Null
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]
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
function WaitEnabled($id, $sec) { Start-Sleep 2; for ($i = 0; $i -lt $sec; $i++) { if ((ById $id).Current.IsEnabled) { return }; Start-Sleep 1 }; throw "$id not enabled" }
function Shot($name, $h = $script:mainH) { $rc = New-Object SW+R; [SW]::GetWindowRect($h, [ref]$rc) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($rc.Rr - $rc.L), ($rc.B - $rc.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [SW]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $OutDir "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); "saved $name.png" }
function Expand($id, [bool]$open) { $p = (ById $id).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern); if ($open) { $p.Expand() } else { $p.Collapse() } }
function CloseApp { $p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } } }

$env:MULTICAMAPP_TEST_VERIFY_FOLDER = (Resolve-Path $Folder).Path
Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 -and $_.Path -eq (Resolve-Path $McExe).Path } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
try {
  [SW]::SetWindowPos($script:mainH, [IntPtr]::Zero, 40, 40, $Width, $Height, 0x0004) | Out-Null   # SWP_NOZORDER
  for ($i = 0; $i -lt 40; $i++) { try { $null = ById 'StartPreviewBtn'; break } catch { Start-Sleep 1 } }
  Start-Sleep 4
  $ja = -join [char[]](0x65E5, 0x672C, 0x8A9E)
  Sel 'LanguageBox' 'English'; Start-Sleep 1
  if (-not $VerifyOnly) {
    # Main page: 4-camera layout configured, preview off
    Radio '4 Cameras'; Start-Sleep 1
    Sel 'Cam1Box' 'Integrated Webcam (Built-in front)'; Sel 'Cam2Box' 'j5 Webcam JVU250 #1'; Sel 'Cam3Box' 'j5 Webcam JVU250 #2'; Sel 'Cam4Box' 'c922 Pro Stream Webcam'
    Sel 'ResolutionBox' '720p'; Sel 'FpsBox' '30 fps'; Start-Sleep 2
    Shot 'main_en'
    Expand 'AdvancedCameraControlsExpander' $true; Start-Sleep 1; Shot 'main_advanced_en'; Expand 'AdvancedCameraControlsExpander' $false
  }
  # Verification: before run, after Verify All, after Deep Verify
  Click 'VerifyNavBtn'; Start-Sleep 5; Shot 'verify_ready_en'
  Click 'VerifyAllBtn'; WaitEnabled 'VerifyAllBtn' 300; Start-Sleep 2
  Click 'DeepVerifyBtn'; WaitEnabled 'DeepVerifyBtn' 600; Start-Sleep 2; Shot 'verify_results_en'
  if (-not $VerifyOnly) {
    # Hardware diagnostics after a scan
    Click 'HardwareNavBtn'; Start-Sleep 1; Click 'RunScanButton'; WaitEnabled 'RunScanButton' 180; Start-Sleep 1; Shot 'hardware_en'
  }
  # Japanese UI
  Click 'MainNavBtn'; Sel 'LanguageBox' $ja; Start-Sleep 2
  if (-not $VerifyOnly) { Shot 'main_ja' }
  Click 'VerifyNavBtn'; Start-Sleep 2; Shot 'verify_results_ja'
  Click 'MainNavBtn'; Sel 'LanguageBox' 'English'; Start-Sleep 1
} finally { CloseApp }
