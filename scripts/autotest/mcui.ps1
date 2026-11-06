# MultiCamApp UI-automation driver (test harness; not shipped). Usage: mcui.ps1 <action> [args...]
param([Parameter(Mandatory)][string]$Action, [string]$A1, [string]$A2, [string]$A3)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # Japanese UI text must survive the pipe to the caller
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
function Win { $p = Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | Select -First 1; if (-not $p) { throw "MultiCamApp not running" }; $AE::FromHandle($p.MainWindowHandle) }
function ById($id) { $e = (Win).FindFirst($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id))); if (-not $e) { throw "element not found: $id" }; $e }
function Pat($e, $p) { $e.GetCurrentPattern($p::Pattern) }
function Items($box) { $e = ById $box; $ec = Pat $e ([Windows.Automation.ExpandCollapsePattern]); $ec.Expand(); Start-Sleep -Milliseconds 300
  $list = $e.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))); $ec.Collapse(); $list }
switch ($Action) {
  'launch' { $exe = $A1; Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { try { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } } catch {} }
             Start-Process $exe | Out-Null; for ($i=0; $i -lt 40; $i++) { Start-Sleep 1; try { $null = ById 'StartPreviewBtn'; break } catch {} }; 'launched' }
  'items'  { Items $A1 | % { "{0} | enabled={1}" -f $_.Current.Name, $_.Current.IsEnabled } }
  'select' { $e = ById $A1; $ec = Pat $e ([Windows.Automation.ExpandCollapsePattern]); $ec.Expand(); Start-Sleep -Milliseconds 300
             $all = $e.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)))
             $hit = $null; foreach ($it in $all) { if ($it.Current.Name -eq $A2) { $hit = $it; break } }
             if (-not $hit -and $A2 -match '^#(\d+)$') { $hit = $all[[int]$Matches[1]] }
             if (-not $hit) { $ec.Collapse(); throw "item '$A2' not in $A1" }
             (Pat $hit ([Windows.Automation.SelectionItemPattern])).Select(); $ec.Collapse(); "selected $A1=$($hit.Current.Name)" }
  'radio'  { $w = Win; $r = $w.FindFirst($Scope::Descendants, (New-Object Windows.Automation.AndCondition((New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::RadioButton)), (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $A1)))))
             if (-not $r) { throw "radio '$A1' not found" }; (Pat $r ([Windows.Automation.SelectionItemPattern])).Select(); "radio $A1" }
  'text'   { (Pat (ById $A1) ([Windows.Automation.ValuePattern])).SetValue($A2); "text $A1=$A2" }
  'click'  { $e = ById $A1; if (-not $e.Current.IsEnabled) { throw "$A1 is disabled" }; (Pat $e ([Windows.Automation.InvokePattern])).Invoke(); "clicked $A1" }
  'check'  { $tp = Pat (ById $A1) ([Windows.Automation.TogglePattern]); $want = if ($A2 -eq 'true') { 'On' } else { 'Off' }
             if ("$($tp.Current.ToggleState)" -ne $want) { $tp.Toggle() }; "check $A1=$($tp.Current.ToggleState)" }
  'expand' { (Pat (ById $A1) ([Windows.Automation.ExpandCollapsePattern])).Expand(); "expanded $A1" }
  'enabled'{ (ById $A1).Current.IsEnabled }
  'read'   { foreach ($id in $A1.Split(',')) { try { $e = ById $id } catch { "$id=<not visible>"; continue }; $v = $e.Current.Name
               try { $sel = (Pat $e ([Windows.Automation.SelectionPattern])).Current.GetSelection(); if ($sel.Count -gt 0) { $v = $sel[0].Current.Name } } catch { try { $v = (Pat $e ([Windows.Automation.ValuePattern])).Current.Value } catch {} }
               "$id=$v" } }
  'waitenabled' { $t = [int]$A2; for ($i=0; $i -lt $t; $i++) { if ((ById $A1).Current.IsEnabled) { "ready after ${i}s"; return }; Start-Sleep 1 }; throw "$A1 not enabled within ${t}s" }
  'close'  { Get-Process MultiCamApp -ErrorAction SilentlyContinue | ? { $_.MainWindowHandle -ne 0 } | % { $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(30000)) { $_.Kill(); 'killed' } else { "closed exit=$($_.ExitCode)" } } }
  default  { throw "unknown action $Action" }
}
