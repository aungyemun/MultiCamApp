# Release-readiness integration pass. Each step runs time-limited; results go to <runs>\final_checks.json.
# Recordings are written by the app to its normal output folder (the user's Videos folder).
param([string]$Cam1 = 'c922 Pro Stream Webcam', [string]$Cam2 = 'Integrated Webcam (Built-in front)', [int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$ps = @('-NoProfile','-ExecutionPolicy','Bypass','-File')
function Step($script, [string[]]$a, $t) { $r = Invoke-Timed powershell.exe ($ps + @((Join-Path $PSScriptRoot $script)) + $a) $t; [ordered]@{ exit = $r.ExitCode; out = $r.Out; err = $r.Err } }
$R = [ordered]@{ started = (Get-Date).ToString('s') }
if (Get-Process MultiCamApp -ErrorAction SilentlyContinue) { $R.note = 'MultiCamApp was running before the test and was closed by the harness' }

# 1. build + unit tests
. (Join-Path $McRoot 'env\activate.ps1') | Out-Null
$sln = Join-Path $McRoot 'source\MultiCamApp\MultiCamApp.sln'
$b = Invoke-Timed dotnet @('build', $sln, '-c', 'Release', '-p:Platform=x64', '-nologo', '-v', 'q') 900
$R.build = @(($b.Out -split "`r?`n") | ? { $_ -match 'Warning\(s\)|Error\(s\)' })
$trx = Join-Path $McRuns 'unit_tests.trx'
$t = Invoke-Timed dotnet @('test', $sln, '-c', 'Release', '-p:Platform=x64', '--no-build', '-nologo', '--logger', "trx;LogFileName=$trx") 900
$R.unitTests = @(($t.Out -split "`r?`n") | ? { $_ -match 'Passed!|Failed!|Total tests' })

# 2. real recordings, EN and JA UI, then independent analysis (frames, CSV, gaps, duplicates)
$R.recordings = [ordered]@{}
foreach ($lang in 'en', 'ja') {
  $name = "rc210_$lang"
  $s = Step 'rec_lang.ps1' @('-Name', $name, '-Lang', $lang, '-Cam1', $Cam1, '-Cam2', $Cam2, '-Seconds', "$Seconds") 600
  $rec = try { $s.out | ConvertFrom-Json } catch { $null }
  $sess = Get-ChildItem $McVideos -Directory -Filter "${name}_*" | Sort LastWriteTime -Desc | Select -First 1
  $an = $null
  if ($sess) { $env:MCA_FFMPEG_DIR = $McFfDir; $a = Invoke-Timed $McPy @((Join-Path $PSScriptRoot 'analyze.py'), $sess.FullName, '1280', '720', '30') 600
    $j = Join-Path $sess.FullName '_autotest_analysis.json'; if (Test-Path $j) { $an = Get-Content $j -Raw | ConvertFrom-Json; Remove-Item $j -Force } }
  $R.recordings[$lang] = [ordered]@{ ok = $rec.ok; error = $rec.error; session = $sess.Name; analysis = $an }
}

# 3. Verify / Hardware page functions incl. exports, then parse the exported files
$pf = Step 'pages_func.ps1' @('-Sessions', 'rc210_en_*', 'rc210_ja_*', 'cx03_regress_kill_*') 1200
$R.pagesFunc = try { Get-Content (Join-Path $McRuns 'pages_func.json') -Raw | ConvertFrom-Json } catch { "unreadable: $($pf.err)" }
$set = Join-Path $McRoot 'data\temp\autotest\verify_set'
$ex = [ordered]@{}
try { $js = Get-Content (Join-Path $set 'verification_report.json') -Raw -Encoding UTF8 | ConvertFrom-Json; $ex.jsonParses = $true; $ex.jsonRows = @($js.TableRows).Count; $ex.jsonSummary = $js.Summary | Select VideosFound, VideosPassed, VideosWarning, VideosFailed, OverallVerdict } catch { $ex.jsonParses = "NO: $_" }
try { $csv = Import-Csv (Join-Path $set 'verification_report.csv') -Encoding UTF8; $ex.csvRows = @($csv).Count; $ex.csvColumns = @($csv[0].PSObject.Properties.Name).Count } catch { $ex.csvRows = "NO: $_" }
$ex.txtBytes = (Get-Item (Join-Path $set 'verification_report.txt') -ErrorAction SilentlyContinue).Length
$ex.auditBytes = (Get-Item (Join-Path $set 'video_audit_report.txt') -ErrorAction SilentlyContinue).Length
$R.exports = $ex

# 4. Verify-page texts at each stage, EN and JA
$demo = Join-Path $McRoot 'data\temp\autotest\demo_set'
foreach ($lang in 'en', 'ja') { $v = Step 'verify_texts.ps1' @('-Folder', $demo, '-Lang', $lang) 900; $R["verifyTexts_$lang"] = $v.exit }
Remove-Item (Join-Path $demo '*.txt'), (Join-Path $demo '*.json'), (Join-Path $demo '*.csv') -ErrorAction SilentlyContinue

# 5. visual audit validation (synthetic + demo recordings)
$vv = Step 'visual_validate.ps1' @('-Real', $demo) 1200; $R.visualValidate = $vv.exit

# 6. large-folder UI freeze
$big = Join-Path $McRoot 'data\temp\autotest\big_set'
if (Test-Path $big) { Remove-Item (Join-Path $big '*.txt'), (Join-Path $big '*.json'), (Join-Path $big '*.csv') -ErrorAction SilentlyContinue
  $bv = Step 'big_verify.ps1' @('-Folder', $big) 1200; $R.bigVerify = try { $bv.out | ConvertFrom-Json } catch { $bv.err } }

$R.finished = (Get-Date).ToString('s')
$out = Join-Path $McRuns 'final_checks.json'
[IO.File]::WriteAllText($out, ($R | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
"wrote $out"
