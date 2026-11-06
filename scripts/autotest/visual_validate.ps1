# Visual stability audit validation: synthetic videos with known answers + real recordings, app code
# (AuditCheck, uses the built MultiCamApp.dll) vs the separate Python implementation (visual_audit.py).
# Writes <runs>\visual_validate.json (UTF-8). Usage: visual_validate.ps1 [-Real <folder>...]
param([string[]]$Real = @())
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$ff = Join-Path $McFfDir 'ffmpeg.exe'
$synth = Join-Path $McRoot 'data\temp\autotest\synth_visual'
if (Test-Path $synth) { Remove-Item $synth -Recurse -Force }
& $McPy (Join-Path $PSScriptRoot 'synth_visual.py') $ff $synth | Out-Null
$bin = Join-Path $McRoot 'data\temp\autotest\auditcheck_bin'
dotnet build (Join-Path $PSScriptRoot 'AuditCheck\AuditCheck.csproj') -c Release -nologo -v q -o $bin | Out-Null
$env:MCA_FFMPEG_DIR = $McFfDir
function Run($mp4) {
  $a = & (Join-Path $bin 'AuditCheck.exe') $ff $mp4 30 | ConvertFrom-Json
  $p = (& $McPy (Join-Path $PSScriptRoot 'visual_audit.py') $mp4 --seconds 30 2>$null) -join ' ' | ConvertFrom-Json
  [ordered]@{ app = $a; py = [ordered]@{ bandMedianPct = $p.rollingBands.bandStrengthMedianPct; bandPeakPct = $p.rollingBands.amplitudePct
      k = $p.rollingBands.bandsPerFrame; hz = $p.rollingBands.temporalHz; dir = $p.rollingBands.direction
      flashes = $p.flash.framesWithJump4pct; ydif = $p.temporalNoiseYdifMedian } } }
$r = [ordered]@{ truth = Get-Content (Join-Path $synth 'truth.json') -Raw | ConvertFrom-Json; synth = [ordered]@{}; real = [ordered]@{} }
foreach ($d in Get-ChildItem $synth -Directory) { $r.synth[$d.Name] = Run (Join-Path $d.FullName 'cam1\cam1.mp4') }
foreach ($root in $Real) { foreach ($m in Get-ChildItem $root -Recurse -Filter 'cam*.mp4' | ? { $_.Name -notlike '*.tmp.mp4' }) {
  $r.real["$($m.Directory.Parent.Name)/$($m.Name)"] = Run $m.FullName } }
$out = Join-Path $McRuns 'visual_validate.json'
[IO.File]::WriteAllText($out, ($r | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
"wrote $out"
