# Functional test of the Video Verification and Hardware Diagnostics pages (no recording).
# Uses a small copy of known sessions so every result can be checked. All UI Automation runs
# in-process; wrap the call in Invoke-Timed. Writes <runs>\pages_func.json and pf_*.png.
param([string[]]$Sessions = @('v210b_en_*', 'v210b_ja_*', 'cx03_regress_kill_*'), [int]$DeepVerifyTimeoutSec = 600)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PW { [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[PW]::SetProcessDPIAware() | Out-Null
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]
$r = [ordered]@{ errors = @() }
$script:tStart = Get-Date
function Cond($p, $v) { New-Object Windows.Automation.PropertyCondition($p, $v) }
function Proc { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1 }
function Win { $AE::FromHandle($script:mainH) }
function ById($id, $root = $null) { if (-not $root) { $root = Win }; $e = $root.FindFirst($Scope::Descendants, (Cond $AE::AutomationIdProperty $id)); if (-not $e) { throw "not found: $id" }; $e }
function Click($id) { (ById $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function SetText($id, $t) { (ById $id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($t) }
function GetText($id) { $e = ById $id; try { $e.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $e.Current.Name } }
function Read($ids) { $o = [ordered]@{}; foreach ($i in $ids) { try { $o[$i] = GetText $i } catch { $o[$i] = '<n/a>' } }; $o }
function Enabled($id) { (ById $id).Current.IsEnabled }
function WaitEnabled($id, $sec) { $sw = [Diagnostics.Stopwatch]::StartNew(); Start-Sleep 2
  while ($sw.Elapsed.TotalSeconds -lt $sec) { if (Enabled $id) { return [int]$sw.Elapsed.TotalSeconds }; Start-Sleep 1 }; throw "$id not enabled within $sec s" }
function Shot($tag) { $rc = New-Object PW+R; [PW]::GetWindowRect($script:mainH, [ref]$rc) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($rc.Rr - $rc.L), ($rc.B - $rc.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [PW]::PrintWindow($script:mainH, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $McRuns "pf_$tag.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }
function Step($name, [scriptblock]$b) { try { & $b } catch { $script:r.errors += "${name}: $_" } }
function Dialog { $wins = [Windows.Automation.AutomationElement]::RootElement.FindAll($Scope::Children, (Cond $AE::ProcessIdProperty (Proc).Id))
  $d = $wins | ? { $_.Current.NativeWindowHandle -ne $script:mainH.ToInt32() -and $_.Current.ClassName -eq '#32770' } | Select -First 1
  # The file/folder dialog is usually exposed as a child of the owner window, not of the desktop.
  if (-not $d) { $d = (Win).FindFirst($Scope::Children, (Cond $AE::ClassNameProperty '#32770')) }
  $d }
# File/folder name box of a standard Windows file dialog: the editable field next to the File name / Folder label.

# --- test data: copies of known sessions (originals untouched) ---
$set = Join-Path $McRoot 'data\temp\autotest\verify_set'
if (Test-Path $set) { Remove-Item $set -Recurse -Force }   # previous copy made by this script
New-Item -ItemType Directory $set | Out-Null
foreach ($pat in $Sessions) { $s = Get-ChildItem $McVideos -Directory -Filter $pat | Sort LastWriteTime -Desc | Select -First 1
  if ($s) { Copy-Item $s.FullName (Join-Path $set $s.Name) -Recurse } else { $r.errors += "session $pat not found" } }
$r.sessions = @(Get-ChildItem $set -Directory | % Name)

Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
$env:MULTICAMAPP_TEST_VERIFY_FOLDER = $set   # inherited by the app (test hook)
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
for ($i = 0; $i -lt 40; $i++) { try { $null = ById 'StartPreviewBtn'; break } catch { Start-Sleep 1 } }
Start-Sleep 2

# --- Video Verification ---
Step 'nav verify' { Click 'VerifyNavBtn'; Start-Sleep 2 }
# FolderBox is read-only by design (chosen with Browse). The app's test hook MULTICAMAPP_TEST_VERIFY_FOLDER
# preselects the test folder instead; no keystrokes are sent to dialogs.
Step 'check folder' { Start-Sleep 6; $script:r.folderBox = GetText 'FolderBox'; if ($script:r.folderBox -ne $set) { throw "folder is $($script:r.folderBox)" }
  $script:r.afterScan = Read @('CardFoundValue','CardOverallValue') }
Step 'browse opens picker' { Click 'BrowseFolderBtn'; Start-Sleep 3; $d = Dialog; if (-not $d) { throw 'folder dialog not found' }
  $script:r.folderDialogTitle = $d.Current.Name; $d.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep 1 }
Step 'verify all' { Click 'VerifyAllBtn'; $script:r.verifyAllSec = WaitEnabled 'VerifyAllBtn' 300
  $script:r.afterVerify = Read @('CardOverallValue','CardFoundValue','CardPassValue','CardWarnValue','CardFailValue','CardTimingConfidenceValue','CardTimingModeValue','CardOriginalFramesValue','CardDuplicateFramesValue','CardTimestampCsvValue','CardQueueDropsValue','CardEnvLockValue','CardFpsSpreadValue','CardDurSpreadValue')
  Shot 'verify_done' }
Step 'deep verify' { if ($script:r.folderBox -ne $set) { throw 'skipped: test folder was not selected' }; Click 'DeepVerifyBtn'; $script:r.deepVerifySec = WaitEnabled 'DeepVerifyBtn' $DeepVerifyTimeoutSec
  $script:r.afterDeep = Read @('CardDeepVerifyStatusValue','CardDeepVerifyFilesValue','CardDeepVerifyDuplicatesValue','CardDeepVerifyWorstFileValue','CardVisualBandsValue','CardVisualFlashValue','CardVisualNoiseValue')
  Shot 'deep_done' }
Step 'detailed view' { (ById 'DetailedViewRadio').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 2; Shot 'detailed'
  (ById 'SimpleViewRadio').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep 1 }
Step 'search' { SetText 'SearchBox' 'v210b_ja'; Start-Sleep 2; Shot 'search'; SetText 'SearchBox' ''; Start-Sleep 1 }
Step 'export all' { Click 'ExportAllBtn'; Start-Sleep 4
  $script:r.exportAllFiles = @(Get-ChildItem $set -File | ? { $_.LastWriteTime -gt (Get-Date).AddMinutes(-2) } | % { "$($_.Name) $($_.Length)" }) }
$txt = Join-Path $McRuns 'pf_export.txt'; if (Test-Path $txt) { Remove-Item $txt -Force }
Step 'export txt opens save dialog' { Click 'ExportTxtBtn'; Start-Sleep 3; $d = Dialog; if (-not $d) { throw 'save dialog not found' }
  $script:r.saveDialogTitle = $d.Current.Name; $d.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep 1 }
Step 'log' { $script:r.logTail = @((GetText 'LogBox') -split "`r?`n" | ? { $_ } | Select -Last 12) }
Step 'clear' { Click 'ClearBtn'; Start-Sleep 2; $script:r.afterClear = Read @('CardFoundValue','CardOverallValue') }

# --- Hardware Diagnostics ---
Step 'nav hardware' { Click 'HardwareNavBtn'; Start-Sleep 2 }
Step 'hw scan' { Click 'RunScanButton'; $script:r.hwScanSec = WaitEnabled 'RunScanButton' 180
  $script:r.hwAfterScan = Read @('StatusText','LastScanText','RecommendedPresetText','SystemCardText','CameraDevicesCardText','UsbStatusCardText') }
Step 'hw reports' { foreach ($x in 'SystemReportExpander','CameraReportExpander','UsbReportExpander') { (ById $x).GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }; Start-Sleep 1
  $script:r.hwReportLengths = Read @('SystemSummaryBox','CameraSummaryBox','UsbSummaryBox') | % { $o = [ordered]@{}; foreach ($k in $_.Keys) { $o[$k] = "$($_[$k])".Length }; $o }
  $script:r.hwSystemReportHead = @((GetText 'SystemSummaryBox') -split "`r?`n" | Select -First 8)
  Shot 'hw_reports' }
Step 'hw copy' { Click 'CopySummaryButton'; Start-Sleep 1; $script:r.hwCopy = GetText 'StatusText'
  Add-Type -AssemblyName System.Windows.Forms; $script:r.clipboardChars = ([Windows.Forms.Clipboard]::GetText()).Length }

# UI freezes logged by the app's watchdog during this test
$logs = Join-Path (Split-Path $McExe -Parent) 'logs'
$al = Get-ChildItem $logs -Filter 'app_runtime_*.txt' -ErrorAction SilentlyContinue | Sort LastWriteTime -Desc | Select -First 1
if ($al) { $r.freezeOverWarning = @(Select-String $al.FullName -Pattern 'UI_FREEZE_CRITICAL|UI_HEARTBEAT_LATENCY_HIGH' | ? {
  try { [datetime]::ParseExact($_.Line.Substring(0, 19), 'yyyy-MM-dd HH:mm:ss', $null) -ge $script:tStart } catch { $false } } | % { $_.Line.Substring(11) }) }
$p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } }
$r | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $McRuns 'pages_func.json') -Encoding UTF8
$r | ConvertTo-Json -Depth 5
