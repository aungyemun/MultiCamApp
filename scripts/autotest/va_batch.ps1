# Runs visual_audit.py over session/camera pairs (each with a hard timeout). Usage: va_batch.ps1 -Items 'prefix:cam1','prefix2:cam2' [-Seconds 30]
param([string]$Items, [int]$Seconds = 30, [string]$Videos = '')
$list = @($Items -split '[,\s]+' | ? { $_ })   # -File may turn a comma list into a space-joined string
$d = $PSScriptRoot; . "$d\lib.ps1"; $py = $McPy; if (-not $Videos) { $Videos = $McVideos }
foreach ($it in $list) {
  $pre, $cam = $it.Split(':')
  $s = Get-ChildItem $Videos -Directory -Filter "${pre}*" | Sort LastWriteTime -Desc | Select -First 1
  if (-not $s) { "$it : session not found"; continue }
  $r = Invoke-Timed $py @('-I', "$d\visual_audit.py", "$($s.FullName)\$cam\$cam.mp4", '--seconds', $Seconds) 900
  if ($r.TimedOut) { "$it : TIMEOUT"; continue }
  try { $j = $r.Out | ConvertFrom-Json } catch { "$it : bad output $($r.Err.Trim())"; continue }
  if ($j.error) { "$it : $($j.error)"; continue }
  $b = $j.rollingBands; $f = $j.flash
  "{0,-40} n={1,5} {2,5:F1}s | bands {3,6:F3}% k={4} {5,5:F2}Hz {6,-4} p/m={7,6} strength={14,6:F3}% | jumps={8} flicker={9:F3}%@{10}Hz range={11}% | noise={12} block={13}" -f $it, $j.framesAnalysed, $j.seconds, $b.amplitudePct, $b.bandsPerFrame, $b.temporalHz, $b.direction, $b.peakToMedian, $f.framesWithJump4pct, $f.lumaFlickerPct, $f.lumaFlickerHz, $f.lumaRangePct, $j.temporalNoiseYdifMedian, $j.blockinessMedian, $b.bandStrengthMedianPct
}
