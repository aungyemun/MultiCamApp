# Captures every text on the Video Verification page at each stage (after auto-scan, after Verify All,
# after Deep Verify) so the displayed values can be compared with the files themselves.
# Usage: verify_texts.ps1 -Folder <sessions folder> [-Lang en|ja]. Writes <runs>\verify_texts_<lang>.json (UTF-8).
param([Parameter(Mandatory)][string]$Folder, [ValidateSet('en','ja')][string]$Lang = 'en')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]; $Scope = [Windows.Automation.TreeScope]
function Cond($p, $v) { New-Object Windows.Automation.PropertyCondition($p, $v) }
function Proc { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1 }
function Win { $AE::FromHandle($script:mainH) }
function ById($id) { $e = (Win).FindFirst($Scope::Descendants, (Cond $AE::AutomationIdProperty $id)); if (-not $e) { throw "not found: $id" }; $e }
function Click($id) { (ById $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Sel($box, $name) { $e = ById $box; $ec = $e.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern); $ec.Expand(); Start-Sleep -Milliseconds 300
  $hit = $e.FindAll($Scope::Descendants, (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::ListItem))) | ? { $_.Current.Name -eq $name } | Select -First 1
  $hit.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $ec.Collapse() }
function WaitEnabled($id, $sec) { Start-Sleep 2; for ($i = 0; $i -lt $sec; $i++) { if ((ById $id).Current.IsEnabled) { return $i }; Start-Sleep 1 }; throw "$id not enabled" }
# All named text on the page: cards (Card*Label/Card*Value pairs), notes, hints, session headers/summaries, table cells.
function Snapshot {
  $o = [ordered]@{ cards = [ordered]@{}; texts = @(); headers = @(); rows = @() }
  $all = (Win).FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) { $c = $e.Current
    if ($c.IsOffscreen -and $c.BoundingRectangle.IsEmpty) { }
    $id = $c.AutomationId; $n = $c.Name
    if ($id -like 'Card*Value') { $lab = $id -replace 'Value$', 'Label'; $o.cards[$id] = $n }
    elseif ($id -like 'Card*Label') { $o.cards[$id] = $n }
    elseif ($c.ControlType -eq [Windows.Automation.ControlType]::Group -or $c.ClassName -eq 'Expander') { if ($n) { $o.headers += $n } }
    elseif ($c.ControlType -eq [Windows.Automation.ControlType]::DataItem) { $cells = $e.FindAll($Scope::Children, [Windows.Automation.Condition]::TrueCondition) | % { $_.Current.Name }; $o.rows += ($cells -join ' | ') }
    elseif ($c.ControlType -eq [Windows.Automation.ControlType]::Text -and $n -and $n.Length -gt 2 -and -not $id.StartsWith('Card')) { $o.texts += ("[$id] " + $n) } }
  $o.texts = @($o.texts | Select -Unique); $o }
$env:MULTICAMAPP_TEST_VERIFY_FOLDER = (Resolve-Path $Folder).Path
Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
Start-Process $McExe | Out-Null
for ($i = 0; $i -lt 60 -and -not (Proc); $i++) { Start-Sleep 1 }
$script:mainH = (Proc).MainWindowHandle
for ($i = 0; $i -lt 40; $i++) { try { $null = ById 'StartPreviewBtn'; break } catch { Start-Sleep 1 } }
Start-Sleep 3
Sel 'LanguageBox' $(if ($Lang -eq 'ja') { -join [char[]](0x65E5, 0x672C, 0x8A9E) } else { 'English' }); Start-Sleep 2
$r = [ordered]@{ folder = Split-Path $Folder -Leaf; lang = $Lang }
Click 'VerifyNavBtn'; Start-Sleep 6; $r.afterScan = Snapshot
Click 'VerifyAllBtn'; $r.verifyAllSec = WaitEnabled 'VerifyAllBtn' 600; Start-Sleep 2; $r.afterVerifyAll = Snapshot
Click 'DeepVerifyBtn'; $r.deepSec = WaitEnabled 'DeepVerifyBtn' 900; Start-Sleep 2; $r.afterDeep = Snapshot
try { $r.log = @(((ById 'LogBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value) -split "`r?`n" | ? { $_ }) } catch {}
Click 'MainNavBtn'; Sel 'LanguageBox' 'English'; Start-Sleep 1
$p = Proc; if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(30000)) { $p.Kill() } }
$out = Join-Path $McRuns "verify_texts_$Lang.json"
[IO.File]::WriteAllText($out, ($r | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
"wrote $out"
